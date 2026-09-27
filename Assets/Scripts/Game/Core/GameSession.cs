using System;
using System.Collections.Generic;
using BombTavern.Config;
using UnityEngine;

namespace BombTavern.Core
{
    /// <summary>
    /// 对局核心。完全确定性的状态机：
    /// 相同 (配置, 种子, 指令序列) 必然得到相同结果，因此联机只需同步指令。
    /// 表现层与 AI 只通过 TryApply / 事件 / 只读查询与它交互。
    /// </summary>
    public class GameSession
    {
        /// <summary>桌子最大座位数（结构常量，与 GameConfig.totalSeats 的取值范围一致）。</summary>
        public const int MaxSeats = 4;

        const float FallbackFindChance = 0.15f;

        public GameConfig Config { get; private set; }
        public ulong Seed { get; private set; }
        public PlayerState[] Players { get; private set; }

        public int CurrentSeat { get; private set; }
        public int TurnIndex { get; private set; }
        public int RoundIndex { get; private set; }

        public bool IsOver { get; private set; }
        public int WinnerSeat { get; private set; } = -1;
        public GameOverReason OverReason { get; private set; } = GameOverReason.None;

        /// <summary>共用盘面（sharedBoard = true 时所有人共用的那一块）。</summary>
        public RiskBoard SharedBoard { get; private set; }

        /// <summary>当前被公开的悬停位置（-1 表示无）。</summary>
        public int HoverSeat { get; private set; } = -1;
        public int HoverX { get; private set; } = -1;
        public int HoverY { get; private set; } = -1;

        /// <summary>共用盘面模式下的全局冻结回合数。</summary>
        int _sharedFreezeTurns;

        public event Action<GameEvent> EventRaised;

        readonly List<GameEvent> _log = new List<GameEvent>();
        public IReadOnlyList<GameEvent> Log => _log;

        DeterministicRandom _rng;
        bool _skipNextTurn;
        int _skipSeat = -1;
        int _turnsTakenThisRound;

        /// <summary>实际生效的难度。配置缺失时使用内置兜底，避免开局直接崩。</summary>
        public DifficultyPreset ActiveDifficulty { get; private set; }

        public DifficultyPreset Difficulty => ActiveDifficulty;
        public DriftProfile Drift => Config != null ? Config.driftProfile : null;
        public GachaConfig Gacha => Config != null ? Config.gacha : null;
        public RiskColorRamp Ramp => Config != null ? Config.colorRamp : null;

        /// <summary>共用盘面模式下的全局冻结回合数（状态同步需要读取）。</summary>
        public int SharedFreezeTurns => _sharedFreezeTurns;

        // ============================================================ 状态同步
        /// <summary>
        /// 把主机下发的权威状态覆盖到本地。客户端**不跑模拟**，所以这是唯一改变客户端状态的通路。
        /// 只覆盖「对局元状态」；玩家与盘面的内容由 StateSnapshot 直接写字段，
        /// 因为那些字段本身就是 public 的（lives / inventory / RawCells / knownTruth）。
        /// </summary>
        public void RestoreAuthoritative(
            int currentSeat, int turnIndex, int roundIndex,
            bool isOver, int winnerSeat, GameOverReason overReason,
            int hoverSeat, int hoverX, int hoverY,
            int sharedFreezeTurns)
        {
            CurrentSeat = currentSeat;
            TurnIndex = turnIndex;
            RoundIndex = roundIndex;
            IsOver = isOver;
            WinnerSeat = winnerSeat;
            OverReason = overReason;
            HoverSeat = hoverSeat;
            HoverX = hoverX;
            HoverY = hoverY;
            _sharedFreezeTurns = sharedFreezeTurns;
        }

        // ============================================================ 初始化
        /// <param name="humanCountOverride">
        /// 大于 0 时覆盖配置中的真人玩家数量（联机时按实际连接人数决定），其余座位交给 AI。
        /// </param>
        public void Initialize(GameConfig config, ulong seed, int humanCountOverride = -1)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Seed = seed;
            _rng = new DeterministicRandom(seed);

            _log.Clear();
            _skipNextTurn = false;
            _turnsTakenThisRound = 0;
            IsOver = false;
            WinnerSeat = -1;
            OverReason = GameOverReason.None;
            TurnIndex = 0;
            RoundIndex = 0;
            CurrentSeat = 0;

            int seats = Clamp(Config.totalSeats, 1, MaxSeats);
            int requestedHumans = humanCountOverride > 0 ? humanCountOverride : Config.humanPlayerCount;
            int humans = Clamp(requestedHumans, 1, seats);
            Players = new PlayerState[seats];
            _sharedFreezeTurns = 0;
            HoverSeat = -1;
            HoverX = -1;
            HoverY = -1;

