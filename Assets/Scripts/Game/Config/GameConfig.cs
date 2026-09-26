using UnityEngine;

namespace BombTavern.Config
{
    /// <summary>
    /// 全局对局配置。所有可调数值集中在此，禁止散落魔法数字。
    /// 资产路径：Assets/LiarsBar/Game/Config/GameConfig.asset
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "炸弹酒馆/配置/对局配置", order = 0)]
    public class GameConfig : ScriptableObject
    {
        [Header("座位与人数")]
        [Tooltip("桌子总座位数（即最大参战炸弹数量）。")]
        [Range(1, 4)] public int totalSeats = 4;

        [Tooltip("真人玩家数量。剩余座位自动由 AI 机器人补齐。")]
        [Range(1, 4)] public int humanPlayerCount = 1;

        [Header("座位布局")]
        [Tooltip("各座位的角度（度，绕 Y 轴，0 度朝 +Z），四名玩家围桌而坐。场景装配与运行时重新就坐都读它。")]
        public float[] seatAnglesDegrees = { 45f, 135f, 225f, 315f };

        [Tooltip("玩家就坐时离桌心的距离（米）。要大于桌面半径，否则会站在桌子里。")]
        public float seatStandRadius = 1.62f;

        [Header("角色显示名")]
        [Tooltip("各座位炸弹角色的显示名，按座位顺序。数量不足时自动使用「炸弹N号」。")]
        public string[] seatDisplayNames = { "红雷", "蓝焰", "青引", "金牙" };

        [Header("机会（生命）")]
        [Tooltip("每名玩家开局拥有的机会次数，用光即出局（炸弹引爆）。")]
        [Range(1, 10)] public int startingLives = 3;

        [Tooltip("被炸掉一次机会时，是否清空该玩家当前已翻开的格子信息（更惩罚）。")]
        public bool clearRevealedOnHit = false;

        [Header("道具")]
        [Tooltip("开局每人获得的道具数量。")]
        [Range(0, 8)] public int startingItemCount = 2;

        [Tooltip("道具背包上限，超出时新道具丢弃。")]
        [Range(1, 12)] public int maxInventorySize = 6;

        [Tooltip("每回合每名玩家可使用的道具次数上限。0 表示不限制。")]
        [Range(0, 5)] public int maxItemsPerTurn = 1;

        [Header("回合")]
        [Tooltip("对局最大回合数。0 表示不限回合，直到只剩一名玩家。")]
        [Range(0, 200)] public int maxTurns = 0;

        [Tooltip("单回合思考/操作限时（秒）。0 表示不限时。")]
        [Range(0f, 300f)] public float turnTimeLimitSeconds = 0f;

        [Header("回合行为")]
        [Tooltip("翻开格子后是否立即结束当前回合。")]
        public bool revealEndsTurn = true;

        [Tooltip("插旗是否算作一次主行动并结束回合。关闭则插旗不消耗回合。")]
        public bool flagEndsTurn = false;

        [Tooltip("使用道具是否结束回合。关闭则可以先用药再翻格。")]
        public bool itemUseEndsTurn = false;

        [Header("概率漂移")]
        [Tooltip("是否启用每回合的隐藏概率漂移（信息失真的来源）。")]
        public bool driftEnabled = true;

        [Tooltip("漂移参数资产。")]
        public DriftProfile driftProfile;

        [Header("共用盘面")]
        [Tooltip("所有玩家是否共用同一块扫雷盘面（酒馆中央的那块大屏）。关闭则每人一块私有盘面。")]
        public bool sharedBoard = true;

        [Tooltip("是否把当前行动玩家的鼠标悬停格子公开给所有人观看。")]
        public bool publishHover = true;

        [Tooltip("悬停信息同步的最小间隔（秒），避免刷屏。0 表示每次移动都同步。")]
        [Range(0f, 1f)] public float hoverSyncInterval = 0.12f;

        [Header("道具保密")]
        [Tooltip("使用道具时是否对其他人保密（不进入公共事件流、不改变公共显示颜色）。")]
        public bool itemUseIsSecret = true;

        [Tooltip("道具使用保密时，是否仍然把「使用道具」这个事件下发给其他玩家，好演出拿出道具的动作。\n" +
                 "开：别人看得到动作，但 HUD 不会显示道具名，效果也不公开（只能靠动作形态猜）。\n" +
                 "关：完全无痕，对手连动作都看不到。")]
        public bool itemUseAnimationVisible = false;

        [Tooltip("探查类道具是否改写公共显示值。关闭时只记录到使用者自己的私人读数中（更符合保密设定）。")]
        public bool probeRefreshesPublicDisplay = false;

        [Header("难度")]
        [Tooltip("默认难度预设，决定盘面尺寸与初始风险分布。")]
        public DifficultyPreset difficulty;

        [Header("道具掉落与保底")]
        [Tooltip("掉落与抽卡保底参数。")]
        public GachaConfig gacha;

        [Header("颜色映射")]
        [Tooltip("风险值到格子颜色的映射表。")]
        public RiskColorRamp colorRamp;

        [Header("AI")]
        [Tooltip("AI 每步操作的思考延迟（秒），用于让玩家看清过程。")]
        [Range(0f, 5f)] public float botThinkDelaySeconds = 0.9f;

        [Tooltip("AI 对风险的容忍度：0=极度保守（只碰接近 0 的格子），1=极度激进。")]
        [Range(0f, 1f)] public float botRiskTolerance = 0.45f;

        [Tooltip("AI 使用探查道具后，认为值得继续翻开的剩余风险阈值。")]
        [Range(0f, 1f)] public float botProbeAcceptThreshold = 0.2f;

        [Tooltip("AI 觉得需要先动用降险/探查道具的风险阈值。低于容忍度则直接翻。")]
        [Range(0f, 1f)] public float botUseItemRiskThreshold = 0.35f;

        [Tooltip("AI 在自己局面尚可时，主动用「增险」道具暗算领先对手的概率。")]
        [Range(0f, 1f)] public float botSabotageChance = 0.35f;

        [Tooltip("AI 在翻格之前先探查一下最安全格子的概率。这个概率随轮次增长——" +
                 "对应「拖得越久越不敢相信屏幕上的旧信息」的设计意图。")]
        [Range(0f, 1f)] public float botProbeBeforeRevealChance = 0.3f;

        [Tooltip("每过一轮，上面那个探查概率增加多少（绝对值）。")]
        [Range(0f, 0.2f)] public float botProbeChancePerRound = 0.04f;

        [Tooltip("AI 在使用降险道具时，愿意把风险压到多低才动手（低于容忍度则直接翻，省道具）。")]
        [Range(0f, 1f)] public float botDecreaseTargetRisk = 0.3f;

        [Tooltip("自动代打：把本地玩家的座位也交给 AI。用于观战演示，以及无头环境下的自动化验证。")]
        public bool autoPlayLocalSeat = false;

        [Header("联机")]
        [Tooltip("局域网联机默认端口。")]
        [Range(1024, 65535)] public int defaultPort = 7788;

        [Tooltip("单个客户端等待连接超时（秒）。")]
        [Range(1f, 60f)] public float connectTimeoutSeconds = 10f;

        [Header("表现")]
        [Tooltip("炸弹角色爆炸时的镜头震动强度。")]
        [Range(0f, 1f)] public float explosionShakeStrength = 0.5f;
    }
}
