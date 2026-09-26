using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 第一人称控制。
    ///
    /// 默认是**就坐模式**：玩家坐在自己的椅子上，只能用鼠标转动视角看桌面与四周，
    /// 不能走动、跳跃或掉下桌子，这符合「围坐一桌玩扫雷」的玩法。
    /// 把 seatedMode 关掉才会退回可自由行走的老行为。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("就坐")]
        [Tooltip("开启后玩家固定坐在椅子上：无法移动与跳跃，只保留鼠标视角。")]
        public bool seatedMode = true;

        [Tooltip("就坐时水平视角的左右极限（度，相对椅子的朝向）。180 表示可以自由转身。")]
        [Range(20f, 180f)] public float seatedYawLimit = 78f;

        [Tooltip("就坐时能低头多少度（正值，单位：度）。太小就够不到自己面板最下面一行格子。")]
        [Range(0f, 89f)] public float seatedPitchDown = 80f;

        [Tooltip("就坐时能抬头多少度（正值，单位：度）。")]
        [Range(0f, 60f)] public float seatedPitchUp = 32f;

        /// <summary>
        /// 旁观（幽灵）模式：本地玩家已出局，放开就坐视角限制。
        ///
        /// 为什么必须放开：seatedYawLimit 默认只有 78°，坐在椅子上只能看
        /// 自己正前方那一小块。出局后如果还锁着，玩家就**永远看不到另外三个人的面板**——
        /// 而「出局后当解说」正是这个模式存在的唯一理由。
        /// 放开到整圈 + 更大的俯仰，才谈得上「能去看别人的盘面」。
        /// </summary>
        public bool spectatorMode;

        [Header("移动")]
        [Tooltip("常规行走速度（米/秒）。仅在关闭就坐模式时生效。")]
        public float walkSpeed = 2.6f;

        [Tooltip("疾跑速度（米/秒）。")]
        public float sprintSpeed = 4.4f;

        [Tooltip("跳跃初速度。")]
        public float jumpSpeed = 3.6f;

        [Tooltip("重力加速度。")]
        public float gravity = 18f;

        [Tooltip("落地后的最大下落速度限制。")]
        public float terminalVelocity = 20f;

        [Header("视角")]
        [Tooltip("鼠标水平灵敏度。")]
        public float mouseSensitivity = 2.2f;

        [Tooltip("俯仰角限制（度）。")]
        public float pitchLimit = 75f;

        [Tooltip("是否锁定并隐藏鼠标指针。")]
        public bool lockCursor = true;

        [Tooltip("开局俯仰角（度）。正值表示略微低头，便于看到桌面上的屏幕。")]
        [Range(-60f, 60f)] public float startPitch = 0f;

        [Header("相机")]
        [Tooltip("相机相对身体的高度偏移。")]
        public float eyeHeight = 1.55f;

        [Tooltip("本地相机。留空则自动查找子物体。")]
        public Camera viewCamera;

        [Tooltip("行走时的视角轻微摇晃幅度。")]
        public float bobAmplitude = 0.035f;

        [Tooltip("摇晃频率。")]
        public float bobFrequency = 8f;

        CharacterController _controller;
        float _pitch;
        float _verticalSpeed;
        float _bobPhase;
        float _bodyHeight;
        float _seatYaw;              // 椅子的朝向，就坐时视角以它为基准左右摆动

        public bool InputEnabled { get; set; } = true;
        public bool UiMode { get; private set; }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
            _bodyHeight = _controller.height;
            _seatYaw = transform.eulerAngles.y;
            _pitch = Mathf.Clamp(startPitch, -pitchLimit, pitchLimit);
        }

        void Start()
        {
            ApplyCursor();

            // 就坐时连角色控制器都关掉：彻底消除「被椅子/桌子的碰撞体挤出去、
            // 或者从地板缺口漏下去」的可能。相机是子物体，视角不受影响。
            if (seatedMode && _controller != null) _controller.enabled = false;
        }

        void OnEnable() => ApplyCursor();

        void OnDisable()
        {
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void ApplyCursor()
        {
            if (!lockCursor || UiMode) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>
        /// 切换全屏 UI 交互模式。结算界面开启时持续解锁鼠标，
        /// 避免「左键重新捕获鼠标」抢走 UGUI 按钮的点击。
        /// </summary>
        public void SetUiMode(bool active)
        {
            UiMode = active;
            InputEnabled = !active;

            if (active)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                ApplyCursor();
            }
        }

        void Update()
        {
            // 光标的锁定/释放是桌面端独有的概念。触摸端没有光标，
            // 拖拽本身就是转视角，不需要（也不能）走这套逻辑。
            if (!GameInput.TouchMode)
            {
                if (UiMode)
                {
                    if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
                    if (!Cursor.visible) Cursor.visible = true;
                }
                // Esc 由对局设置菜单统一处理，避免关闭菜单的同一帧又被这里解锁鼠标。
                else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked && lockCursor)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            if (InputEnabled) Look();
            Move();
        }

        void Look()
        {
            // 桌面端：光标没锁定时（Esc 释放 / 菜单中）不转视角，
            // 否则玩家点界面时视野会跟着乱转。触摸端没有这个顾虑。
            if (!GameInput.TouchMode && Cursor.lockState != CursorLockMode.Locked) return;

            // 视角增量统一从 GameInput 取：桌面端是鼠标增量，触摸端是拖拽位移。
            var raw = GameInput.LookDelta;
            float mx = raw.x * mouseSensitivity;
            float my = raw.y * mouseSensitivity;

            if (seatedMode)
            {
                // 坐着转视角：以椅子朝向为基准左右摆动，并收紧俯仰范围，
                // 这样玩家看得到自己与对手的面板，又不会转得失去方向感。
                //
                // 出局后（spectatorMode）放开到整圈：这时候已经没有行动权，
                // 唯一的乐趣就是转头去看别人怎么博弈，还锁着就没有意义了。
                float yawLimit = spectatorMode ? 180f : seatedYawLimit;
                float pitchDown = spectatorMode ? 85f : seatedPitchDown;
                float pitchUp = spectatorMode ? 65f : seatedPitchUp;

                float offset = Mathf.DeltaAngle(_seatYaw, transform.eulerAngles.y);
                offset = Mathf.Clamp(offset + mx, -yawLimit, yawLimit);
                transform.rotation = Quaternion.Euler(0f, _seatYaw + offset, 0f);

                // 俯仰约定：正的 _pitch = 低头。
                // 相机的前方是本地 +Z，绕本地 X 轴正向旋转会把 +Z 压向 -Y（向下），
                // 所以「低头」对应正角度、「抬头」对应负角度。
                // 之前把两个限位直接当成 Clamp 的上下界用，等于把低头卡在 30°、
                // 抬头放到 72° —— 方向整个反了，才会「向下看不到全部格子」。
                _pitch = Mathf.Clamp(_pitch - my, -pitchUp, pitchDown);
            }
            else
            {
                transform.Rotate(0f, mx, 0f, Space.Self);
                _pitch = Mathf.Clamp(_pitch - my, -pitchLimit, pitchLimit);
            }

            if (viewCamera != null)
            {
                var e = viewCamera.transform.localEulerAngles;
                viewCamera.transform.localEulerAngles = new Vector3(_pitch, e.y, e.z);
            }
        }

        void Move()
        {
            // 就坐模式：完全不产生位移，既不走路也不会踩空掉下去。
            if (seatedMode) return;

            float dt = Time.deltaTime;

            float h = InputEnabled ? Input.GetAxisRaw("Horizontal") : 0f;
            float v = InputEnabled ? Input.GetAxisRaw("Vertical") : 0f;

            Vector3 wish = transform.right * h + transform.forward * v;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            bool sprinting = InputEnabled && Input.GetKey(KeyCode.LeftShift);
            float speed = sprinting ? sprintSpeed : walkSpeed;

            if (_controller.isGrounded)
            {
                if (_verticalSpeed < 0f) _verticalSpeed = -1f;
                if (InputEnabled && Input.GetKeyDown(KeyCode.Space)) _verticalSpeed = jumpSpeed;
            }
            else
            {
                _verticalSpeed -= gravity * dt;
                if (_verticalSpeed < -terminalVelocity) _verticalSpeed = -terminalVelocity;
            }

            Vector3 motion = wish * speed;
            motion.y = _verticalSpeed;
            _controller.Move(motion * dt);

            ApplyHeadBob(dt, wish.sqrMagnitude > 0.01f && _controller.isGrounded);
        }

        void ApplyHeadBob(float dt, bool moving)
        {
            if (viewCamera == null) return;

            if (moving)
            {
                _bobPhase += dt * bobFrequency;
                float offset = Mathf.Sin(_bobPhase) * bobAmplitude;
                var t = viewCamera.transform;
                var e = t.localEulerAngles;
                t.localPosition = new Vector3(0f, eyeHeight + offset, 0f);
                t.localEulerAngles = new Vector3(_pitch, e.y, 0f);
            }
            else
            {
                _bobPhase = 0f;
                var t = viewCamera.transform;
                t.localPosition = Vector3.Lerp(t.localPosition, new Vector3(0f, eyeHeight, 0f), dt * 8f);
                t.localEulerAngles = new Vector3(_pitch, t.localEulerAngles.y, 0f);
            }
        }

        /// <summary>把出生位置放在指定点，并朝向目标（用于开局对准自己的平板）。</summary>
        public void Teleport(Vector3 position, Vector3 lookAt)
        {
            if (_controller != null) _controller.enabled = false;
            transform.position = position;
            var dir = lookAt - position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _seatYaw = transform.eulerAngles.y;   // 坐姿基准朝向跟着新朝向走

            // 就坐模式下保持控制器关闭，避免瞬移后又被物理推挤
            if (_controller != null) _controller.enabled = !seatedMode;
            _verticalSpeed = 0f;
        }
    }
}
