using System.Collections.Generic;
using BombTavern.Config;
using BombTavern.Core;
using UnityEngine;

namespace BombTavern.Runtime
{
    // 说明：格子碰撞代理 CellTile 单独放在 CellTile.cs 中。
    // Unity 要求 MonoBehaviour 的类名与文件名一致，
    // 写在本文件里会导致组件的脚本引用丢失（missing script），进而损坏场景数据。

    /// <summary>
    /// 扫雷屏。默认是酒馆中央那块**所有玩家共用**的大屏；
    /// 颜色只由公共显示的 DisplayedRisk 决定，真实概率漂移后显示值不变，
    /// 偏差大的格子会轻微抖动，提示"这条信息已经过期了"。
    /// 另外会渲染当前行动者的悬停标记（使用道具时不会有任何提示）。
    /// </summary>
    /// <summary>选择指针显示在哪些面板上。</summary>
    public enum SeatMarkerScope
    {
        [Tooltip("只画在「做选择那个人自己」的面板上")]
        OwnPanelOnly,
        [Tooltip("所有面板都画（同一根指针会同时出现多份）")]
        AllPanels,
        [Tooltip("只画在本地玩家自己的面板上")]
        ViewerPanelOnly,
    }

    [DisallowMultipleComponent]
    public class TabletScreen : MonoBehaviour
    {
        [Header("材质")]
        [Tooltip("格子底色材质。由装配器指向工程内的材质资产；为空时退回运行时创建。")]
        public Material tileMaterial;

        [Tooltip("底座材质。为空时退回运行时创建。")]
        public Material slabMaterial;

        [Header("布局")]
        [Tooltip("单格边长（米）。")]
        public float cellSize = 0.052f;

        [Tooltip("格子之间的缝隙（米）。")]
        public float cellGap = 0.006f;

        [Tooltip("屏幕边框的额外留白（米）。")]
        public float bezelMargin = 0.030f;

        [Tooltip("底座厚度（米）。")]
        public float slabThickness = 0.016f;

        [Tooltip("屏幕下方为血量灯泡预留的灯条深度（米）。" +
                 "有了这条留白，灯泡才能嵌进面板里，而不是浮在盘面外面。")]
        public float lampStripDepth = 0.082f;

        [Header("表现")]
        [Tooltip("未翻开格子的自发光强度。")]
        [Range(0f, 3f)] public float emissiveIntensity = 1.15f;

        [Tooltip("已翻开格子的自发光强度。")]
        [Range(0f, 3f)] public float revealedEmissive = 0.05f;

        [Tooltip("是否对「信息过期」的格子做抖动提示。")]
        public bool animateStale = true;

        [Tooltip("抖动幅度（米）。")]
        public float staleShakeAmplitude = 0.0016f;

        [Header("悬停标记")]
        [Tooltip("是否为其他人的悬停显示标记。")]
        public bool showRemoteHover = true;

        [Tooltip("别人落下操作后，他的卡通印记还会在格子上停留多久（秒）。")]
        [Range(0f, 3f)] public float committedHoldSeconds = 1.6f;

        [Tooltip("二维选择标记相对格子的尺寸倍率。略大于 1 会稍微压住相邻格边，但远看更清楚。")]
        [Range(0.4f, 1.6f)] public float markerIconScale = 1.24f;

        [Tooltip("二维标记沿屏幕法线漂浮的高度（米）。让它是「浮在格子上方」而不是贴在格面上。")]
        [Range(0.01f, 0.15f)] public float markerFloatHeight = 0.042f;

        [Tooltip("落下操作后的上下浮动幅度（米）。")]
        [Range(0f, 0.02f)] public float markerBobAmplitude = 0.005f;

        [Header("选择标记作用范围")]
        [Tooltip("一根选择指针该画在哪些面板上。\n" +
                 "OwnPanelOnly：只画在「做选择那个人自己」的面板上（默认，每人一根，不会到处重复）。\n" +
                 "AllPanels：所有面板都画（会同时出现四份同样的指针）。\n" +
                 "ViewerPanelOnly：只画在本地玩家自己的面板上。")]
        public SeatMarkerScope markerScope = SeatMarkerScope.ViewerPanelOnly;

        [Header("私人情报标记")]
        [Tooltip("是否在自己面板上给「已探明的格子」加标记。没有它，用了探查道具几乎看不出变化。")]
        public bool showPrivateIntel = true;

        [Tooltip("情报标记相对格子的尺寸倍率。")]
        [Range(0.25f, 1.2f)] public float intelIconScale = 0.60f;

        [Tooltip("情报标记沿视线方向漂浮的高度（米）。")]
        [Range(0.005f, 0.12f)] public float intelFloatHeight = 0.020f;

        [Tooltip("刚获得情报时的强调时长（秒）。")]
        [Range(0f, 10f)] public float intelFlashSeconds = 2.6f;

        [Tooltip("情报标记的重新扫描间隔（秒）。")]
        [Range(0.05f, 2f)] public float intelRescanInterval = 0.2f;

