using System;
using System.Collections.Generic;
using BombTavern.AI;
using BombTavern.Config;
using BombTavern.Core;
using LiarsBar;
using UnityEngine;

namespace BombTavern.Runtime
{
    public enum GameMode
    {
        [Tooltip("单人：本地玩家 + AI 补位")]
        SinglePlayer,
        [Tooltip("局域网主机：本机为真人玩家并接受客户端连接")]
        LanHost,
        [Tooltip("局域网客户端：连接到主机")]
        LanClient,
    }

    /// <summary>
    /// 对局驱动。负责创建 GameSession、驱动 AI、把玩家输入转成指令、把事件分发给表现层。
    /// 联机模式下指令需要经传输层同步（见 BombTavern.Net）。
    /// </summary>
    [DisallowMultipleComponent]
    public class GameDirector : MonoBehaviour
    {
        [Header("配置")]
        [Tooltip("对局配置资产。为空时使用内置保守默认值（仅用于快速试玩）。")]
        public GameConfig config;

        [Tooltip("运行模式。")]
        public GameMode mode = GameMode.SinglePlayer;

        [Tooltip("固定随机种子。0 表示每次开局随机（联机时由主机生成并下发）。")]
        public ulong fixedSeed = 0UL;

        [Header("场景绑定")]
        [Tooltip("所有玩家共用的那块大屏（config.sharedBoard = true 时使用）。")]
        public TabletScreen sharedTablet;

        [Tooltip("每名玩家各自面前的一块面板，按座位顺序。共用盘面时它们全部指向同一份盘面数据。")]
        public TabletScreen[] tablets = new TabletScreen[GameSession.MaxSeats];

        [Tooltip("皮肤目录，用于给角色换造型。留空则回退到 SessionContext 上的目录。")]
        public SkinCatalog skinCatalog;

        [Tooltip("每名玩家对应的炸弹角色，按座位顺序。")]
        public BombCharacter[] characters = new BombCharacter[GameSession.MaxSeats];

        [Header("联机")]
        [Tooltip("局域网端口。为空时取 config.defaultPort。")]
        public int lanPort = 0;

        [Tooltip("本地玩家占用的座位号。联机客户端由主机下发。")]
        public int localSeat = 0;

        [Tooltip("第一人称下隐藏本地玩家的炸弹角色，避免相机穿模看到自己体内。")]
        public bool hideLocalCharacter = true;

        // ---- 运行时状态
        public GameSession Session { get; private set; }
        public bool IsReady => Session != null && Session.Players != null;

        /// <summary>对局事件广播（表现层与 HUD 订阅）。</summary>
        public event Action<GameEvent> Event;

        /// <summary>对局初始化完成。</summary>
        public event Action<GameSession> SessionReady;

        /// <summary>
        /// 任意来源（本地玩家 / AI / 远端）的指令被权威接受后触发。
        /// 联机主机订阅它来广播指令，保证 AI 的动作也能同步给客户端。
        /// </summary>
        public event Action<GameCommand> CommandApplied;

        readonly Dictionary<int, BotBrain> _brains = new Dictionary<int, BotBrain>();
        float _botTimer;
        int _botRejectStreak;
        float _botDelayForThisAction;
        int _lastBotSeat = -1;
        float _tabletRefreshTimer;
        float _turnStartedAt;
        int _timedTurn = -1;

        public float TurnSecondsRemaining
        {
            get
            {
                float limit = config != null ? config.turnTimeLimitSeconds : 0f;
                if (Session == null || Session.IsOver || limit <= 0f) return -1f;
                return Mathf.Max(0f, limit - (Time.unscaledTime - _turnStartedAt));
            }
        }

        [Header("节奏")]
        [Tooltip("面板颜色的兜底刷新间隔（秒）。即使漏掉某条事件，颜色也会在这个时间内收敛回真实状态。")]
        [Range(0.1f, 3f)] public float tabletRefreshInterval = 0.4f;

        [Tooltip("AI 每步思考延迟的随机浮动上限（秒）。基准值取 config.botThinkDelaySeconds。")]
        [Range(0f, 5f)] public float botThinkDelayJitter = 1.0f;

        /// <summary>联机模式下由传输层注入：把本地指令发出去并等待确认。委托不参与序列化。</summary>
        [NonSerialized] public Func<GameCommand, bool> RemoteSubmit;

        /// <summary>
        /// 客户端权威模式：本端完全不跑模拟，所有状态由主机快照覆盖。
        /// 开启后不再驱动任何 AI —— 否则客户端会自己把局面往前推，和主机打架。
        /// </summary>
        [NonSerialized] public bool clientAuthorityMode;

        void Start()
        {
            // 清掉上一局残留的输入（尤其是界面注入的道具槽队列），
            // 否则重开一局时会莫名其妙自动用掉一个道具。
            GameInput.Reset();

            if (config == null)
            {
                Debug.LogError("[炸弹酒馆] GameDirector 未绑定 GameConfig，无法开局。");
                return;
            }

            switch (mode)
            {
                case GameMode.SinglePlayer:
                    StartSinglePlayer();
                    break;

                case GameMode.LanHost:
                    // 主机不自动开局，等房主在大厅点「开始游戏」
                    break;

                case GameMode.LanClient:
                    // 客户端等待主机下发种子与开局消息
                    break;
            }
        }

