using System.Collections.Generic;
using BombTavern.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 独立包的显示设置。首次启动默认使用显示器原生分辨率、
    /// 全屏无边框和最高画质；后续以本机 PlayerPrefs 为准。
    /// </summary>
    public static class RuntimeDisplaySettings
    {
        const string ModeKey = "BombTavern.DisplayMode";
        const string WidthKey = "BombTavern.ResolutionWidth";
        const string HeightKey = "BombTavern.ResolutionHeight";
        const string QualityKey = "BombTavern.Quality";
        const string LegacyFullscreenKey = "BombTavern.Fullscreen";
        const string VsyncKey = "BombTavern.VSync";
        const string DefaultsVersionKey = "BombTavern.DisplayDefaultsV3";

        static bool _applied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoApply() => ApplySavedOnce();

        public static void ApplySavedOnce()
        {
            if (_applied) return;
            _applied = true;

            // 无画面自动化会在同一台机器同时启动主机和客户端，
            // 两个进程不应竞争写入同一份 PlayerPrefs，而且它们本来也没有显示器。
            if (Application.isBatchMode) return;

            // 仅供自动化视觉回归使用，不写 PlayerPrefs，不影响玩家设置。
            if (TryReadTestResolution(out Vector2Int testResolution))
            {
                Screen.SetResolution(testResolution.x, testResolution.y, FullScreenMode.Windowed);
                return;
            }

            // V3 首次启动（包括从旧测试包升级）重置一次显示默认值。
            // 否则 Unity 会沿用注册表中旧包留下的低分辨率窗口设置。
            if (!PlayerPrefs.HasKey(DefaultsVersionKey))
            {
                var firstNative = Screen.currentResolution;
                // 新包以 2K 为首选输出；显示器比 2K 更高时保留其原生分辨率。
                PlayerPrefs.SetInt(WidthKey, Mathf.Max(2560, firstNative.width));
                PlayerPrefs.SetInt(HeightKey, Mathf.Max(1440, firstNative.height));
                PlayerPrefs.SetInt(ModeKey, (int)FullScreenMode.FullScreenWindow);
                PlayerPrefs.SetInt(LegacyFullscreenKey, 1);
                PlayerPrefs.SetInt(QualityKey, Mathf.Max(0, QualitySettings.names.Length - 1));
                PlayerPrefs.SetInt(VsyncKey, 1);
                PlayerPrefs.SetInt(DefaultsVersionKey, 1);
                PlayerPrefs.Save();
            }

            int quality = PlayerPrefs.GetInt(QualityKey, Mathf.Max(0, QualitySettings.names.Length - 1));
            QualitySettings.SetQualityLevel(Mathf.Clamp(quality, 0, QualitySettings.names.Length - 1), true);
            QualitySettings.vSyncCount = PlayerPrefs.GetInt(VsyncKey, 1) > 0 ? 1 : 0;

            var native = Screen.currentResolution;
            int width = PlayerPrefs.GetInt(WidthKey, native.width > 0 ? native.width : 1920);
            int height = PlayerPrefs.GetInt(HeightKey, native.height > 0 ? native.height : 1080);
            Screen.SetResolution(width, height, SavedMode());
        }

        static bool TryReadTestResolution(out Vector2Int resolution)
        {
            resolution = default;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "-btResolution", System.StringComparison.OrdinalIgnoreCase)) continue;
                string[] parts = args[i + 1].ToLowerInvariant().Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int width) &&
                    int.TryParse(parts[1], out int height) && width >= 800 && height >= 450)
                {
                    resolution = new Vector2Int(width, height);
                    return true;
                }
            }
            return false;
        }

        public static FullScreenMode SavedMode()
        {
            if (PlayerPrefs.HasKey(ModeKey))
            {
                int raw = PlayerPrefs.GetInt(ModeKey, (int)FullScreenMode.FullScreenWindow);
                if (raw == (int)FullScreenMode.Windowed) return FullScreenMode.Windowed;
                if (raw == (int)FullScreenMode.ExclusiveFullScreen) return FullScreenMode.ExclusiveFullScreen;
                return FullScreenMode.FullScreenWindow;
            }

            // 兼容旧版只保存 bool 的设置。
            if (PlayerPrefs.HasKey(LegacyFullscreenKey) && PlayerPrefs.GetInt(LegacyFullscreenKey, 1) == 0)
                return FullScreenMode.Windowed;
            return FullScreenMode.FullScreenWindow;
        }

        public static void SetMode(FullScreenMode mode)
        {
            PlayerPrefs.SetInt(ModeKey, (int)mode);
            PlayerPrefs.SetInt(LegacyFullscreenKey, mode == FullScreenMode.Windowed ? 0 : 1);
            PlayerPrefs.Save();
            Screen.SetResolution(Screen.width, Screen.height, mode);
        }

        public static void SetResolution(Vector2Int resolution)
        {
            int width = Mathf.Max(800, resolution.x);
            int height = Mathf.Max(450, resolution.y);
            PlayerPrefs.SetInt(WidthKey, width);
            PlayerPrefs.SetInt(HeightKey, height);
            PlayerPrefs.Save();
            Screen.SetResolution(width, height, Screen.fullScreenMode);
        }

        public static void SetQuality(int index)
        {
            int clamped = Mathf.Clamp(index, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            QualitySettings.SetQualityLevel(clamped, true);
            PlayerPrefs.SetInt(QualityKey, clamped);
            PlayerPrefs.Save();
        }

        public static void SetVsync(bool enabled)
        {
            QualitySettings.vSyncCount = enabled ? 1 : 0;
            PlayerPrefs.SetInt(VsyncKey, QualitySettings.vSyncCount);
            PlayerPrefs.Save();
        }

        public static List<Vector2Int> ResolutionOptions()
        {
            var result = new List<Vector2Int>();
            var seen = new HashSet<string>();
            var native = Screen.currentResolution;
            // 即使当前测试机只有 1080p，也必须将 2560×1440 写入导出包的可选列表。
            int maxW = Mathf.Max(2560, native.width > 0 ? native.width : 2560);
            int maxH = Mathf.Max(1440, native.height > 0 ? native.height : 1440);
            int[,] common =
            {
                { 1024, 576 }, { 1280, 720 }, { 1366, 768 }, { 1600, 900 },
                { 1920, 1080 }, { 2560, 1440 }, { 3840, 2160 }
            };

            for (int i = 0; i < common.GetLength(0); i++)
                Add(common[i, 0], common[i, 1]);
            Add(Screen.width, Screen.height);
            Add(maxW, maxH);

            result.Sort((a, b) => (a.x * a.y).CompareTo(b.x * b.y));
            return result;

            void Add(int width, int height)
            {
                if (width < 800 || height < 450 || width > maxW || height > maxH) return;
                string key = width + "x" + height;
                if (seen.Add(key)) result.Add(new Vector2Int(width, height));
            }
        }

        public static string ModeName(FullScreenMode mode)
        {
            if (mode == FullScreenMode.Windowed) return "窗口（可拖拽边缘）";
            if (mode == FullScreenMode.ExclusiveFullScreen) return "独占全屏";
            return "全屏无边框";
        }
    }

    /// <summary>Esc 打开的对局内设置面板。联机对局不暂停，只锁定本地操作。</summary>
    [DisallowMultipleComponent]
    public sealed class InGameSettingsUgui : MonoBehaviour
    {
        GameDirector _director;
        RectTransform _panel;
        RectTransform _blocker;
        Button _modeButton;
        Button _resolutionButton;
        Button _qualityButton;
        Toggle _vsyncToggle;
        Slider _masterVolumeSlider;
        Slider _voiceVolumeSlider;
        Button _voiceTestButton;
        Button _voiceModeButton;
        Button _rulesButton;
        TMP_Text _voiceTestStatus;
        TMP_Text _rulesText;
        TMP_Text _summary;
        UiModalAnimator _modalAnimator;
        VoiceChatManager _voice;
        bool _open;
        bool _automationOpened;
        List<Vector2Int> _resolutions;

        public void Configure(GameDirector director, RectTransform root)
        {
            _director = director;
            RuntimeDisplaySettings.ApplySavedOnce();
            Build(root);
        }

        void Build(RectTransform root)
        {
            _blocker = UiKit.Rect(root, "InGameSettingsBlocker", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var shade = _blocker.gameObject.AddComponent<Image>();
            shade.color = new Color(0.01f, 0.008f, 0.014f, 0.82f);
            var blockerButton = _blocker.gameObject.AddComponent<Button>();
            blockerButton.onClick.AddListener(Close);

            var image = UiKit.Panel(root, "InGameSettingsPanel");
            _panel = image.rectTransform;
            _modalAnimator = image.gameObject.AddComponent<UiModalAnimator>();
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.anchoredPosition = Vector2.zero;
            _panel.sizeDelta = new Vector2(900f, 1280f);
            UiKit.VBox(_panel, UiTheme.S2, new RectOffset(36, 36, 28, 28));

            var title = UiKit.Label(_panel, "游 戏 设 置", UiTheme.FontTitle + 4f,
                UiTheme.Accent, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyles.Bold;
            UiKit.Size(title.rectTransform, h: 48f);

            var hint = UiKit.Paragraph(_panel,
                "联机对局不会暂停。窗口模式下可直接拖拽窗口边缘改变尺寸和比例。",
                UiTheme.FontBody, UiTheme.TextMid, TextAnchor.MiddleCenter);
            UiKit.Size(hint.rectTransform, h: 52f);

            UiKit.SectionTitle(_panel, "显示");
            _modeButton = UiKit.Btn(_panel, "", CycleMode);
            UiKit.Size(_modeButton.GetComponent<RectTransform>(), h: 52f);
            _resolutionButton = UiKit.Btn(_panel, "", CycleResolution);
            UiKit.Size(_resolutionButton.GetComponent<RectTransform>(), h: 52f);
            _qualityButton = UiKit.Btn(_panel, "", CycleQuality);
            UiKit.Size(_qualityButton.GetComponent<RectTransform>(), h: 52f);
            _vsyncToggle = UiKit.ToggleRow(_panel, "垂直同步", QualitySettings.vSyncCount > 0,
                value => { RuntimeDisplaySettings.SetVsync(value); RefreshLabels(); });

            UiKit.SectionTitle(_panel, "音频");
            _masterVolumeSlider = UiKit.SliderRow(_panel, "主音量", AudioListener.volume,
                value =>
                {
                    AudioListener.volume = value;
                    PlayerPrefs.SetFloat("BombTavern.Volume", value);
                    PlayerPrefs.Save();
                }, out _);
            if (_voice == null) _voice = FindObjectOfType<VoiceChatManager>();
            float voiceVolume = PlayerPrefs.GetFloat("BombTavern.VoiceVolume", _voice != null ? _voice.outputVolume : 1f);
            _voiceVolumeSlider = UiKit.SliderRow(_panel, "语音音量", voiceVolume,
                value =>
                {
                    if (_voice == null) _voice = FindObjectOfType<VoiceChatManager>();
                    if (_voice != null) _voice.outputVolume = value;
                    PlayerPrefs.SetFloat("BombTavern.VoiceVolume", value);
                    PlayerPrefs.Save();
                }, out _);

            _voiceTestButton = UiKit.Btn(_panel, "测试麦克风", ToggleVoiceTest);
            UiKit.Size(_voiceTestButton.GetComponent<RectTransform>(), h: 52f);
            _voiceTestStatus = UiKit.Paragraph(_panel, "", UiTheme.FontCaption, UiTheme.TextLo,
                TextAnchor.MiddleCenter);
            UiKit.Size(_voiceTestStatus.rectTransform, h: 50f);

            UiKit.SectionTitle(_panel, "游戏与辅助");
            _voiceModeButton = UiKit.Btn(_panel, "", ToggleVoiceMode);
            UiKit.Size(_voiceModeButton.GetComponent<RectTransform>(), h: 48f);

            _rulesButton = UiKit.Btn(_panel, "游戏规则介绍", ToggleRules);
            UiKit.Size(_rulesButton.GetComponent<RectTransform>(), h: 48f);
            _rulesText = UiKit.Paragraph(_panel,
                "每位玩家轮流选择格子；概率颜色是上次公开的信息，会随回合发生隐藏偏移。\n" +
                "限时结束仍未操作时，系统会自动翻开一格。踩中会损失生命，生命归零进入观战。\n" +
                "数字键 1—6 拿出道具，E 键使用；V 键按住发言，也可切换为自动声控。",
                UiTheme.FontCaption, UiTheme.TextMid, TextAnchor.MiddleLeft);
            UiKit.Size(_rulesText.rectTransform, h: 118f);
            _rulesText.gameObject.SetActive(false);

            _summary = UiKit.Paragraph(_panel, "", UiTheme.FontCaption, UiTheme.TextLo,
                TextAnchor.MiddleCenter);
            UiKit.Size(_summary.rectTransform, h: 42f);

            var close = UiKit.Btn(_panel, "保存并继续游戏", Close, accent: true);
            UiKit.Size(close.GetComponent<RectTransform>(), h: 54f);

            var exitMenu = UiKit.Btn(_panel, "退出到主界面", ExitToMenu);
            UiKit.Size(exitMenu.GetComponent<RectTransform>(), h: 48f);
            var exitDesktop = UiKit.Btn(_panel, "退出游戏", () => Application.Quit(), danger: true);
            UiKit.Size(exitDesktop.GetComponent<RectTransform>(), h: 48f);

            _blocker.gameObject.SetActive(false);
            image.gameObject.GetComponent<CanvasGroup>().alpha = 0f;
            image.transform.localScale = Vector3.one * 0.96f;
            _panel.gameObject.SetActive(false);
            TavernSettingsLayout.Arrange(_panel, Close, showMatchActions: true);
            var audioPage = _panel.Find("音频");
            _voiceModeButton.transform.SetParent(audioPage, false);
            _voiceTestStatus.transform.SetParent(audioPage, false);
            _voiceTestStatus.gameObject.SetActive(true);
            var matchPage = _panel.Find("对局");
            var lan = FindObjectOfType<BombTavern.Net.LanNetworkManager>();
            string exitText = lan != null && lan.IsHost ? "结束房间，返回大厅" : "退出对局，返回大厅";
            exitMenu.GetComponentInChildren<TMP_Text>(true).text = exitText;
            exitMenu.transform.SetParent(matchPage, false);
            exitMenu.gameObject.SetActive(true);
            UiKit.Size(exitMenu.GetComponent<RectTransform>(), h: 64f);
            string exitHint = _director.mode == GameMode.SinglePlayer
                ? "离开当前单人对局并返回大厅。"
                : lan != null && lan.IsHost
                    ? "你是房主：离开会关闭当前房间，其他玩家需要重新加入。"
                    : "离开联机对局后，你的角色会由 AI 接管。";
            var note = UiKit.Paragraph(matchPage, exitHint, UiTheme.FontBody, UiTheme.TextMid);
            UiKit.Size(note.rectTransform, h: 72f);
        }

        void Update()
        {
            if (_director == null || _director.IsPregame || _director.Session == null) return;
            if (_director.Session.IsOver) { if (_open) Close(); return; }
            if (!_automationOpened && HasCommandLineArg("-btOpenInGameSettings"))
            {
                _automationOpened = true;
                Open();
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_open) Close();
                else Open();
            }

            if (_open) RefreshVoiceTest();
        }

        void Open()
        {
            _open = true;
            _resolutions = RuntimeDisplaySettings.ResolutionOptions();
            _blocker.gameObject.SetActive(true);
            _modalAnimator.Show();
            _blocker.SetAsLastSibling();
            _panel.SetAsLastSibling();
            SetInputCaptured(true);
            RefreshLabels();
        }

        void Close()
        {
            if (!_open) return;
            _open = false;
            _blocker.gameObject.SetActive(false);
            _modalAnimator.Hide();
            if (_voice != null && _voice.IsMicrophoneTestRunning) _voice.StopMicrophoneTest();
            SetInputCaptured(false);
        }

        void ToggleVoiceTest()
        {
            if (_voice == null) _voice = FindObjectOfType<VoiceChatManager>();
            if (_voice == null) return;
            if (_voice.IsMicrophoneTestRunning) _voice.StopMicrophoneTest();
            else _voice.BeginMicrophoneTest();
            RefreshVoiceTest();
        }

        void ToggleVoiceMode()
        {
            if (_voice == null) _voice = FindObjectOfType<VoiceChatManager>();
            if (_voice == null) return;
            _voice.SetVoiceActivation(!_voice.VoiceActivationEnabled);
            RefreshVoiceTest();
        }

        void ToggleRules()
        {
            if (_rulesText != null) _rulesText.gameObject.SetActive(!_rulesText.gameObject.activeSelf);
        }

        void ExitToMenu()
        {
            if (_voice != null) _voice.StopMicrophoneTest(false);
            SessionContext.ReturnToMainMenu();
        }

        void RefreshVoiceTest()
        {
            if (_voiceTestButton == null || _voiceTestStatus == null) return;
            if (_voice == null) _voice = FindObjectOfType<VoiceChatManager>();
            if (_voice == null)
            {
                _voiceTestButton.interactable = false;
                _voiceTestButton.GetComponentInChildren<TMP_Text>(true).text = "麦克风测试不可用";
                _voiceTestStatus.text = "当前场景没有语音管理器";
                return;
            }

            _voiceTestButton.interactable = _voice.IsMicReady;
            _voiceTestButton.GetComponentInChildren<TMP_Text>(true).text = _voice.IsMicrophoneTestRunning
                ? "停止麦克风测试"
                : "录制 3 秒并回放";
            int meter = Mathf.RoundToInt(_voice.MicrophoneInputLevel * 10f);
            string bars = new string('■', meter) + new string('□', 10 - meter);
            _voiceTestStatus.text = $"设备：{_voice.MicrophoneName}\n{bars}  {_voice.MicrophoneTestStatus}";
            if (_voiceModeButton != null)
                _voiceModeButton.GetComponentInChildren<TMP_Text>(true).text = _voice.VoiceActivationEnabled
                    ? "发言方式    自动检测说话并传输"
                    : "发言方式    按住 V 键发言";
        }

        void CycleMode()
        {
            FullScreenMode next = Screen.fullScreenMode == FullScreenMode.FullScreenWindow
                ? FullScreenMode.ExclusiveFullScreen
                : Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen
                    ? FullScreenMode.Windowed
                    : FullScreenMode.FullScreenWindow;
            RuntimeDisplaySettings.SetMode(next);
            RefreshLabels();
        }

        void CycleResolution()
        {
            if (_resolutions == null || _resolutions.Count == 0)
                _resolutions = RuntimeDisplaySettings.ResolutionOptions();
            if (_resolutions.Count == 0) return;

            int current = 0;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < _resolutions.Count; i++)
            {
                int distance = Mathf.Abs(_resolutions[i].x - Screen.width) + Mathf.Abs(_resolutions[i].y - Screen.height);
                if (distance < bestDistance) { bestDistance = distance; current = i; }
            }
            RuntimeDisplaySettings.SetResolution(_resolutions[(current + 1) % _resolutions.Count]);
            RefreshLabels();
        }

        void CycleQuality()
        {
            int count = QualitySettings.names.Length;
            if (count <= 0) return;
            RuntimeDisplaySettings.SetQuality((QualitySettings.GetQualityLevel() + 1) % count);
            RefreshLabels();
        }

        void ToggleVsync()
        {
            RuntimeDisplaySettings.SetVsync(QualitySettings.vSyncCount == 0);
            RefreshLabels();
        }

        void RefreshLabels()
        {
            if (_modeButton == null) return;
            _modeButton.GetComponentInChildren<TMP_Text>(true).text = "显示模式    " + RuntimeDisplaySettings.ModeName(Screen.fullScreenMode);
            _resolutionButton.GetComponentInChildren<TMP_Text>(true).text = $"分辨率    {Screen.width} × {Screen.height}";
            string[] qualityNames = QualitySettings.names;
            int quality = Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, Mathf.Max(0, qualityNames.Length - 1));
            _qualityButton.GetComponentInChildren<TMP_Text>(true).text = "画面质量    " + (qualityNames.Length > 0 ? qualityNames[quality] : "默认");
            if (_vsyncToggle != null) _vsyncToggle.SetIsOnWithoutNotify(QualitySettings.vSyncCount > 0);
            _summary.text = "当前窗口 " + Screen.width + "×" + Screen.height +
                            "  ·  按 Esc 保存并返回对局";
        }

        void SetInputCaptured(bool captured)
        {
            var fps = FindObjectOfType<FirstPersonController>();
            if (fps != null) fps.SetUiMode(captured);
            else
            {
                Cursor.lockState = captured ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = captured;
            }
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
