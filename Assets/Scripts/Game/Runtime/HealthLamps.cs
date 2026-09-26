using BombTavern.Core;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 血量灯泡：装在每个座位那块面板上沿的一组灯泡，亮几个就代表还剩几次机会。
    ///
    /// 做成实体建模而不是 HUD 上的几个圆点，是因为它是「桌面上看得见的公共信息」——
    /// 你坐在自己位置上就能一眼看出对面还剩几条命，不需要读 UI 文字。
    ///
    /// 灯灭时不是全黑，而是留一点余辉（dimEmission），否则暗掉的灯泡在暗场景里直接看不见，
    /// 玩家会分不清「这盏灭了」还是「这盏本来就不存在」。
    /// </summary>
    [DisallowMultipleComponent]
    public class HealthLamps : MonoBehaviour
    {
        [Header("绑定")]
        [Tooltip("对局驱动。留空则自动查找。")]
        public GameDirector director;

        [Tooltip("这组灯显示哪个座位的血量。留空(小于 0)则从同物体的 TabletScreen 推断。")]
        public int seatIndex = -1;

        [Header("造型")]
        [Tooltip("灯泡半径（米）。")]
        public float bulbRadius = 0.024f;

        [Tooltip("灯泡间距（米）。")]
        public float bulbSpacing = 0.070f;

        [Tooltip("灯罩的扁度（1 = 正球，越小越扁）。压扁一点更像指示灯而不是玻璃珠。")]
        [Range(0.4f, 1.2f)] public float bulbSquash = 0.9f;

        [Tooltip("灯罩凸出屏面的比例。0.6 表示约六成露在外面 —— " +
                 "凸出来别人才看得见，平贴的话从桌子对面只能看到一条边。")]
        [Range(0.2f, 1f)] public float bulbProtrusion = 0.6f;

        [Tooltip("额外抬高的距离（米）。0 表示按 bulbProtrusion 自动计算。")]
        public float heightOffset = 0f;

        [Header("亮度")]
        [Tooltip("点亮时的自发光倍率。过高会把颜色曝成白色，1.5~2.5 之间最稳。")]
        public float litEmission = 1.8f;

        [Tooltip("熄灭时的自发光倍率。保留一点余辉，否则暗灯会彻底看不见。")]
        public float dimEmission = 0.10f;

        [Tooltip("失去几点机会时灯泡变色（越少越危险）。")]
        public Color dangerColor = new Color(0.95f, 0.22f, 0.18f);

        Renderer[] _bulbs;
        Transform _lampRoot;
        MaterialPropertyBlock _mpb;
        int _lastLives = -1;
        bool _laidOut;

        void Awake()
        {
            if (director == null) director = FindObjectOfType<GameDirector>();
            if (seatIndex < 0)
            {
                var sibling = GetComponent<TabletScreen>();
                if (sibling != null) seatIndex = sibling.panelSeatIndex;
            }
            Build();
        }

        void Start() => Layout();

        void Build()
        {
            var tablet = GetComponent<TabletScreen>();
            _mpb = new MaterialPropertyBlock();
            _bulbs = new Renderer[3];

            // 灯组挂在自己的子物体上。
            // 千万不能直接动 transform：本组件和 TabletScreen 挂在同一个物体上，
            // 挪 transform 等于把整块面板挪走。
            _lampRoot = new GameObject("LampRoot").transform;
            _lampRoot.SetParent(transform, false);

            for (int i = 0; i < _bulbs.Length; i++)
            {
                // 用**球体**灯罩，不是平贴的碟片。
                //
                // 碟片几乎完全顺着屏面法线，坐在桌子对面看过去就是「侧着的一条线」，
                // 根本读不出亮了几盏 —— 血量应该是**全场公共信息**，
                // 只有自己能看见等于没做。球体各向同性，从任何方向都能读出亮/灭。
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Lamp_{i}";
                go.transform.SetParent(_lampRoot, false);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = new Vector3(
                    bulbRadius * 2f, bulbRadius * 2f * bulbSquash, bulbRadius * 2f);

                Strip(go, out var r);
                if (r != null && tablet != null && tablet.tileMaterial != null)
                    r.sharedMaterial = tablet.tileMaterial;
                _bulbs[i] = r;
            }
        }

        static void Strip(GameObject go, out Renderer renderer)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            }

            renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        /// <summary>
        /// 把灯组摆到盘面正上方。
        ///
        /// 面板的局部坐标轴很重要，弄错就会「灯泡飘在盘面中间」：
        ///   局部 X —— 盘面宽度方向
        ///   局部 Y —— **盘面法线**（底座就是沿 Y 压扁的，所以法线是 Y 不是 Z）
        ///   局部 Z —— 盘面高度方向，且 +Z 朝屏幕下方（行号越大越往下）
        /// 所以「盘面上方」= 沿 **-Z** 偏移；再沿 +Y 抬一点点让灯浮出表面。
        /// </summary>
        void Layout()
        {
            var tablet = GetComponent<TabletScreen>();
            if (tablet == null || tablet.Board == null) return;   // 开局前 Board 还是空的

            float step = tablet.cellSize + tablet.cellGap;
            float boardDepth = tablet.Board.Rows * step - tablet.cellGap;

            // 面板局部 **-Z = 屏幕上方**，也就是倾斜面板翘起来的那一端。
            // 灯必须装在这一端：另一端（+Z）低到接近桌面，会被桌子整个挡住。
            // 这里按「边框中央」定位，左右再按 bulbSpacing 排开。
            float stripZ = -(boardDepth * 0.5f + tablet.bezelMargin * 0.5f);

            // 球心高度：让约 bulbProtrusion 比例露在屏面之上。
            // 球在 Y 方向的半高是 bulbRadius * bulbSquash；
            // 球心放在 (2p-1) 倍半高处，露出的比例恰好是 p，同时底部仍嵌在板里不悬空。
            float halfHeight = bulbRadius * bulbSquash;
            float y = halfHeight * (2f * bulbProtrusion - 1f);
            if (heightOffset != 0f) y = heightOffset;

            if (_lampRoot != null) _lampRoot.localPosition = new Vector3(0f, y, stripZ);

            for (int i = 0; i < _bulbs.Length; i++)
            {
                var r = _bulbs[i];
                if (r == null) continue;
                float x = (i - (_bulbs.Length - 1) * 0.5f) * bulbSpacing;
                r.transform.localPosition = new Vector3(x, 0f, 0f);
            }

            _laidOut = true;
        }

        void Update()
        {
            if (director == null) return;

            // 防御性重建：万一灯组节点被别的东西清掉了（面板重建之类），自己补回来。
            // 根因已经在 TabletScreen.ClearBuilt 里修掉，这里只是保险。
            if (_lampRoot == null)
            {
                Build();
                _laidOut = false;
                _lastLives = -1;      // 强制重刷一次颜色
            }

            if (_bulbs == null) return;
            // 运行中脚本重载会保留场景对象，却会把非序列化字段恢复为 null。
            // 灯泡 Renderer 仍在，此时直接 GetPropertyBlock(null) 会每帧抛异常。
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            // 盘面一出现就摆一次位置（开局前 Board 为空，Layout 会直接返回）
            if (!_laidOut) Layout();

            var session = director.Session;
            var player = session != null ? session.PlayerAt(seatIndex) : null;
            int lives = player != null ? player.lives : -1;

            // 只有变化时才重写 PropertyBlock，避免每帧都产生材质开销
            if (lives == _lastLives) return;
            _lastLives = lives;

            int max = player != null ? Mathf.Max(1, player.maxLives) : _bulbs.Length;

            for (int i = 0; i < _bulbs.Length; i++)
            {
                var r = _bulbs[i];
                if (r == null) continue;

                bool exists = i < max;
                bool lit = i < lives;

                Color c;
                float emission;

                if (!exists)
                {
                    c = new Color(0.12f, 0.12f, 0.14f);
                    emission = 0f;
                }
                else if (lit)
                {
                    // 自己灯用角色标识色，越少越偏危险色
                    var seat = director.ColorOfSeat(seatIndex);
                    float t = max <= 1 ? 1f : 1f - (float)(lives - 1) / (max - 1);
                    c = Color.Lerp(seat, dangerColor, Mathf.Clamp01(t) * 0.55f);
                    emission = litEmission;
                }
                else
                {
                    c = new Color(0.10f, 0.10f, 0.12f);
                    emission = dimEmission;
                }

                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_Color", c);
                _mpb.SetColor("_EmissionColor", c * emission);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