        // ============================================================ 开局
        public void StartSinglePlayer()
        {
            ulong seed = fixedSeed != 0UL ? fixedSeed : MakeRandomSeed();
            StartSession(seed);
        }

        // ============================================================ 开局前观战
        /// <summary>
        /// 开局前状态：主菜单正盖在真场景上，四个座位交给 AI 自己打一局当背景演示。
        /// 这个状态下 HUD 不画（有菜单了），真人输入也不接受。
        /// </summary>
        [NonSerialized] public bool pregameMode;

        public bool IsPregame => pregameMode;

        // ============================================================ 角色显示
        /// <summary>
        /// 联机大厅阶段：只有这些座位渲染角色，等人进来再逐个显示。
        /// null 表示不做占座限制（单机 / 开局前菜单）。
        /// </summary>
        [NonSerialized] public HashSet<int> lobbyOccupiedSeats;

        /// <summary>是否处于「等人进来」的大厅阶段（只显示已就座的座位）。</summary>
        [NonSerialized] public bool lobbyMode;

        [NonSerialized] int _spectatorViewSeat = -1;
        public int SpectatorViewSeat => _spectatorViewSeat;

        /// <summary>
        /// 统一刷新「每个座位该不该显示角色」。三个阶段要求不同：
        ///
        ///   开局前菜单 —— 四个角色都要看得见。原来这里无条件隐藏本地角色，
        ///                 于是玩家在菜单里永远看不到自己选的那只，以为模型没做出来。
        ///   联机大厅   —— 只显示已经坐了人的座位，来一个人亮一个。
        ///   对局中     —— 隐藏本地角色，否则第一人称相机会拍到自己体内。
        /// </summary>
        public void RefreshCharacterVisibility()
        {
            if (characters == null) return;

            bool inGame = Session != null && !pregameMode && !lobbyMode;
            bool hideMine = hideLocalCharacter && inGame;

            for (int i = 0; i < characters.Length; i++)
            {
                var c = characters[i];
                if (c == null) continue;

                bool show = true;
                if (hideMine && (i == localSeat || i == _spectatorViewSeat)) show = false;
                if (lobbyMode && lobbyOccupiedSeats != null && !lobbyOccupiedSeats.Contains(i)) show = false;

                if (c.gameObject.activeSelf != show) c.gameObject.SetActive(show);
            }
        }

        /// <summary>
        /// 进入/退出一局真实对局时调用，重新判定本地角色该不该藏。
        /// 隐藏规则依赖「是不是在对局中」，所以对局开始时必须刷一次。
        /// </summary>
        public void SetInGameCharacterVisibility() => RefreshCharacterVisibility();

        /// <summary>
        /// 开一局「演示对局」：四个座位全交给 AI，纯做菜单背景。
        /// 复用 autoPlayLocalSeat 是刻意的 —— 它就是「把本地座位交出去」这个开关，
        /// 不需要为演示再引入第二套代码路径。
        /// </summary>
        public void StartDemoSession()
        {
            if (config == null) return;

            bool savedAuto = config.autoPlayLocalSeat;
            config.autoPlayLocalSeat = true;

            // pregameMode 必须在 StartSession 之前置位：
            // BindView 里的角色可见性判断要读它，晚一步就会把本地角色藏掉，
            // 玩家在菜单里就看不到自己那只。
            pregameMode = true;

            StartSession(MakeRandomSeed(), GameSession.MaxSeats);

            config.autoPlayLocalSeat = savedAuto;

            // 演示局里本地座位也是 AI，所以要补上它的 brain
            if (!_brains.ContainsKey(localSeat))
                _brains[localSeat] = new BotBrain(config, localSeat, Session.Seed);
        }

        /// <summary>
        /// 真人接管：结束演示局，用干净的种子重开一局。
        /// 演示局只是背景，它留下的任何局面都不该被玩家继承。
        /// </summary>
        public void BeginRealGame(ulong seed = 0UL)
        {
            pregameMode = false;
            if (seed == 0UL) seed = fixedSeed != 0UL ? fixedSeed : MakeRandomSeed();
            StartSession(seed);
        }

        /// <summary>
        /// 用指定种子开局（联机时主机生成后通过传输层下发）。
        /// humanCountOverride &gt; 0 时覆盖配置里的真人数量，其余座位交给 AI。
        /// </summary>
        public void StartSession(ulong seed, int humanCountOverride = -1)
        {
            ApplySessionRuleOverrides();
            _spectatorViewSeat = -1;
            Session = new GameSession();
            Session.EventRaised += OnSessionEvent;
            Session.Initialize(config, seed, humanCountOverride);

            var ctx = SessionContext.Instance;
            var local = Session.PlayerAt(localSeat);
            if (ctx != null && local != null && !string.IsNullOrWhiteSpace(ctx.playerName))
                local.displayName = ctx.playerName.Trim();

            _timedTurn = Session.TurnIndex;
            _turnStartedAt = Time.unscaledTime;

            BuildBrains();
            BindView();
            SessionReady?.Invoke(Session);
        }

