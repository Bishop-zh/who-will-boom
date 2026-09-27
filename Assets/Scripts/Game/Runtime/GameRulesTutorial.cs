using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.Runtime
{
    /// <summary>独立的规则教程弹窗；菜单和对局设置共用同一套内容。</summary>
    [DisallowMultipleComponent]
    public sealed class GameRulesTutorial : MonoBehaviour
    {
        const string SeenKey = "BombTavern.RulesTutorialSeen.v1";

        RectTransform _blocker;
        RectTransform _panel;
        ScrollRect _scroll;
        UiModalAnimator _animator;
        Vector2Int _lastScreen;
        Rect _lastSafeArea;

        public bool IsOpen { get; private set; }

        public void Configure(RectTransform root)
        {
            _blocker = UiKit.Rect(root, "RulesTutorialBlocker", Vector2.zero, Vector2.one,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            _blocker.gameObject.AddComponent<Image>().color = new Color(.01f, .008f, .005f, .84f);
            _blocker.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            var panelImage = UiKit.Panel(root, "RulesTutorialPanel");
            _panel = panelImage.rectTransform;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(.5f, .5f);
            _panel.anchoredPosition = Vector2.zero;
            _panel.sizeDelta = new Vector2(1080f, 780f);
            _animator = _panel.gameObject.AddComponent<UiModalAnimator>();

            var title = UiKit.Label(_panel, "酒馆生存手册", 38f, UiTheme.AccentHi, TextAnchor.MiddleCenter);
            PlaceTop(title.rectTransform, 20f, 56f);
            title.fontStyle = FontStyles.Bold;
            var subtitle = UiKit.Label(_panel, "读懂概率，留意过期的信息，活到最后。", 22f,
                UiTheme.TextMid, TextAnchor.MiddleCenter);
            PlaceTop(subtitle.rectTransform, 77f, 36f);

            var viewport = UiKit.Rect(_panel, "RulesScrollViewport", Vector2.zero, Vector2.one,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            viewport.offsetMin = new Vector2(48f, 112f);
            viewport.offsetMax = new Vector2(-48f, -126f);
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, .001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;

            var content = UiKit.Rect(viewport, "RulesContent", new Vector2(0f, 1f), Vector2.one,
                new Vector2(.5f, 1f), Vector2.zero, Vector2.zero);
            UiKit.VBox(content, 10f, new RectOffset(12, 24, 8, 24), fitHeight: true);
            _scroll.content = content;

            AddSection(content, "01 · 获胜目标",
                "最多四名玩家轮流行动。每人开局有一定次数的生命，踩雷且没有护盾时失去一条；生命归零即出局。成为最后仍存活的玩家即可获胜。空座位由 AI 补齐；对局中离开的玩家也可由 AI 接管。", 112f);
            AddSection(content, "02 · 格子上的数字与颜色",
                "盘面颜色和悬停百分比表示上次公开的爆炸风险，不是预先放好的固定地雷。翻格时，系统按该格当时的真实风险掷骰，因此低风险也可能爆炸，高风险也可能安全。安全翻开会留下已探明的格子；踩过的格子会留下爆炸痕迹。", 138f);
            AddSection(content, "03 · 为什么旧信息会骗人",
                "每回合后，尚未翻开的格子可能发生隐藏的概率漂移。公开颜色不一定立即更新，读数越旧，越应该谨慎。探查单格、扫描一行或区域等道具可以刷新局部信息；它们也会暴露你的行动意图。", 116f);
            AddSection(content, "04 · 你的回合能做什么",
                "轮到你时，左键翻开格子，右键插旗或取消旗帜。旗帜是你的判断，不能保证该格一定危险；已插旗的格子需先取消旗帜才能翻开。可以先选择道具并使用，再决定翻哪一格。通常翻格后换人，具体限时与节奏以房主创建对局时的设置为准；超时仍未行动会自动翻开一格。", 145f);
            AddSection(content, "05 · 道具与风险",
                "道具可用于探查、扫描、降低或提高风险、获得护盾、冻结漂移、查看邻格信息或改变行动顺序。部分效果可以施加给对手，部分使用记录可能对其他人保密。护盾会抵挡一次爆炸，但那次爆炸仍会在盘面留下痕迹。", 118f);
            AddSection(content, "06 · 单机、联机与语音",
                "主菜单可单人开始、创建房间或加入房间。房主负责结算操作，其他玩家看到同步后的局面；联机对局不会因有人打开设置而暂停。默认按住 V 发言，也可在音频设置切换自动声控；可先用麦克风测试确认设备可用。", 118f);
            AddSection(content, "07 · 操作速查",
                "鼠标拖拽转动视角，左键翻格，右键插旗；数字键 1—6 选择道具，E 键使用；V 键按住说话，H 键切换本地 AI 托管。按 Esc 可打开或关闭对局设置。游戏规则可随时从设置里的“查看完整规则”再次打开。", 118f);

            var hint = UiKit.Label(_panel, "向下滚动阅读完整规则 · 以后可从设置再次打开", 20f,
                UiTheme.TextLo, TextAnchor.MiddleCenter);
            PlaceBottom(hint.rectTransform, 94f, 26f);
            var close = UiKit.Btn(_panel, "我明白了 · 开始游戏", Close, accent: true);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(.5f, 0f);
            closeRect.anchoredPosition = new Vector2(0f, 27f);
            closeRect.sizeDelta = new Vector2(340f, 58f);

            _blocker.gameObject.SetActive(false);
            _panel.GetComponent<CanvasGroup>().alpha = 0f;
            _panel.gameObject.SetActive(false);
            FitToScreen();
        }

        static void AddSection(RectTransform parent, string heading, string body, float bodyHeight)
        {
            UiKit.SectionTitle(parent, heading);
            var paragraph = UiKit.Paragraph(parent, body, 22f, UiTheme.TextHi);
            paragraph.lineSpacing = 4f;
            UiKit.Size(paragraph.rectTransform, h: bodyHeight);
        }

        static void PlaceTop(RectTransform rect, float top, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(900f, height);
        }

        static void PlaceBottom(RectTransform rect, float bottom, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottom);
            rect.sizeDelta = new Vector2(900f, height);
        }

        public void ShowFirstTime()
        {
            if (!Application.isBatchMode && PlayerPrefs.GetInt(SeenKey, 0) == 0) Show();
        }

        public void Show()
        {
            if (IsOpen || _panel == null) return;
            IsOpen = true;
            FitToScreen();
            _blocker.gameObject.SetActive(true);
            _blocker.SetAsLastSibling();
            _panel.SetAsLastSibling();
            _animator.Show();
            _scroll.verticalNormalizedPosition = 1f;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
            _blocker.gameObject.SetActive(false);
            _animator.Hide();
        }

        void Update()
        {
            if (_panel == null) return;
            if (_lastScreen != new Vector2Int(Screen.width, Screen.height) || _lastSafeArea != Screen.safeArea)
                FitToScreen();
        }

        void FitToScreen()
        {
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
            _lastSafeArea = Screen.safeArea;
            _animator.SetTargetScale(UiKit.FitScale(_panel.sizeDelta));
        }
    }
}
