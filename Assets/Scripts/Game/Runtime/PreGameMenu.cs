using System.Collections.Generic;
using BombTavern.Config;
using BombTavern.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 开局前的「观战式」主菜单。
    ///
    /// 布局按现在通用游戏的做法：**画面中央就是真场景** —— 四名角色在 AI 驱动下先打一局
    /// 当背景演示；功能面板贴四个角落，不遮挡中央画面。
    ///
    /// 为什么不做成独立的菜单场景：那边没有角色、没有盘面，只能摆一张静态图，
    /// 做不出「正在进行的对局」这个观感。
    ///
    /// 镜头：菜单期间接管第一人称相机做环绕，开局时把相机**还原回原位**再交还给控制器 ——
    /// 不还原的话相机会带着环绕期间累积的局部偏移，玩家一开局就是歪的。
    /// </summary>
    [DisallowMultipleComponent]
    public class PreGameMenu : MonoBehaviour
    {
        enum Mode { Single, Host, Join }

        [Header("绑定")]
        [Tooltip("对局驱动。留空则自动查找。")]
        public GameDirector director;

        [Tooltip("第一人称装置（菜单期间被接管相机）。留空则自动查找。")]
        public FirstPersonController rig;

        [Tooltip("盘面交互器（菜单期间禁用）。留空则自动查找。")]
        public TabletInteractor interactor;

        [Header("旁观机位")]
        [Tooltip("机位半径（米）。")]
        public float orbitRadius = 4.25f;

        [Tooltip("机位高度（米）。抬高一点，视野越过近处角色的肩膀看清整张桌子。")]
        public float orbitHeight = 2.95f;

        [Tooltip("看向目标的高度（米）。取桌面上方一点，让盘面与四名角色同时入画。")]
        public float lookAtHeight = 0.85f;

        [Tooltip("机位基准方位（度）。**要选在两个座位之间**（座位在 45/135/225/315），" +
                 "取 90 就落在 0 号与 1 号之间，四个人都不会堵在镜头正前方。")]
        public float baseAngle = 90f;

        [Tooltip("左右摆动幅度（度）。做成小幅摆动而不是整圈环绕 —— " +
                 "整圈转必然有一段时间正对某个角色的后背，画面会被糊满。")]
        public float swayDegrees = 16f;

        [Tooltip("摆动速度。")]
        public float orbitSpeed = 6.5f;

        [Header("配色")]
        public Color panelColor = new Color(0.05f, 0.05f, 0.08f, 0.90f);
        public Color accentColor = new Color(0.98f, 0.76f, 0.30f);

        Mode _mode = Mode.Single;
        bool _menu;
        bool _lobby;
        LanNetworkManager _lan;
        float _swayPhase;

        string _playerName = "无名炸弹";
        string _address = "";
        string _port = "7788";

        Camera _cam;
        Vector3 _camLocalPos;
        Quaternion _camLocalRot;

        Font _font;
        GUIStyle _panel, _title, _label, _small, _button, _accent, _state, _rosterBtn;
        Texture2D _whiteTex;
        PreGameMenuUgui _uguiView;

        // UGUI 视图只通过这组窄接口读写菜单状态，流程控制仍由本类负责。
        // 这样后续替换视觉预制体时不会碰联机/开局逻辑。
        internal string PlayerName { get => _playerName; set => _playerName = value; }
        internal string Address { get => _address; set => _address = value; }
        internal string PortText { get => _port; set => _port = value; }
        internal int ModeIndex { get => (int)_mode; set => _mode = (Mode)Mathf.Clamp(value, 0, 2); }
        internal SkinCatalog SkinCatalog => SessionContext.Instance != null ? SessionContext.Instance.skinCatalog : null;
        internal string SelectedSkinId => SessionContext.Instance != null ? SessionContext.Instance.skinId : string.Empty;

        internal void SelectSkin(string skinId)
        {
            if (SessionContext.Instance != null && !string.IsNullOrEmpty(skinId))
                SessionContext.Instance.skinId = skinId;
        }

        void Awake()
        {
            if (director == null) director = FindObjectOfType<GameDirector>();
            if (rig == null) rig = FindObjectOfType<FirstPersonController>();
            if (interactor == null) interactor = FindObjectOfType<TabletInteractor>();

            if (rig != null) _cam = rig.viewCamera;

            if (_cam != null)
            {
                // 记下 FPS 相机的原始局部变换，开局时必须还原
                _camLocalPos = _cam.transform.localPosition;
                _camLocalRot = _cam.transform.localRotation;
            }
        }

        // 自动化验证用：-btMenuShot <路径> 在菜单稳定渲染后截一张图并退出
        string _shotPath;
        float _shotTimer;
        bool _shotTaken;

        void Start()
        {
            var ctx = SessionContext.Instance;
            var cli = NetCliOptions.Parse();

            // 无头自检（-btAutoplay）要的是「直接开局、跑完一整局」，不能停在菜单上
            if (HasArg("-btAutoplay"))
            {
                enabled = false;
                return;
            }

            var mode = cli.specified
                ? cli.mode
                : (ctx != null ? ctx.mode : GameMode.SinglePlayer);

            // 联机：**直接进对局场景**，用观战机位看着桌子等人。
            // 桌上先只显示房主自己，每进来一个人就多亮一个角色；
            // 开局时没坐满的座位由 AI 补上（StartSession 的 humans 参数负责）。
            if (mode != GameMode.SinglePlayer)
            {
                EnterLobby();
                return;
            }

            _shotPath = ReadArgValue("-btMenuShot");

            if (ctx != null)
            {
                if (!string.IsNullOrEmpty(ctx.playerName)) _playerName = ctx.playerName;
                if (!string.IsNullOrEmpty(ctx.connectAddress)) _address = ctx.connectAddress;
                if (ctx.port > 0) _port = ctx.port.ToString();
            }

            EnterMenu();
            BuildUgui();
        }

        void BuildUgui()
        {
            _uguiView = gameObject.AddComponent<PreGameMenuUgui>();
            _uguiView.Configure(this);
        }

        /// <summary>
        /// 关掉菜单界面本体。
        ///
        /// 必须单独做这一步：菜单视图是**独立组件** PreGameMenuUgui，
        /// 它自带 Canvas 与 Update()，把 PreGameMenu 自己 enabled=false 并不会
        /// 连带关掉它。原来的后果就是「进了对局，开始界面还盖在画面上」——
        /// 玩家看不到牌桌，也没法操作，只能以为游戏坏了。
        /// 联机大厅进对局走的是 ExitLobby()，同样要收掉。
        /// </summary>
        void HideUguiView()
        {
            if (_uguiView == null) return;
            _uguiView.gameObject.SetActive(false);
            Destroy(_uguiView);
            _uguiView = null;
        }

        void EnterMenu()
        {
            _menu = true;

            if (rig != null) rig.enabled = false;      // 相机交给菜单
            if (interactor != null) interactor.enabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (director != null) director.StartDemoSession();
        }

        /// <summary>演示局打完了就再开一局，保证背景一直是「进行中」而不是停在结算画面。</summary>
        void KeepDemoAlive()
        {
            if (director == null) return;
            if (director.Session == null || director.Session.IsOver)
                director.StartDemoSession();
        }

        // ============================================================ 联机大厅
        /// <summary>
        /// 联机进入方式：不弹选择菜单，直接站在观战机位看桌子等人。
        /// 桌上先只亮房主一个角色，别人进来一个亮一个。
        /// </summary>
        void EnterLobby()
        {
            _lobby = true;
            _lan = FindObjectOfType<LanNetworkManager>();

            if (director != null)
            {
                director.lobbyMode = true;
                director.lobbyOccupiedSeats = new HashSet<int> { director.localSeat };
                director.RefreshCharacterVisibility();
            }

            // 镜头交给观战环绕；盘面交互关掉（这时还没有盘面可点）
            if (rig != null) rig.enabled = false;
            if (interactor != null) interactor.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// 每帧把「谁已经就座」同步给显示层：
        /// 花名册里出现的座位才渲染角色，所以来一个人桌上就多亮一个。
        /// </summary>
        void UpdateLobby()
        {
            if (director == null) return;

            var seats = director.lobbyOccupiedSeats;
            if (seats == null)
            {
                seats = new HashSet<int>();
                director.lobbyOccupiedSeats = seats;
            }

            seats.Clear();
            seats.Add(director.localSeat);        // 自己一定在

            if (_lan != null && _lan.RosterList != null)
            {
                for (int i = 0; i < _lan.RosterList.Count; i++)
                    if (_lan.RosterList[i].seat >= 0) seats.Add(_lan.RosterList[i].seat);
            }

            director.RefreshCharacterVisibility();

            // 房主点了开始，或者客户端收到开局 → 交还镜头进入第一人称
            if (_lan != null && _lan.sessionStarted && director.Session != null)
                ExitLobby();
        }

        void ExitLobby()
        {
            _lobby = false;
            HideUguiView();

            if (director != null)
            {
                director.lobbyMode = false;
                director.lobbyOccupiedSeats = null;
                director.RefreshCharacterVisibility();   // 这时才隐藏本地角色
            }

            RestoreCamera();
            if (rig != null) rig.enabled = true;
            if (interactor != null) interactor.enabled = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            enabled = false;
        }

        static bool HasArg(string key)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], key, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string ReadArgValue(string key)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], key, System.StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        void HandleMenuShot()
        {
            if (string.IsNullOrEmpty(_shotPath) || _shotTaken) return;

            _shotTimer += Time.unscaledDeltaTime;
            if (_shotTimer < 3f) return;

            _shotTaken = true;
            ScreenCapture.CaptureScreenshot(_shotPath);
            Debug.Log($"[菜单] 已请求截图：{_shotPath}");
            _quitAt = Time.unscaledTime + 2.5f;
        }

        float _quitAt = -1f;

        void LateUpdate()
        {
            if (string.IsNullOrEmpty(_shotPath)) { }
            else
            {
                HandleMenuShot();
                if (_quitAt > 0f && Time.unscaledTime >= _quitAt) Application.Quit(0);
            }

            if (_menu) KeepDemoAlive();
            else if (_lobby) UpdateLobby();

            // 菜单阶段与联机大厅阶段共用同一套观战环绕机位
            if ((!_menu && !_lobby) || _cam == null) return;

            _swayPhase += Time.deltaTime * orbitSpeed * 0.16f;
            float a = (baseAngle + Mathf.Sin(_swayPhase) * swayDegrees) * Mathf.Deg2Rad;

            var target = new Vector3(0f, lookAtHeight, 0f);
            var eye = target + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * orbitRadius
                             + Vector3.up * (orbitHeight - lookAtHeight);

            _cam.transform.position = eye;
            _cam.transform.rotation = Quaternion.LookRotation((target - eye).normalized, Vector3.up);
        }

        /// <summary>界面点击音。UI 统一走这里，方便以后换音源或加静音开关。</summary>
        static void Click()
        {
            if (AudioDirector.Instance != null) AudioDirector.Instance.PlayUi();
        }

        // ============================================================ 开局
        internal void BeginGame()
        {
            Click();
            _menu = false;
            HideUguiView();

            var ctx = SessionContext.Instance;
            if (ctx != null)
            {
                ctx.playerName = string.IsNullOrWhiteSpace(_playerName) ? "无名炸弹" : _playerName.Trim();
                ctx.mode = ModeOf(_mode);
                ctx.connectAddress = string.IsNullOrWhiteSpace(_address) ? string.Empty : _address.Trim();
                ctx.port = int.TryParse(_port, out int p) && p > 0 ? p : 7788;
            }

            if (_mode != Mode.Single)
            {
                // 联机：重载本场景，让 NetBootstrap / LanNetworkManager 按
                // 刚写进 SessionContext 的配置起房或连房。菜单那一路会自行让位。
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            RestoreCamera();

            if (rig != null) rig.enabled = true;
            if (interactor != null) interactor.enabled = true;

            if (director != null) director.BeginRealGame();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            enabled = false;
        }

        /// <summary>把相机放回第一人称原位。不做这一步，开局视角会带着环绕的残留偏移。</summary>
        void RestoreCamera()
        {
            if (_cam == null) return;
            _cam.transform.localPosition = _camLocalPos;
            _cam.transform.localRotation = _camLocalRot;
        }

        static GameMode ModeOf(Mode m)
        {
            switch (m)
            {
                case Mode.Host: return GameMode.LanHost;
                case Mode.Join: return GameMode.LanClient;
                default: return GameMode.SinglePlayer;
            }
        }

        // ============================================================ 界面
        void EnsureStyles()
        {
            if (_font == null)
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" }, 16);

            if (_panel != null) return;

            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, panelColor);
            tex.Apply();

            // 纯白贴图，用来给角色色块上色（Box 不上色，只能自己画）
            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();

            _panel = new GUIStyle(GUI.skin.box) { font = _font, fontSize = UiTheme.FontBody };
            UiTheme.ApplyPanel(_panel);
            _panel.normal.textColor = UiTheme.TextHi;
            _panel.padding = UiTheme.PanelPadding;

            _title = new GUIStyle(GUI.skin.label)
            {
                font = _font,
                fontSize = UiTheme.FontTitle + 6,
                fontStyle = FontStyle.Bold,
            };
            _title.normal.textColor = UiTheme.Accent;

            _accent = new GUIStyle(GUI.skin.label)
            {
                font = _font,
                fontSize = UiTheme.FontHead - 2,
                fontStyle = FontStyle.Bold,
            };
            _accent.normal.textColor = UiTheme.Accent;

            _label = new GUIStyle(GUI.skin.label) { font = _font, fontSize = UiTheme.FontBody };
            _label.normal.textColor = UiTheme.TextMid;

            _small = new GUIStyle(GUI.skin.label)
            {
                font = _font,
                fontSize = UiTheme.FontCaption,
                wordWrap = true,
            };
            _small.normal.textColor = UiTheme.TextLo;

            _state = new GUIStyle(_small) { fontSize = UiTheme.FontBody, fontStyle = FontStyle.Bold };
            _state.normal.textColor = UiTheme.Accent;

            // 主行动按钮（开始游戏）用强调底图，和普通按钮拉开层级
            _button = new GUIStyle(GUI.skin.button)
            {
                font = _font,
                fontSize = UiTheme.FontHead + 2,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(16, 16, 14, 14),
            };
            UiTheme.ApplyButton(_button, accent: true);

            // 角色选择行：左对齐、字小一点，整行可点
            _rosterBtn = new GUIStyle(GUI.skin.button)
            {
                font = _font,
                fontSize = UiTheme.FontBody,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 8, 4, 4),
            };
            UiTheme.ApplyButton(_rosterBtn);
        }

        void OnGUI()
        {
            // 新版视图已由 UGUI 接管。保留下面的 IMGUI 代码仅作老场景回退，
            // 避免场景资源尚未重新生成时出现完全无菜单的情况。
            if (_uguiView != null) return;
            if (!_menu) return;
            EnsureStyles();

            float w = Screen.width;
            float h = Screen.height;

            DrawTitle(w);
            DrawRosterPanel();
            DrawNetPanel(w);
            DrawHelpPanel(w, h);
            DrawStartBar(w, h);
        }

        /// <summary>顶部中央标题。只占一条，不挡中央画面。</summary>
        void DrawTitle(float w)
        {
            const float tw = 420f;
            GUILayout.BeginArea(new Rect(w * 0.5f - tw * 0.5f, 16f, tw, 56f), _panel);
            GUILayout.Label("炸 弹 酒 馆", _title);
            GUILayout.EndArea();
        }

        /// <summary>
        /// 左上：选角色（换装）。
        /// 点哪只就用哪只；其余三只仍然按对局种子随机分给其他座位，
        /// 所以「四人各一只」的规则不会被破坏。
        /// </summary>
        void DrawRosterPanel()
        {
            GUILayout.BeginArea(new Rect(20f, 16f, 268f, 258f), _panel);
            GUILayout.Label("选择角色", _accent);

            var ctx = SessionContext.Instance;
            var catalog = ctx != null ? ctx.skinCatalog : null;

            if (catalog == null || catalog.skins == null || catalog.skins.Count == 0)
            {
                GUILayout.Label("（未配置角色目录）", _small);
            }
            else
            {
                for (int i = 0; i < catalog.skins.Count; i++)
                {
                    var s = catalog.skins[i];
                    if (s == null) continue;

                    bool picked = ctx != null && ctx.skinId == s.skinId;

                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = picked ? accentColor : Color.white;

                    // 用整行按钮而不是单选框：点角色名的任意位置都能换装
                    if (GUILayout.Button((picked ? "▶ " : "　 ") + s.displayName,
                            _rosterBtn, GUILayout.Height(28f)))
                    {
                        if (ctx != null) ctx.skinId = s.skinId;
                        Click();
                    }

                    GUI.backgroundColor = prev;
                }
            }

            GUILayout.Space(4);
            GUILayout.Label("选中的归你，其余三只随机分给其他座位。", _small);
            GUILayout.EndArea();
        }

        /// <summary>右上：联机设置。单机时收起，不干扰主流程。</summary>
        void DrawNetPanel(float w)
        {
            const float pw = 268f;
            GUILayout.BeginArea(new Rect(w - pw - 20f, 16f, pw, 232f), _panel);

            GUILayout.Label("联机", _accent);
            // 用按钮 + 背景高亮表示当前选中，比 Toggle 的默认样式更能一眼看出选的是哪个
            GUILayout.BeginHorizontal();
            if (ModeButton("单机", Mode.Single)) _mode = Mode.Single;
            if (ModeButton("开房", Mode.Host)) _mode = Mode.Host;
            if (ModeButton("加入", Mode.Join)) _mode = Mode.Join;
            GUILayout.EndHorizontal();

            if (_mode == Mode.Single)
            {
                GUILayout.Space(6);
                GUILayout.Label("与三名 AI 对局。开始后你坐上 0 号位。", _small);
            }
            else
            {
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUILayout.Label("端口", _label, GUILayout.Width(38f));
                _port = GUILayout.TextField(_port ?? "", GUILayout.Height(24f));
                GUILayout.EndHorizontal();

                if (_mode == Mode.Join)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("地址", _label, GUILayout.Width(38f));
                    _address = GUILayout.TextField(_address ?? "", GUILayout.Height(24f));
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Radmin VPN 填 26.x；内网穿透填 frp 给的域名。", _small);
                }
                else
                {
                    GUILayout.Label("开始后进入大厅，把界面上的地址念给对方。", _small);
                }
            }

            GUILayout.EndArea();
        }

        /// <summary>模式按钮：选中的那个底色高亮。</summary>
        bool ModeButton(string text, Mode m)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = _mode == m ? accentColor : Color.white;
            bool clicked = GUILayout.Button(text, _button, GUILayout.Height(34f));
            GUI.backgroundColor = prev;
            return clicked;
        }

        /// <summary>
        /// 右下：操作说明。
        /// 放右下而不是左下，是为了和左上的角色面板形成对角，
        /// 中间一大片留给场景，四个角各自独立、互不挤压。
        /// </summary>
        void DrawHelpPanel(float w, float h)
        {
            // 高度要放得下 5 条按键说明 + 换行的补充说明，否则最后一行会被裁掉
            const float pw = 330f;
            GUILayout.BeginArea(new Rect(w - pw - 20f, h - 236f, pw, 220f), _panel);
            GUILayout.Label("操作", _accent);
            GUILayout.Label("鼠标 —— 转动视角（坐姿，不能走动）", _label);
            GUILayout.Label("左键 —— 翻开指向的格子", _label);
            GUILayout.Label("右键 —— 在该格插旗", _label);
            GUILayout.Label("1 ~ 6 —— 使用道具", _label);
            GUILayout.Label("H —— 在「自己操作 / AI 代打」间切换", _label);
            GUILayout.Space(4);
            GUILayout.Label("四周的屏幕：中央绿盘是你自己的，其余三块属于对手。", _small);
            GUILayout.EndArea();
        }

        /// <summary>底部中央：名字 + 开始。主行动点，放正下方而不是中央。</summary>
        void DrawStartBar(float w, float h)
        {
            const float bw = 470f;
            const float bh = 112f;
            GUILayout.BeginArea(new Rect(w * 0.5f - bw * 0.5f, h - bh - 22f, bw, bh), _panel);

            GUILayout.BeginHorizontal();
            GUILayout.Label("名字", _label, GUILayout.Width(40f));
            _playerName = GUILayout.TextField(_playerName ?? "", GUILayout.Height(26f));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            string label = _mode == Mode.Host ? "创 建 房 间"
                         : _mode == Mode.Join ? "加 入 房 间"
                         : "开 始 游 戏";
            if (GUILayout.Button(label, _button, GUILayout.Height(40f))) BeginGame();

            GUILayout.EndArea();
        }
    }
}
