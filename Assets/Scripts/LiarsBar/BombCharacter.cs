using UnityEngine;

namespace LiarsBar
{
    /// <summary>卡通炸弹角色：座位身份 + 待机呼吸动画 + 爆炸表现。</summary>
    [DisallowMultipleComponent]
    public class BombCharacter : MonoBehaviour
    {
        [Header("身份")]
        public string displayName = "炸弹";
        public Color accentColor = Color.white;
        public int seatIndex;

        [Header("待机动画")]
        public float bobAmplitude = 0.035f;
        public float bobSpeed = 1.7f;
        public float swayDegrees = 2.2f;
        public float blinkInterval = 3.2f;

        [Header("情绪")]
        [Range(0f, 1f)] public float tension = 0.35f;

        [Header("说话")]
        [Tooltip("说话时嘴巴开合的速度（次/秒）。")]
        public float talkSpeed = 11f;

        [Tooltip("说话时嘴巴张开的程度（0=不张，1=张到最大）。")]
        [Range(0f, 1f)] public float talkOpenAmount = 0.85f;

        bool _talking;
        float _mouthOpen = 1f;

        Transform _body;
        Vector3 _bodyBase;
        Transform _eyeL;
        Transform _eyeR;
        Vector3 _eyeLBase;
        Vector3 _eyeRBase;
        Transform _mouth;
        Vector3 _mouthBase;
        float _phase;
        float _blinkTimer;
        float _nextBlink = 3f;

        void Awake()
        {
            _phase = Random.value * Mathf.PI * 2f;
            RefreshRefs();
        }

        /// <summary>
        /// 重新绑定内部节点引用。
        /// 换皮肤会重建 Visual 节点，必须重新取一次 BodyRoot / 眼睛 / 嘴，否则动画会失效。
        /// </summary>
        public void RefreshRefs()
        {
            _body = transform.Find("Visual/BodyRoot");
            if (_body == null) _body = transform.Find("BodyRoot");

            _eyeL = _eyeR = _mouth = null;

            if (_body != null)
            {
                _bodyBase = _body.localPosition;
                _eyeL = _body.Find("EyeL");
                _eyeR = _body.Find("EyeR");
                _mouth = _body.Find("Mouth");

                if (_eyeL != null) _eyeLBase = _eyeL.localScale;
                if (_eyeR != null) _eyeRBase = _eyeR.localScale;
                if (_mouth != null) _mouthBase = _mouth.localScale;
            }
        }

        /// <summary>设置说话状态：true 时嘴巴会持续开合（用于语音聊天）。</summary>
        public void SetTalking(bool talking)
        {
            _talking = talking;
        }

        /// <summary>是否正在说话。</summary>
        public bool IsTalking => _talking;

        void Update()
        {
            if (_body == null) return;

            float t = Time.time * bobSpeed + _phase;
            _body.localPosition = _bodyBase + new Vector3(0f, Mathf.Sin(t) * bobAmplitude, 0f);
            _body.localRotation = Quaternion.Euler(
                Mathf.Sin(t * 0.7f) * swayDegrees * tension,
                Mathf.Cos(t * 0.5f) * swayDegrees,
                0f);

            // 眨眼：仅缩放“眼睛”子物体
            _blinkTimer += Time.deltaTime;
            if (_blinkTimer >= _nextBlink)
            {
                _blinkTimer = 0f;
                _nextBlink = blinkInterval + Random.Range(-0.8f, 0.8f);
            }
            float blink = _blinkTimer < 0.12f ? 0.12f : 1f;
            if (_eyeL != null) _eyeL.localScale = new Vector3(_eyeLBase.x, _eyeLBase.y * blink, _eyeLBase.z);
            if (_eyeR != null) _eyeR.localScale = new Vector3(_eyeRBase.x, _eyeRBase.y * blink, _eyeRBase.z);

            // 说话时嘴巴开合
            if (_mouth != null)
            {
                float target = 1f;
                if (_talking)
                {
                    float pulse = Mathf.Abs(Mathf.Sin(Time.time * talkSpeed));
                    target = 1f + talkOpenAmount * pulse * 2.2f;
                }

                _mouthOpen = Mathf.Lerp(_mouthOpen, target, Time.deltaTime * 14f);
                _mouth.localScale = new Vector3(_mouthBase.x, _mouthBase.y * _mouthOpen, _mouthBase.z);
            }
        }

        /// <summary>触发一次爆炸表现（仅视觉效果，便于游戏逻辑扩展）。</summary>
        public void Explode()
        {
            var go = new GameObject("BombBurst");
            go.transform.position = transform.position + Vector3.up * 0.5f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(accentColor, new Color(1f, 0.72f, 0.2f));
            main.startSize = 0.22f;
            main.startSpeed = 4.5f;
            main.startLifetime = 0.9f;
            main.duration = 0.35f;
            main.loop = false;
            var emis = ps.emission;
            emis.rateOverTime = 0f;
            emis.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
            ps.Play();
            Destroy(go, 3f);
        }
    }
}
