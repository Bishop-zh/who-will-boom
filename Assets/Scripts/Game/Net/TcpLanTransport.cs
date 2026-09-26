using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace BombTavern.Net
{
    /// <summary>Transport 抽象：便于后续替换为内网穿透 / 中继实现。</summary>
    public interface ITransport : IDisposable
    {
        bool IsRunning { get; }
        bool IsHost { get; }

        /// <summary>收到一行消息（peerId, line）。仅在主线程派发。</summary>
        event Action<int, string> MessageReceived;
        event Action<int> PeerConnected;
        event Action<int> PeerDisconnected;

        void Stop();
        void Send(int peerId, string line);
        void Broadcast(string line);
    }

    /// <summary>
    /// 局域网 TCP 传输。
    /// 线程模型：socket 读写均放在后台线程；发送保持单队列顺序，收到的行先入队，
    /// 由 Pump()（主线程调用）统一派发事件，避免网络卡顿阻塞画面或跨线程触碰 Unity API。
    /// </summary>
    public class TcpLanTransport : ITransport
    {
        public const int HostPeerId = 0;

        public bool IsHost { get; private set; }
        public bool IsRunning { get; private set; }
        public int Port { get; private set; }
        public string ConnectAddress { get; private set; }

        public event Action<int, string> MessageReceived;
        public event Action<int> PeerConnected;
        public event Action<int> PeerDisconnected;

        readonly ConcurrentQueue<NetEnvelope> _inbox = new ConcurrentQueue<NetEnvelope>();
        readonly ConcurrentQueue<OutgoingEnvelope> _outbox = new ConcurrentQueue<OutgoingEnvelope>();
        readonly Dictionary<int, TcpClient> _peers = new Dictionary<int, TcpClient>();
        readonly Dictionary<int, StreamWriter> _writers = new Dictionary<int, StreamWriter>();
        readonly object _peerLock = new object();

        TcpListener _listener;
        TcpClient _client;
        Thread _acceptThread;
        Thread _sendThread;
        readonly AutoResetEvent _sendSignal = new AutoResetEvent(false);
        volatile bool _running;
        int _nextPeerId = 1;

        /// <summary>客户端连接的阶段。UI 靠它区分「正在连 / 连上了 / 失败了」。</summary>
        public enum LinkState { Idle, Connecting, Connected, Failed }

        public LinkState State { get; private set; } = LinkState.Idle;

        /// <summary>连接失败的人话原因（成功或未开始为空）。</summary>
        public string LastError { get; private set; }

        /// <summary>
        /// 连接超时（毫秒）。TCP 默认超时约 20 秒，太久了 ——
        /// 内网穿透最常见的失败（隧道没开、端口填错）不该让人干等 20 秒。
        /// </summary>
        public int connectTimeoutMs = 6000;

        /// <summary>连接失败事件（主线程派发）。参数为可读原因。</summary>
        public event Action<string> ConnectFailed;

        struct NetEnvelope
        {
            public int peerId;
            public string line;
            public PeerSignal signal;
            public string note;          // Failed 时携带失败原因
        }

        struct OutgoingEnvelope
        {
            public int peerId;
            public string line;
            public bool broadcast;
        }

        enum PeerSignal { None, Connected, Disconnected, Failed }

        public void StartHost(int port)
        {
            IsHost = true;
            Port = port;
            StartHostInternal();
        }

        public void StartClient(string address, int port)
        {
            IsHost = false;
            Port = port;
            ConnectAddress = address;
            StartClientInternal();
        }

        void StartHostInternal()
        {
            try
            {
                // 绑 IPAddress.Any 是刻意的：这样会同时监听所有网卡 ——
                // 物理局域网、Radmin VPN 的 26.x 虚拟网卡、樱花内网穿透的本机回环，
                // 全都能进来，不需要为每种穿透方案改监听地址。
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();
                _running = true;
                IsRunning = true;
                State = LinkState.Connected;
                LastError = null;

                _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "BombTavern-Accept" };
                _acceptThread.Start();
                StartSendWorker();

                Debug.Log($"[联机] 主机已在端口 {Port} 上监听（所有网卡）。");
            }
            catch (SocketException se)
            {
                IsRunning = false;
                State = LinkState.Failed;
                LastError = se.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? $"端口 {Port} 已被占用。请换个端口，或关掉之前残留的本游戏进程。"
                    : $"开房失败（{se.SocketErrorCode}）：{se.Message}";

                Debug.LogError("[联机] " + LastError);
            }
            catch (Exception e)
            {
                IsRunning = false;
                State = LinkState.Failed;
                LastError = $"开房失败：{e.Message}";
                Debug.LogError("[联机] " + LastError);
            }
        }

        /// <summary>
        /// 连主机。**必须在后台线程做**：
        /// TcpClient.Connect 是阻塞调用，目标不可达时会卡住十几秒，
        /// 放在主线程会让整个游戏直接假死。这里用 BeginConnect + 超时，
        /// 失败时把「人话原因」回传给界面，而不是只丢一句英文异常。
        /// </summary>
        void StartClientInternal()
        {
            State = LinkState.Connecting;
            LastError = null;

            var thread = new Thread(ConnectWorker) { IsBackground = true, Name = "BombTavern-Connect" };
            thread.Start();
        }

        void ConnectWorker()
        {
            TcpClient client = null;
            try
            {
                client = new TcpClient { NoDelay = true };

                // BeginConnect 会异步完成 DNS 解析，所以这里既能填 IPv4，
                // 也能填内网穿透给的域名（例如 frp 的 node.xxx.com）。
                IAsyncResult ar = client.BeginConnect(ConnectAddress, Port, null, null);

                if (!ar.AsyncWaitHandle.WaitOne(connectTimeoutMs))
                {
                    SafeClose(client);
                    Fail($"连接超时：{ConnectAddress}:{Port} 没有响应。\n" +
                         "请确认房主已开房、地址与端口填对了、防火墙放行了该端口。");
                    return;
                }

                client.EndConnect(ar);      // 被拒绝 / 域名解析失败都会在这里抛出
                _client = client;

                _running = true;
                IsRunning = true;
                State = LinkState.Connected;

                lock (_peerLock) _peers[HostPeerId] = client;
                StartSendWorker();

                var t = new Thread(() => ReadLoop(HostPeerId, client))
                    { IsBackground = true, Name = "BombTavern-Read" };
                t.Start();

                Enqueue(HostPeerId, null, PeerSignal.Connected);
                Debug.Log($"[联机] 已连接到主机 {ConnectAddress}:{Port}。");
            }
            catch (SocketException se)
            {
                SafeClose(client);
                Fail(Describe(se));
            }
            catch (Exception e)
            {
                SafeClose(client);
                Fail($"连接失败：{e.Message}");
            }
        }

        /// <summary>把 SocketException 翻译成玩家看得懂的话 —— 这些错误码在内网穿透场景下最常见。</summary>
        string Describe(SocketException se)
        {
            switch (se.SocketErrorCode)
            {
                case SocketError.HostNotFound:
                    return $"找不到主机「{ConnectAddress}」。\n" +
                           "地址可能有拼写错误，或 DNS 解析不到（内网穿透的域名请确认隧道已启动）。";
                case SocketError.ConnectionRefused:
                    return $"{ConnectAddress}:{Port} 拒绝连接。\n" +
                           "端口上没有服务在监听：房主没开房，或内网穿透隧道未启动 / 映射端口填错。";
                case SocketError.TimedOut:
                case SocketError.HostUnreachable:
                case SocketError.NetworkUnreachable:
                    return $"无法到达 {ConnectAddress}:{Port}。\n" +
                           "检查双方网络、VPN 是否都连上同一个虚拟网络。";
                default:
                    return $"连接失败（{se.SocketErrorCode}）：{se.Message}";
            }
        }

        void Fail(string reason)
        {
            IsRunning = false;
            State = LinkState.Failed;
            LastError = reason;

            Debug.LogError("[联机] " + reason);
            Enqueue(HostPeerId, null, PeerSignal.Failed);   // 让 Pump 在主线程通知 UI
        }

        static void SafeClose(TcpClient c)
        {
            if (c == null) return;
            try { c.Close(); } catch (Exception) { /* 已经关了 */ }
        }

        void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    client.NoDelay = true;
                    int id;
                    lock (_peerLock)
                    {
                        id = _nextPeerId++;
                        _peers[id] = client;
                    }
                    var t = new Thread(() => ReadLoop(id, client)) { IsBackground = true, Name = $"BombTavern-Read-{id}" };
                    t.Start();
                    Enqueue(id, null, PeerSignal.Connected);
                }
                catch (Exception)
                {
                    if (_running) Thread.Sleep(120);
                }
            }
        }

        void ReadLoop(int peerId, TcpClient client)
        {
            try
            {
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    while (_running && client.Connected)
                    {
                        string line = reader.ReadLine();
                        if (line == null) break;
                        Enqueue(peerId, line, PeerSignal.None);
                    }
                }
            }
            catch (Exception)
            {
                // 断开属正常路径，不打印噪声日志
            }
            finally
            {
                Enqueue(peerId, null, PeerSignal.Disconnected);
            }
        }

        void Enqueue(int peerId, string line, PeerSignal signal)
        {
            _inbox.Enqueue(new NetEnvelope { peerId = peerId, line = line, signal = signal });
        }

        /// <summary>主线程调用：把后台线程收到的消息派发出去。</summary>
        public void Pump()
        {
            while (_inbox.TryDequeue(out var env))
            {
                switch (env.signal)
                {
                    case PeerSignal.Failed:
                        ConnectFailed?.Invoke(LastError);
                        break;
                    case PeerSignal.Connected:
                        PeerConnected?.Invoke(env.peerId);
                        break;
                    case PeerSignal.Disconnected:
                        lock (_peerLock)
                        {
                            _peers.Remove(env.peerId);
                            _writers.Remove(env.peerId);
                        }
                        PeerDisconnected?.Invoke(env.peerId);
                        break;
                    default:
                        MessageReceived?.Invoke(env.peerId, env.line);
                        break;
                }
            }
        }

        public void Send(int peerId, string line)
        {
            if (!_running || string.IsNullOrEmpty(line)) return;
            _outbox.Enqueue(new OutgoingEnvelope { peerId = peerId, line = line });
            _sendSignal.Set();
        }

        public void Broadcast(string line)
        {
            if (!_running || string.IsNullOrEmpty(line)) return;
            _outbox.Enqueue(new OutgoingEnvelope { line = line, broadcast = true });
            _sendSignal.Set();
        }

        void StartSendWorker()
        {
            if (_sendThread != null && _sendThread.IsAlive) return;
            _sendThread = new Thread(SendLoop)
            {
                IsBackground = true,
                Name = "BombTavern-Send"
            };
            _sendThread.Start();
        }

        void SendLoop()
        {
            while (_running)
            {
                if (!_outbox.TryDequeue(out var env))
                {
                    _sendSignal.WaitOne(100);
                    continue;
                }

                if (env.broadcast) WriteBroadcast(env.line);
                else WriteOne(env.peerId, env.line);
            }
        }

        void WriteOne(int peerId, string line)
        {
            lock (_peerLock)
            {
                if (!_peers.TryGetValue(peerId, out var c) || c == null) return;
                var writer = GetWriter(peerId, c);
                SafeWrite(writer, line);
            }
        }

        void WriteBroadcast(string line)
        {
            lock (_peerLock)
            {
                foreach (var kv in _peers)
                {
                    if (kv.Key == HostPeerId) continue;
                    if (kv.Value == null) continue;
                    var writer = GetWriter(kv.Key, kv.Value);
                    SafeWrite(writer, line);
                }
            }
        }

        StreamWriter GetWriter(int peerId, TcpClient client)
        {
            if (_writers.TryGetValue(peerId, out var w) && w != null) return w;
            var stream = client.GetStream();
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            _writers[peerId] = writer;
            return writer;
        }

        static void SafeWrite(StreamWriter writer, string line)
        {
            if (writer == null) return;
            try { writer.WriteLine(line); }
            catch (Exception) { /* 对端已断开 */ }
        }

        public void PumpPing() { }

        public void Stop()
        {
            _running = false;
            _sendSignal.Set();
            IsRunning = false;
            State = LinkState.Idle;

            lock (_peerLock)
            {
                foreach (var kv in _writers)
                {
                    try { kv.Value?.Flush(); kv.Value?.Dispose(); } catch { }
                }
                _writers.Clear();

                foreach (var kv in _peers)
                {
                    try { kv.Value?.Close(); } catch { }
                }
                _peers.Clear();
            }

            try { _listener?.Stop(); } catch { }
            _listener = null;

            try { _client?.Close(); } catch { }
            _client = null;
        }

        public void Dispose() => Stop();
    }
}
