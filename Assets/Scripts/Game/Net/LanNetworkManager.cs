using System;
using System.Collections.Generic;
using System.Text;
using BombTavern.Config;
using BombTavern.Core;
using BombTavern.Runtime;
using UnityEngine;

namespace BombTavern.Net
{
    /// <summary>花名册里的一名玩家。</summary>
    public struct SeatInfo
    {
        public int seat;
        public string name;
        public string skinId;

        public SeatInfo(int seat, string name, string skinId)
        {
            this.seat = seat;
            this.name = name;
            this.skinId = skinId;
        }
    }

    /// <summary>
    /// 局域网联机管理（房主制）。
    ///
    /// 流程：
    ///   1) 客户端连上后发 Hello（昵称 + 皮肤），主机分配座位并回 Roster
    ///   2) 主机随时广播 RosterAll，让所有人看到完整的座位表
    ///   3) **只有房主**能点「开始游戏」→ 生成种子 → 广播 Welcome → 所有端同时开局
    ///   4) 对局中：客户端把指令发给主机，主机权威结算后广播给所有人（含 AI 动作）
    ///
    /// 同步模型：确定性锁步。各端 (配置 + 种子 + 指令序列) 一致 ⇒ 状态一致。
    /// 公网联机：替换 ITransport 实现即可，上层逻辑不动。
    /// </summary>
    [DisallowMultipleComponent]
    public class LanNetworkManager : MonoBehaviour
    {
        const char Sep = '|';

        [Header("绑定")]
        [Tooltip("对局驱动。留空则自动查找。")]
        public GameDirector director;

        [Header("模式")]
        [Tooltip("局域网角色。由开始界面写入，也可手动指定。")]
        public GameMode mode = GameMode.SinglePlayer;

        [Tooltip("客户端要连接的主机地址。")]
        public string connectAddress = "127.0.0.1";

        [Tooltip("端口。0 表示取 GameConfig.defaultPort。")]
        public int port = 0;

        [Header("本机信息")]
        [Tooltip("本机玩家昵称（联机时广播给其他人）。")]
        public string localPlayerName = "红雷";

        [Header("状态（只读）")]
        public int localSeat = -1;
        public int connectedClients;
        public bool sessionStarted;

        [Tooltip("已发出的状态快照数（主机侧）。")]
        public int SnapshotsSent;

        [Tooltip("已应用的状态快照数（客户端侧）。")]
        public int SnapshotsApplied;

        [Header("状态同步")]
        [Tooltip("兜底快照间隔（秒）。指令事件已能覆盖绝大多数变化，这个定时器只用于防丢包。")]
        [Range(0.25f, 10f)] public float snapshotInterval = 2f;

        float _snapshotTimer;

        /// <summary>座位表（含本机与远端）。大厅 UI 直接读它。</summary>
        public readonly List<SeatInfo> RosterList = new List<SeatInfo>();

        /// <summary>大厅状态文本。</summary>
        public string StatusText { get; private set; } = "未启动";

        public bool IsHost => mode == GameMode.LanHost;
        public bool CanStart => IsHost && !sessionStarted && director != null && director.config != null;

        TcpLanTransport _transport;
        readonly List<GameCommand> _accepted = new List<GameCommand>();
        readonly Dictionary<int, int> _peerSeat = new Dictionary<int, int>();
        readonly Dictionary<int, SeatInfo> _peerInfo = new Dictionary<int, SeatInfo>();
        readonly List<int> _freeSeats = new List<int>();
        readonly List<string> _pendingCommands = new List<string>();
        readonly List<string> _notices = new List<string>();

        int _nextSeat = 1;

        public IReadOnlyList<string> Notices => _notices;

        void Awake()
        {
            if (director == null) director = FindObjectOfType<GameDirector>();
        }

        void Start()
        {
            if (director == null || director.config == null)
            {
                enabled = false;
                return;
            }

            ApplySessionContext();
            ApplyCliOptions();      // 命令行优先级高于开始界面，便于开专用主机 / 自动化自检

            if (port <= 0) port = director.config.defaultPort;

            switch (mode)
            {
                case GameMode.LanHost: StartHost(); break;
                case GameMode.LanClient: StartClient(); break;
                default:
                    StatusText = "单机模式，未启用联机。";
                    enabled = false;
                    break;
            }
        }

