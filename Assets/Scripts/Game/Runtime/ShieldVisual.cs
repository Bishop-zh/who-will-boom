using BombTavern.Core;
using LiarsBar;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 护盾的视觉表现：一圈发光小球绕着角色缓慢旋转。
    ///
    /// 为什么用「一圈小球」而不是一个半透明球罩：
    /// 透明需要专门的着色器与渲染队列设置，而这里所有材质都是不透明的工程内资产。
    /// 用一圈**不透明自发光小球**，既能表达「有一层东西护着」，
    /// 又不需要引入任何新的渲染管线假设。
    ///
    /// 数量随剩余护盾次数变化（1 次 = 6 颗，2 次 = 12 颗…），所以看一眼就知道护了几层。
    /// 被打掉一层时整圈闪一下 —— 这是「护盾真的挡下了」的唯一可见证据。
    /// </summary>
    [DisallowMultipleComponent]
    public class ShieldVisual : MonoBehaviour
    {
        [Header("绑定")]
        public GameDirector director;

        [Tooltip("这是哪个座位的护盾。")]
        public int seatIndex = -1;

        [Header("造型")]
        [Tooltip("光环半径（米）。")]
        public float radius = 0.60f;

        [Tooltip("光环离角色原点的高度（米）。")]
        public float height = 0.42f;

        [Tooltip("每一层护盾对应的光点数量。")]
        public int dotsPerCharge = 6;

        [Tooltip("光点半径（米）。")]
        public float dotRadius = 0.035f;

        [Tooltip("旋转角速度（度/秒）。")]
        public float spinSpeed = 55f;

        [Tooltip("光点上下起伏的幅度（米）。")]
        public float bobAmplitude = 0.035f;

        [Header("配色")]
        public Color shieldColor = new Color(0.42f, 0.72f, 1f);

        [Tooltip("点亮时的自发光倍率。")]
        public float litEmission = 2.1f;

        const int MaxDots = 18;

        Transform _root;
        Renderer[] _dots;
        MaterialPropertyBlock _mpb;
        int _lastCharges = -1;
        float _spin;
        float _pulseUntil;

        void Awake()
        {
            if (director == null) director = FindObjectOfType<GameDirector>();
            if (seatIndex < 0)
            {
                var c = GetComponent<BombCharacter>();
                if (c != null) seatIndex = c.seatIndex;
            }

            BuildRing();
        }

        void BuildRing()
        {
            _mpb = new MaterialPropertyBlock();
            _dots = new Renderer[MaxDots];

            _root = new GameObject("ShieldRing").transform;
            _root.SetParent(transform, false);
            _root.localPosition = new Vector3(0f, height, 0f);

            for (int i = 0; i < MaxDots; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"ShieldDot_{i}";
                go.transform.SetParent(_root, false);

                var col = go.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
                }

                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _dots[i] = r;

                // 先全部关掉，等 Update 里按实际护盾层数打开
                go.SetActive(false);
            }
        }

        void Update()
        {
            if (director == null || _dots == null) return;

            var s = director.Session;
            var p = s != null ? s.PlayerAt(seatIndex) : null;
            int charges = p != null ? Mathf.Max(0, p.shieldCharges) : 0;

            if (charges != _lastCharges)
            {
                // 层数减少 = 刚挡下一次攻击 → 闪一下
                if (_lastCharges > 0 && charges < _lastCharges) _pulseUntil = Time.time + 0.45f;
                _lastCharges = charges;
                ApplyCount(charges);
            }

            if (charges <= 0) return;

            _spin += spinSpeed * Time.deltaTime;

            bool pulsing = Time.time < _pulseUntil;
            float pulse = pulsing ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 26f)) : 1f;

            float visible = charges * dotsPerCharge;
            for (int i = 0; i < _dots.Length; i++)
            {
                var r = _dots[i];
                if (r == null || !r.gameObject.activeSelf) continue;

                // 只给「实际参与构成这一层」的光点更新颜色，其余保持暗态
                float max = visible;
                bool active = i < max;
                float bob = Mathf.Sin(Time.time * 2.2f + i * 0.7f) * bobAmplitude;
                r.transform.localPosition = new Vector3(
                    r.transform.localPosition.x,
                    bob,
                    r.transform.localPosition.z);

                Color c = active ? shieldColor : new Color(0.10f, 0.14f, 0.20f);
                float e = active ? litEmission * pulse : 0.05f;

                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_Color", c);
                _mpb.SetColor("_EmissionColor", c * e);
                r.SetPropertyBlock(_mpb);
            }

            _root.localRotation = Quaternion.Euler(0f, _spin, 0f);
        }

        /// <summary>按层数决定显示多少颗光点，并重新均分角度。</summary>
        void ApplyCount(int charges)
        {
            int visible = Mathf.Clamp(charges * dotsPerCharge, 0, MaxDots);

            for (int i = 0; i < _dots.Length; i++)
            {
                var r = _dots[i];
                if (r == null) continue;

                bool on = i < visible;
                if (r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
                if (!on) continue;

                float a = (i / (float)dotsPerCharge) * Mathf.PI * 2f;
                r.transform.localScale = Vector3.one * (dotRadius * 2f);
                r.transform.localPosition = new Vector3(
                    Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
            }

            if (_root != null) _root.gameObject.SetActive(visible > 0);
        }
    }
}
