using System.Collections;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>设置及说明弹窗的淡入和缩放；独立脚本确保 Unity 预制体可序列化组件引用。</summary>
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

        IEnumerator Animate(bool opening)
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
}