        /// <summary>从 SessionContext 取开始界面里填的信息。</summary>
        void ApplySessionContext()
        {
            var ctx = SessionContext.Instance;
            if (ctx == null) return;

            mode = ctx.mode;
            localPlayerName = ctx.playerName;
            if (!string.IsNullOrEmpty(ctx.connectAddress)) connectAddress = ctx.connectAddress;
            if (ctx.port > 0) port = ctx.port;
        }

        /// <summary>命令行参数覆盖：同一份可执行文件既能当主机也能当客户端。</summary>
        void ApplyCliOptions()
        {
            var cli = NetCliOptions.Parse();
            if (!cli.specified) return;

            mode = cli.mode;
            if (!string.IsNullOrEmpty(cli.address)) connectAddress = cli.address;
            if (cli.port > 0) port = cli.port;
            if (!string.IsNullOrWhiteSpace(cli.playerName))
            {
                localPlayerName = cli.playerName.Trim();
                if (SessionContext.Instance != null) SessionContext.Instance.playerName = localPlayerName;
            }

            Debug.Log($"[联机] 命令行覆盖：mode={mode} addr={connectAddress} port={port}");
        }

        void OnDestroy()
        {
            if (director != null) director.CommandApplied -= OnHostCommandApplied;
            _transport?.Stop();
        }

        // ============================================================ 主机
        void StartHost()
        {
            director.localSeat = 0;
            localSeat = 0;

            _transport = new TcpLanTransport();
            BindTransport();
            _transport.StartHost(port);

            director.CommandApplied += OnHostCommandApplied;

            RosterList.Clear();
            AddSeat(new SeatInfo(0, localPlayerName, SkinIdOfLocal()));
            RefreshRoster();

            // 枚举本机地址，好在界面上直接告诉房主「把哪个地址念给对方」。
            RefreshShareableAddresses();

            StatusText = $"已开房，端口 {port}。等其他人加入…";
            Debug.Log($"[联机] 主机已启动，端口 {port}，可分享地址 {ShareableAddresses.Count} 个。");
        }

        string SkinIdOfLocal()
        {
            var ctx = SessionContext.Instance;
            return ctx != null ? ctx.skinId : "round_black";
        }

        void BindTransport()
        {
            _transport.MessageReceived += OnMessage;
            _transport.PeerConnected += OnPeerConnected;
            _transport.PeerDisconnected += OnPeerDisconnected;

            // 连接失败必须有人接住：否则界面只会永远停在「正在连接…」，
            // 玩家不知道是自己地址填错、房主没开房，还是穿透隧道没起。
            _transport.ConnectFailed += OnConnectFailed;
        }

        void OnConnectFailed(string reason)
        {
            StatusText = "连接失败。";
            AddNotice(reason);
        }

        /// <summary>往大厅公告区追加一条消息（界面用它反馈输入错误等）。</summary>
        public void AddNotice(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _notices.Add(text);
            if (_notices.Count > 8) _notices.RemoveAt(0);
        }

        /// <summary>当前链路状态，UI 用它区分「正在连 / 连上了 / 失败了」。</summary>
        public TcpLanTransport.LinkState LinkState =>
            _transport != null ? _transport.State : TcpLanTransport.LinkState.Idle;

        /// <summary>连接失败的原因（成功或未开始时为空）。</summary>
        public string LinkError => _transport != null ? _transport.LastError : null;

        /// <summary>连接超时（秒），透传给传输层，方便在 Inspector 调整。</summary>
        public float connectTimeoutSeconds
        {
            get => _transport != null ? _transport.connectTimeoutMs / 1000f : 6f;
            set { if (_transport != null) _transport.connectTimeoutMs = Mathf.RoundToInt(value * 1000f); }
        }

        /// <summary>本机所有可以念给对方听的地址（Radmin VPN 的排在前面）。</summary>
        public List<ShareableAddress> ShareableAddresses { get; } = new List<ShareableAddress>();

        /// <summary>刷新可分享地址列表。开房时调一次即可。</summary>
        public void RefreshShareableAddresses()
        {
            ShareableAddresses.Clear();
            ShareableAddresses.AddRange(NetAddressUtility.ListShareable());
        }

        /// <summary>
        /// 丢掉当前连接重来。客户端在「地址填错 / 房主还没开房」时用得上，
        /// 不必重启整个游戏。
        /// </summary>
        public void RetryConnect(string address = null)
        {
            if (mode != GameMode.LanClient) return;
            if (!string.IsNullOrWhiteSpace(address)) connectAddress = address.Trim();

            _notices.Clear();
            _transport?.Stop();

            _transport = new TcpLanTransport();
            BindTransport();
            _transport.StartClient(connectAddress, port);

            StatusText = $"正在连接 {connectAddress}:{port} …";
        }

