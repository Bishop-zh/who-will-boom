using System;
using UnityEngine;

namespace BombTavern.Config
{
    /// <summary>
    /// 风险值(0~1)到格子颜色的映射：绿 → 黄 → 红。
    /// 注意：格子颜色只由"显示风险值 DisplayedRisk"决定；
    /// 真实概率漂移后显示值不刷新，因此颜色会逐渐失真。
    /// 资产路径：Assets/LiarsBar/Game/Config/ColorRamp_Risk.asset
    /// </summary>
    [CreateAssetMenu(fileName = "ColorRamp_Risk", menuName = "炸弹酒馆/配置/风险配色", order = 4)]
    public class RiskColorRamp : ScriptableObject
    {
        [Serializable]
        public struct Stop
        {
            [Tooltip("风险阈值（0~1）。")]
            [Range(0f, 1f)] public float risk;
            [Tooltip("该阈值对应的颜色。")]
            public Color color;
        }

        [Header("颜色阶（按风险从低到高排列，需保持数组有序）")]
        public Stop[] stops =
        {
            new Stop { risk = 0.00f, color = new Color(0.22f, 0.80f, 0.32f) },
            new Stop { risk = 0.25f, color = new Color(0.62f, 0.85f, 0.24f) },
            new Stop { risk = 0.45f, color = new Color(0.95f, 0.85f, 0.20f) },
            new Stop { risk = 0.65f, color = new Color(0.96f, 0.55f, 0.14f) },
            new Stop { risk = 0.80f, color = new Color(0.90f, 0.20f, 0.16f) },
            new Stop { risk = 1.00f, color = new Color(0.68f, 0.05f, 0.10f) },
        };

        [Header("已翻开格子的表现")]
        [Tooltip("已翻开（安全通过）格子的底色。")]
        public Color revealedColor = new Color(0.16f, 0.15f, 0.19f);

        [Tooltip("已翻开格子上数字/期望雷数文字的颜色。")]
        public Color revealedTextColor = new Color(0.85f, 0.87f, 0.92f);

        [Header("显示值过期提示")]
        [Tooltip("显示值与真实值偏差超过该阈值时，格子开始出现「信息过期」抖动提示。")]
        [Range(0f, 1f)] public float staleHintThreshold = 0.08f;

        [Tooltip("信息过期提示的抖动频率（次/秒），0 表示不抖动。")]
        [Range(0f, 20f)] public float staleShakeFrequency = 6f;

        /// <summary>按风险值取色（数组需按 risk 升序）。</summary>
        public Color Evaluate(float risk)
        {
            if (stops == null || stops.Length == 0) return Color.white;
            if (risk <= stops[0].risk) return stops[0].color;

            for (int i = 0; i < stops.Length - 1; i++)
            {
                if (risk <= stops[i + 1].risk)
                {
                    float span = stops[i + 1].risk - stops[i].risk;
                    float t = span <= Mathf.Epsilon ? 0f : (risk - stops[i].risk) / span;
                    return Color.Lerp(stops[i].color, stops[i + 1].color, t);
                }
            }
            return stops[stops.Length - 1].color;
        }
    }
}
