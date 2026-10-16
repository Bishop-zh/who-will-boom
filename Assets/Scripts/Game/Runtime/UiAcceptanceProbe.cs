using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BombTavern.Runtime
{
    /// <summary>仅 -btUiAcceptance 启动的后台 UI 验证。通过 Raycast 和指针事件检查真实控件，不注入桌面输入。</summary>
    public sealed class UiAcceptanceProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name; public bool passed; public string detail; public double callbackMs, nextFrameMs; }
        [Serializable] public class Layout { public string page, path, text; public float x, y, width, height; public bool overflow, outside; }
        [Serializable] public class Report { public string scope; public int width, height; public List<Check> checks = new List<Check>(); public List<Layout> layouts = new List<Layout>(); public List<string> errors = new List<string>(); public List<string> limitations = new List<string>(); }
        readonly Report report = new Report();
        readonly Dictionary<string, float> floatPrefs = new Dictionary<string, float>();
        readonly Dictionary<string, int> intPrefs = new Dictionary<string, int>();
        readonly HashSet<string> absentPrefs = new HashSet<string>();
        string directory;
        double started;
        bool finished;
        PreGameMenuUgui menu;
        SettingsPopupView settings;
        GameRulesTutorial tutorial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-btUiAcceptance");
            if (index < 0 || index + 1 >= args.Length) return;
            var probe = new GameObject("UiAcceptanceProbe").AddComponent<UiAcceptanceProbe>();
            probe.directory = Path.GetFullPath(args[index + 1]);
            DontDestroyOnLoad(probe.gameObject);
        }

        void Start()
        {
            Directory.CreateDirectory(directory);
            started = Time.realtimeSinceStartupAsDouble;
            foreach (var key in new[] { "BombTavern.Volume", "BombTavern.MusicVolume", "BombTavern.SfxVolume", "BombTavern.VoiceVolume", "BombTavern.UiScale", "BombTavern.Brightness" })
            { if (PlayerPrefs.HasKey(key)) floatPrefs[key] = PlayerPrefs.GetFloat(key); else absentPrefs.Add(key); }
            foreach (var key in new[] { "BombTavern.DisplayMode", "BombTavern.Fullscreen", "BombTavern.ResolutionWidth", "BombTavern.ResolutionHeight", "BombTavern.Quality", "BombTavern.VSync", "BombTavern.FrameRate", "BombTavern.VoiceActivation", "BombTavern.RulesTutorialSeen.v1" })
            { if (PlayerPrefs.HasKey(key)) intPrefs[key] = PlayerPrefs.GetInt(key); else absentPrefs.Add(key); }
            PlayerPrefs.SetFloat("BombTavern.UiScale", 1f);
            AudioListener.pause = true;
            Application.logMessageReceived += OnLog;
            StartCoroutine(Guard(Run()));
        }

        void OnLog(string message, string trace, LogType type)
        { if (type == LogType.Exception || type == LogType.Error) report.errors.Add(message + "\n" + trace); }
        void Update() { if (!finished && Time.realtimeSinceStartupAsDouble - started > 100) { Record("watchdog", false, "后台验证超过100秒"); Finish(); } }

        IEnumerator Guard(IEnumerator routine)
        {
            while (!finished)
            {
                object current;
                try { if (!routine.MoveNext()) break; current = routine.Current; }
                catch (Exception error) { Record("probe.exception", false, error.ToString()); break; }
                yield return current;
            }
            Finish();
        }

        IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(3);
            menu = FindObjectOfType<PreGameMenuUgui>();
            tutorial = menu.GetComponent<GameRulesTutorial>();
            if (tutorial == null) tutorial = menu.GetComponentsInChildren<GameRulesTutorial>(true).FirstOrDefault();
            settings = menu.GetComponentsInChildren<SettingsPopupView>(true).FirstOrDefault();
            report.scope = "后台图形播放器：真实UGUI射线命中+指针事件+回调状态；无操作系统鼠标输入";
            report.width = Screen.width; report.height = Screen.height;
            PlayerPrefs.SetInt("BombTavern.RulesTutorialSeen.v1", 0);
            tutorial.ShowFirstTime(true);
            Record("first-launch.popup", tutorial.IsOpen, "显式允许后台执行首次打开逻辑");
            yield return new WaitForSecondsRealtime(.3f);
            yield return Click(Find(menu.transform, "RulesTutorialPanel").Find("Close"), "first-launch.close-x", () => !tutorial.IsOpen);
            tutorial.ShowFirstTime(true);
            Record("first-launch.once", !tutorial.IsOpen, "关闭后不重复自动打开");
            yield return Capture("01-home");
            yield return Click(Find(menu.transform, "Home_游戏说明"), "rules.open", () => tutorial.IsOpen);
            yield return Capture("02-rules");
            var scroll = Find(menu.transform, "RulesScrollViewport").GetComponent<ScrollRect>();
            var scrollData = new PointerEventData(EventSystem.current) { position = Center((RectTransform)scroll.transform), scrollDelta = new Vector2(0, -12) };
            var scrollHits = new List<RaycastResult>(); EventSystem.current.RaycastAll(scrollData, scrollHits);
            if (scrollHits.Count > 0) ExecuteEvents.ExecuteHierarchy(scrollHits[0].gameObject, scrollData, ExecuteEvents.scrollHandler);
            yield return null;
            Record("rules.scroll", scroll.verticalNormalizedPosition < .99f, "normalized=" + scroll.verticalNormalizedPosition);
            yield return Capture("02b-rules-details");
            yield return Click(Find(menu.transform, "RulesTutorialPanel").Find("Close"), "rules.close-x", () => !tutorial.IsOpen);
            yield return Click(Find(menu.transform, "Home_游戏说明"), "rules.reopen", () => tutorial.IsOpen);
            yield return ClickAt(new Vector2(10, 10), "rules.close-outside", () => !tutorial.IsOpen);
            var stats = menu.GetComponentsInChildren<StatisticsPopupView>(true).First();
            yield return Click(Find(menu.transform, "Home_战绩统计"), "stats.open", () => stats.IsOpen);
            yield return Capture("02c-stats-empty");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-btStatsAcceptance") >= 0) yield return StatisticsChecks(stats);
            yield return Click(Find(stats.transform, "Close"), "stats.close", () => !stats.IsOpen);
            yield return Click(Find(menu.transform, "Home_开始游戏"), "setup.open", () => Find(menu.transform, "StartGameSetup(Clone)").gameObject.activeInHierarchy);
            Record("skin.default-first", SessionContext.Instance.skinId == "black_sphere", SessionContext.Instance.skinId);
            yield return Capture("03-setup-single");
            foreach (var id in new[] { "red_bundle", "brown_cracker", "gray_shell", "black_sphere" })
            {
                yield return Click(Find(menu.transform, "Skin_" + id), "skin.select." + id, () => SessionContext.Instance.skinId == id);
                var images = menu.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Skin_"));
                int selected = images.Count(b => ((Image)b.targetGraphic).sprite.name == "SetupCardSelected");
                Record("skin.exclusive." + id, selected == 1 && Find(menu.transform, "Skin_" + id).GetComponent<Image>().sprite.name == "SetupCardSelected", "selected=" + selected);
            }
            yield return Click(Find(menu.transform, "Mode_1"), "mode.host", () => Find(menu.transform, "PortRow").gameObject.activeInHierarchy);
            yield return Capture("04-setup-host");
            yield return Click(Find(menu.transform, "Mode_2"), "mode.join", () => Find(menu.transform, "AddressInput").gameObject.activeInHierarchy);
            yield return Capture("05-setup-join");
            var owner = menu.GetComponent<PreGameMenu>();
            var field = Find(menu.transform, "PlayerNameInput").GetComponent<TMP_InputField>();
            yield return Click(field.transform, "name.focus", () => EventSystem.current.currentSelectedGameObject == field.gameObject);
            field.ProcessEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.A, modifiers = EventModifiers.Control });
            foreach (char character in "BackgroundQA") field.ProcessEvent(new Event { type = EventType.KeyDown, character = character });
            Record("name.keyboard-binding", owner.PlayerName == "BackgroundQA", owner.PlayerName);
            Find(menu.transform, "AddressInput").GetComponent<TMP_InputField>().text = "not a valid address!";
            yield return Click(Find(menu.transform, "StartButton"), "join.validation", () => Find(menu.transform, "NetworkHint").GetComponent<TMP_Text>().text.Contains("扫描"));
            yield return Click(Find(menu.transform, "Mode_0"), "mode.single", () => owner.ModeIndex == 0);
            yield return Click(Find(menu.transform, "StartButton"), "match-rules.open", () => Find(menu.transform, "GameSetupPanel").gameObject.activeInHierarchy);
            var rulePointer=new PointerEventData(EventSystem.current){position=Center((RectTransform)Find(menu.transform,"BackHome"))};
            var ruleHits=new List<RaycastResult>(); EventSystem.current.RaycastAll(rulePointer,ruleHits);
            Record("match-rules.blocks-underlying",ruleHits.Count>0 && ruleHits[0].gameObject.name=="GameSetupBlocker","hit="+(ruleHits.Count>0?ruleHits[0].gameObject.name:"none"));
            yield return Capture("06-match-rules");
            yield return Click(Find(menu.transform, "GameSetupPanel").GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<TMP_Text>().text == "返回").transform, "match-rules.back", () => !Find(menu.transform, "GameSetupPanel").gameObject.activeInHierarchy);
            yield return Click(Find(menu.transform, "BackHome"), "setup.back-home", () => Find(menu.transform, "HomeMenu(Clone)").gameObject.activeInHierarchy);
            yield return Click(Find(menu.transform, "Home_开始游戏"), "setup.reopen", () => Find(menu.transform, "StartGameSetup(Clone)").gameObject.activeInHierarchy);
            yield return Click(Find(menu.transform, "Skin_gray_shell"), "skin.choose-before-back", () => SessionContext.Instance.skinId == "gray_shell");
            yield return Click(Find(menu.transform, "BackHome"), "setup.back-again", () => Find(menu.transform, "HomeMenu(Clone)").gameObject.activeInHierarchy);
            yield return Click(Find(menu.transform, "Home_开始游戏"), "setup.reenter", () => Find(menu.transform, "StartGameSetup(Clone)").gameObject.activeInHierarchy);
            Record("skin.retained-on-reentry", SessionContext.Instance.skinId == "gray_shell", SessionContext.Instance.skinId);
            yield return Click(Find(menu.transform, "BackHome"), "setup.back-final", () => Find(menu.transform, "HomeMenu(Clone)").gameObject.activeInHierarchy);
            yield return Click(Find(menu.transform, "Home_设置"), "settings.open", () => settings.IsOpen);
            yield return Capture("07-settings-display");
            var settingsTitle = Find(settings.transform, "TitleArt");
            var settingsFrame = Find(settings.transform, "WindowFrame");
            Record("settings.user-slices", settingsTitle != null && settingsFrame != null &&
                settingsTitle.GetComponent<Image>().sprite != null && settingsFrame.GetComponent<Image>().sprite != null,
                "用户标题、主边框切片独立组装");
            var brightnessHandle = Find(settings.transform, "Brightness").GetComponent<Slider>().handleRect;
            var handleBounds = ScreenRect(brightnessHandle);
            Record("settings.handle-proportions", handleBounds.width > 0 && handleBounds.height / handleBounds.width > .6f && handleBounds.height / handleBounds.width < 1.6f,
                handleBounds.width.ToString("F1") + " × " + handleBounds.height.ToString("F1"));
            foreach (string name in new[] { "WindowMode", "Resolution", "Quality" })
            {
                var control = Find(settings.transform, name);
                yield return Click(control, "dropdown.open." + name, () => control.Find("Options").gameObject.activeInHierarchy);
                yield return Capture("08-dropdown-" + name);
                var options = control.Find("Options").GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Option_")).ToArray();
                Record("dropdown.options." + name, options.Length >= 2, "count=" + options.Length);
                int originalWidth = Screen.width, originalHeight = Screen.height;
                int selectedIndex = name == "Resolution" ? Array.FindIndex(options, b => b.GetComponentInChildren<TMP_Text>().text != Screen.width + " × " + Screen.height) : 0;
                var choice = options[selectedIndex];
                yield return Click(choice.transform, "dropdown.select." + name, () => !control.Find("Options").gameObject.activeInHierarchy);
                if (name == "Quality") Record("quality.binding", QualitySettings.GetQualityLevel() == 0, "actual=" + QualitySettings.GetQualityLevel());
                if (name == "WindowMode") Record("window-mode.binding", RuntimeDisplaySettings.SavedMode() == FullScreenMode.Windowed, RuntimeDisplaySettings.SavedMode().ToString());
                if (name == "Resolution")
                {
                    string requested = choice.GetComponentInChildren<TMP_Text>().text;
                    Record("resolution.preference-binding", requested == PlayerPrefs.GetInt("BombTavern.ResolutionWidth") + " × " + PlayerPrefs.GetInt("BombTavern.ResolutionHeight"), requested);
                    yield return new WaitForSecondsRealtime(.4f);
                    if (requested == Screen.width + " × " + Screen.height) Record("resolution.render-size", true, requested);
                    else report.limitations.Add("后台 batch 图形播放器尺寸固定；分辨率下拉请求 " + requested + "，实际 " + Screen.width + " × " + Screen.height + "。实际系统窗口切换未验证。");
                    RuntimeDisplaySettings.SetResolution(new Vector2Int(originalWidth, originalHeight));
                    yield return new WaitForSecondsRealtime(.4f);
                }
            }
            yield return Click(Find(settings.transform, "Quality"), "dropdown.reopen", () => Find(settings.transform, "Quality").Find("Options").gameObject.activeInHierarchy);
            yield return ClickAt(Center((RectTransform)Find(settings.transform, "DisplayPage").Find("Heading")), "dropdown.dismiss",
                () => !Find(settings.transform, "Quality").Find("Options").gameObject.activeInHierarchy, Find(settings.transform, "DropdownDismiss").gameObject);
            yield return Click(Find(settings.transform, "Fps60"), "fps60", () => Application.targetFrameRate == 60 && QualitySettings.vSyncCount == 0);
            yield return Click(Find(settings.transform, "Fps120"), "fps120", () => Application.targetFrameRate == 120);
            yield return Click(Find(settings.transform, "FpsUnlimited"), "fps.unlimited", () => Application.targetFrameRate == -1);
            bool wasVsync = QualitySettings.vSyncCount > 0;
            yield return Click(Find(settings.transform, "Vsync"), "vsync.toggle", () => (QualitySettings.vSyncCount > 0) != wasVsync);
            yield return DragSlider("Brightness", .7f, () => Mathf.Abs(PlayerPrefs.GetFloat("BombTavern.Brightness") - .7f) < .04f);
            yield return DragSlider("UiScale", 1.2f, () => Mathf.Abs(PlayerPrefs.GetFloat("BombTavern.UiScale") - 1.2f) < .04f);
            yield return Capture("09-settings-scale120");
            yield return DragSlider("UiScale", 1f, () => Mathf.Abs(PlayerPrefs.GetFloat("BombTavern.UiScale") - 1f) < .04f);
            yield return Click(Find(settings.transform, "TabAudio"), "settings.audio-tab", () => Find(settings.transform, "AudioPage").gameObject.activeInHierarchy);
            yield return Capture("10-settings-audio");
            yield return DragSlider("MasterVolume", .4f, () => Mathf.Abs(AudioListener.volume - .4f) < .04f);
            yield return DragSlider("MusicVolume", .4f, () => Mathf.Abs(AudioDirector.Instance.musicVolume - .4f) < .04f);
            yield return DragSlider("SfxVolume", .4f, () => Mathf.Abs(AudioDirector.Instance.sfxVolume - .4f) < .04f);
            yield return DragSlider("VoiceVolume", .4f, () => Mathf.Abs(PlayerPrefs.GetFloat("BombTavern.VoiceVolume") - .4f) < .04f);
            yield return Click(Find(settings.transform, "TabAssist"), "settings.assist-tab", () => Find(settings.transform, "AssistPage").gameObject.activeInHierarchy);
            yield return Capture("11-settings-assist");
            yield return Click(Find(settings.transform, "Rules"), "settings.rules-open", () => tutorial.IsOpen);
            yield return Capture("12-rules-over-settings");
            yield return Click(Find(menu.transform, "RulesTutorialPanel").Find("Close"), "settings.rules-close", () => !tutorial.IsOpen);
            yield return Click(Find(settings.transform, "Confirm"), "settings.confirm-close", () => !settings.IsOpen);
            for (int i = 0; i < 3; i++)
            {
                yield return Click(Find(menu.transform, "Home_设置"), "settings.reopen." + i, () => settings.IsOpen);
                yield return Click(Find(settings.transform, i == 0 ? "Close" : i == 1 ? "LeaveSettings" : "Confirm"), "settings.close." + i, () => !settings.IsOpen);
            }
            yield return Click(Find(menu.transform, "Home_设置"), "settings.outside-open", () => settings.IsOpen);
            yield return ClickAt(new Vector2(10, 10), "settings.outside-close", () => !settings.IsOpen);
            // 单人真实开局后复查共用预制体。设置开启接口单独验证，其余通过射线点击。
            yield return Click(Find(menu.transform, "Home_开始游戏"), "game.setup-open", () => Find(menu.transform, "StartGameSetup(Clone)").gameObject.activeInHierarchy);
            yield return Click(Find(menu.transform, "StartButton"), "game.rules-open", () => Find(menu.transform, "GameSetupPanel").gameObject.activeInHierarchy);
            yield return Click(Find(menu.transform, "GameSetupPanel").GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<TMP_Text>().text == "确认并开始").transform,
                "game.begin", () => !menu.gameObject.activeInHierarchy);
            yield return new WaitForSecondsRealtime(1);
            var inGame = FindObjectOfType<InGameSettingsUgui>();
            settings = FindObjectsOfType<SettingsPopupView>(true).First(v => v != settings);
            inGame.SendMessage("Open");
            yield return new WaitForSecondsRealtime(.3f);
            Record("in-game.settings-api", settings.IsOpen, "通过设置开启接口调用，不是硬件 ESC 输入");
            yield return Capture("13-in-game-settings");
            yield return Click(Find(settings.transform, "TabAssist"), "in-game.assist", () => Find(settings.transform, "AssistPage").gameObject.activeInHierarchy);
            yield return Click(Find(settings.transform, "Rules"), "in-game.rules-open", () => FindObjectsOfType<GameRulesTutorial>().Any(t => t.IsOpen));
            yield return Capture("14-in-game-rules");
            var gameTutorial = FindObjectsOfType<GameRulesTutorial>().First(t => t.IsOpen);
            yield return Click(Find(gameTutorial.transform, "RulesTutorialPanel").Find("Close"), "in-game.rules-close", () => !gameTutorial.IsOpen);
            bool sampledClosing = false;
            yield return Click(Find(settings.transform, "Close"), "in-game.settings-close", () =>
            {
                if (!sampledClosing)
                {
                    sampledClosing = true;
                    var controller = FindObjectOfType<FirstPersonController>();
                    Record("in-game.close-input-isolation", settings.gameObject.activeInHierarchy && Find(settings.transform, "Panel").GetComponent<CanvasGroup>().blocksRaycasts && controller != null && controller.UiMode,
                        "淡出当帧保留遮罩与第一人称输入锁");
                }
                return !settings.IsOpen;
            });
            Record("in-game.close-input-release", !settings.gameObject.activeInHierarchy && !FindObjectOfType<FirstPersonController>().UiMode, "完全隐藏后恢复输入");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-btStatsAcceptance") >= 0)
            {
                var director = FindObjectOfType<GameDirector>(); var session = director.Session;
                session.RestoreAuthoritative(0, 1, 0, true, director.localSeat, Core.GameOverReason.LastPlayerStanding, -1, -1, -1, 0);
                yield return null; yield return null;
                Record("stats.actual-director-settlement", MatchStatisticsStore.Local.Data.total == 1 && MatchStatisticsStore.Local.Data.wins == 1, "后台隔离文件 total=" + MatchStatisticsStore.Local.Data.total);
                yield return Capture("17-game-over-prefab");
                var results = FindObjectsOfType<UiPrefabBindings>(true).First(v => v.name == "GameOver(Clone)");
                var resultCard = Find(results.transform, "ResultCard") as RectTransform;
                var resultCaption = results.GetComponentsInChildren<TMP_Text>().First(t => t.text.Contains("值得分享"));
                var captionBounds = ScreenRect(resultCaption.rectTransform);
                Record("results.caption-clear-of-frame", !resultCaption.isTextOverflowing &&
                    captionBounds.yMin > ScreenRect(resultCard).yMin + 48 * Mathf.Abs(resultCard.lossyScale.y),
                    "结算说明与下边框保留安全间距");
                yield return Click(UiPrefabBindings.ButtonWithText(results.transform, "详细战绩").transform, "results.details", () => results.GetComponentsInChildren<TMP_Text>().Any(t => t.text.Contains("道具")));
                yield return Click(UiPrefabBindings.ButtonWithText(results.transform, "再来一局").transform, "results.restart", () => !director.Session.IsOver);
                yield return null; yield return null;
                director.Session.RestoreAuthoritative(0, 1, 0, true, 1, Core.GameOverReason.LastPlayerStanding, -1, -1, -1, 0);
                yield return null; yield return null;
                Record("stats.restart-loss", MatchStatisticsStore.Local.Data.total == 2 && MatchStatisticsStore.Local.Data.wins == 1 && MatchStatisticsStore.Local.Data.streak == 0, "total=" + MatchStatisticsStore.Local.Data.total);
                yield return Click(UiPrefabBindings.ButtonWithText(results.transform, "返回大厅").transform, "results.back-to-home", () => director == null);
                yield return new WaitForSecondsRealtime(3);
                menu = FindObjectOfType<PreGameMenuUgui>();
                var freshStats = menu.GetComponentsInChildren<StatisticsPopupView>(true).First();
                yield return Click(Find(menu.transform,"Home_战绩统计"), "stats.after-scene-reload", () => freshStats.IsOpen && Find(freshStats.transform,"TotalValue").GetComponent<TMP_Text>().text == "2");
                yield return Capture("19-stats-real-settlements");
                yield return ClickAt(new Vector2(10,10), "stats.close-outside", () => !freshStats.IsOpen);
                var lobbyHost = new GameObject("LobbyBindingProbe"); var lobbyView = lobbyHost.AddComponent<LobbyUI>(); lobbyView.enabled = false;
                lobbyView.SendMessage("Build");
                lobbyHost.GetComponentInChildren<Canvas>().sortingOrder = 1000;
                Canvas.ForceUpdateCanvases();
                yield return null;
                var lobbyBindings = lobbyHost.GetComponentsInChildren<UiPrefabBindings>(true).First(v => v.name == "LobbyPanel(Clone)");
                Record("lobby.prefab-bindings", lobbyBindings.entries.Length >= 10 && lobbyHost.GetComponentsInChildren<TMP_InputField>(true).Length == 2, "refs=" + lobbyBindings.entries.Length);
                // 使用未启用联机的本地管理器，验证回调到昵称状态，不开启监听器。
                var localLan = FindObjectOfType<Net.LanNetworkManager>(); lobbyView.lan=localLan;
                string previousName=localLan.localPlayerName;
                lobbyBindings.GetComponentsInChildren<TMP_InputField>(true).First(f=>f.characterLimit==16).text="PrefabQA";
                yield return Click(UiPrefabBindings.ButtonWithText(lobbyBindings.transform, "确认昵称").transform, "lobby.claim-button", () => localLan.localPlayerName=="PrefabQA");
                localLan.localPlayerName=previousName; if(SessionContext.Instance!=null) SessionContext.Instance.playerName=previousName;
                yield return Capture("18-lobby-prefab"); Destroy(lobbyHost);
            }
            report.limitations.Add("未操作前台：物理鼠标/键盘、全屏切换、恢复默认（含全屏）、真实麦克风、公网/跨机连接未执行。计时仅事件处理到状态变化及下一帧，不代表设备到显示延迟。");
            Record("runtime.errors", report.errors.Count == 0, "errors=" + report.errors.Count);
        }

        IEnumerator StatisticsChecks(StatisticsPopupView stats)
        {
            Transform Node(string name) => Find(stats.transform, name);
            Record("stats.empty-state", Node("Empty").gameObject.activeInHierarchy && Node("RateValue").GetComponent<TMP_Text>().text == "—", "空记录不显示虚构胜率");
            string fixture = Path.Combine(directory, "stats-fixture.json");
            var data = new MatchStatisticsData { total = 128, wins = 76, single = 42, multiplayer = 86, bestStreak = 8, streak = 2 };
            for (int i = 0; i < 10; i++) data.recent.Add(new MatchRecord { id = "fixture" + i, playerName = "<size=200>玩家</size>测试昵称", won = i % 3 != 2, mode = (GameMode)(i % 3), endedUtcTicks = DateTime.UtcNow.AddMinutes(-i * 18).Ticks, rounds = 12, reveals = 19, hits = 2, itemsUsed = 4 });
            File.WriteAllText(fixture, JsonUtility.ToJson(data)); stats.Store = new MatchStatisticsStore(fixture);
            yield return Click(Node("Refresh"), "stats.refresh-data", () => Node("TotalValue").GetComponent<TMP_Text>().text == "128");
            Record("stats.rate-and-modes", Mathf.Abs(Node("WinRing").GetComponent<StatisticsRingGraphic>().Fraction - 76f / 128) < .0001f && Mathf.Abs(Node("SingleFill").GetComponent<Image>().fillAmount - 42f / 128) < .0001f && Node("StreakValue").GetComponent<TMP_Text>().text == "8", "环形图、比例条与真实存档一致");
            Record("stats.recent-limit", stats.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("Recent_")) == 10, "近期显示10局");
            yield return Capture("15-stats-populated");
            yield return Click(Node("Recent_0"), "stats.record-details", () => Node("Details").gameObject.activeInHierarchy && Node("DetailText").GetComponent<TMP_Text>().text.Contains("翻开格子：19"));
            Record("stats.nickname-literal", !Node("DetailText").GetComponent<TMP_Text>().richText && Node("DetailText").GetComponent<TMP_Text>().text.Contains("<size=200>"), "昵称不解析TMP格式标签");
            yield return Capture("16-stats-details");
            yield return Click(Node("DetailsClose"), "stats.details-close", () => !Node("Details").gameObject.activeInHierarchy);
            yield return ClickAt(Center((RectTransform)Node("Frame")), "stats.inside-does-not-close", () => stats.IsOpen);
            var ring = Node("WinRing").GetComponent<StatisticsRingGraphic>();
            foreach (float fraction in new[] { 0f, 1f }) { ring.Fraction = fraction; yield return null; Record("stats.ring-endpoint." + fraction, Mathf.Approximately(ring.Fraction, fraction), "端点设置"); yield return Capture("15b-stats-ring-"+fraction); }
            stats.Refresh();
        }

        IEnumerator Click(Transform target, string name, Func<bool> check)
        {
            if (target == null) { Record(name, false, "缺少目标"); yield break; }
            yield return ClickAt(Center((RectTransform)target), name, check, target.gameObject);
        }
        IEnumerator ClickAt(Vector2 point, string name, Func<bool> check, GameObject intended = null)
        {
            Canvas.ForceUpdateCanvases();
            var data = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left, pointerId = -99 };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            var hit = hits.Count > 0 ? hits[0].gameObject : null;
            var handler = hit != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit) : null;
            if (hit == null || (intended != null && handler != intended))
            { Record(name, false, "射线命中=" + (hit != null ? PathOf(hit.transform) : "none") + "；预期=" + (intended != null ? intended.name : "any")); yield break; }
            double start = Time.realtimeSinceStartupAsDouble;
            string hitName = hit.name;
            data.pointerCurrentRaycast = hits[0]; data.pressPosition = point; data.pointerPressRaycast = hits[0];
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerClickHandler);
            while (!check() && Time.realtimeSinceStartupAsDouble - start < .4) yield return null;
            Record(name, check(), "hit=" + hitName, (Time.realtimeSinceStartupAsDouble - start) * 1000);
            var measured = report.checks[report.checks.Count - 1];
            yield return null;
            Canvas.ForceUpdateCanvases();
            measured.nextFrameMs = (Time.realtimeSinceStartupAsDouble - start) * 1000;
            yield return new WaitForSecondsRealtime(.22f);
        }

        IEnumerator DragSlider(string name, float value, Func<bool> check)
        {
            var slider = Find(settings.transform, name).GetComponent<Slider>();
            var rect = (RectTransform)slider.transform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, Mathf.InverseLerp(slider.minValue, slider.maxValue, value)), 0, 0)));
            var data = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left, pointerId = -99 };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            bool reachable = hits.Count > 0 && (hits[0].gameObject.transform == slider.transform || hits[0].gameObject.transform.IsChildOf(slider.transform));
            if (!reachable) { Record("slider." + name, false, "滑条没有可命中的 Graphic；hit=" + (hits.Count > 0 ? hits[0].gameObject.name : "none")); yield break; }
            double start = Time.realtimeSinceStartupAsDouble;
            var hit = hits[0].gameObject;
            data.pointerCurrentRaycast = hits[0]; data.pressPosition = point;
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.beginDragHandler);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.dragHandler);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.endDragHandler);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerUpHandler);
            Record("slider." + name, check(), "actual=" + slider.value, (Time.realtimeSinceStartupAsDouble - start) * 1000);
            yield return new WaitForSecondsRealtime(.25f);
        }

        IEnumerator Capture(string page)
        {
            yield return new WaitForSecondsRealtime(.3f);
            yield return new WaitForEndOfFrame();
            // Windows batch 播放器的交换链可能是黑帧。保留同一场景与UGUI几何，显式渲染到离屏目标。
            var target = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            var cameraGo = new GameObject("UiAcceptanceCaptureCamera");
            var uiCamera = cameraGo.AddComponent<Camera>();
            uiCamera.cullingMask = 1 << 31;
            uiCamera.clearFlags = CameraClearFlags.Depth;
            uiCamera.nearClipPlane = .01f; uiCamera.farClipPlane = 100f;
            uiCamera.transform.position = new Vector3(0, 0, -10);
            uiCamera.targetTexture = target;
            var canvases = FindObjectsOfType<Canvas>().Where(c => c.isActiveAndEnabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var geometry = FindObjectsOfType<Button>().Where(b => b.GetComponentInParent<Canvas>() != null &&
                canvases.Contains(b.GetComponentInParent<Canvas>().rootCanvas)).ToDictionary(b => (RectTransform)b.transform, b => ScreenRect((RectTransform)b.transform));
            var layers = new Dictionary<GameObject, int>();
            var previousActive = RenderTexture.active;
            var background = Camera.main;
            var previousTarget = background != null ? background.targetTexture : null;
            Texture2D texture = null;
            try
            {
                if (background != null) { background.targetTexture = target; background.Render(); }
                foreach (var canvas in canvases)
                {
                    foreach (var child in canvas.GetComponentsInChildren<Transform>(true))
                    { layers[child.gameObject] = child.gameObject.layer; child.gameObject.layer = 31; }
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 1;
                }
                Canvas.ForceUpdateCanvases();
                float maximumDelta = 0;
                string maximumPath = "";
                foreach (var pair in geometry)
                {
                    var corners = new Vector3[4]; pair.Key.GetWorldCorners(corners);
                    var a = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
                    var b = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);
                    float delta = Mathf.Max(Vector2.Distance(a, pair.Value.min), Vector2.Distance(b, pair.Value.max));
                    if (delta > maximumDelta) { maximumDelta = delta; maximumPath = PathOf(pair.Key); }
                }
                Record("capture.geometry." + page, maximumDelta < 1f, "离屏与实际覆盖层几何最大偏差=" + maximumDelta.ToString("F3") + "px " + maximumPath);
                uiCamera.Render();
                RenderTexture.active = target;
                texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); texture.Apply();
                File.WriteAllBytes(System.IO.Path.Combine(directory, page + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                foreach (var pair in layers) pair.Key.layer = pair.Value;
                if (background != null) background.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Destroy(cameraGo); Destroy(target); if (texture != null) Destroy(texture);
                Canvas.ForceUpdateCanvases();
            }
            foreach (var label in FindObjectsOfType<TMP_Text>().Where(t => t.GetComponentInParent<Canvas>() != null && t.GetComponentInParent<Canvas>().rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay))
            {
                var rect = ScreenRect(label.rectTransform);
                report.layouts.Add(new Layout { page = page, path = PathOf(label.transform), text = label.text,
                    x = rect.x, y = rect.y, width = rect.width, height = rect.height,
                    overflow = label.isTextOverflowing, outside = rect.xMin < -2 || rect.yMin < -2 || rect.xMax > Screen.width + 2 || rect.yMax > Screen.height + 2 });
            }
            Debug.Log("[UI验收] 截屏 " + page);
        }
        static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        static Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        static Rect ScreenRect(RectTransform rect) { var corners = new Vector3[4]; rect.GetWorldCorners(corners); var a = RectTransformUtility.WorldToScreenPoint(null, corners[0]); var b = RectTransformUtility.WorldToScreenPoint(null, corners[2]); return Rect.MinMaxRect(a.x, a.y, b.x, b.y); }
        static string PathOf(Transform node) => node.parent == null ? node.name : PathOf(node.parent) + "/" + node.name;
        void Record(string name, bool pass, string detail, double ms = 0) { report.checks.Add(new Check { name = name, passed = pass, detail = detail, callbackMs = ms }); Debug.Log("[UI验收] " + (pass ? "PASS " : "FAIL ") + name + " " + detail + " " + ms.ToString("F2") + "ms"); }
        void Finish()
        {
            if (finished) return;
            finished = true;
            Application.logMessageReceived -= OnLog;
            foreach (var pair in floatPrefs) PlayerPrefs.SetFloat(pair.Key, pair.Value);
            foreach (var pair in intPrefs) PlayerPrefs.SetInt(pair.Key, pair.Value);
            foreach (var key in absentPrefs) PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            File.WriteAllText(System.IO.Path.Combine(directory, "ui-tests.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[UI验收] 完成 " + report.checks.Count(c => c.passed) + "/" + report.checks.Count);
            Application.Quit(report.checks.All(c => c.passed) ? 0 : 1);
        }
    }
}
