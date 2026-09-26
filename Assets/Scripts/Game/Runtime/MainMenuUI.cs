using BombTavern.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 开始界面（IMGUI，无需 TextMeshPro 资源）。
    /// 负责：昵称、皮肤选择、单人 / 创建房间 / 加入房间。
    /// 场景：Assets/Scenes/BombTavern_Menu.unity
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuUI : MonoBehaviour
    {
        [Header("场景")]
        [Tooltip("点击开始后加载的对局场景名。")]
        public string playSceneName = "BombTavern_Play";

        [Header("配色")]
        public Color panelColor = new Color(0.06f, 0.06f, 0.09f, 0.90f);
        public Color accentColor = new Color(0.98f, 0.76f, 0.30f);
        public Color selectColor = new Color(0.98f, 0.76f, 0.30f);

        string _name = "红雷";
        string _address = "127.0.0.1";
        int _skinIndex;

        Font _font;
        GUIStyle _panel, _title, _label, _button, _buttonSelected;
        Texture2D _panelTex;

        void Start()
        {
            // 真菜单已经搬到对局场景里了（PreGameMenu：中央真场景 + 四角功能面板），
            // 因为只有在对局场景里才能让「正在进行的对局」当背景。
            // 本场景现在只作为启动入口，负责带一下路。
            SceneManager.LoadScene(playSceneName);
        }

        void Awake()
        {
            var ctx = SessionContext.Instance;
            if (ctx != null)
            {
                _name = ctx.playerName;
                _address = ctx.connectAddress;

                if (ctx.skinCatalog != null && ctx.skinCatalog.skins != null)
                {
                    for (int i = 0; i < ctx.skinCatalog.skins.Count; i++)
                    {
                        var s = ctx.skinCatalog.skins[i];
                        if (s != null && s.skinId == ctx.skinId) { _skinIndex = i; break; }
                    }
                }
            }
        }

        void EnsureStyles()
        {
            if (_font == null)
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" }, 16);

            if (_panel != null) return;

            if (_panelTex == null)
            {
                _panelTex = new Texture2D(1, 1);
                _panelTex.SetPixel(0, 0, panelColor);
                _panelTex.Apply();
            }

            _panel = new GUIStyle(GUI.skin.box) { font = _font, fontSize = 15 };
            _panel.normal.background = _panelTex;
            _panel.normal.textColor = Color.white;
            _panel.padding = new RectOffset(18, 18, 14, 14);

            _title = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 30, fontStyle = FontStyle.Bold };
            _title.normal.textColor = accentColor;

            _label = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 15 };
            _label.normal.textColor = new Color(0.90f, 0.91f, 0.95f);

            _button = new GUIStyle(GUI.skin.button) { font = _font, fontSize = 16 };
            _button.normal.textColor = Color.white;
            _button.padding = new RectOffset(12, 12, 9, 9);

            _buttonSelected = new GUIStyle(_button);
            _buttonSelected.normal.textColor = selectColor;
            _buttonSelected.fontStyle = FontStyle.Bold;
        }

        void OnGUI()
        {
            EnsureStyles();

            float w = 520f, h = 430f;
            var rect = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);

            GUILayout.BeginArea(rect, _panel);
            GUILayout.Label("炸弹酒馆 · 概率扫雷", _title);
            GUILayout.Space(6);
            GUILayout.Label("四只炸弹围着桌子玩扫雷：概率会漂移，颜色会撒谎。", _label);
            GUILayout.Space(14);

            GUILayout.Label("昵称", _label);
            _name = GUILayout.TextField(_name, 16, GUILayout.Height(28));
            GUILayout.Space(10);

            GUILayout.Label("本局角色（四名固定炸弹，开局随机分配座位）", _label);
            DrawRoster();

            GUILayout.Space(12);

            if (GUILayout.Button("单 人 游 戏", _button, GUILayout.Height(38)))
                StartGame(GameMode.SinglePlayer);

            GUILayout.Space(6);

            if (GUILayout.Button("创 建 房 间（主机）", _button, GUILayout.Height(34)))
                StartGame(GameMode.LanHost);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("主机 IP", _label, GUILayout.Width(62));
            _address = GUILayout.TextField(_address, 24, GUILayout.Height(28));
            if (GUILayout.Button("加入房间", _button, GUILayout.Height(28), GUILayout.Width(120)))
                StartGame(GameMode.LanClient);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            if (GUILayout.Button("退出", _button, GUILayout.Height(28)))
                Application.Quit();

            GUILayout.EndArea();
        }

        /// <summary>
        /// 展示四名固定角色。这里只做预览 —— 实际座位由开局时的种子确定性随机分配，
        /// 所以不是可选列表，而是「本局会出现的四个角色」。
        /// </summary>
        void DrawRoster()
        {
            var ctx = SessionContext.Instance;
            if (ctx == null || ctx.skinCatalog == null || ctx.skinCatalog.skins.Count == 0)
            {
                GUILayout.Label("（未配置角色目录）", _label);
                return;
            }

            var skins = ctx.skinCatalog.skins;
            GUILayout.BeginHorizontal();

            for (int i = 0; i < skins.Count; i++)
            {
                var s = skins[i];
                if (s == null) continue;

                var prev = GUI.backgroundColor;
                GUI.backgroundColor = s.bodyColor;

                GUILayout.BeginVertical(_panel, GUILayout.Width(112), GUILayout.Height(74));
                GUILayout.Space(2);
                GUILayout.Label(s.displayName, _label);
                GUILayout.Space(2);
                GUI.backgroundColor = s.gloveColor;
                GUILayout.Box("", GUILayout.Height(10));
                GUI.backgroundColor = prev;
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }

            GUILayout.EndHorizontal();
        }

        void StartGame(GameMode mode)
        {
            var ctx = SessionContext.Instance;
            if (ctx == null)
            {
                Debug.LogError("[开始界面] 缺少 SessionContext。");
                return;
            }

            ctx.playerName = string.IsNullOrEmpty(_name) ? "无名炸弹" : _name;
            ctx.mode = mode;
            ctx.connectAddress = string.IsNullOrEmpty(_address) ? "127.0.0.1" : _address;

            var catalog = ctx.skinCatalog;
            if (catalog != null && catalog.skins != null && _skinIndex >= 0 && _skinIndex < catalog.skins.Count)
            {
                var s = catalog.skins[_skinIndex];
                if (s != null) ctx.skinId = s.skinId;
            }

            SceneManager.LoadScene(playSceneName);
        }
    }
}
