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
            var imported = Resources.Load<Sprite>("BombTavern/UI/RulesTutorialSheet");
            var sheet = imported != null ? imported.texture : null;
            if (sheet == null)
            {
                Debug.LogError("[游戏说明] 找不到 RulesTutorialSheet。");
                return;
            }

            _blocker = UiKit.Rect(root, "RulesTutorialBlocker", Vector2.zero, Vector2.one,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            _blocker.gameObject.AddComponent<Image>().color = new Color(.015f, .009f, .005f, .63f);
            _blocker.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            _panel = UiKit.Rect(root, "RulesTutorialPanel", new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero,
                new Vector2(1760f, 1120f));
            _panel.anchoredPosition = new Vector2(0f, -45f);
            AddImage(_panel, "Frame", sheet, 18, 363, 1217, 435, true,
                new Vector4(65, 65, 65, 65));
            _animator = _panel.gameObject.AddComponent<UiModalAnimator>();

            var logo = AddImage(_panel, "RulesTitle", sheet, 18, 2, 555, 290);
            Place(logo.rectTransform, 570, -154, 595, 260);
            PutText(_panel, "了解规则、流程与胜负条件", 31, new Color(1f, .91f, .76f),
                580, 128, 600, 46, TextAnchor.MiddleCenter);

            var close = AddImage(_panel, "Close", sheet, 1116, 243, 120, 109);
            Place(close.rectTransform, 1620, 26, 92, 82);
            close.raycastTarget = true;
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = close;
            closeButton.onClick.AddListener(Close);

            var viewport = UiKit.Rect(_panel, "RulesScrollViewport", Vector2.zero, Vector2.one,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            viewport.offsetMin = new Vector2(75f, 36f);
            viewport.offsetMax = new Vector2(-75f, -174f);
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, .001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 55f;

            var content = UiKit.Rect(viewport, "RulesContent", new Vector2(0, 1),
                new Vector2(1, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(0, 1950));
            _scroll.content = content;

            BuildOverview(content, sheet);
            BuildDetails(content, sheet);

            _blocker.gameObject.SetActive(false);
            _panel.GetComponent<CanvasGroup>().alpha = 0f;
            _panel.gameObject.SetActive(false);
            FitToScreen();
        }

        static readonly Color GuideGold = new Color(1f, .73f, .25f);
        static readonly Color GuideText = new Color(1f, .91f, .76f);

        static void BuildOverview(RectTransform content, Texture2D sheet)
        {
            var goal = Card(content, sheet, "Goal", 0, 130);
            PutIcon(goal, sheet, "Target", 595, 90, 133, 150, 35, 23, 105, 93);
            PutText(goal, "游戏目标", 40, GuideGold, 165, 15, 330, 53, TextAnchor.MiddleLeft, true);
            PutText(goal, "通过观察概率、辨别信息、合理翻格与使用道具，在多人对局中避免成为爆炸者。",
                29, GuideText, 165, 67, 1330, 45);

            var flow = Card(content, sheet, "Flow", 145, 220);
            PutIcon(flow, sheet, "Gear", 735, 90, 133, 150, 35, 16, 106, 92);
            PutText(flow, "基础流程", 40, GuideGold, 165, 12, 330, 55, TextAnchor.MiddleLeft, true);
            string[] steps = { "观察概率", "判断信息", "翻格或使用道具", "承担风险", "进入下一回合" };
            float[] widths = { 260, 260, 335, 255, 310 };
            float x = 32;
            for (int i = 0; i < steps.Length; i++)
            {
                var step = AddImage(flow, "Step_" + (i + 1), sheet, 594, 244, 244, 107,
                    true, new Vector4(25, 25, 20, 20));
                Place(step.rectTransform, x, 105, widths[i], 90);
                var badge = UiKit.Rect(step.transform, "Number", new Vector2(0, .5f),
                    new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(16, 0),
                    new Vector2(60, 60));
                var badgeImage = badge.gameObject.AddComponent<Image>();
                badgeImage.sprite = UiKit.RoundedSprite(new Color(1f, .69f, .17f),
                    new Color(.39f, .17f, .025f), 32, 3);
                badgeImage.raycastTarget = false;
                PutText(badge, (i + 1).ToString(), 36, new Color(.18f, .09f, .02f),
                    0, 0, 60, 60, TextAnchor.MiddleCenter, true);
                PutText(step.rectTransform, steps[i], 26, GuideText, 80, 12, widths[i] - 90, 66);
                x += widths[i];
                if (i == steps.Length - 1) continue;
                PutText(flow, "›", 53, GuideGold, x + 2, 110, 34, 72, TextAnchor.MiddleCenter);
                x += 36;
            }

            var rules = Card(content, sheet, "CoreRules", 380, 300);
            PutIcon(rules, sheet, "Clipboard", 870, 89, 112, 151, 35, 14, 100, 100);
            PutText(rules, "核心规则", 40, GuideGold, 165, 16, 330, 55, TextAnchor.MiddleLeft, true);
            string[] bullets =
            {
                "每位玩家在自己的回合内进行操作。",
                "翻到危险结果时可能直接出局或触发特殊效果。",
                "部分信息可能过时，需结合局势判断真伪。",
                "合理使用道具可以改变风险与信息优势。"
            };
            for (int i = 0; i < bullets.Length; i++)
            {
                PutText(rules, "•", 40, GuideGold, 46, 91 + i * 48, 28, 42);
                PutText(rules, bullets[i], 27, GuideText, 90, 91 + i * 48, 780, 42);
            }
            var note = AddImage(rules, "TipsNote", sheet, 265, 962, 720, 286);
            Place(note.rectTransform, 905, 26, 650, 255);
            PutIcon(note.rectTransform, sheet, "Bulb", 1110, 88, 140, 153, 38, 14, 74, 74);
            PutText(note.rectTransform, "小提示", 36, new Color(.20f, .10f, .02f),
                119, 18, 230, 53, TextAnchor.MiddleLeft, true);
            PutText(note.rectTransform, "1    注意观察他人的选择与停顿。\n\n2    不要只看概率，也要怀疑信息来源。",
                25, new Color(.18f, .10f, .04f), 52, 95, 550, 130);

            var win = Card(content, sheet, "Victory", 696, 125);
            PutIcon(win, sheet, "Trophy", 985, 90, 126, 150, 35, 14, 100, 94);
            PutText(win, "胜负说明", 39, GuideGold, 165, 13, 340, 51, TextAnchor.MiddleLeft, true);
            PutText(win, "坚持到最后，或在关键回合中成功规避爆炸，即可取得优势并赢得对局。",
                28, GuideText, 165, 68, 1310, 42);
        }

        static void BuildDetails(RectTransform content, Texture2D sheet)
        {
            PutText(content, "详细玩法 · 向下滚动阅读", 34, GuideGold,
                35, 865, 900, 55, TextAnchor.MiddleLeft, true);
            Detail(content, sheet, 925, "格子上的数字与颜色",
                "盘面颜色和悬停百分比表示上次公开的爆炸风险，而非固定地雷。翻格时按当时的真实风险判定，因此低风险也可能爆炸，高风险也可能安全。安全格会留下已探明标记，踩过的格子会留下爆炸痕迹。", 182);
            Detail(content, sheet, 1120, "为什么旧信息会骗人",
                "每回合后，未翻开的格子可能发生隐藏漂移，公开颜色不一定立即更新。探查、扫描等道具可以刷新局部信息；但使用道具也可能暴露你的意图。", 158);
            Detail(content, sheet, 1290, "回合、道具与胜负",
                "轮到你时可左键翻格、右键插旗或取消旗帜；已插旗的格子须先取消旗帜。可先使用道具再翻格。生命归零即出局，最后存活的玩家获胜。空座位和中途离开的座位由 AI 接管。共用地图上的增险陷阱对所有人有效，包括使用者。", 190);
            Detail(content, sheet, 1492, "联机、语音与观战",
                "房主负责结算并同步局面。默认按住 V 发言，也可在音频设置切换自动声控；死亡后仍可双向语音，并用 Q / E 切换观战玩家。观战可看到对方指向的格子和公开概率，但不能操作。", 175);
            Detail(content, sheet, 1679, "操作速查",
                "左键翻格、右键插旗；数字键 1—6 选择道具，E 键使用；V 键按住说话，H 键切换本地 AI 托管。Esc 打开或关闭对局设置。本说明可从主页或设置再次打开。", 170);
        }

        static RectTransform Card(RectTransform parent, Texture2D sheet, string name, float top, float height)
        {
            var image = AddImage(parent, name, sheet, 18, 805, 1218, 151,
                true, new Vector4(50, 50, 38, 38));
            Place(image.rectTransform, 0, top, 1610, height);
            return image.rectTransform;
        }

        static void Detail(RectTransform parent, Texture2D sheet,
            float top, string heading, string body, float height)
        {
            var card = Card(parent, sheet, "Detail_" + heading, top, height);
            PutText(card, heading, 33, GuideGold, 42, 13, 1450, 45, TextAnchor.MiddleLeft, true);
            PutText(card, body, 25, GuideText, 42, 62, 1500, height - 68);
        }

        static void PutIcon(Transform parent, Texture2D sheet, string name,
            int sourceX, int sourceY, int sourceWidth, int sourceHeight,
            float x, float top, float width, float height)
        {
            var image = AddImage(parent, name, sheet, sourceX, sourceY, sourceWidth, sourceHeight);
            Place(image.rectTransform, x, top, width, height);
            image.preserveAspect = true;
        }

        static Image AddImage(Transform parent, string name, Texture2D sheet,
            int x, int y, int width, int height, bool sliced = false, Vector4 border = default)
        {
            var rect = UiKit.Rect(parent, name, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, 1), Vector2.zero, new Vector2(width, height));
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Sprite.Create(sheet, new Rect(x, sheet.height - y - height, width, height),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, border);
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        static TMP_Text PutText(Transform parent, string value, float size, Color color,
            float x, float top, float width, float height,
            TextAnchor alignment = TextAnchor.MiddleLeft, bool bold = false)
        {
            var label = UiKit.Label(parent, value, size, color, alignment);
            Place(label.rectTransform, x, top, width, height);
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            return label;
        }

        static void Place(RectTransform rect, float x, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -top);
            rect.sizeDelta = new Vector2(width, height);
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
            _animator.SetTargetScale(UiKit.FitScale(new Vector2(_panel.sizeDelta.x, _panel.sizeDelta.y + 175f)));
        }
    }
}
