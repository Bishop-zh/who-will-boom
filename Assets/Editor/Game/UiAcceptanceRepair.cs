using System.Linq;
using System.IO;
using BombTavern.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.EditorTools
{
    /// <summary>修复后台验收发现的命中、边框与页脚位置问题。</summary>
    public static class UiAcceptanceRepair
    {
        public static void BuildCandidate()
        {
            Repair();
            PlayerBuilder.BuildWindows();
        }

        public static void Repair()
        {
            WriteButton("Dark", new Color(.11f, .065f, .035f), new Color(.85f, .57f, .22f));
            WriteButton("Gold", new Color(1f, .72f, .25f), new Color(1f, .92f, .56f));
            string settings = "Assets/Resources/BombTavern/Prefabs/UI/SettingsPopup.prefab";
            var root = PrefabUtility.LoadPrefabContents(settings);
            try
            {
                var dark = Resources.Load<Sprite>("BombTavern/UI/SettingsButtonDark");
                var gold = Resources.Load<Sprite>("BombTavern/UI/SettingsButtonGold");
                var subtitle = root.transform.Find("Panel/Subtitle").GetComponent<TMP_Text>();
                subtitle.rectTransform.anchoredPosition = new Vector2(0, -298);
                subtitle.rectTransform.sizeDelta = new Vector2(680, 50); subtitle.fontSize = 25;
                foreach (var button in root.GetComponentsInChildren<Button>(true))
                {
                    if (new[] { "Resolution", "WindowMode", "Quality", "Vsync", "Close", "Shade", "DropdownDismiss" }.Contains(button.name)) continue;
                    var image = button.targetGraphic as Image;
                    if (image == null) continue;
                    image.sprite = button.name == "Confirm" || button.name == "TabDisplay" ? gold : dark;
                    image.type = Image.Type.Sliced;
                }
                root.transform.Find("Panel/Footer").GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 116);
                foreach (var pageName in new[] { "DisplayPage", "AudioPage" })
                {
                    var page = root.transform.Find("Panel/" + pageName);
                    foreach (RectTransform child in page)
                    {
                        float originalY = child.anchoredPosition.y;
                        if (pageName == "DisplayPage")
                        {
                            string[] names = { "Resolution", "WindowMode", "Quality", "Vsync", "Fps", "Brightness", "UiScale" };
                            for (int i = 0; i < names.Length; i++) if (child.name.StartsWith(names[i])) originalY = -100 - 68 * i;
                        }
                        else
                        {
                            string[] names = { "MasterVolume", "MusicVolume", "SfxVolume", "VoiceVolume", "VoiceMode", "MicTest", "MicStatus" };
                            float[] positions = { -100, -165, -230, -295, -360, -420, -463 };
                            for (int i = 0; i < names.Length; i++) if (child.name.StartsWith(names[i])) originalY = positions[i];
                        }
                        if (originalY >= -105) continue;
                        var position = child.anchoredPosition;
                        position.y = pageName == "DisplayPage" ? -100 + (originalY + 100) * .87f : originalY;
                        child.anchoredPosition = position;
                    }
                }
                var assist = root.transform.Find("Panel/AssistPage");
                foreach (var pair in new[] { ("ExitMatch", -300f), ("MatchHint", -352f), ("ExitGame", -440f) })
                {
                    var rect = assist.Find(pair.Item1).GetComponent<RectTransform>();
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, pair.Item2);
                }
                // 下拉框与滑条保持原图比例，避免九宫格将发光边框挤成细线。
                foreach (var image in root.GetComponentsInChildren<Image>(true))
                    if (image.sprite != null && (image.sprite.name.StartsWith("SettingsSlider") || new[] { "Resolution", "WindowMode", "Quality" }.Contains(image.name))) image.type = Image.Type.Simple;
                foreach (var slider in root.GetComponentsInChildren<Slider>(true))
                {
                    var rect = slider.GetComponent<RectTransform>();
                    rect.anchoredPosition = new Vector2(630, rect.anchoredPosition.y);
                    rect.sizeDelta = new Vector2(280, 45);
                    var background = slider.transform.Find("Background")?.GetComponent<Image>();
                    if (background != null) { background.raycastTarget = true; background.rectTransform.sizeDelta = new Vector2(280, 30); }
                    if (slider.targetGraphic != null)
                    {
                        slider.targetGraphic.raycastTarget = true;
                        slider.targetGraphic.rectTransform.sizeDelta = new Vector2(43, 43);
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, settings);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            string rules = "Assets/Resources/BombTavern/Prefabs/UI/RulesTutorial.prefab";
            root = PrefabUtility.LoadPrefabContents(rules);
            try
            {
                var note = root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "TipsNote");
                var title = note.GetComponentsInChildren<TMP_Text>(true).First(t => t.text == "小提示");
                var backdrop = note.Find("TipsTitleBackdrop") as RectTransform;
                if (backdrop == null) { backdrop = new GameObject("TipsTitleBackdrop", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>(); backdrop.SetParent(note, false); }
                backdrop.anchorMin = backdrop.anchorMax = backdrop.pivot = new Vector2(0, 1);
                backdrop.anchoredPosition = new Vector2(100, -8); backdrop.sizeDelta = new Vector2(275, 68);
                backdrop.SetAsFirstSibling();
                var image = backdrop.GetComponent<Image>(); image.sprite = Resources.Load<Sprite>("BombTavern/UI/SettingsButtonDark"); image.type = Image.Type.Sliced; image.raycastTarget = false;
                title.color = new Color(1f, .78f, .36f); title.fontSize = 32;
                foreach (var text in note.GetComponentsInChildren<TMP_Text>(true).Where(t => t != title)) { text.color = new Color(.1f, .045f, .005f, 1); text.fontStyle = FontStyles.Bold; }
                PrefabUtility.SaveAsPrefabAsset(root, rules);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            string setup = "Assets/Resources/BombTavern/Prefabs/UI/StartGameSetup.prefab";
            root = PrefabUtility.LoadPrefabContents(setup);
            try
            {
                foreach (var portrait in root.GetComponentsInChildren<RectTransform>(true).Where(t => t.name == "Portrait"))
                    portrait.sizeDelta = new Vector2(200, 160);
                var back = root.transform.Find("BackHome");
                back.GetComponent<Image>().sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/BombTavern/UI/StartGameSetupSheet.png")
                    .OfType<Sprite>().First(s => s.name == "SetupFooter");
                back.GetComponentInChildren<TMP_Text>().text = "‹  返回主页";
                var session = root.transform.Find("SessionPanel");
                var hint = session.Find("NetworkHint").GetComponent<TMP_Text>();
                hint.rectTransform.anchoredPosition = new Vector2(0, -478);
                hint.rectTransform.sizeDelta = new Vector2(450, 60);
                hint.fontSize = 20; hint.enableAutoSizing = true; hint.fontSizeMin = 18; hint.fontSizeMax = 20;
                hint.overflowMode = TextOverflowModes.Ellipsis;
                foreach (var name in new[] { "PortInput", "PortLabel" })
                {
                    var rect = session.Find("PortRow/" + name).GetComponent<RectTransform>();
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                }
                var scan = session.Find("ScanButton").GetComponent<Image>();
                scan.sprite = Resources.Load<Sprite>("BombTavern/UI/SettingsButtonDark"); scan.type = Image.Type.Sliced;
                foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
                {
                    input.textComponent.enableWordWrapping = false;
                    input.textComponent.enableAutoSizing = true;
                    input.textComponent.fontSizeMin = 20; input.textComponent.fontSizeMax = 25;
                }
                foreach (var image in root.GetComponentsInChildren<Image>(true))
                {
                    if (image.name.StartsWith("Mode_")) { image.sprite = Resources.Load<Sprite>("BombTavern/UI/SettingsButtonDark"); image.type = Image.Type.Sliced; }
                    if (image.name == "StartButton") image.type = Image.Type.Simple;
                }
                var label = root.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "StartButtonLabel");
                label.rectTransform.anchoredPosition = new Vector2(75, 0);
                label.rectTransform.sizeDelta = new Vector2(300, 75);
                label.fontSize = 34;
                PrefabUtility.SaveAsPrefabAsset(root, setup);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[UI验收修复] 滑条命中与二级页面控件属性已保存，保留其他排版。");
        }

        static void WriteButton(string name, Color fill, Color border)
        {
            // 原生 UGUI 切角直线边框，避免将下拉箭头烘焙进普通按钮。
            string path = "Assets/Resources/BombTavern/UI/SettingsButton" + name + ".png";
            File.WriteAllBytes(path, UiTheme.ChamferTex(fill, border, 10, 3).EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(14, 14, 14, 14);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}
