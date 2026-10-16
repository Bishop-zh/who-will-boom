using System;
using System.IO;
using System.Linq;
using BombTavern.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.EditorTools
{
    // Explicit migration only: never runs on project open and never re-slices the user's atlas.
    public static class UiStyleRefreshBuilder
    {
        const string Folder = "Assets/Resources/BombTavern/Prefabs/UI/";
        const string Sheet = "Assets/Resources/BombTavern/UI/SettingsSheetV2.png";
        static readonly Color Cream = new Color(1, .91f, .77f);
        static Sprite[] settings;
        static Sprite S(string name) => settings.FirstOrDefault(s => s.name == name)
            ?? throw new InvalidOperationException("用户切片缺失：" + name);
        static RectTransform Node(GameObject root, string name) => root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == name);
        static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = Vector2.one * .5f; rect.anchoredPosition = position; rect.sizeDelta = size; }
        static void ImageStyle(Image image, Sprite sprite, bool sliced = true)
        { image.sprite = sprite; image.color = Color.white; image.type = sliced ? Image.Type.Sliced : Image.Type.Simple; }
        static Sprite Framed(Sprite source, string name, float border)
        {
            // Derive a centered, nine-slice sprite without changing original rect/pivot/name/GUID.
            const string directory = "Assets/Resources/BombTavern/UI/UserSlices";
            Directory.CreateDirectory(directory);
            string path = directory + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;
            var sprite = Sprite.Create(source.texture, source.rect, Vector2.one * .5f,
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, Vector4.one * border);
            sprite.name = name; AssetDatabase.CreateAsset(sprite, path); return sprite;
        }
        static RectTransform Art(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size)
        {
            var child = parent.Find(name) as RectTransform;
            if (child == null) { child = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>(); child.SetParent(parent, false); }
            Place(child, Vector2.one * .5f, position, size);
            var image = child.GetComponent<Image>(); image.sprite = sprite; image.color = Color.white; image.raycastTarget = false;
            image.type = sprite == null || sprite.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
            return child;
        }
        public static void BuildAndPlayer()
        {
            string originalMeta = File.ReadAllText(Sheet + ".meta");
            settings = AssetDatabase.LoadAllAssetsAtPath(Sheet).OfType<Sprite>().ToArray();
            RefreshSettings(); RefreshOtherPages(); AssetDatabase.SaveAssets();
            if (File.ReadAllText(Sheet + ".meta") != originalMeta) throw new Exception("用户图集切片记录不应被改写");
            StatisticsAcceptance.Run(); PlayerBuilder.BuildWindows();
        }
        public static void RebuildSettingsFromUserSlices()
        {
            settings = AssetDatabase.LoadAllAssetsAtPath(Sheet).OfType<Sprite>().ToArray();
            string originalMeta = File.ReadAllText(Sheet + ".meta");
            RefreshSettings(); AssetDatabase.SaveAssets();
            if (File.ReadAllText(Sheet + ".meta") != originalMeta) throw new Exception("用户图集切片记录不应被改写");
        }
        static void RefreshSettings()
        {
            var root = PrefabUtility.LoadPrefabContents(Folder + "SettingsPopup.prefab");
            try
            {
                var panel = Node(root, "Panel"); panel.sizeDelta = new Vector2(1600, 1120);
                var panelImage = panel.GetComponent<Image>(); panelImage.sprite = null; panelImage.color = Color.clear; panelImage.raycastTarget = true;
                var frame = Art(panel, "WindowFrame", Framed(S("SettingsSheetV2_1"), "SettingsWindowFrame", 56), new Vector2(0, -50), new Vector2(1540, 950));
                frame.SetAsFirstSibling();
                var backing = Art(panel, "WindowBacking", null, new Vector2(0, -50), new Vector2(1460, 870));
                backing.GetComponent<Image>().color = new Color(.16f, .085f, .04f, 1); backing.SetAsFirstSibling();
                Art(panel, "TitleArt", S("SettingsSheetV2_0"), new Vector2(0, 450), new Vector2(1100, 217));
                Place(Node(root, "Subtitle"), new Vector2(.5f, 1), new Vector2(0, -235), new Vector2(760, 50));
                Place(Node(root, "Close"), Vector2.one, new Vector2(-110, -210), new Vector2(66, 66));
                Place(Node(root, "Side"), new Vector2(0, 1), new Vector2(270, -610), new Vector2(390, 610));
                string[] tabs = { "TabDisplay", "TabAudio", "TabAssist" };
                for (int i = 0; i < tabs.Length; i++) Place(Node(root, tabs[i]), new Vector2(0, 1), new Vector2(270, -340 - i * 145), new Vector2(350, 110));
                foreach (string name in new[] { "DisplayPage", "AudioPage", "AssistPage" })
                    Place(Node(root, name), new Vector2(0, 1), new Vector2(1035, -575), new Vector2(900, 650));
                Place(Node(root, "Footer"), new Vector2(.5f, 0), new Vector2(0, 100), new Vector2(1430, 82));
                foreach (var button in root.GetComponentsInChildren<Button>(true))
                {
                    if (new[] { "Shade", "DropdownDismiss", "Close", "Vsync", "Resolution", "WindowMode", "Quality" }.Contains(button.name)) continue;
                    var image = button.targetGraphic as Image;
                    if (image != null) ImageStyle(image, Resources.Load<Sprite>("BombTavern/UI/SettingsButton" + (button.name == "Confirm" || button.name == "TabDisplay" ? "Gold" : "Dark")));
                }
                foreach (var dropdown in new[] { "Resolution", "WindowMode", "Quality" })
                {
                    var control = Node(root, dropdown); ImageStyle(control.GetComponent<Image>(), S("SettingsTabDark"));
                    control.GetComponent<Image>().pixelsPerUnitMultiplier = 2;
                    var value = control.Find("Value").GetComponent<TMP_Text>(); value.rectTransform.anchoredPosition = new Vector2(-28, 0); value.rectTransform.sizeDelta = new Vector2(265, 50);
                    var options = control.Find("Options").GetComponent<Image>(); ImageStyle(options, Resources.Load<Sprite>("BombTavern/UI/SettingsButtonDark")); options.raycastTarget = true;
                    ImageStyle(control.Find("Options/OptionTemplate").GetComponent<Image>(), Resources.Load<Sprite>("BombTavern/UI/SettingsButtonDark"));
                }
                foreach (var slider in root.GetComponentsInChildren<Slider>(true))
                {
                    ImageStyle(slider.transform.Find("Background").GetComponent<Image>(), S("SettingsSliderEmpty"));
                    ImageStyle(slider.fillRect.GetComponent<Image>(), S("SettingsSliderFilled"));
                    ImageStyle(slider.handleRect.GetComponent<Image>(), S("SettingsSliderKnob"), false);
                    // Slider drives handle Y anchors to 0..1, so height is parent height plus sizeDelta.
                    slider.handleRect.sizeDelta = new Vector2(52, 1);
                }
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "SettingsPopup.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static void RefreshOtherPages()
        {
            var stats = Resources.LoadAll<Sprite>("BombTavern/UI/StatisticsSheet");
            var frame = stats.First(s => s.name == "StatsPanel");
            var dark = Resources.Load<Sprite>("BombTavern/UI/SettingsButtonDark");
            var gold = Resources.Load<Sprite>("BombTavern/UI/SettingsButtonGold");
            foreach (string name in new[] { "GameSetupRules", "GameOver", "LobbyPanel" })
            {
                string path = Folder + name + ".prefab"; var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    string bodyName = name == "GameSetupRules" ? "GameSetupPanel" : name == "GameOver" ? "ResultCard" : "LobbyPanel";
                    var body = Node(root, bodyName); ImageStyle(body.GetComponent<Image>(), StatisticsPrefabBuilder.CardSprite());
                    var backing = Art(body, "WindowBacking", null, Vector2.zero, Vector2.zero);
                    backing.anchorMin = Vector2.zero; backing.anchorMax = Vector2.one;
                    backing.offsetMin = Vector2.zero; backing.offsetMax = Vector2.zero;
                    ImageStyle(backing.GetComponent<Image>(), frame);
                    if (backing.GetComponent<LayoutElement>() == null) backing.gameObject.AddComponent<LayoutElement>();
                    backing.GetComponent<LayoutElement>().ignoreLayout = true; backing.SetAsFirstSibling();
                    if (name == "LobbyPanel" && body.GetComponent<VerticalLayoutGroup>() != null)
                        body.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(40, 40, 24, 24);
                    if (name == "GameOver" && body.GetComponent<VerticalLayoutGroup>() != null)
                        body.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(60, 60, 44, 80);
                    foreach (var button in root.GetComponentsInChildren<Button>(true))
                    {
                        if (button.name.Contains("Blocker")) continue;
                        var image = button.targetGraphic as Image; if (image == null) continue;
                        bool primary = button.name.Contains("确认并开始") || button.name.Contains("再来一局") || button.name.Contains("开始游戏");
                        ImageStyle(image, primary ? gold : dark);
                        foreach (var label in button.GetComponentsInChildren<TMP_Text>(true)) label.color = primary ? new Color(.16f, .08f, .03f) : Cream;
                    }
                    foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
                        if (input.GetComponent<Image>() != null) ImageStyle(input.GetComponent<Image>(), dark);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
    }
}