            ActiveDifficulty = ResolveDifficulty();

            // 共用盘面只生成一次，所有玩家指向同一个实例
            SharedBoard = Config.sharedBoard ? RiskBoard.Generate(ActiveDifficulty, ref _rng) : null;

            for (int i = 0; i < seats; i++)
            {
                var p = new PlayerState
                {
                    seatIndex = i,
                    isBot = i >= humans,
                    displayName = ResolveSeatName(i),
                    maxLives = Config.startingLives,
                    lives = Config.startingLives,
                };

                p.board = SharedBoard ?? RiskBoard.Generate(ActiveDifficulty, ref _rng);
                p.knownTruth = new float[p.board.CellCount];
                for (int k = 0; k < p.knownTruth.Length; k++) p.knownTruth[k] = -1f;

                p.itemFindChance = RollIndividualFindChance();
                Players[i] = p;
            }

            DealStartingItems();
            AutoRevealStart();

            Raise(GameEvent.Make(GameEventType.SessionStarted));
            BeginTurnFor(0);
        }

        /// <summary>解析难度：优先用配置，缺失时用内置默认值兜底。</summary>
        DifficultyPreset ResolveDifficulty()
        {
            if (Config.difficulty != null) return Config.difficulty;

            Debug.LogWarning("[炸弹酒馆] GameConfig 未绑定难度预设，本次使用内置默认难度（12x10，平均风险 15.6%）。");
            var fallback = ScriptableObject.CreateInstance<DifficultyPreset>();
            fallback.displayName = "默认（兜底）";
            fallback.name = "DifficultyFallback_Runtime";
            return fallback;
        }

        float RollIndividualFindChance()
        {
            var g = Gacha;
            float baseChance = g != null ? g.baseFindChance : FallbackFindChance;
            float variance = g != null ? g.perPlayerChanceVariance : 0f;
            float lo = g != null ? g.minFindChance : 0f;
            float hi = g != null ? g.maxFindChance : 1f;
            return Clamp(baseChance + _rng.NextCentered() * variance, lo, hi);
        }

        string ResolveSeatName(int seat)
        {
            var names = Config.seatDisplayNames;
            if (names != null && seat < names.Length && !string.IsNullOrEmpty(names[seat])) return names[seat];
            return $"炸弹{seat + 1}号";
        }

        void DealStartingItems()
        {
            int count = Config.startingItemCount;
            if (count <= 0) return;

            var pool = ResolveStartingPool();
            if (pool == null || pool.Count == 0) return;

            bool allowDup = Gacha == null || Gacha.allowDuplicates;
            for (int i = 0; i < Players.Length; i++)
            {
                for (int k = 0; k < count; k++)
                {
                    var def = PickWeightedFrom(pool, Players[i], allowDup);
                    if (def != null) AddItem(Players[i], def);
                }
            }
        }

        /// <summary>
        /// 开局发牌池。筛选规则收在 ItemCatalog 里 ——
        /// 那边同时负责过滤「已禁用」的道具，不在会话里重复实现一遍。
        /// </summary>
        List<ItemDefinition> ResolveStartingPool()
        {
            var catalog = Gacha != null ? Gacha.dropPool : null;
            return catalog != null ? catalog.ResolveStartingPool() : null;
        }

        void AutoRevealStart()
        {
            int n = Difficulty != null ? Difficulty.startRevealedCount : 0;
            if (n <= 0) return;

            for (int i = 0; i < Players.Length; i++)
            {
                // 共用盘面只需展开一次
                if (Config.sharedBoard && i > 0) break;

                var p = Players[i];
                for (int k = 0; k < n; k++)
                {
                    int best = -1;
                    float bestRisk = float.MaxValue;
                    var cells = p.board.RawCells;
                    for (int c = 0; c < cells.Length; c++)
                    {
                        if (cells[c].revealed) continue;
                        if (cells[c].trueRisk < bestRisk)
                        {
                            bestRisk = cells[c].trueRisk;
                            best = c;
                        }
                    }
                    if (best < 0) break;
                    p.board.MarkRevealed(best % p.board.Columns, best / p.board.Columns);
                    p.totalReveals++;
                }
            }
        }

