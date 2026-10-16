using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BombTavern.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.EditorTools
{
    /// <summary>用二级界面的独立图集创建可在 Prefab Mode 编辑的 UGUI 页面。</summary>
    public static class StartGameSetupPrefabBuilder
    {
        const string Sheet = "Assets/Resources/BombTavern/UI/StartGameSetupSheet.png";
        const string Prefab = "Assets/Resources/BombTavern/Prefabs/UI/StartGameSetup.prefab";
        const string Font = "Assets/Resources/BombTavern/UI/Fonts/NotoSansSC-Dynamic.asset";
        const string PortraitDir = "Assets/Resources/BombTavern/UI/Portraits";
        static Dictionary<string, Sprite> sprites;
        static TMP_FontAsset font;
        static readonly Color Cream = new Color(1f, .9f, .7f);
        static readonly Color Gold = new Color(1f, .72f, .24f);

        [MenuItem("工具/炸弹酒馆/13. 创建开始游戏二级界面预制体", false, 23)]
        public static void CreateMissing()
        {
            if (File.Exists(Prefab)) { Debug.Log("[开局界面] 预制体已存在，保留手工修改。"); return; }
            Build();
        }

        public static void Build()
        {
            var importer = AssetImporter.GetAtPath(Sheet) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("开局界面图集缺失", Sheet);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.spritesheet = new[]
            {
                Slice("SetupLeftPanel", 25, 35, 715, 630, 35),
                Slice("SetupRightPanel", 740, 25, 500, 770, 35),
                Slice("SetupBack", 27, 706, 149, 105, 22),
                Slice("SetupBombIcon", 180, 706, 91, 105),
                Slice("SetupEyeIcon", 268, 706, 98, 105),
                Slice("SetupNoteIcon", 365, 706, 80, 105),
                Slice("SetupDiceIcon", 442, 706, 100, 105),
                Slice("SetupWarningIcon", 542, 706, 96, 105),
                Slice("SetupNextIcon", 635, 706, 92, 105),
                Slice("SetupStart", 25, 830, 715, 190, 30),
                Slice("SetupCardNormal", 758, 835, 238, 266, 24),
                Slice("SetupCardSelected", 995, 830, 249, 270, 24),
                Slice("SetupFooter", 34, 1110, 1190, 107, 22),
            };
            importer.SaveAndReimport();
            sprites = AssetDatabase.LoadAllAssetsAtPath(Sheet).OfType<Sprite>()
                .ToDictionary(s => s.name, s => s);
            foreach (var key in new[] { "SetupLeftPanel", "SetupRightPanel", "SetupBack", "SetupStart", "SetupCardNormal", "SetupCardSelected", "SetupFooter" })
                if (!sprites.ContainsKey(key)) throw new InvalidOperationException("开局图集缺少切片：" + key);
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Font);
            if (font == null) throw new FileNotFoundException("缺少中文字体", Font);

            var root = Rect(null, "StartGameSetup", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            try
            {
                BuildBack(root);
                BuildRoster(root);
                BuildSession(root);
                BuildFooter(root);
                Directory.CreateDirectory(Path.GetDirectoryName(Prefab));
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, Prefab);
                AssetDatabase.SaveAssets();
                Debug.Log("[开局界面] 已创建 " + Prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
        }

        [MenuItem("工具/炸弹酒馆/14. 渲染角色卡立绘", false, 24)]
        public static void GeneratePortraits()
        {
            Directory.CreateDirectory(PortraitDir);
            foreach (string id in new[] { "black_sphere", "red_bundle", "brown_cracker", "gray_shell" })
                RenderPortrait(id);
            AssetDatabase.Refresh();
            foreach (string id in new[] { "black_sphere", "red_bundle", "brown_cracker", "gray_shell" })
            {
                var importer = AssetImporter.GetAtPath(PortraitDir + "/" + id + ".png") as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            Debug.Log("[开局界面] 四位角色卡立绘已渲染。");
        }

        static void RenderPortrait(string id)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/BombTavern/Prefabs/Bomb_" + id + ".prefab");
            if (source == null) throw new FileNotFoundException("角色预制体缺失：" + id);
            var character = UnityEngine.Object.Instantiate(source);
            var cameraGo = new GameObject("PortraitCamera");
            var lightGo = new GameObject("PortraitLight");
            var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            var oldTarget = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                character.transform.position = Vector3.zero;
                foreach (var child in character.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                var renderers = character.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("角色没有 Renderer：" + id);
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                float size = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z, .3f);
                var cam = cameraGo.AddComponent<Camera>();
                cam.cullingMask = 1 << 31;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.orthographic = true;
                cam.orthographicSize = size * 1.28f;
                cam.nearClipPlane = .01f;
                cam.farClipPlane = size * 20f;
                cam.targetTexture = target;
                cam.transform.position = bounds.center + new Vector3(size * .55f, size * .15f, size * 3.5f);
                cam.transform.LookAt(bounds.center);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 2.2f;
                light.color = new Color(1f, .88f, .68f);
                light.cullingMask = 1 << 31;
                lightGo.transform.rotation = Quaternion.LookRotation(bounds.center - cam.transform.position);
                cam.Render();
                RenderTexture.active = target;
                texture = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                texture.Apply();
                File.WriteAllBytes(PortraitDir + "/" + id + ".png", texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = oldTarget;
                UnityEngine.Object.DestroyImmediate(character);
                UnityEngine.Object.DestroyImmediate(cameraGo);
                UnityEngine.Object.DestroyImmediate(lightGo);
                UnityEngine.Object.DestroyImmediate(target);
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        static SpriteMetaData Slice(string name, int x, int top, int w, int h, int border = 0)
        {
            return new SpriteMetaData
            {
                name = name,
                rect = new Rect(x, 1254 - top - h, w, h),
                pivot = new Vector2(.5f, .5f), alignment = (int)SpriteAlignment.Center,
                border = new Vector4(border, border, border, border)
            };
        }

        static void BuildBack(Transform root)
        {
            var button = Image(root, "BackHome", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(169, -67), new Vector2(255, 73), "SetupFooter", true);
            TextCentered(button, "BackHomeLabel", "‹  返回主页", Vector2.zero, new Vector2(222, 58), 27, Cream);
        }

        static void BuildRoster(Transform root)
        {
            var panel = Image(root, "RosterPanel", new Vector2(0, .5f), new Vector2(0, .5f),
                new Vector2(319, 0), new Vector2(590, 760), "SetupLeftPanel");
            Text(panel, "SectionOne", "●  01  选择身份 ━━━━━━", new Vector2(0, -66), new Vector2(480, 48), 26, Gold);
            Text(panel, "RosterHeading", "今晚由谁上桌？", new Vector2(0, -132), new Vector2(475, 63), 39, Cream);
            Text(panel, "RosterNote", "角色只改变外观，其余座位会由玩家或 AI 补齐。",
                new Vector2(0, -192), new Vector2(485, 42), 21, new Color(.87f, .76f, .62f));
            string[] ids = { "black_sphere", "red_bundle", "brown_cracker", "gray_shell" };
            string[] names = { "黑色球形炸弹", "红色成捆炸弹", "棕色无踢脚", "灰色炮弹" };
            Color[] colors = { new Color(.13f, .16f, .23f), new Color(.77f, .13f, .13f),
                new Color(.54f, .3f, .18f), new Color(.48f, .49f, .55f) };
            for (int i = 0; i < 4; i++)
            {
                int col = i % 2, row = i / 2;
                var card = Image(panel, "Skin_" + ids[i], new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(162 + col * 264, -350 - row * 218), new Vector2(244, 208),
                    "SetupCardNormal", true);
                // 图集中没有角色立绘。卡片中心使用项目内的角色造型预览，便于后续直接替换。
                Image(card, "ColorChip", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(-76, 4), new Vector2(32, 32), null).GetComponent<Image>().color = colors[i];
                var portrait = Image(card, "Portrait", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(14, 22), new Vector2(200, 160), "SetupBombIcon");
                var portraitAsset = AssetDatabase.LoadAssetAtPath<Sprite>(PortraitDir + "/" + ids[i] + ".png");
                var portraitImage = portrait.GetComponent<Image>();
                if (portraitAsset != null) { portraitImage.sprite = portraitAsset; portraitImage.color = Color.white; portraitImage.preserveAspect = true; }
                else portraitImage.color = colors[i] * 1.6f;
                Text(card, "SkinName", names[i], new Vector2(0, -177), new Vector2(218, 38), 22, Cream);
            }
        }

        static void BuildSession(Transform root)
        {
            var panel = Image(root, "SessionPanel", new Vector2(1, .5f), new Vector2(1, .5f),
                new Vector2(-313, 0), new Vector2(570, 780), "SetupRightPanel");
            Text(panel, "SectionTwo", "●  02  准备入局 ━━━━━━", new Vector2(0, -69), new Vector2(468, 47), 25, Gold);
            Text(panel, "SessionHeading", "建立你的牌桌席位", new Vector2(0, -141), new Vector2(470, 64), 38, Cream);
            Text(panel, "NameLabel", "玩家昵称", new Vector2(-151, -220), new Vector2(170, 40), 25, Cream);
            Input(panel, "PlayerNameInput", "红雷", "输入昵称", new Vector2(0, -272), new Vector2(440, 55), 16);
            Text(panel, "ModeLabel", "游戏方式", new Vector2(-151, -352), new Vector2(170, 40), 25, Cream);
            string[] modes = { "单人", "开房", "加入" };
            for (int i = 0; i < 3; i++)
            {
                var mode = Image(panel, "Mode_" + i, new Vector2(.5f, 1), new Vector2(.5f, 1),
                    new Vector2(-148 + 148 * i, -415), new Vector2(128, 58), "SetupCardNormal", true);
                TextCentered(mode, "ModeLabel_" + i, modes[i], Vector2.zero, new Vector2(112, 48), 26, Cream);
            }
            Text(panel, "NetworkHint", "与三名 AI 对局，立即进入。", new Vector2(0, -485),
                new Vector2(450, 53), 22, new Color(.87f, .76f, .62f));
            Text(panel, "AddressLabel", "主机地址", new Vector2(-148, -529), new Vector2(175, 37), 22, Cream);
            Input(panel, "AddressInput", "", "例如 192.168.1.10", new Vector2(0, -570), new Vector2(440, 49), 64);
            var port = Rect(panel, "PortRow", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -620), new Vector2(440, 45));
            Text(port, "PortLabel", "端口", new Vector2(-161, 0), new Vector2(100, 42), 21, Cream);
            Input(port, "PortInput", "7788", "7788", new Vector2(-55, 0), new Vector2(170, 43), 5);
            var scan = Image(panel, "ScanButton", new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(132, -620), new Vector2(170, 43), "SetupCardNormal", true);
            TextCentered(scan, "ScanLabel", "扫描房间", Vector2.zero, new Vector2(155, 38), 18, Cream);
            var start = Image(panel, "StartButton", new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, 76), new Vector2(478, 100), "SetupStart", true);
            TextCentered(start, "StartButtonLabel", "开 始 游 戏", new Vector2(75, 0), new Vector2(300, 75),
                34, new Color(.25f, .11f, .02f));
        }

        static void BuildFooter(Transform root)
        {
            var footer = Image(root, "CoreLoop", new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, 60), new Vector2(1240, 100), "SetupFooter");
            string[] labels = { "观察概率", "判断信息是否过时", "翻格或使用道具", "承担风险", "进入下一回合" };
            string[] icons = { "SetupEyeIcon", "SetupNoteIcon", "SetupDiceIcon", "SetupWarningIcon", "SetupNextIcon" };
            for (int i = 0; i < labels.Length; i++)
            {
                float x = -492 + i * 247;
                Image(footer, "FlowIcon_" + i, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(x - 75, 0), new Vector2(53, 53), icons[i]);
                TextCentered(footer, "FlowText_" + i, labels[i], new Vector2(x + 28, 0), new Vector2(180, 60), 21, Cream);
                if (i < 4) TextCentered(footer, "FlowArrow_" + i, "→", new Vector2(x + 132, 0), new Vector2(50, 48), 28, Gold);
            }
        }

        static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false); rt.anchorMin = min; rt.anchorMax = max;
            rt.pivot = new Vector2(.5f, .5f); rt.anchoredPosition = position; rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Image(Transform parent, string name, Vector2 min, Vector2 max,
            Vector2 position, Vector2 size, string sprite, bool button = false)
        {
            var rt = Rect(parent, name, min, max, position, size);
            var image = rt.gameObject.AddComponent<Image>();
            if (sprite != null) image.sprite = sprites[sprite];
            image.color = Color.white;
            image.type = image.sprite != null && image.sprite.border != Vector4.zero
                ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            image.raycastTarget = button;
            if (button) { var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = image; }
            return rt;
        }

        static RectTransform Text(Transform parent, string name, string value, Vector2 position,
            Vector2 size, int fontSize, Color color)
        {
            var rt = Rect(parent, name, new Vector2(.5f, 1), new Vector2(.5f, 1), position, size);
            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = value; label.fontSize = fontSize; label.color = color;
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            return rt;
        }

        static RectTransform TextCentered(Transform parent, string name, string value, Vector2 position,
            Vector2 size, int fontSize, Color color)
        {
            var rt = Text(parent, name, value, position, size, fontSize, color);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            return rt;
        }

        static void Input(Transform parent, string name, string value, string placeholder,
            Vector2 position, Vector2 size, int limit)
        {
            var rt = Rect(parent, name, new Vector2(.5f, 1), new Vector2(.5f, 1), position, size);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(.075f, .042f, .022f, .94f);
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.58f, .34f, .1f, 1f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            var field = rt.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = bg; field.characterLimit = limit;
            field.lineType = TMP_InputField.LineType.SingleLine;
            var text = Rect(rt, "Text", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-22, -8));
            var textComponent = text.gameObject.AddComponent<TextMeshProUGUI>();
            textComponent.font = font; textComponent.fontSize = 25; textComponent.color = Cream;
            textComponent.alignment = TextAlignmentOptions.Left; textComponent.raycastTarget = false;
            var hint = Rect(rt, "Placeholder", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-22, -8));
            var hintComponent = hint.gameObject.AddComponent<TextMeshProUGUI>();
            hintComponent.font = font; hintComponent.fontSize = 22; hintComponent.color = new Color(.55f, .47f, .38f);
            hintComponent.text = placeholder; hintComponent.alignment = TextAlignmentOptions.Left;
            hintComponent.raycastTarget = false;
            field.textComponent = textComponent; field.placeholder = hintComponent;
            field.text = value;
        }
    }
}
