using BombTavern.Core;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 第一人称右手视角模型。项目角色用彩色球体作为手套，因此这里复用同一视觉语言：
    /// 平时手在视野右下方跟随相机；瞄准格子时前伸；翻格/插旗时完成一次点按；
    /// 使用道具时把道具举到手上并做一次明显的使用动作。
    /// </summary>
    [DisallowMultipleComponent]
    public class FirstPersonHandView : MonoBehaviour
    {
        public int seatIndex = -1;
        public GameDirector director;

        [Header("摆放")]
        public Vector3 restOffset = new Vector3(0.20f, -0.24f, 0.42f);
        public Vector3 aimOffset = new Vector3(0.13f, -0.15f, 0.50f);
        public Vector3 tapOffset = new Vector3(0.04f, -0.04f, 0.66f);
        public Vector3 itemOffset = new Vector3(0.08f, -0.09f, 0.48f);
        [Range(0.04f, 0.18f)] public float handRadius = 0.060f;
        [Range(2f, 24f)] public float followSpeed = 12f;

        public Transform PropAnchor { get; private set; }

        Transform _visual;
        Renderer _renderer;
        float _tapStarted = -10f;
        float _itemStarted = -10f;
        const float TapSeconds = 0.34f;
        const float ItemSeconds = 1.15f;
        bool _hooked;

        void Awake() => EnsureBuilt();
        void OnDestroy() => Unhook();

        public void Hook(GameDirector value, int seat, Color color)
        {
            Unhook();
            director = value;
            seatIndex = seat;
            EnsureBuilt();
            SetColor(color);
            if (director != null)
            {
                director.Event += OnGameEvent;
                _hooked = true;
            }
        }

        void Unhook()
        {
            if (_hooked && director != null) director.Event -= OnGameEvent;
            _hooked = false;
        }

        void EnsureBuilt()
        {
            if (_visual != null) return;

            var hand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hand.name = "LocalRightHand";
            hand.transform.SetParent(transform, false);
            hand.transform.localScale = Vector3.one * (handRadius * 2f);
            var collider = hand.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _visual = hand.transform;
            _renderer = hand.GetComponent<Renderer>();

            var anchor = new GameObject("PropAnchor").transform;
            anchor.SetParent(_visual, false);
            anchor.localPosition = new Vector3(0f, 0.18f, 0.46f);
            anchor.localScale = Vector3.one / Mathf.Max(0.001f, handRadius * 2f);
            PropAnchor = anchor;

            _visual.localPosition = restOffset;
        }

        void SetColor(Color color)
        {
            if (_renderer == null) return;
            var shader = Shader.Find("Standard");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.color = color;
            material.SetFloat("_Glossiness", 0.45f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 0.18f);
            _renderer.sharedMaterial = material;
        }

        void OnGameEvent(GameEvent e)
        {
            if (e.seatIndex != seatIndex) return;
            if (e.type == GameEventType.CellRevealed || e.type == GameEventType.CellFlagged)
                _tapStarted = Time.time;
            else if (e.type == GameEventType.ItemUsed)
                _itemStarted = Time.time;
        }

        void LateUpdate()
        {
            if (_visual == null) return;

            // 准备界面使用环绕观察机位，并不是玩家主控阶段；此时显示手会挡住菜单。
            // 进入实际对局后才启用第一人称手模。
            var localPlayer = director != null && director.Session != null
                ? director.Session.PlayerAt(seatIndex)
                : null;
            bool shouldShow = localPlayer != null && !localPlayer.eliminated &&
                              !director.IsPregame && !director.Session.IsOver;
            if (_visual.gameObject.activeSelf != shouldShow)
                _visual.gameObject.SetActive(shouldShow);
            if (!shouldShow) return;

            float tapT = Mathf.Clamp01((Time.time - _tapStarted) / TapSeconds);
            float itemT = Mathf.Clamp01((Time.time - _itemStarted) / ItemSeconds);
            bool tapping = tapT < 1f;
            bool usingItem = itemT < 1f;
            bool holdingItem = false;
            bool aiming = false;
            float nx = 0f, ny = 0f;

            var interactor = director != null ? director.GetComponentInChildren<TabletInteractor>() : null;
            if (interactor == null) interactor = FindObjectOfType<TabletInteractor>();
            holdingItem = interactor != null && interactor.SelectedItemSlot >= 0;
            if (interactor != null && interactor.Hovered != null && director != null && director.Session != null)
            {
                aiming = director.Session.IsMyTurn(seatIndex);
                var board = director.Session.SharedBoard;
                if (board != null && board.Columns > 1 && board.Rows > 1)
                {
                    nx = interactor.Hovered.x / (float)(board.Columns - 1) * 2f - 1f;
                    ny = interactor.Hovered.y / (float)(board.Rows - 1) * 2f - 1f;
                }
            }

            Vector3 target = aiming ? aimOffset + new Vector3(nx * 0.025f, -ny * 0.018f, 0f) : restOffset;
            if (holdingItem) target = itemOffset;
            if (usingItem)
            {
                float pulse = Mathf.Sin(itemT * Mathf.PI);
                target = Vector3.Lerp(target, itemOffset + new Vector3(0f, pulse * 0.035f, 0f), pulse);
            }
            if (tapping)
            {
                float stroke = Mathf.Sin(tapT * Mathf.PI);
                target = Vector3.Lerp(target, tapOffset, stroke);
            }

            float follow = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            _visual.localPosition = Vector3.Lerp(_visual.localPosition, target, follow);
            float roll = aiming ? -8f + nx * 7f : -18f;
            if (tapping) roll += Mathf.Sin(tapT * Mathf.PI) * 22f;
            _visual.localRotation = Quaternion.Slerp(_visual.localRotation,
                Quaternion.Euler(-ny * 6f, nx * 8f, roll), follow);
        }
    }
}