        void ApplySessionRuleOverrides()
        {
            var ctx = SessionContext.Instance;
            if (ctx == null || config == null) return;

            // 使用运行时副本，避免在编辑器测试时改脏 ScriptableObject 资产。
            if (!config.name.EndsWith("_Runtime"))
            {
                config = Instantiate(config);
                config.name += "_Runtime";
                if (config.difficulty != null) config.difficulty = Instantiate(config.difficulty);
                if (config.driftProfile != null) config.driftProfile = Instantiate(config.driftProfile);
            }

            if (config.difficulty != null)
            {
                config.difficulty.columns = Mathf.Clamp(ctx.boardColumns, 6, 24);
                config.difficulty.rows = Mathf.Clamp(ctx.boardRows, 6, 18);
                int cells = config.difficulty.columns * config.difficulty.rows;
                config.difficulty.baseRisk = Mathf.Clamp(ctx.bombCount / (float)Mathf.Max(1, cells), 0.02f, 0.65f);
                config.difficulty.displayName = $"自定义 {config.difficulty.columns}×{config.difficulty.rows}";
            }
            if (config.driftProfile != null)
            {
                config.driftProfile.baseDriftMagnitude = Mathf.Clamp(ctx.driftPercent / 100f, 0f, 0.3f);
                config.driftProfile.roundEscalation = 1f + Mathf.Clamp(ctx.driftGrowthPercentPerRound / 100f, 0f, 0.2f);
            }
            config.turnTimeLimitSeconds = Mathf.Clamp(ctx.turnTimeLimitSeconds, 10f, 120f);
        }

        /// <summary>联机时客户端掉线：把该座位改交给 AI，避免整局卡死。</summary>
        public void ConvertSeatToBot(int seat)
        {
            var p = Session?.PlayerAt(seat);
            if (p == null) return;
            p.isBot = true;
            if (!_brains.ContainsKey(seat))
                _brains[seat] = new BotBrain(config, seat, Session.Seed);
            Debug.Log($"[炸弹酒馆] 座位 {seat} 已移交给 AI。");
        }

        /// <summary>
        /// 联机重连：把之前因断线移交给 AI 的座位交还给真人。
        /// 客户端重连后会收到一份完整快照，所以这里只需要把大脑摘掉。
        /// </summary>
        public void TakeBackSeatFromBot(int seat)
        {
            var p = Session?.PlayerAt(seat);
            if (p == null) return;

            p.isBot = false;
            _brains.Remove(seat);
            Debug.Log($"[炸弹酒馆] 座位 {seat} 已交还真人。");
        }

        ulong MakeRandomSeed()
        {
            // 仅用于本地开局撒种；联机时必须由主机统一下发以保证各端一致
            ulong t = (ulong)DateTime.UtcNow.Ticks;
            return t ^ 0xD1B54A32D192ED03UL;
        }

        void BuildBrains()
        {
            _brains.Clear();

            // 客户端不跑 AI：所有座位的 AI 都在主机侧。
            // 唯一例外是「本机托管」——用它自己的 AI 决定操作，但操作会走网络提交。
            if (clientAuthorityMode)
            {
                if (config != null && config.autoPlayLocalSeat && Session.PlayerAt(localSeat) != null)
                    _brains[localSeat] = new BotBrain(config, localSeat, Session.Seed);
                return;
            }

            for (int i = 0; i < Session.Players.Length; i++)
            {
                var p = Session.Players[i];
                if (p.isBot || ShouldAutoDrive(i)) _brains[i] = new BotBrain(config, i, Session.Seed);
            }
        }

        /// <summary>该座位是否由 AI 驱动（机器人座位，或开启了自动代打的本地座位）。</summary>
        bool ShouldAutoDrive(int seat)
        {
            if (Session == null || Session.PlayerAt(seat) == null) return false;
            if (Session.PlayerAt(seat).isBot) return true;
            return config != null && config.autoPlayLocalSeat && seat == localSeat;
        }

        /// <summary>开启/关闭本地座位自动代打（运行时切换，便于观战与自动化验证）。</summary>
        public void SetAutoPlayLocalSeat(bool enabled)
        {
            if (config != null) config.autoPlayLocalSeat = enabled;
            else return;

            if (Session == null) return;
            if (enabled && !_brains.ContainsKey(localSeat))
                _brains[localSeat] = new BotBrain(config, localSeat, Session.Seed);
            else if (!enabled)
                _brains.Remove(localSeat);
        }

        /// <summary>本地座位当前是否由 AI 托管（HUD 会把它显示出来）。</summary>
        public bool IsLocalAutoPlayed => config != null && config.autoPlayLocalSeat;

        /// <summary>
        /// 真人随时抢回控制权。存在的意义：
        /// 自动化自检会打开托管（命令行 -btAutoplay），如果不在界面上标出来、
        /// 也不给一键收回，玩家就会以为「这游戏自己在动」。
        /// </summary>
        public void SetLocalAutoPlay(bool on)
        {
            SetAutoPlayLocalSeat(on);
            if (on) return;

            // 刚关掉托管时，清掉为 AI 计时的残留，让玩家立刻能操作，
            // 而不是还要干等一个"AI 思考时间"。
            _botTimer = 0f;
            _botRejectStreak = 0;
        }

