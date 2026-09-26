using System.Collections.Generic;
using BombTavern.Config;
using BombTavern.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BombTavern.EditorTools
{
    /// <summary>
    /// 生成开始界面场景（BombTavern_Menu）：摄像机 + SessionContext + MainMenuUI。
    /// 批处理入口： -executeMethod BombTavern.EditorTools.MenuSceneBuilder.BuildMenuScene
    /// </summary>
    public static class MenuSceneBuilder
    {
        public const string MenuScene = "Assets/Scenes/BombTavern_Menu.unity";
        public const string PlayScene = "Assets/Scenes/BombTavern_Play.unity";

        [MenuItem("工具/炸弹酒馆/5. 生成开始界面场景", false, 14)]
        public static void BuildMenuSceneFromMenu() => BuildMenuScene();

        public static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "BombTavern_Menu";

            // ---- 摄像机
            var camGo = new GameObject("MenuCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.035f, 0.030f, 0.040f);
            cam.fieldOfView = 60f;
            cam.transform.position = new Vector3(0f, 1.4f, 2.2f);
            cam.transform.localEulerAngles = new Vector3(6f, 180f, 0f);
            camGo.AddComponent<AudioListener>();

            // ---- 一点氛围光，避免界面后面漆黑一片看不出是 3D 场景
            var lightGo = new GameObject("MenuLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.5f);
            light.intensity = 1.4f;
            light.range = 14f;
            light.transform.position = new Vector3(0f, 2.6f, 1.4f);

            // ---- 会话上下文（跨场景携带设置）
            var ctxGo = new GameObject("SessionContext");
            var ctx = ctxGo.AddComponent<SessionContext>();
            ctx.skinCatalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(
                GameAssetCreator.SkinDir + "/SkinCatalog.asset");
            ctx.playerName = "红雷";
            ctx.skinId = ctx.skinCatalog != null && ctx.skinCatalog.defaultSkin != null
                ? ctx.skinCatalog.defaultSkin.skinId
                : "round_black";

            // ---- 开始界面 UI
            var uiGo = new GameObject("MainMenuUI");
            var ui = uiGo.AddComponent<MainMenuUI>();
            ui.playSceneName = "BombTavern_Play";

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, MenuScene);

            // 开始界面排第一，对局场景第二，保证 Build 后先进入菜单
            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MenuScene, true),
                new EditorBuildSettingsScene(PlayScene, true),
            };
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();

            Debug.Log($"[炸弹酒馆] 开始界面已生成：{MenuScene}");
        }
    }
}
