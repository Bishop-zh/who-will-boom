using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BombTavern.EditorTools
{
    /// <summary>
    /// 构建 Windows 独立播放器，用于真机验证玩法（批处理模式下 Play 模式起不来）。
    ///
    /// 构建：-executeMethod BombTavern.EditorTools.PlayerBuilder.BuildWindows
    /// 运行：Build/BombTavern/BombTavern.exe -batchmode -nographics -logFile run.log -btAutoplay
    /// </summary>
    public static class PlayerBuilder
    {
        const string PlayScene = "Assets/Scenes/BombTavern_Play.unity";
        const string OutputDir = "Build/WhoWillBOOM_2026-10-08";
        const string ExeName = "BombTavern.exe";

        [MenuItem("工具/炸弹酒馆/7. 构建 Windows 独立版", false, 17)]
        public static void BuildWindowsFromMenu() => BuildWindows();

        /// <summary>
        /// 对照实验：只构建原始酒馆场景（不含玩法层），用于定位序列化损坏来自哪一层。
        /// -executeMethod BombTavern.EditorTools.PlayerBuilder.BuildTavernSmoke
        /// </summary>
        public static void BuildTavernSmoke()
        {
            const string scene = "Assets/Scenes/LiarsBar_BombTavern.unity";
            const string dir = "Build/TavernSmoke";

            if (!File.Exists(scene))
            {
                Debug.LogError($"找不到 {scene}");
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(dir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(dir, "TavernSmoke.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[对照构建] {report.summary.result}  输出 {dir}/TavernSmoke.exe");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public static void BuildWindows()
        {
            if (!File.Exists(PlayScene))
            {
                Debug.LogError($"[炸弹酒馆] 找不到 {PlayScene}，请先生成可玩场景。");
                EditorApplication.Exit(1);
                return;
            }

            // 新版开局菜单就在真实对局场景内。旧 BombTavern_Menu 只保留为历史生成工具，
            // 不再放进播放器，避免启动时先闪出一帧旧 IMGUI 再跳转。
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(PlayScene, true),
            };
            EditorBuildSettings.scenes = scenes.ToArray();

            string exePath = Path.Combine(OutputDir, ExeName);

            Directory.CreateDirectory(OutputDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { PlayScene },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            Debug.Log("[炸弹酒馆] 开始构建独立播放器…");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[炸弹酒馆] 构建成功：{exePath}（{summary.totalSize / (1024 * 1024)} MB，耗时 {summary.totalTime.TotalSeconds:F1}s）");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[炸弹酒馆] 构建失败：{summary.result}，错误 {summary.totalErrors} 条");
                EditorApplication.Exit(1);
            }
        }
    }
}