        void OnPeerConnected(int peerId)
        {
            if (mode != GameMode.LanHost)
            {
                // 客户端：连上后立刻报上昵称与皮肤
                _transport.Send(TcpLanTransport.HostPeerId,
                    NetProtocol.Encode(NetMessageType.Hello, localPlayerName + Sep + SkinIdOfLocal()));
                StatusText = $"已连接 {connectAddress}:{port}，等待房主开始…";
                return;
            }

            if (_peerSeat.ContainsKey(peerId)) return;

            int seat = sessionStarted
                ? TakeFreeSeat()                                   // 对局中只有「重连」能落座
                : TakeSeat();

            _peerSeat[peerId] = seat;

            // 重连回到自己原来的座位：把之前顶替他的 AI 交还给人
            if (sessionStarted && seat >= 0) director.TakeBackSeatFromBot(seat);

            connectedClients = CountSeatedPeers();

            int humans = Mathf.Min(1 + CountSeatedPeers(), GameSession.MaxSeats);
            _transport.Send(peerId, NetProtocol.Encode(NetMessageType.Roster,
                $"{seat}{Sep}0{Sep}{director.config.totalSeats}{Sep}{humans}"));

            Debug.Log($"[联机] peer={peerId} 加入，分配座位 {seat}。");
            RefreshRoster();

            // 对局中才加入的：直接补一份完整快照，让他立刻看到当前局面
            // （不需要历史指令回放 —— 这正是状态同步比锁步省事的地方）
            if (sessionStarted && director != null && director.Session != null)
            {
                var bytes = StateSnapshot.Write(director.Session, seat);
                if (bytes != null)
                {
                    _transport.Send(peerId, NetProtocol.Encode(
                        NetMessageType.Snapshot, NetProtocol.EncodeSnapshot(bytes)));
                    SnapshotsSent++;
                }
            }
        }

        void OnPeerDisconnected(int peerId)
        {
            if (mode == GameMode.LanClient && peerId == TcpLanTransport.HostPeerId)
            {
                AddNotice("房主已离开，房间关闭。");
                StartCoroutine(ReturnAfterHostLeft());
                return;
            }
            if (_peerSeat.TryGetValue(peerId, out int seat))
            {
                _peerSeat.Remove(peerId);
                _peerInfo.Remove(peerId);
                connectedClients = _peerSeat.Count;

                if (sessionStarted)
                {
                    director.ConvertSeatToBot(seat);
                    BroadcastSnapshot();
                }

                // 释放座位号：既让断线的人重连时能坐回原位，
                // 也避免连几次就把四个座位用光。
                ReleaseSeat(seat);

                _notices.Add($"座位 {seat} 断线，已移交给 AI。");
                RefreshRoster();
            }
        }

        /// <summary>取一个可用座位：优先复用释放出来的，否则取下一个没用过的。满员返回 -1。</summary>
        int TakeSeat()
        {
            if (_freeSeats.Count > 0)
            {
                int s = _freeSeats[_freeSeats.Count - 1];
                _freeSeats.RemoveAt(_freeSeats.Count - 1);
                return s;
            }
            return _nextSeat < GameSession.MaxSeats ? _nextSeat++ : -1;
        }

        /// <summary>对局中只接受「回到自己原来的座位」，不接受新玩家中途加入（会把局面搅乱）。</summary>
        int TakeFreeSeat() => _freeSeats.Count > 0 ? TakeSeat() : -1;

        void ReleaseSeat(int seat)
        {
            if (seat < 0 || seat >= GameSession.MaxSeats) return;
            if (_freeSeats.Contains(seat)) return;
            _freeSeats.Add(seat);
        }

        /// <summary>当前真正有座位的客户端数量（旁观者不算）。</summary>
        int CountSeatedPeers()
        {
            int n = 0;
            foreach (var kv in _peerSeat)
                if (kv.Value >= 0) n++;
            return n;
        }

