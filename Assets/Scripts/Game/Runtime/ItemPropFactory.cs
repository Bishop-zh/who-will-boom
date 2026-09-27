using BombTavern.Config;
using UnityEngine;
using UnityEngine.Rendering;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 道具模型工厂：按道具效果类型拼出一个可识别的小道具。
    ///
    /// 为什么按「效果类型」而不是按「道具条目」建模型：
    ///   道具目录是可配置的，策划可以加十几种道具，但效果类型是有限的枚举。
    ///   按效果类型建模，新增道具只要复用已有效果就自动有模型，不会出现「没建模」的空洞。
    ///
    /// 朝向约定：+Z 朝使用者（镜片正面/手电筒前端），+Y 朝上。
    /// 全部用 Unity 原生几何体，材质用 MaterialPropertyBlock 染色，
    /// 不依赖任何自定义 Shader 资源，打包后不会丢材质。
    /// </summary>
    public static class ItemPropFactory
    {
        // 调色板
        static readonly Color Glass = new Color(0.42f, 0.92f, 1.00f);
        static readonly Color Frame = new Color(0.24f, 0.26f, 0.32f);
        static readonly Color Brass = new Color(0.80f, 0.66f, 0.26f);
        static readonly Color Red = new Color(0.90f, 0.24f, 0.20f);
        static readonly Color Cyan = new Color(0.46f, 0.94f, 1.00f);
        static readonly Color Steel = new Color(0.62f, 0.66f, 0.72f);
        static readonly Color Dark = new Color(0.16f, 0.16f, 0.19f);

        /// <summary>按效果类型建一个道具模型并挂到 parent 下。</summary>
        public static GameObject Build(ItemEffectType effect, Transform parent, string name = null)
        {
            var root = new GameObject(string.IsNullOrEmpty(name) ? $"Prop_{effect}" : name);
            root.transform.SetParent(parent, false);
            var t = root.transform;

            switch (effect)
            {
                case ItemEffectType.ProbeCell: BuildGlasses(t); break;
                case ItemEffectType.ScanRow:
                case ItemEffectType.ScanArea: BuildMagnifier(t); break;
                case ItemEffectType.DecreaseRisk: BuildCoolant(t); break;
                case ItemEffectType.IncreaseRisk: BuildVial(t); break;
                case ItemEffectType.AreaIncreaseRisk: BuildSprayer(t); break;
                case ItemEffectType.DiamondFlux: BuildFluxCrystal(t); break;
                case ItemEffectType.Shield: BuildShield(t); break;
                case ItemEffectType.FreezeDrift: BuildIce(t); break;
                case ItemEffectType.RevealNeighbors: BuildFlashlight(t); break;
                case ItemEffectType.SkipTurn: BuildPocketWatch(t); break;
                default: BuildGenericBox(t); break;
            }

            return root;
        }

        // ============================================================ 各道具造型

        /// <summary>眼镜：两片镜片 + 鼻梁 + 两条镜腿。</summary>
        static void BuildGlasses(Transform t)
        {
            // Cylinder 的长轴是 Y，绕 X 转 90° 后长轴沿 Z —— 镜片面就朝向使用者
            Part(t, PrimitiveType.Cylinder, new Vector3(-0.028f, 0f, 0f), new Vector3(0.052f, 0.0025f, 0.052f), Glass, new Vector3(90f, 0f, 0f));
            Part(t, PrimitiveType.Cylinder, new Vector3(0.028f, 0f, 0f), new Vector3(0.052f, 0.0025f, 0.052f), Glass, new Vector3(90f, 0f, 0f));

            // 镜框圈
            Part(t, PrimitiveType.Cylinder, new Vector3(-0.028f, 0f, -0.002f), new Vector3(0.060f, 0.0020f, 0.060f), Frame, new Vector3(90f, 0f, 0f));
            Part(t, PrimitiveType.Cylinder, new Vector3(0.028f, 0f, -0.002f), new Vector3(0.060f, 0.0020f, 0.060f), Frame, new Vector3(90f, 0f, 0f));
            // 镜片再往前压一层，盖住框内圈
            Part(t, PrimitiveType.Cylinder, new Vector3(-0.028f, 0f, 0.0015f), new Vector3(0.050f, 0.0020f, 0.050f), Glass, new Vector3(90f, 0f, 0f));
            Part(t, PrimitiveType.Cylinder, new Vector3(0.028f, 0f, 0.0015f), new Vector3(0.050f, 0.0020f, 0.050f), Glass, new Vector3(90f, 0f, 0f));

            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.012f, 0.004f, 0.005f), Frame);
            Part(t, PrimitiveType.Cube, new Vector3(-0.062f, 0f, -0.030f), new Vector3(0.005f, 0.005f, 0.062f), Frame);
            Part(t, PrimitiveType.Cube, new Vector3(0.062f, 0f, -0.030f), new Vector3(0.005f, 0.005f, 0.062f), Frame);
        }

        /// <summary>放大镜：镜圈 + 手柄。</summary>
        static void BuildMagnifier(Transform t)
        {
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.020f, 0f), new Vector3(0.062f, 0.0030f, 0.062f), Brass, new Vector3(90f, 0f, 0f));
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.020f, 0.002f), new Vector3(0.052f, 0.0025f, 0.052f), Glass, new Vector3(90f, 0f, 0f));
            Part(t, PrimitiveType.Cube, new Vector3(0f, -0.036f, 0f), new Vector3(0.008f, 0.056f, 0.008f), Frame, new Vector3(0f, 0f, 12f));
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, -0.064f, 0f), new Vector3(0.020f, 0.010f, 0.020f), Dark, new Vector3(90f, 0f, 0f));
        }

        /// <summary>冷却剂：罐体 + 喷嘴。</summary>
        static void BuildCoolant(Transform t)
        {
            Part(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.036f, 0.056f, 0.036f), Steel);
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.032f, 0f), new Vector3(0.030f, 0.010f, 0.030f), Cyan);
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0.052f, 0.012f), new Vector3(0.010f, 0.012f, 0.028f), Dark);
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.060f, 0f), new Vector3(0.016f, 0.006f, 0.016f), Cyan);
        }

        /// <summary>危险试剂：细颈瓶 + 瓶塞。</summary>
        static void BuildVial(Transform t)
        {
            Part(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.032f, 0.048f, 0.032f), Red);
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.030f, 0f), new Vector3(0.016f, 0.016f, 0.016f), Glass);
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0.046f, 0f), new Vector3(0.020f, 0.016f, 0.020f), Dark);
            Part(t, PrimitiveType.Sphere, new Vector3(0f, 0.058f, 0f), Vector3.one * 0.014f, Brass);
        }

        /// <summary>九宫格喷雾：红色罐体与三个喷嘴。</summary>
        static void BuildSprayer(Transform t)
        {
            Part(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(.042f, .066f, .042f), Red);
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, .04f, 0f),
                new Vector3(.048f, .012f, .048f), Brass);
            for (int i = -1; i <= 1; i++)
                Part(t, PrimitiveType.Cylinder, new Vector3(i * .025f, .052f, 0f),
                    new Vector3(.008f, .023f, .008f), Steel);
        }

        /// <summary>菱形扰动器：中空框与紫色晶体。</summary>
        static void BuildFluxCrystal(Transform t)
        {
            var violet = new Color(.7f, .38f, .95f);
            Part(t, PrimitiveType.Cube, Vector3.zero, new Vector3(.082f, .082f, .022f), Brass,
                new Vector3(0f, 0f, 45f));
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, .016f),
                new Vector3(.058f, .058f, .018f), Dark, new Vector3(0f, 0f, 45f));
            Part(t, PrimitiveType.Sphere, new Vector3(0f, 0f, .032f), Vector3.one * .038f, violet);
        }

        /// <summary>护盾：圆角盾牌 + 盾钉。</summary>
        static void BuildShield(Transform t)
        {
            Part(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.096f, 0.108f, 0.012f), Steel);
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0.054f, 0f), new Vector3(0.096f, 0.016f, 0.014f), Dark);
            Part(t, PrimitiveType.Cube, new Vector3(0f, -0.054f, 0f), new Vector3(0.060f, 0.016f, 0.014f), Dark);
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.012f), new Vector3(0.030f, 0.006f, 0.030f), Cyan, new Vector3(90f, 0f, 0f));
        }

        /// <summary>冰晶：三根交叉细杆 + 中心球。</summary>
        static void BuildIce(Transform t)
        {
            Part(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.010f, 0.120f, 0.010f), Cyan);
            Part(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.010f, 0.120f, 0.010f), Cyan, new Vector3(0f, 0f, 60f));
            Part(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.010f, 0.120f, 0.010f), Cyan, new Vector3(0f, 0f, 120f));
            Part(t, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.030f, Glass);
        }

        /// <summary>手电筒：筒身 + 灯头 + 灯面。</summary>
        static void BuildFlashlight(Transform t)
        {
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, -0.010f, 0f), new Vector3(0.030f, 0.060f, 0.030f), Dark, new Vector3(90f, 0f, 0f));
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.040f), new Vector3(0.048f, 0.020f, 0.048f), Steel, new Vector3(90f, 0f, 0f));
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.052f), new Vector3(0.040f, 0.0030f, 0.040f), new Color(1f, 0.94f, 0.62f), new Vector3(90f, 0f, 0f));
        }

        /// <summary>怀表：表壳 + 表冠 + 指针。</summary>
        static void BuildPocketWatch(Transform t)
        {
            Part(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.062f, 0.012f, 0.062f), Brass, new Vector3(90f, 0f, 0f));
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.007f), new Vector3(0.050f, 0.0030f, 0.050f), new Color(0.96f, 0.93f, 0.82f), new Vector3(90f, 0f, 0f));
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0.008f, 0.010f), new Vector3(0.004f, 0.030f, 0.004f), Dark);
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0.004f, 0.010f), new Vector3(0.024f, 0.004f, 0.004f), Red);
            Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.036f, 0f), new Vector3(0.012f, 0.010f, 0.012f), Brass);
        }

        /// <summary>兜底：一个带发光条的道具盒。</summary>
        static void BuildGenericBox(Transform t)
        {
            Part(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.070f, 0.070f, 0.070f), Dark);
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.037f), new Vector3(0.040f, 0.010f, 0.006f), Brass);
        }

        // ============================================================ 工具
        static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale,
            Color color, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = type.ToString();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = euler;

            // 道具不参与物理，碰撞体一律去掉，避免和面板/身体互相推挤造成穿模
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;

                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_Color", color);
                mpb.SetColor("_EmissionColor", color * 0.35f);
                r.SetPropertyBlock(mpb);
            }

            return go.transform;
        }
    }
}
