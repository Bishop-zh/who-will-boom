using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 带骨骼的炸弹角色外观。
    ///
    /// 它把 Animator 包一层，对外只暴露四个状态（Idle / Aim / UseItem / Hit），
    /// 上层（BombActionAnimator、ItemPropPresenter）不需要知道动画是怎么组织的。
    ///
    /// 状态切换全部由代码驱动，不在 AnimatorController 里连过渡线 ——
    /// 用 <c>CrossFadeInFixedTime</c> 可以直接切到任意状态，省掉一堆
    /// 「谁在什么条件下转到谁」的连线，改起来不容易出错。
    /// </summary>
    [DisallowMultipleComponent]
    public class RiggedBombView : MonoBehaviour
    {
        public enum State { Idle, Aim, UseItem, Hit }

        // 使用完整状态路径并显式指定第 0 层。只传短名并让 layer=-1 自动搜索时，
        // 控制器刚完成热重载的首帧可能报 State could not be found / Invalid Layer Index。
        static readonly int HashIdle = Animator.StringToHash("Base Layer.Idle");
        static readonly int HashAim = Animator.StringToHash("Base Layer.Aim");
        static readonly int HashUseItem = Animator.StringToHash("Base Layer.UseItem");
        static readonly int HashHit = Animator.StringToHash("Base Layer.Hit");

        [Tooltip("驱动模型的 Animator。")]
        public Animator animator;

        [Tooltip("右手骨骼。道具会挂到这里。")]
        public Transform rightHand;

        [Tooltip("右上臂骨骼。用于在动画上叠加视线跟随。")]
        public Transform rightUpperArm;

        [Tooltip("右前臂骨骼。用于让手腕更自然地指向当前格子。")]
        public Transform rightForearm;

        [Header("一次性动作时长（秒）")]
        [Tooltip("使用道具动作播完后自动回到待机的时间。")]
        public float useItemSeconds = 1.15f;

        [Tooltip("被炸动作播完后自动回到待机的时间。")]
        public float hitSeconds = 1.5f;

        [Tooltip("对准动作在没有新指令后自动回到待机的时间。")]
        public float aimSeconds = 1.2f;

        [Header("待机加成")]
        [Tooltip("待机时在模型根节点上叠加的呼吸起伏（米）。" +
                 "剪辑自带的 Idle 幅度很小（脊椎仅 1.2cm），跨桌看不出来，叠加一点才像活的。")]
        public float idleBobAmplitude = 0.024f;

        [Tooltip("待机呼吸频率（次/秒）。")]
        public float idleBobSpeed = 1.55f;

        [Tooltip("待机左右摇摆幅度（度）。")]
        public float idleSwayDegrees = 2.6f;

        [Header("倒地")]
        [Tooltip("出局后倒下的角度（度）。负值向后倒（离开桌面），正值向前趴在桌上。")]
        public float downTiltDegrees = -78f;

        [Tooltip("倒地过程时长（秒）。")]
        public float downSeconds = 0.75f;

        bool _returnPending;
        float _returnAt;
        State _current = State.Idle;

        [Header("注视")]
        [Tooltip("对准格子时，身体最多朝该方向偏转多少度。让「他在看哪里」有可见的身体语言。")]
        public float aimYawDegrees = 9f;

        [Tooltip("对准格子时，身体最多前倾/后仰多少度。")]
        public float aimPitchDegrees = 7f;

        [Tooltip("注视偏转的跟随速度。越小越柔，越大越跟手。")]
        public float aimFollowSpeed = 3.2f;

        bool _down;
        float _downAt;
        Vector3 _baseLocalPos;
        Quaternion _baseLocalRot = Quaternion.identity;
        float _phase;

        // 注视偏转：目标值由 BombActionAnimator 每帧写入，这里做平滑跟随。
        // 直接赋值会「啪」地跳过去，看着很僵；插值之后才有转头的感觉。
        float _aimYaw, _aimPitch;
        float _aimYawTarget, _aimPitchTarget;
        bool _hasAim;

        /// <summary>
        /// 设置注视偏转目标（度）。
        /// yaw 正 = 向右看，pitch 正 = 低头。传 false 表示不再注视，慢慢回到正中。
        /// </summary>
        public void SetAimBias(float yawDeg, float pitchDeg)
        {
            _aimYawTarget = Mathf.Clamp(yawDeg, -aimYawDegrees, aimYawDegrees);
            _aimPitchTarget = Mathf.Clamp(pitchDeg, -aimPitchDegrees, aimPitchDegrees);
            _hasAim = true;
        }

        /// <summary>取消注视，身体慢慢回正。</summary>
        public void ClearAimBias()
        {
            _hasAim = false;
            _aimYawTarget = 0f;
            _aimPitchTarget = 0f;
        }

        public Transform RightHand => rightHand;
        public bool IsValid => animator != null;

        /// <summary>是否已经处于出局倒地状态。</summary>
        public bool IsDown => _down;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // 记下模型自带的基础变换。FBX 导出常常带一个 -90° 之类的根旋转，
            // 直接覆写 localRotation 会让整个模型躺倒，所以必须叠加而不是赋值。
            _baseLocalPos = transform.localPosition;
            _baseLocalRot = transform.localRotation;
            _phase = Random.value * Mathf.PI * 2f;
        }

        void Start()
        {
            if (animator != null && animator.layerCount > 0 && animator.HasState(0, HashIdle))
                animator.CrossFadeInFixedTime(HashIdle, 0.05f, 0);
            _current = State.Idle;
        }

        /// <summary>
        /// 出局：倒下并**一直保持**。
        /// 原来出局复用 Hit 动作，而 Hit 是「一次性动作，1.5 秒后回待机」，
        /// 结果被淘汰的角色会重新站起来装作没事 —— 这正是「没有死亡动画」的原因。
        /// </summary>
        public void PlayDown()
        {
            _returnPending = false;
            if (_down) return;

            _down = true;
            _downAt = Time.time;
            CrossFade(State.Hit);
        }

        /// <summary>重新开局：站起来回到待机。</summary>
        public void Revive()
        {
            if (!_down) return;
            _down = false;
            _returnPending = false;
            CrossFade(State.Idle);
        }

        void Update()
        {
            if (_returnPending && Time.time >= _returnAt)
            {
                _returnPending = false;
                CrossFade(State.Idle);
            }

            ApplyProceduralPose();
        }

        void LateUpdate()
        {
            // Animator 在 Update 阶段写骨骼；这里晚一拍叠加偏转，既保留 Aim/UseItem 剪辑，
            // 又让右手连续跟随当前注视格，而不是只能播固定方向的动作。
            if (_down || !_hasAim) return;

            float yaw = aimYawDegrees > 0.001f ? _aimYaw / aimYawDegrees : 0f;
            float pitch = aimPitchDegrees > 0.001f ? _aimPitch / aimPitchDegrees : 0f;
            if (rightUpperArm != null)
                rightUpperArm.localRotation *= Quaternion.Euler(pitch * 10f, yaw * 16f, -yaw * 7f);
            if (rightForearm != null)
                rightForearm.localRotation *= Quaternion.Euler(pitch * 7f, yaw * 11f, 0f);
            if (rightHand != null)
                rightHand.localRotation *= Quaternion.Euler(pitch * 4f, yaw * 6f, -yaw * 3f);
        }

        /// <summary>
        /// 程序化叠加层：待机呼吸 / 摇摆，以及出局后的倒地。
        ///
        /// 只写 **自己这个 GameObject**（模型根）的变换，不碰骨骼。
        /// 动画剪辑只驱动子级骨骼，且 applyRootMotion = false，
        /// 所以这一层和 Animator 互不干扰，剪辑播不出来时也至少还有呼吸。
        /// </summary>
        void ApplyProceduralPose()
        {
            float t = Time.time;

            float tilt = 0f;
            if (_down)
            {
                float k = Mathf.Clamp01((t - _downAt) / Mathf.Max(0.05f, downSeconds));
                // 缓出：起手快、落定慢，看起来才有重量
                tilt = downTiltDegrees * (1f - Mathf.Pow(1f - k, 3f));
            }

            float bob = _down ? 0f : Mathf.Sin(t * idleBobSpeed + _phase) * idleBobAmplitude;
            float sway = _down ? 0f : Mathf.Sin(t * idleBobSpeed * 0.62f + _phase) * idleSwayDegrees;

            // 注视偏转平滑跟随。用 1-exp 而不是 Lerp(…, dt*k)，
            // 这样帧率变化时跟随手感一致，不会掉帧就变迟钝。
            float follow = 1f - Mathf.Exp(-aimFollowSpeed * Time.deltaTime);
            _aimYaw = Mathf.Lerp(_aimYaw, _aimYawTarget, follow);
            _aimPitch = Mathf.Lerp(_aimPitch, _aimPitchTarget, follow);

            transform.localPosition = _baseLocalPos + new Vector3(0f, bob, 0f);
            transform.localRotation = _baseLocalRot * Quaternion.Euler(
                tilt + sway * 0.35f + _aimPitch,
                _aimYaw,
                sway);
        }

        /// <summary>切到某个状态并保持（Idle 会一直循环）。</summary>
        public void Play(State state)
        {
            // 必须先取消「一次性动作的自动回待机」，
            // 否则上一个一次性动作留下的定时器会把刚切过去的持续状态又拉回 Idle。
            _returnPending = false;
            CrossFade(state);
        }

        /// <summary>播一次动作，然后自动回到待机。</summary>
        public void PlayOneShot(State state)
        {
            CrossFade(state);

            _returnPending = true;
            _returnAt = Time.time + DurationOf(state);
        }

        float DurationOf(State state) => state switch
        {
            State.UseItem => useItemSeconds,
            State.Hit => hitSeconds,
            State.Aim => aimSeconds,
            _ => 0f,
        };

        void CrossFade(State state)
        {
            if (animator == null || animator.layerCount <= 0) return;

            _current = state;
            // 过渡给足一点：0.10 秒的硬切在近景看就是「啪」地跳一下，
            // 0.22 秒才像真的在动身体。
            int hash = HashOf(state);
            if (animator.HasState(0, hash))
                animator.CrossFadeInFixedTime(hash, 0.22f, 0);
        }

        static int HashOf(State state) => state switch
        {
            State.Aim => HashAim,
            State.UseItem => HashUseItem,
            State.Hit => HashHit,
            _ => HashIdle,
        };
    }

    /// <summary>
    /// 带骨骼模型库：skinId → Resources 里的 FBX + AnimatorController。
    ///
    /// 走 Resources 而不是 AssetDatabase，是因为 AssetDatabase 只在编辑器可用，
    /// 打进包以后取不到。资源由 Tools/Blender 下的脚本生成到这个目录。
    /// </summary>
    public static class RiggedCharacterLibrary
    {
        const string CharacterRoot = "BombTavern/Characters/";
        const string ControllerRoot = "BombTavern/Controllers/";
        const string PrefabRoot = "BombTavern/Prefabs/";

        static readonly string[] KnownIds =
        {
            "black_sphere", "red_bundle", "brown_cracker", "gray_shell",
        };

        /// <summary>这个皮肤有没有对应的带骨骼模型。</summary>
        public static bool Has(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return false;
            for (int i = 0; i < KnownIds.Length; i++)
                if (KnownIds[i] == skinId) return true;
            return false;
        }

        /// <summary>
        /// 在 parent 下实例化带骨骼模型。
        /// 资源缺失时返回 null —— 调用方必须回退到程序化拼装，不能让游戏开天窗。
        /// </summary>
        public static RiggedBombView Instantiate(string skinId, Transform parent)
        {
            if (!Has(skinId) || parent == null) return null;

            // 优先用编辑器烘好的预制体 —— 关键是它带着 **Avatar**。
            // Generic 骨骼的 Animator 没有 Avatar 就等于空转，
            // 这正是「角色站着不动、没有待机与死亡动作」的根因。
            var prefab = Resources.Load<GameObject>(PrefabRoot + "Bomb_" + skinId);
            bool staged = prefab != null;
            if (prefab == null) prefab = Resources.Load<GameObject>(CharacterRoot + "Bomb_" + skinId);

            if (prefab == null)
            {
                Debug.LogWarning($"[角色] {skinId}: Resources 里找不到模型，回退到程序化拼装。");
                return null;
            }

            var go = Object.Instantiate(prefab, parent, false);
            go.name = "RiggedModel";

            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                // 兜底：模型没带 Animator 时补一个。
                // 但补出来的 Avatar 一定是空的，动画播不出来 —— 只保证「至少有静态模型」。
                var skinned = go.GetComponentInChildren<SkinnedMeshRenderer>();
                animator = (skinned != null ? skinned.gameObject : go).AddComponent<Animator>();
                Debug.LogWarning($"[角色] {skinId}: 用的是未烘焙的 FBX（无 Avatar），动画不会播放。" +
                                 "请执行「工具/炸弹酒馆/3. 生成角色骨骼预制体」。");
            }

            if (animator.runtimeAnimatorController == null)
            {
                var controller = Resources.Load<RuntimeAnimatorController>(
                    ControllerRoot + "Bomb_" + skinId + "_Controller");
                if (controller != null) animator.runtimeAnimatorController = controller;
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var view = go.AddComponent<RiggedBombView>();
            view.animator = animator;
            view.rightHand = FindBone(go.transform, "ArmR_Hand");
            view.rightUpperArm = FindBone(go.transform, "ArmR_Upper");
            view.rightForearm = FindBone(go.transform, "ArmR_Fore");

            // 诊断：动画不出来时，这几项是分辨「资源没加载」和
            // 「加载了但 Avatar / 控制器没接上」的唯一依据。
            var skinnedDiag = go.GetComponentInChildren<SkinnedMeshRenderer>();
            Debug.Log($"[角色] {skinId}: 预制体={staged} " +
                      $"控制器={(animator.runtimeAnimatorController != null)} " +
                      $"Avatar={(animator.avatar != null)} " +
                      $"蒙皮骨骼={(skinnedDiag != null ? skinnedDiag.bones.Length : 0)} " +
                      $"右手={(view.rightHand != null)}");

            return view;
        }

        /// <summary>
        /// 按名字找骨骼。FBX 导回来时骨骼名可能带命名空间前缀，
        /// 所以用「包含」匹配而不是全等。
        /// </summary>
        static Transform FindBone(Transform root, string boneName)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == boneName) return all[i];
            }
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name.Contains(boneName)) return all[i];
            }
            return null;
        }
    }
}
