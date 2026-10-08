using System;
using System.IO;
using System.Linq;
using BombTavern.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BombTavern.EditorTools
{
    public static class SettingsPrefabBuilder
    {
        const string Sheet = "Assets/Resources/BombTavern/UI/SettingsSheet.png";
        const string Prefab = "Assets/Resources/BombTavern/Prefabs/UI/SettingsPopup.prefab";
        const string Font = "Assets/Resources/BombTavern/UI/Fonts/NotoSansSC-Dynamic.asset";

        [MenuItem("工具/炸弹酒馆/10. 创建设置预制体", false, 20)]
        public static void CreateMissing()
        {
            if (File.Exists(Prefab))
            {
                RepairModalAnimators();
                Debug.Log("[设置预制体] 已存在，保留手工修改。");
                return;
            }
            var importer = AssetImporter.GetAtPath(Sheet) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("设置图集缺失", Sheet);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.spritesheet = new[]
            {
                Slice("SettingsTitle", 137, 85, 990, 225),
                Slice("SettingsMonitor", 22, 318, 112, 118),
                Slice("SettingsSound", 136, 318, 112, 118),
                Slice("SettingsGear", 248, 318, 112, 118),
                Slice("SettingsClose", 1122, 318, 114, 118),
                Slice("SettingsDropdown", 30, 469, 553, 103, 24),
                Slice("SettingsSliderTrack", 26, 724, 575, 62, 22),
                Slice("SettingsSliderKnob", 1124, 720, 87, 82),
                Slice("SettingsToggleOn", 330, 916, 250, 113, 24),
                Slice("SettingsButtonDark", 40, 1059, 256, 103, 22),
                Slice("SettingsButtonGold", 299, 1059, 274, 103, 22),
            };
            importer.SaveAndReimport();
            var root = SettingsPopupView.CreateTemplate();
            try
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Font);
                if (font == null) throw new FileNotFoundException("中文字体预制体资源缺失", Font);
                foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) label.font = font;
                Directory.CreateDirectory(Path.GetDirectoryName(Prefab));
                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                RepairModalAnimators();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[设置预制体] 已创建 " + Prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [MenuItem("工具/炸弹酒馆/11. 修复弹窗预制体组件", false, 21)]
        public static void RepairModalAnimators()
        {
            Repair(Prefab, "Panel");
            Repair("Assets/Resources/BombTavern/Prefabs/UI/RulesTutorial.prefab", "RulesTutorialPanel");
            AssetDatabase.SaveAssets();
        }

        static void Repair(string path, string panelName)
        {
            if (!File.Exists(path)) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var panel = root.GetComponentsInChildren<RectTransform>(true)
                    .FirstOrDefault(rect => rect.name == panelName);
                if (panel == null) throw new InvalidOperationException("弹窗预制体缺少 " + panelName + "：" + path);
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(panel.gameObject);
                if (missing > 0) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(panel.gameObject);
                if (panel.GetComponent<UiModalAnimator>() == null)
                    panel.gameObject.AddComponent<UiModalAnimator>();
                if (missing > 0) Debug.Log("[弹窗预制体] 已替换缺失动画组件：" + path);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static SpriteMetaData Slice(string name, int x, int top, int width, int height, int border = 0)
        {
            return new SpriteMetaData
            {
                name = name,
                rect = new Rect(x, 1254 - top - height, width, height),
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(.5f, .5f),
                border = new Vector4(border, border, border, border),
            };
        }
    }
}
