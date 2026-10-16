using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.Runtime
{
    /// <summary>主页和对局共用的设置预制体。层级及位置可直接在 Prefabs/UI 中编辑。</summary>
    public sealed class SettingsPopupView : MonoBehaviour
    {
        const string ResourcePath = "BombTavern/Prefabs/UI/SettingsPopup";
        RectTransform _panel;
        UiModalAnimator _animator;
        Action _close;
        Action _showRules;
        Action _exitMatch;
        VoiceChatManager _voice;
        GameDirector _director;
        bool _open;
        Vector2Int _labelScreen;
        int _page;
        readonly List<Vector2Int> _resolutions = new List<Vector2Int>();
        string _expandedDropdown;
        static readonly Color Gold = new Color(1f, .78f, .36f);
        static readonly Color White = new Color(1f, .91f, .77f);

        public bool IsOpen => _open;

        public static SettingsPopupView Attach(RectTransform parent, Action close, Action showRules,
            GameDirector director = null, Action exitMatch = null)
        {
            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError("[设置] 缺少 SettingsPopup.prefab，请在 Unity 中执行 工具/炸弹酒馆/创建设置预制体。");
                return null;
            }
            var instance = Instantiate(prefab, parent, false);
            instance.name = "SettingsPopup";
            var rect = instance.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            var view = instance.GetComponent<SettingsPopupView>();
            view.Initialize(close, showRules, director, exitMatch);
            return view;
        }

        void Initialize(Action close, Action showRules, GameDirector director, Action exitMatch)
        {
            _close = close; _showRules = showRules; _director = director; _exitMatch = exitMatch;
            _panel = Find("Panel").GetComponent<RectTransform>();
            _animator = _panel.GetComponent<UiModalAnimator>();
            if (_animator == null) _animator = _panel.gameObject.AddComponent<UiModalAnimator>();
            Bind("Shade", Close);
            Bind("Close", Close);
            Bind("TabDisplay", () => Select(0));
            Bind("TabAudio", () => Select(1));
            Bind("TabAssist", () => Select(2));
            Bind("Resolution", () => ShowDropdown("Resolution", ResolutionLabels(), index =>
            {
                RuntimeDisplaySettings.SetResolution(_resolutions[index]);
                Refresh();
            }));
            Bind("WindowMode", () => ShowDropdown("WindowMode",
                new[] { "窗口", "全屏无边框", "独占全屏" }, index =>
                {
                    RuntimeDisplaySettings.SetMode(index == 0 ? FullScreenMode.Windowed :
                        index == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.ExclusiveFullScreen);
                    Refresh();
                }));
            Bind("Quality", () => ShowDropdown("Quality", QualitySettings.names,
                index => { RuntimeDisplaySettings.SetQuality(index); Refresh(); }));
            Bind("DropdownDismiss", CloseDropdowns);
            Bind("Vsync", () => { RuntimeDisplaySettings.SetVsync(QualitySettings.vSyncCount == 0); Refresh(); });
            Bind("Fps60", () => SetFrameRate(60));
            Bind("Fps120", () => SetFrameRate(120));
            Bind("FpsUnlimited", () => SetFrameRate(-1));
            Bind("MicTest", ToggleMicTest);
            Bind("VoiceMode", () => { Voice()?.SetVoiceActivation(!Voice().VoiceActivationEnabled); Refresh(); });
            Bind("Rules", () => _showRules?.Invoke());
            Bind("ExitMatch", () => _exitMatch?.Invoke());
            Bind("ExitGame", () => { PlayerPrefs.Save(); Application.Quit(); });
            Bind("Restore", RestoreDefaults);
            Bind("LeaveSettings", Close);
            Bind("Confirm", () => { PlayerPrefs.Save(); Close(); });
            Slider("Brightness").onValueChanged.AddListener(v => { RuntimeDisplaySettings.SetBrightness(v); RefreshSliderLabels(); });
            Slider("UiScale").onValueChanged.AddListener(v => { PlayerPrefs.SetFloat("BombTavern.UiScale", v); RefreshSliderLabels(); });
            Slider("MasterVolume").onValueChanged.AddListener(v => { AudioListener.volume = v; PlayerPrefs.SetFloat("BombTavern.Volume", v); RefreshSliderLabels(); });
            Slider("MusicVolume").onValueChanged.AddListener(v => { if (AudioDirector.Instance != null) AudioDirector.Instance.musicVolume = v; PlayerPrefs.SetFloat("BombTavern.MusicVolume", v); RefreshSliderLabels(); });
            Slider("SfxVolume").onValueChanged.AddListener(v => { if (AudioDirector.Instance != null) AudioDirector.Instance.sfxVolume = v; PlayerPrefs.SetFloat("BombTavern.SfxVolume", v); RefreshSliderLabels(); });
            Slider("VoiceVolume").onValueChanged.AddListener(v => { if (Voice() != null) Voice().SetOutputVolume(v); else PlayerPrefs.SetFloat("BombTavern.VoiceVolume", v); RefreshSliderLabels(); });
            Find("ExitMatch").SetActive(exitMatch != null);
            Find("MatchHint").SetActive(exitMatch != null);
            if (exitMatch != null)
            {
                var lan = FindObjectOfType<BombTavern.Net.LanNetworkManager>();
                Label("ExitMatch").text = lan != null && lan.IsHost ? "结束房间，返回大厅" : "退出对局，返回大厅";
                Label("MatchHint").text = director != null && director.mode == GameMode.SinglePlayer
                    ? "离开单人对局并返回大厅。"
                    : lan != null && lan.IsHost ? "房主退出会关闭房间。" : "退出后角色由 AI 接管。";
            }
            Select(0);
            gameObject.SetActive(false);
        }

        public void Open()
        {
            if (_open) return;
            _open = true;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _resolutions.Clear(); _resolutions.AddRange(RuntimeDisplaySettings.ResolutionOptions());
            CloseDropdowns();
            FitPanelToScreen();
            Refresh();
            _animator?.Show();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            CloseDropdowns();
            if (_voice != null && _voice.IsMicrophoneTestRunning) _voice.StopMicrophoneTest();
            PlayerPrefs.Save();
            void CompleteClose() { if (_open) return; gameObject.SetActive(false); _close?.Invoke(); }
            if (_animator != null) _animator.Hide(CompleteClose);
            else CompleteClose();
        }

        void Update()
        {
            if (!_open) return;
            if (_labelScreen != new Vector2Int(Screen.width, Screen.height)) Refresh();
            if (_voice != null && _voice.IsMicrophoneTestRunning) RefreshMic();
            FitPanelToScreen();
        }

        void FitPanelToScreen()
        {
            if (_panel == null) return;
            float scale = UiKit.FitScale(_panel.sizeDelta);
            if (_animator != null) _animator.SetTargetScale(scale);
            else _panel.localScale = Vector3.one * scale;
        }

        void Select(int page)
        {
            CloseDropdowns();
            _page = page;
            Find("DisplayPage").SetActive(page == 0);
            Find("AudioPage").SetActive(page == 1);
            Find("AssistPage").SetActive(page == 2);
            string[] tabs = { "TabDisplay", "TabAudio", "TabAssist" };
            for (int i = 0; i < tabs.Length; i++)
            {
                var button = Find(tabs[i]).GetComponent<Button>();
                var image = button.targetGraphic as Image;
                image.sprite = ButtonSprite(i == page);
                image.color = Color.white;
                Label(tabs[i]).color = i == page ? new Color(.16f, .08f, .03f) : White;
            }
            Refresh();
        }

        void Refresh()
        {
            if (!_open) return;
            _labelScreen = new Vector2Int(Screen.width, Screen.height);
            Label("Resolution").text = $"{Screen.width} × {Screen.height}";
            Label("WindowMode").text = RuntimeDisplaySettings.ModeName(Screen.fullScreenMode);
            var quality = QualitySettings.names;
            Label("Quality").text = quality.Length == 0 ? "默认" : quality[Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, quality.Length - 1)];
            Find("Vsync").GetComponent<Image>().sprite = SheetSprite(
                QualitySettings.vSyncCount > 0 ? "SettingsToggleOn" : "SettingsToggleOff");
            int fps = PlayerPrefs.GetInt("BombTavern.FrameRate", 120);
            bool vsync = QualitySettings.vSyncCount > 0;
            foreach (var name in new[] { "Fps60", "Fps120", "FpsUnlimited" })
            {
                bool selected = (name == "Fps60" && fps == 60) ||
                    (name == "Fps120" && fps == 120) || (name == "FpsUnlimited" && fps < 0)
                    ;
                Find(name).GetComponent<Image>().sprite = ButtonSprite(selected);
                Label(name).color = selected ? new Color(.16f, .08f, .03f) : White;
            }
            Label("FpsTitle").text = vsync ? "帧率上限 · 同步优先" : "帧率上限";
            SetSlider("Brightness", PlayerPrefs.GetFloat("BombTavern.Brightness", .5f));
            SetSlider("UiScale", PlayerPrefs.GetFloat("BombTavern.UiScale", 1f));
            SetSlider("MasterVolume", AudioListener.volume);
            SetSlider("MusicVolume", PlayerPrefs.GetFloat("BombTavern.MusicVolume", .65f));
            SetSlider("SfxVolume", PlayerPrefs.GetFloat("BombTavern.SfxVolume", 1f));
            SetSlider("VoiceVolume", PlayerPrefs.GetFloat("BombTavern.VoiceVolume", 1f));
            Label("VoiceMode").text = Voice() != null && Voice().VoiceActivationEnabled ? "发言方式：自动声控" : "发言方式：按住 V 发言";
            RefreshMic(); RefreshSliderLabels();
        }

        void RefreshSliderLabels()
        {
            foreach (var name in new[] { "Brightness", "UiScale", "MasterVolume", "MusicVolume", "SfxVolume", "VoiceVolume" })
                Label(name + "Value").text = Mathf.RoundToInt(Slider(name).value * 100f) + "%";
        }

        void RefreshMic()
        {
            var voice = Voice();
            Label("MicTest").text = voice == null || !voice.IsMicReady ? "麦克风不可用" :
                voice.IsMicrophoneTestRunning ? "停止测试" : "录制 3 秒并回放";
            Find("MicTest").GetComponent<Button>().interactable = voice != null && voice.IsMicReady;
            Label("MicStatus").text = voice == null ? "当前没有语音设备" : $"{voice.MicrophoneName} · {voice.MicrophoneTestStatus}";
        }

        static Sprite ButtonSprite(bool selected) => Resources.Load<Sprite>("BombTavern/UI/SettingsButton" + (selected ? "Gold" : "Dark"));

        static Sprite SheetSprite(string name)
        {
            foreach (var sprite in Resources.LoadAll<Sprite>("BombTavern/UI/SettingsSheetV2"))
                if (sprite.name == name) return sprite;
            return null;
        }

        string[] ResolutionLabels()
        {
            var labels = new string[_resolutions.Count];
            for (int i = 0; i < labels.Length; i++)
                labels[i] = $"{_resolutions[i].x} × {_resolutions[i].y}";
            return labels;
        }

        void ShowDropdown(string name, IList<string> choices, Action<int> select)
        {
            if (_expandedDropdown == name) { CloseDropdowns(); return; }
            CloseDropdowns();
            if (choices == null || choices.Count == 0) return;
            var control = Find(name).GetComponent<RectTransform>();
            var list = control.Find("Options") as RectTransform;
            var template = list.Find("OptionTemplate").GetComponent<Button>();
            for (int i = list.childCount - 1; i >= 0; i--)
            {
                var child = list.GetChild(i);
                if (child == template.transform) continue;
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            float height = 44f;
            list.sizeDelta = new Vector2(0, choices.Count * height + 14f);
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                var option = Instantiate(template, list, false);
                option.name = "Option_" + i;
                var rect = option.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(.5f, 1);
                rect.anchoredPosition = new Vector2(0, -7f - height * i);
                rect.sizeDelta = new Vector2(-12f, height);
                option.GetComponentInChildren<TMP_Text>(true).text = choices[i];
                option.onClick.AddListener(() => { CloseDropdowns(); select(index); });
                option.gameObject.SetActive(true);
            }
            var dismiss = Find("DropdownDismiss");
            dismiss.SetActive(true);
            dismiss.transform.SetAsLastSibling();
            control.SetAsLastSibling();
            list.gameObject.SetActive(true);
            _expandedDropdown = name;
        }

        void CloseDropdowns()
        {
            _expandedDropdown = null;
            foreach (var name in new[] { "Resolution", "WindowMode", "Quality" })
            {
                var list = Find(name).transform.Find("Options");
                if (list != null) list.gameObject.SetActive(false);
            }
            Find("DropdownDismiss").SetActive(false);
        }

        VoiceChatManager Voice() => _voice != null ? _voice : (_voice = FindObjectOfType<VoiceChatManager>());
        void ToggleMicTest() { var voice = Voice(); if (voice == null) return; if (voice.IsMicrophoneTestRunning) voice.StopMicrophoneTest(); else voice.BeginMicrophoneTest(); RefreshMic(); }
        void SetFrameRate(int rate)
        {
            PlayerPrefs.SetInt("BombTavern.FrameRate", rate);
            if (QualitySettings.vSyncCount > 0) RuntimeDisplaySettings.SetVsync(false);
            Application.targetFrameRate = rate;
            Refresh();
        }
        void RestoreDefaults()
        {
            RuntimeDisplaySettings.SetMode(FullScreenMode.FullScreenWindow);
            var native = Screen.currentResolution; RuntimeDisplaySettings.SetResolution(new Vector2Int(native.width, native.height));
            RuntimeDisplaySettings.SetQuality(Mathf.Max(0, QualitySettings.names.Length - 1));
            RuntimeDisplaySettings.SetVsync(true);
            AudioListener.volume = 1f; PlayerPrefs.SetFloat("BombTavern.Volume", 1f);
            if (AudioDirector.Instance != null) { AudioDirector.Instance.musicVolume = .65f; AudioDirector.Instance.sfxVolume = 1f; }
            PlayerPrefs.SetFloat("BombTavern.MusicVolume", .65f); PlayerPrefs.SetFloat("BombTavern.SfxVolume", 1f);
            if (Voice() != null) { Voice().SetOutputVolume(1f); Voice().SetVoiceActivation(false); }
            else PlayerPrefs.SetFloat("BombTavern.VoiceVolume", 1f);
            RuntimeDisplaySettings.SetBrightness(.5f);
            PlayerPrefs.SetFloat("BombTavern.UiScale", 1f);
            PlayerPrefs.SetInt("BombTavern.FrameRate", 120); Application.targetFrameRate = 120;
            RuntimeDisplaySettings.SetVsync(true);
            Refresh(); PlayerPrefs.Save();
        }
        void SetSlider(string name, float value) => Slider(name).SetValueWithoutNotify(value);
        Slider Slider(string name) => Find(name).GetComponent<Slider>();
        TMP_Text Label(string name) => Find(name).GetComponentInChildren<TMP_Text>(true);
        GameObject Find(string name) { foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == name) return t.gameObject; throw new MissingReferenceException("SettingsPopup/" + name); }
        void Bind(string name, Action action) => Find(name).GetComponent<Button>().onClick.AddListener(() => action());

        // 由编辑器菜单调用，只在预制体不存在时生成一次，之后不会覆盖手工排版。
        public static GameObject CreateTemplate()
        {
            var sprites = new Dictionary<string, Sprite>();
            foreach (var sprite in Resources.LoadAll<Sprite>("BombTavern/UI/SettingsSheetV2")) sprites[sprite.name] = sprite;
            Sprite S(string name) => sprites.TryGetValue(name, out var sprite) ? sprite : null;
            var root = NewRect(null, "SettingsPopup", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            root.gameObject.AddComponent<SettingsPopupView>();
            ImageAt(root, "Shade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null, new Color(0, 0, 0, .72f), true);
            var panel = NewRect(root, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1600, 1030));
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = S("SettingsPanelFull");
            panelImage.color = Color.white;
            panelImage.raycastTarget = true;
            panel.gameObject.AddComponent<CanvasGroup>(); panel.gameObject.AddComponent<UiModalAnimator>();
            TextAt(panel, "Subtitle", "调整画面、音频与辅助选项", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -278), new Vector2(680, 38), 27, White);
            ImageAt(panel, "Close", Vector2.one, Vector2.one, new Vector2(-100, -350), new Vector2(68, 68), S("SettingsClose"), Color.white, true);
            ImageAt(panel, "Side", new Vector2(0, 1), new Vector2(0, 1), new Vector2(310, -616), new Vector2(390, 525), null, new Color(.065f, .033f, .018f, .42f));
            string[] tabNames = { "TabDisplay", "TabAudio", "TabAssist" };
            string[] tabLabels = { "显示", "音频", "辅助功能" };
            string[] tabIcons = { "SettingsMonitor", "SettingsSound", "SettingsGear" };
            for (int i = 0; i < 3; i++)
            {
                var tab = ImageAt(panel, tabNames[i], new Vector2(0, 1), new Vector2(0, 1), new Vector2(310, -440 - 115 * i), new Vector2(350, 92), S("SettingsTabBright"), Color.white, true);
                ImageAt(tab, "Icon", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(64, 0), new Vector2(58, 58), S(tabIcons[i]), Color.white);
                TextAt(tab, "Caption", tabLabels[i], new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(34, 0), new Vector2(220, 70), 34, White);
            }
            var display = Page(panel, "DisplayPage");
            Heading(display, "显示设置");
            var dismiss = ImageAt(display, "DropdownDismiss", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null, Color.clear, true);
            dismiss.gameObject.SetActive(false);
            RowDropdown(display, "分辨率", "Resolution", -100, S("SettingsTabDark"));
            RowDropdown(display, "窗口模式", "WindowMode", -168, S("SettingsTabDark"));
            RowDropdown(display, "画面质量", "Quality", -236, S("SettingsTabDark"));
            RowBackground(display, "VsyncRow", -304);
            TextAt(display, "VsyncTitle", "垂直同步", new Vector2(0, 1), new Vector2(0, 1), new Vector2(165, -304), new Vector2(265, 48), 26, White);
            ImageAt(display, "Vsync", new Vector2(0, 1), new Vector2(0, 1), new Vector2(748, -304), new Vector2(145, 58), S("SettingsToggleOn"), Color.white, true);
            RowBackground(display, "FpsRow", -372);
            TextAt(display, "FpsTitle", "帧率上限", new Vector2(0, 1), new Vector2(0, 1), new Vector2(190, -372), new Vector2(340, 54), 25, White);
            string[] fps = { "Fps60", "Fps120", "FpsUnlimited" }; string[] fpsLabels = { "60", "120", "无限" };
            for (int i = 0; i < 3; i++)
            {
                var button = ImageAt(display, fps[i], new Vector2(0, 1), new Vector2(0, 1), new Vector2(545 + i * 132, -372), new Vector2(120, 53), S("SettingsTabDark"), Color.white, true);
                TextAt(button, "Caption", fpsLabels[i], new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(110, 48), 25, White);
            }
            RowSlider(display, "Brightness", "亮度", -440, S("SettingsSliderEmpty"), S("SettingsSliderFilled"), S("SettingsSliderKnob"), 0, 1, .5f);
            RowSlider(display, "UiScale", "UI 缩放", -508, S("SettingsSliderEmpty"), S("SettingsSliderFilled"), S("SettingsSliderKnob"), .75f, 1.25f, 1f);
            var audio = Page(panel, "AudioPage"); Heading(audio, "音频设置");
            RowSlider(audio, "MasterVolume", "主音量", -112, S("SettingsSliderEmpty"), S("SettingsSliderFilled"), S("SettingsSliderKnob"), 0, 1, 1);
            RowSlider(audio, "MusicVolume", "音乐音量", -202, S("SettingsSliderEmpty"), S("SettingsSliderFilled"), S("SettingsSliderKnob"), 0, 1, .65f);
            RowSlider(audio, "SfxVolume", "音效音量", -292, S("SettingsSliderEmpty"), S("SettingsSliderFilled"), S("SettingsSliderKnob"), 0, 1, 1);
            RowSlider(audio, "VoiceVolume", "语音音量", -382, S("SettingsSliderEmpty"), S("SettingsSliderFilled"), S("SettingsSliderKnob"), 0, 1, 1);
            WideButton(audio, "VoiceMode", "按住 V 发言", -462, S("SettingsTabDark"));
            WideButton(audio, "MicTest", "录制 3 秒并回放", -530, S("SettingsTabDark"));
            TextAt(audio, "MicStatus", "", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -577), new Vector2(840, 38), 20, White);
            var assist = Page(panel, "AssistPage"); Heading(assist, "辅助功能");
            WideButton(assist, "Rules", "查看完整规则", -122, S("SettingsTabDark"));
            TextAt(assist, "VoiceHint", "按 V 发言；联机对局中打开设置不会暂停游戏。", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -214), new Vector2(840, 72), 25, White);
            WideButton(assist, "ExitMatch", "退出对局，返回大厅", -340, S("SettingsTabDark"));
            TextAt(assist, "MatchHint", "", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -397), new Vector2(840, 54), 24, White);
            WideButton(assist, "ExitGame", "退出游戏", -485, S("SettingsTabDark"));
            var footer = NewRect(panel, "Footer", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 69), new Vector2(1430, 82));
            string[] footNames = { "Restore", "LeaveSettings", "Confirm" }; string[] footLabels = { "恢复默认", "退出设置", "确定" };
            for (int i = 0; i < 3; i++)
            {
                var button = ImageAt(footer, footNames[i], new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-450 + 450 * i, 0), new Vector2(385, 76), S(i == 2 ? "SettingsTabBright" : "SettingsTabDark"), Color.white, true);
                TextAt(button, "Caption", footLabels[i], new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(340, 65), 30, i == 2 ? new Color(.17f, .08f, .03f) : White);
            }
            display.gameObject.SetActive(true); audio.gameObject.SetActive(false); assist.gameObject.SetActive(false);
            return root.gameObject;
        }

        static RectTransform Page(RectTransform panel, string name) => NewRect(panel, name,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(1025, -647), new Vector2(900, 550));

        static void Heading(RectTransform parent, string title)
        {
            TextAt(parent, "Heading", title, new Vector2(0, 1), new Vector2(0, 1), new Vector2(235, -27), new Vector2(440, 56), 32, Gold);
            ImageAt(parent, "HeadingLine", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -64), new Vector2(855, 2), null, new Color(.75f, .5f, .16f, .7f));
        }

        static void RowBackground(RectTransform parent, string name, float y)
        {
            ImageAt(parent, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(450, y),
                new Vector2(870, 62), null, new Color(.06f, .03f, .012f, .55f));
        }

        static void RowDropdown(RectTransform parent, string title, string name, float y, Sprite sprite)
        {
            RowBackground(parent, name + "Row", y);
            TextAt(parent, name + "Title", title, new Vector2(0, 1), new Vector2(0, 1), new Vector2(175, y), new Vector2(295, 52), 26, White);
            var button = ImageAt(parent, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(680, y), new Vector2(355, 55), sprite, Color.white, true);
            TextAt(button, "Value", "", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-10, 0), new Vector2(285, 50), 24, White);
            var list = ImageAt(button, "Options", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, -8), Vector2.zero, sprite, Color.white);
            list.pivot = new Vector2(.5f, 1);
            var option = ImageAt(list, "OptionTemplate", new Vector2(0, 1), Vector2.one, new Vector2(0, -7), new Vector2(-12, 44), sprite, Color.white, true);
            option.pivot = new Vector2(.5f, 1);
            TextAt(option, "OptionLabel", "选项", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(320, 40), 22, White);
            option.gameObject.SetActive(false);
            list.gameObject.SetActive(false);
        }

        static void RowSlider(RectTransform parent, string name, string label, float y,
            Sprite track, Sprite filled, Sprite knob, float min, float max, float value)
        {
            RowBackground(parent, name + "Row", y);
            TextAt(parent, name + "Title", label, new Vector2(0, 1), new Vector2(0, 1), new Vector2(175, y), new Vector2(295, 52), 26, White);
            var slider = NewRect(parent, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(670, y), new Vector2(345, 45));
            var background = ImageAt(slider, "Background", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(340, 30), track, Color.white);
            background.GetComponent<Image>().raycastTarget = true;
            var fill = NewRect(slider, "Fill Area", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fillImage = ImageAt(fill, "Fill", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -15), filled, Color.white);
            var handle = NewRect(slider, "Handle Slide Area", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var handleImage = ImageAt(handle, "Handle", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(53, 53), knob, Color.white);
            handleImage.GetComponent<Image>().raycastTarget = true;
            var component = slider.gameObject.AddComponent<Slider>(); component.minValue = min; component.maxValue = max; component.value = value;
            component.fillRect = fillImage; component.handleRect = handleImage; component.targetGraphic = handleImage.GetComponent<Image>();
            TextAt(parent, name + "Value", "", new Vector2(0, 1), new Vector2(0, 1), new Vector2(845, y), new Vector2(86, 50), 24, White);
        }
        static void WideButton(RectTransform parent, string name, string text, float y, Sprite sprite)
        {
            var button = ImageAt(parent, name, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, y), new Vector2(780, 62), sprite, Color.white, true);
            TextAt(button, "Caption", text, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(720, 54), 26, White);
        }
        static RectTransform NewRect(Transform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max; rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        static RectTransform ImageAt(Transform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size, Sprite sprite, Color tint, bool button = false)
        {
            var rect = NewRect(parent, name, min, max, position, size); var image = rect.gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = tint;
            if (sprite == null) image.type = Image.Type.Simple; else image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            if (button) { var b = rect.gameObject.AddComponent<Button>(); b.targetGraphic = image; } else image.raycastTarget = false;
            return rect;
        }
        static void TextAt(Transform parent, string name, string value, Vector2 min, Vector2 max, Vector2 position, Vector2 size, float fontSize, Color color)
        {
            var rect = NewRect(parent, name, min, max, position, size); var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = UiKit.Font; label.text = value; label.fontSize = fontSize; label.color = color; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        }
    }
}
