using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 输入抽象层：把「鼠标 + 键盘」和「触摸」收敛到同一套接口。
    ///
    /// 为什么需要它：原来的操作层直接读 `Input.GetAxis("Mouse X")` / `GetMouseButtonDown`
    /// / `GetKeyDown(Alpha1..6)`，这些在手机上全都不存在。要上多端就必须先有一层抽象，
    /// 否则每加一个平台都要在业务代码里写一遍 `if (IsMobile)`。
    ///
    /// 触摸端的核心矛盾：**拖拽转视角** 和 **点击翻格** 都是「按下-移动-抬起」。
    /// 这里用一套手势状态机区分：
    ///   短按不动         → 主操作（翻格）
    ///   长按不动         → 次操作（插旗）
    ///   移动超过阈值     → 转视角，并且**取消**这次点击
    /// 这是移动端扫雷类游戏的通用做法，比放一个模式切换按钮自然得多。
    ///
    /// 采样是惰性的（按帧号），不需要在场景里挂驱动组件。
    /// </summary>
    public static class GameInput
    {
        // ============================================================ 配置
        /// <summary>移动超过这个像素数就判定为「转视角」而不是「点击」。</summary>
        public static float dragThreshold = 14f;

        /// <summary>按住超过这个秒数判定为「长按」→ 插旗。</summary>
        public static float longPressSeconds = 0.42f;

        /// <summary>按下到抬起超过这个秒数就不再算点击（防止长按末尾误触发翻格）。</summary>
        public static float tapMaxSeconds = 0.30f;

        /// <summary>触摸拖拽像素 → 视角增量的换算系数。让手机和鼠标的手感接近。</summary>
        public static float touchLookScale = 0.16f;

        /// <summary>强制指定输入模式。Auto 时按设备能力判断。</summary>
        public enum Mode { Auto, Desktop, Touch }
        public static Mode mode = Mode.Auto;

        /// <summary>对讲按键。由 VoiceChatManager 按自己的配置写入。</summary>
        public static KeyCode pushToTalkKey = KeyCode.V;

        static bool? _touchCached;

        static bool? _touchFromCli;

        /// <summary>
        /// 命令行 `-btTouch` 强制触摸模式。
        /// 用途：在 PC 上验证触摸手势与触摸布局，不必真机也能自查。
        /// </summary>
        static bool TouchForcedByCli()
        {
            if (_touchFromCli.HasValue) return _touchFromCli.Value;

            // 移动端布局必然配触摸输入，所以 -btMobile 也应当切到触摸分支，
            // 否则会出现「UI 是手机排版、操作却还是鼠标逻辑」的怪状态。
            if (UiKit.MobileForcedByCli())
            {
                _touchFromCli = true;
                return true;
            }

            bool found = false;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], "-btTouch", System.StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }

            _touchFromCli = found;
            return found;
        }

        /// <summary>当前是否按触摸方式处理。第一次有触摸输入时自动切换。</summary>
        public static bool TouchMode
        {
            get
            {
                if (TouchForcedByCli()) return true;
                if (mode == Mode.Touch) return true;
                if (mode == Mode.Desktop) return false;
                if (_touchCached.HasValue) return _touchCached.Value;
                return UnityEngine.Input.touchSupported && Application.isMobilePlatform;
            }
        }

        // ============================================================ 每帧状态
        static int _frame = -1;
        static bool _modeLogged;

        static bool _primaryDown, _secondaryDown;
        static Vector2 _lookDelta;
        static bool _autoPlayToggle;
        static bool _itemUseConfirm;

        /// <summary>
        /// 待消费的「切换托管」请求。
        ///
        /// 为什么要有这一层：<see cref="_autoPlayToggle"/> 是「本帧为真」的语义，
        /// 必须每帧清零；但清零就会把界面按钮在**上一帧**注入的请求一起吃掉。
        /// 所以注入只写这个 pending 标志，由下一帧的首次采样搬到 _autoPlayToggle 上并清空。
        /// 不这么分，就会退化成「标志一旦置真就永远为真」——
        /// 而 GameDirector 每帧都读它，等于按一次 H 之后托管开关每帧翻转一次，
        /// 对局直接卡死（AI 的思考计时刚累积就被清零）。
        /// </summary>
        static bool _autoPlayTogglePending;
        static bool _pushToTalkHeld;
        static Vector2 _pointerPos;
        static bool _pointerValid;

        static readonly System.Collections.Generic.Queue<int> ItemSlotQueue =
            new System.Collections.Generic.Queue<int>();

        // 触摸手势状态
        static bool _tracking;
        static Vector2 _startPos;
        static float _startTime;
        static bool _dragging;
        static bool _longPressFired;

        // ============================================================ 对外接口
        /// <summary>是否有可用的屏幕指针（鼠标或触摸都在）。</summary>
        public static bool PointerAvailable { get { EnsureFrame(); return _pointerValid; } }

        /// <summary>本帧确认的主操作（鼠标左键 / 触摸短按）。</summary>
        public static bool PrimaryDown { get { EnsureFrame(); return _primaryDown; } }

        /// <summary>本帧确认的次操作（鼠标右键 / 触摸长按）。</summary>
        public static bool SecondaryDown { get { EnsureFrame(); return _secondaryDown; } }

        /// <summary>本帧视角增量，单位与 <c>Input.GetAxis("Mouse X")</c> 一致。</summary>
        public static Vector2 LookDelta { get { EnsureFrame(); return _lookDelta; } }

        /// <summary>本帧是否按下了「切换 AI 托管」（键盘 H / 界面按钮）。</summary>
        public static bool AutoPlayToggleDown { get { EnsureFrame(); return _autoPlayToggle; } }

        /// <summary>本帧是否确认使用已拿出的道具（键盘 E）。</summary>
        public static bool ItemUseConfirmDown { get { EnsureFrame(); return _itemUseConfirm; } }

        /// <summary>对讲键是否按住。</summary>
        public static bool PushToTalkHeld { get { EnsureFrame(); return _pushToTalkHeld; } }

        /// <summary>取一个本帧要使用的道具槽位（键盘 1~6 或界面按钮注入）。</summary>
        public static bool ConsumeItemSlot(out int index)
        {
            EnsureFrame();
            if (ItemSlotQueue.Count == 0) { index = -1; return false; }
            index = ItemSlotQueue.Dequeue();
            return true;
        }

        /// <summary>由界面按钮注入一个道具槽位点击（触摸端没有数字键，靠这个）。</summary>
        public static void InjectItemSlot(int index) => ItemSlotQueue.Enqueue(index);

        /// <summary>由界面按钮注入一次「切换托管」。写 pending，由下一帧采样搬走。</summary>
        public static void InjectAutoPlayToggle() => _autoPlayTogglePending = true;

        /// <summary>从屏幕点发一条射线。触摸与鼠标共用。</summary>
        public static bool TryGetPointerRay(Camera cam, out Ray ray)
        {
            EnsureFrame();
            ray = default;
            if (cam == null || !_pointerValid) return false;

            ray = cam.ScreenPointToRay(_pointerPos);
            return true;
        }

        // ============================================================ 采样
        static void EnsureFrame()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;

            // 每帧重置一次性状态；被界面注入的输入已经在上一帧写进来了
            _primaryDown = false;
            _secondaryDown = false;
            _itemUseConfirm = false;
            _lookDelta = Vector2.zero;

            // 把上一帧挂起的「切换托管」搬成本帧的一次性事件，搬完即清。
            // 漏掉这一句就是「按一次 H 永久翻转」那个 bug 的源头。
            _autoPlayToggle = _autoPlayTogglePending;
            _autoPlayTogglePending = false;

            bool touch = DetectTouch();
            if (touch) SampleTouch();
            else SampleDesktop();

            SampleCommon();

            // 只报一次：输入模式判断错的话，界面文案和手势分支会全错，
            // 而这个问题从画面上很难一眼看出来，必须有日志。
            if (!_modeLogged)
            {
                _modeLogged = true;
                Debug.Log($"[输入] 模式={(touch ? "触摸" : "桌面")} " +
                          $"命令行强制={TouchForcedByCli()} 平台={Application.platform} " +
                          $"支持触摸={UnityEngine.Input.touchSupported}");
            }
        }

        /// <summary>只要出现真实触摸，就永久切到触摸模式（避免混合输入时来回抖）。</summary>
        static bool DetectTouch()
        {
            if (mode == Mode.Desktop) return false;
            if (mode == Mode.Touch) return true;

            // ⚠ 命令行强制必须在这里判，而不是只在 TouchMode 属性里判：
            // EnsureFrame 走的是 DetectTouch()，漏了这一条会导致
            // 「-btTouch 在 PC 上根本进不了触摸模式」，而属性却报告 true。
            if (TouchForcedByCli()) return true;

            if (!_touchCached.HasValue && UnityEngine.Input.touchCount > 0)
                _touchCached = true;

            return _touchCached ?? (UnityEngine.Input.touchSupported && Application.isMobilePlatform);
        }

        static void SampleDesktop()
        {
            _pointerPos = UnityEngine.Input.mousePosition;
            _pointerValid = true;

            if (UnityEngine.Input.GetMouseButtonDown(0)) _primaryDown = true;
            if (UnityEngine.Input.GetMouseButtonDown(1)) _secondaryDown = true;

            _lookDelta = new Vector2(
                UnityEngine.Input.GetAxis("Mouse X"),
                UnityEngine.Input.GetAxis("Mouse Y"));

            for (int i = 0; i < 6; i++)
                if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1 + i)) ItemSlotQueue.Enqueue(i);
        }

        /// <summary>
        /// 触摸手势状态机。
        /// 关键点：一旦判定为拖拽，**这次按压就永远不再产生点击** ——
        /// 否则「转视角顺便翻了一格」会让人非常恼火。
        /// </summary>
        static Vector2 _lastMousePos;

        /// <summary>
        /// 用鼠标模拟触摸（只在 `-btTouch` 强制触摸模式下生效）。
        /// 没有它就没法在 PC 上验证手势状态机 —— 短按/长按/拖拽三条分支全跑不到。
        /// </summary>
        static void SampleTouchWithMouse()
        {
            var pos = (Vector2)UnityEngine.Input.mousePosition;

            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                _tracking = true;
                _dragging = false;
                _longPressFired = false;
                _startPos = pos;
                _lastMousePos = pos;
                _startTime = Time.unscaledTime;
                _pointerPos = pos;
                _pointerValid = true;
                return;
            }

            if (_tracking && UnityEngine.Input.GetMouseButton(0))
            {
                if (!_dragging && Vector2.Distance(pos, _startPos) > dragThreshold) _dragging = true;

                if (_dragging)
                {
                    _lookDelta += (pos - _lastMousePos) * touchLookScale;
                    _pointerValid = false;
                }
                else if (!_longPressFired && Time.unscaledTime - _startTime >= longPressSeconds)
                {
                    _longPressFired = true;
                    _pointerPos = pos;
                    _pointerValid = true;
                    _secondaryDown = true;
                }
                else
                {
                    _pointerPos = pos;
                    _pointerValid = true;
                }

                _lastMousePos = pos;
                return;
            }

            if (UnityEngine.Input.GetMouseButtonUp(0))
            {
                if (_tracking && !_dragging && !_longPressFired
                    && Time.unscaledTime - _startTime <= tapMaxSeconds)
                {
                    _pointerPos = pos;
                    _pointerValid = true;
                    _primaryDown = true;
                }

                _tracking = false;
                _dragging = false;
                _longPressFired = false;
            }
        }

        static void SampleTouch()
        {
            _pointerValid = false;

            // PC 上强制触摸模式时没有真实触摸点，用鼠标模拟，保证手势状态机可验证
            if (UnityEngine.Input.touchCount == 0 && TouchForcedByCli())
            {
                SampleTouchWithMouse();
                return;
            }

            if (UnityEngine.Input.touchCount == 0)
            {
                // 抬手后可能还有一帧要收尾
                if (_tracking && !_dragging && !_longPressFired && !_primaryDown
                    && Time.unscaledTime - _startTime <= tapMaxSeconds)
                {
                    _pointerPos = _startPos;
                    _pointerValid = true;
                    _primaryDown = true;
                }
                _tracking = false;
                _dragging = false;
                _longPressFired = false;
                return;
            }

            var t = UnityEngine.Input.GetTouch(0);

            switch (t.phase)
            {
                case TouchPhase.Began:
                    _tracking = true;
                    _dragging = false;
                    _longPressFired = false;
                    _startPos = t.position;
                    _startTime = Time.unscaledTime;
                    _pointerPos = t.position;
                    _pointerValid = true;
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (!_tracking) break;

                    // 位移超过阈值 → 转为转视角，并且**放弃**这次点击
                    if (!_dragging && Vector2.Distance(t.position, _startPos) > dragThreshold)
                        _dragging = true;

                    if (_dragging)
                    {
                        _lookDelta += t.deltaPosition * touchLookScale;
                        _pointerValid = false;
                    }
                    else if (!_longPressFired &&
                             Time.unscaledTime - _startTime >= longPressSeconds)
                    {
                        // 长按不动 → 插旗
                        _longPressFired = true;
                        _pointerPos = t.position;
                        _pointerValid = true;
                        _secondaryDown = true;
                    }
                    else
                    {
                        _pointerPos = t.position;
                        _pointerValid = true;
                    }
                    break;

                case TouchPhase.Ended:
                    // 短按（没拖拽、没触发长按、时间够短）才是一次点击
                    if (_tracking && !_dragging && !_longPressFired
                        && Time.unscaledTime - _startTime <= tapMaxSeconds)
                    {
                        _pointerPos = t.position;
                        _pointerValid = true;
                        _primaryDown = true;
                    }

                    _tracking = false;
                    _dragging = false;
                    _longPressFired = false;
                    break;

                case TouchPhase.Canceled:
                    _tracking = false;
                    _dragging = false;
                    _longPressFired = false;
                    break;
            }
        }

        /// <summary>与设备无关的部分：H 键切换托管、对讲键。</summary>
        static void SampleCommon()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.H)) _autoPlayToggle = true;
            if (UnityEngine.Input.GetKeyDown(KeyCode.E)) _itemUseConfirm = true;

            _pushToTalkHeld = UnityEngine.Input.GetKey(pushToTalkKey);
        }

        /// <summary>场景切换时清掉残留，避免把上一局的按键带过去。</summary>
        public static void Reset()
        {
            _frame = -1;
            _primaryDown = _secondaryDown = false;
            _itemUseConfirm = false;
            _lookDelta = Vector2.zero;
            _autoPlayToggle = false;
            _autoPlayTogglePending = false;
            _pushToTalkHeld = false;
            _tracking = _dragging = _longPressFired = false;
            ItemSlotQueue.Clear();
        }
    }
}