        // ============================================================ 指令入口
        public bool TryApply(GameCommand cmd)
        {
            if (IsOver) return Reject(cmd.seatIndex, "对局已结束");
            if (Players == null || cmd.seatIndex < 0 || cmd.seatIndex >= Players.Length)
                return Reject(cmd.seatIndex, "座位无效");
            if (cmd.turnIndex >= 0 && cmd.turnIndex != TurnIndex)
                return Reject(cmd.seatIndex, $"回合序号不匹配（指令乱序：期望 {TurnIndex}，收到 {cmd.turnIndex}）");
            if (cmd.type != CommandType.EndTurn && cmd.seatIndex != CurrentSeat)
                return Reject(cmd.seatIndex, "还没轮到你");
            if (Players[cmd.seatIndex].eliminated)
                return Reject(cmd.seatIndex, "你已出局");

            switch (cmd.type)
            {
                case CommandType.RevealCell: return ApplyReveal(cmd);
                case CommandType.ToggleFlag: return ApplyFlag(cmd);
                case CommandType.UseItem: return ApplyUseItem(cmd);
                case CommandType.EndTurn: return ApplyEndTurn(cmd);
                case CommandType.SetHover: return ApplyHover(cmd);
            }
            return Reject(cmd.seatIndex, "未知指令");
        }

        /// <summary>公开当前行动者的悬停格子。仅当前行动者的悬停会被公开。</summary>
        bool ApplyHover(GameCommand cmd)
        {
            if (!Config.publishHover) return false;
            if (cmd.seatIndex != CurrentSeat) return false;
            if (!Players[cmd.seatIndex].board.InBounds(cmd.x, cmd.y)) return false;
            if (HoverSeat == cmd.seatIndex && HoverX == cmd.x && HoverY == cmd.y) return true;

            HoverSeat = cmd.seatIndex;
            HoverX = cmd.x;
            HoverY = cmd.y;

            Raise(new GameEvent
            {
                type = GameEventType.HoverChanged, seatIndex = cmd.seatIndex, x = cmd.x, y = cmd.y,
            });
            return true;
        }

        /// <summary>
        /// 联机客户端专用：直接写入主机下发的公开悬停位置。
        /// 不走指令校验 —— 客户端不模拟，悬停只是表现层数据。
        /// </summary>
        public void SetPublicHover(int seat, int x, int y)
        {
            HoverSeat = seat;
            HoverX = x;
            HoverY = y;

            Raise(new GameEvent
            {
                type = GameEventType.HoverChanged, seatIndex = seat, x = x, y = y,
            });
        }

        void ClearHover()
        {
            HoverSeat = -1;
            HoverX = -1;
            HoverY = -1;
        }

        // ============================================================ 翻格
        bool ApplyReveal(GameCommand cmd)
        {
            var p = Players[cmd.seatIndex];
            if (!p.board.InBounds(cmd.x, cmd.y)) return Reject(cmd.seatIndex, "超出盘面范围");

            var cell = p.board.Get(cmd.x, cmd.y);
            if (cell.revealed) return Reject(cmd.seatIndex, "该格已经翻开");
            if (cell.blasted) return Reject(cmd.seatIndex, "该格已经踩过");
            if (cell.flagged) return Reject(cmd.seatIndex, "请先取消该格的旗帜");

            // 核心判定：按真实爆炸概率掷骰，而不是读取预先确定的雷
            bool hit = _rng.Roll(cell.trueRisk);

            if (hit)
            {
                p.totalHits++;
                if (p.shieldCharges > 0)
                {
                    p.shieldCharges--;
                    // 护盾挡下的也是实打实的雷，同样留疤痕 ——
                    // 否则「用护盾探雷」等于没探，玩家白花一件道具。
                    p.board.MarkBlasted(cmd.x, cmd.y, (byte)p.seatIndex);
                    Raise(new GameEvent
                    {
                        type = GameEventType.ShieldConsumed, seatIndex = p.seatIndex,
                        x = cmd.x, y = cmd.y, value = cell.trueRisk,
                    });
                }
                else
                {
                    p.lives--;
                    // 先把「这里炸过」写回盘面，再发事件。
                    // 事件只负责这一下的表现，盘面才负责让玩家记住。
                    p.board.MarkBlasted(cmd.x, cmd.y, (byte)p.seatIndex);
                    Raise(new GameEvent
                    {
                        type = GameEventType.CellHit, seatIndex = p.seatIndex,
                        x = cmd.x, y = cmd.y, value = cell.trueRisk,
                    });
                    if (Config.clearRevealedOnHit) p.board.ClearRevealed();
                    if (p.lives <= 0) Eliminate(p);
                }
            }
            else
            {
                p.board.MarkRevealed(cmd.x, cmd.y);
                p.totalReveals++;
                Raise(new GameEvent
                {
                    type = GameEventType.CellRevealed, seatIndex = p.seatIndex,
                    x = cmd.x, y = cmd.y, value = cell.trueRisk,
                });
                TryDropItem(p, cmd.x, cmd.y);
            }

            if (!IsOver && Config.revealEndsTurn) EndTurnInternal();
            return true;
        }

