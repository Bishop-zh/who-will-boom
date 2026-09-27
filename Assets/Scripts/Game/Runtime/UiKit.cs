using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using UnityEngine.TextCore.LowLevel;

namespace BombTavern.Runtime
{
    /// <summary>
    /// UGUI 基础设施：画布、字体、主题化控件。
    ///
    /// 为什么从 IMGUI 迁到 UGUI：
    ///   1) **适配** —— CanvasScaler 按参考分辨率等比缩放。IMGUI 里到处是手算像素矩形，
    ///      换个宽高比就错位，而我们要上 PC + 手机多端。
    ///   2) **性能** —— `OnGUI` 每帧重建整个布局；UGUI 只在脏了的时候重建。
    ///
    /// 字体统一使用 TextMeshPro。中文源字体随包携带并在运行时创建动态 SDF 图集，
    /// 避免测试人员的电脑没有指定系统字体时出现空白文字。
    /// </summary>
    public static class UiKit
    {
        /// <summary>2K 桌面端设计参考分辨率。</summary>
        public static readonly Vector2 PcReference = new Vector2(2560f, 1440f);

        /// <summary>1080p 窗口仍使用原来的参考值，避免在低于 2K 的屏幕上把 UI 缩小。</summary>
        public static readonly Vector2 StandardPcReference = new Vector2(1920f, 1080f);

        /// <summary>小窗口桌面端使用的基准，避免 1280×720 下字号被压到难以阅读。</summary>
        public static readonly Vector2 CompactPcReference = new Vector2(1600f, 900f);

        /// <summary>
        /// 移动端参考分辨率。**故意取更小的值**，因为 CanvasScaler 是等比的：
        /// 参考值越小，整套 UI（含字号）在屏幕上占的比例就越大。
        ///
        /// 这是「一套布局适配双端」的正解 —— 不需要为手机改任何尺寸常量，
        /// 也不用维护两套预制体，只是换个缩放基准。
        /// 手机观看距离近但屏幕小，UI 必须更「大」才看得清；
        /// 而 PC 屏大，UI 偏大只占更多空余面积，代价很小。
        /// </summary>
        public static readonly Vector2 MobileReference = new Vector2(1280f, 720f);

        static bool? _mobileFromCli;

        /// <summary>`-btMobile` 强制移动端布局，便于在 PC 上验证手机排版。</summary>
        public static bool MobileForcedByCli()
        {
            if (_mobileFromCli.HasValue) return _mobileFromCli.Value;

            bool found = false;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], "-btMobile", System.StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }

