using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace BombTavern.Net
{
    /// <summary>局域网房间发现：主机每秒广播一次，主界面自动收集同网段房间。</summary>
    [DisallowMultipleComponent]
    public sealed class LanRoomDiscovery : MonoBehaviour
    {
        const int DiscoveryPort = 7787;
        const string Prefix = "WHO_WILL_BOOM_ROOM|";

        public struct RoomInfo
        {
            public string address, name;
            public int port, players;
            public float lastSeen;
        }

        public readonly List<RoomInfo> Rooms = new List<RoomInfo>();
        readonly ConcurrentQueue<RoomInfo> _incoming = new ConcurrentQueue<RoomInfo>();
        UdpClient _receiver, _sender;
        Thread _thread;
        volatile bool _running;
        float _nextBroadcast;
        LanNetworkManager _lan;

        void Start()
        {
            _lan = FindObjectOfType<LanNetworkManager>();
            try
            {
                _receiver = new UdpClient();
                _receiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _receiver.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _sender = new UdpClient { EnableBroadcast = true };
                _running = true;
                _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "BombTavern-RoomDiscovery" };
                _thread.Start();
            }
            catch (Exception e) { Debug.LogWarning("[房间扫描] 启动失败：" + e.Message); }
        }

        void Update()
        {
            while (_incoming.TryDequeue(out var room))
            {
                room.lastSeen = Time.unscaledTime;
                int index = Rooms.FindIndex(r => r.address == room.address && r.port == room.port);
                if (index >= 0) Rooms[index] = room;
                else
                {
                    Rooms.Add(room);
                    Debug.Log($"[房间扫描] 发现 {room.name}：{room.address}:{room.port}（{room.players} 人）");
                }
            }
            Rooms.RemoveAll(r => Time.unscaledTime - r.lastSeen > 4.5f);

            if (_lan == null) _lan = FindObjectOfType<LanNetworkManager>();
            if (_lan == null || !_lan.enabled || !_lan.IsHost || !_lan.VoiceRelayAvailable) return;
            if (Time.unscaledTime < _nextBroadcast) return;
            _nextBroadcast = Time.unscaledTime + 1f;
            string hostName = string.IsNullOrWhiteSpace(_lan.localPlayerName) ? "房主" : _lan.localPlayerName.Replace('|', ' ');
            string message = Prefix + _lan.port + "|" + _lan.connectedClients + "|" + hostName;
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            try { _sender?.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort)); }
            catch { }
        }

        void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] bytes = _receiver.Receive(ref remote);
                    string text = Encoding.UTF8.GetString(bytes);
                    if (!text.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                    string[] p = text.Substring(Prefix.Length).Split('|');
                    if (p.Length < 3 || !int.TryParse(p[0], out int port) || !int.TryParse(p[1], out int players)) continue;
                    _incoming.Enqueue(new RoomInfo
                    {
                        address = remote.Address.ToString(), port = port, players = players, name = p[2]
                    });
                }
                catch { if (_running) Thread.Sleep(50); }
            }
        }

        void OnDestroy()
        {
            _running = false;
            try { _receiver?.Close(); } catch { }
            try { _sender?.Close(); } catch { }
        }
    }
}
