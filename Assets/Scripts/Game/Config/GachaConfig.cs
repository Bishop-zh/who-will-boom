using UnityEngine;

namespace BombTavern.Config
{
    /// <summary>
    /// 道具掉落与"抽卡保底"参数。
    /// 规则：每名玩家拥有**独立的**掉落概率；连续未掉落会累积保底计数，达到硬保底必出。
    /// 资产路径：Assets/LiarsBar/Game/Config/Gacha_Default.asset
    /// </summary>
    [CreateAssetMenu(fileName = "Gacha_", menuName = "炸弹酒馆/配置/道具掉落", order = 3)]
    public class GachaConfig : ScriptableObject
    {
        [Header("基础掉落概率（每名玩家独立）")]
        [Tooltip("开局为每名玩家分配掉落概率的基准值。")]
        [Range(0f, 1f)] public float baseFindChance = 0.18f;

        [Tooltip("每名玩家掉落概率相对基准值的随机浮动幅度（±）。用于制造「运气好坏」的个体差异。")]
        [Range(0f, 0.5f)] public float perPlayerChanceVariance = 0.07f;

        [Tooltip("单名玩家掉落概率的上下限。")]
        [Range(0f, 1f)] public float minFindChance = 0.03f;
        [Range(0f, 1f)] public float maxFindChance = 0.6f;

        [Header("保底机制")]
        [Tooltip("软保底起点：连续未掉落达到该次数后，掉落概率开始逐步提升。")]
        [Range(0, 40)] public int softPityStart = 6;

        [Tooltip("软保底每超出一次，掉落概率增加的绝对值。")]
        [Range(0f, 0.3f)] public float softPityStep = 0.06f;

        [Tooltip("硬保底：连续未掉落达到该次数时必定掉落。")]
        [Range(1, 60)] public int hardPityThreshold = 14;

        [Tooltip("成功掉落道具后，是否把保底计数清零。")]
        public bool resetPityOnDrop = true;

        [Tooltip("成功掉落但背包已满（道具被丢弃）时，是否也清零保底计数。")]
        public bool resetPityWhenInventoryFull = false;

        [Header("掉落内容")]
        [Tooltip("掉落池。为空时使用项目内的默认道具目录。")]
        public ItemCatalog dropPool;

        [Tooltip("是否允许掉出玩家已经持有的同一道具。")]
        public bool allowDuplicates = true;
    }
}
