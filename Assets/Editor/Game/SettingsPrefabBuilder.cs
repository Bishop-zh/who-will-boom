using System;
using System.Collections.Generic;
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
        const string Sheet = "Assets/Resources/BombTavern/UI/SettingsSheetV2.png";
        const string Prefab = "Assets/Resources/BombTavern/Prefabs/UI/SettingsPopup.prefab";
        const string Font = "Assets/Resources/BombTavern/UI/Fonts/NotoSansSC-Dynamic.asset";

        [MenuItem("工具/炸弹酒馆/10. 创建设置预制体", false, 20)]
        public static void CreateMissing()
        {
            if (File.Exists(Prefab))
            {
                Debug.Log("[设置预制体] 已存在，保留手工修改。");
                return;
            }
            BuildFromSheet();
        }

        [MenuItem("工具/炸弹酒馆/12. 从新版图集重建设置预制体", false, 22)]
        public static void RebuildFromCurrentSheet() => BuildFromSheet();

        static void BuildFromSheet()
        {
            var currentSlices = AssetDatabase.LoadAllAssetsAtPath(Sheet).OfType<Sprite>().ToArray();
            if (currentSlices.Any(s => s.name == "SettingsSheetV2_0") && currentSlices.Any(s => s.name == "SettingsSheetV2_1"))
            {
                if (!File.Exists(Prefab))
                {
                    var template = SettingsPopupView.CreateTemplate();
                    try
                    {
                        var currentFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Font);
                        foreach (var label in template.GetComponentsInChildren<TMP_Text>(true)) label.font = currentFont;
                        Directory.CreateDirectory(Path.GetDirectoryName(Prefab));
                        PrefabUtility.SaveAsPrefabAsset(template, Prefab);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(template); }
                }
                UiStyleRefreshBuilder.RebuildSettingsFromUserSlices();
                Debug.Log("[设置预制体] 使用用户保存的独立切片，不覆盖 Sprite Editor 记录。");
                return;
            }
            if (currentSlices.Length > 0)
                throw new InvalidOperationException("设置图集已有精灵切片，但未找到独立标题 SettingsSheetV2_0 和主框 SettingsSheetV2_1。保留现有切片，请先核对名称；不会自动重新切片。");
            var importer = AssetImporter.GetAtPath(Sheet) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("设置图集缺失", Sheet);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.spritesheet = new[]
            {
                Slice("SettingsPanelFull", 0, 0, 1254, 765),
                Slice("SettingsMonitor", 18, 775, 108, 114),
                Slice("SettingsSound", 132, 775, 108, 114),
                Slice("SettingsGear", 246, 775, 108, 114),
                Slice("SettingsVsyncIcon", 680, 775, 108, 114),
                Slice("SettingsFpsIcon", 794, 775, 108, 114),
                Slice("SettingsBrightnessIcon", 908, 775, 108, 114),
                Slice("SettingsScaleIcon", 1022, 775, 108, 114),
                Slice("SettingsClose", 1135, 775, 104, 114),
                Slice("SettingsTabDark", 38, 915, 560, 112, 24),
                Slice("SettingsTabBright", 611, 915, 498, 112, 24),
                Slice("SettingsSliderEmpty", 78, 1072, 515, 53, 18),
                Slice("SettingsSliderFilled", 78, 1130, 515, 56, 18),
                Slice("SettingsSliderKnob", 610, 1080, 104, 130),
                Slice("SettingsToggleOff", 790, 1082, 202, 108),
                Slice("SettingsToggleOn", 998, 1082, 204, 108),
            };
            importer.SaveAndReimport();
            var required = new[]
            {
                "SettingsPanelFull", "SettingsMonitor", "SettingsSound", "SettingsGear",
                "SettingsClose", "SettingsTabDark", "SettingsTabBright", "SettingsSliderEmpty",
                "SettingsSliderFilled", "SettingsSliderKnob", "SettingsToggleOff", "SettingsToggleOn",
            };
            var sprites = new HashSet<string>(AssetDatabase.LoadAllAssetsAtPath(Sheet).OfType<Sprite>()
                .Select(sprite => sprite.name));
            foreach (var name in required)
                if (!sprites.Contains(name))
                    throw new InvalidOperationException("设置图集缺少切片 " + name);
            var root = SettingsPopupView.CreateTemplate();
            try
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Font);
                if (font == null) throw new FileNotFoundException("中文字体预制体资源缺失", Font);
                foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) label.font = font;
                Directory.CreateDirectory(Path.GetDirectoryName(Prefab));
                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[设置预制体] 已使用新版图集重建 " + Prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static SpriteMetaData Slice(string name, int x, int top, int width, int height, int border = 0)
        {
            return new SpriteMetaData
            {
                name = name,
                rect = new Rect(x, 1254 - top - height, width, height),
                pivot = new Vector2(0.5f, 0.5f),
                alignment = (int)SpriteAlignment.Center,
                border = new Vector4(border, border, border, border)
            };
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

    }
}
