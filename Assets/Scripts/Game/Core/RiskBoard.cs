using System;
using BombTavern.Config;

namespace BombTavern.Core
{
    /// <summary>
    /// 单个格子的状态。
    /// 关键设计：格子没有「是不是雷」的布尔值，只有一个真实爆炸概率 trueRisk；
    /// displayedRisk 是玩家屏幕上看到的概率（颜色来源），它只在开局 / 被道具探查 / 被道具修改时更新，
    /// 真实概率漂移时不会同步 → 玩家的信息会逐渐过期。
    /// </summary>
    [Serializable]
    public struct RiskCell
    {
        /// <summary>真实爆炸概率（隐藏，翻格时用它掷骰）。</summary>
        public float trueRisk;

        /// <summary>玩家看到的爆炸概率（格子颜色来源）。</summary>
        public float displayedRisk;

        /// <summary>翻开瞬间快照的相邻格子期望雷数（传统扫雷的数字提示）。</summary>
        public float revealedExpectation;

        /// <summary>是否已被翻开（安全通过）。</summary>
        public bool revealed;

        /// <summary>玩家插旗标记。</summary>
        public bool flagged;

        /// <summary>是否是开局保证的安全区。</summary>
        public bool safeZone;

        /// <summary>
        /// 这一格**已经炸过**（有人在这里踩到雷）。带上座位号是为了画成「谁的血」。
        ///
        /// 为什么必须落回盘面数据，而不是只发一条 CellHit 事件：
        /// 事件是「一次性表现」——播完爆炸动画就没了。玩家上一轮踩的雷，
        /// 下一轮再看那块屏幕已经恢复成普通未翻开格子，
        /// 于是「这块我踩过、它是雷」这条最重要的记忆被系统自己擦掉了。
        /// 对局越到后期格数越少，这个缺失就越致命。
        /// </summary>
        public bool blasted;

        /// <summary>踩爆这一格的座位号（255 = 未知）。</summary>
        public byte blastedSeat;

        /// <summary>显示值与真实值的偏差，用于表现层的「信息过期」抖动提示。</summary>
        public float Staleness => displayedRisk - trueRisk;
    }

    /// <summary>
    /// 概率雷盘。纯 C# 实现（不依赖 UnityEngine），便于单元测试与联机确定性复算。
    /// </summary>
    public class RiskBoard
    {
        public readonly int Columns;
        public readonly int Rows;

        /// <summary>已翻开格子上的期望雷数是否实时刷新（配置项，默认 false = 快照）。</summary>
        public bool NeighborNumbersLive;

        readonly RiskCell[] _cells;

        public RiskBoard(int columns, int rows, bool neighborNumbersLive = false)
        {
            Columns = columns < 1 ? 1 : columns;
            Rows = rows < 1 ? 1 : rows;
            _cells = new RiskCell[Columns * Rows];
            NeighborNumbersLive = neighborNumbersLive;
        }

        public int CellCount => _cells.Length;
        public RiskCell[] RawCells => _cells;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Columns && y < Rows;
        public int Index(int x, int y) => y * Columns + x;

        public RiskCell Get(int x, int y) => _cells[Index(x, y)];
        public void Set(int x, int y, RiskCell cell) => _cells[Index(x, y)] = cell;

