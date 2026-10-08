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
        int _page;
        readonly List<Vector2Int> _resolutions = new List<Vector2Int>();
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
            Bind("Resolution", CycleResolution);
            Bind("WindowMode", CycleMode);
            Bind("Quality", CycleQuality);
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
            Slider("Brightness").onValueChanged.AddListener(v => { SetBrightness(v); RefreshSliderLabels(); });
            Slider("UiScale").onValueChanged.AddListener(v => { PlayerPrefs.SetFloat("BombTavern.UiScale", v); RefreshSliderLabels(); });
            Slider("MasterVolume").onValueChanged.AddListener(v => { AudioListener.volume = v; PlayerPrefs.SetFloat("BombTavern.Volume", v); RefreshSliderLabels(); });
            Slider("MusicVolume").onValueChanged.AddListener(v => { if (AudioDirector.Instance != null) AudioDirector.Instance.musicVolume = v; PlayerPrefs.SetFloat("BombTavern.MusicVolume", v); RefreshSliderLabels(); });
            Slider("SfxVolume").onValueChanged.AddListener(v => { if (AudioDirector.Instance != null) AudioDirector.Instance.sfxVolume = v; PlayerPrefs.SetFloat("BombTavern.SfxVolume", v); RefreshSliderLabels(); });
            Slider("VoiceVolume").onValueChanged.AddListener(v => { if (Voice() != null) Voice().outputVolume = v; PlayerPrefs.SetFloat("BombTavern.VoiceVolume", v); RefreshSliderLabels(); });
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
            TavernSettingsLayout.FitToScreen(_panel);
            Refresh();
            _animator?.Show();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (_voice != null && _voice.IsMicrophoneTestRunning) _voice.StopMicrophoneTest();
            PlayerPrefs.Save();
            _animator?.Hide();
            gameObject.SetActive(false);
            _close?.Invoke();
        }

        void Update()
        {
            if (!_open) return;
            if (_voice != null && _voice.IsMicrophoneTestRunning) RefreshMic();
            if (_panel != null) TavernSettingsLayout.FitToScreen(_panel);
        }

        void Select(int page)
        {
            _page = page;
            Find("DisplayPage").SetActive(page == 0);
            Find("AudioPage").SetActive(page == 1);
            Find("AssistPage").SetActive(page == 2);
            string[] tabs = { "TabDisplay", "TabAudio", "TabAssist" };
            for (int i = 0; i < tabs.Length; i++)
            {
                var button = Find(tabs[i]).GetComponent<Button>();
                button.targetGraphic.color = i == page ? Color.white : new Color(.72f, .66f, .59f);
                Label(tabs[i]).color = i == page ? new Color(.16f, .08f, .03f) : White;
            }
            Refresh();
        }

        void Refresh()
        {
            if (!_open) return;
            Label("Resolution").text = $"{Screen.width} × {Screen.height}  ▾";
            Label("WindowMode").text = RuntimeDisplaySettings.ModeName(Screen.fullScreenMode) + "  ▾";
            var quality = QualitySettings.names;
            Label("Quality").text = (quality.Length == 0 ? "默认" : quality[Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, quality.Length - 1)]) + "  ▾";
            Label("Vsync").text = QualitySettings.vSyncCount > 0 ? "● 开启" : "○ 关闭";
            int fps = PlayerPrefs.GetInt("BombTavern.FrameRate", 120);
            foreach (var name in new[] { "Fps60", "Fps120", "FpsUnlimited" })
                Find(name).GetComponent<Image>().color = (name == "Fps60" && fps == 60) ||
                    (name == "Fps120" && fps == 120) || (name == "FpsUnlimited" && fps < 0)
                        ? Color.white : new Color(.7f, .65f, .6f);
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

        VoiceChatManager Voice() => _voice != null ? _voice : (_voice = FindObjectOfType<VoiceChatManager>());
        void ToggleMicTest() { var voice = Voice(); if (voice == null) return; if (voice.IsMicrophoneTestRunning) voice.StopMicrophoneTest(); else voice.BeginMicrophoneTest(); RefreshMic(); }
        void CycleMode() { var next = Screen.fullScreenMode == FullScreenMode.Windowed ? FullScreenMode.FullScreenWindow : Screen.fullScreenMode == FullScreenMode.FullScreenWindow ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed; RuntimeDisplaySettings.SetMode(next); Refresh(); }
        void CycleQuality() { if (QualitySettings.names.Length == 0) return; RuntimeDisplaySettings.SetQuality((QualitySettings.GetQualityLevel() + 1) % QualitySettings.names.Length); Refresh(); }
        void CycleResolution()
        {
            if (_resolutions.Count == 0) return;
            int best = 0, distance = int.MaxValue;
            for (int i = 0; i < _resolutions.Count; i++) { int d = Mathf.Abs(_resolutions[i].x - Screen.width) + Mathf.Abs(_resolutions[i].y - Screen.height); if (d < distance) { distance = d; best = i; } }
            RuntimeDisplaySettings.SetResolution(_resolutions[(best + 1) % _resolutions.Count]); Refresh();
        }
        void SetFrameRate(int rate) { PlayerPrefs.SetInt("BombTavern.FrameRate", rate); Application.targetFrameRate = rate; Refresh(); }
        static void SetBrightness(float value) { PlayerPrefs.SetFloat("BombTavern.Brightness", value); RenderSettings.ambientIntensity = Mathf.Lerp(.65f, 1.35f, value); }
        void RestoreDefaults()
        {
            RuntimeDisplaySettings.SetMode(FullScreenMode.FullScreenWindow);
            var native = Screen.currentResolution; RuntimeDisplaySettings.SetResolution(new Vector2Int(native.width, native.height));
            RuntimeDisplaySettings.SetQuality(Mathf.Max(0, QualitySettings.names.Length - 1));
            RuntimeDisplaySettings.SetVsync(true);
            AudioListener.volume = 1f; PlayerPrefs.SetFloat("BombTavern.Volume", 1f);
            if (AudioDirector.Instance != null) { AudioDirector.Instance.musicVolume = .65f; AudioDirector.Instance.sfxVolume = 1f; }
            PlayerPrefs.SetFloat("BombTavern.MusicVolume", .65f); PlayerPrefs.SetFloat("BombTavern.SfxVolume", 1f);
            if (Voice() != null) { Voice().outputVolume = 1f; Voice().SetVoiceActivation(false); }
            PlayerPrefs.SetFloat("BombTavern.VoiceVolume", 1f); SetBrightness(.5f);
            PlayerPrefs.SetFloat("BombTavern.UiScale", 1f); SetFrameRate(120); Refresh(); PlayerPrefs.Save();
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
            foreach (var sprite in Resources.LoadAll<Sprite>("BombTavern/UI/SettingsSheet")) sprites[sprite.name] = sprite;
            Sprite S(string name) => sprites.TryGetValue(name, out var sprite) ? sprite : null;
            var root = NewRect(null, "SettingsPopup", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            root.gameObject.AddComponent<SettingsPopupView>();
            ImageAt(root, "Shade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null, new Color(0, 0, 0, .72f), true);
            var panel = NewRect(root, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1600, 1050));
            panel.gameObject.AddComponent<Image>().color = new Color(.105f, .052f, .024f, .97f);
            panel.gameObject.AddComponent<CanvasGroup>(); panel.gameObject.AddComponent<UiModalAnimator>();
            ImageAt(panel, "TopLine", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -92), new Vector2(1540, 4), null, Gold);
            ImageAt(panel, "BottomLine", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 90), new Vector2(1540, 4), null, Gold);
            ImageAt(panel, "TitleArt", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -58), new Vector2(990, 230), S("SettingsTitle"), Color.white);
            TextAt(panel, "Subtitle", "调整画面、音频与辅助选项", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -182), new Vector2(660, 40), 28, White);
            ImageAt(panel, "Close", Vector2.one, Vector2.one, new Vector2(-52, -48), new Vector2(72, 72), S("SettingsClose"), Color.white, true);
            ImageAt(panel, "Side", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(238, -12), new Vector2(404, 708), null, new Color(.08f, .04f, .02f, .6f));
            string[] tabNames = { "TabDisplay", "TabAudio", "TabAssist" };
            string[] tabLabels = { "显示", "音频", "辅助功能" };
            string[] tabIcons = { "SettingsMonitor", "SettingsSound", "SettingsGear" };
            for (int i = 0; i < 3; i++)
            {
                var tab = ImageAt(panel, tabNames[i], new Vector2(0, 1), new Vector2(0, 1), new Vector2(242, -245 - 140 * i), new Vector2(370, 112), S(i == 0 ? "SettingsButtonGold" : "SettingsButtonDark"), Color.white, true);
                ImageAt(tab, "Icon", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(60, 0), new Vector2(66, 66), S(tabIcons[i]), Color.white);
                TextAt(tab, "Caption", tabLabels[i], new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(40, 0), new Vector2(200, 70), 35, White);
            }
            var display = Page(panel, "DisplayPage");
            Heading(display, "显示设置");
            RowButton(display, "ResolutionRow", "分辨率", "Resolution", -120, S("SettingsDropdown"));
            RowButton(display, "ModeRow", "窗口模式", "WindowMode", -220, S("SettingsDropdown"));
            RowButton(display, "QualityRow", "画面质量", "Quality", -320, S("SettingsDropdown"));
            RowButton(display, "VsyncRow", "垂直同步", "Vsync", -420, S("SettingsToggleOn"));
            TextAt(display, "FpsTitle", "帧率上限", new Vector2(0, 1), new Vector2(0, 1), new Vector2(155, -520), new Vector2(250, 65), 30, White);
            string[] fps = { "Fps60", "Fps120", "FpsUnlimited" }; string[] fpsLabels = { "60", "120", "无限" };
            for (int i = 0; i < 3; i++)
            {
                var button = ImageAt(display, fps[i], new Vector2(0, 1), new Vector2(0, 1), new Vector2(610 + i * 170, -520), new Vector2(158, 68), S("SettingsButtonDark"), Color.white, true);
                TextAt(button, "Caption", fpsLabels[i], new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(150, 60), 28, White);
            }
            RowSlider(display, "Brightness", "亮度", -620, S("SettingsSliderTrack"), S("SettingsSliderKnob"), 0, 1, .5f);
            RowSlider(display, "UiScale", "UI 缩放", -720, S("SettingsSliderTrack"), S("SettingsSliderKnob"), .75f, 1.25f, 1f);
            var audio = Page(panel, "AudioPage"); Heading(audio, "音频设置");
            RowSlider(audio, "MasterVolume", "主音量", -135, S("SettingsSliderTrack"), S("SettingsSliderKnob"), 0, 1, 1);
            RowSlider(audio, "MusicVolume", "音乐音量", -245, S("SettingsSliderTrack"), S("SettingsSliderKnob"), 0, 1, .65f);
            RowSlider(audio, "SfxVolume", "音效音量", -355, S("SettingsSliderTrack"), S("SettingsSliderKnob"), 0, 1, 1);
            RowSlider(audio, "VoiceVolume", "语音音量", -465, S("SettingsSliderTrack"), S("SettingsSliderKnob"), 0, 1, 1);
            WideButton(audio, "VoiceMode", "按住 V 发言", -585, S("SettingsButtonDark"));
            WideButton(audio, "MicTest", "录制 3 秒并回放", -690, S("SettingsButtonDark"));
            TextAt(audio, "MicStatus", "", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -765), new Vector2(980, 48), 23, White);
            var assist = Page(panel, "AssistPage"); Heading(assist, "辅助功能");
            WideButton(assist, "Rules", "查看完整规则", -145, S("SettingsButtonDark"));
            TextAt(assist, "VoiceHint", "按 V 发言；联机对局中打开设置不会暂停游戏。", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -260), new Vector2(1000, 90), 28, White);
            WideButton(assist, "ExitMatch", "退出对局，返回大厅", -425, S("SettingsButtonDark"));
            TextAt(assist, "MatchHint", "", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -500), new Vector2(1000, 70), 25, White);
            WideButton(assist, "ExitGame", "退出游戏", -650, S("SettingsButtonDark"));
            var footer = NewRect(panel, "Footer", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 43), new Vector2(1500, 90));
            string[] footNames = { "Restore", "LeaveSettings", "Confirm" }; string[] footLabels = { "恢复默认", "退出设置", "确定" };
            for (int i = 0; i < 3; i++)
            {
                var button = ImageAt(footer, footNames[i], new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-470 + 470 * i, 0), new Vector2(420, 82), S(i == 2 ? "SettingsButtonGold" : "SettingsButtonDark"), Color.white, true);
                TextAt(button, "Caption", footLabels[i], new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(370, 70), 34, i == 2 ? new Color(.17f, .08f, .03f) : White);
            }
            display.gameObject.SetActive(true); audio.gameObject.SetActive(false); assist.gameObject.SetActive(false);
            return root.gameObject;
        }

        static RectTransform Page(RectTransform panel, string name) => NewRect(panel, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(1080, -590), new Vector2(1010, 790));
        static void Heading(RectTransform parent, string title) { TextAt(parent, "Heading", title, new Vector2(0, 1), new Vector2(0, 1), new Vector2(250, -32), new Vector2(480, 62), 38, Gold); ImageAt(parent, "HeadingLine", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -78), new Vector2(970, 2), null, Gold); }
        static void RowButton(RectTransform parent, string rowName, string label, string control, float y, Sprite sprite)
        {
            TextAt(parent, rowName, label, new Vector2(0, 1), new Vector2(0, 1), new Vector2(155, y), new Vector2(260, 70), 30, White);
            var button = ImageAt(parent, control, new Vector2(0, 1), new Vector2(0, 1), new Vector2(720, y), new Vector2(475, 72), sprite, Color.white, true);
            TextAt(button, "Value", "", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(420, 62), 27, White);
        }
        static void RowSlider(RectTransform parent, string name, string label, float y, Sprite track, Sprite knob, float min, float max, float value)
        {
            TextAt(parent, name + "Title", label, new Vector2(0, 1), new Vector2(0, 1), new Vector2(155, y), new Vector2(260, 70), 30, White);
            var slider = NewRect(parent, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(682, y), new Vector2(425, 60));
            var bg = ImageAt(slider, "Background", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(420, 50), track, Color.white);
            var fill = NewRect(slider, "Fill Area", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fillImage = ImageAt(fill, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null, Gold);
            var handle = NewRect(slider, "Handle Slide Area", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var handleImage = ImageAt(handle, "Handle", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(60, 60), knob, Color.white);
            var component = slider.gameObject.AddComponent<Slider>(); component.minValue = min; component.maxValue = max; component.value = value;
            component.fillRect = fillImage; component.handleRect = handleImage; component.targetGraphic = handleImage.GetComponent<Image>();
            TextAt(parent, name + "Value", "", new Vector2(0, 1), new Vector2(0, 1), new Vector2(950, y), new Vector2(140, 65), 28, White);
        }
        static void WideButton(RectTransform parent, string name, string text, float y, Sprite sprite)
        {
            var button = ImageAt(parent, name, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, y), new Vector2(900, 82), sprite, Color.white, true);
            TextAt(button, "Caption", text, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(830, 68), 28, White);
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