        /// <summary>
        /// 主机权威接受任意指令后（含 AI 的动作）：把整份权威状态快照推给所有客户端。
        /// 注意这里**不再回传指令**：客户端不重放指令、不跑模拟，只整体覆盖状态，
        /// 因此不会出现浮点漂移导致的失步。
        /// </summary>
        void OnHostCommandApplied(GameCommand cmd)
        {
            _accepted.Add(cmd);

            // 悬停随鼠标频繁变化，为它推整份快照（约 2KB）太浪费，单独走一条小消息。
            // 兜底快照依然会带上悬停，所以不会失同步。
            if (cmd.type == CommandType.SetHover)
            {
                _transport?.Broadcast(NetProtocol.Encode(NetMessageType.Hover,
                    NetProtocol.EncodeHover(cmd.seatIndex, cmd.x, cmd.y)));
                return;
            }

            BroadcastSnapshot();
        }

        /// <summary>
        /// 给每个客户端各发一份快照。因为快照按座位做了过滤
        /// （私有盘面 / 私人探查读数只发给本人），所以不能用 Broadcast，必须逐个 Send。
        /// </summary>
        void BroadcastSnapshot()
        {
            if (mode != GameMode.LanHost || _transport == null) return;
            if (director == null || director.Session == null) return;

            // 只取「上一条快照之后」的新事件。
            // 用游标而不是清空日志，是为了让定时兜底快照不会把同一批事件重复播一遍动画。
            var log = director.Session.Log;
            int total = log != null ? log.Count : 0;

            List<GameEvent> fresh = null;
            if (total > _eventCursor && log != null)
            {
                fresh = new List<GameEvent>(total - _eventCursor);
                for (int i = _eventCursor; i < total; i++) fresh.Add(log[i]);
            }
            _eventCursor = total;

            foreach (var kv in _peerSeat)
            {
                int seat = kv.Value;
                var bytes = StateSnapshot.Write(director.Session, seat, fresh);
                if (bytes == null) continue;

                _transport.Send(kv.Key, NetProtocol.Encode(
                    NetMessageType.Snapshot, NetProtocol.EncodeSnapshot(bytes)));
                SnapshotsSent++;
            }
        }

        /// <summary>道具目录：客户端用它把 itemId 还原成 ItemDefinition。</summary>
        ItemCatalog Catalog()
        {
            var cfg = director != null ? director.config : null;
            return cfg != null && cfg.gacha != null ? cfg.gacha.dropPool : null;
        }

        // ============================================================ 语音中继
        /// <summary>
        /// 收到某个座位的语音 PCM（座位号已由主机按连接解析，客户端无法伪造）。
        /// 回调在主线程派发，订阅方可以直接入队回放。
        /// </summary>
        public event Action<int, byte[]> VoicePacketReceived;

        /// <summary>本机是否有可用的语音中继通道（联机中才为真）。</summary>
        public bool VoiceRelayAvailable =>
            mode != GameMode.SinglePlayer && _transport != null && _transport.IsRunning;

        /// <summary>
        /// 发一包自己的语音。
        /// 客户端发给主机，主机再中继给其余客户端 —— 和游戏共用同一条 TCP 连接，
        /// 所以内网穿透只需转发这一个 TCP 端口，不必额外开 UDP。
        /// </summary>
        public void SendVoice(int seat, byte[] pcm)
        {
            if (!VoiceRelayAvailable || pcm == null || pcm.Length == 0) return;

            string line = NetProtocol.Encode(NetMessageType.Voice, NetProtocol.EncodeVoice(seat, pcm));
            if (IsHost) _transport.Broadcast(line);
            else _transport.Send(TcpLanTransport.HostPeerId, line);
        }

        /// <summary>客户端收到快照后的落地处理。</summary>
        void ApplySnapshot(byte[] bytes)
        {
            if (director == null || director.Session == null) return;

            _incomingEvents.Clear();
            if (!StateSnapshot.Apply(director.Session, bytes, Catalog(), _incomingEvents)) return;

            // 标记对局已开始（Welcome 可能因网络抖动丢失，快照本身就是权威信号）
            sessionStarted = true;

            // 甲方已回应：客户端可以发下一手了
            director.NotifyNetworkAck();

            // 状态已整体替换，立刻重绘面板，避免残留上一帧的颜色
            director.RefreshTabletsNow();

            // 事件再注入表现层：事件流 / 横幅 / 爆炸 / 动作状态机都靠它驱动
            for (int i = 0; i < _incomingEvents.Count; i++)
                director.InjectNetworkEvent(_incomingEvents[i]);

            SnapshotsApplied++;
        }

