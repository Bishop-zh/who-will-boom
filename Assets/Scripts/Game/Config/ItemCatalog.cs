using System.Collections.Generic;
using UnityEngine;

namespace BombTavern.Config
{
    /// <summary>
    /// 道具目录：掉落池与开局发放池。
    ///
    /// 本类同时是道具系统的**唯一查询入口** —— 掉落、发牌、UI、联机还原 itemId
    /// 都从这里取，不要在别处直接遍历 items，否则「禁用」会在某个角落漏掉。
    ///
    /// 注意：本类必须单独放在 ItemCatalog.cs 中。
    /// Unity 要求 ScriptableObject / MonoBehaviour 的类名与文件名一致，
    /// 否则 .asset 的脚本引用无法解析，资产会变成"加载不出来"的坏资产。
    /// </summary>
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "炸弹酒馆/道具/道具目录", order = 11)]
    public class ItemCatalog : ScriptableObject
    {
        [Tooltip("全部道具定义。")]
        public List<ItemDefinition> items = new List<ItemDefinition>();

        [Tooltip("开局发放道具时使用的池子。留空则使用 items 中 canBeStartingItem 为真的条目。")]
        public List<ItemDefinition> startingPool = new List<ItemDefinition>();

        // ---- 缓存：掉落是逐格触发的，每次都重新筛一遍会白白产生垃圾
        List<ItemDefinition> _droppableCache;
        List<ItemDefinition> _startingCache;
        Dictionary<string, ItemDefinition> _byId;

        /// <summary>清掉缓存。改完目录后必须调用（编辑器工具会自动调）。</summary>
        public void InvalidateCache()
        {
            _droppableCache = null;
            _startingCache = null;
            _byId = null;
        }

        void OnValidate() => InvalidateCache();

        /// <summary>按 id 查定义。**包含被禁用的**，因为联机收到的旧道具还得能还原出名字。</summary>
        public ItemDefinition Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;

            if (_byId == null)
            {
                _byId = new Dictionary<string, ItemDefinition>();
                for (int i = 0; i < items.Count; i++)
                {
                    var d = items[i];
                    if (d == null || string.IsNullOrEmpty(d.itemId)) continue;
                    _byId[d.itemId] = d;      // id 重复时后者覆盖，Validate 会报出来
                }
            }

            return _byId.TryGetValue(itemId, out var found) ? found : null;
        }

        /// <summary>
        /// 可掉落道具。**保持 items 的原始顺序** —— 掉落用的加权随机是顺序敏感的，
        /// 这里如果排序就会改变抽到的结果，破坏联机各端的一致性。
        /// </summary>
        public List<ItemDefinition> DroppableItems
        {
            get
            {
                if (_droppableCache != null) return _droppableCache;

                _droppableCache = new List<ItemDefinition>();
                for (int i = 0; i < items.Count; i++)
                {
                    var d = items[i];
                    if (d != null && d.IsDroppable) _droppableCache.Add(d);
                }
                return _droppableCache;
            }
        }

        /// <summary>
        /// 开局发牌池。显式配了 startingPool 就用它（同样过滤禁用项），
        /// 否则退回「items 里 canBeStartingItem 的条目」。
        /// </summary>
        public List<ItemDefinition> ResolveStartingPool()
        {
            if (_startingCache != null) return _startingCache;

            _startingCache = new List<ItemDefinition>();

            if (startingPool != null && startingPool.Count > 0)
            {
                for (int i = 0; i < startingPool.Count; i++)
                {
                    var d = startingPool[i];
                    if (d != null && d.enabled) _startingCache.Add(d);
                }
                return _startingCache;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var d = items[i];
                if (d != null && d.enabled && d.canBeStartingItem) _startingCache.Add(d);
            }
            return _startingCache;
        }

        /// <summary>启用中的道具，按 sortOrder → itemId 排序，供 UI / 图鉴展示。</summary>
        public List<ItemDefinition> EnabledItemsForDisplay()
        {
            var list = new List<ItemDefinition>();
            for (int i = 0; i < items.Count; i++)
            {
                var d = items[i];
                if (d != null && d.enabled) list.Add(d);
            }
            list.Sort((a, b) =>
            {
                int c = a.sortOrder.CompareTo(b.sortOrder);
                return c != 0 ? c : string.CompareOrdinal(a.itemId, b.itemId);
            });
            return list;
        }

        /// <summary>
        /// 全目录自检：空引用、id 重复、字段填错。
        /// 加道具、改数值之后跑一次，比在游戏里碰运气强。
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var seen = new HashSet<string>();

            for (int i = 0; i < items.Count; i++)
            {
                var d = items[i];
                if (d == null)
                {
                    problems.Add($"items[{i}] 是空引用");
                    continue;
                }

                if (!string.IsNullOrEmpty(d.itemId) && !seen.Add(d.itemId))
                    problems.Add($"{d.itemId}: itemId 重复，Find() 只会返回其中一个，联机会错位");

                string err = d.Validate();
                if (err != null) problems.Add(err);
            }

            if (startingPool != null)
            {
                for (int i = 0; i < startingPool.Count; i++)
                {
                    var d = startingPool[i];
                    if (d == null) problems.Add($"startingPool[{i}] 是空引用");
                    else if (!items.Contains(d)) problems.Add($"{d.itemId}: 在 startingPool 里但不在 items 里");
                }
            }

            return problems;
        }
    }
}
