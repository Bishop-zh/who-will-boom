using System;
using System.Collections.Generic;
using BombTavern.Config;

namespace BombTavern.Core
{
    /// <summary>背包中的一件道具实例。</summary>
    [Serializable]
    public struct ItemInstance
    {
        public ItemDefinition definition;
        public int remainingUses;

        public bool IsUsable => definition != null && remainingUses > 0;
    }

    /// <summary>
    /// 一名玩家（真人或 AI）的完整状态。联机时该状态由确定性模拟推导，不做同步。
    /// </summary>
    [Serializable]
    public class PlayerState
    {
        // ---- 身份
        public int seatIndex;
        public string displayName = "炸弹";
        public bool isBot;

        /// <summary>该玩家自己的平板盘面。</summary>
        public RiskBoard board;

        // ---- 机会
        public int maxLives;
        public int lives;
        public bool eliminated;

        // ---- 道具掉落（每人独立概率 + 保底计数）
        public float itemFindChance;
        public int pityCounter;
        public int totalFindRolls;
        public int totalItemsFound;

        // ---- 状态效果
        public int shieldCharges;
        public int freezeTurnsRemaining;

        // ---- 背包
        public readonly List<ItemInstance> inventory = new List<ItemInstance>();
        public int itemsUsedThisTurn;

        /// <summary>
        /// 私人探查读数：探查类道具查到的真实概率，只有本人能看到。
        /// -1 表示未知。共用盘面时，公开信息之外的情报都存在这里，保证道具使用不泄露。
        /// </summary>
        public float[] knownTruth;

        // ---- 统计（结算与 AI 评估用）
        public int totalReveals;
        public int totalHits;
        public int totalProbes;
        public int totalModifies;
        public int totalFlags;
        public int totalItemsUsed;

        public bool IsAlive => !eliminated;
        public int InventoryCount => inventory.Count;
        public bool InventoryFull => inventory.Count >= 99;   // 实际上限由 GameSession 按配置判定

        /// <summary>回合开始时重置的计数。</summary>
        public void BeginTurn()
        {
            itemsUsedThisTurn = 0;
        }

        public bool HasItem(string itemId)
        {
            for (int i = 0; i < inventory.Count; i++)
                if (inventory[i].definition != null && inventory[i].definition.itemId == itemId)
                    return true;
            return false;
        }
    }
}
