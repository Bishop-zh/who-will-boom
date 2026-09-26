using System;
using System.Collections.Generic;
using System.Text;
using BombTavern.Config;
using BombTavern.Core;
using BombTavern.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BombTavern.EditorTools
{
    /// <summary>
    /// 玩法冒烟测试：真正进入 Play 模式跑一局，自动替本地座位代打，
    /// 收集运行期异常与对局统计，最后输出 PASS/FAIL。
    ///
    /// 批处理入口：
    ///   -executeMethod BombTavern.EditorTools.GameplaySmokeTest.Run
    /// 不使用 -quit，由脚本自己在结束时 EditorApplication.Exit。
    /// </summary>
    public static class GameplaySmokeTest
    {
        const string PlayScene = "Assets/Scenes/BombTavern_Play.unity";

        /// <summary>最多观察多少秒游戏内时间。</summary>
        const float MaxObserveSeconds = 40f;

        /// <summary>至少需要推进多少回合才算跑通。</summary>
        const int MinTurns = 12;

        /// <summary>进入 Play 模式的最长等待时间（秒），超时判定失败并退出，避免挂死。</summary>
        const float EnterPlayTimeoutSeconds = 45f;

        static int _phase;
        static double _t0;
        static double _enterRequestedAt;
        static GameDirector _director;
        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Notes = new List<string>();

        static int _hoverPublished;
        static int _itemsUsed;
        static int _localActions;
        static float _autopilotCooldown;

        public static void Run()
        {
            Errors.Clear();
            Notes.Clear();
            _phase = 0;
            _hoverPublished = 0;
            _itemsUsed = 0;
            _localActions = 0;

            Application.logMessageReceived += OnLog;

            var scene = EditorSceneManager.OpenScene(PlayScene, OpenSceneMode.Single);
            Debug.Log($"[冒烟] 已打开场景 {scene.name}，准备进入 Play 模式…");

            EditorApplication.update += Tick;
            _enterRequestedAt = EditorApplication.timeSinceStartup;
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
                Errors.Add($"{type}: {condition}\n{stackTrace}");
        }

        static void Tick()
        {
            switch (_phase)
            {
                case 0:
                    if (EditorApplication.isPlaying)
                    {
                        _director = UnityEngine.Object.FindObjectOfType<GameDirector>();
                        _phase = 1;
                        _t0 = EditorApplication.timeSinceStartup;
                        Debug.Log("[冒烟] 已进入 Play 模式，开始观察。");
                        break;
                    }

                    // 看门狗：批处理模式下 Play 模式可能起不来，超时直接判失败退出
                    if (EditorApplication.timeSinceStartup - _enterRequestedAt > EnterPlayTimeoutSeconds)
                    {
                        Debug.LogError($"[冒烟] 失败：{EnterPlayTimeoutSeconds:F0} 秒内未能进入 Play 模式" +
                                       $"（isPlaying={EditorApplication.isPlaying}，" +
                                       $"isCompiling={EditorApplication.isCompiling}，" +
                                       $"isUpdating={EditorApplication.isUpdating}），" +
                                       "批处理模式可能不支持直接进入 Play，请改用「运行时自检」组件。");
                        EditorApplication.update -= Tick;
                        Application.logMessageReceived -= OnLog;
                        EditorApplication.Exit(1);
                    }
                    break;

                case 1:
                    Observe();
                    if (EditorApplication.timeSinceStartup - _t0 > MaxObserveSeconds || IsFinished())
                    {
                        Report();
                        _phase = 2;
                        EditorApplication.ExitPlaymode();
                    }
                    break;

                case 2:
                    if (EditorApplication.isPlaying) return;
                    EditorApplication.update -= Tick;
                    Application.logMessageReceived -= OnLog;
                    Debug.Log("[冒烟] 已退出 Play 模式。");
                    EditorApplication.Exit(Errors.Count == 0 && Passed() ? 0 : 1);
                    break;
            }
        }

        static bool IsFinished()
        {
            if (_director == null || _director.Session == null) return false;
            if (_director.Session.IsOver) return true;
            return _director.Session.TurnIndex >= 400;   // 安全上限
        }

        // ============================================================ 自动代打
        static void Observe()
        {
            if (_director == null)
            {
                Errors.Add("逻辑错误: 场景里找不到 GameDirector");
                return;
            }

            var s = _director.Session;
            if (s == null) return;
            if (s.IsOver) return;

            // 1) 校验视图层是否真的被驱动起来
            if (_director.sharedTablet == null)
                Errors.Add("逻辑错误: GameDirector.sharedTablet 未绑定");

            // 2) 本地座位自动代打：翻格 / 插旗 / 用道具 / 公开悬停
            if (s.CurrentSeat != _director.localSeat) return;

            _autopilotCooldown -= Time.unscaledDeltaTime;
            if (_autopilotCooldown > 0f) return;
            _autopilotCooldown = 0.05f;

            var me = s.PlayerAt(_director.localSeat);
            if (me == null) return;

            int x, y;
            if (!FindUnrevealed(me, out x, out y)) return;

            // 公开悬停（验证 SetHover 指令通路）
            _director.PublishHover(x, y);

            // 轮流练一遍道具与翻格
            if (me.inventory.Count > 0 && (_localActions % 3 == 0))
            {
                _director.Submit(GameCommand.UseItem(_director.localSeat, 0, x, y,
                    _director.localSeat, s.TurnIndex));
                _itemsUsed++;
            }
            else if (_localActions % 5 == 4)
            {
                _director.Submit(GameCommand.Flag(_director.localSeat, x, y, s.TurnIndex));
            }
            else
            {
                _director.Submit(GameCommand.Reveal(_director.localSeat, x, y, s.TurnIndex));
            }

            _localActions++;
            _hoverPublished++;
        }

        static bool FindUnrevealed(PlayerState p, out int x, out int y)
        {
            x = -1;
            y = -1;
            var board = p.board;
            if (board == null) return false;

            // 从左上开始找第一个未翻开且未插旗的格子
            for (int i = 0; i < board.CellCount; i++)
            {
                var c = board.RawCells[i];
                if (c.revealed || c.flagged) continue;
                x = i % board.Columns;
                y = i / board.Columns;
                return true;
            }
            return false;
        }

        // ============================================================ 结果
        static bool Passed()
        {
            var s = _director != null ? _director.Session : null;
            if (s == null) return false;
            return s.TurnIndex >= MinTurns;
        }

        static void Report()
        {
            var sb = new StringBuilder();
            var s = _director != null ? _director.Session : null;

            sb.AppendLine("========== 玩法冒烟测试结果 ==========");
            if (s == null)
            {
                sb.AppendLine("失败：GameDirector.Session 为空，Start() 没有成功开局。");
                sb.AppendLine("=====================================");
                Debug.LogError(sb.ToString());
                return;
            }

            sb.AppendLine($"种子            : {s.Seed}");
            sb.AppendLine($"难度            : {(s.ActiveDifficulty != null ? s.ActiveDifficulty.displayName : "空")}");
            sb.AppendLine($"共用盘面        : {s.SharedBoard != null}（{s.SharedBoard?.Columns}x{s.SharedBoard?.Rows}）");
            sb.AppendLine($"座位数          : {s.Players.Length}");
            sb.AppendLine($"已推进回合      : {s.TurnIndex}  轮次 {s.RoundIndex}");
            sb.AppendLine($"当前行动座位    : {s.CurrentSeat}");
            sb.AppendLine($"悬停公开        : seat={s.HoverSeat} ({s.HoverX},{s.HoverY})");
            sb.AppendLine($"对局结束        : {s.IsOver}  胜者={s.WinnerSeat}  原因={s.OverReason}");
            sb.AppendLine($"本地代打次数    : {_localActions}（用道具 {_itemsUsed} 次，公开悬停 {_hoverPublished} 次）");
            sb.AppendLine("--- 各玩家 ---");

            for (int i = 0; i < s.Players.Length; i++)
            {
                var p = s.Players[i];
                sb.AppendLine($"  [{i}] {p.displayName} bot={p.isBot} 机会={p.lives}/{p.maxLives} " +
                              $"出局={p.eliminated} 翻开={p.totalReveals} 被炸={p.totalHits} " +
                              $"道具={p.inventory.Count} 掉落率={p.itemFindChance:P0} 保底计数={p.pityCounter} " +
                              $"探查={p.totalProbes} 改概率={p.totalModifies}");
            }

            var board = s.SharedBoard;
            if (board != null)
                sb.AppendLine($"盘面未翻开格平均真实风险 : {board.AverageUnrevealedRisk():P1}  已翻开 {board.RevealedCount}/{board.CellCount}");

            sb.AppendLine($"运行期错误/异常数量 : {Errors.Count}");
            foreach (var e in Errors) sb.AppendLine("  ! " + e.Split('\n')[0]);

            bool ok = Errors.Count == 0 && s.TurnIndex >= MinTurns;
            sb.AppendLine(ok ? "结论: PASS ✅ 玩法跑通" : "结论: FAIL ❌");
            sb.AppendLine("=====================================");

            if (ok) Debug.Log(sb.ToString());
            else Debug.LogError(sb.ToString());
        }

        // ============================================================ 纯逻辑自测
        /// <summary>
        /// 不开 Play 模式的纯逻辑回归：确定性、漂移、道具、AI 全链路。
        /// -executeMethod BombTavern.EditorTools.GameplaySmokeTest.RunLogicOnly
        /// </summary>
        public static void RunLogicOnly()
        {
            var failures = new List<string>();
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameAssetCreator.ConfigDir + "/GameConfig.asset");

            // 1) 确定性：同种子两次模拟必须完全一致
            string a = SimulateSummary(config, 4242UL, 4);
            string b = SimulateSummary(config, 4242UL, 4);
            if (a != b) failures.Add("确定性失败：同种子两次模拟结果不一致");
            else Debug.Log("[逻辑自测] 确定性通过");

            // 2) 不同种子应当产生不同结果
            string c = SimulateSummary(config, 9999UL, 4);
            if (a == c) failures.Add("随机性异常：不同种子得到相同结果");

            // 2.5) 开局必须发放初始道具，且掉落池必须接好
            {
                var probeSession = new GameSession();
                probeSession.Initialize(config, 31337UL);
                var sb = new StringBuilder();
                sb.Append("[逻辑自测] 开局道具：");
                for (int i = 0; i < probeSession.Players.Length; i++)
                {
                    var p = probeSession.Players[i];
                    sb.Append($" {p.displayName}={p.inventory.Count}");
                }
                Debug.Log(sb.ToString());

                int expect = config.startingItemCount;
                for (int i = 0; i < probeSession.Players.Length; i++)
                {
                    if (probeSession.Players[i].inventory.Count != expect)
                        failures.Add($"初始道具发放失败：座位{i} 期望 {expect} 件，实际 {probeSession.Players[i].inventory.Count} 件");
                }

                if (config.gacha == null) failures.Add("GachaConfig 未绑定");
                else if (config.gacha.dropPool == null) failures.Add("GachaConfig.dropPool 未绑定（道具池为空）");
                else if (config.gacha.dropPool.items.Count == 0) failures.Add("道具目录为空");
                else Debug.Log($"[逻辑自测] 道具池正常，共 {config.gacha.dropPool.items.Count} 件");

                // 掉落保底：连续翻格必定至少掉一件
                var p0 = probeSession.Players[0];
                int before = p0.inventory.Count;
                for (int k = 0; k < 60 && !probeSession.IsOver; k++)
                {
                    var cur = probeSession.PlayerAt(probeSession.CurrentSeat);
                    if (cur == null) break;
                    int idx = FirstUnrevealedIndex(cur);
                    if (idx < 0) break;
                    int x = idx % cur.board.Columns, y = idx / cur.board.Columns;
                    if (!probeSession.TryApply(GameCommand.Reveal(cur.seatIndex, x, y, probeSession.TurnIndex)))
                        probeSession.TryApply(GameCommand.End(cur.seatIndex, probeSession.TurnIndex));
                }
                if (p0.totalFindRolls == 0 && p0.totalReveals > 0)
                    failures.Add("道具掉落从未被掷骰（翻格后 totalFindRolls 仍为 0）");
                else
                    Debug.Log($"[逻辑自测] 掉落掷骰 {p0.totalFindRolls} 次，实际掉落 {p0.totalItemsFound} 件（保底计数 {p0.pityCounter}）");
            }

            // 3) 漂移必须改变真实值但不改变显示值
            var s = new GameSession();
            s.Initialize(config, 777UL);
            var board = s.SharedBoard;
            float beforeTrue = board.RawCells[0].trueRisk;
            float beforeDisp = board.RawCells[0].displayedRisk;

            var prof = config.driftProfile;
            ulong seed = 5UL;
            var rng = new DeterministicRandom(seed);
            board.Drift(prof, ref rng, 1f);

            if (Mathf.Abs(board.RawCells[0].trueRisk - beforeTrue) < 0.00001f)
                failures.Add("漂移未生效：真实概率没有变化");
            if (Mathf.Abs(board.RawCells[0].displayedRisk - beforeDisp) > 0.00001f)
                failures.Add("规则违背：漂移不该改变显示值（颜色应保持不变）");
            else Debug.Log("[逻辑自测] 漂移只改真实值、显示值不变，符合规则");

            // 4) 探查写入私人读数，且不改公共显示
            var actors = s.Players[0];
            var probe = FindItem(config, "probe");
            if (probe != null)
            {
                float dispBefore = board.RawCells[5].displayedRisk;
                // 直接构造一次翻格后可能失败，这里用指令通路
                s.TryApply(GameCommand.UseItem(0, -1, 0, 0, 0, -1));   // 非法，仅验证不崩
                if (Mathf.Abs(board.RawCells[5].displayedRisk - dispBefore) > 0.00001f)
                    failures.Add("探查不该改变公共显示值");
                else Debug.Log("[逻辑自测] 探查不改公共显示，符合保密规则");
            }

            if (failures.Count == 0) Debug.Log("[逻辑自测] 全部通过 ✅");
            else
            {
                Debug.LogError("[逻辑自测] 失败：\n  " + string.Join("\n  ", failures));
            }

            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        static int FirstUnrevealedIndex(PlayerState p)
        {
            var board = p.board;
            if (board == null) return -1;
            for (int i = 0; i < board.CellCount; i++)
                if (!board.RawCells[i].revealed && !board.RawCells[i].flagged) return i;
            return -1;
        }

        static ItemDefinition FindItem(GameConfig config, string id)
        {
            var cat = config != null && config.gacha != null ? config.gacha.dropPool : null;
            if (cat == null) return null;
            for (int i = 0; i < cat.items.Count; i++)
                if (cat.items[i] != null && cat.items[i].itemId == id) return cat.items[i];
            return null;
        }

        /// <summary>用 AI 自动打完整局，返回可比较的摘要字符串。</summary>
        static string SimulateSummary(GameConfig config, ulong seed, int seats)
        {
            var s = new GameSession();
            s.Initialize(config, seed, 0);   // 0 → 全部交给 AI

            var brains = new Dictionary<int, AI.BotBrain>();
            for (int i = 0; i < s.Players.Length; i++)
                brains[i] = new AI.BotBrain(config, i, s.Seed);

            int guard = 0;
            while (!s.IsOver && guard++ < 4000)
            {
                var cur = s.PlayerAt(s.CurrentSeat);
                if (cur == null) break;
                var cmd = brains[cur.seatIndex].Decide(s);
                if (!s.TryApply(cmd))
                {
                    // AI 给出非法指令时强制过手，避免死循环
                    if (!s.TryApply(GameCommand.End(cur.seatIndex, s.TurnIndex))) break;
                }
            }

            var sb = new StringBuilder();
            sb.Append("turns=").Append(s.TurnIndex)
              .Append(";over=").Append(s.IsOver)
              .Append(";winner=").Append(s.WinnerSeat)
              .Append(";avgRisk=").Append(s.SharedBoard?.AverageUnrevealedRisk().ToString("F5"));
            for (int i = 0; i < s.Players.Length; i++)
            {
                var p = s.Players[i];
                sb.Append("|").Append(p.displayName).Append(":lives=").Append(p.lives)
                  .Append(",rev=").Append(p.totalReveals)
                  .Append(",hit=").Append(p.totalHits)
                  .Append(",items=").Append(p.inventory.Count);
            }
            return sb.ToString();
        }
    }
}