        bool ApplyFlag(GameCommand cmd)
        {
            var p = Players[cmd.seatIndex];
            if (!p.board.InBounds(cmd.x, cmd.y)) return Reject(cmd.seatIndex, "超出盘面范围");
            if (p.board.Get(cmd.x, cmd.y).revealed) return Reject(cmd.seatIndex, "该格已经翻开");
            if (p.board.Get(cmd.x, cmd.y).blasted) return Reject(cmd.seatIndex, "该格已经踩过");

            p.board.ToggleFlag(cmd.x, cmd.y);
            p.totalFlags++;
            Raise(new GameEvent
            {
                type = GameEventType.CellFlagged, seatIndex = p.seatIndex,
                x = cmd.x, y = cmd.y, value = p.board.Get(cmd.x, cmd.y).flagged ? 1f : 0f,
            });

            if (!IsOver && Config.flagEndsTurn) EndTurnInternal();
            return true;
        }

        bool ApplyEndTurn(GameCommand cmd)
        {
            if (cmd.seatIndex != CurrentSeat) return Reject(cmd.seatIndex, "还没轮到你");
            EndTurnInternal();
            return true;
        }

        // ============================================================ 道具
        bool ApplyUseItem(GameCommand cmd)
        {
            var p = Players[cmd.seatIndex];
            if (cmd.inventorySlot < 0 || cmd.inventorySlot >= p.inventory.Count)
                return Reject(cmd.seatIndex, "该道具不存在");

            if (Config.maxItemsPerTurn > 0 && p.itemsUsedThisTurn >= Config.maxItemsPerTurn)
                return Reject(cmd.seatIndex, $"本回合道具使用次数已达上限（{Config.maxItemsPerTurn}）");

            var inst = p.inventory[cmd.inventorySlot];
            var def = inst.definition;
            if (def == null || inst.remainingUses <= 0) return Reject(cmd.seatIndex, "该道具已耗尽");

            if (def.requiresTarget && !p.board.InBounds(cmd.x, cmd.y))
                return Reject(cmd.seatIndex, "该道具需要选择一个目标格");

            if (!ResolveItemEffect(p, def, cmd)) return Reject(cmd.seatIndex, "道具效果未能生效");

            inst.remainingUses--;
            if (inst.remainingUses <= 0) p.inventory.RemoveAt(cmd.inventorySlot);
            else p.inventory[cmd.inventorySlot] = inst;

            p.itemsUsedThisTurn++;
            p.totalItemsUsed++;
            Raise(new GameEvent
            {
                type = GameEventType.ItemUsed, seatIndex = p.seatIndex,
                // 使用事实是公共博弈信息；目标、坐标与实际效果由上面的 secret 事件保密。
                x = -1, y = -1, otherSeat = -1, message = def.displayName,
                // value 字段在这里借用为「道具效果类型」：
                // 表现层要据此选对应的道具模型，但又不想为了这点信息去扩充网络协议。
                value = (int)def.effect,
                secret = false,
            });

            if (!IsOver && Config.itemUseEndsTurn) EndTurnInternal();
            return true;
        }

