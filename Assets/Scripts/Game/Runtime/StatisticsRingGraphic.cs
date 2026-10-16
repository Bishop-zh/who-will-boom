using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.Runtime
{
    /// <summary>原生可遮罩 UGUI 环形图；按准确比例分两段网格，不依赖截图或外部图表插件。</summary>
    public sealed class StatisticsRingGraphic : MaskableGraphic
    {
        [Range(0, 1)] [SerializeField] float fraction;
        [Range(.1f, .95f)] public float innerRadius = .73f;
        public Color remainderColor = new Color(.28f, .22f, .19f);
        public float Fraction { get => fraction; set { fraction = Mathf.Clamp01(value); SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Arc(vh, 0, fraction, color);
            Arc(vh, fraction, 1, remainderColor);
        }
        void Arc(VertexHelper vh, float start, float end, Color tint)
        {
            if (end <= start) return;
            var rect = rectTransform.rect;
            float outer = Mathf.Min(rect.width, rect.height) * .5f;
            int steps = Mathf.Max(1, Mathf.CeilToInt((end - start) * 160));
            for (int i = 0; i < steps; i++)
            {
                float a = (90 - Mathf.Lerp(start, end, (float)i / steps) * 360) * Mathf.Deg2Rad;
                float b = (90 - Mathf.Lerp(start, end, (float)(i + 1) / steps) * 360) * Mathf.Deg2Rad;
                var u = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var v = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                int index = vh.currentVertCount;
                vh.AddVert(rect.center + u * outer, tint, Vector2.zero);
                vh.AddVert(rect.center + v * outer, tint, Vector2.zero);
                vh.AddVert(rect.center + v * outer * innerRadius, tint, Vector2.zero);
                vh.AddVert(rect.center + u * outer * innerRadius, tint, Vector2.zero);
                vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
            }
        }
    }
}
