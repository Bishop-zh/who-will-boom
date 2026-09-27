using BombTavern.Config;
using BombTavern.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 对局 HUD（UGUI 版）。
    ///
    /// 相对 IMGUI 版的收益：
    ///   1) **适配** —— CanvasScaler 按 1920×1080 参考分辨率等比缩放，
    ///      四角面板在不同宽高比下不会再错位；这是上 PC + 手机多端的前提。
    ///   2) **性能** —— IMGUI 每帧重建整个布局；UGUI 只在数据脏了时重建。
    ///      这里的做法是「只在值变化时写 text」。
    ///   3) 中文排版质量（TMP 的 SDF）。
    ///
    /// 布局原则和 IMGUI 版一致：贴边分区，不做浮动堆叠。
    ///   顶部中央 轮次条 + 操作权提示
    ///   右上     玩家卡 ×4
    ///   左上     事件流
    ///   底部中央 道具栏
    ///   中央     准星 + 指向格信息
    /// </summary>
    [DisallowMultipleComponent]
    public class GameHudUgui : MonoBehaviour
    {
        [Header("绑定")]
        [Tooltip("对局驱动。留空则自动查找。")]
        public GameDirector director;

        [Tooltip("平板交互器，用于显示指向格子的信息。留空则自动查找。")]
        public TabletInteractor interactor;

        [Tooltip("语音管理器，用于在玩家卡上标记「说话中」。留空则自动查找。")]
        public VoiceChatManager voice;

        const int FeedLines = 5;
        const int HotbarSlots = 6;

        Canvas _canvas;

        // ---- 顶部
        RectTransform _topGroup;
        TMP_Text _turnLabel;
        TMP_Text _driftBadge;
        Image _turnProgressFill;

        // ---- 玩家卡
        sealed class PlayerCard
        {
            public Image bg, chip;
            public TMP_Text name, stat;
            public CanvasGroup microphone;
            public Image microphoneBody, microphoneStem, microphoneBase;
        }
        readonly PlayerCard[] _cards = new PlayerCard[GameSession.MaxSeats];

        // ---- 事件流
        readonly TMP_Text[] _feed = new TMP_Text[FeedLines];

        // ---- 道具栏
        sealed class HotSlot
        {
            public Image bg;
            public TMP_Text key, name, tag;
        }
        readonly HotSlot[] _slots = new HotSlot[HotbarSlots];

        // ---- 横幅
        TMP_Text _banner;
        float _bannerStart, _bannerEnd;
        Color _bannerColor = Color.white;

        // ---- 准星信息
        RectTransform _hoverCard;
        TMP_Text _hoverTitle, _hoverSub;

        // ---- 已拿出的道具说明（右侧）
        RectTransform _selectedItemCard;
        TMP_Text _selectedItemName, _selectedItemDescription, _selectedItemPrompt;
        int _lastSelectedItemSlot = int.MinValue;

        // ---- 轮到你：四边呼吸框
        readonly Image[] _pulse = new Image[4];

        // ---- 状态缓存（只在变化时写控件，避免每帧重建文本）
        int _lastTurnSeat = int.MinValue;
        int _lastFeedCount = -1;
        bool _built;

        void Awake()
        {
            if (director == null) director = FindObjectOfType<GameDirector>();
            if (interactor == null) interactor = FindObjectOfType<TabletInteractor>();
            if (voice == null) voice = FindObjectOfType<VoiceChatManager>();
        }

        void OnDestroy()
        {
            if (director != null)
            {
                director.Event -= OnGameEvent;
                director.SessionReady -= OnSessionReady;
            }
        }

        void Start()
        {
            if (director == null)
            {
                Debug.LogError("[HUD] 找不到 GameDirector，界面不会显示。");
                enabled = false;
                return;
            }

            Build();

            director.Event += OnGameEvent;
            director.SessionReady += OnSessionReady;
            // 说话状态由 RefreshCards 每帧轮询，不订阅事件：
            // VoiceChatManager 没有对外广播这个变化，而轮询一个 bool 的成本可以忽略。
        }

        void OnSessionReady(GameSession s) => ResetTransient();

        // ============================================================ 构建
        void Build()
        {
            _canvas = UiKit.CreateCanvas("HudCanvas", 100);
            _canvas.transform.SetParent(transform, false);

            // 所有边缘 HUD 都相对设备安全区定位。CanvasScaler 负责分辨率缩放，
            // SafeAreaFitter 负责刘海/圆角/窗口尺寸，两层职责不要混在一起。
            var root = UiKit.CreateSafeAreaRoot(_canvas);

            BuildTopBar(root);
            BuildPlayerCards(root);
            BuildFeed(root);
            BuildHotbar(root);
            BuildHoverCard(root);
            BuildSelectedItemCard(root);
            BuildBanner(root);
            BuildPulse(root);
            BuildGameOver(root);
            gameObject.AddComponent<InGameSettingsUgui>().Configure(director, root);

            _built = true;
        }

        /// <summary>顶部中央只保留当前回合提示，避免规则说明长期遮挡画面。</summary>
        void BuildTopBar(RectTransform root)
        {
            var panel = UiKit.Panel(root, "TurnBanner", card: true);
            _topGroup = panel.rectTransform;
            _topGroup.anchorMin = _topGroup.anchorMax = new Vector2(0.5f, 1f);
            _topGroup.pivot = new Vector2(0.5f, 1f);
            _topGroup.anchoredPosition = new Vector2(0f, -UiTheme.S4);
            _topGroup.sizeDelta = new Vector2(UiKit.IsMobile ? 720f : 820f, 124f);

            _turnLabel = UiKit.Label(_topGroup, "等待回合", UiTheme.FontTitle + 4f,
                UiTheme.TextHi, TextAnchor.MiddleCenter);
            _turnLabel.fontStyle = FontStyles.Bold;
            _turnLabel.rectTransform.anchorMin = new Vector2(0f, 0.42f);
            _turnLabel.rectTransform.anchorMax = Vector2.one;
            _turnLabel.rectTransform.offsetMin = new Vector2(24f, 0f);
            _turnLabel.rectTransform.offsetMax = new Vector2(-24f, -8f);
            var outline = _turnLabel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            _driftBadge = UiKit.Label(_topGroup, "概率扰动 ±0%", UiTheme.FontCaption,
                UiTheme.TextMid, TextAnchor.MiddleCenter);
            _driftBadge.rectTransform.anchorMin = new Vector2(0.25f, 0.14f);
            _driftBadge.rectTransform.anchorMax = new Vector2(0.75f, 0.42f);
            _driftBadge.rectTransform.offsetMin = Vector2.zero;
            _driftBadge.rectTransform.offsetMax = Vector2.zero;

            var progressBg = UiKit.Rect(_topGroup, "TurnProgress", new Vector2(0f, 0f),
                new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f),
                new Vector2(-40f, 6f));
            progressBg.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.42f);
            var fill = UiKit.Rect(progressBg, "Fill", Vector2.zero, Vector2.one,
                new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            _turnProgressFill = fill.gameObject.AddComponent<Image>();
            _turnProgressFill.color = UiTheme.AccentHi;
            _turnProgressFill.type = Image.Type.Filled;
            _turnProgressFill.fillMethod = Image.FillMethod.Horizontal;
            _turnProgressFill.fillOrigin = 0;
        }

        /// <summary>右上：玩家卡。竖向排列，高度由内容撑开。</summary>
        void BuildPlayerCards(RectTransform root)
        {
            var box = UiKit.Rect(root, "PlayerCards", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-UiTheme.S4, -UiTheme.S4), new Vector2(430f, 0f));

            UiKit.VBox(box, (int)UiTheme.S1, UiTheme.PanelPadding, fitHeight: true);

            var title = UiKit.Label(box, "玩家", UiTheme.FontHead - 2, UiTheme.Accent);
            UiKit.Size(title.rectTransform, h: 26f);

            for (int i = 0; i < _cards.Length; i++)
            {
                var card = new PlayerCard();

                var bg = UiKit.Panel(box, $"Card_{i}", card: true);
                UiKit.Size(bg.rectTransform, h: UiTheme.PlayerCardHeight);
                card.bg = bg;

                var row = UiKit.Rect(bg.transform, "Row", Vector2.zero, Vector2.one,
                    new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                UiKit.HBox(row, (int)UiTheme.S2, new RectOffset(0, 0, 0, 0));

                // 标识色条
                var chip = UiKit.Rect(row, "Chip", Vector2.zero, Vector2.one,
                    new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                card.chip = chip.gameObject.AddComponent<Image>();
                card.chip.raycastTarget = false;
                UiKit.Size(chip, w: 8f, h: 34f);

                // 说话指示放在名字左侧。固定占位、只切透明度，避免开始/停止说话时文字跳动。
                var mic = UiKit.Rect(row, "Microphone", Vector2.zero, Vector2.one,
                    new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                UiKit.Size(mic, w: 18f, h: 34f);
                card.microphone = mic.gameObject.AddComponent<CanvasGroup>();
                card.microphone.alpha = 0f;

                var micBody = UiKit.Rect(mic, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(8f, 14f));
                card.microphoneBody = micBody.gameObject.AddComponent<Image>();
                var micStem = UiKit.Rect(mic, "Stem", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(2f, 7f));
                card.microphoneStem = micStem.gameObject.AddComponent<Image>();
                var micBase = UiKit.Rect(mic, "Base", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(11f, 2f));
                card.microphoneBase = micBase.gameObject.AddComponent<Image>();

                // 名字 + 状态两行
                var col = UiKit.Rect(row, "Texts", Vector2.zero, Vector2.one,
                    new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                UiKit.VBox(col, 0f, new RectOffset(0, 0, 0, 0));

                card.name = UiKit.Label(col, "—", UiTheme.FontBody, UiTheme.TextHi);
                UiKit.Size(card.name.rectTransform, h: 22f);

                card.stat = UiKit.Label(col, "", UiTheme.FontCaption, UiTheme.TextLo);
                UiKit.Size(card.stat.rectTransform, h: 18f);

                _cards[i] = card;
            }
        }

        /// <summary>左上：事件流。固定行数，新事件从下往上滚。</summary>
        void BuildFeed(RectTransform root)
        {
            var panel = UiKit.Panel(root, "Feed", card: true);
            var box = panel.rectTransform;
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 1f);
            box.anchoredPosition = new Vector2(UiTheme.S4, -(UiTheme.S4 + 104f));
            box.sizeDelta = new Vector2(470f, 0f);
            panel.color = new Color(0.035f, 0.028f, 0.045f, 0.94f);
            var border = panel.gameObject.AddComponent<Outline>();
            border.effectColor = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.72f);
            border.effectDistance = new Vector2(2f, -2f);

            UiKit.VBox(box, 2f, UiTheme.PanelPadding, fitHeight: true);

            var title = UiKit.Label(box, "事件", UiTheme.FontHead - 2, UiTheme.Accent);
            UiKit.Size(title.rectTransform, h: 24f);

            for (int i = 0; i < FeedLines; i++)
            {
                _feed[i] = UiKit.Label(box, "", UiTheme.FontCaption, UiTheme.TextLo);
                _feed[i].enableWordWrapping = true;
                _feed[i].overflowMode = TextOverflowModes.Truncate;
                UiKit.Size(_feed[i].rectTransform, h: 32f);
            }
        }

        /// <summary>底部中央：道具快捷栏。</summary>
        void BuildHotbar(RectTransform root)
        {
            var box = UiKit.Rect(root, "Hotbar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, UiTheme.S4), new Vector2(1080f, 0f));

            UiKit.VBox(box, (int)UiTheme.S1, UiTheme.PanelPadding, fitHeight: true);

            var caption = UiKit.Label(box, "道具", UiTheme.FontCaption, UiTheme.TextLo);
            UiKit.Size(caption.rectTransform, h: 18f);
            _hotbarCaption = caption;

            var row = UiKit.Rect(box, "Slots", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            UiKit.HBox(row, (int)UiTheme.S1, new RectOffset(0, 0, 0, 0));

            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = new HotSlot();

                var bg = UiKit.Panel(row, $"Slot_{i}", card: true);
                UiKit.Size(bg.rectTransform, w: 164f, h: 94f);
                slot.bg = bg;

                // 道具格做成按钮：桌面端点它可以代替数字键，触摸端它就是唯一的入口
                // （手机没有键盘，没有这个按钮就没法用道具）。
                var btn = bg.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                int captured = i;                      // 闭包捕获：不能直接用循环变量 i
                btn.onClick.AddListener(() => GameInput.InjectItemSlot(captured));

                var cb = btn.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.25f, 1.25f, 1.3f);
                cb.pressedColor = new Color(0.8f, 0.8f, 0.85f);
                cb.disabledColor = new Color(1f, 1f, 1f, 0.4f);
                cb.fadeDuration = 0.08f;
                btn.colors = cb;

                var col = UiKit.Rect(bg.transform, "Col", Vector2.zero, Vector2.one,
                    new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                UiKit.VBox(col, 0f, new RectOffset(6, 6, 4, 4));

                var head = UiKit.Rect(col, "Head", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.zero);
                UiKit.HBox(head, 2f, new RectOffset(0, 0, 0, 0));

                slot.key = UiKit.Label(head, $"[{i + 1}]", UiTheme.FontCaption, UiTheme.TextLo);
                UiKit.Size(slot.key.rectTransform, w: 24f, h: 16f);

                slot.tag = UiKit.Label(head, "", UiTheme.FontCaption, UiTheme.TextLo,
                    TextAnchor.MiddleRight);
                UiKit.Size(slot.tag.rectTransform, w: 40f, h: 16f);

                slot.name = UiKit.Label(col, "", UiTheme.FontCaption, UiTheme.TextHi);
                UiKit.Size(slot.name.rectTransform, h: 18f);

                _slots[i] = slot;
            }
        }

        TMP_Text _hotbarCaption;

        /// <summary>中央：准星 + 指向格信息卡（贴在准星下方）。</summary>
        void BuildHoverCard(RectTransform root)
        {
            var panel = UiKit.Panel(root, "HoverCard", card: true);
            var box = panel.rectTransform;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.anchoredPosition = new Vector2(0f, -92f);
            box.sizeDelta = new Vector2(340f, 0f);
            panel.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            UiKit.VBox(box, 5f, new RectOffset(18, 18, 12, 12), fitHeight: true);

            _hoverTitle = UiKit.Label(box, "", UiTheme.FontHead, Color.white,
                TextAnchor.MiddleCenter);
            _hoverTitle.fontStyle = FontStyles.Bold;
            UiKit.Size(_hoverTitle.rectTransform, h: 30f);
            _hoverSub = UiKit.Label(box, "", UiTheme.FontBody, UiTheme.TextMid,
                TextAnchor.MiddleCenter);
            UiKit.Size(_hoverSub.rectTransform, h: 24f);

            _hoverCard = box;
            _hoverCard.gameObject.SetActive(false);
        }

        void BuildSelectedItemCard(RectTransform root)
        {
            var panel = UiKit.Panel(root, "SelectedItem", card: true);
            var box = panel.rectTransform;
            // 右边缘放在安全区宽度的 80% 处，即比原位置向左移约一个屏幕宽度的 1/5。
            box.anchorMin = box.anchorMax = new Vector2(0.8f, 0.5f);
            box.pivot = new Vector2(1f, 0.5f);
            box.anchoredPosition = new Vector2(-UiTheme.S4, -70f);
            box.sizeDelta = new Vector2(330f, 0f);
            UiKit.VBox(box, 8f, new RectOffset(18, 18, 16, 16), fitHeight: true);

            _selectedItemName = UiKit.Label(box, "", UiTheme.FontHead, UiTheme.Accent,
                TextAnchor.MiddleLeft);
            _selectedItemName.fontStyle = FontStyles.Bold;
            UiKit.Size(_selectedItemName.rectTransform, h: 30f);

            _selectedItemDescription = UiKit.Paragraph(box, "", UiTheme.FontBody,
                UiTheme.TextHi, TextAnchor.UpperLeft);
            UiKit.Size(_selectedItemDescription.rectTransform, h: 58f);

            _selectedItemPrompt = UiKit.Label(box, "", UiTheme.FontBody, UiTheme.Success,
                TextAnchor.MiddleCenter);
            _selectedItemPrompt.fontStyle = FontStyles.Bold;
            UiKit.Size(_selectedItemPrompt.rectTransform, h: 26f);

            var use = UiKit.Btn(box, "使 用", () =>
            {
                if (interactor != null) interactor.ConfirmSelectedItem();
            }, accent: true);
            UiKit.Size(use.GetComponent<RectTransform>(), h: 38f);

            _selectedItemCard = box;
            _selectedItemCard.gameObject.SetActive(false);
        }

        /// <summary>中央上方：状态机横幅（开局 / 淘汰 / 回合推进 / 轮到你）。</summary>
        void BuildBanner(RectTransform root)
        {
            var box = UiKit.Rect(root, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 132f), new Vector2(900f, 60f));

            _banner = UiKit.Label(box, "", UiTheme.FontTitle + 6, UiTheme.Accent,
                TextAnchor.MiddleCenter);
            _banner.fontStyle = FontStyles.Bold;
            box.gameObject.SetActive(false);
        }

        /// <summary>轮到自己时屏幕四边的呼吸框。做四条细长 Image，而不是一个描边。</summary>
        void BuildPulse(RectTransform root)
        {
            for (int i = 0; i < _pulse.Length; i++)
            {
                var rt = UiKit.Rect(root, $"Pulse_{i}", Vector2.zero, Vector2.zero,
                    new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

                var img = rt.gameObject.AddComponent<Image>();
                img.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0f);
                img.raycastTarget = false;
                _pulse[i] = img;

                const float t = 7f;
                switch (i)
                {
                    case 0: // 上
                        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
                        rt.pivot = new Vector2(0.5f, 1f); rt.sizeDelta = new Vector2(0f, t);
                        break;
                    case 1: // 下
                        rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
                        rt.pivot = new Vector2(0.5f, 0f); rt.sizeDelta = new Vector2(0f, t);
                        break;
                    case 2: // 左
                        rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f);
                        rt.pivot = new Vector2(0f, 0.5f); rt.sizeDelta = new Vector2(t, 0f);
                        break;
                    default: // 右
                        rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f);
                        rt.pivot = new Vector2(1f, 0.5f); rt.sizeDelta = new Vector2(t, 0f);
                        break;
                }
            }
        }

        // ============================================================ 每帧刷新
        void Update()
        {
            if (!_built) return;

            // 菜单与大厅使用真实场景作为背景，但此时不应叠加对局 HUD。
            bool showHud = director != null && !director.IsPregame;
            if (_canvas.enabled != showHud) _canvas.enabled = showHud;
            if (!showHud) return;

            var s = director.Session;
            if (s == null) return;

            SyncSpectatorMode(s);
            RefreshTopBar(s);
            RefreshCards(s);
            RefreshHotbar(s);
            RefreshHoverCard(s);
            RefreshSelectedItem(s);
            RefreshPulse(s);
            RefreshBanner();
            RefreshFeed();
            RefreshGameOver(s);
        }

        /// <summary>
        /// 把「本地玩家是否已出局」同步给第一人称控制器。
        ///
        /// 出局后放开就坐视角限制，玩家才能转头去看另外三个人的面板 ——
        /// 不然所谓的幽灵模式只是把自己那块屏幕换成真实概率，
        /// 人还是被钉在 78° 的视野里，什么也看不着。
        /// </summary>
        void SyncSpectatorMode(GameSession s)
        {
            if (_fps == null) _fps = FindObjectOfType<FirstPersonController>();
            if (_fps == null) return;

            bool ghost = IsGhost(s);
            if (_fps.spectatorMode != ghost) _fps.spectatorMode = ghost;

            if (!ghost)
            {
                director.ClearSpectatorView();
                return;
            }

            int watched = director.SpectatorViewSeat;
            var watchedPlayer = s.PlayerAt(watched);
            if (watched < 0 || watchedPlayer == null || watchedPlayer.eliminated)
                CycleSpectator(s, 1);

            if (Input.GetKeyDown(KeyCode.Q)) CycleSpectator(s, -1);
            if (Input.GetKeyDown(KeyCode.E)) CycleSpectator(s, 1);
        }

        void CycleSpectator(GameSession s, int direction)
        {
            if (s?.Players == null || s.Players.Length == 0) return;
            int from = director.SpectatorViewSeat >= 0 ? director.SpectatorViewSeat : director.localSeat;
            for (int step = 1; step <= s.Players.Length; step++)
            {
                int seat = (from + direction * step) % s.Players.Length;
                if (seat < 0) seat += s.Players.Length;
                var p = s.PlayerAt(seat);
                if (seat != director.localSeat && p != null && !p.eliminated)
                {
                    director.SetSpectatorViewSeat(seat);
                    return;
                }
            }
        }

        FirstPersonController _fps;

        void RefreshTopBar(GameSession s)
        {
            int state = s.IsOver ? -2 : s.CurrentSeat;
            _lastTurnSeat = state;
            if (s.IsOver)
            {
                _turnLabel.text = "对局结束";
                _turnLabel.color = UiTheme.TextMid;
                if (_driftBadge != null) _driftBadge.text = string.Empty;
                if (_turnProgressFill != null) _turnProgressFill.fillAmount = 0f;
                return;
            }

            string owner = s.CurrentSeat == director.localSeat ? "你的回合" : $"{NameOf(s.CurrentSeat)}的回合";
            float drift = s.Drift != null ? s.Drift.baseDriftMagnitude * s.CurrentEscalation() * 100f : 0f;
            float left = director.TurnSecondsRemaining;
            string timer = left >= 0f ? $"    {Mathf.CeilToInt(left):00} 秒" : string.Empty;
            string text = $"{owner}{timer}";
            if (_turnLabel.text != text) _turnLabel.text = text;
            _turnLabel.color = director.ColorOfSeat(s.CurrentSeat);
            if (_driftBadge != null)
            {
                _driftBadge.text = $"概率扰动  ±{drift:0.0}%";
                _driftBadge.color = drift >= 8f ? UiTheme.Danger : UiTheme.TextMid;
            }
            if (_turnProgressFill != null)
            {
                float duration = director.config != null ? Mathf.Max(1f, director.config.turnTimeLimitSeconds) : 60f;
                _turnProgressFill.fillAmount = Mathf.Clamp01(left / duration);
                _turnProgressFill.color = left <= 10f ? UiTheme.Danger : UiTheme.AccentHi;
            }
        }

        void RefreshCards(GameSession s)
        {
            if (voice == null) voice = FindObjectOfType<VoiceChatManager>();
            for (int i = 0; i < _cards.Length; i++)
            {
                var card = _cards[i];
                if (card == null) continue;

                var p = s.PlayerAt(i);
                if (p == null)
                {
                    card.bg.gameObject.SetActive(false);
                    continue;
                }

                bool isTurn = i == s.CurrentSeat && !s.IsOver;
                bool isMe = i == director.localSeat;
                var seatColor = director.ColorOfSeat(i);

                // 当前行动者的卡整体点亮；出局的整卡压暗
                card.bg.color = isTurn ? new Color(1.35f, 1.3f, 1.15f, 1f)
                    : p.eliminated ? new Color(0.6f, 0.6f, 0.65f, 0.75f)
                    : Color.white;

                card.chip.color = p.eliminated
                    ? new Color(seatColor.r, seatColor.g, seatColor.b, 0.3f) : seatColor;

                string tag = isMe ? " [你]" : (p.isBot ? " [AI]" : "");
                card.name.text = p.displayName + tag;
                card.name.color = p.eliminated ? UiTheme.TextDisabled : UiTheme.TextHi;
                card.name.fontStyle = isTurn ? FontStyles.Bold : FontStyles.Normal;

                // 数值血量必须显示：实体灯泡在对手视角中可能被角色或面板挡住。
                string hearts = p.eliminated ? new string('○', p.maxLives)
                    : new string('●', Mathf.Max(0, p.lives)) + new string('○', Mathf.Max(0, p.maxLives - p.lives));
                card.stat.text = p.eliminated
                    ? $"生命 0/{p.maxLives}   已出局"
                    : $"{hearts}  {p.lives}/{p.maxLives}   道具 {p.inventory.Count}/{director.config.maxInventorySize}"
                      + (p.shieldCharges > 0 ? $"   护盾 ×{p.shieldCharges}" : "");
                card.stat.color = p.eliminated ? UiTheme.TextDisabled
                    : p.shieldCharges > 0 ? UiTheme.Info : UiTheme.TextLo;

                bool talking = voice != null && voice.IsSeatSpeaking(i);
                card.microphone.alpha = talking ? 1f : 0f;
                Color micColor = p.eliminated ? UiTheme.TextDisabled : seatColor;
                card.microphoneBody.color = micColor;
                card.microphoneStem.color = micColor;
                card.microphoneBase.color = micColor;
            }
        }

        void RefreshHotbar(GameSession s)
        {
            var me = s.PlayerAt(director.localSeat);
            bool myTurn = !s.IsOver && s.CurrentSeat == director.localSeat && !director.IsLocalAutoPlayed;

            if (_hotbarCaption != null)
                _hotbarCaption.text = myTurn ? "数字键选择道具 · E 键使用" : "道具（未轮到你）";

            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot == null) continue;

                // 背包元素是 ItemInstance（结构体）而不是 ItemDefinition，
                // 三目运算符会因为没有公共类型而推断失败，所以分开取。
                var inst = me != null && i < me.inventory.Count ? me.inventory[i] : default;
                var def = inst.definition;
                bool has = def != null;
                bool selected = interactor != null && interactor.SelectedItemSlot == i;

                // 未就绪时整体压暗，玩家一眼能看出「现在能不能用」
                slot.bg.color = !has ? new Color(0.55f, 0.55f, 0.6f, 0.7f)
                    : selected ? new Color(1.35f, 1.12f, 0.68f, 1f)
                    : myTurn ? Color.white
                    : new Color(0.78f, 0.78f, 0.82f, 0.9f);

                if (def == null)
                {
                    slot.name.text = "";
                    slot.tag.text = "";
                    continue;
                }

                slot.name.text = def.displayName;
                slot.tag.text = RarityTag(def.rarity);
                slot.name.color = RarityColor(def.rarity);
            }
        }

        void RefreshHoverCard(GameSession s)
        {
            if (interactor == null || interactor.Hovered == null || director.IsPregame || IsGhost(s))
            {
                if (_hoverCard.gameObject.activeSelf) _hoverCard.gameObject.SetActive(false);
                return;
            }

            var tile = interactor.Hovered;

            // 双保险：即便交互器放行了，也只显示自己面板上的格子
            if (interactor.HoveredTablet != null &&
                !interactor.HoveredTablet.IsLocalPanel(director.localSeat))
            {
                _hoverCard.gameObject.SetActive(false);
                return;
            }

            var board = s.SharedBoard;
            if (tile.ownerSeat >= 0)
            {
                var o = s.PlayerAt(tile.ownerSeat);
                if (o != null) board = o.board;
            }
            if (board == null || !board.InBounds(tile.x, tile.y))
            {
                _hoverCard.gameObject.SetActive(false);
                return;
            }

            if (!_hoverCard.gameObject.activeSelf) _hoverCard.gameObject.SetActive(true);

            var cell = board.Get(tile.x, tile.y);

            if (cell.blasted)
            {
                _hoverTitle.text = $"({tile.x},{tile.y})  已踩过";
                _hoverTitle.color = UiTheme.Danger;
                _hoverSub.text = "该格已处理，不能再次选择";
                return;
            }

            if (cell.revealed)
            {
                _hoverTitle.text = $"({tile.x},{tile.y})  已翻开";
                _hoverTitle.color = UiTheme.TextMid;
                _hoverSub.text = $"相邻期望雷数 {cell.revealedExpectation:F1}";
                return;
            }

            var me = s.PlayerAt(director.localSeat);
            float known = -1f;
            if (me != null && me.knownTruth != null)
            {
                int idx = board.Index(tile.x, tile.y);
                if (idx >= 0 && idx < me.knownTruth.Length) known = me.knownTruth[idx];
            }

            float shownRisk = known >= 0f ? known : cell.displayedRisk;
            string source = known >= 0f ? "探查概率" : "初始概率";
            _hoverTitle.text = $"{source}  {Mathf.RoundToInt(shownRisk * 100f)}%";
            _hoverTitle.color = UiTheme.TextHi;

            _hoverSub.text = cell.flagged ? $"({tile.x},{tile.y}) · 已插旗" : $"格子 ({tile.x},{tile.y})";
        }

        void RefreshSelectedItem(GameSession s)
        {
            int selected = interactor != null ? interactor.SelectedItemSlot : -1;
            var def = interactor != null ? interactor.SelectedItemDefinition : null;
            bool show = selected >= 0 && def != null && !s.IsOver && !IsGhost(s);

            if (_selectedItemCard.gameObject.activeSelf != show)
                _selectedItemCard.gameObject.SetActive(show);
            if (!show) return;
            if (_lastSelectedItemSlot == selected && _selectedItemName.text == def.displayName) return;

            _lastSelectedItemSlot = selected;
            _selectedItemName.text = $"[{selected + 1}] {def.displayName}";
            _selectedItemName.color = RarityColor(def.rarity);
            _selectedItemDescription.text = def.description;
            _selectedItemPrompt.text = def.requiresTarget
                ? "对准目标格后按 E 使用"
                : "按 E 立即使用";
        }

        void RefreshPulse(GameSession s)
        {
            bool show = !s.IsOver && s.CurrentSeat == director.localSeat
                        && !director.IsLocalAutoPlayed && !IsGhost(s);
            if (show && director.IsPregame) show = false;

            float a = show ? 0.12f + 0.10f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f)) : 0f;
            var c = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, a);

            for (int i = 0; i < _pulse.Length; i++)
                if (_pulse[i] != null) _pulse[i].color = c;
        }

        // ============================================================ 横幅
        /// <summary>弹一条横幅。UGUI 版不需要手动淡出，由 RefreshBanner 按时间插值。</summary>
        public void ShowBanner(string text, Color color, float duration)
        {
            if (!_built || _banner == null) return;

            _banner.text = text;
            _bannerColor = color;
            _bannerStart = Time.unscaledTime;
            _bannerEnd = _bannerStart + duration;
            _banner.transform.parent.gameObject.SetActive(true);
        }

        void RefreshBanner()
        {
            if (_banner == null) return;

            float now = Time.unscaledTime;
            if (now > _bannerEnd)
            {
                if (_banner.transform.parent.gameObject.activeSelf)
                    _banner.transform.parent.gameObject.SetActive(false);
                return;
            }

            const float fade = 0.22f;
            float alpha = 1f;
            if (now - _bannerStart < fade) alpha = Mathf.InverseLerp(_bannerStart, _bannerStart + fade, now);
            if (_bannerEnd - now < fade) alpha = Mathf.Min(alpha, Mathf.InverseLerp(_bannerEnd, _bannerEnd - fade, now));

            _banner.color = new Color(_bannerColor.r, _bannerColor.g, _bannerColor.b, alpha);

            // 轻微下浮，比「原地闪现」更像一次播报
            float k = Mathf.Clamp01((now - _bannerStart) / fade);
            var rt = (RectTransform)_banner.transform.parent;
            rt.anchoredPosition = new Vector2(0f, 132f + (1f - k) * 10f);
        }

        // ============================================================ 事件流
        readonly System.Collections.Generic.List<string> _feedTexts = new System.Collections.Generic.List<string>();
        readonly System.Collections.Generic.List<Color> _feedColors = new System.Collections.Generic.List<Color>();

        void OnGameEvent(GameEvent e)
        {
            if (e.type == GameEventType.PlayerEliminated && !_eliminationOrder.Contains(e.seatIndex))
                _eliminationOrder.Add(e.seatIndex);
            // 主机本地同样会收到所有权威事件；其他玩家的私密效果不能进入本机日志。
            if (e.secret && (director == null || e.seatIndex != director.localSeat)) return;
            var text = EventText(e);
            if (text == null) return;

            int round = director != null && director.Session != null ? director.Session.RoundIndex + 1 : 1;
            _feedTexts.Add($"第 {round} 轮  {text}");
            _feedColors.Add(EventColor(e));
            if (_feedTexts.Count > FeedLines)
            {
                _feedTexts.RemoveAt(0);
                _feedColors.RemoveAt(0);
            }
            _lastFeedCount = -1;   // 下一帧重绘
        }

        void RefreshFeed()
        {
            if (_lastFeedCount == _feedTexts.Count) return;
            _lastFeedCount = _feedTexts.Count;

            for (int i = 0; i < _feed.Length; i++)
            {
                if (_feed[i] == null) continue;

                // 最新的排在最上面
                int idx = _feedTexts.Count - 1 - i;
                if (idx < 0)
                {
                    _feed[i].text = "";
                    continue;
                }

                _feed[i].text = _feedTexts[idx];
                Color c = _feedColors[idx];
                c.a = Mathf.Lerp(0.45f, 1f, 1f - i / (float)Mathf.Max(1, _feed.Length - 1));
                _feed[i].color = c;
            }
        }

        void ResetTransient()
        {
            _eliminationOrder.Clear();
            _showResultDetails = false;
            _feedTexts.Clear();
            _feedColors.Clear();
            _lastFeedCount = -1;
            _lastTurnSeat = int.MinValue;
            _lastSelectedItemSlot = int.MinValue;
            if (_banner != null) _banner.transform.parent.gameObject.SetActive(false);
        }

        // ============================================================ 结算
        RectTransform _gameOver;
        RectTransform _resultCard;
        Vector2Int _resultScreen;
        Rect _resultSafeArea;
        TMP_Text _goTitle, _goSub;
        readonly TMP_Text[] _goRows = new TMP_Text[GameSession.MaxSeats];
        bool _goShown;
        bool _showResultDetails;
        readonly Image[] _resultBadges = new Image[GameSession.MaxSeats];
        readonly System.Collections.Generic.List<int> _eliminationOrder = new System.Collections.Generic.List<int>();
        readonly TMP_Text[,] _resultCells = new TMP_Text[GameSession.MaxSeats, 5];
        const string LobbySceneName = "BombTavern_Play";

        void BuildGameOver(RectTransform root)
        {
            // 全屏模态层：拦住后面的道具栏与视角操作，
            // 让结算按钮成为唯一可交互目标。
            var overlay = UiKit.Rect(root, "GameOver", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var shade = overlay.gameObject.AddComponent<Image>();
            shade.color = new Color(0.012f, 0.008f, 0.006f, 0.86f);
            shade.raycastTarget = true;

            var bg = UiKit.Panel(overlay, "ResultCard");
            var box = bg.rectTransform;
            _resultCard = box;
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.anchoredPosition = Vector2.zero;
            box.sizeDelta = new Vector2(1120f, 0f);

            UiKit.VBox(box, (int)UiTheme.S2, UiTheme.PanelPadding, fitHeight: true);

            var eyebrow = UiKit.Label(box, "♛", UiTheme.FontTitle, UiTheme.AccentHi,
                TextAnchor.MiddleCenter);
            UiKit.Size(eyebrow.rectTransform, h: 46f);

            _goTitle = UiKit.Label(box, "对 局 结 束", UiTheme.FontTitle, UiTheme.Accent,
                TextAnchor.MiddleCenter);
            _goTitle.fontStyle = FontStyles.Bold;
            UiKit.Size(_goTitle.rectTransform, h: 40f);

            _goSub = UiKit.Label(box, "", UiTheme.FontBody, UiTheme.TextMid, TextAnchor.MiddleCenter);
            UiKit.Size(_goSub.rectTransform, h: 26f);

            var header = UiKit.Rect(box, "Columns", Vector2.zero, Vector2.one, Vector2.one * .5f, Vector2.zero, Vector2.zero);
            UiKit.Size(header, h: 38);
            string[] headings = { "排名", "玩家", "状态", "已开格", "踩雷" };
            float[] widths = { .13f, .38f, .60f, .80f, 1f };
            for (int col = 0; col < 5; col++) MakeResultCell(header, headings[col], col, widths, UiTheme.TextLo);
            for (int i = 0; i < _goRows.Length; i++)
            {
                var card = UiKit.Panel(box, "Standing_" + i, card: true);
                UiKit.Size(card.rectTransform, h: 82);
                if (i == 0)
                    card.sprite = UiKit.ChamferSprite(UiTheme.Surface3, UiTheme.AccentHi, 10, 2);
                for (int col = 0; col < 5; col++)
                    _resultCells[i, col] = MakeResultCell(card.rectTransform, "", col, widths, i == 0 ? UiTheme.AccentHi : UiTheme.TextMid);
                var name = _resultCells[i, 1].rectTransform;
                name.offsetMin = new Vector2(58, 4);
                var badge = UiKit.Rect(card.transform, "PlayerBadge", new Vector2(.13f, .5f), new Vector2(.13f, .5f), Vector2.one * .5f, new Vector2(27, 0), new Vector2(34, 34));
                var icon = badge.gameObject.AddComponent<Image>();
                icon.sprite = UiKit.RoundedSprite(Color.white, UiTheme.Line, 16, 2);
                _resultBadges[i] = icon;
                var fuse = UiKit.Rect(badge, "Fuse", new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.one * .5f, new Vector2(3, 4), new Vector2(5, 10));
                fuse.gameObject.AddComponent<Image>().color = UiTheme.AccentHi;
            }
            var btns = UiKit.Rect(box, "Btns", Vector2.zero, Vector2.one, Vector2.one * .5f, Vector2.zero, Vector2.zero);
            UiKit.Size(btns, h: 84);
            var actions = UiKit.HBox(btns, 24, new RectOffset(0, 0, 14, 0));
            actions.childForceExpandWidth = true;
            UiKit.Btn(btns, "返回大厅", OnBackToLobby);
            _goRestartBtn = UiKit.Btn(btns, "再来一局", OnRestart, accent: true);
            UiKit.Btn(btns, "详细战绩", () => { _showResultDetails = !_showResultDetails; });
            foreach (var button in btns.GetComponentsInChildren<Button>()) UiKit.Size(button.GetComponent<RectTransform>(), w: 300, h: 62);
            TavernSettingsLayout.StylePrimary(_goRestartBtn);
            var caption = UiKit.Label(box, "每一局，都是一段值得分享的酒馆故事", 21, UiTheme.TextMid, TextAnchor.MiddleCenter);
            UiKit.Size(caption.rectTransform, h: 38);

            _gameOver = overlay;
            overlay.gameObject.SetActive(false);
        }

        TMP_Text MakeResultCell(RectTransform parent, string text, int column, float[] widths, Color color)
        {
            var label = UiKit.Label(parent, text, 24, color, column == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = new Vector2(column == 0 ? 0 : widths[column - 1], 0);
            label.rectTransform.anchorMax = new Vector2(widths[column], 1);
            label.rectTransform.offsetMin = new Vector2(10, 4);
            label.rectTransform.offsetMax = new Vector2(-10, -4);
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        Button _goRestartBtn;

        void RefreshGameOver(GameSession s)
        {
            if (s.IsOver != _goShown)
            {
                _goShown = s.IsOver;
                _gameOver.gameObject.SetActive(_goShown);
                SetGameOverInteraction(_goShown);
                if (_goShown) _gameOver.SetAsLastSibling();
            }
            if (!_goShown) return;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if ((screen != _resultScreen || Screen.safeArea != _resultSafeArea) && _resultCard != null)
            {
                _resultScreen = screen;
                _resultSafeArea = Screen.safeArea;
                LayoutRebuilder.ForceRebuildLayoutImmediate(_resultCard);
                float height = Mathf.Max(1f, LayoutUtility.GetPreferredHeight(_resultCard));
                _resultCard.localScale = Vector3.one * UiKit.FitScale(new Vector2(1120f, height));
            }

            bool won = s.WinnerSeat == director.localSeat;
            _goTitle.text = won ? "你 活 到 了 最 后" : "对 局 结 束";
            _goSub.text = $"{NameOf(s.WinnerSeat)} 是最后的幸存者 · 共 {s.RoundIndex + 1} 回合";

            var order = new System.Collections.Generic.List<int>();
            for (int i = 0; i < s.Players.Length; i++) if (s.PlayerAt(i) != null) order.Add(i);
            order.Sort((a, b) =>
            {
                if (a == b) return 0;
                if (a == s.WinnerSeat) return -1;
                if (b == s.WinnerSeat) return 1;
                return _eliminationOrder.IndexOf(b).CompareTo(_eliminationOrder.IndexOf(a));
            });
            for (int i = 0; i < _goRows.Length; i++)
            {
                _resultCells[i, 0].transform.parent.gameObject.SetActive(i < order.Count);
                if (i >= order.Count) continue;
                var p = s.PlayerAt(order[i]);
                _resultBadges[i].color = director.ColorOfSeat(order[i]);
                _resultCells[i, 0].text = "第 " + (i + 1) + " 名";
                _resultCells[i, 1].text = p.displayName + (p.seatIndex == director.localSeat ? "  [你]" : p.isBot ? "  [AI]" : "");
                _resultCells[i, 2].text = _showResultDetails ? $"道具 {p.totalItemsUsed} / 插旗 {p.totalFlags}" : p.eliminated ? "触发地雷" : "最后幸存";
                _resultCells[i, 3].text = p.totalReveals.ToString();
                _resultCells[i, 4].text = p.totalHits.ToString();
            }

            // 联机下只有房主能开新局
            var lan = director.GetComponent<Net.LanNetworkManager>();
            bool canRestart = director.mode == GameMode.SinglePlayer || (lan != null && lan.IsHost);

            _goRestartBtn.interactable = canRestart;
            if (!canRestart && _goSub != null)
                _goSub.text += "   ·   等待房主开新一局…";
        }

        void OnRestart()
        {
            SetGameOverInteraction(false);
            ResetTransient();
            _goShown = false;
            _gameOver.gameObject.SetActive(false);

            var lan = director.GetComponent<Net.LanNetworkManager>();
            if (director.mode == GameMode.SinglePlayer) { director.StartSinglePlayer(); return; }
            if (lan != null && lan.IsHost) lan.RestartGame();
        }

        /// <summary>退出到桌面。单机与联机都直接结束进程（联机的连接由系统回收）。</summary>
        void OnQuitGame()
        {
            Application.Quit();
        }

        void OnBackToLobby()
        {
            ResetTransient();
            SetGameOverInteraction(true);
            SessionContext.ReturnToMainMenu();
        }

        void SetGameOverInteraction(bool active)
        {
            var fps = FindObjectOfType<FirstPersonController>();
            if (fps != null)
            {
                fps.SetUiMode(active);
                return;
            }

            // 没有 FPS 装置时的兜底（例如独立 UI 测试场景）。
            Cursor.lockState = active ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = active;
        }

        // ============================================================ 工具
        bool IsGhost(GameSession s)
        {
            var me = s.PlayerAt(director.localSeat);
            return me != null && me.eliminated;
        }

        string NameOf(int seat)
        {
            var p = director != null && director.Session != null ? director.Session.PlayerAt(seat) : null;
            return p != null ? p.displayName : $"座位{seat}";
        }

        static string Pct(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        static string RarityTag(ItemRarity r)
        {
            switch (r)
            {
                case ItemRarity.Rare: return "稀有";
                case ItemRarity.Epic: return "史诗";
                case ItemRarity.Legendary: return "传说";
                default: return "普通";
            }
        }

        static Color RarityColor(ItemRarity r)
        {
            switch (r)
            {
                case ItemRarity.Rare: return UiTheme.Info;
                case ItemRarity.Epic: return new Color(0.78f, 0.55f, 0.98f);
                case ItemRarity.Legendary: return UiTheme.Accent;
                default: return UiTheme.TextMid;
            }
        }

        /// <summary>把事件翻译成一行可读文本。返回 null 表示这条不进事件流。</summary>
        string EventText(GameEvent e)
        {
            switch (e.type)
            {
                case GameEventType.CellRevealed:
                    return $"{Name(e)} 安全翻开 ({e.x},{e.y})";
                case GameEventType.CellHit:
                    return $"{Name(e)} 踩雷 ({e.x},{e.y})";
                case GameEventType.CellFlagged:
                    return $"{Name(e)} {(e.value > 0.5f ? "插旗" : "取消旗帜")} ({e.x},{e.y})";
                case GameEventType.CellProbed:
                    return $"{Name(e)} 探明 ({e.x},{e.y}) → {Pct(e.value)}";
                case GameEventType.CellRiskModified:
                    return $"{Name(e)} 用 [{e.message}] 改动 ({e.x},{e.y})";
                case GameEventType.RowScanned:
                    return $"{Name(e)} 扫描第 {e.y} 行";
                case GameEventType.AreaScanned:
                    return $"{Name(e)} 扫描区域 ({e.x},{e.y})";
                case GameEventType.ItemGained:
                    return $"{Name(e)} 摸到 [{e.message}]";
                case GameEventType.InventoryFull:
                    return $"{Name(e)} 背包已满，[{e.message}] 丢失";
                case GameEventType.ItemUsed:
                    return $"{Name(e)} 使用 [{e.message}]";
                case GameEventType.PlayerEliminated:
                    return $"{Name(e)} 出局";
                case GameEventType.GameOver:
                    return $"{Name(e)} 获胜";
                default:
                    return null;
            }
        }

        /// <summary>事件流的取色。这里的 Name 是静态方法，需要从文本里反推不出座位，
        /// 所以颜色统一按语义走，不按玩家标识色 —— 事件流要的是一眼分辨「好事/坏事」。</summary>
        string Name(GameEvent e) => NameOf(e.seatIndex);

        static Color EventColor(GameEvent e)
        {
            switch (e.type)
            {
                case GameEventType.CellHit:
                case GameEventType.PlayerEliminated:
                    return UiTheme.Danger;
                case GameEventType.CellRevealed:
                case GameEventType.ItemGained:
                    return UiTheme.Success;
                case GameEventType.CellProbed:
                case GameEventType.CellRiskModified:
                    return UiTheme.Accent;
                default:
                    return UiTheme.TextMid;
            }
        }
    }
}
