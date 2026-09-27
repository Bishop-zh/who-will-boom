using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.Runtime
{
    // Shared frame for the menu and in-game settings; existing controls keep their callbacks.
    public static class TavernSettingsLayout
    {
        public static void Arrange(RectTransform panel, Action close, bool showMatchActions = false)
        {
            var original = new List<Transform>();
            foreach (Transform child in panel) original.Add(child);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.enabled = false; UnityEngine.Object.Destroy(layout); }
            panel.sizeDelta = new Vector2(1180, 850);
            var titlePlate = UiKit.Panel(panel, "TitlePlate", card: true).rectTransform;
            Place(titlePlate, new Vector2(.5f, 1), new Vector2(0, -8), new Vector2(650, 76));
            UiKit.Label(titlePlate, "游 戏 设 置", 38, UiTheme.AccentHi, TextAnchor.MiddleCenter);
            var x = UiKit.Btn(panel, "×", () => close());
            Place(x.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(-38, -36), new Vector2(52, 52));
            var nav = UiKit.Rect(panel, "Categories", Vector2.zero, Vector2.one, Vector2.one * .5f, Vector2.zero, Vector2.zero);
            nav.anchorMin = new Vector2(0, 0); nav.anchorMax = new Vector2(0, 1);
            nav.offsetMin = new Vector2(30, 118); nav.offsetMax = new Vector2(252, -112);
            UiKit.VBox(nav, 16, new RectOffset());
            string[] names = { "显示", "音频", "对局", "辅助功能" };
            var pages = new RectTransform[4];
            var tabs = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                var page = UiKit.Rect(panel, names[i], Vector2.zero, Vector2.one, Vector2.one * .5f, Vector2.zero, Vector2.zero);
                page.offsetMin = new Vector2(288, 122); page.offsetMax = new Vector2(-38, -116);
                UiKit.VBox(page, 8, new RectOffset(8, 8, 0, 0));
                pages[i] = page;
                int index = i;
                tabs[i] = UiKit.Btn(nav, names[i], () => Select(index));
                UiKit.Size(tabs[i].GetComponent<RectTransform>(), h: 64);
            }
            // 房间规则在创建游戏时设置；对局页仅提供离开当前对局的操作。
            tabs[2].gameObject.SetActive(showMatchActions);
            int section = -1;
            foreach (var child in original)
            {
                string label = child.GetComponentInChildren<TMP_Text>(true)?.text ?? "";
                if (child.name.StartsWith("Section_"))
                    section = label == "显示" ? 0 : label == "音频" ? 1 : 2;
                if (section < 0 || label == "完成" || label == "保存并继续游戏" || label.StartsWith("退出") ||
                    label.StartsWith("鼠标拖拽") || (child.GetComponent<TMP_Text>() != null && string.IsNullOrEmpty(label)))
                { child.gameObject.SetActive(false); continue; }
                int target = section;
                if (label.Contains("规则") || label.Contains("每位玩家") || label.Contains("颜色显示")) target = 3;
                child.SetParent(pages[target], false);
            }
            // Microphone controls belong with audio regardless of the legacy section order.
            foreach (var page in pages)
            {
                var children = new List<Transform>();
                foreach (Transform child in page) children.Add(child);
                foreach (var child in children)
                {
                    var text = child.GetComponentInChildren<TMP_Text>(true);
                    if (text != null && (text.text.Contains("录制") || text.text.Contains("麦克风") || text.text.Contains("发言方式")))
                        child.SetParent(pages[1], false);
                }
            }
            AddAudioSlider(pages[1], "音乐音量", "BombTavern.MusicVolume", .65f, v => { if (AudioDirector.Instance != null) AudioDirector.Instance.musicVolume = v; });
            AddAudioSlider(pages[1], "音效音量", "BombTavern.SfxVolume", 1f, v => { if (AudioDirector.Instance != null) AudioDirector.Instance.sfxVolume = v; });
            var note = UiKit.Paragraph(pages[3], "设置即时生效并自动保存。\n联机对局中打开设置不会暂停游戏。\n按住 V 发言；可在音频设置切换自动声控。", 23, UiTheme.TextMid);
            UiKit.Size(note.rectTransform, h: 140);
            var footer = UiKit.Rect(panel, "Footer", new Vector2(0, 0), new Vector2(1, 0), new Vector2(.5f, 0), new Vector2(0, 30), new Vector2(-60, 60));
            var row = UiKit.HBox(footer, 18, new RectOffset()); row.childForceExpandWidth = true;
            UiKit.Btn(footer, "恢复默认", () =>
            {
                AudioListener.volume = 1;
                PlayerPrefs.SetFloat("BombTavern.Volume", 1);
                foreach (var slider in panel.GetComponentsInChildren<Slider>(true))
                    slider.value = slider.transform.parent.name.Contains("音乐") ? .65f : 1f;
                RuntimeDisplaySettings.SetVsync(true);
                foreach (var toggle in panel.GetComponentsInChildren<Toggle>(true)) toggle.isOn = true;
                RuntimeDisplaySettings.SetQuality(QualitySettings.names.Length - 1);
                RuntimeDisplaySettings.SetMode(FullScreenMode.FullScreenWindow);
                var native = Screen.currentResolution;
                RuntimeDisplaySettings.SetResolution(new Vector2Int(native.width, native.height));
                var voice = UnityEngine.Object.FindObjectOfType<VoiceChatManager>();
                if (voice != null) voice.SetVoiceActivation(false);
                PlayerPrefs.Save();
                close();
            });
            UiKit.Btn(footer, "退出游戏", () => { PlayerPrefs.Save(); Application.Quit(); }, danger: true);
            UiKit.Btn(footer, "应用", () => PlayerPrefs.Save());
            UiKit.Btn(footer, "确定", () => { PlayerPrefs.Save(); close(); }, accent: true);
            foreach (var button in footer.GetComponentsInChildren<Button>())
                UiKit.Size(button.GetComponent<RectTransform>(), w: 240, h: 60);
            StylePrimary(footer.GetChild(footer.childCount - 1).GetComponent<Button>());
            FitToScreen(panel);
            Select(0);
            void Select(int index)
            {
                for (int i = 0; i < pages.Length; i++)
                {
                    pages[i].gameObject.SetActive(i == index);
                    var image = tabs[i].targetGraphic as Image;
                    if (image != null)
                    {
                        image.sprite = i == index
                            ? UiKit.ChamferSprite(UiTheme.AccentHi, UiTheme.TextHi, 9, 2)
                            : UiKit.ChamferSprite(UiTheme.Surface2, UiTheme.Line, 9, 2);
                        image.color = Color.white;
                    }
                    tabs[i].GetComponentInChildren<TMP_Text>(true).color =
                        i == index ? UiTheme.Surface1 : UiTheme.TextHi;
                }
            }
        }

        public static void FitToScreen(RectTransform panel)
        {
            if (panel == null) return;
            float scale = UiKit.FitScale(panel.sizeDelta);
            var animator = panel.GetComponent<UiModalAnimator>();
            if (animator != null) animator.SetTargetScale(scale);
            else panel.localScale = Vector3.one * scale;
        }

        public static void StylePrimary(Button button)
        {
            var image = button.targetGraphic as Image;
            if (image != null) image.sprite = UiKit.ChamferSprite(UiTheme.AccentHi, UiTheme.TextHi, 9, 2);
            button.GetComponentInChildren<TMP_Text>(true).color = UiTheme.Surface1;
        }

        static void AddAudioSlider(Transform parent, string title, string key, float initial, Action<float> apply)
        {
            UiKit.SliderRow(parent, title, PlayerPrefs.GetFloat(key, initial), v => { apply(v); PlayerPrefs.SetFloat(key, v); PlayerPrefs.Save(); }, out _);
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }
    }
}