        [Tooltip("悬停标记的边框粗细（相对格子边长）。")]
        [Range(0.05f, 0.5f)] public float hoverFrameThickness = 0.18f;

        [Tooltip("本地自己的悬停抬升高度（米）。")]
        public float localHoverLift = 0.004f;

        public GameDirector Director { get; private set; }
        public RiskBoard Board { get; private set; }

        /// <summary>私有盘面模式下盘面归属的玩家；共用盘面为 null。</summary>
        public PlayerState Owner { get; private set; }

        public int OwnerSeat => Owner != null ? Owner.seatIndex : -1;
        public bool IsShared => Owner == null;

        [Header("身份")]
        [Tooltip("这块面板属于哪个座位（场景装配时写入）。-1 表示所有玩家共用的大屏。")]
        public int panelSeatIndex = -1;

        /// <summary>
        /// 面板归属座位。共用盘面模式下 Owner 是 null，靠 OwnerSeat 分不出四块面板，
        /// 所以必须用装配时写入的 panelSeatIndex。
        /// </summary>
        public int PanelSeat => panelSeatIndex >= 0 ? panelSeatIndex : OwnerSeat;

        /// <summary>
        /// 这块面板是不是「坐在 localSeat 上的人面前那一块」。
        /// 别人的面板不允许被指向、不允许显示信息、也不允许操作。
        /// </summary>
        public bool IsLocalPanel(int localSeat)
        {
            int seat = PanelSeat;
            if (seat < 0) return true;      // 共用大屏人人可用
            return seat == localSeat;
        }

