using UnityEngine;

namespace BombTavern.Config
{
    /// <summary>
    /// 难度预设：参照传统扫雷的雷密度分布生成"概率风险场"。
    /// 传统扫雷密度：初级 9x9/10 雷 = 12.3%；中级 16x16/40 雷 = 15.6%；高级 16x30/99 雷 = 20.6%。
    /// 资产路径：Assets/LiarsBar/Game/Config/Difficulty_*.asset
    /// </summary>
    [CreateAssetMenu(fileName = "Difficulty_", menuName = "炸弹酒馆/配置/难度预设", order = 1)]
    public class DifficultyPreset : ScriptableObject
    {
        [Header("显示")]
        [Tooltip("在 UI 上展示的难度名称。")]
        public string displayName = "中级";

        [Tooltip("难度说明，展示在平板标题栏。")]
        [TextArea(2, 4)] public string description = "接近传统扫雷中级：16x16，平均风险 15.6%。";

        [Header("盘面尺寸")]
        [Tooltip("横向格子数。")]
        [Range(4, 40)] public int columns = 12;

        [Tooltip("纵向格子数。")]
        [Range(4, 30)] public int rows = 10;

        [Header("风险密度（对应传统扫雷的雷占比）")]
        [Tooltip("每格的基础爆炸概率均值。传统扫雷：初级 0.123 / 中级 0.156 / 高级 0.206。")]
        [Range(0f, 1f)] public float baseRisk = 0.156f;

        [Tooltip("在基础概率上下浮动的最大幅度，使概率有疏密分布而非均匀。")]
        [Range(0f, 1f)] public float riskVariance = 0.12f;

        [Header("风险簇（模拟传统扫雷的雷区聚集）")]
        [Tooltip("风险簇数量，按盘面格数比例计算：簇数 = 格数 * 该比例。簇中心附近概率显著更高。")]
        [Range(0f, 0.5f)] public float clusterDensity = 0.06f;

        [Tooltip("簇中心相对于基础概率的放大倍数。")]
        [Range(1f, 6f)] public float clusterBoost = 2.6f;

        [Tooltip("距离簇中心多远后放大效果衰减到 0（以格为单位）。")]
        [Range(1f, 12f)] public float clusterRadius = 3.5f;

        [Header("安全区")]
        [Tooltip("开局保证安全的格子比例（在最安全的一批格子里随机取）。用于给玩家一个起步点。")]
        [Range(0f, 0.5f)] public float guaranteedSafeRatio = 0.08f;

        [Tooltip("被标记为安全时，该格的爆炸概率上限。")]
        [Range(0f, 0.2f)] public float safeRiskCeiling = 0.02f;

        [Header("初始可见信息")]
        [Tooltip("开局是否让所有格子的显示值等于真实值（对应规则：初始所有格子颜色都正确标记）。")]
        public bool revealAllAtStart = true;

        [Tooltip("开局自动翻开的格子数量（作为传统扫雷的开局展开区域）。")]
        [Range(0, 12)] public int startRevealedCount = 0;

        [Tooltip("已翻开格子上的「相邻期望雷数」是否实时更新。关闭则只在翻开瞬间快照（与颜色规则一致，信息会过期）。")]
        public bool neighborNumbersAreLive = false;
    }
}
