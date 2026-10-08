using System.Collections.Generic;
using BombTavern.Config;
using BombTavern.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 开局菜单的纯 UGUI 视图。业务状态由 <see cref="PreGameMenu"/> 持有，
    /// 本类只负责布局、输入与视觉反馈，便于以后换成美术预制体而不改开局流程。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PreGameMenuUgui : MonoBehaviour
    {
        PreGameMenu _owner;
        Canvas _canvas;
        RectTransform _safe;
        RectTransform _home;
        RectTransform _statsPanel;
        RectTransform _statsBlocker;
        bool _setupVisible;
        RectTransform _left;
        RectTransform _right;
        RectTransform _footer;
        RectTransform _setupBack;
        RectTransform _addressLabel;
        RectTransform _portRow;
        TMP_InputField _nameInput;
        TMP_InputField _addressInput;
        TMP_InputField _portInput;
        TMP_Text _networkHint;
        TMP_Text _startLabel;
        RectTransform _settingsPanel;
        RectTransform _settingsBlocker;
        RectTransform _ruleSetupPanel;
        RectTransform _ruleSetupBlocker;
        bool _ruleSetupOpen;
        UiModalAnimator _settingsAnimator;
        TMP_Text _settingsValue;
        Slider _volumeSlider;
        Toggle _fullscreenToggle;
        Toggle _vsyncToggle;
        Button _voiceTestButton;
        Button _voiceModeButton;
        Button _mapButton;
        Button _bombButton;
        Button _driftButton;
        Button _turnTimeButton;
        Button _rulesButton;
        Button _scanButton;
        TMP_Text _voiceTestStatus;
        GameRulesTutorial _tutorial;
        VoiceChatManager _voice;
        bool _settingsOpen;
        readonly List<Button> _modeButtons = new List<Button>();
        readonly List<Button> _skinButtons = new List<Button>();
        int _lastMode = -1;
        string _lastSkin;
        Vector2Int _lastScreen;
        Rect _lastSafeArea;

        public void Configure(PreGameMenu owner)
        {
            _owner = owner;
            Build();
            RefreshSelection(true);
            if (HasCommandLineArg("-btOpenSettings")) ToggleSettings();
            _tutorial.ShowFirstTime();
        }

        void Build()
        {
            LoadRuntimeSettings();
            _canvas = UiKit.CreateCanvas("PreGameMenuCanvas", 220);
            _canvas.transform.SetParent(transform, false);
            _safe = UiKit.CreateSafeAreaRoot(_canvas);

            BuildHome();
            BuildRoster();
            BuildSessionPanel();
            BuildLoopFooter();
            BuildSettingsPanel();
            BuildGameSetupPanel();
            BuildStatsPanel();
            _tutorial = _safe.gameObject.AddComponent<GameRulesTutorial>();
            _tutorial.Configure(_safe);
            ApplyLayout();
            ShowSetup(false);
        }

        Sprite HomeSprite(Texture2D sheet, int x, int y, int width, int height)
        {
            return Sprite.Create(sheet, new Rect(x, sheet.height - y - height, width, height),
                new Vector2(.5f, .5f), 100f);
        }

        void BuildHome()
        {
            var prefab = Resources.Load<GameObject>("BombTavern/Prefabs/UI/HomeMenu");
            if (prefab == null)
            {
                BuildHomeFallback();
                return;
            }

            _home = Instantiate(prefab, _safe, false).GetComponent<RectTransform>();
            if (_home == null)
            {
                Debug.LogError("[主页] HomeMenu 预制体缺少 RectTransform。");
                return;
            }
            BindHomeButton("Home_开始游戏", () => ShowSetup(true));
            BindHomeButton("Home_游戏说明", ShowTutorial);
            BindHomeButton("Home_设置", ToggleSettings);
            BindHomeButton("Home_战绩统计", ShowStats);
            BindHomeButton("Home_退出游戏", () => Application.Quit());
        }

        void BindHomeButton(string childName, System.Action action)
        {
            var child = _home.Find(childName);
            var button = child != null ? child.GetComponent<Button>() : null;
            if (button == null)
            {
                Debug.LogError("[主页] 预制体中缺少按钮：" + childName);
                return;
            }
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => { Click(); action(); });
        }

        // 仅用于首次生成预制体以及资源丢失时的回退；正常运行使用可编辑的 HomeMenu.prefab。
        void BuildHomeFallback()
        {
            _home = UiKit.Rect(_safe, "Home", Vector2.zero, Vector2.one,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            // 菜单左侧保留暗部，让金色按钮和标题在实时酒馆背景上清晰可读。
            var shade = UiKit.Rect(_home, "LeftShade", new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(0, .5f), Vector2.zero, new Vector2(830, 0));
            shade.gameObject.AddComponent<Image>().color = new Color(.025f, .016f, .011f, .68f);
            var sheet = Resources.Load<Texture2D>("BombTavern/UI/HomeMenuSheet");
            if (sheet == null)
            {
                Debug.LogError("[主页] 找不到 HomeMenuSheet，检查 Resources/BombTavern/UI。");
                return;
            }
            var logo = UiKit.Rect(_home, "Logo", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(72, -50), new Vector2(690, 345));
            var logoImage = logo.gameObject.AddComponent<Image>();
            logoImage.sprite = HomeSprite(sheet, 60, 24, 1140, 592);
            logoImage.preserveAspect = true;
            logoImage.raycastTarget = false;

            Sprite dark = HomeSprite(sheet, 18, 635, 590, 165);
            Sprite gold = HomeSprite(sheet, 625, 635, 610, 165);
            int[] iconX = { 50, 300, 535, 775, 995 };
            int[] iconWidth = { 230, 210, 200, 200, 230 };
            string[] labels = { "开始游戏", "游戏说明", "设置", "战绩统计", "退出游戏" };
            System.Action[] actions = { () => ShowSetup(true), ShowTutorial, ToggleSettings, ShowStats, () => Application.Quit() };
            for (int i = 0; i < labels.Length; i++)
            {
                bool exit = i == 4;
                float top = exit ? 1260f : 505f + i * 145f;
                float width = exit ? 370f : 600f;
                float height = exit ? 100f : 132f;
                var row = UiKit.Rect(_home, "Home_" + labels[i], new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(0, 1), new Vector2(92, -top), new Vector2(width, height));
                var image = row.gameObject.AddComponent<Image>();
                image.sprite = i == 0 ? gold : dark;
                image.type = Image.Type.Simple;
                var button = row.gameObject.AddComponent<Button>();
                int index = i;
                button.onClick.AddListener(() => { Click(); actions[index](); });
                var icon = UiKit.Rect(row, "Icon", new Vector2(0, .5f), new Vector2(0, .5f),
                    new Vector2(0, .5f), new Vector2(exit ? 40 : 45, 0),
                    new Vector2(exit ? 60 : 72, exit ? 60 : 72));
                var iconImage = icon.gameObject.AddComponent<Image>();
                iconImage.sprite = HomeSprite(sheet, iconX[i], 825, iconWidth[i], 190);
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                var label = UiKit.Label(row, labels[i], exit ? 36 : 42,
                    i == 0 ? new Color(.15f, .085f, .025f) : new Color(1f, .89f, .72f),
                    TextAnchor.MiddleLeft);
                label.fontStyle = FontStyles.Bold;
                label.rectTransform.offsetMin = new Vector2(exit ? 126 : 190, 0);
                label.rectTransform.offsetMax = new Vector2(-42, 0);
                label.raycastTarget = false;
                if (!exit)
                {
                    var arrow = UiKit.Label(row, "›", 64, i == 0 ? new Color(.37f, .20f, .06f) : UiTheme.Accent,
                        TextAnchor.MiddleCenter);
                    arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = new Vector2(1, .5f);
                    arrow.rectTransform.pivot = new Vector2(1, .5f);
                    arrow.rectTransform.anchoredPosition = new Vector2(-32, 0);
                    arrow.rectTransform.sizeDelta = new Vector2(60, 90);
                    arrow.raycastTarget = false;
                }
                else
                {
                    row.anchorMin = row.anchorMax = new Vector2(0, 0);
                    row.pivot = new Vector2(0, 0);
                    row.anchoredPosition = new Vector2(92, 70);
                }
            }
        }

        void ShowSetup(bool show)
        {
            _setupVisible = show;
            if (_home != null) _home.gameObject.SetActive(!show);
            if (_left != null) _left.gameObject.SetActive(show);
            if (_right != null) _right.gameObject.SetActive(show);
            if (_footer != null) _footer.gameObject.SetActive(show);
            if (_setupBack != null) _setupBack.gameObject.SetActive(show);
        }

        void BuildStatsPanel()
        {
            _statsBlocker = UiKit.Rect(_safe, "StatsBlocker", Vector2.zero, Vector2.one,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            _statsBlocker.gameObject.AddComponent<Image>().color = new Color(.01f, .007f, .005f, .76f);
            var panel = UiKit.Panel(_safe, "StatsPanel");
            _statsPanel = panel.rectTransform;
            _statsPanel.anchorMin = _statsPanel.anchorMax = _statsPanel.pivot = new Vector2(.5f, .5f);
            _statsPanel.sizeDelta = new Vector2(760, 360);
            UiKit.VBox(_statsPanel, 20, new RectOffset(36, 36, 30, 30));
            UiKit.Size(UiKit.Label(_statsPanel, "战绩统计", UiTheme.FontTitle, UiTheme.Accent,
                TextAnchor.MiddleCenter).rectTransform, h: 56);
            UiKit.Size(UiKit.Paragraph(_statsPanel,
                "目前游戏只保存每局结束时的结算结果，尚未记录跨局历史战绩。\n你可以在对局结束界面查看本局详细战绩。",
                UiTheme.FontBody, UiTheme.TextMid, TextAnchor.MiddleCenter).rectTransform, h: 150);
            UiKit.Size(UiKit.Btn(_statsPanel, "返回主页", HideStats).GetComponent<RectTransform>(), h: 55);
            _statsPanel.gameObject.SetActive(false);
            _statsBlocker.gameObject.SetActive(false);
        }

        void ShowStats()
        {
            _statsBlocker.gameObject.SetActive(true);
            _statsPanel.gameObject.SetActive(true);
        }

        void HideStats()
        {
            _statsBlocker.gameObject.SetActive(false);
            _statsPanel.gameObject.SetActive(false);
        }

        void BuildSettingsPanel()
        {
            // 全屏遮罩把输入焦点锁在设置层，点击空白处也可关闭，符合网游常见的 modal 行为。
            var blocker = UiKit.Rect(_safe, "SettingsBlocker", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _settingsBlocker = blocker;
            var blockerImage = blocker.gameObject.AddComponent<Image>();
            blockerImage.color = new Color(0.015f, 0.01f, 0.008f, 0.72f);
            var blockerButton = blocker.gameObject.AddComponent<Button>();
            blockerButton.onClick.AddListener(ToggleSettings);

            var panel = UiKit.Panel(_safe, "SettingsPanel");
            _settingsPanel = panel.rectTransform;
            _settingsAnimator = panel.gameObject.AddComponent<UiModalAnimator>();
            _settingsPanel.anchorMin = _settingsPanel.anchorMax = _settingsPanel.pivot = new Vector2(0.5f, 0.5f);
            _settingsPanel.anchoredPosition = Vector2.zero;
            _settingsPanel.sizeDelta = new Vector2(900f, 1260f);
            UiKit.VBox(_settingsPanel, UiTheme.S2, UiTheme.PanelPadding);

            var title = UiKit.Label(_settingsPanel, "游戏设置", UiTheme.FontTitle, UiTheme.Accent,
                TextAnchor.MiddleCenter);
            title.fontStyle = FontStyles.Bold;
            UiKit.Size(title.rectTransform, h: 44f);

            var hint = UiKit.Paragraph(_settingsPanel, "设置会自动保存到本机，下次启动继续生效。",
                UiTheme.FontCaption, UiTheme.TextLo, TextAnchor.MiddleCenter);
            UiKit.Size(hint.rectTransform, h: 24f);

            UiKit.SectionTitle(_settingsPanel, "显示");
            _fullscreenToggle = UiKit.ToggleRow(_settingsPanel, "全屏显示", Screen.fullScreen,
                value =>
                {
                    Screen.fullScreen = value;
                    PlayerPrefs.SetInt("BombTavern.Fullscreen", value ? 1 : 0);
                    PlayerPrefs.Save();
                });
            _vsyncToggle = UiKit.ToggleRow(_settingsPanel, "垂直同步", QualitySettings.vSyncCount > 0,
                value =>
                {
                    QualitySettings.vSyncCount = value ? 1 : 0;
                    PlayerPrefs.SetInt("BombTavern.VSync", QualitySettings.vSyncCount);
                    PlayerPrefs.Save();
                });

            UiKit.SectionTitle(_settingsPanel, "音频");
            _volumeSlider = UiKit.SliderRow(_settingsPanel, "主音量", AudioListener.volume,
                value =>
                {
                    AudioListener.volume = value;
                    PlayerPrefs.SetFloat("BombTavern.Volume", value);
                    PlayerPrefs.Save();
                }, out _);

            UiKit.SectionTitle(_settingsPanel, "辅助与游戏");
            _voiceTestButton = UiKit.Btn(_settingsPanel, "录制 3 秒并回放", ToggleVoiceTest);
            UiKit.Size(_voiceTestButton.GetComponent<RectTransform>(), h: 48f);
            _voiceTestStatus = UiKit.Paragraph(_settingsPanel, "", UiTheme.FontCaption, UiTheme.TextLo,
                TextAnchor.MiddleCenter);
            UiKit.Size(_voiceTestStatus.rectTransform, h: 54f);

            _voiceModeButton = UiKit.Btn(_settingsPanel, "", ToggleVoiceMode);
            UiKit.Size(_voiceModeButton.GetComponent<RectTransform>(), h: 46f);

            _mapButton = UiKit.Btn(_settingsPanel, "", CycleMap);
            _bombButton = UiKit.Btn(_settingsPanel, "", CycleBombs);
            _driftButton = UiKit.Btn(_settingsPanel, "", CycleDrift);
            _turnTimeButton = UiKit.Btn(_settingsPanel, "", CycleTurnTime);
            UiKit.Size(_mapButton.GetComponent<RectTransform>(), h: 44f);
            UiKit.Size(_bombButton.GetComponent<RectTransform>(), h: 44f);
            UiKit.Size(_driftButton.GetComponent<RectTransform>(), h: 44f);
            UiKit.Size(_turnTimeButton.GetComponent<RectTransform>(), h: 44f);

            _rulesButton = UiKit.Btn(_settingsPanel, "查看完整规则", ShowTutorial);
            UiKit.Size(_rulesButton.GetComponent<RectTransform>(), h: 44f);

            _settingsValue = UiKit.Paragraph(_settingsPanel, "", UiTheme.FontCaption, UiTheme.TextMid,
                TextAnchor.MiddleCenter);
            UiKit.Size(_settingsValue.rectTransform, h: 42f);

            var close = UiKit.Btn(_settingsPanel, "完成", ToggleSettings, accent: true);
            UiKit.Size(close.GetComponent<RectTransform>(), h: 50f);
            blocker.SetSiblingIndex(_safe.childCount - 2);
            _settingsBlocker.gameObject.SetActive(false);
            panel.gameObject.GetComponent<CanvasGroup>().alpha = 0f;
            panel.transform.localScale = Vector3.one * 0.96f;
            _settingsPanel.gameObject.SetActive(false);
            RefreshSettingsLabels();
            TavernSettingsLayout.Arrange(_settingsPanel, ToggleSettings);
            var audioPage = _settingsPanel.Find("音频");
            _voiceModeButton.transform.SetParent(audioPage, false);
            _voiceTestStatus.transform.SetParent(audioPage, false);
            _voiceTestStatus.gameObject.SetActive(true);
            AddMenuDisplayControls(_settingsPanel.Find("显示"));
        }

        void AddMenuDisplayControls(Transform page)
        {
            Button resolution = null, quality = null, mode = null;
            resolution = UiKit.Btn(page, $"分辨率    {Screen.width} × {Screen.height}   ›", () =>
            {
                var options = RuntimeDisplaySettings.ResolutionOptions();
                int index = options.FindIndex(r => r.x == Screen.width && r.y == Screen.height);
                var next = options[(index + 1) % options.Count];
                RuntimeDisplaySettings.SetResolution(next);
                resolution.GetComponentInChildren<TMP_Text>(true).text = $"分辨率    {next.x} × {next.y}   ›";
            });
            mode = UiKit.Btn(page, "窗口模式    " + RuntimeDisplaySettings.ModeName(Screen.fullScreenMode) + "   ›", () =>
            {
                var next = Screen.fullScreenMode == FullScreenMode.Windowed ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                RuntimeDisplaySettings.SetMode(next);
                mode.GetComponentInChildren<TMP_Text>(true).text = "窗口模式    " + RuntimeDisplaySettings.ModeName(next) + "   ›";
            });
            quality = UiKit.Btn(page, "画面质量    " + QualitySettings.names[QualitySettings.GetQualityLevel()] + "   ›", () =>
            {
                RuntimeDisplaySettings.SetQuality((QualitySettings.GetQualityLevel() + 1) % QualitySettings.names.Length);
                quality.GetComponentInChildren<TMP_Text>(true).text = "画面质量    " + QualitySettings.names[QualitySettings.GetQualityLevel()] + "   ›";
            });
            UiKit.Size(resolution.GetComponent<RectTransform>(), h: 56);
            UiKit.Size(mode.GetComponent<RectTransform>(), h: 56);
            UiKit.Size(quality.GetComponent<RectTransform>(), h: 56);
            _fullscreenToggle.transform.parent.gameObject.SetActive(false);
            resolution.transform.SetSiblingIndex(1); mode.transform.SetSiblingIndex(2); quality.transform.SetSiblingIndex(3);
        }

        void BuildGameSetupPanel()
        {
            _ruleSetupBlocker = UiKit.Rect(_safe, "GameSetupBlocker", Vector2.zero, Vector2.one,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            _ruleSetupBlocker.gameObject.AddComponent<Image>().color = new Color(.015f, .01f, .008f, .78f);
            var panel = UiKit.Panel(_safe, "GameSetupPanel");
            _ruleSetupPanel = panel.rectTransform;
            _ruleSetupPanel.anchorMin = _ruleSetupPanel.anchorMax = _ruleSetupPanel.pivot = new Vector2(.5f, .5f);
            _ruleSetupPanel.anchoredPosition = Vector2.zero;
            _ruleSetupPanel.sizeDelta = new Vector2(900, 710);
            UiKit.VBox(_ruleSetupPanel, 14, new RectOffset(42, 42, 32, 32));
            var title = UiKit.Label(_ruleSetupPanel, "创建游戏 · 对局规则", UiTheme.FontTitle, UiTheme.AccentHi,
                TextAnchor.MiddleCenter);
            title.fontStyle = FontStyles.Bold;
            UiKit.Size(title.rectTransform, h: 54);
            var intro = UiKit.Paragraph(_ruleSetupPanel, "调整牌桌规模与每回合的节奏。开房时由房主决定，加入者自动使用房主的规则。",
                UiTheme.FontBody, UiTheme.TextMid, TextAnchor.MiddleCenter);
            UiKit.Size(intro.rectTransform, h: 65);
            UiKit.SectionTitle(_ruleSetupPanel, "地图与节奏");
            foreach (var button in new[] { _mapButton, _bombButton, _driftButton, _turnTimeButton })
            {
                button.transform.SetParent(_ruleSetupPanel, false);
                button.gameObject.SetActive(true);
                UiKit.Size(button.GetComponent<RectTransform>(), h: 58);
            }
            var actions = UiKit.Rect(_ruleSetupPanel, "SetupActions", Vector2.zero, Vector2.one,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            UiKit.Size(actions, h: 70);
            var row = UiKit.HBox(actions, 22, new RectOffset());
            row.childForceExpandWidth = true;
            var back = UiKit.Btn(actions, "返回", HideGameSetup);
            var create = UiKit.Btn(actions, "确认并开始", ConfirmGameSetup, accent: true);
            UiKit.Size(back.GetComponent<RectTransform>(), w: 390, h: 60);
            UiKit.Size(create.GetComponent<RectTransform>(), w: 390, h: 60);
            TavernSettingsLayout.StylePrimary(create);
            _ruleSetupBlocker.gameObject.SetActive(false);
            _ruleSetupPanel.gameObject.SetActive(false);
        }

        void ShowGameSetup()
        {
            RefreshSettingsLabels();
            _ruleSetupOpen = true;
            _ruleSetupBlocker.gameObject.SetActive(true);
            _ruleSetupPanel.gameObject.SetActive(true);
        }

        void HideGameSetup()
        {
            _ruleSetupOpen = false;
            _ruleSetupBlocker.gameObject.SetActive(false);
            _ruleSetupPanel.gameObject.SetActive(false);
        }

        void ConfirmGameSetup()
        {
            HideGameSetup();
            _owner.BeginGame();
        }

        void ToggleSettings()
        {
            _settingsOpen = !_settingsOpen;
            if (!_settingsOpen && _voice != null && _voice.IsMicrophoneTestRunning)
                _voice.StopMicrophoneTest();
            if (_settingsAnimator != null)
            {
                if (_settingsOpen) _settingsAnimator.Show();
                else _settingsAnimator.Hide();
            }
            if (_settingsBlocker != null) _settingsBlocker.gameObject.SetActive(_settingsOpen);
            if (_settingsOpen) RefreshSettingsLabels();
            Click();
        }

        void ToggleVoiceTest()
        {
            if (_voice == null) _voice = FindObjectOfType<VoiceChatManager>();
            if (_voice == null) return;
            if (_voice.IsMicrophoneTestRunning) _voice.StopMicrophoneTest();
            else _voice.BeginMicrophoneTest();
            RefreshVoiceTest();
            Click();
        }

        void ToggleVoiceMode()
        {
            if (_voice == null) _voice = FindObjectOfType<VoiceChatManager>();
            if (_voice == null) return;
            _voice.SetVoiceActivation(!_voice.VoiceActivationEnabled);
            RefreshVoiceTest();
            Click();
        }

        void ShowTutorial()
        {
            _tutorial?.Show();
            Click();
        }

        SessionContext Context => SessionContext.Instance;

        void CycleMap()
        {
            var c = Context; if (c == null) return;
            if (c.boardColumns == 8) { c.boardColumns = 10; c.boardRows = 10; }
            else if (c.boardColumns == 10) { c.boardColumns = 12; c.boardRows = 10; }
            else if (c.boardColumns == 12) { c.boardColumns = 16; c.boardRows = 12; }
            else { c.boardColumns = 8; c.boardRows = 8; }
            c.bombCount = Mathf.Clamp(c.bombCount, 1, c.boardColumns * c.boardRows / 2);
            RefreshSettingsLabels(); Click();
        }

        void CycleBombs()
        {
            var c = Context; if (c == null) return;
            int max = Mathf.Max(5, c.boardColumns * c.boardRows / 2);
            c.bombCount += 5;
            if (c.bombCount > max) c.bombCount = 5;
            RefreshSettingsLabels(); Click();
        }

        void CycleDrift()
        {
            var c = Context; if (c == null) return;
            float[] baseValues = { 2f, 3f, 5f, 7f, 10f };
            int next = 0;
            for (int i = 0; i < baseValues.Length; i++)
                if (Mathf.Approximately(c.driftPercent, baseValues[i])) { next = (i + 1) % baseValues.Length; break; }
            c.driftPercent = baseValues[next];
            c.driftGrowthPercentPerRound = c.driftPercent <= 3f ? 2f : c.driftPercent <= 7f ? 3f : 5f;
            RefreshSettingsLabels(); Click();
        }

        void CycleTurnTime()
        {
            var c = Context; if (c == null) return;
            int[] values = { 15, 30, 45, 60, 90 };
            int next = 0;
            for (int i = 0; i < values.Length; i++)
                if (Mathf.RoundToInt(c.turnTimeLimitSeconds) == values[i]) { next = (i + 1) % values.Length; break; }
            c.turnTimeLimitSeconds = values[next];
            RefreshSettingsLabels(); Click();
        }

        void RefreshVoiceTest()
        {
            if (_voiceTestButton == null || _voiceTestStatus == null) return;
            if (_voice == null) _voice = FindObjectOfType<VoiceChatManager>();
            if (_voice == null)
            {
                _voiceTestButton.interactable = false;
                _voiceTestStatus.text = "当前场景没有语音管理器";
                return;
            }

            _voiceTestButton.interactable = _voice.IsMicReady;
                _voiceTestButton.GetComponentInChildren<TMP_Text>(true).text = _voice.IsMicrophoneTestRunning
                ? "停止麦克风测试"
                : "录制 3 秒并回放";
            int meter = Mathf.RoundToInt(_voice.MicrophoneInputLevel * 10f);
            _voiceTestStatus.text = $"设备：{_voice.MicrophoneName}\n" +
                                    new string('■', meter) + new string('□', 10 - meter) +
                                    "  " + _voice.MicrophoneTestStatus;
            if (_voiceModeButton != null)
                _voiceModeButton.GetComponentInChildren<TMP_Text>(true).text = _voice.VoiceActivationEnabled
                    ? "发言方式    自动检测说话并传输"
                    : "发言方式    按住 V 键发言";
        }

        void ToggleVolume()
        {
            float next = AudioListener.volume > 0.01f ? 0f : 1f;
            AudioListener.volume = next;
            PlayerPrefs.SetFloat("BombTavern.Volume", next);
            PlayerPrefs.Save();
            RefreshSettingsLabels();
            Click();
        }

        void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            PlayerPrefs.SetInt("BombTavern.Fullscreen", Screen.fullScreen ? 1 : 0);
            PlayerPrefs.Save();
            RefreshSettingsLabels();
            Click();
        }

        void ToggleVsync()
        {
            QualitySettings.vSyncCount = QualitySettings.vSyncCount > 0 ? 0 : 1;
            PlayerPrefs.SetInt("BombTavern.VSync", QualitySettings.vSyncCount);
            PlayerPrefs.Save();
            RefreshSettingsLabels();
            Click();
        }

        void RefreshSettingsLabels()
        {
            if (_settingsPanel == null) return;
            if (_volumeSlider != null) _volumeSlider.SetValueWithoutNotify(AudioListener.volume);
            if (_fullscreenToggle != null) _fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
            if (_vsyncToggle != null) _vsyncToggle.SetIsOnWithoutNotify(QualitySettings.vSyncCount > 0);
            _settingsValue.text = "鼠标拖拽转视角 · 左键翻格 · 右键插旗 · H 键切换托管";
            var c = Context;
            if (c != null)
            {
                if (_mapButton != null) _mapButton.GetComponentInChildren<TMP_Text>(true).text = $"地图大小    {c.boardColumns} × {c.boardRows}";
                if (_bombButton != null) _bombButton.GetComponentInChildren<TMP_Text>(true).text = $"炸弹数量    {c.bombCount}（平均 {c.bombCount / (float)(c.boardColumns * c.boardRows):P0}）";
                if (_driftButton != null) _driftButton.GetComponentInChildren<TMP_Text>(true).text = $"概率偏移    初始 ±{c.driftPercent:0.#}% · 每轮增长 {c.driftGrowthPercentPerRound:0.#}%";
                if (_turnTimeButton != null) _turnTimeButton.GetComponentInChildren<TMP_Text>(true).text = $"回合限时    {c.turnTimeLimitSeconds:0} 秒";
            }
        }

        void LoadRuntimeSettings()
        {
            AudioListener.volume = PlayerPrefs.GetFloat("BombTavern.Volume", 1f);
            if (PlayerPrefs.HasKey("BombTavern.Fullscreen"))
                Screen.fullScreen = PlayerPrefs.GetInt("BombTavern.Fullscreen", 1) != 0;
            QualitySettings.vSyncCount = PlayerPrefs.GetInt("BombTavern.VSync", QualitySettings.vSyncCount);
        }

        void BuildRoster()
        {
            var panel = UiKit.Panel(_safe, "RosterPanel");
            _left = panel.rectTransform;
            UiKit.VBox(_left, UiTheme.S2, UiTheme.PanelPadding);

            var eyebrow = UiKit.Label(_left, "01  选择身份", UiTheme.FontCaption, UiTheme.Accent);
            eyebrow.fontStyle = FontStyles.Bold;
            UiKit.Size(eyebrow.rectTransform, h: 20f);

            var heading = UiKit.Label(_left, "今晚由谁上桌？", UiTheme.FontHead + 3, UiTheme.TextHi);
            heading.fontStyle = FontStyles.Bold;
            UiKit.Size(heading.rectTransform, h: 34f);

            var note = UiKit.Paragraph(_left, "角色只改变外观。其余座位会由玩家或 AI 补齐。",
                UiTheme.FontCaption, UiTheme.TextLo);
            UiKit.Size(note.rectTransform, h: 38f);

            var catalog = _owner.SkinCatalog;
            if (catalog == null || catalog.skins == null || catalog.skins.Count == 0)
            {
                var missing = UiKit.Paragraph(_left, "尚未配置角色目录。", UiTheme.FontBody, UiTheme.Danger);
                UiKit.Size(missing.rectTransform, h: 42f);
                return;
            }

            var gridRoot = UiKit.Rect(_left, "IdentityCards", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var grid = gridRoot.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(256f, 146f);
            grid.spacing = new Vector2(UiTheme.S2, UiTheme.S2);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.UpperCenter;
            UiKit.Size(gridRoot, h: 304f);

            for (int i = 0; i < catalog.skins.Count; i++)
            {
                SkinDefinition skin = catalog.skins[i];
                if (skin == null) continue;

                var button = UiKit.Btn(gridRoot, skin.displayName, () =>
                {
                    _owner.SelectSkin(skin.skinId);
                    RefreshSelection(true);
                    Click();
                });
                button.name = "Skin_" + skin.skinId;
                UiKit.Size(button.GetComponent<RectTransform>(), w: 256f, h: 146f);

                var chip = UiKit.Rect(button.transform, "ColorChip", new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f),
                    new Vector2(54f, 54f));
                var chipImage = chip.gameObject.AddComponent<Image>();
                chipImage.color = skin.bodyColor;
                chipImage.raycastTarget = false;

                var label = button.GetComponentInChildren<TMP_Text>(true);
                label.alignment = TextAlignmentOptions.Bottom;
                label.rectTransform.offsetMin = new Vector2(12f, 12f);
                label.rectTransform.offsetMax = new Vector2(-12f, -76f);
                var noteLabel = UiKit.Label(button.transform, "身份外观", UiTheme.FontCaption,
                    UiTheme.TextLo, TextAnchor.LowerCenter);
                noteLabel.rectTransform.offsetMin = new Vector2(10f, 8f);
                noteLabel.rectTransform.offsetMax = new Vector2(-10f, -112f);
                _skinButtons.Add(button);
            }
        }

        void BuildSessionPanel()
        {
            var panel = UiKit.Panel(_safe, "SessionPanel");
            _right = panel.rectTransform;
            UiKit.VBox(_right, UiTheme.S2, UiTheme.PanelPadding);

            var eyebrow = UiKit.Label(_right, "02  准备入局", UiTheme.FontCaption, UiTheme.Accent);
            eyebrow.fontStyle = FontStyles.Bold;
            UiKit.Size(eyebrow.rectTransform, h: 20f);

            var heading = UiKit.Label(_right, "建立你的牌桌席位", UiTheme.FontHead + 3, UiTheme.TextHi);
            heading.fontStyle = FontStyles.Bold;
            UiKit.Size(heading.rectTransform, h: 34f);

            var nameLabel = UiKit.Label(_right, "玩家昵称", UiTheme.FontCaption, UiTheme.TextMid);
            UiKit.Size(nameLabel.rectTransform, h: 20f);
            _nameInput = UiKit.Input(_right, _owner.PlayerName, "输入昵称", 16);
            UiKit.Size(_nameInput.GetComponent<RectTransform>(), h: 42f);
            _nameInput.onValueChanged.AddListener(v => _owner.PlayerName = v);

            var modeLabel = UiKit.Label(_right, "游戏方式", UiTheme.FontCaption, UiTheme.TextMid);
            UiKit.Size(modeLabel.rectTransform, h: 20f);

            var modes = UiKit.Rect(_right, "Modes", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UiKit.HBox(modes, UiTheme.S1, new RectOffset());
            UiKit.Size(modes, h: 42f);
            string[] names = { "单人", "开房", "加入" };
            for (int i = 0; i < names.Length; i++)
            {
                int captured = i;
                var button = UiKit.Btn(modes, names[i], () =>
                {
                    _owner.ModeIndex = captured;
                    RefreshSelection(true);
                    Click();
                });
                UiKit.Size(button.GetComponent<RectTransform>(), w: 104f, h: 40f);
                _modeButtons.Add(button);
            }

            var addressLabel = UiKit.Label(_right, "主机地址", UiTheme.FontCaption, UiTheme.TextMid);
            _addressLabel = addressLabel.rectTransform;
            UiKit.Size(addressLabel.rectTransform, h: 20f);
            _addressInput = UiKit.Input(_right, _owner.Address, "例如 192.168.1.10", 64);
            UiKit.Size(_addressInput.GetComponent<RectTransform>(), h: 40f);
            _addressInput.onValueChanged.AddListener(v => _owner.Address = v);

            _scanButton = UiKit.Btn(_right, "扫描局域网房间", SelectDiscoveredRoom);
            UiKit.Size(_scanButton.GetComponent<RectTransform>(), h: 36f);

            var portRow = UiKit.Rect(_right, "PortRow", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _portRow = portRow;
            UiKit.HBox(portRow, UiTheme.S2, new RectOffset());
            UiKit.Size(portRow, h: 40f);
            var portLabel = UiKit.Label(portRow, "端口", UiTheme.FontCaption, UiTheme.TextMid);
            UiKit.Size(portLabel.rectTransform, w: 58f, h: 38f);
            _portInput = UiKit.Input(portRow, _owner.PortText, "7788", 5);
            UiKit.Size(_portInput.GetComponent<RectTransform>(), w: 150f, h: 38f);
            _portInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            _portInput.onValueChanged.AddListener(v => _owner.PortText = v);

            _networkHint = UiKit.Paragraph(_right, string.Empty, UiTheme.FontCaption, UiTheme.TextLo);
            UiKit.Size(_networkHint.rectTransform, h: 48f);

            var start = UiKit.Btn(_right, "开始游戏", StartGame, accent: true);
            UiKit.Size(start.GetComponent<RectTransform>(), h: 54f);
            _startLabel = start.GetComponentInChildren<TMP_Text>(true);
        }

        void BuildLoopFooter()
        {
            var panel = UiKit.Panel(_safe, "CoreLoop", card: true);
            _footer = panel.rectTransform;

            var text = UiKit.Paragraph(_footer,
                "观察概率  →  判断信息是否过时  →  翻格或使用道具  →  承担风险  →  进入下一回合",
                UiTheme.FontBody, UiTheme.TextHi, TextAnchor.MiddleCenter);
            text.fontStyle = FontStyles.Bold;
            text.rectTransform.offsetMin = new Vector2(18f, 8f);
            text.rectTransform.offsetMax = new Vector2(-18f, -8f);

            var back = UiKit.Btn(_safe, "‹  返回主页", () => { Click(); ShowSetup(false); });
            var backRt = back.GetComponent<RectTransform>();
            SetAnchor(backRt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(UiTheme.S4, -35), new Vector2(260, 62));
            // 返回按钮仅在第二层出现，由 ShowSetup 统一管理。
            _setupBack = backRt;
        }

        void StartGame()
        {
            _owner.PlayerName = _nameInput != null ? _nameInput.text : _owner.PlayerName;
            _owner.Address = _addressInput != null ? _addressInput.text : _owner.Address;
            _owner.PortText = _portInput != null ? _portInput.text : _owner.PortText;
            if (_owner.ModeIndex == 2)
            {
                int fallback = int.TryParse(_owner.PortText, out int parsedPort) ? parsedPort : 7788;
                if (!NetAddressUtility.TryParseEndpointInput(_owner.Address, fallback,
                    out string host, out int endpointPort, out string reason))
                {
                    _networkHint.text = reason + " 可先点击“扫描房间”。";
                    return;
                }
                _owner.Address = host;
                _owner.PortText = endpointPort.ToString();
            }
            Click();
            if (_owner.ModeIndex == 2) _owner.BeginGame();
            else ShowGameSetup();
        }

        void Update()
        {
            if (_owner == null) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_tutorial != null && _tutorial.IsOpen) _tutorial.Close();
                else if (_settingsOpen) ToggleSettings();
                else if (_ruleSetupOpen) HideGameSetup();
                else if (_statsPanel != null && _statsPanel.gameObject.activeSelf) HideStats();
                else if (_setupVisible) ShowSetup(false);
            }
            if (_settingsOpen) RefreshVoiceTest();
            if (_lastScreen != new Vector2Int(Screen.width, Screen.height) || _lastSafeArea != Screen.safeArea) ApplyLayout();
            RefreshSelection(false);
            if (_scanButton != null && _owner.ModeIndex == 2)
            {
                var discovery = FindObjectOfType<LanRoomDiscovery>();
                int count = discovery != null ? discovery.Rooms.Count : 0;
                _scanButton.GetComponentInChildren<TMP_Text>(true).text = count > 0
                    ? $"发现 {count} 个房间 · 点击选择"
                    : "扫描局域网房间（也可手动填写地址）";
            }
        }

        int _discoveryIndex;
        void SelectDiscoveredRoom()
        {
            var discovery = FindObjectOfType<LanRoomDiscovery>();
            if (discovery == null || discovery.Rooms.Count == 0)
            {
                _networkHint.text = "暂未发现局域网房间。Radmin 请手动填房主 26.x 地址；樱花穿透请填面板域名和远程端口。";
                return;
            }
            _discoveryIndex %= discovery.Rooms.Count;
            var room = discovery.Rooms[_discoveryIndex++];
            _owner.Address = room.address;
            _owner.PortText = room.port.ToString();
            _addressInput.text = room.address;
            _portInput.text = room.port.ToString();
            _networkHint.text = $"已选择 {room.name} 的房间（{room.address}:{room.port}，当前 {room.players} 人）。";
            Click();
        }

        void RefreshSelection(bool force)
        {
            int mode = _owner.ModeIndex;
            string skin = _owner.SelectedSkinId;
            if (!force && mode == _lastMode && skin == _lastSkin) return;
            _lastMode = mode;
            _lastSkin = skin;

            for (int i = 0; i < _modeButtons.Count; i++)
            {
                var image = _modeButtons[i].targetGraphic as Image;
                if (image != null) image.color = i == mode ? UiTheme.Accent : Color.white;
            }

            for (int i = 0; i < _skinButtons.Count; i++)
            {
                bool selected = _skinButtons[i].name == "Skin_" + skin;
                var image = _skinButtons[i].targetGraphic as Image;
                if (image != null) image.color = selected ? UiTheme.Accent : Color.white;
                var label = _skinButtons[i].GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = (selected ? "▶  " : "     ") + label.text.TrimStart('▶', ' ');
            }

            bool joining = mode == 2;
            if (_addressLabel != null) _addressLabel.gameObject.SetActive(joining);
            if (_addressInput != null) _addressInput.gameObject.SetActive(joining);
            if (_scanButton != null) _scanButton.gameObject.SetActive(joining);
            if (_portRow != null) _portRow.gameObject.SetActive(mode != 0);
            if (_networkHint != null)
                _networkHint.text = mode == 0 ? "与三名 AI 对局，立即进入。"
                    : mode == 1 ? "创建房间后，在大厅把地址告诉其他玩家。"
                    : "局域网可扫描；Radmin 填房主 26.x；樱花穿透填服务器域名，并在端口栏填远程端口。";
            if (_startLabel != null)
                _startLabel.text = mode == 0 ? "开 始 游 戏" : mode == 1 ? "创 建 房 间" : "加 入 房 间";
        }

        void ApplyLayout()
        {
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
            _lastSafeArea = Screen.safeArea;
            bool compact = UiKit.CompactLayout;
            float side = compact ? 540f : 600f;
            float y = UiKit.IsMobile ? 0f : compact ? -104f : -118f;
            float panelScale = UiKit.IsMobile
                ? UiKit.FitScale(new Vector2(1f, 760f), 240f)
                : 1f;

            SetAnchor(_left, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(UiTheme.S4, y), new Vector2(side, compact ? 700f : 760f));
            SetAnchor(_right, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(-UiTheme.S4, y), new Vector2(side + 30f, compact ? 760f : 830f));
            _left.localScale = Vector3.one * panelScale;
            _right.localScale = Vector3.one * panelScale;
            SetAnchor(_footer, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, UiTheme.S4),
                new Vector2(compact ? 900f : 1080f, 68f));
            TavernSettingsLayout.FitToScreen(_settingsPanel);
            if (_ruleSetupPanel != null)
                _ruleSetupPanel.localScale = Vector3.one * UiKit.FitScale(_ruleSetupPanel.sizeDelta);
        }

        static void SetAnchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            if (rt == null) return;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        static void Click()
        {
            if (AudioDirector.Instance != null) AudioDirector.Instance.PlayUi();
        }

        static bool HasCommandLineArg(string key)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], key, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
