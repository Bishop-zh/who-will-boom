using System.Collections.Generic;
using UnityEngine;

namespace BombTavern.Config
{
    /// <summary>
    /// 皮肤目录。
    /// 注意：本类必须单独放在 SkinCatalog.cs（类名与文件名一致）。
    /// 资产路径：Assets/LiarsBar/Game/Skins/SkinCatalog.asset
    /// </summary>
    [CreateAssetMenu(fileName = "SkinCatalog", menuName = "炸弹酒馆/皮肤/皮肤目录", order = 21)]
    public class SkinCatalog : ScriptableObject
    {
        [Tooltip("全部可选皮肤，顺序即 UI 中的选择顺序。")]
        public List<SkinDefinition> skins = new List<SkinDefinition>();

        [Tooltip("没有指定皮肤时使用的默认皮肤。")]
        public SkinDefinition defaultSkin;

        /// <summary>按 ID 查找皮肤，找不到返回 null。</summary>
        public SkinDefinition Find(string skinId)
        {
            if (string.IsNullOrEmpty(skinId) || skins == null) return null;
            for (int i = 0; i < skins.Count; i++)
            {
                var s = skins[i];
                if (s != null && s.skinId == skinId) return s;
            }
            return null;
        }

        /// <summary>取默认皮肤，目录为空时返回 null。</summary>
        public SkinDefinition GetDefault()
        {
            if (defaultSkin != null) return defaultSkin;
            return skins != null && skins.Count > 0 ? skins[0] : null;
        }

        /// <summary>按座位取一套皮肤（没有配置时循环使用目录），用于给 AI 穿衣。</summary>
        public SkinDefinition GetForSeat(int seatIndex)
        {
            if (skins == null || skins.Count == 0) return null;
            return skins[Mathf.Abs(seatIndex) % skins.Count];
        }
    }
}