        readonly List<GameEvent> _incomingEvents = new List<GameEvent>();
        int _eventCursor;

        /// <summary>
        /// 房主点「开始游戏」。只有主机会真正开局，其余端收到 Welcome 后各自开局。
        /// </summary>
        public void StartGame()
        {
            if (!CanStart)
            {
                Debug.LogWarning("[联机] 现在不能开始（不是房主 / 已开局 / 配置缺失）。");
                return;
            }

            sessionStarted = true;

            ulong seed = director.fixedSeed != 0UL ? director.fixedSeed : MakeSeed();
            int humans = Mathf.Clamp(1 + _peerSeat.Count, 1, GameSession.MaxSeats);

            director.StartSession(seed, humans);
            ApplyRosterNamesToSession();

            _transport?.Broadcast(NetProtocol.Encode(NetMessageType.Welcome, BuildWelcome(seed, humans)));
            BroadcastRosterAll();

            // 开局立刻推一次完整快照：不做指令回放，
            // 客户端拿到快照就直接是当前权威状态。
            BroadcastSnapshot();

            StatusText = $"对局进行中（真人 {humans} 名，端口 {port}）";
            Debug.Log($"[联机] 房主开始游戏：seed={seed} humans={humans}");
        }

        /// <summary>
        /// 房主开新一局（结算面板的「再来一局」）。
        /// 和 StartGame 的区别：这里不检查 sessionStarted —— 结算时它本来就是 true，
        /// 用 CanStart 会把按钮判死。
        /// </summary>
        public void RestartGame()
        {
            if (!IsHost || director == null || director.config == null) return;

            ulong seed = director.fixedSeed != 0UL ? director.fixedSeed : MakeSeed();
            int humans = Mathf.Clamp(1 + CountSeatedPeers(), 1, GameSession.MaxSeats);

            director.StartSession(seed, humans);
            ApplyRosterNamesToSession();

            _transport?.Broadcast(NetProtocol.Encode(NetMessageType.Welcome, BuildWelcome(seed, humans)));
            BroadcastRosterAll();

            // 新局的事件日志是新的，游标必须归零，
            // 否则会从上一局的长度处开始取，客户端永远收不到新局事件。
            _eventCursor = 0;
            BroadcastSnapshot();

            StatusText = $"新一局开始（真人 {humans} 名）";
        }

        ulong MakeSeed()
        {
            ulong t = (ulong)System.DateTime.UtcNow.Ticks;
            return t ^ 0xA0761D6478BD642FUL;
        }

        // ============================================================ 客户端
        void StartClient()
        {
            _transport = new TcpLanTransport();
            BindTransport();
            _transport.StartClient(connectAddress, port);
            StatusText = $"正在连接 {connectAddress}:{port} …";
        }

