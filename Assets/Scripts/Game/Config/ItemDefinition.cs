using UnityEngine;

namespace BombTavern.Config
{
    /// <summary>道具效果类型。</summary>
    public enum ItemEffectType
    {
        [Tooltip("探查一格：把该格显示风险刷新为真实风险")]
        ProbeCell,
        [Tooltip("升高一格的真实爆炸概率")]
        IncreaseRisk,
        [Tooltip("降低一格的真实爆炸概率")]
        DecreaseRisk,
        [Tooltip("护盾：抵挡一次爆炸，不消耗机会")]
        Shield,
        [Tooltip("扫描整行：刷新该行所有格子的显示风险")]
        ScanRow,
        [Tooltip("扫描范围：刷新以目标格为中心的圆形区域内所有格子")]
        ScanArea,
        [Tooltip("冻结漂移：在若干回合内使全盘真实概率不再变化")]
        FreezeDrift,
        [Tooltip("透视：翻开目标格周围 8 格的期望雷数")]
        RevealNeighbors,
        [Tooltip("换手：强制把当前回合交给指定的下一名玩家")]
        SkipTurn,
    }

    /// <summary>道具可以作用在谁的盘面上。</summary>
    public enum ItemTargetScope
    {
        [Tooltip("只能作用于自己的平板")]
        SelfBoard,
        [Tooltip("只能作用于其他玩家的平板（用于暗算）")]
        OpponentBoard,
        [Tooltip("自己或他人都可以")]
        AnyBoard,
    }

    /// <summary>道具稀有度，用于掉落权重与 UI 展示。</summary>
    public enum ItemRarity
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3,
    }

    /// <summary>
    /// 单件道具定义。数值全部可调，禁止写死在逻辑里。
    /// 资产路径：Assets/LiarsBar/Game/Data/Items/*.asset
    /// </summary>
    [CreateAssetMenu(fileName = "Item_", menuName = "炸弹酒馆/道具/道具定义", order = 10)]
    public class ItemDefinition : ScriptableObject
    {
        [Header("标识")]
        [Tooltip("唯一 ID，存档与联机同步用。改动会导致旧存档/旧客户端不兼容。")]
        public string itemId = "probe";

        [Tooltip("UI 显示名。")]
        public string displayName = "探查器";

        [Tooltip("UI 说明文本，建议写清数值影响。")]
        [TextArea(2, 4)] public string description = "查看目标格子的真实爆炸概率。";

        [Tooltip("稀有度，影响掉落权重与边框颜色。")]
        public ItemRarity rarity = ItemRarity.Common;

        [Tooltip("分类标签，仅用于策划分类与批量筛选（例如「情报」「进攻」「防守」）。不参与逻辑。")]
        public string category = "";

        [Tooltip("UI 排序序号，越小越靠前。")]
        public int sortOrder = 0;

        [Header("开关")]
        [Tooltip("关掉后这个道具**不会**出现在掉落池、开局手牌和抽卡池里。\n" +
                 "用来「临时禁用」一个道具而不必删资产 —— 删除资产会让引用它的场景与旧存档一起坏掉。")]
        public bool enabled = true;

        [Header("效果")]
        [Tooltip("效果类型。")]
        public ItemEffectType effect = ItemEffectType.ProbeCell;

        [Tooltip("是否需要玩家选定一个目标格。")]
        public bool requiresTarget = true;

        [Tooltip("作用目标范围：自己的平板 / 他人的平板 / 任意。选「他人」时会变成暗算道具。")]
        public ItemTargetScope targetScope = ItemTargetScope.SelfBoard;

        [Tooltip("真实爆炸概率的调整量（IncreaseRisk 取正、DecreaseRisk 取正、Defuse 类可为负）。")]
        [Range(-1f, 1f)] public float riskDelta = 0.25f;

        [Tooltip("作用半径（按格数）。0 表示只影响目标格本身。")]
        [Range(0, 6)] public int radius = 0;

        [Tooltip("持续回合数（用于 FreezeDrift 这类状态型道具）。0 表示即时生效。")]
        [Range(0, 10)] public int durationTurns = 0;

        [Tooltip("护盾可抵挡的爆炸次数（Shield 类道具）。")]
        [Range(1, 5)] public int shieldCharges = 1;

        [Header("是否同步显示值")]
        [Tooltip("修改真实风险后，是否同时刷新该格的显示风险。关闭则玩家改完也看不到结果（更阴险）。")]
        public bool refreshDisplayAfterModify = true;

        [Header("掉落")]
        [Tooltip("掉落池权重，越大越容易掉出。")]
        [Range(0f, 100f)] public float dropWeight = 30f;

        [Tooltip("是否允许出现在开局发放的初始道具里。")]
        public bool canBeStartingItem = true;

        /// <summary>能不能进入掉落 / 发牌流程。禁用或权重为 0 都不行。</summary>
        public bool IsDroppable => enabled && dropWeight > 0f;

        /// <summary>
        /// 策划填错时的自检。返回 null 表示没问题。
        /// 放在定义上而不是校验工具里，是为了让「加新道具」时就能立刻发现自己填漏了。
        /// </summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(itemId)) return "itemId 为空";
            if (string.IsNullOrWhiteSpace(displayName)) return $"{itemId}: displayName 为空";

            switch (effect)
            {
                case ItemEffectType.IncreaseRisk:
                case ItemEffectType.DecreaseRisk:
                    if (riskDelta <= 0f) return $"{itemId}: 风险调整量必须大于 0（方向由效果类型决定）";
                    break;

                case ItemEffectType.ScanArea:
                    if (radius < 1) return $"{itemId}: 范围扫描的 radius 至少为 1";
                    break;

                case ItemEffectType.FreezeDrift:
                    if (durationTurns < 1) return $"{itemId}: 冻结漂移的 durationTurns 至少为 1";
                    break;

                case ItemEffectType.Shield:
                    if (shieldCharges < 1) return $"{itemId}: 护盾次数至少为 1";
                    break;
            }

            if (enabled && dropWeight <= 0f && canBeStartingItem)
                return $"{itemId}: 掉落权重为 0 却允许作为初始道具，会导致它只能开局拿到";

            return null;
        }
    }

    // 说明：道具目录 ItemCatalog 单独放在 ItemCatalog.cs 中。
    // Unity 要求 ScriptableObject 的类名与文件名一致，
    // 写在本文件里会导致 ItemCatalog.asset 的脚本引用无法解析。
}
