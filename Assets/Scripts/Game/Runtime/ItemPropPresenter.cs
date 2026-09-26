using BombTavern.Config;
using BombTavern.Core;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 道具使用表现：拿到道具模型并按「抬出 → 举住 → 收回」演一遍。
    ///
    /// 两种用途：
    ///   ThirdPersonHand  —— 挂在角色的右手上，别人能看到「谁拿出了什么东西」。
    ///   FirstPersonCamera —— 挂在相机上，自己能看到手边抬起来的道具。
    ///
    /// 不穿模的两个要点：
    ///   1) 道具的碰撞体在建模型时就全部去掉了 —— 它不参与物理，不可能把角色推出去或者卡进面板。
    ///   2) 第一人称的持握位置固定在相机前方 0.3 米左右，而面板在 0.6 米开外，
    ///      所以道具只是「投影上盖住」面板，几何上不会相交。
    ///
    /// 道具类型从事件的 value 字段取（ItemUsed 事件把它当效果类型用），
    /// 这样不必为了表现层去扩充网络协议。
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemPropPresenter : MonoBehaviour
    {
        public enum ViewMode
        {
            [Tooltip("挂在角色右手上（第三人称）")]
            ThirdPersonHand,
            [Tooltip("挂在相机上（第一人称视角模型）")]
            FirstPersonCamera,
        }

        [Header("绑定")]
        [Tooltip("表现方式。")]
        public ViewMode view = ViewMode.ThirdPersonHand;

        [Tooltip("只对这个座位的事件做出反应。-1 表示不限座位。")]
        public int seatIndex = -1;

        [Tooltip("对局驱动。留空则自动查找。")]
        public GameDirector director;

        [Header("第一人称摆放（相机局部坐标，米）")]
        [Tooltip("起始位置：藏在视野下方。")]
        public Vector3 fpRestOffset = new Vector3(0.16f, -0.42f, 0.30f);

        [Tooltip("举住位置：抬到准星下方偏右、靠近画面中央 —— " +
                 "原来贴右下角太靠边，玩家经常注意不到自己刚用了道具。")]
        public Vector3 fpHoldOffset = new Vector3(0.07f, -0.11f, 0.34f);

        [Tooltip("第一人称下的整体缩放。")]
        [Range(0.2f, 2f)] public float fpScale = 1.05f;

        [Header("第三人称摆放（手部局部坐标，米）")]
        [Tooltip("起始位置：垂在手下。")]
        public Vector3 tpRestOffset = new Vector3(0.02f, -0.18f, 0.03f);

        [Tooltip("举住位置：举到手前。")]
        public Vector3 tpHoldOffset = new Vector3(0.02f, 0.01f, 0.075f);

        [Tooltip("第三人称下的整体缩放。")]
        [Range(0.2f, 2f)] public float tpScale = 0.80f;

        [Header("节奏")]
        [Tooltip("整套动作时长（秒）。")]
        [Range(0.3f, 3f)] public float totalSeconds = 1.15f;

        [Tooltip("抬出动作占总时长的比例。")]
        [Range(0.05f, 0.6f)] public float riseFraction = 0.24f;

        [Tooltip("收回动作占总时长的比例。")]
        [Range(0.05f, 0.6f)] public float fallFraction = 0.24f;

        [Tooltip("举住期间的旋转速度（度/秒），让道具看起来是「在手里转着展示」。")]
        public float spinDegreesPerSecond = 150f;

        GameObject _prop;
        ItemEffectType _currentEffect = (ItemEffectType)(-1);
        float _elapsed = -1f;
        float _spin;
        bool _selected;

        void Awake()
        {
            if (director == null) director = FindObjectOfType<GameDirector>();
        }

        void OnEnable()
        {
            if (director != null) director.Event += OnGameEvent;
        }

        void OnDisable()
        {
            if (director != null) director.Event -= OnGameEvent;
        }

        /// <summary>绑定到对局驱动（换皮肤/重建时需要重新挂）。</summary>
        public void Hook(GameDirector d)
        {
            if (director != null) director.Event -= OnGameEvent;
            director = d;
            if (director != null) director.Event += OnGameEvent;
        }

        void OnGameEvent(GameEvent e)
        {
            if (e.type != GameEventType.ItemUsed) return;
            if (seatIndex >= 0 && e.seatIndex != seatIndex) return;

            Play((ItemEffectType)(int)e.value);
        }

        /// <summary>演一遍「拿出某个道具」。</summary>
        public void Play(ItemEffectType effect)
        {
            EnsureProp(effect);
            if (_prop == null) return;

            _selected = false;
            _elapsed = 0f;
            _spin = 0f;
            _prop.SetActive(true);
            ApplyPose(0f);
        }

        /// <summary>把道具拿在手里等待确认，不触发实际使用动作。</summary>
        public void ShowSelected(ItemEffectType effect)
        {
            EnsureProp(effect);
            if (_prop == null) return;

            _selected = true;
            _elapsed = -1f;
            _spin = 0f;
            _prop.SetActive(true);
            ApplyHeldPose();
        }

        public void ClearSelection()
        {
            _selected = false;
            if (_elapsed < 0f && _prop != null) _prop.SetActive(false);
        }

        void EnsureProp(ItemEffectType effect)
        {
            if (_prop != null && _currentEffect == effect) return;

            if (_prop != null)
            {
                if (Application.isPlaying) Destroy(_prop); else DestroyImmediate(_prop);
                _prop = null;
            }

            _currentEffect = effect;
            _prop = ItemPropFactory.Build(effect, transform, $"Prop_{effect}");
            _prop.SetActive(false);
        }

        void Update()
        {
            if (_prop == null) return;

            if (_selected)
            {
                ApplyHeldPose();
                return;
            }

            if (_elapsed < 0f) return;

            _elapsed += Time.deltaTime;

            float total = Mathf.Max(0.05f, totalSeconds);
            float t = Mathf.Clamp01(_elapsed / total);

            if (t >= 1f)
            {
                _elapsed = -1f;
                _prop.SetActive(false);
                return;
            }

            ApplyPose(t);
        }

        void ApplyHeldPose()
        {
            if (_prop == null) return;
            Vector3 hold = view == ViewMode.FirstPersonCamera ? fpHoldOffset : tpHoldOffset;
            float scale = view == ViewMode.FirstPersonCamera ? fpScale : tpScale;
            _spin += spinDegreesPerSecond * 0.35f * Time.deltaTime;
            _prop.transform.localPosition = hold + new Vector3(0f, Mathf.Sin(Time.time * 4f) * 0.005f, 0f);
            _prop.transform.localScale = Vector3.one * scale;
            _prop.transform.localRotation = Quaternion.Euler(0f, _spin, 0f);
        }

        /// <summary>t ∈ [0,1]：抬出 → 举住 → 收回。</summary>
        void ApplyPose(float t)
        {
            if (_prop == null) return;

            Vector3 rest = view == ViewMode.FirstPersonCamera ? fpRestOffset : tpRestOffset;
            Vector3 hold = view == ViewMode.FirstPersonCamera ? fpHoldOffset : tpHoldOffset;
            float scale = view == ViewMode.FirstPersonCamera ? fpScale : tpScale;

            float rise = Mathf.Clamp01(riseFraction);
            float fall = Mathf.Clamp01(fallFraction);

            Vector3 pos;
            float appear;

            if (t < rise)
            {
                float k = rise <= 0f ? 1f : t / rise;
                float e = 1f - (1f - k) * (1f - k);          // easeOut
                pos = Vector3.Lerp(rest, hold, e);
                appear = Mathf.Clamp01(k * 1.6f);
            }
            else if (t > 1f - fall)
            {
                float k = fall <= 0f ? 1f : (t - (1f - fall)) / fall;
                float e = k * k;                              // easeIn
                pos = Vector3.Lerp(hold, rest, e);
                appear = Mathf.Clamp01(1f - k * 1.4f);
            }
            else
            {
                // 举住：轻微上下浮动，看起来像在展示
                pos = hold + new Vector3(0f, Mathf.Sin(_elapsed * 6f) * 0.008f, 0f);
                appear = 1f;
            }

            _prop.transform.localPosition = pos;
            _prop.transform.localScale = Vector3.one * (scale * Mathf.Lerp(0.2f, 1f, appear));

            _spin += spinDegreesPerSecond * Time.deltaTime;
            _prop.transform.localRotation = Quaternion.Euler(-12f * (1f - appear), _spin, 0f);
        }
    }
}