        /// <summary>
        /// 在「真人操作 / AI 托管」之间切换。
        /// 输入来源走 GameInput：桌面端是 H 键，触摸端由 HUD 上的按钮注入。
        /// </summary>
        void HandleLocalControlKeys()
        {
            if (config == null) return;
            if (!GameInput.AutoPlayToggleDown) return;

            SetLocalAutoPlay(!IsLocalAutoPlayed);
            Debug.Log($"[对局] 本地座位 AI 托管 = {IsLocalAutoPlayed}");
        }

        void BindView()
        {
            bool shared = config != null && config.sharedBoard;

            for (int i = 0; i < Session.Players.Length; i++)
            {
                var p = Session.Players[i];

                // 每人都有一块面板；共用盘面时所有面板指向同一份数据，
                // 所以任何人翻开一格，四面八方的屏幕上都会同时更新。
                var tablet = tablets != null && i < tablets.Length ? tablets[i] : null;
                if (tablet != null)
                {
                    // 本地视图策略：每台机器只在自己座位的面板上
                    // 画「其他人的选择框」，不把这个辅助表现当成联机世界物体。
                    tablet.markerScope = SeatMarkerScope.ViewerPanelOnly;
                    if (shared) tablet.BindShared(this, Session.SharedBoard);
                    else tablet.BindPrivate(this, p);
                }

                if (characters != null && i < characters.Length && characters[i] != null)
                    characters[i].displayName = p.displayName;
            }

            RefreshCharacterVisibility();

            // 没有面板的座位（配置座位数变少时）隐藏掉
            if (tablets != null)
            {
                for (int i = Session.Players.Length; i < tablets.Length; i++)
                {
                    if (tablets[i] == null) continue;
                    tablets[i].gameObject.SetActive(false);
                }
            }

            ApplyCharacterSkins();
            AttachActionAnimators();
            SeatPlayerRig();
            AttachFirstPersonPropPresenter();
            ClearHoverPublish();
        }

        // ============================================================ 角色外观
        // 四名固定角色（黑色球形炸弹 / 红色成捆炸弹 / 棕色二踢脚 / 灰色炮弹），
        // 每局按种子确定性洗牌后一个座位一个，保证四人不重样且各端一致。
        [NonSerialized] public SkinDefinition[] seatSkins = new SkinDefinition[GameSession.MaxSeats];

        void ApplyCharacterSkins()
        {
            if (characters == null) return;

            var ctx = SessionContext.Instance;
            var catalog = skinCatalog != null ? skinCatalog : (ctx != null ? ctx.skinCatalog : null);
            if (catalog == null || catalog.skins == null || catalog.skins.Count == 0) return;

            var order = BuildSeatSkinOrder(catalog.skins.Count);
            _seatOrder = order;

            for (int seat = 0; seat < characters.Length; seat++)
            {
                var c = characters[seat];
                if (c == null) continue;

                var skin = catalog.skins[order[seat % order.Length]];
                if (skin == null) continue;

                BombVisualFactory.ApplySkin(c.gameObject, skin);
                if (seat < seatSkins.Length) seatSkins[seat] = skin;
                c.accentColor = ColorOfSeat(seat);

                // 昵称属于玩家，不属于皮肤。角色只换外观，绝不能覆盖联网花名册。
                var p = Session != null ? Session.PlayerAt(seat) : null;
                c.displayName = p != null ? p.displayName : $"座位{seat + 1}";
                ApplyHandSeatColor(c, ColorOfSeat(seat));
            }
        }

        static void ApplyHandSeatColor(BombCharacter character, Color color)
        {
            var rigged = character != null ? character.GetComponentInChildren<RiggedBombView>(true) : null;
            var hand = rigged != null ? rigged.RightHand : null;
            if (hand == null) return;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in hand.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(block);
            }
        }

        [NonSerialized] int[] _seatOrder;

        /// <summary>
        /// 决定每个座位穿哪只角色。
        ///
        /// 两步：
        ///   1) 把玩家在菜单里选中的角色换到本地座位 —— 这就是「换装」
        ///   2) 其余座位用「对局种子」派生的独立随机流洗牌
        ///
        /// 独立流的意义：洗牌不消耗对局随机数，不会破坏联机锁步一致性。
        /// 洗牌时跳过本地座位，否则会把刚换好的角色又洗走。
        /// </summary>
        int[] BuildSeatSkinOrder(int count)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;

            int preferred = PreferredSkinIndex(count);
            if (preferred >= 0 && localSeat >= 0 && localSeat < count)
                (order[localSeat], order[preferred]) = (order[preferred], order[localSeat]);

            ulong seed = (Session != null ? Session.Seed : 0UL) ^ 0x9E3779B97F4A7C15UL;
            var rng = new DeterministicRandom(seed);

            for (int i = count - 1; i > 0; i--)
            {
                if (i == localSeat) continue;          // 本地座位已定，不参与洗牌

                int j = rng.NextInt(0, i + 1);
                if (j == localSeat) j = i;             // 抽到本地座位就退化成不交换
                if (j == i) continue;
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }

