using System.Text;
using BombTavern.Core;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 无头自动验证探针。
    ///
    /// 仅当启动参数包含 -btAutoplay 时生效：
    ///   1) 把本地座位也交给 AI（自动代打）
    ///   2) 跑完整局，结束或超时后把统计写入日志
    ///   3) 以退出码 0/1 结束进程
    ///
    /// 这样可以在命令行里真正跑一遍玩法，用于验证"能不能跑通"。
    /// 例：BombTavern.exe -batchmode -nographics -logFile run.log -btAutoplay
    /// </summary>
    [DisallowMultipleComponent]
    public class AutoplayHeadlessProbe : MonoBehaviour
    {
        const string Arg = "-btAutoplay";

        [Tooltip("对局驱动。留空则自动查找。")]
        public GameDirector director;

        [Tooltip("最长等待时间（秒），超时判定失败。")]
        // 一局可能有 70+ 回合，而 AI 每步都带思考延迟；150 秒会把正常的长局误判成失败。
        public float timeoutSeconds = 300f;

        [Tooltip("要截图时等待多少秒（此时对局已经跑起来，HUD 有内容可看）。")]
        public float screenshotDelay = 4f;

        // 由命令行 -btShot <路径> 指定；用于给 HUD 拍运行时截图
        string _shotPath;
        bool _shotTaken;
        float _shotTimer;
        bool _quitAfterShot;
        float _quitAfterShotAt = -1f;

        bool _active;
        bool _reported;
        bool _quitPending;
        int _exitCode;
        float _elapsed;

        void Awake()
        {
            _active = HasAutoplayArg();
            if (!_active)
            {
                enabled = false;
                return;
            }
            if (director == null) director = FindObjectOfType<GameDirector>();
            Debug.Log("[无头自检] 检测到 -btAutoplay，启用自动验证。");
        }

        static bool HasAutoplayArg()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], Arg, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>解析 -btShot &lt;路径&gt;：在开局若干秒后给 HUD 拍一张运行时截图。</summary>
        static string ReadShotArg() => ReadStringArg("-btShot");

        /// <summary>读一个 "-key value" 形式的字符串参数，没有则返回 null。</summary>
        static string ReadStringArg(string key)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], key, System.StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        /// <summary>读一个 "-key value" 形式的浮点参数，没有则返回 fallback。</summary>
        static float ReadFloatArg(string key, float fallback)
        {
            var s = ReadStringArg(key);
            return !string.IsNullOrEmpty(s) &&
                   float.TryParse(s, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out float v)
                ? v : fallback;
        }

        void Start()
        {
            if (director == null)
            {
                Finish("GameDirector 缺失，场景可能没有正确装配", 1);
                return;
            }

            director.SetAutoPlayLocalSeat(true);
            director.Event += OnGameEvent;
            _shotPath = ReadShotArg();
            _quitAfterShot = HasArg("-btQuitAfterShot");

            // 联机主机没人点「开始游戏」，所以这里延迟一小段等客户端连上来后自动开局。
            // 延迟可用 -btHostDelay <秒> 覆盖，保证自检里客户端一定先连上。
            hostStartDelay = ReadFloatArg("-btHostDelay", hostStartDelay);
            // 联机自检时客户端要等主机开局，截图时机也要相应推后
            screenshotDelay = ReadFloatArg("-btShotDelay", screenshotDelay);

            var lan = FindObjectOfType<Net.LanNetworkManager>();
            if (lan != null && lan.enabled && lan.IsHost) _lan = lan;
            Debug.Log("[无头自检] 已开启自动代打，开始跑完整局…" +
                      (string.IsNullOrEmpty(_shotPath) ? "" : $"（将在 {screenshotDelay:F0}s 后截图到 {_shotPath}）"));
        }

        void OnGameEvent(GameEvent e)
        {
            if (e.type != GameEventType.GameOver) return;
            if (_reported) return;
            Finish(null, 0);
        }

        [Header("联机自检")]
        [Tooltip("主机自动开局前等待客户端连接的时间（秒）。")]
        public float hostStartDelay = 4f;

        Net.LanNetworkManager _lan;
        bool _hostStarted;

        void Update()
        {
            if (_quitAfterShotAt > 0f && Time.unscaledTime >= _quitAfterShotAt)
            {
                Application.Quit(0);
                return;
            }
            if (_lan != null && !_hostStarted && _elapsed >= hostStartDelay && director.Session == null)
            {
                _hostStarted = true;
                _lan.StartGame();
                Debug.Log($"[无头自检] 主机等待 {hostStartDelay:F1}s 后自动开局，等待客户端…");
            }

            if (_reported)
            {
                if (_quitPending)
                {
                    _quitPending = false;
                    Application.Quit(_exitCode);
                }
                return;
            }

            _elapsed += Time.unscaledDeltaTime;

            if (!_shotTaken && !string.IsNullOrEmpty(_shotPath))
            {
                _shotTimer += Time.unscaledDeltaTime;
                if (_shotTimer >= screenshotDelay)
                {
                    _shotTaken = true;
                    ScreenCapture.CaptureScreenshot(_shotPath);
                    Debug.Log($"[无头自检] 已请求截图：{_shotPath}");
                    if (_quitAfterShot) _quitAfterShotAt = Time.unscaledTime + 2.5f;
                }
            }

            if (_elapsed > timeoutSeconds)
                Finish($"超时 {timeoutSeconds:F0} 秒仍未结束对局", 1);
        }

        static bool HasArg(string key)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], key, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ============================================================ 报告
        void Finish(string problem, int exitCode)
        {
            _reported = true;
            _exitCode = exitCode;
            _quitPending = true;

            var s = director != null ? director.Session : null;
            var sb = new StringBuilder();

            sb.AppendLine("================ 无头玩法自检结果 ================");
            if (s == null)
            {
                sb.AppendLine("结果: FAIL ❌ 对局根本没有初始化（GameDirector.Session 为空）");
                sb.AppendLine("================================================");
                Debug.LogError(sb.ToString());
                return;
            }

            sb.AppendLine($"种子            : {s.Seed}");
            sb.AppendLine($"难度            : {(s.ActiveDifficulty != null ? s.ActiveDifficulty.displayName : "空")}");
            sb.AppendLine($"共用盘面        : {(s.SharedBoard != null ? $"{s.SharedBoard.Columns}x{s.SharedBoard.Rows}" : "无")}");
            sb.AppendLine($"座位数          : {s.Players.Length}");
            sb.AppendLine($"推进回合数      : {s.TurnIndex}   轮次 {s.RoundIndex}");
            sb.AppendLine($"对局结束        : {s.IsOver}   胜者座位={s.WinnerSeat}   原因={s.OverReason}");
            sb.AppendLine($"盘面已翻开      : {s.SharedBoard?.RevealedCount}/{s.SharedBoard?.CellCount}" +
                          $"   平均残余风险={s.SharedBoard?.AverageUnrevealedRisk():P1}");
            sb.AppendLine("--- 各玩家 ---");

            for (int i = 0; i < s.Players.Length; i++)
            {
                var p = s.Players[i];
                sb.AppendLine($"  [{i}] {p.displayName} bot={p.isBot} 机会={p.lives}/{p.maxLives} 出局={p.eliminated} " +
                              $"翻开={p.totalReveals} 被炸={p.totalHits} 背包={p.inventory.Count} " +
                              $"用道具={p.totalItemsUsed} 摸到={p.totalItemsFound} 探查={p.totalProbes} " +
                              $"改概率={p.totalModifies} 保底计数={p.pityCounter}");
            }

            bool ok = problem == null && s.TurnIndex >= 8;
            sb.AppendLine(problem != null ? $"问题            : {problem}" : "问题            : 无");
            sb.AppendLine(ok ? "结果: PASS ✅ 玩法跑通（开局 → 回合推进 → AI 行动 → 结束）"
                             : "结果: FAIL ❌");
            sb.AppendLine("================================================");

            if (ok)
            {
                Debug.Log(sb.ToString());
                _exitCode = 0;
            }
            else
            {
                Debug.LogError(sb.ToString());
                _exitCode = exitCode != 0 ? exitCode : 1;
            }
        }
    }
}