        bool ResolveItemEffect(PlayerState p, ItemDefinition def, GameCommand cmd)
        {
            // 护盾 / 冻结 / 换手作用于自身，其余效果需要一个目标盘面
            bool needsBoard = def.effect != ItemEffectType.Shield
                              && def.effect != ItemEffectType.FreezeDrift
                              && def.effect != ItemEffectType.SkipTurn;

            RiskBoard board = null;
            if (needsBoard)
            {
                board = ResolveTargetBoard(p, def, cmd);
                if (board == null) return false;
                if (def.requiresTarget && board.InBounds(cmd.x, cmd.y) && board.Get(cmd.x, cmd.y).blasted)
                    return false;
            }

            // 先给「注册表里的自定义效果」一次机会。
            // 没注册的类型会落到下面的内置 switch，所以现有道具行为完全不变。
            // 用全限定名是为了不在本文件顶部多引一个命名空间。
            if (BombTavern.Items.ItemEffectRegistry.TryGet(def.effect, out var customEffect))
            {
                var ctx = new BombTavern.Items.ItemContext
                {
                    session = this,
                    user = p,
                    board = board,
                    def = def,
                    cmd = cmd,
                    probeInto = ProbeInto,
                    raise = Raise,
                };

                if (!customEffect.CanApply(ctx, out string reason))
                {
                    if (!string.IsNullOrEmpty(reason))
                        UnityEngine.Debug.Log($"[道具] {def.displayName} 目标不合法：{reason}");
                    return false;
                }

                return customEffect.Apply(ctx);
            }

            switch (def.effect)
            {
                case ItemEffectType.ProbeCell:
                {
                    ProbeInto(p, board, cmd.x, cmd.y);
                    p.totalProbes++;
                    Raise(new GameEvent
                    {
                        type = GameEventType.CellProbed, seatIndex = p.seatIndex,
                        x = cmd.x, y = cmd.y, value = board.Get(cmd.x, cmd.y).trueRisk,
                        secret = Config.itemUseIsSecret,
                    });
                    return true;
                }

                case ItemEffectType.IncreaseRisk:
                case ItemEffectType.DecreaseRisk:
                {
                    float sign = def.effect == ItemEffectType.IncreaseRisk ? 1f : -1f;
                    // 风险修改属于隐藏效果。界面继续显示开局值，直到使用者主动探查。
                    board.ModifyTrueRisk(cmd.x, cmd.y, sign * def.riskDelta, false, Drift);
                    p.totalModifies++;
                    Raise(new GameEvent
                    {
                        type = GameEventType.CellRiskModified, seatIndex = p.seatIndex,
                        x = cmd.x, y = cmd.y, value = board.Get(cmd.x, cmd.y).trueRisk,
                        message = def.displayName, secret = Config.itemUseIsSecret,
                    });
                    return true;
                }

                case ItemEffectType.AreaIncreaseRisk:
                {
                    int changed = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = cmd.x + dx, y = cmd.y + dy;
                            if (!board.InBounds(x, y)) continue;
                            var cell = board.Get(x, y);
                            if (cell.revealed || cell.blasted) continue;
                            float delta = def.riskDelta * (dx == 0 && dy == 0 ? 1.5f : 1f);
                            board.ModifyTrueRisk(x, y, delta, false, Drift);
                            changed++;
                        }
                    }
                    if (changed == 0) return false;
                    p.totalModifies++;
                    Raise(new GameEvent
                    {
                        type = GameEventType.AreaRiskModified, seatIndex = p.seatIndex,
                        x = cmd.x, y = cmd.y, value = (int)def.effect,
                        message = def.displayName, secret = Config.itemUseIsSecret,
                    });
                    return true;
                }

