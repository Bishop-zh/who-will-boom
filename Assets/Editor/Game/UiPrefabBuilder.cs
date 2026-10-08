using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BombTavern.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace BombTavern.EditorTools
{
    /// <summary>
    /// 把已有 UGUI 页面只生成一次为可编辑预制体。已有文件不会被覆盖，
    /// 后续排版直接在 Prefabs/UI 中修改，运行时会加载同一个预制体。
    /// </summary>
    public static class UiPrefabBuilder
    {
        const string PrefabDir = "Assets/Resources/BombTavern/Prefabs/UI";
        const string HomeSheet = "Assets/Resources/BombTavern/UI/HomeMenuSheet.png";
        const string RulesSheet = "Assets/Resources/BombTavern/UI/RulesTutorialSheet.png";
        const string FontPath = "Assets/Resources/BombTavern/UI/Fonts/NotoSansSC-Dynamic.asset";
        const string BadgePath = "Assets/Resources/BombTavern/UI/NumberBadge.png";

        struct ImageReference
        {
            public Image image;
            public string sheet;
            public string sprite;
        }

        [MenuItem("工具/炸弹酒馆/9. 创建主页与游戏说明预制体", false, 19)]
        public static void CreateMissing()
        {
            Directory.CreateDirectory(PrefabDir);
            string homePath = PrefabDir + "/HomeMenu.prefab";
            string rulesPath = PrefabDir + "/RulesTutorial.prefab";
            if (File.Exists(homePath) && File.Exists(rulesPath))
            {
                Debug.Log("[UI 预制体] 已存在，不覆盖手工排版。");
                return;
            }

            GameObject home = null;
            GameObject rules = null;
            try
            {
                // 先用现有布局生成层级，再把临时 Sprite/字体换成可序列化资产。
                if (!File.Exists(homePath)) home = BuildHomeHierarchy();
                if (!File.Exists(rulesPath)) rules = BuildRulesHierarchy();
                var references = CaptureImages(home, rules);

                ConfigureSprites(HomeSheet, HomeDefinitions());
                ConfigureSprites(RulesSheet, RulesDefinitions());
                var persistentFont = EnsureFont();
                var badge = EnsureBadge(rules);
                RemapImages(references, badge);
                ApplyFont(home, persistentFont);
                ApplyFont(rules, persistentFont);

                if (home != null)
                {
                    PrefabUtility.SaveAsPrefabAsset(home, homePath);
                    Debug.Log("[UI 预制体] 已创建 " + homePath);
                }
                if (rules != null)
                {
                    PrefabUtility.SaveAsPrefabAsset(rules, rulesPath);
                    Debug.Log("[UI 预制体] 已创建 " + rulesPath);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                if (home != null) UnityEngine.Object.DestroyImmediate(home);
                if (rules != null) UnityEngine.Object.DestroyImmediate(rules);
            }
        }

        static GameObject BuildHomeHierarchy()
        {
            var container = new GameObject("HomeAuthoring", typeof(RectTransform));
            var component = container.AddComponent<PreGameMenuUgui>();
            typeof(PreGameMenuUgui).GetField("_safe", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(component, container.GetComponent<RectTransform>());
            var method = typeof(PreGameMenuUgui).GetMethod("BuildHomeFallback",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException("BuildHomeFallback");
            method.Invoke(component, null);
            var home = container.transform.Find("Home");
            if (home == null) throw new InvalidOperationException("主页布局生成失败");
            home.SetParent(null, false);
            home.name = "HomeMenu";
            UnityEngine.Object.DestroyImmediate(container);
            return home.gameObject;
        }

        static GameObject BuildRulesHierarchy()
        {
            var root = new GameObject("RulesTutorial", typeof(RectTransform));
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            var component = root.AddComponent<GameRulesTutorial>();
            var method = typeof(GameRulesTutorial).GetMethod("ConfigureFallback",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException("ConfigureFallback");
            method.Invoke(component, new object[] { rt });
            UnityEngine.Object.DestroyImmediate(component);
            var blocker = root.transform.Find("RulesTutorialBlocker");
            var panel = root.transform.Find("RulesTutorialPanel");
            if (blocker == null || panel == null) throw new InvalidOperationException("游戏说明布局生成失败");
            blocker.gameObject.SetActive(true);
            panel.gameObject.SetActive(true);
            panel.localScale = Vector3.one;
            panel.GetComponent<CanvasGroup>().alpha = 1f;
            return root;
        }

        static Dictionary<string, SpriteMetaData[]> HomeDefinitions()
        {
            return new Dictionary<string, SpriteMetaData[]>
            {
                [HomeSheet] = new[]
                {
                    Slice("HomeLogo", 60, 24, 1140, 592),
                    Slice("HomeButtonDark", 18, 635, 590, 165),
                    Slice("HomeButtonGold", 625, 635, 610, 165),
                    Slice("HomeIconStart", 50, 825, 230, 190),
                    Slice("HomeIconRules", 300, 825, 210, 190),
                    Slice("HomeIconSettings", 535, 825, 200, 190),
                    Slice("HomeIconStats", 775, 825, 200, 190),
                    Slice("HomeIconQuit", 995, 825, 230, 190),
                }
            };
        }

        static Dictionary<string, SpriteMetaData[]> RulesDefinitions()
        {
            return new Dictionary<string, SpriteMetaData[]>
            {
                [RulesSheet] = new[]
                {
                    Slice("RulesFrame", 18, 363, 1217, 435, 65),
                    Slice("RulesLogo", 18, 2, 555, 290),
                    Slice("RulesClose", 1116, 243, 120, 109),
                    Slice("RulesCard", 18, 805, 1218, 151, 38),
                    Slice("RulesStep", 594, 244, 244, 107, 20),
                    Slice("RulesTarget", 595, 90, 133, 150),
                    Slice("RulesGear", 735, 90, 133, 150),
                    Slice("RulesClipboard", 870, 89, 112, 151),
                    Slice("RulesTrophy", 985, 90, 126, 150),
                    Slice("RulesBulb", 1110, 88, 140, 153),
                    Slice("RulesNote", 265, 962, 720, 286),
                }
            };
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

        static List<ImageReference> CaptureImages(params GameObject[] roots)
        {
            var result = new List<ImageReference>();
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var image in root.GetComponentsInChildren<Image>(true))
                {
                    var sprite = image.sprite;
                    if (sprite == null || sprite.texture == null) continue;
                    string sheet = sprite.texture.name;
                    if (sheet != "HomeMenuSheet" && sheet != "RulesTutorialSheet") continue;
                    var defs = sheet == "HomeMenuSheet" ? HomeDefinitions()[HomeSheet] : RulesDefinitions()[RulesSheet];
                    var match = defs.FirstOrDefault(d => SameRect(d.rect, sprite.rect));
                    if (string.IsNullOrEmpty(match.name))
                        throw new InvalidOperationException("找不到图集切片：" + image.name);
                    result.Add(new ImageReference { image = image, sheet = sheet, sprite = match.name });
                }
            }
            return result;
        }

        static bool SameRect(Rect a, Rect b)
            => Mathf.Abs(a.x - b.x) < .5f && Mathf.Abs(a.y - b.y) < .5f &&
               Mathf.Abs(a.width - b.width) < .5f && Mathf.Abs(a.height - b.height) < .5f;

        static void ConfigureSprites(string path, Dictionary<string, SpriteMetaData[]> definitions)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("找不到图集：" + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.spritesheet = definitions[path];
            importer.SaveAndReimport();
        }

        static void RemapImages(List<ImageReference> images, Sprite badge)
        {
            var home = AssetDatabase.LoadAllAssetsAtPath(HomeSheet).OfType<Sprite>()
                .ToDictionary(s => s.name, s => s);
            var rules = AssetDatabase.LoadAllAssetsAtPath(RulesSheet).OfType<Sprite>()
                .ToDictionary(s => s.name, s => s);
            foreach (var entry in images)
            {
                var sprites = entry.sheet == "HomeMenuSheet" ? home : rules;
                if (!sprites.TryGetValue(entry.sprite, out var sprite))
                    throw new InvalidOperationException("缺少导入的 Sprite：" + entry.sprite);
                entry.image.sprite = sprite;
            }
            var view = images.FirstOrDefault(e => e.sheet == "RulesTutorialSheet").image;
            var root = view != null ? view.transform.root : null;
            if (root == null) return;
            foreach (var image in root.GetComponentsInChildren<Image>(true))
                if (image.name == "Number" && image.sprite != null) image.sprite = badge;
        }

        static TMP_FontAsset EnsureFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<Font>(
                "Assets/Resources/BombTavern/UI/Fonts/NotoSansSC-Regular.otf");
            if (source == null) throw new InvalidOperationException("找不到 Noto Sans SC 字体");
            var asset = TMP_FontAsset.CreateFontAsset(source, 64, 8,
                GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            asset.name = "NotoSansSC-Dynamic";
            asset.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(asset, FontPath);
            if (asset.material != null && !AssetDatabase.Contains(asset.material))
            {
                asset.material.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }
            foreach (var atlas in asset.atlasTextures)
            {
                if (atlas == null || AssetDatabase.Contains(atlas)) continue;
                atlas.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(atlas, asset);
            }
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static Sprite EnsureBadge(GameObject rules)
        {
            if (!File.Exists(BadgePath))
            {
                var image = rules?.GetComponentsInChildren<Image>(true)
                    .FirstOrDefault(i => i.name == "Number" && i.sprite != null);
                if (image == null) throw new InvalidOperationException("找不到流程编号底图");
                File.WriteAllBytes(BadgePath, image.sprite.texture.EncodeToPNG());
                AssetDatabase.ImportAsset(BadgePath, ImportAssetOptions.ForceUpdate);
                var importer = AssetImporter.GetAtPath(BadgePath) as TextureImporter;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            var badge = AssetDatabase.LoadAssetAtPath<Sprite>(BadgePath);
            if (badge == null) throw new InvalidOperationException("无法导入流程编号底图");
            return badge;
        }

        static void ApplyFont(GameObject root, TMP_FontAsset font)
        {
            if (root == null) return;
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) label.font = font;
        }
    }
}
