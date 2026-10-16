using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace BombTavern.Runtime
{
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

}