                case ItemEffectType.DiamondFlux:
                {
                    // 完整的曼哈顿半径 2 菱形恰好有 12 个非中心格。
                    if (cmd.x < 2 || cmd.y < 2 || cmd.x >= board.Columns - 2 || cmd.y >= board.Rows - 2)
                        return false;
                    var targets = new List<int>(12);
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int distance = Math.Abs(dx) + Math.Abs(dy);
                            if (distance == 0 || distance > 2) continue;
                            int x = cmd.x + dx, y = cmd.y + dy;
                            var cell = board.Get(x, y);
                            if (!cell.revealed && !cell.blasted) targets.Add(board.Index(x, y));
                        }
                    }
                    if (targets.Count == 0) return false;
                    // 先洗牌再分配升降险，避免整个区域只朝一个方向变化。
                    for (int i = targets.Count - 1; i > 0; i--)
                    {
                        int j = _rng.NextInt(0, i + 1);
                        int temp = targets[i]; targets[i] = targets[j]; targets[j] = temp;
                    }
                    int increaseCount = (targets.Count + 1) / 2;
                    for (int i = 0; i < targets.Count; i++)
                    {
                        int x = targets[i] % board.Columns, y = targets[i] / board.Columns;
                        float delta = _rng.Range(def.riskDelta * 0.5f, def.riskDelta);
                        board.ModifyTrueRisk(x, y, i < increaseCount ? delta : -delta, false, Drift);
                    }
                    p.totalModifies++;
                    Raise(new GameEvent
                    {
                        type = GameEventType.AreaRiskModified, seatIndex = p.seatIndex,
                        x = cmd.x, y = cmd.y, value = (int)def.effect,
                        message = def.displayName, secret = Config.itemUseIsSecret,
                    });
                    return true;
                }

                case ItemEffectType.Shield:
                {
                    p.shieldCharges += def.shieldCharges < 1 ? 1 : def.shieldCharges;
                    return true;
                }

                case ItemEffectType.ScanRow:
                {
                    for (int x = 0; x < board.Columns; x++) ProbeInto(p, board, x, cmd.y);
                    Raise(new GameEvent
                    {
                        type = GameEventType.RowScanned, seatIndex = p.seatIndex, x = 0, y = cmd.y,
                        message = def.displayName, secret = Config.itemUseIsSecret,
                    });
                    return true;
                }

                case ItemEffectType.ScanArea:
                {
                    int r = def.radius;
                    for (int dy = -r; dy <= r; dy++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (dx * dx + dy * dy > r * r) continue;
                            ProbeInto(p, board, cmd.x + dx, cmd.y + dy);
                        }
                    }
                    Raise(new GameEvent
                    {
                        type = GameEventType.AreaScanned, seatIndex = p.seatIndex,
                        x = cmd.x, y = cmd.y, value = r, message = def.displayName,
                        secret = Config.itemUseIsSecret,
                    });
                    return true;
                }

                case ItemEffectType.FreezeDrift:
                {
                    int turns = def.durationTurns < 1 ? 1 : def.durationTurns;
                    // 共用盘面的冻结是全桌生效的，记在会话上
                    if (Config.sharedBoard) _sharedFreezeTurns += turns;
                    else p.freezeTurnsRemaining += turns;

                    Raise(new GameEvent
                    {
                        type = GameEventType.DriftFrozen, seatIndex = p.seatIndex,
                        value = Config.sharedBoard ? _sharedFreezeTurns : p.freezeTurnsRemaining,
                        message = def.displayName, secret = Config.itemUseIsSecret,
                    });
                    return true;
                }

                case ItemEffectType.RevealNeighbors:
                {
                    var buf = new int[8];
                    int n = board.NeighborsInto(cmd.x, cmd.y, buf);
                    for (int i = 0; i < n; i++)
                        ProbeInto(p, board, buf[i] % board.Columns, buf[i] / board.Columns);
                    Raise(new GameEvent
                    {
                        type = GameEventType.AreaScanned, seatIndex = p.seatIndex,
                        x = cmd.x, y = cmd.y, value = 1f, message = def.displayName,
                        secret = Config.itemUseIsSecret,
                    });
                    return true;
                }

                case ItemEffectType.SkipTurn:
                {
                    _skipNextTurn = true;
                    _skipSeat = cmd.targetSeat;
                    return true;
                }
            }
            return false;
        }

        /// <summary>按道具的目标范围解析出真正被作用的盘面。返回 null 表示目标非法。</summary>
        RiskBoard ResolveTargetBoard(PlayerState actor, ItemDefinition def, GameCommand cmd)
        {
            // 共用盘面：所有人面对的是同一块盘，道具直接作用其上
            if (Config.sharedBoard && SharedBoard != null) return SharedBoard;

            int targetSeat = cmd.targetSeat < 0 ? actor.seatIndex : cmd.targetSeat;

            if (targetSeat == actor.seatIndex)
            {
                // 作用于自己：只有允许自用的道具才行
                return def.targetScope == ItemTargetScope.OpponentBoard ? null : actor.board;
            }

            var other = PlayerAt(targetSeat);
            if (other == null || other.eliminated) return null;

            // 作用于他人：只有允许对外的道具才行
            return def.targetScope == ItemTargetScope.SelfBoard ? null : other.board;
        }

        /// <summary>
        /// 探查一格：把真实概率记进使用者的私人读数（only he can see）。
        /// 公共显示值默认不改写，因此其他人既看不到颜色变化，也收不到事件。
        /// </summary>
        void ProbeInto(PlayerState p, RiskBoard board, int x, int y)
        {
            if (board == null || !board.InBounds(x, y)) return;

            int idx = board.Index(x, y);
            p.knownTruth[idx] = board.RawCells[idx].trueRisk;

            if (Config.probeRefreshesPublicDisplay) board.RefreshDisplay(x, y);
        }

        /// <summary>读取某玩家对某格的私人探查读数。-1 表示他还不知道真相。</summary>
        public float KnownTruthOf(int seat, int x, int y)
        {
            var p = PlayerAt(seat);
            if (p == null || p.board == null || !p.board.InBounds(x, y)) return -1f;
            var arr = p.knownTruth;
            if (arr == null) return -1f;
            int idx = p.board.Index(x, y);
            return idx >= 0 && idx < arr.Length ? arr[idx] : -1f;
        }

        // ============================================================ 掉落 / 保底
        bool TryDropItem(PlayerState p, int x, int y)
        {
            var g = Gacha;
            if (g == null || g.dropPool == null || g.dropPool.DroppableItems.Count == 0) return false;

            p.totalFindRolls++;

            float chance = p.itemFindChance;
            int overSoft = p.pityCounter - g.softPityStart;
            if (overSoft > 0) chance += overSoft * g.softPityStep;
            if (chance > 1f) chance = 1f;

            // 硬保底：连续未出达到阈值则必定掉落
            bool hardPity = (p.pityCounter + 1) >= g.hardPityThreshold;
            bool got = hardPity || _rng.Roll(chance);

            if (!got)
            {
                p.pityCounter++;
                return false;
            }

            // 走 DroppableItems 而不是 items：被禁用的道具不能掉出来
            var def = PickWeightedFrom(g.dropPool.DroppableItems, p, g.allowDuplicates);
            if (def == null)
            {
                p.pityCounter++;
                return false;
            }

            if (AddItem(p, def))
            {
                p.totalItemsFound++;
                if (g.resetPityOnDrop) p.pityCounter = 0;
                Raise(new GameEvent
                {
                    type = GameEventType.ItemGained, seatIndex = p.seatIndex, x = x, y = y,
                    message = def.displayName, value = hardPity ? 1f : 0f,
                    // 摸到什么道具也属于私人情报
                    secret = Config.itemUseIsSecret,
                });
                return true;
            }

            if (g.resetPityWhenInventoryFull) p.pityCounter = 0;
            Raise(new GameEvent
            {
                type = GameEventType.InventoryFull, seatIndex = p.seatIndex, message = def.displayName,
                secret = Config.itemUseIsSecret,
            });
            return false;
        }

        ItemDefinition PickWeightedFrom(List<ItemDefinition> pool, PlayerState p, bool allowDuplicates)
        {
            if (pool == null || pool.Count == 0) return null;

            float total = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                var d = pool[i];
                if (d == null || d.dropWeight <= 0f) continue;
                if (!allowDuplicates && p.HasItem(d.itemId)) continue;
                total += d.dropWeight;
            }
            if (total <= 0f) return null;

            float r = _rng.NextFloat() * total;
            float acc = 0f;
            ItemDefinition last = null;
            for (int i = 0; i < pool.Count; i++)
            {
                var d = pool[i];
                if (d == null || d.dropWeight <= 0f) continue;
                if (!allowDuplicates && p.HasItem(d.itemId)) continue;
                last = d;
                acc += d.dropWeight;
                if (r < acc) return d;
            }
            return last;
        }

        bool AddItem(PlayerState p, ItemDefinition def)
        {
            if (def == null) return false;
            if (Config != null && p.inventory.Count >= Config.maxInventorySize) return false;
            p.inventory.Add(new ItemInstance { definition = def, remainingUses = 1 });
            return true;
        }

        // ============================================================ 回合推进
        void EndTurnInternal()
        {
            Raise(new GameEvent { type = GameEventType.TurnEnded, seatIndex = CurrentSeat });

            if (Drift != null && Config.driftEnabled && !Drift.driftAtTurnStartInsteadOfEnd)
                ApplyDrift(CurrentSeat);

            int next = NextAliveSeat(CurrentSeat);
            if (next < 0)
            {
                FinishGame(GameOverReason.LastPlayerStanding);
                return;
            }

            // 跳过：换手道具的副作用（_skipSeat < 0 表示跳过紧接着的下一位）
            if (_skipNextTurn)
            {
                _skipNextTurn = false;
                if (_skipSeat < 0 || next == _skipSeat)
                {
                    int afterSkip = NextAliveSeat(next);
                    if (afterSkip >= 0) next = afterSkip;
                }
                _skipSeat = -1;
                if (next < 0) { FinishGame(GameOverReason.LastPlayerStanding); return; }
            }

            TurnIndex++;
            _turnsTakenThisRound++;
            if (_turnsTakenThisRound >= AliveCount())
            {
                _turnsTakenThisRound = 0;
                RoundIndex++;
            }

            CurrentSeat = next;

            if (Config.maxTurns > 0 && RoundIndex >= Config.maxTurns)
            {
                FinishGame(GameOverReason.TurnLimitReached);
                return;
            }

            if (Drift != null && Config.driftEnabled && Drift.driftAtTurnStartInsteadOfEnd)
                ApplyDrift(CurrentSeat);

            BeginTurnFor(CurrentSeat);
        }

        void BeginTurnFor(int seat)
        {
            ClearHover();               // 换人后不再公开上一个人的悬停
            Players[seat].BeginTurn();
            Raise(new GameEvent { type = GameEventType.TurnBegan, seatIndex = seat });
        }

        void ApplyDrift(int actingSeat)
        {
            if (Drift == null || !Config.driftEnabled) return;

            // 回合递增带来的漂移放大（用累乘代替 Pow，保证跨平台一致）
            float escalation = 1f;
            for (int i = 0; i < RoundIndex && escalation < Drift.maxEscalation; i++)
                escalation *= Drift.roundEscalation;
            if (escalation > Drift.maxEscalation) escalation = Drift.maxEscalation;

            // 共用盘面只漂移一次；冻结也按全桌计算
            if (Config.sharedBoard && SharedBoard != null)
            {
                if (_sharedFreezeTurns > 0)
                {
                    _sharedFreezeTurns--;
                    return;
                }
                SharedBoard.Drift(Drift, ref _rng, escalation);
                Raise(new GameEvent
                {
                    type = GameEventType.DriftApplied, seatIndex = -1, value = escalation,
                });
                return;
            }

            for (int i = 0; i < Players.Length; i++)
            {
                var p = Players[i];
                if (p.eliminated) continue;
                if (Drift.scope == DriftScope.OwnBoardOnly && p.seatIndex != actingSeat) continue;
                if (p.freezeTurnsRemaining > 0)
                {
                    p.freezeTurnsRemaining--;
                    continue;
                }

                p.board.Drift(Drift, ref _rng, escalation);
                Raise(new GameEvent
                {
                    type = GameEventType.DriftApplied, seatIndex = p.seatIndex, value = escalation,
                });
            }
        }

        int NextAliveSeat(int from)
        {
            for (int step = 1; step <= Players.Length; step++)
            {
                int s = (from + step) % Players.Length;
                if (!Players[s].eliminated && s != from) return s;
            }
            return -1;
        }

        void Eliminate(PlayerState p)
        {
            p.eliminated = true;
            p.lives = 0;
            Raise(new GameEvent { type = GameEventType.PlayerEliminated, seatIndex = p.seatIndex });

            if (AliveCount() <= 1) FinishGame(GameOverReason.LastPlayerStanding);
        }

        void FinishGame(GameOverReason reason)
        {
            if (IsOver) return;
            IsOver = true;
            OverReason = reason;
            WinnerSeat = ResolveWinner();
            Raise(new GameEvent
            {
                type = GameEventType.GameOver, seatIndex = WinnerSeat, value = (int)reason,
            });
        }

        int ResolveWinner()
        {
            int best = -1;
            for (int i = 0; i < Players.Length; i++)
            {
                var p = Players[i];
                if (p.eliminated) continue;
                if (best < 0) { best = i; continue; }

                var b = Players[best];
                if (p.lives > b.lives) best = i;
                else if (p.lives == b.lives && p.totalReveals > b.totalReveals) best = i;
                // 完全相同时保留座位号更小者，保证确定性
            }
            if (best < 0)
            {
                // 全灭：按翻开总数决定
                for (int i = 0; i < Players.Length; i++)
                    if (best < 0 || Players[i].totalReveals > Players[best].totalReveals) best = i;
            }
            return best;
        }

        // ============================================================ 只读查询
        public int AliveCount()
        {
            if (Players == null) return 0;
            int n = 0;
            for (int i = 0; i < Players.Length; i++) if (!Players[i].eliminated) n++;
            return n;
        }

        public bool IsMyTurn(int seat) => !IsOver && seat == CurrentSeat && Players != null &&
                                          seat >= 0 && seat < Players.Length && !Players[seat].eliminated;

        public PlayerState PlayerAt(int seat) =>
            Players != null && seat >= 0 && seat < Players.Length ? Players[seat] : null;

        public float CurrentEscalation()
        {
            if (Drift == null) return 1f;
            float v = 1f;
            for (int i = 0; i < RoundIndex && v < Drift.maxEscalation; i++) v *= Drift.roundEscalation;
            return v > Drift.maxEscalation ? Drift.maxEscalation : v;
        }

        /// <summary>
        /// 最近一次被拒绝的原因。联机时主机把它回给提交方，
        /// 让客户端能提示「为什么这一手没生效」，而不必自己去推断。
        /// </summary>
        public string LastRejectReason { get; private set; } = string.Empty;

        // ============================================================ 内部工具
        bool Reject(int seat, string reason)
        {
            LastRejectReason = reason ?? string.Empty;

            Raise(new GameEvent
            {
                type = GameEventType.ActionRejected, seatIndex = seat, message = reason,
            });
            return false;
        }

        void Raise(GameEvent e)
        {
            _log.Add(e);
            EventRaised?.Invoke(e);
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
