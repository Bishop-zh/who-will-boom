using BombTavern.Core;
using LiarsBar;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 炸弹角色的动作状态机。
    ///
    /// 状态流转：
    ///   Idle → Aim（对准某格）→ Tap（翻格/插旗）→ Idle
    ///   Idle/Aim → UseItem（使用道具）→ Idle
    ///   任意 → Stagger（踩到雷）→ Idle / Down（出局）
    ///
    /// 实现方式：只驱动手臂 / 手 / 嘴巴 / 身体这几个节点的局部变换，
    /// 不做骨骼动画，保证换皮肤（重建 Visual 节点）后依然可用。
    /// </summary>
    [DisallowMultipleComponent]
    public class BombActionAnimator : MonoBehaviour
    {
        public enum ActionState
        {
            Idle,
            Aim,
            Tap,
            UseItem,
            Stagger,
            Down,
        }

        [Header("绑定")]
        [Tooltip("这个动画器驱动的座位。")]
        public int seatIndex = -1;

        [Tooltip("对局驱动。留空则自动查找。")]
        public GameDirector director;

        [Header("动作参数")]
        [Tooltip("对准格子时抬起手臂的角度（度）。")]
        public float aimArmAngle = 62f;

        [Tooltip("点格子时手臂下压的角度（度）。")]
        public float tapArmAngle = 6f;

        [Tooltip("使用道具时手臂抬起的高度（米）。")]
        public float useItemLift = 0.05f;

        [Tooltip("点格子动作持续时长（秒）。")]
        public float tapDuration = 0.34f;

        [Tooltip("使用道具动作持续时长（秒）。")]
        public float useItemDuration = 0.55f;

        [Tooltip("被炸后的踉跄时长（秒）。")]
        public float staggerDuration = 0.7f;

        [Tooltip("是否播放「保密动作」（使用道具）。关闭可避免通过动作泄露对手用了道具。")]
        public bool playSecretActions = false;

        [Header("状态（只读）")]
        [SerializeField] ActionState state = ActionState.Idle;

        public ActionState State => state;

        Transform _body, _armR, _armL, _handR, _handL, _mouth;
        Vector3 _bodyBase, _armRBase, _armLBase, _handRBase, _handLBase, _mouthBase;
        float _stateTimer;
        BombCharacter _character;
        bool _hooked;

        void Awake()
        {
            _character = GetComponent<BombCharacter>();
            RefreshRefs();
        }

        void OnDestroy() => Unhook();

        /// <summary>换皮肤后重建节点引用。</summary>
        public void RefreshRefs()
        {
            var body = transform.Find("Visual/BodyRoot");
            if (body == null) body = transform.Find("BodyRoot");

            _body = body;
            _armR = _armL = _handR = _handL = _mouth = null;

            if (_body == null) return;

            _bodyBase = _body.localPosition;
            _armR = _body.Find("ArmR");
            _armL = _body.Find("ArmL");
            _handR = _body.Find("HandR");
            _handL = _body.Find("HandL");
            _mouth = _body.Find("Mouth");

            if (_armR != null) _armRBase = _armR.localEulerAngles;
            if (_armL != null) _armLBase = _armL.localEulerAngles;
            if (_handR != null) _handRBase = _handR.localPosition;
            if (_handL != null) _handLBase = _handL.localPosition;
            if (_mouth != null) _mouthBase = _mouth.localScale;
        }

        public void Hook(GameDirector d)
        {
            Unhook();
            director = d;
            if (director == null) return;
            director.Event += OnGameEvent;
            _hooked = true;
        }

        void Unhook()
        {
            if (_hooked && director != null) director.Event -= OnGameEvent;
            _hooked = false;
        }

        void OnGameEvent(GameEvent e)
        {
            if (e.seatIndex != seatIndex) return;

            switch (e.type)
            {
                case GameEventType.CellRevealed:
                case GameEventType.CellFlagged:
                    Enter(ActionState.Tap, tapDuration);
                    break;

                case GameEventType.CellHit:
                    Enter(ActionState.Stagger, staggerDuration);
                    // 简单特效：踩雷处炸开一小团，比只有动作更能说明「刚才发生了什么」
                    SimpleVfx.OnExplosion(transform.position);
                    break;

                case GameEventType.ItemUsed:
                    // 使用道具对其他人保密时，不播放可见动作，避免通过动作泄露
                    if (!e.secret || playSecretActions)
                    {
                        Enter(ActionState.UseItem, useItemDuration);
                        SimpleVfx.OnCharacter(_character,
                            _character != null ? _character.accentColor : Color.white);
                    }
                    break;

                case GameEventType.PlayerEliminated:
                    Enter(ActionState.Down, float.MaxValue);
                    break;
            }
        }

        void Enter(ActionState next, float duration)
        {
            state = next;
            _stateTimer = duration;
        }

        /// <summary>换成带骨骼模型后，动作交给 Animator 播，不再摆程序化的姿势。</summary>
        RiggedBombView Rigged
        {
            get
            {
                if (_rigged == null) _rigged = GetComponentInChildren<RiggedBombView>(true);
                return _rigged;
            }
        }

        RiggedBombView _rigged;

        void Update()
        {
            var rigged = Rigged;
            if (rigged != null && rigged.IsValid)
            {
                DriveRigged(rigged);
                return;
            }

            if (state != ActionState.Idle && state != ActionState.Down && state != ActionState.Aim)
            {
                _stateTimer -= Time.deltaTime;
                if (_stateTimer <= 0f)
                    state = IsMyHover() ? ActionState.Aim : ActionState.Idle;
            }

            if (state == ActionState.Idle && IsMyHover()) state = ActionState.Aim;
            else if (state == ActionState.Aim && !IsMyHover() && _stateTimer <= 0f) state = ActionState.Idle;

            float t = Time.time;
            switch (state)
            {
                case ActionState.Aim: Pose(t, aimArmAngle, useItemLift * 0.2f, 1f, 1f); break;
                case ActionState.Tap: Pose(t, tapArmAngle, -0.01f, 1.5f, 1.3f); break;
                case ActionState.UseItem: Pose(t, aimArmAngle + 20f, useItemLift, 1.2f, 1.8f); break;
                case ActionState.Stagger: Pose(t, -18f, 0f, 1f, 2.4f, shake: 0.035f, lean: 14f); break;
                case ActionState.Down: Pose(t, -26f, 0f, 0.2f, 0.4f, lean: 26f); break;
                default: Pose(t, 0f, 0f, 1f, 1f); break;
            }
        }

        bool IsMyHover()
        {
            var s = director != null ? director.Session : null;
            if (s == null) return false;
            return s.HoverSeat == seatIndex && s.IsMyTurn(seatIndex);
        }

        RiggedBombView.State _lastRiggedState = (RiggedBombView.State)(-1);

        /// <summary>
        /// 把动作状态机翻译成骨骼动画。
        /// 状态机本身照常推进（Enter 由事件驱动），这里只负责「按状态切动画」，
        /// 并且只在状态真正变化时才切 —— 否则会在同一次动作里反复重置动画。
        /// </summary>
        void DriveRigged(RiggedBombView rigged)
        {
            if (state != ActionState.Idle && state != ActionState.Down && state != ActionState.Aim)
            {
                _stateTimer -= Time.deltaTime;
                if (_stateTimer <= 0f)
                    state = IsMyHover() ? ActionState.Aim : ActionState.Idle;
            }

            if (state == ActionState.Idle && IsMyHover()) state = ActionState.Aim;
            else if (state == ActionState.Aim && !IsMyHover() && _stateTimer <= 0f) state = ActionState.Idle;

            // 注视每帧都要更新（而不是只在状态切换时），否则身体是跟不上的
            UpdateGaze(rigged);

            var want = MapState(state);

            // 出局是终态：倒下并保持。
            // 不能像一次性动作那样「播完回待机」—— 那会让被淘汰的角色重新站起来。
            if (state == ActionState.Down)
            {
                if (!rigged.IsDown) rigged.PlayDown();
                _lastRiggedState = want;
                return;
            }

            // 回到待机（新一局 / 复活）时把人扶起来
            if (state == ActionState.Idle && rigged.IsDown) rigged.Revive();

            if (want == _lastRiggedState) return;
            _lastRiggedState = want;

            if (want == RiggedBombView.State.UseItem || want == RiggedBombView.State.Hit)
                rigged.PlayOneShot(want);
            else
                rigged.Play(want);
        }

        /// <summary>
        /// 把「正在看哪一格」翻译成一点身体偏转。
        ///
        /// 没有这一层，角色在瞄准时是根木头 —— 这是「动作僵硬」的主要来源：
        /// 骨骼动画只有循环播放的待机/指向，缺少「跟着视线动」的连续变化。
        /// 不需要精确算出射线的角度，只要把格子坐标归一化后映射到偏转角就够读了。
        /// </summary>
        void UpdateGaze(RiggedBombView rigged)
        {
            var s = director != null ? director.Session : null;

            if (state == ActionState.Down || s == null || s.IsOver)
            {
                rigged.ClearAimBias();
                return;
            }

            if (!s.IsMyTurn(seatIndex) || s.HoverSeat != seatIndex)
            {
                rigged.ClearAimBias();
                return;
            }

            var board = s.SharedBoard;
            if (board == null || board.Columns < 2 || board.Rows < 2)
            {
                rigged.ClearAimBias();
                return;
            }

            float nx = (s.HoverX / (float)(board.Columns - 1)) * 2f - 1f;
            float ny = (s.HoverY / (float)(board.Rows - 1)) * 2f - 1f;

            rigged.SetAimBias(nx * rigged.aimYawDegrees, ny * rigged.aimPitchDegrees);
        }

        static RiggedBombView.State MapState(ActionState s) => s switch
        {
            ActionState.Aim => RiggedBombView.State.Aim,
            ActionState.Tap => RiggedBombView.State.Aim,       // 点格子复用「指向」的动作
            ActionState.UseItem => RiggedBombView.State.UseItem,
            ActionState.Stagger => RiggedBombView.State.Hit,
            ActionState.Down => RiggedBombView.State.Hit,
            _ => RiggedBombView.State.Idle,
        };

        /// <summary>统一摆姿势：手臂角度 + 手部抬升 + 身体倾斜抖动。</summary>
        void Pose(float t, float armAngle, float handLift, float breathScale, float mouthOpen,
            float shake = 0f, float lean = 0f)
        {
            if (_body == null) return;

            // 手臂：以 Z 轴旋转抬起
            float wave = Mathf.Sin(t * 3.2f) * 2.5f;
            if (_armR != null)
                _armR.localEulerAngles = new Vector3(_armRBase.x, _armRBase.y, _armRBase.z - (armAngle + wave));
            if (_armL != null)
                _armL.localEulerAngles = new Vector3(_armLBase.x, _armLBase.y, _armLBase.z + (armAngle + wave) * 0.45f);

            // 手：跟着手臂一起抬
            if (_handR != null)
                _handR.localPosition = _handRBase + new Vector3(0.012f, handLift, 0.006f);
            if (_handL != null)
                _handL.localPosition = _handLBase + new Vector3(-0.006f, handLift * 0.4f, 0f);

            // 嘴：动作越大张得越开。
            // 正在说话时交给 BombCharacter 的语音口型驱动，避免两个组件抢同一个节点。
            bool talking = _character != null && _character.IsTalking;
            if (_mouth != null && !talking)
                _mouth.localScale = new Vector3(_mouthBase.x, _mouthBase.y * mouthOpen, _mouthBase.z);

            // 身体：呼吸 / 颤抖 / 倾斜
            float breath = Mathf.Sin(t * 1.7f) * 0.004f;
            float tremor = shake > 0f ? Mathf.Sin(t * 34f) * shake : 0f;

            var pos = _bodyBase + new Vector3(tremor, breath, 0f);
            _body.localPosition = pos;
            _body.localRotation = Quaternion.Euler(lean, 0f, tremor * 220f);
        }
    }
}