        // ============================================================ 消息
        void OnMessage(int peerId, string line)
        {
            if (!NetProtocol.TryDecode(line, out var type, out var payload)) return;

            switch (type)
            {
                case NetMessageType.Hello:
                    if (mode == GameMode.LanHost)
                    {
                        if (!_peerSeat.ContainsKey(peerId)) OnPeerConnected(peerId);

                        var p = payload.Split(Sep);
                        string nm = p.Length > 0 && p[0].Length > 0 ? p[0] : "匿名炸弹";
                        string skin = p.Length > 1 && p[1].Length > 0 ? p[1] : SkinIdOfLocal();

                        _peerInfo[peerId] = new SeatInfo(_peerSeat.TryGetValue(peerId, out int st) ? st : -1, nm, skin);
                        RefreshRoster();
                    }
                    break;

                case NetMessageType.Roster:
                {
                    var p = payload.Split(Sep);
                    if (p.Length >= 1 && int.TryParse(p[0], out int seat))
                    {
                        localSeat = seat;
                        director.localSeat = seat;
                        // 正常开局会在 StartSession 内刷新一次；若是重连或中途改座，
                        // Session 已经存在，就要立即把相机、隐藏身体与第一人称手切到新座位。
                        if (director.Session != null) director.RefreshLocalPerspective();
                        StatusText = seat >= 0
                            ? $"已入座 {seat}，等待房主开始…"
                            : "房间已满或已开局，你将旁观。";
                    }
                    break;
                }

                case NetMessageType.RosterAll:
                    if (mode != GameMode.LanHost) ApplyRosterAll(payload);
                    break;

                case NetMessageType.Welcome:
                {
                    var p = payload.Split(Sep);
                    if (mode == GameMode.LanClient && p.Length >= 4 &&
                        ulong.TryParse(p[0], out ulong seed) && int.TryParse(p[3], out int humans))
                    {
                        ApplyWelcomeRules(p);
                        StartClientSession(seed, humans);
                    }
                    break;
                }

                // 客户端 → 主机：一条操作意图。只有主机结算，客户端永不本地结算。
                case NetMessageType.Command:
                {
                    if (mode != GameMode.LanHost) break;
                    if (!GameCommand.TryParse(payload, out var cmd)) break;

                    if (!director.ApplyAuthoritative(cmd))
                    {
                        // 被拒绝：把原因回给提交方，并同样推一份快照。
                        // 推快照是必须的 —— 客户端在等「主机已响应」的信号，
                        // 被拒绝时如果不回，客户端会一直卡在这一手上。
                        _transport.Send(peerId, NetProtocol.Encode(
                            NetMessageType.Notice, $"操作被拒绝：{director.LastRejectReason}"));
                        BroadcastSnapshot();
                    }
                    break;
                }

                // 主机 → 客户端：权威状态快照，状态同步模式下唯一的状态来源
                case NetMessageType.Snapshot:
                {
                    if (mode == GameMode.LanHost) break;
                    if (NetProtocol.TryDecodeSnapshot(payload, out var bytes)) ApplySnapshot(bytes);
                    break;
                }

                // 锁步时代的产物，状态同步下不再使用，收到就忽略
                case NetMessageType.SyncCommand:
                    break;

                case NetMessageType.Notice:
                    _notices.Add(payload);
                    // 拒绝类提示也意味着「主机已响应」，放行客户端的下一手
                    director.NotifyNetworkAck();
                    break;

                // 主机 → 客户端：公开悬停位置的轻量更新
                case NetMessageType.Hover:
                {
                    if (mode == GameMode.LanHost) break;
                    if (!NetProtocol.TryDecodeHover(payload, out int hs, out int hx, out int hy)) break;
                    if (director.Session != null) director.Session.SetPublicHover(hs, hx, hy);
                    break;
                }

                // 语音：客户端 → 主机（主机再中继），或主机 → 客户端（直接回放）
                case NetMessageType.Voice:
                {
                    if (!NetProtocol.TryDecodeVoice(payload, out int claimedSeat, out var pcm)) break;

                    if (mode == GameMode.LanHost)
                    {
                        // 座位号以连接映射为准，客户端不能自称是别人
                        if (!_peerSeat.TryGetValue(peerId, out int senderSeat) || senderSeat < 0) break;

                        string relay = NetProtocol.Encode(NetMessageType.Voice,
                            NetProtocol.EncodeVoice(senderSeat, pcm));

                        foreach (var kv in _peerSeat)
                        {
                            if (kv.Key == peerId || kv.Value < 0) continue;   // 不回给发送者
                            _transport.Send(kv.Key, relay);
                        }

                        VoicePacketReceived?.Invoke(senderSeat, pcm);
                    }
                    else
                    {
                        VoicePacketReceived?.Invoke(claimedSeat, pcm);
                    }
                    break;
                }
            }
        }

        void StartClientSession(ulong seed, int humanCount)
        {
            if (sessionStarted) return;
            sessionStarted = true;

            // 顺序很重要：必须在 StartSession 之前进入权威模式，
            // 因为 BuildBrains 在 StartSession 内部执行，要据此决定不建 AI 大脑。
            director.clientAuthorityMode = true;

            director.StartSession(seed, humanCount);
            ApplyRosterNamesToSession();
            director.RefreshLocalPerspective();

            // 输入全部送主机，本地不做任何结算；
            // 返回 false 让上层知道「这一手还没落地」，UI 上的结果由快照带来。
            director.RemoteSubmit = cmd =>
            {
                if (director.Session == null) return false;
                _transport?.Send(TcpLanTransport.HostPeerId,
                    NetProtocol.Encode(NetMessageType.Command, cmd.ToWire()));
                return true;
            };

            StatusText = $"对局进行中（你的座位 {localSeat}，状态同步）";
            Debug.Log($"[联机] 客户端开局：seed={seed} seat={localSeat} humans={humanCount}");
        }

        System.Collections.IEnumerator ReturnAfterHostLeft()
        {
            yield return null;
            SessionContext.ReturnToMainMenu();
        }

