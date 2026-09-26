using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BombTavern.EditorTools
{
    /// <summary>
    /// 把 Blender 产出的带骨骼角色接进 Unity：
    ///   1) 配置 FBX 导入设置（Generic 骨架 + 把 take 重命名成干净的动作名）
    ///   2) 每个角色生成一个 AnimatorController（Idle / Aim / UseItem / Hit 四个状态）
    ///
    /// 资源路径约定（与 Tools/Blender 下的生成脚本一致）：
    ///   Assets/Resources/BombTavern/Characters/Bomb_&lt;skinId&gt;.fbx
    ///   Assets/Resources/BombTavern/Controllers/Bomb_&lt;skinId&gt;_Controller.controller
    /// 走 Resources 是为了运行时能用 Resources.Load 取到（AssetDatabase 只在编辑器可用）。
    /// </summary>
    public static class RiggedCharacterSetup
    {
        const string CharDir = "Assets/Resources/BombTavern/Characters";
        const string CtrlDir = "Assets/Resources/BombTavern/Controllers";

        /// <summary>动作名。第一个是默认状态。</summary>
        static readonly string[] ClipNames = { "Idle", "Aim", "UseItem", "Hit" };

        static readonly string[] SkinIds =
        {
            "black_sphere", "red_bundle", "brown_cracker", "gray_shell",
        };

        [MenuItem("工具/炸弹酒馆/6. 配置骨骼角色（导入 + 动画控制器）", false, 15)]
        public static void SetupFromMenu() => Setup();

        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(CharDir))
            {
                Debug.LogError($"[骨骼角色] 找不到目录 {CharDir}。" +
                               "请先用 Tools/Blender 下的脚本生成角色。");
                return;
            }

            int configured = ConfigureImporters();
            int controllers = BuildControllers();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[骨骼角色] 导入配置完成 {configured} 个模型，生成 {controllers} 个动画控制器。");
        }

        // ============================================================ 导入设置
        static int ConfigureImporters()
        {
            int count = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CharDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx")) continue;
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer) continue;

                importer.animationType = ModelImporterAnimationType.Generic;
                // 仅设为 Generic 并不保证 Unity 会生成 Avatar；必须明确要求从当前
                // FBX 的骨架创建，否则 Animator 有控制器和剪辑却仍会静止。
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.importBlendShapes = false;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.optimizeGameObjects = false;   // 骨骼要能在运行时按名字找到（道具挂手）
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

                var source = importer.clipAnimations is { Length: > 0 }
                    ? importer.clipAnimations
                    : importer.defaultClipAnimations;

                var clips = new List<ModelImporterClipAnimation>();
                foreach (var clip in source)
                {
                    string name = NormalizeClipName(clip.name);
                    if (name == null) continue;         // 不是我们关心的动作，或重复项

                    clip.name = name;
                    clip.loopTime = name == "Idle";     // 只有待机循环
                    clip.loopPose = name == "Idle";
                    clips.Add(clip);
                }

                if (clips.Count == 0)
                {
                    Debug.LogWarning($"[骨骼角色] {path} 没找到可识别的动作，跳过。");
                    continue;
                }

                importer.clipAnimations = clips.ToArray();
                importer.SaveAndReimport();
                count++;

                Debug.Log($"[骨骼角色] {Path.GetFileName(path)} → Generic，动作 {clips.Count} 个");
            }

            return count;
        }

        /// <summary>
        /// 把 Blender 的 take 名整理成干净的动作名。
        /// Blender 导出的名字很啰嗦（如 BombRig|BombRig|Aim），而且同一个动作
        /// 在多次导入后会带 .001 / .002 后缀 —— 那些一律丢弃，避免出现重名状态。
        /// </summary>
        static string NormalizeClipName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            int bar = raw.LastIndexOf('|');
            string shortName = bar >= 0 ? raw.Substring(bar + 1) : raw;

            // 丢掉 .001 这类去重后缀
            int dot = shortName.IndexOf('.');
            if (dot > 0 && int.TryParse(shortName.Substring(dot + 1), out _)) return null;

            foreach (var known in ClipNames)
                if (known == shortName) return shortName;

            return null;
        }

        // ============================================================ 动画控制器
        static int BuildControllers()
        {
            Directory.CreateDirectory(CtrlDir);
            int count = 0;

            foreach (var id in SkinIds)
            {
                string fbx = $"{CharDir}/Bomb_{id}.fbx";
                if (!File.Exists(fbx))
                {
                    Debug.LogWarning($"[骨骼角色] 缺少 {fbx}，跳过。");
                    continue;
                }

                var clips = new AnimationClip[ClipNames.Length];
                bool complete = true;
                for (int i = 0; i < ClipNames.Length; i++)
                {
                    clips[i] = FindClip(fbx, ClipNames[i]);
                    if (clips[i] == null) complete = false;
                }

                if (!complete)
                {
                    Debug.LogWarning($"[骨骼角色] {id} 的动作不齐（{ClipNames.Length} 个里缺），跳过。");
                    continue;
                }

                string path = $"{CtrlDir}/Bomb_{id}_Controller.controller";
                AssetDatabase.DeleteAsset(path);

                var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                var machine = controller.layers[0].stateMachine;

                for (int i = 0; i < ClipNames.Length; i++)
                {
                    var state = machine.AddState(ClipNames[i]);
                    state.motion = clips[i];

                    // 关键：不要让状态回写默认值。
                    // 否则切到「没有动画化某根骨骼」的片段时，那根骨骼会被强制归零，
                    // 两个动作交叉淡入时就会出现抽一下的跳变。
                    state.writeDefaultValues = false;

                    if (i == 0) machine.defaultState = state;   // Idle 作为默认状态
                }

                EditorUtility.SetDirty(controller);
                count++;
            }

            return count;
        }

        /// <summary>从 FBX 里按名字取一个 AnimationClip。模型里可能还有别的子资源。</summary>
        static AnimationClip FindClip(string fbxPath, string clipName)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is AnimationClip clip && clip.name == clipName) return clip;
            }
            return null;
        }
    }
}
