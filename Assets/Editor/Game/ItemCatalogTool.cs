using System.Text;
using BombTavern.Config;
using UnityEditor;
using UnityEngine;

namespace BombTavern.EditorTools
{
    /// <summary>
    /// 道具目录的管理与自检工具。
    ///
    /// 加道具 / 改数值 / 禁用道具之后跑一次，比在游戏里碰运气强：
    /// 它会一次性报出空引用、itemId 重复、字段填错，以及被禁用（不会掉出来）的条目。
    /// </summary>
    public static class ItemCatalogTool
    {
        // 目录资产在 Items/ 下，不在 Config/ 下
        const string CatalogPath = GameAssetCreator.ItemDir + "/ItemCatalog.asset";

        [MenuItem("工具/炸弹酒馆/4. 道具目录自检", false, 13)]
        public static void ValidateFromMenu()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[道具自检] 找不到目录资产 {CatalogPath}。请先执行「1. 生成默认配置资产」。");
                return;
            }

            catalog.InvalidateCache();

            var sb = new StringBuilder();
            sb.AppendLine("================ 道具目录自检 ================");
            sb.AppendLine($"资产：{CatalogPath}");
            sb.AppendLine($"条目总数：{catalog.items.Count}   可掉落：{catalog.DroppableItems.Count}   " +
                          $"开局池：{catalog.ResolveStartingPool().Count}   自定义效果：{BombTavern.Items.ItemEffectRegistry.Count}");
            sb.AppendLine("--- 明细 ---");

            var enabled = catalog.EnabledItemsForDisplay();
            for (int i = 0; i < enabled.Count; i++)
            {
                var d = enabled[i];
                sb.AppendLine($"  [启用] {d.displayName,-8} id={d.itemId,-16} 效果={d.effect,-16} " +
                              $"稀有={d.rarity,-9} 权重={d.dropWeight,-5:F0} 分类={d.category}");
            }

            for (int i = 0; i < catalog.items.Count; i++)
            {
                var d = catalog.items[i];
                if (d != null && !d.enabled)
                    sb.AppendLine($"  [禁用] {d.displayName,-8} id={d.itemId,-16} 效果={d.effect} " +
                                  "（不会出现在掉落池与开局手牌里）");
            }

            var problems = catalog.Validate();
            if (problems.Count == 0)
            {
                sb.AppendLine("问题：无 ✅");
            }
            else
            {
                sb.AppendLine($"问题（{problems.Count}）：");
                for (int i = 0; i < problems.Count; i++) sb.AppendLine("  ⚠ " + problems[i]);
            }

            sb.AppendLine("==============================================");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 把所有道具恢复为启用。新增 enabled 字段后如果有条目被读成禁用，
        /// 用这个一键修回来，不用一个个点。
        /// </summary>
        [MenuItem("工具/炸弹酒馆/4b. 启用全部道具", false, 14)]
        public static void EnableAll()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[道具自检] 找不到目录资产 {CatalogPath}。");
                return;
            }

            int fixedCount = 0;
            for (int i = 0; i < catalog.items.Count; i++)
            {
                var d = catalog.items[i];
                if (d == null || d.enabled) continue;

                d.enabled = true;
                EditorUtility.SetDirty(d);
                fixedCount++;
            }

            catalog.InvalidateCache();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            Debug.Log($"[道具自检] 已启用 {fixedCount} 个道具。");
        }
    }
}
