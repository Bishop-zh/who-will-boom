using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 全局设计语言。**所有界面都必须从这里取色、取字号、取间距。**
    ///
    /// 之前每个界面各自 `new Color(...)`、各自 `fontSize = 15/16/17`，
    /// 结果就是「能用但不像一个游戏」—— 缺的不是控件，是统一规范。
    ///
    /// 阶段说明：这一层是和渲染技术无关的（颜色 / 字号 / 间距都是纯数据），
    /// 所以现在服务于 IMGUI，将来迁到 UGUI 时同一套值可以直接复用，
    /// 迁移不会推翻视觉规范。
    /// </summary>
    public static class UiTheme
    {
        // ============================================================ 色板（表面）
        //
        // 主题：**做旧黄铜酒馆**。
        //
        // 之前那套是中性冷灰 + 通用金 —— 这正是「AI 生成味」的来源：
        // 冷灰是任何 UI 框架的默认值，纯金是任何「高级感」模板的默认选择，
        // 两者叠加就是没有任何身份。这里换成有明确出处的材质色：
        // 深色胡桃木、做旧黄铜、油灯下的暖白。
        //
        // 所有表面都带一点**暖偏**（R > G > B），即使是「黑」也是暖黑，
        // 整屏才会有一致的色温，而不是拼起来的灰块。

        /// <summary>最底层背景（全屏遮罩）。暖黑。</summary>
        public static readonly Color Surface0 = new Color32(20, 12, 9, 235);

        /// <summary>面板背景。深胡桃木。</summary>
        public static readonly Color Surface1 = new Color32(33, 21, 15, 240);

        /// <summary>面板上的次级块（卡片、格子）。</summary>
        public static readonly Color Surface2 = new Color32(51, 33, 22, 247);

        /// <summary>更亮的块（激活态）。</summary>
        public static readonly Color Surface3 = new Color32(75, 45, 25, 250);

        /// <summary>描边 / 分隔线。做旧黄铜。</summary>
        public static readonly Color Line = new Color32(201, 151, 61, 150);

        // ============================================================ 色板（文本）
        // 暖白而不是纯白：纯白在暖色面板上会显得刺眼、像贴上去的
        public static readonly Color TextHi = new Color32(245, 232, 207, 255);
        public static readonly Color TextMid = new Color32(190, 170, 139, 255);
        public static readonly Color TextLo = new Color(0.520f, 0.470f, 0.396f);
        public static readonly Color TextDisabled = new Color(0.360f, 0.322f, 0.268f);

        // ============================================================ 色板（语义）
        /// <summary>主色：亮黄铜（灯下的金属高光）。</summary>
        public static readonly Color Accent = new Color32(201, 151, 61, 255);

        /// <summary>悬停、选中与主行动按钮的黄铜高光。</summary>
        public static readonly Color AccentHi = new Color32(240, 195, 91, 255);

        /// <summary>危险：信号红（不是纯红，压暗一点更像油漆而不是荧光）。</summary>
        public static readonly Color Danger = new Color32(217, 83, 69, 255);

        /// <summary>安全：做旧橄榄绿。</summary>
        public static readonly Color Success = new Color32(114, 183, 122, 255);

        /// <summary>信息：窑蓝。</summary>
        public static readonly Color Info = new Color(0.400f, 0.596f, 0.824f);

        /// <summary>警告：油灯橙。</summary>
        public static readonly Color Warn = new Color(0.925f, 0.588f, 0.239f);

        /// <summary>幽灵视角：冷蓝，刻意跳出暖色主题以表示「这是另一个视角」。</summary>
        public static readonly Color Ghost = new Color(0.549f, 0.749f, 0.965f);

        // ============================================================ 字阶（4 级）
        public const int FontTitle = 38;
        public const int FontHead = 28;
        public const int FontBody = 23;
        public const int FontCaption = 20;

        // ============================================================ 间距（8px 栅格）
        public const float S1 = 6f;
        public const float S2 = 12f;
        public const float S3 = 16f;
        public const float S4 = 24f;
        public const float S5 = 32f;
        public const float S6 = 44f;

        // ============================================================ 布局常量
        /// <summary>
        /// 顶部轮次条的高度。
        /// 抽成常量是必须的：它下面还压着「操作权提示」和横幅，
        /// 各自写死偏移的话，一改内边距就会互相重叠。
        /// </summary>
        public const float RoundBarHeight = 96f;

        /// <summary>玩家卡的高度（含内边距）。</summary>
        public const float PlayerCardHeight = 76f;

        /// <summary>面板内边距。</summary>
        public static readonly RectOffset PanelPadding = new RectOffset(28, 28, 24, 24);

        /// <summary>卡片内边距。</summary>
        public static readonly RectOffset CardPadding = new RectOffset(18, 18, 12, 12);

        // ============================================================ 圆角面板贴图
        const int PanelTexSize = 48;
        const int PanelRadius = 14;
        const int PanelBorderWidth = 2;

        static Texture2D _panelTex;
        static Texture2D _cardTex;
        static Texture2D _whiteTex;

        public static Texture2D White
        {
            get
            {
                if (_whiteTex == null) _whiteTex = SolidTex(Color.white);
                return _whiteTex;
            }
        }

        /// <summary>主面板底图：**切角** + 黄铜描边。</summary>
        public static Texture2D PanelTexture
        {
            get
            {
                if (_panelTex == null)
                    _panelTex = ChamferTex(Surface1, Line, PanelRadius, PanelBorderWidth);
                return _panelTex;
            }
        }

        /// <summary>卡片底图：切角更小、更亮一点。</summary>
        public static Texture2D CardTexture
        {
            get
            {
                if (_cardTex == null)
                    _cardTex = ChamferTex(Surface2, Line, 10, 2);
                return _cardTex;
            }
        }

        /// <summary>
        /// 切角矩形贴图。
        ///
        /// **用 45° 斜切代替圆角**：圆角是任何 UI 框架的默认形状，
        /// 满屏圆角矩形就是「AI 生成味」最典型的特征。切角是靶场标牌、
        /// 军械箱、老式仪表的共同视觉符号，有明确的出处，一眼就能记住。
        ///
        /// 判据用曼哈顿距离 dx+dy —— 它的等值线正好是 45° 斜线，
        /// 比欧氏距离的圆弧更「硬」，也更容易在小尺寸下保持形状。
        /// </summary>
        public static Texture2D ChamferTex(Color fill, Color border, int chamfer, int borderWidth)
        {
            int n = chamfer * 2 + 4;
            int last = n - 1;

            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Max(chamfer - x, x - (last - chamfer), 0);
                    float dy = Mathf.Max(chamfer - y, y - (last - chamfer), 0);
                    float d = dx + dy;                       // 曼哈顿距离 → 45° 斜切

                    Color c;
                    if (d > chamfer) c = new Color(0f, 0f, 0f, 0f);
                    else if (d > chamfer - borderWidth) c = border;
                    else c = fill;

                    // 斜边做 1px 过渡，避免锯齿
                    float edge = chamfer - d;
                    if (edge >= 0f && edge < 1f) c.a *= edge;

                    t.SetPixel(x, y, c);
                }
            }

            t.Apply();
            return t;
        }

        /// <summary>把面板底图套到任意 GUIStyle 上（含 9 宫格边框，圆角不会被拉伸）。</summary>
        public static void ApplyPanel(GUIStyle style, Texture2D tex = null, int radius = PanelRadius)
        {
            if (style == null) return;
            style.normal.background = tex != null ? tex : PanelTexture;
            style.border = new RectOffset(radius, radius, radius, radius);
        }

        // ============================================================ 按钮
        const int BtnRadius = 9;

        static Texture2D _btnTex;
        static Texture2D _btnAccentTex;

        /// <summary>普通按钮底图。</summary>
        public static Texture2D ButtonTexture
        {
            get
            {
                if (_btnTex == null) _btnTex = ChamferTex(Surface3, Line, BtnRadius, 2);
                return _btnTex;
            }
        }

        /// <summary>强调按钮底图（主行动点，例如「开始游戏」）。</summary>
        public static Texture2D AccentButtonTexture
        {
            get
            {
                if (_btnAccentTex == null)
                {
                    var fill = new Color(Accent.r * 0.55f, Accent.g * 0.55f, Accent.b * 0.5f, 0.96f);
                    _btnAccentTex = ChamferTex(fill, new Color(Accent.r, Accent.g, Accent.b, 0.85f), BtnRadius, 2);
                }
                return _btnAccentTex;
            }
        }

        /// <summary>
        /// 把按钮做成三态（常态 / 悬停 / 按下）。
        /// IMGUI 的按钮只有在三态都有底图时才会有回馈，只设 normal 会「按下去没反应」。
        /// </summary>
        public static void ApplyButton(GUIStyle style, bool accent = false)
        {
            if (style == null) return;

            var baseTex = accent ? AccentButtonTexture : ButtonTexture;
            style.normal.background = baseTex;
            style.hover.background = baseTex;
            style.active.background = baseTex;
            style.border = new RectOffset(BtnRadius, BtnRadius, BtnRadius, BtnRadius);

            style.normal.textColor = accent ? Color.white : TextHi;
            style.hover.textColor = accent ? Color.white : Color.white;
            style.active.textColor = accent ? new Color(1f, 0.94f, 0.8f) : TextMid;

            // 悬停/按下用整体着色区分，不需要另外三张贴图
            style.hover.textColor = Color.white;
        }

        // ============================================================ 生成
        static Texture2D SolidTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.DontSave;
            return t;
        }

        /// <summary>
        /// 生成一张圆角矩形贴图，用于 9 宫格拉伸。
        /// radius 必须与 <see cref="ApplyPanel"/> 传的 border 一致，否则圆角会被拉变形。
        ///
        /// 公开出来是为了让 UGUI 侧复用同一张图 —— 两种渲染技术必须长得一样，
        /// 各自生成一份迟早会走样。
        /// </summary>
        public static Texture2D RoundedTex(Color fill, Color border, int radius, int borderWidth)
        {
            int n = radius * 2 + 4;   // 留一点中间区域给拉伸
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    // 到圆角中心的最短距离：贴在角内侧就是圆，越靠边越接近直线
                    float dx = Mathf.Max(radius - x, x - (n - 1 - radius), 0);
                    float dy = Mathf.Max(radius - y, y - (n - 1 - radius), 0);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    Color c;
                    if (d > radius) c = new Color(0f, 0f, 0f, 0f);                     // 圆角外：透明
                    else if (d > radius - borderWidth) c = border;                      // 描边
                    else c = fill;                                                      // 填充

                    // 边缘 1px 做半透明过渡，避免锯齿
                    float edge = radius - d;
                    if (edge >= 0f && edge < 1f) c.a *= edge;

                    t.SetPixel(x, y, c);
                }
            }

            t.Apply();
            return t;
        }
    }
}
