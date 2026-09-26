using UnityEngine;

namespace BombTavern.Config
{
    /// <summary>弹体造型。</summary>
    public enum BombBodyType
    {
        [Tooltip("黑色圆形炸弹（经典圆雷）")]
        RoundBomb = 0,
        [Tooltip("棕色成捆炸弹（一捆炸药棒）")]
        DynamiteBundle = 1,
        [Tooltip("二踢脚（两节纸筒）")]
        DoubleCracker = 2,
        [Tooltip("炮弹（锥形弹头 + 圆柱弹体）")]
        ArtilleryShell = 3,
    }

    /// <summary>头饰造型。</summary>
    public enum BombHatType
    {
        None = 0,
        Bowler = 1,
        TopHat = 2,
        Party = 3,
    }

    /// <summary>胸前印记造型。</summary>
    public enum BombEmblemShape
    {
        None = 0,
        Disc = 1,
        Ring = 2,
        Cross = 3,
    }

    /// <summary>
    /// 单套角色皮肤。弹体造型、装饰、印记、配色全部数据化，
    /// 换皮肤 = 换一份资产，不改代码。
    /// 注意：本类必须单独放在 SkinDefinition.cs（类名与文件名一致）。
    /// 资产路径：Assets/LiarsBar/Game/Skins/*.asset
    /// </summary>
    [CreateAssetMenu(fileName = "Skin_", menuName = "炸弹酒馆/皮肤/皮肤定义", order = 20)]
    public class SkinDefinition : ScriptableObject
    {
        [Header("标识")]
        [Tooltip("唯一 ID，联机同步用。改动会让旧客户端显示不一致。")]
        public string skinId = "round_black";

        [Tooltip("UI 显示名。")]
        public string displayName = "黑色圆雷";

        [Header("弹体")]
        [Tooltip("弹体造型。")]
        public BombBodyType bodyType = BombBodyType.RoundBomb;

        [Tooltip("弹体主色。")]
        public Color bodyColor = new Color(0.15f, 0.15f, 0.18f);

        [Tooltip("弹体副色（成捆炸弹的炸药棒 / 二踢脚的下半节）。")]
        public Color bodyColorB = new Color(0.24f, 0.24f, 0.29f);

        [Tooltip("弹体光滑度。")]
        [Range(0f, 1f)] public float bodySmoothness = 0.45f;

        [Tooltip("弹体金属度。")]
        [Range(0f, 1f)] public float bodyMetallic = 0.25f;

        [Header("装饰")]
        [Tooltip("腰环 / 捆扎带颜色。")]
        public Color bandColor = new Color(0.79f, 0.64f, 0.15f);

        [Tooltip("手套颜色。")]
        public Color gloveColor = new Color(0.88f, 0.26f, 0.25f);

        [Tooltip("靴子颜色。")]
        public Color bootColor = new Color(0.13f, 0.13f, 0.16f);

        [Tooltip("引信颜色。")]
        public Color fuseColor = new Color(0.54f, 0.42f, 0.29f);

        [Header("印记")]
        [Tooltip("胸前印记造型。")]
        public BombEmblemShape emblem = BombEmblemShape.Disc;

        [Tooltip("印记颜色。")]
        public Color emblemColor = new Color(0.88f, 0.26f, 0.25f);

        [Header("头饰")]
        [Tooltip("头饰造型。")]
        public BombHatType hat = BombHatType.None;

        [Tooltip("头饰颜色。")]
        public Color hatColor = new Color(0.30f, 0.30f, 0.36f);

        [Header("表情")]
        [Tooltip("眼睛整体缩放。")]
        [Range(0.6f, 1.6f)] public float eyeScale = 1f;

        [Tooltip("瞳孔颜色。")]
        public Color pupilColor = new Color(0.08f, 0.08f, 0.10f);

        [Tooltip("是否皱眉（暴躁脸）。")]
        public bool grumpy = false;
    }
}