        /// <summary>把 8 邻域索引写入缓冲，返回数量（避免每帧分配）。</summary>
        public int NeighborsInto(int x, int y, int[] buffer)
        {
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (!InBounds(nx, ny)) continue;
                    buffer[n++] = Index(nx, ny);
                }
            }
            return n;
        }

        /// <summary>相邻格子真实爆炸概率之和（实时值）。</summary>
        public float LiveNeighborExpectation(int x, int y)
        {
            var buf = new int[8];
            int n = NeighborsInto(x, y, buf);
            float sum = 0f;
            for (int i = 0; i < n; i++) sum += _cells[buf[i]].trueRisk;
            return sum;
        }

        /// <summary>渲染用：格子当前应显示的期望雷数。</summary>
        public float DisplayedExpectation(int x, int y)
        {
            var c = _cells[Index(x, y)];
            if (!c.revealed) return 0f;
            return NeighborNumbersLive ? LiveNeighborExpectation(x, y) : c.revealedExpectation;
        }

        // ============================================================ 生成
        /// <summary>
        /// 按难度预设生成初始风险场。
        /// 思路对齐传统扫雷：基础雷密度 + 风险簇聚集 + 一小片安全起步区。
        /// </summary>
        public static RiskBoard Generate(DifficultyPreset preset, ref DeterministicRandom rng)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));

            var board = new RiskBoard(preset.columns, preset.rows, preset.neighborNumbersAreLive);
            int cols = board.Columns, rows = board.Rows;
            int total = board.CellCount;

            // 1) 基础概率 + 上下浮动
            for (int i = 0; i < total; i++)
            {
                float r = preset.baseRisk + rng.NextCentered() * preset.riskVariance;
                board._cells[i].trueRisk = Clamp01(r);
            }

            // 2) 风险簇：模拟传统扫雷的雷区聚集，让颜色呈现疏密
            int clusterCount = (int)Math.Round(total * preset.clusterDensity);
            for (int c = 0; c < clusterCount; c++)
            {
                int cx = rng.NextInt(0, cols);
                int cy = rng.NextInt(0, rows);
                float radius = preset.clusterRadius;
                if (radius < 0.01f) radius = 0.01f;

                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < cols; x++)
                    {
                        float dx = x - cx, dy = y - cy;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dist >= radius) continue;
                        float falloff = 1f - dist / radius;
                        int idx = board.Index(x, y);
                        board._cells[idx].trueRisk = Clamp01(
                            board._cells[idx].trueRisk * (1f + (preset.clusterBoost - 1f) * falloff));
                    }
                }
            }

            // 3) 安全起步区：从最低风险的格子里挑一批压低
            int safeCount = (int)Math.Round(total * preset.guaranteedSafeRatio);
            for (int s = 0; s < safeCount; s++)
            {
                int best = -1;
                float bestRisk = float.MaxValue;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    int idx = rng.NextInt(0, total);
                    if (board._cells[idx].safeZone) continue;
                    if (board._cells[idx].trueRisk < bestRisk)
                    {
                        bestRisk = board._cells[idx].trueRisk;
                        best = idx;
                    }
                }
                if (best < 0) break;
                board._cells[best].safeZone = true;
                if (board._cells[best].trueRisk > preset.safeRiskCeiling)
                    board._cells[best].trueRisk = preset.safeRiskCeiling;
            }

            // 4) 初始显示值 = 真实值（规则：开局所有格子颜色都正确标记）
            for (int i = 0; i < total; i++)
            {
                var cell = board._cells[i];
                cell.displayedRisk = preset.revealAllAtStart ? cell.trueRisk : 0f;
                board._cells[i] = cell;
            }

            return board;
        }

        // ============================================================ 漂移
        /// <summary>
        /// 施加概率漂移。真实值变化，显示值不动 → 颜色不再代表真相。
        /// </summary>
        /// <param name="escalation">回合递增带来的幅度倍数。</param>
        public void Drift(DriftProfile profile, ref DeterministicRandom rng, float escalation)
        {
            if (profile == null) return;
            int passes = profile.driftPassesPerTurn < 1 ? 1 : profile.driftPassesPerTurn;

            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < _cells.Length; i++)
                {
                    if (profile.skipRevealedCells && _cells[i].revealed) continue;

                    float magnitude = profile.baseDriftMagnitude
                                      * (1f + rng.NextCentered() * profile.magnitudeVariance)
                                      * escalation;
                    if (magnitude < 0f) magnitude = 0f;

                    float increaseChance = profile.increaseChance;
                    if (profile.riskMomentum > 0f)
                    {
                        // 概率越高越容易继续升高：风险自我强化
                        increaseChance = Lerp(increaseChance, Clamp01(_cells[i].trueRisk), profile.riskMomentum);
                    }

                    bool up = rng.Roll(increaseChance);
                    float v = _cells[i].trueRisk + (up ? magnitude : -magnitude);
                    _cells[i].trueRisk = Clamp(v, profile.minRisk, profile.maxRisk);
                }
            }
        }

        // ============================================================ 格子操作
        /// <summary>把显示值刷新为真实值（探查类道具 / 初始标记）。</summary>
        public void RefreshDisplay(int x, int y)
        {
            if (!InBounds(x, y)) return;
            int i = Index(x, y);
            _cells[i].displayedRisk = _cells[i].trueRisk;
        }

        /// <summary>修改真实概率。refreshDisplay 决定玩家能否看到改动结果。</summary>
        public void ModifyTrueRisk(int x, int y, float delta, bool refreshDisplay, DriftProfile clampSource)
        {
            if (!InBounds(x, y)) return;
            int i = Index(x, y);
            float lo = clampSource != null ? clampSource.minRisk : 0f;
            float hi = clampSource != null ? clampSource.maxRisk : 1f;
            _cells[i].trueRisk = Clamp(_cells[i].trueRisk + delta, lo, hi);
            if (refreshDisplay) _cells[i].displayedRisk = _cells[i].trueRisk;
        }

        /// <summary>标记为已翻开，并记录期望雷数快照。</summary>
        public void MarkRevealed(int x, int y)
        {
            if (!InBounds(x, y)) return;
            int i = Index(x, y);
            _cells[i].revealed = true;
            _cells[i].flagged = false;
            _cells[i].revealedExpectation = LiveNeighborExpectation(x, y);
        }

        /// <summary>
        /// 记录「这一格炸过」，带上是哪个座位踩的。
        /// 与 MarkRevealed 的区别：翻开是「安全通过」，炸过是「这里确实是雷」，
        /// 两者都是玩家必须记住的信息，但视觉上要能分辨。
        /// </summary>
        public void MarkBlasted(int x, int y, byte seat)
        {
            if (!InBounds(x, y)) return;
            int i = Index(x, y);
            _cells[i].blasted = true;
            _cells[i].blastedSeat = seat;
            _cells[i].revealed = false;
            // 踩爆的格子必然是雷，插旗语义已经由「疤痕」承担，避免同时出现两种标记
            _cells[i].flagged = false;
        }

        public void ToggleFlag(int x, int y)
        {
            if (!InBounds(x, y)) return;
            int i = Index(x, y);
            if (_cells[i].revealed) return;
            _cells[i].flagged = !_cells[i].flagged;
        }

        /// <summary>清空全盘翻开状态（用于惩罚规则）。</summary>
        public void ClearRevealed()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i].revealed = false;
                _cells[i].flagged = false;
            }
        }

        public int RevealedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cells.Length; i++) if (_cells[i].revealed) n++;
                return n;
            }
        }

        /// <summary>全盘未被翻开格子的平均真实风险，用于 AI 与结算统计。</summary>
        public float AverageUnrevealedRisk()
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i].revealed) continue;
                sum += _cells[i].trueRisk;
                n++;
            }
            return n == 0 ? 0f : sum / n;
        }

        // ============================================================ 工具
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