        string BuildWelcome(ulong seed, int humans)
        {
            var d = director.config.difficulty;
            var drift = director.config.driftProfile;
            int cols = d != null ? d.columns : 12;
            int rows = d != null ? d.rows : 10;
            int bombs = d != null ? Mathf.RoundToInt(d.baseRisk * cols * rows) : 19;
            float baseDrift = drift != null ? drift.baseDriftMagnitude * 100f : 0f;
            float growth = drift != null ? (drift.roundEscalation - 1f) * 100f : 0f;
            float turn = director.config.turnTimeLimitSeconds;
            return string.Join(Sep.ToString(), seed, 0, director.config.totalSeats, humans,
                cols, rows, bombs, baseDrift.ToString(System.Globalization.CultureInfo.InvariantCulture),
                growth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                turn.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        static void ApplyWelcomeRules(string[] p)
        {
            if (p == null || p.Length < 10 || SessionContext.Instance == null) return;
            var ctx = SessionContext.Instance;
            if (int.TryParse(p[4], out int cols)) ctx.boardColumns = cols;
            if (int.TryParse(p[5], out int rows)) ctx.boardRows = rows;
            if (int.TryParse(p[6], out int bombs)) ctx.bombCount = bombs;
            if (float.TryParse(p[7], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float drift)) ctx.driftPercent = drift;
            if (float.TryParse(p[8], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float growth)) ctx.driftGrowthPercentPerRound = growth;
            if (float.TryParse(p[9], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float turn)) ctx.turnTimeLimitSeconds = turn;
        }

        void ApplyRosterNamesToSession()
        {
            if (director == null || director.Session == null) return;
            for (int i = 0; i < RosterList.Count; i++)
            {
                var info = RosterList[i];
                var player = director.Session.PlayerAt(info.seat);
                if (player == null || string.IsNullOrWhiteSpace(info.name)) continue;
                player.displayName = info.name.Trim();
                if (director.characters != null && info.seat >= 0 && info.seat < director.characters.Length &&
                    director.characters[info.seat] != null)
                    director.characters[info.seat].displayName = player.displayName;
            }
        }

        // ============================================================ 花名册
        void AddSeat(SeatInfo info)
        {
            for (int i = 0; i < RosterList.Count; i++)
            {
                if (RosterList[i].seat == info.seat) { RosterList[i] = info; return; }
            }
            RosterList.Add(info);
        }

        void RefreshRoster()
        {
            // 重建列表，避免离开的真人仍显示为已入座。
            RosterList.Clear();
            if (mode == GameMode.LanHost)
                AddSeat(new SeatInfo(0, localPlayerName, SkinIdOfLocal()));
            foreach (var kv in _peerInfo) AddSeat(kv.Value);
            RosterList.Sort((a, b) => a.seat.CompareTo(b.seat));
            BroadcastRosterAll();
        }

        /// <summary>广播完整座位表：seat|name|skin，多条用 ';' 分隔。</summary>
        void BroadcastRosterAll()
        {
            if (mode != GameMode.LanHost || _transport == null) return;

            var sb = new StringBuilder();
            for (int i = 0; i < RosterList.Count; i++)
            {
                if (i > 0) sb.Append(';');
                var s = RosterList[i];
                sb.Append(s.seat).Append(Sep).Append(s.name).Append(Sep).Append(s.skinId);
            }

            _transport.Broadcast(NetProtocol.Encode(NetMessageType.RosterAll, sb.ToString()));
        }

        void ApplyRosterAll(string payload)
        {
            RosterList.Clear();
            if (string.IsNullOrEmpty(payload)) return;

            var entries = payload.Split(';');
            for (int i = 0; i < entries.Length; i++)
            {
                var p = entries[i].Split(Sep);
                if (p.Length < 3 || !int.TryParse(p[0], out int seat)) continue;
                AddSeat(new SeatInfo(seat, p[1], p[2]));
            }
            RosterList.Sort((a, b) => a.seat.CompareTo(b.seat));
        }

        // ============================================================ 主循环
        void Update()
        {
            _transport?.Pump();

            // 兜底快照：TCP 本身可靠，这个定时器只防「事件漏发」这类逻辑疏漏
            if (mode != GameMode.LanHost || !sessionStarted || _peerSeat.Count == 0) return;

            _snapshotTimer += Time.deltaTime;
            if (_snapshotTimer < snapshotInterval) return;
            _snapshotTimer = 0f;
            BroadcastSnapshot();
        }
    }
}