            _mobileFromCli = found;
            return found;
        }

        /// <summary>当前是否按移动端布局（决定 UI 放大比例与触摸提示文案）。</summary>
        public static bool IsMobile
            => MobileForcedByCli() || Application.isMobilePlatform;

        /// <summary>当前生效的参考分辨率。</summary>
        public static Vector2 ReferenceResolution
        {
            get
            {
                if (IsMobile) return MobileReference;
                return PcReference;
            }
        }

        /// <summary>当前 UI 相对桌面基准的放大倍数，供需要按平台调尺寸的地方查询。</summary>
        public static float ScaleFactor => IsMobile
            ? PcReference.x / MobileReference.x
            : 1f;

        static Font _sourceFont;
        static TMP_FontAsset _tmpFont;
        static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

        // ============================================================ 字体
        public static TMP_FontAsset Font
        {
            get
            {
                if (_tmpFont != null) return _tmpFont;

                // 独立包不能可靠地把系统动态字体再次交给 TMP FontEngine；
                // 因此优先加载随包携带的 Noto Sans SC，系统字体只作为编辑器兜底。
                _sourceFont = Resources.Load<Font>("BombTavern/UI/Fonts/NotoSansSC-Regular");
                if (_sourceFont == null)
                    _sourceFont = UnityEngine.Font.CreateDynamicFontFromOSFont(
                        new[]
                        {
                            "Noto Sans SC", "Microsoft YaHei UI", "Microsoft YaHei", "SimHei",
                            "PingFang SC", "Noto Sans CJK SC", "Arial",
                        }, 48);
                if (_sourceFont == null)
                {
                    Debug.LogError("[UI] 无法加载中文字体 NotoSansSC-Regular。");
                    return TMP_Settings.defaultFontAsset;
                }
                _tmpFont = TMP_FontAsset.CreateFontAsset(_sourceFont, 64, 8,
                    GlyphRenderMode.SDFAA, 2048, 2048,
                    AtlasPopulationMode.Dynamic, true);
                if (_tmpFont == null)
                {
                    Debug.LogError("[UI] 无法从 Noto Sans SC 创建 TMP Font Asset。");
                    return TMP_Settings.defaultFontAsset;
                }
                _tmpFont.name = "BombTavern_RuntimeChinese_TMP";
                _tmpFont.hideFlags = HideFlags.DontSave;
                return _tmpFont;
            }
        }

        // ============================================================ 画布
        /// <summary>
        /// 建一个屏幕覆盖画布。
        /// matchWidthOrHeight = 0.5 表示宽高变化各占一半权重 ——
        /// 横屏游戏在 16:9 与 21:9 之间切换时，四角面板既不会飞出屏幕也不会挤到中间。
        /// </summary>
        public static Canvas CreateCanvas(string name, int sortOrder = 0)
        {
            var go = new GameObject(name);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            // 桌面端取 0.5（宽高变化各占一半权重，适应 16:9 ~ 21:9）。
            // 移动端偏向宽度：手机横屏的超宽比（20:9）如果按 0.5 混，
            // 高度方向会被压得偏小、字又变小了，靠宽度对齐更稳。
            scaler.matchWidthOrHeight = IsMobile ? 0.15f : 0.5f;
            scaler.referencePixelsPerUnit = 100f;
            go.AddComponent<AdaptiveCanvasScaler>().scaler = scaler;

            go.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();
            return canvas;
        }

        /// <summary>
        /// 创建安全区根节点。所有屏幕边缘 UI 都应挂在它下面，而不是直接挂 Canvas。
        /// 这样带刘海、圆角或系统手势条的设备不会遮住按钮；窗口尺寸变化时也会自动重算。
        /// </summary>
        public static RectTransform CreateSafeAreaRoot(Canvas canvas, string name = "SafeArea")
        {
            var rt = Rect(canvas.transform, name, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.gameObject.AddComponent<SafeAreaFitter>();
            return rt;
        }

        /// <summary>当前参考坐标下的可用宽高，用于选择紧凑/宽屏布局。</summary>
        public static Vector2 ReferenceScreenSize
        {
            get
            {
                float screenAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
                float referenceAspect = ReferenceResolution.x / ReferenceResolution.y;
                return screenAspect >= referenceAspect
                    ? new Vector2(ReferenceResolution.y * screenAspect, ReferenceResolution.y)
                    : new Vector2(ReferenceResolution.x, ReferenceResolution.x / screenAspect);
            }
        }

        public static bool CompactLayout => ReferenceScreenSize.x < 1500f || ReferenceScreenSize.y < 820f;

        /// <summary>Keep fixed-size modal layouts inside the canvas safe area.</summary>
        public static float FitScale(Vector2 contentSize, float margin = 32f)
        {
            if (contentSize.x <= 0f || contentSize.y <= 0f) return 1f;
            Vector2 available = ReferenceResolution;
            if (Screen.width > 0 && Screen.height > 0)
            {
                Vector2 reference = ReferenceResolution;
                float widthRatio = Screen.width / reference.x;
                float heightRatio = Screen.height / reference.y;
                float match = IsMobile ? 0.15f : 0.5f;
                float canvasScale = Mathf.Pow(2f, Mathf.Lerp(
                    Mathf.Log(widthRatio, 2f), Mathf.Log(heightRatio, 2f), match));
                Rect safe = Screen.safeArea;
                float safeWidth = safe.width > 0f ? safe.width : Screen.width;
                float safeHeight = safe.height > 0f ? safe.height : Screen.height;
                available = new Vector2(safeWidth / canvasScale, safeHeight / canvasScale);
            }
            return Mathf.Min(1f,
                Mathf.Max(0.01f, available.x - margin) / contentSize.x,
                Mathf.Max(0.01f, available.y - margin) / contentSize.y);
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        // ============================================================ 圆角 Sprite
        /// <summary>
        /// 取一张可 9 宫格拉伸的圆角 Sprite。
        /// 复用 UiTheme 的贴图生成器 —— IMGUI 与 UGUI 必须长得一样，
        /// 各生成一份迟早会走样。
        /// </summary>
        /// <summary>切角 Sprite（面板与按钮统一用它，见 UiTheme.ChamferTex 的说明）。</summary>
        public static Sprite ChamferSprite(Color fill, Color border, int chamfer, int borderWidth)
        {
            string key = $"C_{ColorUtility.ToHtmlStringRGBA(fill)}_{ColorUtility.ToHtmlStringRGBA(border)}_{chamfer}_{borderWidth}";
            if (SpriteCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = UiTheme.ChamferTex(fill, border, chamfer, borderWidth);
            int n = tex.width;

            var sprite = Sprite.Create(
                tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect,
                new Vector4(chamfer, chamfer, chamfer, chamfer));

            sprite.name = key;
            sprite.hideFlags = HideFlags.DontSave;
            SpriteCache[key] = sprite;
            return sprite;
        }

        public static Sprite RoundedSprite(Color fill, Color border, int radius, int borderWidth)
        {
            string key = $"{ColorUtility.ToHtmlStringRGBA(fill)}_{ColorUtility.ToHtmlStringRGBA(border)}_{radius}_{borderWidth}";
            if (SpriteCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = UiTheme.RoundedTex(fill, border, radius, borderWidth);
            int n = tex.width;

            // border 是 Vector4(left, bottom, right, top)：四边都留 radius，圆角才不会被拉伸
            var sprite = Sprite.Create(
                tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));

            sprite.name = key;
            sprite.hideFlags = HideFlags.DontSave;
            SpriteCache[key] = sprite;
            return sprite;
        }

        // ============================================================ 布局
        /// <summary>建一个 RectTransform。size 会**无条件**写入，见下面的注释。</summary>
        public static RectTransform Rect(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);

            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = offset;

            // 必须无条件设置：新建的 RectTransform 默认 sizeDelta 是 (100,100)，
            // 对拉伸型锚点来说它表示「比父容器大 100 像素」，
            // 于是进度条填充会撑出容器、整块糊在面板上。
            rt.sizeDelta = size;

            return rt;
        }

        /// <summary>四角定位的容器。anchor 用 (0,1)=左上、(1,0)=右下 这种写法。</summary>
        public static RectTransform AnchorBox(Transform parent, string name,
            Vector2 anchor, Vector2 offset, Vector2 size)
            => Rect(parent, name, anchor, anchor, anchor, offset, size);

        // ============================================================ 控件
        /// <summary>圆角面板底。</summary>
        public static Image Panel(Transform parent, string name, bool card = false)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(100f, 100f));

            int chamfer = card ? 10 : 14;

            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = card
                ? ChamferSprite(UiTheme.Surface2, UiTheme.Line, chamfer, 2)
                : ChamferSprite(UiTheme.Surface1, UiTheme.Line, chamfer, 2);
            img.type = Image.Type.Sliced;
            img.color = Color.white;

            // 顶部铜条：面板的「标牌」构型。
            // 没有它，面板就只是一块无特征的色块 —— 这正是「AI 生成味」的来源。
            // 内缩半个切角，避免在斜边上露出直角。
            var rail = Rect(rt, "Rail", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -chamfer * 0.5f),
                new Vector2(-chamfer * 2f, 2f));

            var railImg = rail.gameObject.AddComponent<Image>();
            railImg.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.30f);
            railImg.raycastTarget = false;
            var railLayout = rail.gameObject.AddComponent<LayoutElement>();
            railLayout.ignoreLayout = true;

            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            var rt = Rect(parent, "Label", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);

            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font;
            t.fontSize = size;
            t.color = color;
            t.alignment = ToTmpAlignment(align);
            t.text = text;
            t.raycastTarget = false;          // 文本不参与点击，避免挡住下面的按钮
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = true;

            return t;
        }

        /// <summary>主题化按钮。accent = 主行动点，用底色和描边拉开层级。</summary>
        public static Button Btn(Transform parent, string text, UnityAction onClick,
            bool accent = false, bool danger = false)
        {
            var rt = Rect(parent, "Btn_" + text, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(150f, 40f));

            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = danger
                ? ChamferSprite(new Color(UiTheme.Danger.r * 0.42f, UiTheme.Danger.g * 0.42f,
                    UiTheme.Danger.b * 0.42f, 0.96f), UiTheme.Danger, 9, 2)
                : accent
                ? ChamferSprite(UiTheme.Surface3, new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.9f), 9, 2)
                : ChamferSprite(UiTheme.Surface2, UiTheme.Line, 9, 2);
            img.type = Image.Type.Sliced;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            // 悬停 / 按下用颜色块区分：UGUI 默认的 ColorBlock 偏灰，
            // 在这套深色主题上几乎看不出来
            var cb = btn.colors;
            cb.normalColor = accent ? new Color(1.05f, 0.92f, 0.72f) : Color.white;
            cb.highlightedColor = danger ? new Color(1.18f, 1.02f, 1.02f)
                : accent ? new Color(1.22f, 1.06f, 0.84f) : new Color(1.25f, 1.25f, 1.28f);
            cb.pressedColor = accent ? new Color(0.86f, 0.76f, 0.60f) : new Color(0.82f, 0.82f, 0.86f);
            cb.selectedColor = cb.normalColor;
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;

            if (onClick != null) btn.onClick.AddListener(onClick);

            var label = Label(rt, text, UiTheme.FontBody,
                accent || danger ? Color.white : UiTheme.TextHi, TextAnchor.MiddleCenter);
            label.fontStyle = accent || danger ? FontStyles.Bold : FontStyles.Normal;
            btn.gameObject.AddComponent<UiButtonMotion>();

            return btn;
        }

        /// <summary>主题化单行输入框。占位文本和输入文本都使用统一字体与颜色。</summary>
        public static TMP_InputField Input(Transform parent, string value, string placeholder,
            int characterLimit = 32)
        {
            var rt = Rect(parent, "Input", Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(240f, 38f));

            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = ChamferSprite(UiTheme.Surface0, UiTheme.Line, 8, 2);
            bg.type = Image.Type.Sliced;

            var field = rt.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = bg;
            field.characterLimit = characterLimit;
            field.lineType = TMP_InputField.LineType.SingleLine;

            var text = Label(rt, value ?? string.Empty, UiTheme.FontBody, UiTheme.TextHi);
            text.rectTransform.offsetMin = new Vector2(12f, 4f);
            text.rectTransform.offsetMax = new Vector2(-12f, -4f);
            text.enableWordWrapping = false;

            var hint = Label(rt, placeholder ?? string.Empty, UiTheme.FontBody, UiTheme.TextLo);
            hint.rectTransform.offsetMin = new Vector2(12f, 4f);
            hint.rectTransform.offsetMax = new Vector2(-12f, -4f);
            hint.fontStyle = FontStyles.Italic;

            field.textComponent = text;
            field.placeholder = hint;
            field.text = value ?? string.Empty;
            return field;
        }

        /// <summary>适合说明文案的自动换行文本。</summary>
        public static TextMeshProUGUI Paragraph(Transform parent, string text, float size, Color color,
            TextAnchor align = TextAnchor.UpperLeft)
        {
            var label = Label(parent, text, size, color, align);
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Truncate;
            return label;
        }

        public static TextMeshProUGUI SectionTitle(Transform parent, string text)
        {
            var row = Rect(parent, "Section_" + text, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var line = row.gameObject.AddComponent<Image>();
            line.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.08f);
            line.raycastTarget = false;
            Size(row, h: 40f);
            var label = Label(row, text, UiTheme.FontHead, UiTheme.Accent, TextAnchor.MiddleLeft);
            label.fontStyle = FontStyles.Bold;
            label.rectTransform.offsetMin = new Vector2(12f, 0f);
            return label;
        }

        public static Slider SliderRow(Transform parent, string title, float value,
            UnityAction<float> changed, out TMP_Text valueLabel)
        {
            var row = Rect(parent, "Slider_" + title, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            HBox(row, S2(), new RectOffset(8, 8, 0, 0));
            Size(row, h: 58f);

            var titleLabel = Label(row, title, UiTheme.FontBody, UiTheme.TextHi);
            Size(titleLabel.rectTransform, w: 180f, h: 48f);

            var sliderRoot = Rect(row, "Track", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Size(sliderRoot, w: 320f, h: 36f);
            var slider = sliderRoot.gameObject.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = Mathf.Clamp01(value);

            var bg = Rect(sliderRoot, "Background", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 8f));
            var bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.color = new Color(UiTheme.TextLo.r, UiTheme.TextLo.g, UiTheme.TextLo.b, 0.35f);

            var fillArea = Rect(sliderRoot, "Fill Area", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-18f, 8f));
            fillArea.offsetMin = new Vector2(9f, -4f);
            fillArea.offsetMax = new Vector2(-9f, 4f);
            var fill = Rect(fillArea, "Fill", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fill.gameObject.AddComponent<Image>().color = UiTheme.AccentHi;
            slider.fillRect = fill;

            var handleArea = Rect(sliderRoot, "Handle Slide Area", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20f, 0f));
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);
            var handle = Rect(handleArea, "Handle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = RoundedSprite(UiTheme.AccentHi, UiTheme.TextHi, 10, 2);
            handleImage.type = Image.Type.Sliced;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;

            valueLabel = Label(row, Mathf.RoundToInt(slider.value * 100f) + "%",
                UiTheme.FontCaption, UiTheme.TextMid, TextAnchor.MiddleRight);
            Size(valueLabel.rectTransform, w: 76f, h: 48f);
            TMP_Text capturedLabel = valueLabel;
            slider.onValueChanged.AddListener(v =>
            {
                capturedLabel.text = Mathf.RoundToInt(v * 100f) + "%";
                changed?.Invoke(v);
            });
            return slider;
        }

        public static Toggle ToggleRow(Transform parent, string title, bool value, UnityAction<bool> changed)
        {
            var row = Rect(parent, "Toggle_" + title, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            HBox(row, S2(), new RectOffset(8, 8, 0, 0));
            Size(row, h: 54f);
            var label = Label(row, title, UiTheme.FontBody, UiTheme.TextHi);
            Size(label.rectTransform, w: 500f, h: 48f);

            var toggleRoot = Rect(row, "Switch", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Size(toggleRoot, w: 72f, h: 34f);
            var bg = toggleRoot.gameObject.AddComponent<Image>();
            bg.sprite = RoundedSprite(UiTheme.Surface0, UiTheme.Line, 12, 2);
            bg.type = Image.Type.Sliced;
            var mark = Rect(toggleRoot, "Checkmark", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
            var markImage = mark.gameObject.AddComponent<Image>();
            markImage.sprite = RoundedSprite(UiTheme.AccentHi, UiTheme.TextHi, 10, 2);
            markImage.type = Image.Type.Sliced;
            var toggle = toggleRoot.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.graphic = markImage;
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(changed);
            return toggle;
        }

        static float S2() => UiTheme.S2;

        static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.MidlineLeft;
            }
        }

        /// <summary>给容器加纵向排列。</summary>
        public static VerticalLayoutGroup VBox(RectTransform rt, float spacing, RectOffset padding,
            bool fitHeight = false)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset();
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            if (fitHeight)
            {
                var f = rt.gameObject.AddComponent<ContentSizeFitter>();
                f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            return v;
        }

        /// <summary>给容器加横向排列。</summary>
        public static HorizontalLayoutGroup HBox(RectTransform rt, float spacing, RectOffset padding,
            bool fitWidth = false)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding ?? new RectOffset();
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;

            if (fitWidth)
            {
                var f = rt.gameObject.AddComponent<ContentSizeFitter>();
                f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            return h;
        }

        /// <summary>固定尺寸的占位块（给布局组用，避免文本把容器撑变形）。</summary>
        public static LayoutElement Size(RectTransform rt, float w = -1f, float h = -1f)
        {
            var le = rt.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            if (w >= 0f) le.preferredWidth = w;
            if (h >= 0f)
            {
                var label = rt.GetComponent<TMP_Text>();
                if (label != null) h = Mathf.Max(h, label.fontSize * 1.45f);
                if (rt.GetComponent<Button>() != null || rt.GetComponent<TMP_InputField>() != null)
                    h = Mathf.Max(h, 56f);
                le.preferredHeight = h;
            }
            return le;
        }
    }

    /// <summary>统一的克制按钮反馈：悬停 1.02，按下 0.97，禁用时不响应。</summary>
    [DisallowMultipleComponent]
    public sealed class UiButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        const float HoverScale = 1.02f;
        const float PressedScale = 0.97f;
        const float Duration = 0.12f;

        Button _button;
        Vector3 _target = Vector3.one;

        void Awake() => _button = GetComponent<Button>();

        void Update()
        {
            float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime * (5f / Duration));
            transform.localScale = Vector3.Lerp(transform.localScale, _target, t);
        }

        public void OnPointerEnter(PointerEventData _) => Set(HoverScale);
        public void OnPointerExit(PointerEventData _) => Set(1f);
        public void OnPointerDown(PointerEventData _) => Set(PressedScale);
        public void OnPointerUp(PointerEventData _) => Set(HoverScale);

        void Set(float value)
        {
            _target = _button == null || _button.interactable ? Vector3.one * value : Vector3.one;
        }
    }

    /// <summary>设置框统一的 0.18 秒淡入与轻微缩放动画。</summary>
    [DisallowMultipleComponent]
    public sealed class UiModalAnimator : MonoBehaviour
    {
        CanvasGroup _group;
        Coroutine _routine;
        float _targetScale = 1f;
        bool _opening;

        void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        }

        public void Show()
        {
            gameObject.SetActive(true);
            Play(true);
        }

        public void Hide() => Play(false);

        public void SetTargetScale(float scale)
        {
            scale = Mathf.Max(0.01f, scale);
            if (Mathf.Approximately(_targetScale, scale)) return;
            _targetScale = scale;
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = StartCoroutine(Animate(_opening));
            }
            else
                transform.localScale = Vector3.one * (gameObject.activeSelf && _group != null && _group.alpha > 0.001f
                    ? _targetScale : _targetScale * 0.96f);
        }

        void Play(bool opening)
        {
            _opening = opening;
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Animate(opening));
        }

        System.Collections.IEnumerator Animate(bool opening)
        {
            float duration = 0.18f;
            float elapsed = 0f;
            float fromAlpha = _group.alpha;
            float toAlpha = opening ? 1f : 0f;
            Vector3 fromScale = transform.localScale;
            Vector3 toScale = Vector3.one * _targetScale * (opening ? 1f : 0.96f);
            if (opening && fromAlpha <= 0.001f)
            {
                fromScale = Vector3.one * _targetScale * 0.96f;
                transform.localScale = fromScale;
            }
            _group.interactable = opening;
            _group.blocksRaycasts = opening;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                _group.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
                transform.localScale = Vector3.Lerp(fromScale, toScale, t);
                yield return null;
            }
            _group.alpha = toAlpha;
            transform.localScale = Vector3.one * _targetScale * (opening ? 1f : 0.96f);
            if (!opening) gameObject.SetActive(false);
            _routine = null;
        }
    }

    /// <summary>把 RectTransform 约束到 Screen.safeArea，并监听窗口/方向变化。</summary>
    [DisallowMultipleComponent]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rect;
        Rect _lastArea;
        Vector2Int _lastScreen;

        void Awake()
        {
            _rect = transform as RectTransform;
            Apply();
        }

        void OnEnable() => Apply();

        void Update()
        {
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (_lastArea != Screen.safeArea || _lastScreen != screen) Apply();
        }

        void Apply()
        {
            if (_rect == null || Screen.width <= 0 || Screen.height <= 0) return;

            Rect area = Screen.safeArea;
            _rect.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            _rect.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
            _lastArea = area;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
        }
    }

    /// <summary>窗口尺寸变化时同步 CanvasScaler 的桌面断点。</summary>
    [DisallowMultipleComponent]
    public sealed class AdaptiveCanvasScaler : MonoBehaviour
    {
        public CanvasScaler scaler;
        Vector2Int _lastScreen;

        void Awake()
        {
            if (scaler == null) scaler = GetComponent<CanvasScaler>();
            Apply();
        }

        void Update()
        {
            if (_lastScreen != new Vector2Int(Screen.width, Screen.height)) Apply();
        }

        void Apply()
        {
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
            if (scaler == null) return;
            scaler.referenceResolution = UiKit.ReferenceResolution;
            scaler.matchWidthOrHeight = UiKit.IsMobile ? 0.15f : 0.5f;
        }
    }
}