        /// <summary>玩家在菜单里选的角色在皮肤目录里的下标；没选或找不到返回 -1。</summary>
        int PreferredSkinIndex(int count)
        {
            var ctx = SessionContext.Instance;
            var catalog = skinCatalog != null ? skinCatalog : (ctx != null ? ctx.skinCatalog : null);
            if (ctx == null || catalog == null || catalog.skins == null) return -1;
            if (string.IsNullOrEmpty(ctx.skinId)) return -1;

            for (int i = 0; i < catalog.skins.Count && i < count; i++)
            {
                var s = catalog.skins[i];
                if (s != null && s.skinId == ctx.skinId) return i;
            }
            return -1;
        }

        /// <summary>
        /// 把第一人称装置摆到「本地座位」的位置上并朝向桌心。
        ///
        /// 场景装配时它按 0 号座位摆放（主机就是 0 号，没问题）。
        /// 但联机客户端入座后座位号是主机分配的（可能是 1/2/3），
        /// 如果不重新摆一次，客户端会**站在主机的座位上**：看到的是主机那块面板、
        /// 自己的面板在桌子对面不显示、而且整个人卡在主机角色的身体里面。
        /// </summary>
        void SeatPlayerRig()
            => PositionPlayerRigAtSeat(localSeat);

        void PositionPlayerRigAtSeat(int requestedSeat)
        {
            var fps = FindObjectOfType<FirstPersonController>();
            if (fps == null || config == null) return;

            var angles = config.seatAnglesDegrees;
            if (angles == null || angles.Length == 0) return;

            float radius = config.seatStandRadius > 0.1f ? config.seatStandRadius : 1.62f;

            int seat = Mathf.Max(0, requestedSeat);
            float ang = angles[seat % angles.Length] * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));

            fps.Teleport(dir * radius + Vector3.up * 0.05f, Vector3.zero);
        }

        /// <summary>幽灵本地切换到某个存活玩家的座位视角，不参与联机同步。</summary>
        public bool SetSpectatorViewSeat(int seat)
        {
            var p = Session != null ? Session.PlayerAt(seat) : null;
            if (p == null || p.eliminated || seat == localSeat) return false;

            _spectatorViewSeat = seat;
            PositionPlayerRigAtSeat(seat);
            RefreshCharacterVisibility();
            return true;
        }

        public void ClearSpectatorView()
        {
            if (_spectatorViewSeat < 0) return;
            _spectatorViewSeat = -1;
            SeatPlayerRig();
            RefreshCharacterVisibility();
        }

        /// <summary>
        /// 按本机 localSeat 重新套用第一人称视图隔离。
        /// 主机与每个客户端的场景实例相互独立：本机只隐藏自己的完整身体，
        /// 其他三人保留完整模型，自己则使用相机下的手部模型。
        /// </summary>
        public void RefreshLocalPerspective()
        {
            RefreshCharacterVisibility();
            SeatPlayerRig();
            if (Session != null) AttachFirstPersonPropPresenter();
        }

        /// <summary>
        /// 给每个角色挂上动作状态机。
        /// 必须在 ApplyCharacterSkins 之后调用：换皮肤会重建 Visual 节点，
        /// 动画器需要重新取一次手臂/嘴巴的引用，否则动画会指向已销毁的物体。
        /// </summary>
        void AttachActionAnimators()
        {
            if (characters == null) return;

            for (int seat = 0; seat < characters.Length; seat++)
            {
                var c = characters[seat];
                if (c == null) continue;

                var anim = c.GetComponent<BombActionAnimator>();
                if (anim == null) anim = c.gameObject.AddComponent<BombActionAnimator>();

                anim.seatIndex = seat;

                // 本地座位**永远**播道具动作：自己用了什么自己清楚，谈不上泄密；
                // 而且看不到自己的动作，玩家会以为「道具功能没做」。
                // 对手的动作仍由配置决定 —— 对他们来说，抬手本身就是情报。
                anim.playSecretActions =
                    seat == localSeat || config == null || config.itemUseAnimationVisible;
                anim.RefreshRefs();
                anim.Hook(this);

                AttachHandPropPresenter(c, seat);

                // 护盾光环：有护盾层数时绕角色转一圈，被挡下时整圈闪一下。
                // 护盾原本只是玩家卡上的一个数字，对手完全看不见 —— 这在博弈上是缺失的信息。
                var shield = c.GetComponent<ShieldVisual>();
                if (shield == null) shield = c.gameObject.AddComponent<ShieldVisual>();
                shield.seatIndex = seat;
                shield.director = this;
            }
        }

        /// <summary>
        /// 第三人称的道具表现：把道具模型挂在角色的右手上。
        /// 换皮肤会重建整个 Visual 子树，所以这个方法必须在 ApplyCharacterSkins 之后跑，
        /// 并且要按「当次新建的手部节点」重新挂组件。
        /// </summary>
        void AttachHandPropPresenter(BombCharacter character, int seat)
        {
            // 带骨骼模型的右手不在旧版程序化层级（Visual/BodyRoot/HandR）里。
            // 优先取 RiggedBombView 暴露的真实骨骼，否则道具会错误挂到角色根节点。
            Transform hand = null;
            var rigged = character.GetComponentInChildren<RiggedBombView>(true);
            if (rigged != null) hand = rigged.RightHand;
            if (hand == null) hand = character.transform.Find("Visual/BodyRoot/HandR");
            if (hand == null) hand = character.transform.Find("Visual/BodyRoot");
            if (hand == null) hand = character.transform;

            var host = hand.gameObject;
            var presenter = host.GetComponent<ItemPropPresenter>();
            if (presenter == null) presenter = host.AddComponent<ItemPropPresenter>();

            presenter.view = ItemPropPresenter.ViewMode.ThirdPersonHand;
            presenter.seatIndex = seat;
            presenter.Hook(this);
        }

        /// <summary>
        /// 第一人称的右手与道具视角模型。右手始终可见并跟随视野，
        /// 道具挂在手的持握点上，避免过去「只有道具凭空飘在镜头前」的问题。
        /// </summary>
        void AttachFirstPersonPropPresenter()
        {
            var fps = FindObjectOfType<FirstPersonController>();
            if (fps == null || fps.viewCamera == null) return;

            var camT = fps.viewCamera.transform;
            var handView = camT.GetComponent<FirstPersonHandView>();
            if (handView == null) handView = camT.gameObject.AddComponent<FirstPersonHandView>();
            handView.Hook(this, localSeat, ColorOfSeat(localSeat));

            var propHost = handView.PropAnchor != null ? handView.PropAnchor.gameObject : camT.gameObject;
            var presenter = propHost.GetComponent<ItemPropPresenter>();
            if (presenter == null) presenter = propHost.AddComponent<ItemPropPresenter>();

            presenter.view = ItemPropPresenter.ViewMode.FirstPersonCamera;
            presenter.seatIndex = localSeat;
            // 挂点已经位于手心附近，这里的偏移只负责「从掌下掏出并举稳」。
            presenter.fpRestOffset = new Vector3(0f, -0.18f, 0.02f);
            presenter.fpHoldOffset = new Vector3(0f, 0.02f, 0.05f);
            presenter.fpScale = 0.92f;
            presenter.Hook(this);
        }

        /// <summary>取某座位对应的角色皮肤（可能为空）。</summary>
        public SkinDefinition SkinOf(int seat)
            => seatSkins != null && seat >= 0 && seat < seatSkins.Length ? seatSkins[seat] : null;

        /// <summary>取某座位的标识色：HUD 卡片、格子印记、角色手套统一使用它。</summary>
        public Color ColorOfSeat(int seat)
        {
            switch ((seat % 4 + 4) % 4)
            {
                case 0: return new Color(0.95f, 0.24f, 0.22f); // 红
                case 1: return new Color(0.20f, 0.55f, 1.00f); // 蓝
                case 2: return new Color(0.20f, 0.90f, 0.55f); // 青绿
                default: return new Color(1.00f, 0.72f, 0.18f); // 金
            }
        }

        // ============================================================ 悬停公开
        float _hoverTimer;
        int _lastHoverX = -1;
        int _lastHoverY = -1;

        /// <summary>
        /// 本地玩家把鼠标移到某格时调用：把悬停位置公开给其他玩家（按配置节流）。
        /// 这是唯一会自动共享的"操作意图"，使用道具不会产生任何公开信息。
        /// </summary>
        public void PublishHover(int x, int y)
        {
            if (Session == null || config == null || !config.publishHover) return;
            if (Session.IsOver || Session.CurrentSeat != localSeat) return;
            if (x == _lastHoverX && y == _lastHoverY) return;

            if (config.hoverSyncInterval > 0f && Time.unscaledTime - _hoverTimer < config.hoverSyncInterval)
                return;

            _hoverTimer = Time.unscaledTime;
            _lastHoverX = x;
            _lastHoverY = y;
            Submit(GameCommand.Hover(localSeat, x, y, Session.TurnIndex));
        }

        public void ClearHoverPublish()
        {
            _lastHoverX = -1;
            _lastHoverY = -1;
            _hoverTimer = 0f;
        }

        // ============================================================ 输入入口
        /// <summary>本地玩家提交一条指令。联机客户端会转发给主机，单机直接结算。</summary>
        public bool Submit(GameCommand cmd)
        {
            if (Session == null) return false;
            if (RemoteSubmit != null) return RemoteSubmit(cmd);
            return ApplyLocal(cmd);
        }

        /// <summary>权威应用：主机自己、AI、以及收到远端指令时都走这里。</summary>
        public bool ApplyAuthoritative(GameCommand cmd) => ApplyLocal(cmd);

        /// <summary>最近一次被拒绝的原因（联机时回给提交方）。</summary>
        public string LastRejectReason => Session != null ? Session.LastRejectReason : string.Empty;

        /// <summary>立刻重绘所有面板。客户端应用完快照后调用，避免看到上一帧的颜色。</summary>
        public void RefreshTabletsNow() => RefreshAllTablets();

        /// <summary>
        /// 把远端（主机）下发的对局事件注入本地表现层。
        /// 走的是和本地事件完全相同的通路，所以 HUD 事件流、状态机横幅、
        /// 角色爆炸与动作动画在客户端上和主机表现一致，不需要各写一套。
        /// </summary>
        public void InjectNetworkEvent(GameEvent e) => OnSessionEvent(e);

        bool ApplyLocal(GameCommand cmd)
        {
            if (Session == null) return false;
            bool ok = Session.TryApply(cmd);
            if (ok) CommandApplied?.Invoke(cmd);
            return ok;
        }

        // ============================================================ AI 驱动
        void Update()
        {
            HandleLocalControlKeys();

            if (Session == null) return;

            UpdateTurnTimeout();

            RefreshTabletsPeriodically();
            if (Session.IsOver) return;

            // 客户端不自推进局面，一切等主机快照；
            // 但若开了本机托管，仍然要决定「我这一手出什么」并发给主机
            if (clientAuthorityMode) { DriveClientSeat(); return; }

            var current = Session.PlayerAt(Session.CurrentSeat);
            if (current == null || !ShouldAutoDrive(current.seatIndex)) { _botRejectStreak = 0; return; }

            // 换人出手时重新掷一次思考时间：让每个 AI 的节奏都不一样
            if (current.seatIndex != _lastBotSeat)
            {
                _lastBotSeat = current.seatIndex;
                _botTimer = 0f;
                _botDelayForThisAction = RollBotDelay(current.seatIndex);
            }

            _botTimer += Time.deltaTime;
            if (_botTimer < _botDelayForThisAction) return;
            _botTimer = 0f;
            _botDelayForThisAction = RollBotDelay(current.seatIndex);

            if (!_brains.TryGetValue(current.seatIndex, out var brain)) return;

            var cmd = brain.Decide(Session);
            bool ok = ApplyLocal(cmd);

            if (!ok)
            {
                _botRejectStreak++;
                // 防御：AI 连续给出非法指令时强制过手，避免卡死
                if (_botRejectStreak > 5)
                {
                    _botRejectStreak = 0;
                    ApplyLocal(GameCommand.End(current.seatIndex, Session.TurnIndex));
                }
            }
            else
            {
                _botRejectStreak = 0;
            }
        }

        void UpdateTurnTimeout()
        {
            if (Session == null || Session.IsOver) return;
            if (_timedTurn != Session.TurnIndex)
            {
                _timedTurn = Session.TurnIndex;
                _turnStartedAt = Time.unscaledTime;
            }

            float limit = config != null ? config.turnTimeLimitSeconds : 0f;
            if (limit <= 0f || Time.unscaledTime - _turnStartedAt < limit) return;

            // 客户端不能自行结算；只由单机/主机权威端代替超时玩家翻开一格。
            if (clientAuthorityMode) return;
            var player = Session.PlayerAt(Session.CurrentSeat);
            var board = player != null ? player.board : null;
            if (board == null) return;

            int start = Mathf.Abs(Session.TurnIndex * 17 + Session.CurrentSeat * 31) % Mathf.Max(1, board.CellCount);
            for (int step = 0; step < board.CellCount; step++)
            {
                int index = (start + step) % board.CellCount;
                var cell = board.RawCells[index];
                if (cell.revealed || cell.flagged) continue;
                int x = index % board.Columns;
                int y = index / board.Columns;
                Debug.Log($"[回合] {player.displayName} 操作超时，系统自动翻开 ({x},{y})。");
                ApplyLocal(GameCommand.Reveal(player.seatIndex, x, y, Session.TurnIndex));
                _turnStartedAt = Time.unscaledTime;
                return;
            }

            ApplyLocal(GameCommand.End(player.seatIndex, Session.TurnIndex));
        }

        /// <summary>
        /// 客户端已把一手发给主机、正在等权威响应。
        /// 不能用 TurnIndex 去重 —— 配置允许「一回合内多次行动」（比如用道具不结束回合），
        /// 按回合号去重会让客户端用掉一件道具后就再也不提交，直接死锁。
        /// </summary>
        [NonSerialized] public bool awaitingNetworkAck;
        [NonSerialized] float _ackTimer;

        [Tooltip("等主机响应的超时（秒）。超时自动放行，避免一次丢响应把客户端永久卡死。")]
        public float networkAckTimeout = 4f;

        /// <summary>主机响应到达（快照已应用）。</summary>
        public void NotifyNetworkAck()
        {
            awaitingNetworkAck = false;
            _ackTimer = 0f;
        }

        /// <summary>
        /// 联机客户端：用本机 AI 决定自己的操作，但**必须走 Submit**（发给主机），
        /// 绝不能在本地结算。同时用 TurnIndex 去重 —— 否则在快照回来之前会重复提交同一手。
        /// </summary>
        void DriveClientSeat()
        {
            if (Session == null || Session.IsOver) return;
            if (!Session.IsMyTurn(localSeat)) return;
            if (_brains.Count == 0) return;
            if (!_brains.TryGetValue(localSeat, out var brain)) return;

            // 上一手还在等主机响应，别重复提交（超时兜底见 networkAckTimeout）
            if (awaitingNetworkAck)
            {
                _ackTimer += Time.deltaTime;
                if (_ackTimer < networkAckTimeout) return;
                awaitingNetworkAck = false;
            }

            if (_lastBotSeat != localSeat)
            {
                _lastBotSeat = localSeat;
                _botTimer = 0f;
                _botDelayForThisAction = RollBotDelay(localSeat);
            }

            _botTimer += Time.deltaTime;
            if (_botTimer < _botDelayForThisAction) return;
            _botTimer = 0f;

            awaitingNetworkAck = true;
            _ackTimer = 0f;

            if (!Submit(brain.Decide(Session))) awaitingNetworkAck = false;
        }

        /// <summary>
        /// AI 这一步要"思考"多久。用 (种子, 座位, 回合序号) 做哈希得到确定但看起来随机的值：
        /// 既让 AI 像在犹豫，又不会引入非确定性，联机各端的节奏保持一致。
        /// </summary>
        float RollBotDelay(int seat)
        {
            float baseDelay = config != null ? config.botThinkDelaySeconds : 0.6f;
            float jitter = Mathf.Max(0f, botThinkDelayJitter);
            if (jitter <= 0f) return baseDelay;

            ulong h = Session.Seed ^ ((ulong)(uint)seat << 32) ^ (ulong)Session.TurnIndex;
            h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
            h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
            h ^= h >> 31;

            return baseDelay + (h % 1000u) / 1000f * jitter;
        }

        /// <summary>面板颜色兜底刷新：漏事件也不会让格子颜色停在上一个状态。</summary>
        void RefreshTabletsPeriodically()
        {
            _tabletRefreshTimer += Time.deltaTime;
            if (_tabletRefreshTimer < tabletRefreshInterval) return;

            _tabletRefreshTimer = 0f;
            RefreshAllTablets();
        }

        // ============================================================ 事件分发
        void OnSessionEvent(GameEvent e)
        {
            Event?.Invoke(e);

            switch (e.type)
            {
                case GameEventType.CellRevealed:
                case GameEventType.CellProbed:
                case GameEventType.CellRiskModified:
                case GameEventType.AreaRiskModified:
                case GameEventType.RowScanned:
                case GameEventType.AreaScanned:
                case GameEventType.CellFlagged:
                {
                    // 一条事件可能连带改变多格（插旗影响同格、扫描一整行/区域、
                    // 命中后 clearRevealedOnHit 会整盘回退），所以直接全刷最稳。
                    // 120 格 × 4 块面板的 PropertyBlock 写入开销可以忽略。
                    RefreshAllTablets();
                    break;
                }

                case GameEventType.CellHit:
                    PlayHit(e);
                    RefreshAllTablets();
                    break;

                case GameEventType.ShieldConsumed:
                    RefreshAllTablets();
                    break;

                case GameEventType.PlayerEliminated:
                    PlayEliminated(e);
                    RefreshAllTablets();
                    break;

                case GameEventType.DriftApplied:
                    // 漂移只改真实值，显示值不变，因此无需重绘
                    break;

                case GameEventType.TurnBegan:
                case GameEventType.ItemGained:
                case GameEventType.ItemUsed:
                    RefreshAllTablets();
                    break;
            }
        }

        public TabletScreen TabletOf(int seatIndex)
        {
            // 优先返回该座位自己面前那块面板。
            // 共用盘面时四块面板都指向同一份数据，所以「他的面板」就是他座位那块，
            // 这样增量刷新才会落在玩家真正看得见的那块屏幕上。
            if (tablets != null && seatIndex >= 0 && seatIndex < tablets.Length && tablets[seatIndex] != null)
                return tablets[seatIndex];

            return config != null && config.sharedBoard ? sharedTablet : null;
        }

        /// <summary>
        /// 刷新所有面板。这里**不能**因为共用盘面就提前返回：
        /// 场景里是四块独立面板共读一份数据，只刷 sharedTablet 会导致
        /// 别人翻开的格子在本地那块屏幕上永远不变黑。
        /// </summary>
        void RefreshAllTablets()
        {
            if (tablets != null)
            {
                for (int i = 0; i < tablets.Length; i++)
                    if (tablets[i] != null) tablets[i].RefreshAll();
            }

            // 如果场景另外摆了一块共用大屏，它也要刷（避免与 tablets 重复刷同一块）
            if (sharedTablet != null && !ContainsTablet(sharedTablet))
                sharedTablet.RefreshAll();
        }

        bool ContainsTablet(TabletScreen t)
        {
            if (tablets == null) return false;
            for (int i = 0; i < tablets.Length; i++)
                if (tablets[i] == t) return true;
            return false;
        }

        void PlayHit(GameEvent e)
        {
            if (characters == null || e.seatIndex < 0 || e.seatIndex >= characters.Length) return;
            var c = characters[e.seatIndex];
            if (c != null) c.Explode();
        }

        void PlayEliminated(GameEvent e)
        {
            if (characters == null || e.seatIndex < 0 || e.seatIndex >= characters.Length) return;
            var c = characters[e.seatIndex];
            if (c == null) return;
            // 出局：整体压扁并变色，作为"炸弹报废"的表现
            var t = c.transform;
            t.localScale = new Vector3(t.localScale.x, t.localScale.y * 0.55f, t.localScale.z);
        }
    }
}
