using UnityEditor;
using UnityEngine;

namespace BombTavern.EditorTools
{
    /// <summary>
    /// 把「FBX 模型 + Avatar + AnimatorController」组装成 Resources 下的预制体。
    ///
    /// 为什么必须这么做：
    /// Generic 骨骼的 Animator 靠 **Avatar** 把动画剪辑里的节点路径映射到骨架上。
    /// 运行时 `AddComponent&lt;Animator&gt;()` 新建出来的组件 `avatar` 永远是 null，
    /// 只赋 `runtimeAnimatorController` 是播不出任何动画的 —— 表现就是「角色站着不动」。
    ///
    /// 而 Avatar 是 FBX 的**导入子资产**，运行时没法按名字 Resources.Load 到它，
    /// 所以只能在编辑器里把 Avatar 烘进预制体，运行时直接实例化。
    /// </summary>
    public static class CharacterRigBuilder
    {
        const string FbxDir = "Assets/Resources/BombTavern/Characters";
        const string ControllerDir = "Assets/Resources/BombTavern/Controllers";
        const string PrefabDir = "Assets/Resources/BombTavern/Prefabs";

        static readonly string[] Ids =
        {
            "black_sphere", "red_bundle", "brown_cracker", "gray_shell",
        };

        [MenuItem("工具/炸弹酒馆/3. 生成角色骨骼预制体", false, 12)]
        public static void BuildFromMenu() => BuildAll();

        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets/Resources/BombTavern", "Prefabs");

            int ok = 0, missingAvatar = 0;

            foreach (var id in Ids)
            {
                var fbxPath = $"{FbxDir}/Bomb_{id}.fbx";
                var ctrlPath = $"{ControllerDir}/Bomb_{id}_Controller.controller";

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (model == null)
                {
                    Debug.LogError($"[角色预制体] 找不到模型 {fbxPath}");
                    continue;
                }

                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ctrlPath);
                var avatar = FindAvatar(fbxPath);

                // 实例化一份临时副本，配好 Animator 再存成预制体。
                // 直接改 FBX 的导入器不会得到「带 Animator 的预制体」。
                var temp = (GameObject)Object.Instantiate(model);
                temp.name = "Bomb_" + id;

                var animator = temp.GetComponent<Animator>();
                if (animator == null) animator = temp.AddComponent<Animator>();

                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var prefabPath = $"{PrefabDir}/Bomb_{id}.prefab";
                PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
                Object.DestroyImmediate(temp);

                if (avatar == null) missingAvatar++;

                Debug.Log($"[角色预制体] {id}: Avatar={(avatar != null)} 控制器={(controller != null)} → {prefabPath}");
                if (avatar != null && controller != null) ok++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[角色预制体] 完成：{ok}/{Ids.Length} 个可用。" +
                      (missingAvatar > 0
                          ? $" 有 {missingAvatar} 个模型没生成 Avatar —— 请检查该 FBX 的 Rig 页签是否设为 Generic 且 Root 骨骼不为空。"
                          : ""));
        }

        /// <summary>取 FBX 的 Avatar 子资产。Generic 骨骼的 Avatar 由导入器自动生成。</summary>
        static Avatar FindAvatar(string fbxPath)
        {
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (a is Avatar av) return av;
            return null;
        }
    }
}