        /// <summary>
        /// 取某一格的世界坐标。
        /// 特效要落在「被作用的那一格」上，而不是笼统地在平板中心放一团 ——
        /// 后者玩家根本看不出道具作用在哪。
        /// </summary>
        public bool TryGetCellWorldPosition(int x, int y, out Vector3 world)
        {
            world = default;
            for (int i = 0; i < _tiles.Count; i++)
            {
                var t = _tiles[i];
                if (t == null || t.x != x || t.y != y) continue;

                world = t.transform.position;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 幽灵视角：本地玩家已经出局。
        ///
        /// 判据放在这里而不是缓存成字段 —— 出局状态会随快照整份覆盖而改变，
        /// 缓存就要在每条同步路径上记得刷新，漏一处就会出现「死了还在用活人视角」。
        /// </summary>
        public bool GhostMode
        {
            get
            {
                var s = Director != null ? Director.Session : null;
                if (s == null || Director == null) return false;

                var me = s.PlayerAt(Director.localSeat);
                return me != null && me.eliminated;
            }
        }

        readonly List<CellTile> _tiles = new List<CellTile>();
        Transform _tileRoot;
        Transform _seatMarkerRoot;
        Transform _localSelectionMarker;
        Renderer[] _localSelectionRenderers;
        GameObject _slab;
        MaterialPropertyBlock _mpb;
        bool _built;

        // ---- 每座位一套印记：方框 + 卡通徽章（用该玩家的标识色与徽记形状）
        readonly List<Transform> _seatMarkers = new List<Transform>();
        readonly List<Renderer[]> _seatMarkerRenderers = new List<Renderer[]>();
        readonly int[] _seatMarkX = new int[GameSession.MaxSeats];
        readonly int[] _seatMarkY = new int[GameSession.MaxSeats];
        readonly float[] _seatMarkUntil = new float[GameSession.MaxSeats];

        // 本地悬停（自己）
        int _localHoverX = -1;
        int _localHoverY = -1;

        static Material _fallbackTileMaterial;
        static Material _fallbackSlabMaterial;
        static Shader _cachedShader;

        static Shader BaseShader
        {
            get
            {
                if (_cachedShader == null) _cachedShader = Shader.Find("Standard");
                return _cachedShader;
            }
        }

        /// <summary>
        /// 实际使用的格子材质：优先用装配器指定的资产，
        /// 没有时才运行时兜底创建（运行时 new 出来的材质若被写进场景会导致序列化损坏）。
        /// </summary>
        Material ActiveTileMaterial
        {
            get
            {
                if (tileMaterial != null) return tileMaterial;

                if (_fallbackTileMaterial == null)
                {
                    _fallbackTileMaterial = new Material(BaseShader)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    _fallbackTileMaterial.EnableKeyword("_EMISSION");
                    _fallbackTileMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    _fallbackTileMaterial.SetFloat("_Glossiness", 0.22f);
                    _fallbackTileMaterial.SetFloat("_Metallic", 0f);
                }
                return _fallbackTileMaterial;
            }
        }

        Material ActiveSlabMaterial
        {
            get
            {
                if (slabMaterial != null) return slabMaterial;

                if (_fallbackSlabMaterial == null)
                {
                    _fallbackSlabMaterial = new Material(BaseShader)
                    {
                        color = new Color(0.11f, 0.11f, 0.14f),
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    _fallbackSlabMaterial.SetFloat("_Glossiness", 0.45f);
                    _fallbackSlabMaterial.SetFloat("_Metallic", 0.35f);
                }
                return _fallbackSlabMaterial;
            }
        }

        // ============================================================ 绑定
        /// <summary>共用盘面：整桌共用一块屏。</summary>
        public void BindShared(GameDirector director, RiskBoard board)
        {
            HookDirector(director);
            Board = board;
            Owner = null;
            Rebuild();
        }

        /// <summary>订阅对局事件，用来记录「谁刚在哪一格落下操作」。</summary>
        void HookDirector(GameDirector director)
        {
            if (Director != null) Director.Event -= OnDirectorEvent;
            Director = director;
            if (Director != null) Director.Event += OnDirectorEvent;
        }

        void OnDestroy()
        {
            if (Director != null) Director.Event -= OnDirectorEvent;
        }

        /// <summary>
        /// 记下某个座位最近的公开落子位置。
        /// 道具产生的 CellProbed / CellRiskModified 不能进入这里，
        /// 否则别人会从彩色框反推出私密道具的作用格。
        /// </summary>
        void OnDirectorEvent(GameEvent e)
        {
            switch (e.type)
            {
                case GameEventType.CellRevealed:
                case GameEventType.CellHit:
                case GameEventType.CellFlagged:
                    break;
                default:
                    return;
            }

            int seat = e.seatIndex;
            if (seat < 0 || seat >= _seatMarkX.Length) return;

            _seatMarkX[seat] = e.x;
            _seatMarkY[seat] = e.y;
            _seatMarkUntil[seat] = Time.time + committedHoldSeconds;
        }

        /// <summary>私有盘面：每个座位一块屏。</summary>
        public void BindPrivate(GameDirector director, PlayerState player)
        {
            HookDirector(director);
            Owner = player;
            Board = player != null ? player.board : null;
            Rebuild();
        }

        public void Rebuild()
        {
            if (Board == null) return;

            ClearBuilt();

            float step = cellSize + cellGap;
            float width = Board.Columns * step - cellGap;
            float depth = Board.Rows * step - cellGap;

            // ---- 底座
            _slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _slab.name = IsShared ? "SharedTabletSlab" : $"TabletSlab_seat{OwnerSeat}";
            _slab.transform.SetParent(transform, false);
            // 底座在 +Z 方向多留一条 lampStripDepth，专供血量灯泡。
            // 底座整体往 +Z 偏移半个灯条，盘面本身仍以原点为中心，坐标不用改。
            float slabZ = depth + bezelMargin * 2f + Mathf.Max(0f, lampStripDepth);
            _slab.transform.localPosition = new Vector3(0f, -slabThickness * 0.5f, lampStripDepth * 0.5f);
            _slab.transform.localScale = new Vector3(
                width + bezelMargin * 2f, slabThickness, slabZ);
            _slab.GetComponent<Renderer>().sharedMaterial = ActiveSlabMaterial;
            var slabCol = _slab.GetComponent<Collider>();
            if (slabCol != null) DestroyImmediateSafe(slabCol);

            // ---- 格子
            _tileRoot = new GameObject("Tiles").transform;
            _tileRoot.SetParent(transform, false);
            _mpb ??= new MaterialPropertyBlock();
            _tiles.Clear();

            for (int y = 0; y < Board.Rows; y++)
            {
                for (int x = 0; x < Board.Columns; x++)
                {
                    float px = (x * step) - (width * 0.5f - cellSize * 0.5f);
                    float pz = (y * step) - (depth * 0.5f - cellSize * 0.5f);

                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Cell_{x}_{y}";
                    go.transform.SetParent(_tileRoot, false);
                    go.transform.localPosition = new Vector3(px, 0f, pz);
                    go.transform.localScale = new Vector3(cellSize, 0.010f, cellSize);

                    var r = go.GetComponent<Renderer>();
                    r.sharedMaterial = ActiveTileMaterial;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = true;

                    var tile = go.AddComponent<CellTile>();
                    tile.ownerSeat = PanelSeat;
                    tile.x = x;
                    tile.y = y;
                    tile.tileRenderer = r;
                    _tiles.Add(tile);
                }
            }

            BuildSeatMarkers();
            BuildLocalSelectionMarker();
            _built = true;
            RefreshAll();
        }

        /// <summary>
        /// 为每个座位建立一枚纯色方框印记。
        /// 方框颜色取该角色的标识色；中心完全留空，避免遮住格子的风险颜色。
        /// </summary>
        void BuildSeatMarkers()
        {
            _seatMarkers.Clear();
            _seatMarkerRenderers.Clear();

            int seatCount = Director != null && Director.Session != null
                ? Director.Session.Players.Length
                : 4;

            var root = new GameObject("SeatMarkers").transform;
            root.SetParent(transform, false);
            _seatMarkerRoot = root;

            // 二维标记：所有零件都是「薄片」，靠 billboard 永远正对相机，
            // 于是整体读起来是浮在格子上方的一块平面图形，而不是一圈立体方框。
            float size = Mathf.Max(0.016f, cellSize * markerIconScale);
            const float depth = 0.0022f;
            float edge = Mathf.Max(0.002f, size * 0.20f);

            for (int seat = 0; seat < seatCount; seat++)
            {
                var marker = new GameObject($"SeatMark_{seat}").transform;
                marker.SetParent(root, false);
                marker.gameObject.SetActive(false);

                var parts = new List<Renderer>(4);
                float h = size * 0.5f;

                // 只保留四条外框。旧版还在中央叠了角色徽章和下方指针，
                // 远看像「框中又套框」，也遮住了最重要的格子风险色。
                AddBar(marker, "EdgeT", new Vector3(0f, h - edge * 0.5f, 0f), new Vector3(size, edge, depth), parts);
                AddBar(marker, "EdgeB", new Vector3(0f, -h + edge * 0.5f, 0f), new Vector3(size, edge, depth), parts);
                AddBar(marker, "EdgeL", new Vector3(-h + edge * 0.5f, 0f, 0f), new Vector3(edge, size, depth), parts);
                AddBar(marker, "EdgeR", new Vector3(h - edge * 0.5f, 0f, 0f), new Vector3(edge, size, depth), parts);

                _seatMarkers.Add(marker);
                _seatMarkerRenderers.Add(parts.ToArray());
            }
        }

        /// <summary>
        /// 本地预选虚框：八段短边围出四个角，鼠标只要对准格子就显示，
        /// 真正提交后再由角色手部播放点按动作。
        /// </summary>
        void BuildLocalSelectionMarker()
        {
            var marker = new GameObject("LocalSelection").transform;
            marker.SetParent(transform, false);
            marker.gameObject.SetActive(false);
            _localSelectionMarker = marker;

            float size = Mathf.Max(0.016f, cellSize * 1.12f);
            float h = size * 0.5f;
            float edge = Mathf.Max(0.0018f, size * 0.10f);
            float dash = size * 0.30f;
            const float thickness = 0.0024f;
            var parts = new List<Renderer>(8);

            AddBar(marker, "DashTL_H", new Vector3(-h + dash * 0.5f, h - edge * 0.5f, 0f), new Vector3(dash, edge, thickness), parts);
            AddBar(marker, "DashTR_H", new Vector3(h - dash * 0.5f, h - edge * 0.5f, 0f), new Vector3(dash, edge, thickness), parts);
            AddBar(marker, "DashBL_H", new Vector3(-h + dash * 0.5f, -h + edge * 0.5f, 0f), new Vector3(dash, edge, thickness), parts);
            AddBar(marker, "DashBR_H", new Vector3(h - dash * 0.5f, -h + edge * 0.5f, 0f), new Vector3(dash, edge, thickness), parts);
            AddBar(marker, "DashTL_V", new Vector3(-h + edge * 0.5f, h - dash * 0.5f, 0f), new Vector3(edge, dash, thickness), parts);
            AddBar(marker, "DashTR_V", new Vector3(h - edge * 0.5f, h - dash * 0.5f, 0f), new Vector3(edge, dash, thickness), parts);
            AddBar(marker, "DashBL_V", new Vector3(-h + edge * 0.5f, -h + dash * 0.5f, 0f), new Vector3(edge, dash, thickness), parts);
            AddBar(marker, "DashBR_V", new Vector3(h - edge * 0.5f, -h + dash * 0.5f, 0f), new Vector3(edge, dash, thickness), parts);
            _localSelectionRenderers = parts.ToArray();
        }

        /// <summary>卡通徽章：浮在格子上方的小牌子，形状对应角色的印记造型。</summary>
        void BuildBadge(Transform marker, int seat, List<Renderer> parts, float size, float depth)
        {
            var skin = Director != null ? Director.SkinOf(seat) : null;
            var shape = skin != null ? skin.emblem : BombEmblemShape.Disc;

            float s = Mathf.Max(0.008f, size * 0.50f);

            switch (shape)
            {
                case BombEmblemShape.Ring:
                {
                    // 方环：四条边围出空心，和实心方块拉开区分度
                    float t = Mathf.Max(0.0015f, s * 0.30f);
                    float hs = s * 0.5f;
                    AddBar(marker, "RingT", new Vector3(0f, hs - t * 0.5f, 0f), new Vector3(s, t, depth), parts);
                    AddBar(marker, "RingB", new Vector3(0f, -hs + t * 0.5f, 0f), new Vector3(s, t, depth), parts);
                    AddBar(marker, "RingL", new Vector3(-hs + t * 0.5f, 0f, 0f), new Vector3(t, s, depth), parts);
                    AddBar(marker, "RingR", new Vector3(hs - t * 0.5f, 0f, 0f), new Vector3(t, s, depth), parts);
                    break;
                }

                case BombEmblemShape.Cross:
                    AddBar(marker, "BadgeV", Vector3.zero, new Vector3(s * 0.34f, s, depth), parts);
                    AddBar(marker, "BadgeH", Vector3.zero, new Vector3(s, s * 0.34f, depth), parts);
                    break;

                default:
                    AddBar(marker, "BadgeDisc", Vector3.zero, new Vector3(s, s, depth), parts);
                    break;
            }
        }

        void AddBar(Transform parent, string name, Vector3 pos, Vector3 scale, List<Renderer> parts)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Strip(go, parts);
        }

        void AddCyl(Transform parent, string name, Vector3 pos, Vector3 scale, List<Renderer> parts)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Strip(go, parts);
        }

        void Strip(GameObject go, List<Renderer> parts)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediateSafe(col);

            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = ActiveTileMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            parts.Add(r);
        }

