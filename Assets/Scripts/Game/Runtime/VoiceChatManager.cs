using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using BombTavern.Net;
using LiarsBar;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>语音传输通道。</summary>
    public enum VoiceChannelMode
    {
        [Tooltip("联机中走 TCP 中继（公网/内网穿透可用），单机或无法联机时退回局域网 UDP 广播。")]
        Auto,
        [Tooltip("只在局域网内用 UDP 广播，延迟最低，但公网穿透下通常收不到。")]
        LanUdp,
        [Tooltip("经主机 TCP 中继。与游戏同一条连接，穿透工具只需转发一个 TCP 端口。")]
        TcpRelay,
    }

    /// <summary>
    /// 局域网语音聊天。
    /// 麦克风 → 16kHz 单声道 16 位 PCM → UDP 广播 → 对端 AudioSource 回放。
    ///
    /// 说话状态：某个座位的回放缓冲里有持续数据，就认为他在说话，
    /// 从而驱动该炸弹角色的嘴巴开合。
    ///
    /// 包格式：[1 字节 座位号][PCM 小端 16 位]
    /// </summary>
    [DisallowMultipleComponent]
    public class VoiceChatManager : MonoBehaviour
    {
        [Header("绑定")]
        [Tooltip("对局驱动（用于取角色引用）。留空则自动查找。")]
        public GameDirector director;

        [Tooltip("联机管理器（用于取端口与本地座位）。留空则自动查找。")]
        public LanNetworkManager lan;

        [Header("开关")]
        [Tooltip("是否启用语音。")]
        public bool enableVoice = true;

        [Tooltip("是否为按键说话。关闭则常开（容易串音，建议保持开启）。")]
        public bool pushToTalk = true;

        [Tooltip("按键说话的按键。")]
        public KeyCode pushToTalkKey = KeyCode.V;

        [Header("语音活动检测（VAD）")]
        [Tooltip("绝对音量下限。低于它的声音一律当作噪声，无论自适应门限算出来多少。")]
        [Range(0f, 0.2f)] public float noiseFloorAbsolute = 0.006f;

        [Tooltip("自适应门限 = 环境噪声底 × 该倍率。麦克风增益千差万别，固定阈值必然一半人用不了。")]
        [Range(1.2f, 8f)] public float noiseFloorFactor = 2.6f;

        [Tooltip("噪声底的跟踪速度。越大越跟得上环境变化，但也越容易被说话声带偏。")]
        [Range(0.001f, 0.5f)] public float noiseFloorAdaptRate = 0.05f;

        [Tooltip("起音确认：连续多少段超过门限才判定「开始说话」，避免咔哒声误触发。")]
        [Range(1, 10)] public int attackChunks = 2;

        [Tooltip("拖尾保持：音量掉下去后仍继续发送多少段，避免句尾被切掉。")]
        [Range(0, 40)] public int hangoverChunks = 8;

        [Tooltip("超过门限多少倍才算「有效说话段」，用于更新噪声底（避免把说话声当成噪声）。")]
        [Range(1.5f, 12f)] public float speechOverNoiseRatio = 3.5f;

        [Header("传输通道")]
        [Tooltip("语音走哪条通道。自动 = 联机中走 TCP 中继（公网/内网穿透可用），否则走局域网 UDP 广播。")]
        public VoiceChannelMode channelMode = VoiceChannelMode.Auto;

        [Header("音频参数")]
        [Tooltip("采样率。16000 足够听清人声且省带宽。")]
        public int sampleRate = 16000;

        [Tooltip("每个 UDP 包包含多少毫秒的音频。")]
        [Range(10, 100)] public int chunkMilliseconds = 30;

        [Tooltip("输出音量。")]
        [Range(0f, 1f)] public float outputVolume = 1f;

        [Header("网络")]
        [Tooltip("语音端口 = 联机端口 + 该偏移。")]
        public int portOffset = 1;

        [Header("嘴部动画")]
        [Tooltip("回放缓冲里超过这么多采样点时认为「正在说话」。")]
        public int talkingSampleThreshold = 240;

        AudioClip _micClip;
        string _micDevice;
        int _lastMicPos;
        float[] _micFloat;

        UdpClient _sender;
        UdpClient _receiver;
        IPEndPoint _broadcast;
        Thread _receiveThread;
        volatile bool _running;

        readonly Dictionary<int, VoicePlayback> _playbacks = new Dictionary<int, VoicePlayback>();
        readonly Dictionary<int, BombCharacter> _characters = new Dictionary<int, BombCharacter>();
        readonly Dictionary<int, int> _lastHeard = new Dictionary<int, int>();

        int _localSeat = -1;
        bool _localTalking;
        int _chunkSamples;

        // ---- VAD 状态
        float _noiseFloor = 0.006f;   // 环境噪声底的滑动估计
        int _aboveCount;              // 连续超过门限的段数（起音确认用）
        int _hangover;                // 剩余拖尾段数（保持发送用）
        bool _speaking;

        // ---- 传输通道
        bool _useRelay;

        bool _micReady;
        readonly Queue<float[]> _preRoll = new Queue<float[]>();

        // ---- 麦克风测试：录制三秒，只在本机回放，绝不会发到语音房间
        const float MicTestSeconds = 3f;
        readonly List<float> _micTestSamples = new List<float>();
        AudioSource _micTestSource;
        AudioClip _micTestClip;
        int _micTestLastPos;
        float _micTestEndTime;
        bool _micTestRecording;
        bool _micTestWasPlaying;

        public string MicrophoneName => string.IsNullOrEmpty(_micDevice) ? "未检测到麦克风" : _micDevice;
        public string MicrophoneTestStatus { get; private set; } = "点击后将录制 3 秒并在本机回放";
        public float MicrophoneInputLevel { get; private set; }
        public bool IsMicrophoneTestRunning => _micTestRecording || (_micTestSource != null && _micTestSource.isPlaying);

        /// <summary>本机麦克风是否可用。为 false 时仍能听到别人说话。</summary>
        public bool IsMicReady => _micReady;

        public bool IsTransmitting { get; private set; }
        public bool VoiceActivationEnabled => !pushToTalk;

        public void SetVoiceActivation(bool enabledValue)
        {
            pushToTalk = !enabledValue;
            PlayerPrefs.SetInt("BombTavern.VoiceActivation", enabledValue ? 1 : 0);
            PlayerPrefs.Save();
            ResetVad();
        }

        public void SetOutputVolume(float value)
        {
            outputVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat("BombTavern.VoiceVolume", outputVolume);
            lock (_playbacks)
            {
                foreach (var playback in _playbacks.Values)
                    if (playback != null) playback.volume = outputVolume;
            }
        }

        void Awake()
        {
            if (director == null) director = FindObjectOfType<GameDirector>();
            if (lan == null) lan = FindObjectOfType<LanNetworkManager>();
        }

        void Start()
        {
            // 自动化/服务器模式没有真实音频设备。这里必须在访问 Microphone 前退出，
            // 否则核心玩法测试会被无关的设备初始化错误污染。
            if (Application.isBatchMode)
            {
                Debug.Log("[语音] 批处理模式跳过音频设备初始化。");
                enabled = false;
                return;
            }
            if (!enableVoice) { enabled = false; return; }

            pushToTalk = PlayerPrefs.GetInt("BombTavern.VoiceActivation", pushToTalk ? 0 : 1) == 0;

            sampleRate = 16000;
            chunkMilliseconds = 20;
            hangoverChunks = Mathf.Max(hangoverChunks, 20);
            outputVolume = PlayerPrefs.GetFloat("BombTavern.VoiceVolume", outputVolume);
            _chunkSamples = sampleRate * chunkMilliseconds / 1000;
            _micFloat = new float[_chunkSamples];

            StartMic();
            StartNetwork();
        }

        void OnDestroy()
        {
            StopMicrophoneTest(false);
            StopNetwork();
        }
        void OnApplicationQuit() => StopNetwork();

        // ============================================================ 麦克风
        void StartMic()
        {
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                // 没有麦克风只代表「说不了」，不代表「听不了」。
                // 这里绝不能把组件 disable 掉 —— 那会连 Update/SyncSeats 一起停，
                // 结果就是没麦的人连别人说话都听不到。
                _micReady = false;
                Debug.LogWarning("[语音] 没有检测到麦克风设备：本机只能听，不能说。");
                return;
            }

            _micDevice = Microphone.devices[0];
            _micClip = Microphone.Start(_micDevice, true, 5, sampleRate);
            _micReady = _micClip != null;
            _lastMicPos = 0;
            Debug.Log($"[语音] 已开启麦克风：{_micDevice}（{sampleRate}Hz）");
        }

        // ============================================================ 网络
        /// <summary>
        /// 选通道。中继通道和游戏共用同一条 TCP 连接，
        /// 所以公网/内网穿透工具只转发一个 TCP 端口就行，不需要额外开 UDP。
        /// </summary>
        bool ResolveUseRelay()
        {
            switch (channelMode)
            {
                case VoiceChannelMode.LanUdp: return false;
                case VoiceChannelMode.TcpRelay: return true;
                default: return lan != null && lan.VoiceRelayAvailable;
            }
        }

        void StartNetwork()
        {
            _useRelay = ResolveUseRelay();
            if (lan != null)
            {
                lan.VoicePacketReceived -= OnRelayVoice;
                lan.VoicePacketReceived += OnRelayVoice;
            }

            if (_useRelay)
            {
                if (lan != null)
                {
                    lan.VoicePacketReceived -= OnRelayVoice;
                    lan.VoicePacketReceived += OnRelayVoice;
                }
                Debug.Log("[语音] 使用 TCP 中继通道（与游戏同一条连接，公网/内网穿透可用）。");
                return;
            }

            int basePort = lan != null && lan.port > 0 ? lan.port : (director != null && director.config != null ? director.config.defaultPort : 7788);
            int port = basePort + portOffset;

            try
            {
                _sender = new UdpClient { EnableBroadcast = true };
                _broadcast = new IPEndPoint(IPAddress.Broadcast, port);

                _receiver = new UdpClient();
                _receiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _receiver.Client.Bind(new IPEndPoint(IPAddress.Any, port));

                _running = true;
                _receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "BombTavern-Voice" };
                _receiveThread.Start();

                Debug.Log($"[语音] 已在 UDP {port} 上收发。");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[语音] 无法开启语音收发：{e.Message}");
                enabled = false;
            }
        }

        void StopNetwork()
        {
            _running = false;
            if (lan != null) lan.VoicePacketReceived -= OnRelayVoice;

            try { _receiver?.Close(); } catch { }
            try { _sender?.Close(); } catch { }
            _receiver = null;
            _sender = null;

            if (!string.IsNullOrEmpty(_micDevice) && Microphone.IsRecording(_micDevice))
                Microphone.End(_micDevice);
        }

        /// <summary>中继通道来的语音（主机已派发到主线程）。</summary>
        void OnRelayVoice(int seat, byte[] pcm) => PlayPacket(seat, pcm);

        /// <summary>把一包 16 位小端 PCM 交给对应座位的回放。offset 用于跳过 UDP 包首的座位字节。</summary>
        void PlayPacket(int seat, byte[] pcm, int offset = 0)
        {
            if (pcm == null) return;

            int bytes = pcm.Length - offset;
            if (bytes < 2) return;
            if (seat < 0 || seat == _localSeat) return;      // 自己的声音不用回放

            int samples = bytes / 2;
            var shorts = new short[samples];
            for (int i = 0; i < samples; i++)
                shorts[i] = (short)(pcm[offset + i * 2] | (pcm[offset + i * 2 + 1] << 8));

            lock (_playbacks)
            {
                _lastHeard[seat] = Environment.TickCount;
                if (_playbacks.TryGetValue(seat, out var pb) && pb != null) pb.Enqueue(shorts);
            }
        }

        void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] packet = _receiver.Receive(ref any);
                    if (packet == null || packet.Length < 2) continue;

                    // UDP 包首字节是座位号，PCM 从下标 1 开始
                    if (!_useRelay) PlayPacket(packet[0], packet, 1);
                }
                catch (Exception)
                {
                    if (_running) Thread.Sleep(20);
                }
            }
        }

        // ============================================================ 每帧
        void Update()
        {
            bool relay = ResolveUseRelay();
            if (relay != _useRelay)
            {
                _useRelay = relay;
                if (!_useRelay && _sender == null) StartNetwork();
                ResetVad();
            }
            SyncSeats();
            UpdateMicrophoneTest();
            Transmit();
            UpdateMouths();
        }

        /// <summary>录制三秒后本地回放。测试数据不会进入 SendChunk。</summary>
        public bool BeginMicrophoneTest()
        {
            if (!_micReady || _micClip == null)
            {
                MicrophoneTestStatus = "测试失败：没有可用的麦克风";
                return false;
            }

            StopMicrophoneTest(false);
            if (_micTestSource == null)
            {
                _micTestSource = gameObject.AddComponent<AudioSource>();
                _micTestSource.playOnAwake = false;
                _micTestSource.loop = false;
                _micTestSource.spatialBlend = 0f;
                _micTestSource.volume = 1f;
            }

            _micTestSamples.Clear();
            _micTestLastPos = Microphone.GetPosition(_micDevice);
            _micTestEndTime = Time.unscaledTime + MicTestSeconds;
            _micTestRecording = true;
            _micTestWasPlaying = false;
            MicrophoneInputLevel = 0f;
            MicrophoneTestStatus = "正在录制测试语音… 3 秒后自动回放";
            return true;
        }

        public void StopMicrophoneTest(bool showStatus = true)
        {
            _micTestRecording = false;
            _micTestWasPlaying = false;
            MicrophoneInputLevel = 0f;
            if (_micTestSource != null) _micTestSource.Stop();
            if (_micTestClip != null)
            {
                Destroy(_micTestClip);
                _micTestClip = null;
            }
            if (showStatus) MicrophoneTestStatus = "测试已停止，可重新开始";
        }

        void UpdateMicrophoneTest()
        {
            if (!_micTestRecording)
            {
                if (_micTestWasPlaying && (_micTestSource == null || !_micTestSource.isPlaying))
                {
                    _micTestWasPlaying = false;
                    MicrophoneTestStatus = "测试完成：若能听清回放，语音设备工作正常";
                }
                return;
            }

            int pos = Microphone.GetPosition(_micDevice);
            if (pos < 0) return;
            int available = pos - _micTestLastPos;
            if (available < 0) available += _micClip.samples;
            if (available > 0)
            {
                int remaining = Mathf.Max(0, Mathf.RoundToInt(sampleRate * MicTestSeconds) - _micTestSamples.Count);
                ReadMicSamples(_micTestLastPos, Mathf.Min(available, remaining), _micTestSamples);
                _micTestLastPos = pos;

                int levelCount = Mathf.Min(available, _micTestSamples.Count);
                double sum = 0d;
                for (int i = _micTestSamples.Count - levelCount; i < _micTestSamples.Count; i++)
                    sum += _micTestSamples[i] * _micTestSamples[i];
                MicrophoneInputLevel = levelCount > 0
                    ? Mathf.Clamp01(Mathf.Sqrt((float)(sum / levelCount)) * 8f)
                    : 0f;
            }

            if (Time.unscaledTime < _micTestEndTime && _micTestSamples.Count < sampleRate * MicTestSeconds) return;

            _micTestRecording = false;
            MicrophoneInputLevel = 0f;
            if (_micTestSamples.Count < sampleRate / 4)
            {
                MicrophoneTestStatus = "测试失败：没有采集到足够的声音，请检查系统麦克风权限";
                return;
            }

            _micTestClip = AudioClip.Create("MicrophoneTest", _micTestSamples.Count, 1, sampleRate, false);
            _micTestClip.SetData(_micTestSamples.ToArray(), 0);
            _micTestSource.clip = _micTestClip;
            _micTestSource.Play();
            _micTestWasPlaying = true;
            MicrophoneTestStatus = "正在本机回放测试录音…";
        }

        void ReadMicSamples(int start, int count, List<float> destination)
        {
            if (count <= 0 || _micClip == null) return;
            int first = Mathf.Min(count, _micClip.samples - start);
            var part = new float[first];
            _micClip.GetData(part, start);
            destination.AddRange(part);

            int second = count - first;
            if (second <= 0) return;
            part = new float[second];
            _micClip.GetData(part, 0);
            destination.AddRange(part);
        }

        /// <summary>音源始终由管理器持有；角色出局或切换观战视角也不能停播。</summary>
        void SyncSeats()
        {
            if (director == null) return;

            // 本地座位优先取联机管理器分配的座位；没联机时用对局里的座位。
            int localSeat = lan != null && lan.localSeat >= 0 ? lan.localSeat : director.localSeat;
            _localSeat = localSeat;

            int seats = director.Session != null
                ? director.Session.Players.Length
                : GameSession_DefaultSeats();
            var chars = director.characters;

            lock (_playbacks)
            {
                for (int i = 0; i < seats; i++)
                {
                    if (i == _localSeat) continue;      // 自己的声音不需要回放
                    if (_playbacks.TryGetValue(i, out var existing) && existing != null)
                    {
                        existing.volume = outputVolume;
                        var speaker = chars != null && i < chars.Length ? chars[i] : null;
                        if (speaker != null) existing.transform.position = speaker.transform.position;
                        continue;
                    }

                    var character = chars != null && i < chars.Length ? chars[i] : null;

                    var go = new GameObject($"VoiceSeat_{i}");
                    go.transform.SetParent(transform, false);
                    if (character != null) go.transform.position = character.transform.position;

                    var pb = go.AddComponent<VoicePlayback>();
                    if (pb == null || pb.Source == null)
                    {
                        // 极端情况下（无音频设备 / 物体未激活）放弃这一路，不影响对局
                        Destroy(go);
                        continue;
                    }

                    pb.seatIndex = i;
                    // 采样率必须与发送端一致，否则回放时音调会不对
                    pb.sourceSampleRate = sampleRate;
                    pb.volume = outputVolume;
                    pb.Source.volume = 1f;
                    _playbacks[i] = pb;

                    if (character != null) _characters[i] = character;
                }
            }
        }

        static int GameSession_DefaultSeats() => 4;

        void Transmit()
        {
            if (!_micReady || _micClip == null || _localSeat < 0) return;

            // 测试录音和回放期间禁止向房间发送，避免扬声器回放被麦克风再次收进去形成回声。
            if (IsMicrophoneTestRunning)
            {
                IsTransmitting = false;
                _localTalking = false;
                ResetVad();
                _lastMicPos = Microphone.GetPosition(_micDevice);
                return;
            }

            // 对讲键统一走 GameInput：这样触摸端以后可以换成界面上的按住说话按钮，
            // 而不必在这里再加一套平台分支。
            GameInput.pushToTalkKey = pushToTalkKey;
            bool wantTalk = !pushToTalk || GameInput.PushToTalkHeld;
            IsTransmitting = false;
            _localTalking = false;

            if (!wantTalk)
            {
                ResetVad();
                _lastMicPos = Microphone.GetPosition(_micDevice);
                return;
            }

            int pos = Microphone.GetPosition(_micDevice);
            if (pos < 0) return;

            int available = pos - _lastMicPos;
            if (available < 0) available += _micClip.samples;   // 环形缓冲回绕
            if (available < _chunkSamples) return;

            // Drain every complete frame in order; never skip the fractional remainder.
            // Only discard stale speech after a stall longer than half a second.
            if (available > sampleRate / 2)
            {
                _lastMicPos = (pos - sampleRate / 2 + _micClip.samples) % _micClip.samples;
                available = sampleRate / 2;
                ResetVad();
            }
            while (available >= _chunkSamples)
            {
                if (!_micClip.GetData(_micFloat, _lastMicPos)) break;
                _lastMicPos = (_lastMicPos + _chunkSamples) % _micClip.samples;
                available -= _chunkSamples;
                // Push-to-talk explicitly authorizes transmission, including quiet syllables.
                bool transmit = pushToTalk || VoiceActivityDetected();
                if (!transmit)
                {
                    _preRoll.Enqueue((float[])_micFloat.Clone());
                    while (_preRoll.Count > 6) _preRoll.Dequeue();
                    continue;
                }
                var current = _micFloat;
                while (_preRoll.Count > 0) { _micFloat = _preRoll.Dequeue(); SendChunk(); }
                _micFloat = current;
                SendChunk();
                IsTransmitting = true;
                _localTalking |= Rms(_micFloat) > noiseFloorAbsolute;
            }
        }

        /// <summary>
        /// 语音活动检测。
        ///
        /// 用「环境噪声底 × 倍率」当门限，而不是写死一个数 —— 每个人的麦克风增益、
        /// 房间底噪差别很大，固定阈值必然出现「有人一直触发、有人完全触发不了」。
        /// 另外加了起音确认（防咔哒声）与拖尾保持（防句尾被切）。
        /// </summary>
        bool VoiceActivityDetected()
        {
            float rms = Rms(_micFloat);
            float threshold = Mathf.Max(noiseFloorAbsolute, _noiseFloor * noiseFloorFactor);

            bool voiced = rms >= threshold;

            // 只在「明显不是说话」的段上更新噪声底，否则会被自己的声音带偏
            if (!voiced && rms < threshold * speechOverNoiseRatio)
                _noiseFloor = Mathf.Lerp(_noiseFloor, rms, noiseFloorAdaptRate);

            if (voiced)
            {
                _aboveCount++;
                _hangover = hangoverChunks;
            }
            else
            {
                _aboveCount = 0;
                if (_hangover > 0) _hangover--;
            }

            // 起音要连续 N 段确认；已经在说话时则靠拖尾计数维持
            _speaking = _speaking ? (voiced || _hangover > 0) : (_aboveCount >= attackChunks);
            return _speaking;
        }

        void ResetVad()
        {
            _preRoll.Clear();
            _aboveCount = 0;
            _hangover = 0;
            _speaking = false;
        }

        static float Rms(float[] data)
        {
            if (data == null || data.Length == 0) return 0f;

            double sum = 0d;
            for (int i = 0; i < data.Length; i++) sum += (double)data[i] * data[i];
            return Mathf.Sqrt((float)(sum / data.Length));
        }

        /// <summary>把当前这一段的麦克风数据发出去。</summary>
        void SendChunk()
        {
            var pcm = new byte[_chunkSamples * 2];
            for (int i = 0; i < _chunkSamples; i++)
            {
                int v = (int)(Mathf.Clamp(_micFloat[i], -1f, 1f) * 32767f);
                pcm[i * 2] = (byte)(v & 0xFF);
                pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }

            if (_useRelay)
            {
                if (lan != null) lan.SendVoice(_localSeat, pcm);
                return;
            }

            // 局域网 UDP 广播：包首补一个座位号字节
            if (_sender == null || _broadcast == null) return;

            try
            {
                var framed = new byte[1 + pcm.Length];
                framed[0] = (byte)_localSeat;
                Buffer.BlockCopy(pcm, 0, framed, 1, pcm.Length);
                _sender.Send(framed, framed.Length, _broadcast);
            }
            catch
            {
                // 网络抖动丢一包无所谓，语音本来就是「宁可断不可等」
            }
        }

        void UpdateMouths()
        {
            // 本地玩家：自己在说话时嘴巴动（第一人称看不到自己，但联机时别人能看到自己被隐藏，
            // 这里保留是为了第三人称/观战模式）
            if (_localSeat >= 0 && _characters.TryGetValue(_localSeat, out var me) && me != null)
                me.SetTalking(_localTalking);

            lock (_playbacks)
            {
                foreach (var kv in _playbacks)
                {
                    int seat = kv.Key;
                    var pb = kv.Value;
                    if (pb == null) continue;

                    bool speaking = pb.BufferedSamples > talkingSampleThreshold;

                    if (_characters.TryGetValue(seat, out var character) && character != null)
                        character.SetTalking(speaking);
                }
            }
        }

        /// <summary>给外部（例如 HUD）查询某个座位是否正在说话。</summary>
        public bool IsSeatSpeaking(int seat)
        {
            if (seat == _localSeat) return _micReady && IsTransmitting && _localTalking;

            lock (_playbacks)
            {
                bool heardRecently = _lastHeard.TryGetValue(seat, out int at) &&
                                     unchecked((uint)(Environment.TickCount - at)) < 650u;
                return heardRecently;
            }
        }
    }
}
