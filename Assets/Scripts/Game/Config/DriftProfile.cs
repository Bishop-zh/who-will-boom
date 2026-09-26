using UnityEngine;

namespace BombTavern.Config
{
    /// <summary>
    /// 概率漂移参数：每回合所有格子的真实爆炸概率会微弱变化，
    /// 而玩家看到的显示值不刷新 → 信息逐渐过期，这是本作的核心博弈点。
    /// 资产路径：Assets/LiarsBar/Game/Config/Drift_Default.asset
    /// </summary>
    /// <summary>漂移作用范围。</summary>
    public enum DriftScope
    {
        [Tooltip("只漂移当前行动玩家自己的盘面。")]
        OwnBoardOnly,
        [Tooltip("漂移所有玩家的盘面（默认，视角更一致）。")]
        AllBoards,
    }

    [CreateAssetMenu(fileName = "Drift_", menuName = "炸弹酒馆/配置/漂移参数", order = 2)]
    public class DriftProfile : ScriptableObject
    {
        [Header("作用范围与时机")]
        [Tooltip("漂移作用到哪些盘面。")]
        public DriftScope scope = DriftScope.AllBoards;

        [Tooltip("漂移发生在每个玩家回合结束时。开启则改为在该玩家回合开始时漂移。")]
        public bool driftAtTurnStartInsteadOfEnd = false;

        [Header("基础漂移幅度")]
        [Tooltip("每个回合每格真实概率的基础变化幅度（绝对值）。玩家描述为 5%。")]
        [Range(0f, 0.3f)] public float baseDriftMagnitude = 0.05f;

        [Tooltip("漂移幅度的随机浮动范围：实际幅度 = 基础幅度 * [1-var, 1+var]。")]
        [Range(0f, 1f)] public float magnitudeVariance = 0.5f;

        [Header("方向")]
        [Tooltip("概率上升（变危险）的概率权重。0.5 表示涨跌均等。")]
        [Range(0f, 1f)] public float increaseChance = 0.5f;

        [Tooltip("漂移方向是否与该格当前概率相关：概率越高越容易继续升高（风险聚集）。0 表示无关。")]
        [Range(0f, 1f)] public float riskMomentum = 0.3f;

        [Header("范围与钳制")]
        [Tooltip("是否只漂移尚未被翻开的格子。已翻开的格子通常是已探明区域，保持稳定更易理解。")]
        public bool skipRevealedCells = true;

        [Tooltip("漂移后概率的下限。")]
        [Range(0f, 0.5f)] public float minRisk = 0.0f;

        [Tooltip("漂移后概率的上限。")]
        [Range(0.1f, 1f)] public float maxRisk = 0.95f;

        [Header("回合强度")]
        [Tooltip("每回合漂移多少次（>1 表示一回合内多次微调，效果更混乱）。")]
        [Range(1, 5)] public int driftPassesPerTurn = 1;

        [Tooltip("每经过一个完整回合（所有人都行动过一次），漂移幅度的放大系数。1 表示不放大。")]
        [Range(1f, 1.5f)] public float roundEscalation = 1.03f;

        [Tooltip("漂移幅度放大到多少倍后封顶。")]
        [Range(1f, 6f)] public float maxEscalation = 2.5f;
    }
}