        void ClearBuilt()
        {
            _tiles.Clear();
            _seatMarkers.Clear();
            _seatMarkerRenderers.Clear();

            // 只销毁自己建的那几棵子树。
            //
            // 这里原来是无脑清空 transform 的所有子物体 —— 那会顺手删掉挂在同一个
            // GameObject 上其它组件建的节点（血量灯泡的 LampRoot 就是这么被删没的），
            // 结果灯泡永远不显示。TabletScreen 与 HealthLamps 共用一个物体，
            // 这条边界必须守住。
            DestroyOwned(ref _slab);
            DestroyOwned(ref _tileRoot);
            DestroyOwned(ref _seatMarkerRoot);

            _built = false;
        }

        static void DestroyOwned(ref GameObject go)
        {
            if (go != null) DestroyImmediateSafe(go);
            go = null;
        }

        static void DestroyOwned(ref Transform tr)
        {
            if (tr != null) DestroyImmediateSafe(tr.gameObject);
            tr = null;
        }

        static void DestroyImmediateSafe(Object go)
        {
            if (go == null) return;
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        // ============================================================ 刷新
        public void RefreshAll()
        {
            if (!_built || Board == null) return;
            for (int i = 0; i < _tiles.Count; i++) ApplyTile(_tiles[i]);
        }

        public void RefreshCell(int x, int y)
        {
            if (!_built || Board == null) return;
            if (!Board.InBounds(x, y)) return;
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i].x == x && _tiles[i].y == y)
                {
                    ApplyTile(_tiles[i]);
                    return;
                }
            }
        }

        void ApplyTile(CellTile tile)
        {
            var cell = Board.Get(tile.x, tile.y);
            var ramp = Director != null && Director.config != null ? Director.config.colorRamp : null;

            Color color;
            float emission;
            // 自发光单独一个通道：底色压到焦黑之后，color*emission 也是一片黑，
            // 余烬红光必须由自己的颜色给出，不能从底色乘出来。
            Color? emissionOverride = null;

            if (cell.blasted)
            {
                // 炸过的格子：暖调焦黑 + 暗红余烬。
                //
                // 刻意**不用**踩雷者的标识色去染色：标识色是白/红/棕/灰，
                // 染出来会落在「未知」或某段色带的色域里，玩家会误读成
                // 「这格还没定论」。这里要传达的唯一语义是「这儿是个雷，别碰」，
                // 所以取暖黑（r>g>b，和已翻开的冷灰蓝正好相反）+ 余烬红光，
                // 既不落在绿→红的概率色带上，也不和「安全翻开」混。
                color = new Color(0.055f, 0.042f, 0.040f);
                emission = 1f;
                emissionOverride = new Color(0.62f, 0.10f, 0.05f) * emissiveIntensity;
            }
            else if (cell.revealed)
            {
                color = ramp != null ? ramp.revealedColor : new Color(0.16f, 0.15f, 0.19f);
                emission = revealedEmissive;
            }
            else
            {
                // 观战与存活玩家看同一份公开概率，不泄露真实风险。
                float shown = cell.flagged ? 1f : cell.displayedRisk;

                color = ramp != null ? ramp.Evaluate(shown) : Color.Lerp(Color.green, Color.red, shown);
                if (cell.flagged) color *= 0.35f;

                emission = emissiveIntensity;
            }

            var r = tile.tileRenderer;
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_Color", color);
            _mpb.SetColor("_EmissionColor", emissionOverride ?? (color * emission));
            r.SetPropertyBlock(_mpb);
        }

        // ============================================================ 悬停
        /// <summary>本地玩家把鼠标移到了某格（用于抬升反馈）。传 -1 表示移开。</summary>
        public void SetLocalHover(int x, int y)
        {
            _localHoverX = x;
            _localHoverY = y;
        }

        public bool TryGetLocalHover(out int x, out int y)
        {
            x = _localHoverX;
            y = _localHoverY;
            return _localHoverX >= 0 && _localHoverY >= 0;
        }

        void Update()
        {
            if (!_built || Board == null) return;

            UpdateStaleShake();
            UpdateLocalSelectionMarker();
            UpdateSeatMarkers();
            UpdatePrivateIntel();
        }

        void UpdateLocalSelectionMarker()
        {
            if (_localSelectionMarker == null || Board == null) return;

            bool visible = _localHoverX >= 0 && _localHoverY >= 0 &&
                           Board.InBounds(_localHoverX, _localHoverY) &&
                           Director != null && Director.Session != null &&
                           Director.Session.IsMyTurn(Director.localSeat);
            if (!visible)
            {
                if (_localSelectionMarker.gameObject.activeSelf)
                    _localSelectionMarker.gameObject.SetActive(false);
                return;
            }

            float step = cellSize + cellGap;
            float width = Board.Columns * step - cellGap;
            float depth = Board.Rows * step - cellGap;
            float px = (_localHoverX * step) - (width * 0.5f - cellSize * 0.5f);
            float pz = (_localHoverY * step) - (depth * 0.5f - cellSize * 0.5f);
            var cellWorld = transform.TransformPoint(new Vector3(px, 0f, pz));
            var cam = Camera.main;
            Vector3 outward = cam != null ? cam.transform.position - cellWorld : -transform.forward;
            if (outward.sqrMagnitude < 1e-6f) outward = -transform.forward;
            outward.Normalize();

            Vector3 pos = cellWorld + outward * (markerFloatHeight * 0.72f);
            _localSelectionMarker.position = pos;
            if (cam != null)
                _localSelectionMarker.rotation = Quaternion.LookRotation(pos - cam.transform.position, cam.transform.up);
            if (!_localSelectionMarker.gameObject.activeSelf)
                _localSelectionMarker.gameObject.SetActive(true);

            Color c = new Color(1f, 0.86f, 0.42f);
            float glow = 1.8f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 5f));
            for (int i = 0; i < _localSelectionRenderers.Length; i++)
            {
                var r = _localSelectionRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_Color", c);
                _mpb.SetColor("_EmissionColor", c * glow);
                r.SetPropertyBlock(_mpb);
            }
        }

        // ============================================================ 私人情报标记
        // 探查类道具只写 knownTruth，既不翻格子也不改显示值 —— 所以「用了跟没用一样」。
        // 这里把 knownTruth 可视化：在格子上方浮一个小方牌，牌心颜色取自同一套风险配色，
        // 于是「探明值」直接可读。只在本人面板上显示，别人的面板不受影响。
        Transform _intelRoot;
        readonly Dictionary<int, Transform> _intel = new Dictionary<int, Transform>();
        readonly Dictionary<int, Renderer> _intelDots = new Dictionary<int, Renderer>();
        readonly Dictionary<int, Renderer[]> _intelFrames = new Dictionary<int, Renderer[]>();
        readonly Dictionary<int, float> _intelFlashUntil = new Dictionary<int, float>();
        float _intelTimer;

        void UpdatePrivateIntel()
        {
            int localSeat = Director != null ? Director.localSeat : -1;
            if (!showPrivateIntel || localSeat < 0 || Board == null) return;
            // 只看自己那块面板；共用一个座位时不重复画
            if (panelSeatIndex >= 0 && panelSeatIndex != localSeat) return;

            _intelTimer += Time.deltaTime;
            if (_intelTimer < intelRescanInterval) return;
            _intelTimer = 0f;

            var session = Director.Session;
            if (session == null) return;

            if (_intelRoot == null)
            {
                _intelRoot = new GameObject("PrivateIntel").transform;
                _intelRoot.SetParent(transform, false);
            }

            var cam = Camera.main;
            float now = Time.time;
            float step = cellSize + cellGap;
            float width = Board.Columns * step - cellGap;
            float depth = Board.Rows * step - cellGap;

            for (int i = 0; i < _tiles.Count; i++)
            {
                var tile = _tiles[i];
                float known = session.KnownTruthOf(localSeat, tile.x, tile.y);
                int key = tile.y * Board.Columns + tile.x;

                var cellAt = Board.Get(tile.x, tile.y);
                // 炸过的格子已经自带更强的语义（这儿是个雷），情报标记会让它变糊
                bool should = known >= 0f && !cellAt.revealed && !cellAt.blasted;

                if (!should)
                {
                    if (_intel.TryGetValue(key, out var stale) && stale != null && stale.gameObject.activeSelf)
                        stale.gameObject.SetActive(false);
                    continue;
                }

                if (!_intel.TryGetValue(key, out var badge) || badge == null)
                {
                    badge = BuildIntelBadge(key);
                    _intel[key] = badge;
                    _intelFlashUntil[key] = now + intelFlashSeconds;
                }

                if (!badge.gameObject.activeSelf) badge.gameObject.SetActive(true);

                float px = (tile.x * step) - (width * 0.5f - cellSize * 0.5f);
                float pz = (tile.y * step) - (depth * 0.5f - cellSize * 0.5f);
                var cellWorld = transform.TransformPoint(new Vector3(px, 0f, pz));

                Vector3 outward = cam != null ? (cam.transform.position - cellWorld) : -transform.forward;
                if (outward.sqrMagnitude < 1e-6f) outward = -transform.forward;
                outward.Normalize();

                var pos = cellWorld + outward * intelFloatHeight;
                badge.position = pos;
                if (cam != null)
                    badge.rotation = Quaternion.LookRotation(pos - cam.transform.position, cam.transform.up);

                // 刚拿到情报的格子亮一会儿，避免玩家错过「道具生效了」这一下
                bool flashing = _intelFlashUntil.TryGetValue(key, out float until) && until > now;
                float pulse = flashing ? 1.8f + 1.6f * Mathf.Abs(Mathf.Sin(now * 8f)) : 1.4f;

                if (_intelDots.TryGetValue(key, out var dot) && dot != null)
                {
                    var ramp = Director.config != null ? Director.config.colorRamp : null;
                    Color c = ramp != null ? ramp.Evaluate(known) : Color.Lerp(Color.green, Color.red, known);
                    dot.GetPropertyBlock(_mpb);
                    _mpb.SetColor("_Color", c);
                    _mpb.SetColor("_EmissionColor", c * pulse);
                    dot.SetPropertyBlock(_mpb);
                }

                if (_intelFrames.TryGetValue(key, out var frame) && frame != null)
                {
                    Color edge = new Color(0.30f, 0.95f, 1f);      // 青色 = 「这条情报是你自己的」
                    for (int k = 0; k < frame.Length; k++)
                    {
                        var r = frame[k];
                        if (r == null) continue;
                        r.GetPropertyBlock(_mpb);
                        _mpb.SetColor("_Color", edge);
                        _mpb.SetColor("_EmissionColor", edge * (flashing ? 4.2f : 2.0f));
                        r.SetPropertyBlock(_mpb);
                    }
                }
            }
        }

        /// <summary>情报标记：青色空心方框 + 用风险配色着色的实心小方块。</summary>
        Transform BuildIntelBadge(int key)
        {
            var root = new GameObject("Intel").transform;
            root.SetParent(_intelRoot, false);

            const float depthThickness = 0.0020f;
            float size = Mathf.Max(0.010f, cellSize * intelIconScale);
            float edge = Mathf.Max(0.0016f, size * 0.18f);
            float h = size * 0.5f;

            var frame = new List<Renderer>(4);
            AddBar(root, "IntelT", new Vector3(0f, h - edge * 0.5f, 0f), new Vector3(size, edge, depthThickness), frame);
            AddBar(root, "IntelB", new Vector3(0f, -h + edge * 0.5f, 0f), new Vector3(size, edge, depthThickness), frame);
            AddBar(root, "IntelL", new Vector3(-h + edge * 0.5f, 0f, 0f), new Vector3(edge, size, depthThickness), frame);
            AddBar(root, "IntelR", new Vector3(h - edge * 0.5f, 0f, 0f), new Vector3(edge, size, depthThickness), frame);
            _intelFrames[key] = frame.ToArray();

            var dotParts = new List<Renderer>(1);
            AddBar(root, "IntelDot", Vector3.zero, new Vector3(size * 0.46f, size * 0.46f, depthThickness), dotParts);
            _intelDots[key] = dotParts[0];

            return root;
        }

        void UpdateStaleShake()
        {
            if (!animateStale) return;

            var ramp = Director != null && Director.config != null ? Director.config.colorRamp : null;
            float freq = ramp != null ? ramp.staleShakeFrequency : 0f;
            float threshold = ramp != null ? ramp.staleHintThreshold : 0.08f;
            if (freq <= 0f || threshold <= 0f) return;

            float t = Time.time * freq;

            for (int i = 0; i < _tiles.Count; i++)
            {
                var tile = _tiles[i];
                var cell = Board.Get(tile.x, tile.y);
                var tr = tile.transform;
                var p = tr.localPosition;

                float lift = (tile.x == _localHoverX && tile.y == _localHoverY) ? localHoverLift : 0f;

                float stale = Mathf.Abs(cell.Staleness);
                float shake = 0f;
                if (!cell.revealed && stale > threshold)
                {
                    float amount = Mathf.InverseLerp(threshold, 1f, stale) * staleShakeAmplitude;
                    shake = Mathf.Sin(t + (tile.x * 7 + tile.y * 13) * 0.37f) * amount;
                }

                float y = lift + shake;
                if (Mathf.Abs(p.y - y) > 0.000001f)
                    tr.localPosition = new Vector3(p.x, y, p.z);
            }
        }

        /// <summary>这根指针该不该画在当前这块面板上。</summary>
        bool MarkerAllowedOnThisPanel(int seat)
        {
            switch (markerScope)
            {
                case SeatMarkerScope.AllPanels:
                    return true;

                case SeatMarkerScope.ViewerPanelOnly:
                {
                    int localSeat = Director != null ? Director.localSeat : -1;
                    return panelSeatIndex < 0 || panelSeatIndex == localSeat;
                }

                default:   // OwnPanelOnly
                    // panelSeatIndex < 0 表示共享大屏，那种情况下没有「谁的面板」，允许显示
                    return panelSeatIndex < 0 || panelSeatIndex == seat;
            }
        }

        /// <summary>
        /// 刷新每个座位的印记位置：
        /// - 当前行动者正在看的格子 → 实时显示，其他人由此知道他在选哪
        /// - 刚刚落下操作的格子 → 保留一小段时间，让人看清是谁动的手
        /// 本地自己的印记不画（自己已有抬升与信息板反馈），避免视觉噪音。
        /// </summary>
        void UpdateSeatMarkers()
        {
            if (_seatMarkers.Count == 0 || Board == null) return;

            var s = Director != null ? Director.Session : null;
            if (s == null)
            {
                for (int i = 0; i < _seatMarkers.Count; i++)
                    if (_seatMarkers[i] != null) _seatMarkers[i].gameObject.SetActive(false);
                return;
            }

            float step = cellSize + cellGap;
            float width = Board.Columns * step - cellGap;
            float depth = Board.Rows * step - cellGap;
            float now = Time.time;
            var cam = Camera.main;      // 每帧取一次就够，别在循环里反复 FindGameObjectWithTag

            for (int seat = 0; seat < _seatMarkers.Count; seat++)
            {
                var marker = _seatMarkers[seat];
                if (marker == null) continue;

                // 本地玩家已有金色虚线预选框；彩色框只用来跟踪其他玩家。
                if (Director != null && seat == Director.localSeat)
                {
                    if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
                    continue;
                }

                // 一根指针只画在它该在的那块面板上。
                // 之前每块面板都会画所有座位的指针，于是同一次选择在四块屏幕上各出现一份。
                if (!MarkerAllowedOnThisPanel(seat))
                {
                    if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
                    continue;
                }

                int mx = -1, my = -1;
                bool committed = false;

                // 不再要求「正好是他的回合」：主机在回合切换时会清掉公开的悬停，
                // 所以只要 HoverSeat 还有值就说明那个人此刻确实在选这一格。
                // 这样别人（包括 AI）扫描格子时的选择过程在所有面板上都看得见。
                bool isRemoteHover = showRemoteHover && s.HoverSeat == seat &&
                                     seat != Director.localSeat;

                if (isRemoteHover)
                {
                    mx = s.HoverX;
                    my = s.HoverY;
                }
                else if (seat < _seatMarkUntil.Length && _seatMarkUntil[seat] > now)
                {
                    mx = _seatMarkX[seat];
                    my = _seatMarkY[seat];
                    committed = true;
                }

                if (mx < 0 || my < 0 || !Board.InBounds(mx, my))
                {
                    if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
                    continue;
                }

                if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);

                float px = (mx * step) - (width * 0.5f - cellSize * 0.5f);
                float pz = (my * step) - (depth * 0.5f - cellSize * 0.5f);

                // 先定位到格心，再**朝相机方向**抬起。
                //
                // 这里不能用 transform.forward：面板的 +Z 是 LookRotation(dir) 给出的「朝外」，
                // 而玩家坐在面板外侧，也就是从面板的 -Z 面看过来。
                // 沿 +Z 抬起等于把图标抬到屏幕背面去，结果就是被格子挡在下面。
                // 朝相机抬起则永远在当前观察者的这一侧。
                float lift = markerFloatHeight + (committed ? Mathf.Sin(now * 6f) * markerBobAmplitude : 0f);
                var cellWorld = transform.TransformPoint(new Vector3(px, 0f, pz));

                Vector3 outward = cam != null ? (cam.transform.position - cellWorld) : -transform.forward;
                if (outward.sqrMagnitude < 1e-6f) outward = -transform.forward;
                outward.Normalize();

                var lifted = cellWorld + outward * lift;
                marker.position = lifted;

                // 始终正对相机：一块平面图形，任何角度看都是同一个二维形状
                if (cam != null)
                    marker.rotation = Quaternion.LookRotation(lifted - cam.transform.position, cam.transform.up);

                // 标识色统一取自角色皮肤，与 HUD 卡片、角色手套一致
                Color c = Director != null ? Director.ColorOfSeat(seat) : Color.white;

                // 刚落子的印记做呼吸强调，便于注意到「刚才发生了什么」
                float glow = committed ? 1.4f + 1.6f * Mathf.Abs(Mathf.Sin(now * 9f)) : 2.2f;

                var renderers = _seatMarkerRenderers[seat];
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null) continue;
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetColor("_Color", c);
                    _mpb.SetColor("_EmissionColor", c * glow);
                    r.SetPropertyBlock(_mpb);
                }
            }
        }

        /// <summary>本地玩家是否是真人（联机时用于限制操作权限）。</summary>
        public bool IsLocalOwner() => Owner != null && !Owner.isBot;
    }
}
