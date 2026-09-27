using BombTavern.Config;
using BombTavern.Core;

namespace BombTavern.AI
{
    /// <summary>
    /// 敌人 AI。
    /// 约束：
    /// 1) 只读取玩家可见信息（显示概率 + 已翻开格的期望雷数），不看 TrueRisk，避免"开挂"。
    /// 2) 使用独立随机流（种子派生自对局种子与座位），绝不动用 GameSession 的随机流，
    ///    否则会破坏锁步确定性。
    /// 3) 每次 Decide 只产出一条指令；调用方在指令被接受后再次调用。
    /// </summary>
    public class BotBrain
    {
        readonly GameConfig _config;
        readonly DeterministicRandom _rng;
        float[] _belief;

        public int Seat { get; }

        public BotBrain(GameConfig config, int seat, ulong sessionSeed)
        {
            _config = config;
            Seat = seat;
            // 派生独立流：不同座位、不同对局种子互不相同，但完全可复现
            _rng = new DeterministicRandom(sessionSeed ^ (0x9E3779B97F4A7C15UL * (ulong)(seat + 1)));
        }

        /// <summary>产出下一步指令。</summary>
        public GameCommand Decide(GameSession session)
        {
            var me = session.PlayerAt(Seat);
            if (me == null || me.eliminated) return GameCommand.End(Seat, session.TurnIndex);

            BuildBelief(me);

            int bestIdx = -1;
            float bestRisk = float.MaxValue;
            var cells = me.board.RawCells;

            for (int i = 0; i < cells.Length; i++)
            {
                if (_belief[i] < 0f) continue;          // 已翻开
                if (cells[i].flagged) continue;          // 自己插了旗
                if (cells[i].blasted) continue;          // 已经踩过，炸痕是终态
                if (_belief[i] < bestRisk)
                {
                    bestRisk = _belief[i];
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) return GameCommand.End(Seat, session.TurnIndex);

            int bx = bestIdx % me.board.Columns;
            int by = bestIdx / me.board.Columns;

            bool canUseItem = _config.maxItemsPerTurn <= 0 || me.itemsUsedThisTurn < _config.maxItemsPerTurn;

            // 1) 局面尚可时，考虑用增险道具暗算领先的对手
            if (canUseItem && TryPlanSabotage(session, me, bestIdx,
                    out int sabSlot, out int sabSeat, out int sx, out int sy))
                return GameCommand.UseItem(Seat, sabSlot, sx, sy, sabSeat, session.TurnIndex);

            // 2) 拖得越久越不敢相信屏幕上的旧颜色：按轮次递增的概率先探查一次。
            //    注意 AI 看不到真实概率，也不知道自己的信息过期了多少，
            //    所以用"轮次"作为过期程度的代理信号，而不是偷看 Staleness。
            //    已经探明过的格子不再重复探查。
            if (canUseItem && !HasKnownTruth(me, bestIdx))
            {
                int slotProbe = FindSlot(me, ItemEffectType.ProbeCell);
                if (slotProbe >= 0)
                {
                    float probeChance = _config.botProbeBeforeRevealChance
                                        + session.RoundIndex * _config.botProbeChancePerRound;
                    if (probeChance > 1f) probeChance = 1f;
                    if (_rng.Roll(probeChance))
                        return GameCommand.UseItem(Seat, slotProbe, bx, by, Seat, session.TurnIndex);
                }
            }

            if (canUseItem)
            {
                // 3) 风险仍然偏高，手里有降险道具就先压一压
                int slotDecrease = FindSlot(me, ItemEffectType.DecreaseRisk);
                if (slotDecrease >= 0 && bestRisk >= _config.botDecreaseTargetRisk)
                    return GameCommand.UseItem(Seat, slotDecrease, bx, by, Seat, session.TurnIndex);

                // 4) 机会不算宽裕时，先把护盾戴上
                int slotShield = FindSlot(me, ItemEffectType.Shield);
                if (slotShield >= 0 && me.shieldCharges <= 0 && me.lives <= me.maxLives - 1)
                    return GameCommand.UseItem(Seat, slotShield, bx, by, Seat, session.TurnIndex);
            }

            // 5) 最低风险已在容忍范围内 → 直接翻
            if (bestRisk <= _config.botRiskTolerance)
                return GameCommand.Reveal(Seat, bx, by, session.TurnIndex);

            // 6) 没有更好的选择了：硬着头皮翻最安全的一格
            return GameCommand.Reveal(Seat, bx, by, session.TurnIndex);
        }

        /// <summary>
        /// 置信度估计：以显示概率为先验，再用"已翻开格的期望雷数"做推理修正。
        /// 只使用玩家屏幕上能看到的信息。
        /// </summary>
        void BuildBelief(PlayerState me)
        {
            var board = me.board;
            int count = board.CellCount;
            if (_belief == null || _belief.Length != count) _belief = new float[count];

            var cells = board.RawCells;
            var known = me.knownTruth;

            for (int i = 0; i < count; i++)
            {
                if (cells[i].revealed || cells[i].blasted)
                {
                    _belief[i] = -1f;
                    continue;
                }

                // 自己探查过的格子用探明的真相，其余用屏幕上显示的旧值
                float knownRisk = known != null && i < known.Length ? known[i] : -1f;
                _belief[i] = knownRisk >= 0f ? Clamp01(knownRisk) : Clamp01(cells[i].displayedRisk);
            }

            var buf = new int[8];
            for (int y = 0; y < board.Rows; y++)
            {
                for (int x = 0; x < board.Columns; x++)
                {
                    int idx = board.Index(x, y);
                    if (!cells[idx].revealed) continue;

                    float expectation = board.DisplayedExpectation(x, y);
                    int n = board.NeighborsInto(x, y, buf);

                    float sum = 0f;
                    int open = 0;
                    for (int k = 0; k < n; k++)
                    {
                        int ni = buf[k];
                        if (_belief[ni] < 0f) continue;
                        sum += _belief[ni];
                        open++;
                    }

                    // 期望雷数低于当前估计总和 → 说明周围格子的风险被高估，按比例下调
                    if (open == 0 || sum <= 0.0001f || expectation >= sum) continue;

                    float factor = expectation / sum;
                    for (int k = 0; k < n; k++)
                    {
                        int ni = buf[k];
                        if (_belief[ni] < 0f) continue;
                        _belief[ni] *= factor;
                    }
                }
            }

            for (int i = 0; i < count; i++)
                if (_belief[i] > 1f) _belief[i] = 1f;
        }

        bool TryPlanSabotage(GameSession session, PlayerState me, int ownNextCell,
            out int slot, out int targetSeat, out int x, out int y)
        {
            slot = -1;
            targetSeat = -1;
            x = -1;
            y = -1;

            if (_config.botSabotageChance <= 0f) return false;
            if (me.lives <= 1) return false;                 // 自身处境危险时不浪
            if (session.AliveCount() <= 1) return false;

            // 先确认手里有对外的风险道具，再掷骰（避免白白消耗随机数）。
            for (int i = 0; i < me.inventory.Count; i++)
            {
                var d = me.inventory[i].definition;
                if (d == null || me.inventory[i].remainingUses <= 0) continue;
                if ((d.effect == ItemEffectType.IncreaseRisk ||
                     d.effect == ItemEffectType.AreaIncreaseRisk ||
                     d.effect == ItemEffectType.DiamondFlux) &&
                    d.targetScope != ItemTargetScope.SelfBoard)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0) return false;
            if (!_rng.Roll(_config.botSabotageChance)) return false;

            // 目标：机会最多的对手
            int bestSeat = -1;
            for (int i = 0; i < session.Players.Length; i++)
            {
                var p = session.Players[i];
                if (p.seatIndex == Seat || p.eliminated) continue;
                if (bestSeat < 0 || p.lives > session.Players[bestSeat].lives) bestSeat = p.seatIndex;
            }
            if (bestSeat < 0) return false;

            // 共用盘面下无法只陷害一个人：选对手可能会翻的低风险区域，
            // 同时避开自己本回合准备翻的格子，减少立刻踩到自己陷阱的情况。
            var ob = session.PlayerAt(bestSeat).board;
            var cells = ob.RawCells;
            var effect = me.inventory[slot].definition.effect;
            int idx = -1;
            float highest = float.MinValue;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].revealed || cells[i].flagged || cells[i].blasted) continue;
                int cx = i % ob.Columns, cy = i / ob.Columns;
                if (effect == ItemEffectType.DiamondFlux &&
                    (cx < 2 || cy < 2 || cx >= ob.Columns - 2 || cy >= ob.Rows - 2)) continue;
                float score = 0f;
                int affected = 0;
                bool hitsOwnNext = false;
                int radius = effect == ItemEffectType.DiamondFlux ? 2 :
                    effect == ItemEffectType.AreaIncreaseRisk ? 1 : 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (effect == ItemEffectType.DiamondFlux &&
                            (System.Math.Abs(dx) + System.Math.Abs(dy) > 2 || (dx == 0 && dy == 0))) continue;
                        int tx = cx + dx, ty = cy + dy;
                        if (!ob.InBounds(tx, ty)) continue;
                        int target = ob.Index(tx, ty);
                        if (target == ownNextCell) hitsOwnNext = true;
                        var cell = cells[target];
                        if (cell.revealed || cell.blasted) continue;
                        score += 1f - cell.displayedRisk;
                        affected++;
                    }
                }
                if (hitsOwnNext || affected == 0) continue;
                score /= affected;
                score += affected * 0.015f; // 大范围道具优先投到仍有价值的完整区域
                if (score <= highest) continue;
                highest = score;
                idx = i;
            }
            if (idx < 0) return false;

            targetSeat = bestSeat;
            x = idx % ob.Columns;
            y = idx / ob.Columns;
            return true;
        }

        /// <summary>该格是否已经有自己探明的读数（避免重复消耗探查道具）。</summary>
        static bool HasKnownTruth(PlayerState me, int cellIndex)
        {
            var known = me.knownTruth;
            if (known == null || cellIndex < 0 || cellIndex >= known.Length) return false;
            return known[cellIndex] >= 0f;
        }

        static int FindSlot(PlayerState me, ItemEffectType effect)
        {
            for (int i = 0; i < me.inventory.Count; i++)
            {
                var d = me.inventory[i].definition;
                if (d != null && d.effect == effect && me.inventory[i].remainingUses > 0) return i;
            }
            return -1;
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
