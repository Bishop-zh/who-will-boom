using System.Collections.Generic;
using BombTavern.Config;
using LiarsBar;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 炸弹角色外观工厂（运行时）。
    /// 按皮肤数据拼装弹体（三种造型）+ 五官 + 四肢 + 装饰 + 印记 + 头饰。
    /// 换皮肤时销毁 Visual 节点重建，逻辑组件 BombCharacter 保留不动。
    /// </summary>
    public static class BombVisualFactory
    {
        const string VisualNode = "Visual";
        const string BodyRootNode = "BodyRoot";

        // 经典圆雷的回转剖面：x = 半径，y = 高度（总高 1.0，最大半径 0.65）
        static readonly Vector2[] RoundBombProfile =
        {
            new Vector2(0.001f, 0.000f), new Vector2(0.260f, 0.015f),
            new Vector2(0.440f, 0.070f), new Vector2(0.570f, 0.190f),
            new Vector2(0.645f, 0.360f), new Vector2(0.650f, 0.520f),
            new Vector2(0.600f, 0.660f), new Vector2(0.480f, 0.770f),
            new Vector2(0.320f, 0.840f), new Vector2(0.200f, 0.875f),
            new Vector2(0.175f, 0.930f), new Vector2(0.175f, 0.985f),
            new Vector2(0.001f, 1.000f),
        };

        // 炮弹的回转剖面：底部弹带缘 → 圆柱弹体 → 卵形弹头
        static readonly Vector2[] ShellProfile =
        {
            new Vector2(0.001f, 0.000f), new Vector2(0.300f, 0.000f),
            new Vector2(0.300f, 0.040f), new Vector2(0.268f, 0.062f),
            new Vector2(0.268f, 0.300f), new Vector2(0.284f, 0.318f),
            new Vector2(0.284f, 0.352f), new Vector2(0.266f, 0.372f),
            new Vector2(0.266f, 0.550f), new Vector2(0.254f, 0.628f),
            new Vector2(0.222f, 0.716f), new Vector2(0.170f, 0.808f),
            new Vector2(0.100f, 0.898f), new Vector2(0.040f, 0.962f),
            new Vector2(0.001f, 1.000f),
        };

        struct Metrics
        {
            public float height, faceRadius, eyeY, browY, mouthY, shoulderY;
        }

        static Metrics MetricsOf(BombBodyType type)
        {
            switch (type)
            {
                case BombBodyType.DynamiteBundle:
                    return new Metrics { height = 0.82f, faceRadius = 0.27f, eyeY = 0.50f, browY = 0.60f, mouthY = 0.35f, shoulderY = 0.30f };
                case BombBodyType.DoubleCracker:
                    return new Metrics { height = 0.96f, faceRadius = 0.18f, eyeY = 0.60f, browY = 0.72f, mouthY = 0.42f, shoulderY = 0.36f };
                case BombBodyType.ArtilleryShell:
                    return new Metrics { height = 1.02f, faceRadius = 0.24f, eyeY = 0.52f, browY = 0.64f, mouthY = 0.36f, shoulderY = 0.28f };
                default:
                    return new Metrics { height = 0.86f, faceRadius = 0.545f, eyeY = 0.48f, browY = 0.60f, mouthY = 0.35f, shoulderY = 0.29f };
            }
        }

        // ============================================================ 对外入口
        public static GameObject Build(SkinDefinition skin, string displayName, Transform parent,
            Vector3 localPos, float yawDegrees, float scale = 1f)
        {
            if (skin == null) skin = FallbackSkin();

            var root = new GameObject("Bomb_" + skin.skinId);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localEulerAngles = new Vector3(0f, yawDegrees, 0f);
            root.transform.localScale = Vector3.one * scale;

            var comp = root.AddComponent<BombCharacter>();
            comp.displayName = displayName;
            comp.accentColor = skin.gloveColor;
            comp.tension = skin.grumpy ? 0.9f : 0.3f;

            ApplySkin(root, skin);
            return root;
        }

        /// <summary>换皮肤：销毁旧外观节点并按新皮肤重建。逻辑组件不受影响。</summary>
        public static void ApplySkin(GameObject characterRoot, SkinDefinition skin)
        {
            if (characterRoot == null || skin == null) return;

            // 清空根节点下所有外观节点。
            // 旧版工厂生成的角色没有 Visual 包装层（BodyRoot 直接挂在根上），
            // 只找 Visual 会漏掉旧外观，导致新旧模型叠在一起。
            var rootT = characterRoot.transform;
            for (int i = rootT.childCount - 1; i >= 0; i--)
            {
                var child = rootT.GetChild(i).gameObject;
                // 编辑模式必须用 DestroyImmediate：Destroy 是延迟的，
                // 批处理/保存场景时没有下一帧，旧外观会一直残留并和新模型叠在一起。
                if (Application.isPlaying) Object.Destroy(child);
                else Object.DestroyImmediate(child);
            }

            var visual = new GameObject(VisualNode);
            visual.transform.SetParent(rootT, false);

            // 优先用 Blender 做好的带骨骼模型（含 Idle / Aim / UseItem / Hit 四段动画）。
            // 资源缺失就回退到程序化拼装 —— 这条回退路径必须保留，
            // 否则资源没导入或者被误删时，角色会直接开天窗。
            if (RiggedCharacterLibrary.Instantiate(skin.skinId, visual.transform) != null) return;

            BuildVisual(visual.transform, skin);

            var comp = characterRoot.GetComponent<BombCharacter>();
            if (comp != null) comp.RefreshRefs();
        }

        static SkinDefinition FallbackSkin()
        {
            var s = ScriptableObject.CreateInstance<SkinDefinition>();
            s.name = "SkinFallback_Runtime";
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        static void BuildVisual(Transform visual, SkinDefinition skin)
        {
            var m = MetricsOf(skin.bodyType);
            var bodyRoot = NewNode(BodyRootNode, visual);

            switch (skin.bodyType)
            {
                case BombBodyType.DynamiteBundle: BuildDynamiteBundle(bodyRoot, skin, m); break;
                case BombBodyType.DoubleCracker: BuildDoubleCracker(bodyRoot, skin, m); break;
                case BombBodyType.ArtilleryShell: BuildArtilleryShell(bodyRoot, skin, m); break;
                default: BuildRoundBomb(bodyRoot, skin, m); break;
            }

            BuildFace(bodyRoot, skin, m);
            BuildArms(bodyRoot, skin, m);
            BuildBoots(bodyRoot, skin, m);
            BuildHat(bodyRoot, skin, m);
        }

        // ============================================================ 弹体
        static void BuildRoundBomb(Transform t, SkinDefinition skin, Metrics m)
        {
            var shell = MatOf(skin, "body", skin.bodyColor, skin.bodySmoothness, skin.bodyMetallic);
            float h = m.height;

            MeshObj(RevolveMesh("RoundBomb", RoundBombProfile), t, Vector3.zero, Vector3.one * h, shell);

            Cyl(NewNode("Band", t), new Vector3(0f, h * 0.385f, 0f),
                new Vector3(1.10f, 0.020f, 1.10f), BandMat(skin));

            Cyl(NewNode("Cap", t), new Vector3(0f, h * 1.005f, 0f),
                new Vector3(0.30f, 0.032f, 0.30f), MetalMat(skin));
            Cyl(NewNode("Fuse1", t), new Vector3(0f, h * 1.10f, 0f),
                new Vector3(0.055f, 0.036f, 0.055f), FuseMat(skin), new Vector3(6f, 0f, 0f));
            Cyl(NewNode("Fuse2", t), new Vector3(0.012f, h * 1.155f, 0.020f),
                new Vector3(0.050f, 0.032f, 0.050f), FuseMat(skin), new Vector3(24f, 0f, 0f));
            Sphere(NewNode("Spark", t), new Vector3(0.020f, h * 1.20f, 0.052f),
                Vector3.one * 0.085f, SparkMat(skin));
        }

        static void BuildDynamiteBundle(Transform t, SkinDefinition skin, Metrics m)
        {
            var matA = MatOf(skin, "body", skin.bodyColor, skin.bodySmoothness, skin.bodyMetallic);
            var matB = MatOf(skin, "bodyB", skin.bodyColorB, skin.bodySmoothness, skin.bodyMetallic);

            const int sticks = 6;
            float stickR = 0.085f;
            float ringR = stickR * 1.55f;
            float h = m.height * 0.95f;

            for (int i = 0; i < sticks; i++)
            {
                float a = i * Mathf.PI * 2f / sticks;
                var pos = new Vector3(Mathf.Cos(a) * ringR, h * 0.5f, Mathf.Sin(a) * ringR);
                Cyl(NewNode("Stick_" + i, t), pos, new Vector3(stickR * 2f, h * 0.5f, stickR * 2f),
                    i % 2 == 0 ? matA : matB);
            }

            Cyl(NewNode("Stick_Center", t), new Vector3(0f, h * 0.52f, 0f),
                new Vector3(stickR * 2f, h * 0.55f, stickR * 2f), matA);

            float bandR = ringR + stickR;
            for (int i = 0; i < 2; i++)
            {
                float y = h * (0.28f + i * 0.42f);
                Cyl(NewNode("Band_" + i, t), new Vector3(0f, y, 0f),
                    new Vector3(bandR * 2f, 0.018f, bandR * 2f), BandMat(skin));
            }

            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                var pos = new Vector3(Mathf.Cos(a) * 0.05f, h + 0.075f, Mathf.Sin(a) * 0.05f);
                Cyl(NewNode("Fuse_" + i, t), pos, new Vector3(0.035f, 0.035f, 0.035f),
                    FuseMat(skin), new Vector3(10f * Mathf.Sin(a), 0f, -10f * Mathf.Cos(a)));
            }

            Sphere(NewNode("Spark", t), new Vector3(0f, h + 0.16f, 0f), Vector3.one * 0.075f, SparkMat(skin));
        }

        static void BuildDoubleCracker(Transform t, SkinDefinition skin, Metrics m)
        {
            var matTop = MatOf(skin, "body", skin.bodyColor, skin.bodySmoothness, skin.bodyMetallic);
            var matBottom = MatOf(skin, "bodyB", skin.bodyColorB, skin.bodySmoothness, skin.bodyMetallic);

            float r = 0.16f;
            float half = m.height * 0.5f;

            Cyl(NewNode("Lower", t), new Vector3(0f, half * 0.5f, 0f), new Vector3(r * 2f, half * 0.5f, r * 2f), matBottom);
            Cyl(NewNode("Upper", t), new Vector3(0f, half * 1.5f, 0f), new Vector3(r * 2f, half * 0.5f, r * 2f), matTop);

            Cyl(NewNode("Seam", t), new Vector3(0f, half, 0f), new Vector3(r * 2.06f, 0.012f, r * 2.06f), BandMat(skin));
            Cyl(NewNode("Cap", t), new Vector3(0f, m.height + 0.008f, 0f), new Vector3(r * 2.06f, 0.014f, r * 2.06f), BandMat(skin));

            Cyl(NewNode("Fuse1", t), new Vector3(0f, m.height + 0.055f, 0f),
                new Vector3(0.035f, 0.040f, 0.035f), FuseMat(skin));
            Sphere(NewNode("Spark", t), new Vector3(0f, m.height + 0.115f, 0f), Vector3.one * 0.070f, SparkMat(skin));
        }

        /// <summary>灰色炮弹：回转体弹身（含卵形弹头）+ 弹带 + 底缘 + 弹头引信。</summary>
        static void BuildArtilleryShell(Transform t, SkinDefinition skin, Metrics m)
        {
            var shell = MatOf(skin, "body", skin.bodyColor, skin.bodySmoothness, skin.bodyMetallic);
            var band = BandMat(skin);

            float h = m.height;
            // 回转网格本身是 0..1 高度，这里按整体高度缩放
            MeshObj(RevolveMesh("ArtilleryShell", ShellProfile), t, Vector3.zero, Vector3.one * h, shell);

            // 两道弹带（黄铜色），压在弹身外圈
            Cyl(NewNode("DriveBand_Low", t), new Vector3(0f, h * 0.318f, 0f),
                new Vector3(0.290f * h, 0.008f, 0.290f * h), MetalMat(skin));
            Cyl(NewNode("DriveBand_High", t), new Vector3(0f, h * 0.352f, 0f),
                new Vector3(0.290f * h, 0.008f, 0.290f * h), MetalMat(skin));

            // 底缘加厚一圈，看起来更像炮弹而非锥体
            Cyl(NewNode("BaseRim", t), new Vector3(0f, h * 0.022f, 0f),
                new Vector3(0.312f * h, 0.022f, 0.312f * h), band);

            // 弹头引信
            Cyl(NewNode("NoseCap", t), new Vector3(0f, h * 1.005f, 0f),
                new Vector3(0.075f, 0.020f, 0.075f), MetalMat(skin));
            Sphere(NewNode("Spark", t), new Vector3(0f, h * 1.062f, 0f),
                Vector3.one * 0.070f, SparkMat(skin));
        }

        // ============================================================ 五官
        static void BuildFace(Transform t, SkinDefinition skin, Metrics m)
        {
            float r = m.faceRadius;
            float eyeX = Mathf.Max(0.085f, r * 0.34f);
            float eyeZ = r * 0.94f;

            var white = MatOf(skin, "eyeWhite", new Color(0.95f, 0.94f, 0.90f), 0.55f, 0f);
            var pupil = MatOf(skin, "pupil", skin.pupilColor, 0.3f, 0f);
            var ink = MatOf(skin, "ink", new Color(0.08f, 0.08f, 0.10f), 0.3f, 0f);

            BuildEye(t, "EyeL", -eyeX, m.eyeY, eyeZ, white, pupil, ink, skin);
            BuildEye(t, "EyeR", eyeX, m.eyeY, eyeZ, white, pupil, ink, skin);

            float browTilt = skin.grumpy ? 20f : -8f;
            float bw = Mathf.Max(0.10f, r * 0.55f);
            Cube(NewNode("BrowL", t), new Vector3(-eyeX, m.browY, eyeZ * 0.9f),
                new Vector3(bw, 0.030f, 0.045f), ink, new Vector3(0f, 0f, -browTilt));
            Cube(NewNode("BrowR", t), new Vector3(eyeX, m.browY, eyeZ * 0.9f),
                new Vector3(bw, 0.030f, 0.045f), ink, new Vector3(0f, 0f, browTilt));

            // 嘴：说话时会开合，名字必须是 Mouth
            float mw = Mathf.Max(0.08f, r * 0.42f);
            Cube(NewNode("Mouth", t), new Vector3(eyeX * 0.2f, m.mouthY, eyeZ * 0.96f),
                new Vector3(mw, 0.026f, 0.05f), ink, new Vector3(0f, 0f, -9f));

            BuildEmblem(t, skin, m);
        }

        static void BuildEye(Transform parent, string name, float x, float eyeY, float z,
            Material white, Material pupil, Material ink, SkinDefinition skin)
        {
            float s = skin.eyeScale;
            var eye = Sphere(NewNode(name, parent), new Vector3(x, eyeY, z),
                new Vector3(0.155f, 0.175f, 0.130f) * s, white);

            Sphere(NewNode("Pupil", eye.transform), new Vector3(0f, -0.02f, 0.52f),
                new Vector3(0.44f, 0.42f, 0.42f), pupil);
            Sphere(NewNode("Glint", eye.transform), new Vector3(-0.16f, 0.24f, 0.62f),
                Vector3.one * 0.16f, white);

            if (skin.grumpy)
                Cube(NewNode("Lid", eye.transform), new Vector3(0f, 0.30f, 0.30f),
                    new Vector3(0.95f, 0.16f, 0.30f), ink);
        }

        static void BuildEmblem(Transform t, SkinDefinition skin, Metrics m)
        {
            if (skin.emblem == BombEmblemShape.None) return;

            float r = m.faceRadius;
            float y = m.mouthY + m.height * 0.10f;
            float z = r * 0.98f;
            var mat = MatOf(skin, "emblem", skin.emblemColor, 0.35f, 0f);

            switch (skin.emblem)
            {
                case BombEmblemShape.Disc:
                    Cyl(NewNode("Emblem", t), new Vector3(0f, y, z),
                        new Vector3(r * 0.46f, 0.010f, r * 0.46f), mat, new Vector3(90f, 0f, 0f));
                    break;

                case BombEmblemShape.Ring:
                    Cyl(NewNode("EmblemRing", t), new Vector3(0f, y, z),
                        new Vector3(r * 0.52f, 0.010f, r * 0.52f), mat, new Vector3(90f, 0f, 0f));
                    Cyl(NewNode("EmblemHole", t), new Vector3(0f, y, z + 0.006f),
                        new Vector3(r * 0.28f, 0.010f, r * 0.28f),
                        MatOf(skin, "body", skin.bodyColor, 0.3f, 0f), new Vector3(90f, 0f, 0f));
                    break;

                case BombEmblemShape.Cross:
                    Cube(NewNode("EmblemV", t), new Vector3(0f, y, z), new Vector3(r * 0.18f, r * 0.62f, 0.02f), mat);
                    Cube(NewNode("EmblemH", t), new Vector3(0f, y, z), new Vector3(r * 0.62f, r * 0.18f, 0.02f), mat);
                    break;
            }
        }

        // ============================================================ 四肢 / 头饰
        static void BuildArms(Transform t, SkinDefinition skin, Metrics m)
        {
            float r = m.faceRadius;
            float sx = r * 1.06f, hx = r * 1.30f;
            float sy = m.shoulderY, hy = m.shoulderY * 0.55f;

            var shell = MatOf(skin, "body", skin.bodyColor, skin.bodySmoothness, skin.bodyMetallic);
            var glove = MatOf(skin, "glove", skin.gloveColor, 0.35f, 0f);

            for (int side = -1; side <= 1; side += 2)
            {
                string s = side < 0 ? "L" : "R";
                Cyl(NewNode("Arm" + s, t), new Vector3(side * sx, sy + 0.01f, 0.06f),
                    new Vector3(0.150f, 0.135f, 0.150f), shell, new Vector3(0f, 0f, side * -40f));
                Sphere(NewNode("Hand" + s, t), new Vector3(side * hx, hy, 0.12f), Vector3.one * 0.160f, glove);
            }
        }

        static void BuildBoots(Transform t, SkinDefinition skin, Metrics m)
        {
            float r = m.faceRadius;
            var boot = MatOf(skin, "boot", skin.bootColor, 0.35f, 0f);
            float bx = Mathf.Max(0.10f, r * 0.36f);
            float bz = Mathf.Max(0.16f, r * 0.55f);

            Sphere(NewNode("BootL", t), new Vector3(-bx, 0.055f, bz), new Vector3(0.180f, 0.110f, 0.250f), boot);
            Sphere(NewNode("BootR", t), new Vector3(bx, 0.055f, bz), new Vector3(0.180f, 0.110f, 0.250f), boot);
        }

        static void BuildHat(Transform t, SkinDefinition skin, Metrics m)
        {
            if (skin.hat == BombHatType.None) return;

            float top = m.height;
            var hat = MatOf(skin, "hat", skin.hatColor, 0.4f, 0f);
            var band = BandMat(skin);
            float bz = -0.02f;

            switch (skin.hat)
            {
                case BombHatType.Bowler:
                    Cyl(NewNode("HatBrim", t), new Vector3(0f, top + 0.045f, bz), new Vector3(0.520f, 0.009f, 0.520f), hat);
                    Cyl(NewNode("HatCrown", t), new Vector3(0f, top + 0.108f, bz), new Vector3(0.300f, 0.040f, 0.300f), hat);
                    Cyl(NewNode("HatBand", t), new Vector3(0f, top + 0.065f, bz), new Vector3(0.315f, 0.009f, 0.315f), band);
                    break;

                case BombHatType.TopHat:
                    Cyl(NewNode("HatBrim", t), new Vector3(0f, top + 0.045f, bz), new Vector3(0.560f, 0.007f, 0.560f), hat);
                    Cyl(NewNode("HatCrown", t), new Vector3(0f, top + 0.152f, bz), new Vector3(0.340f, 0.095f, 0.340f), hat);
                    Cyl(NewNode("HatBand", t), new Vector3(0f, top + 0.070f, bz), new Vector3(0.352f, 0.012f, 0.352f), band);
                    break;

                case BombHatType.Party:
                    Cyl(NewNode("PartyCone", t), new Vector3(0f, top + 0.135f, bz), new Vector3(0.260f, 0.075f, 0.260f), hat);
                    Cyl(NewNode("PartyConeTop", t), new Vector3(0f, top + 0.238f, bz), new Vector3(0.140f, 0.055f, 0.140f), hat);
                    Sphere(NewNode("PartyBall", t), new Vector3(0f, top + 0.302f, bz), Vector3.one * 0.095f, band);
                    break;
            }
        }

        // ============================================================ 材质
        static Material MetalMat(SkinDefinition s)
            => MatOf(s, "metal", new Color(0.29f, 0.29f, 0.33f), 0.62f, 0.85f);

        static Material FuseMat(SkinDefinition s)
            => MatOf(s, "fuse", s.fuseColor, 0.12f, 0f);

        static Material BandMat(SkinDefinition s)
            => MatOf(s, "band", s.bandColor, 0.35f, 0f);

        static Material SparkMat(SkinDefinition s)
            => MatOf(s, "spark", new Color(1f, 0.69f, 0.23f), 0f, 0f, new Color(1f, 0.62f, 0.15f) * 2.2f);

        // ============================================================ 基础件
        static Transform NewNode(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // 按名字缓存：现在有两种回转体（圆雷 / 炮弹），不能共用一个静态字段
        static readonly Dictionary<string, Mesh> RevolveCache = new Dictionary<string, Mesh>();

        static Mesh RevolveMesh(string name, Vector2[] profile)
        {
            if (RevolveCache.TryGetValue(name, out var cached) && cached != null) return cached;

            const int segments = 32;
            int n = profile.Length;
            var verts = new List<Vector3>(segments * n);
            var uvs = new List<Vector2>(segments * n);

            for (int i = 0; i < segments; i++)
            {
                float u = (float)i / (segments - 1);
                float ang = u * Mathf.PI * 2f;
                float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);
                for (int j = 0; j < n; j++)
                {
                    verts.Add(new Vector3(profile[j].x * cos, profile[j].y, profile[j].x * sin));
                    uvs.Add(new Vector2(u, (float)j / (n - 1)));
                }
            }

            var tris = new List<int>();
            for (int i = 0; i < segments - 1; i++)
            {
                for (int j = 0; j < n - 1; j++)
                {
                    int a = i * n + j;
                    int b = (i + 1) * n + j;
                    int c = (i + 1) * n + j + 1;
                    int d = i * n + j + 1;
                    tris.Add(a); tris.Add(d); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(b);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            mesh.hideFlags = HideFlags.DontSave;
            RevolveCache[name] = mesh;
            return mesh;
        }

        static GameObject Sphere(Transform parent, Vector3 pos, Vector3 scale, Material mat)
            => Prim(PrimitiveType.Sphere, parent, pos, scale, mat);

        static GameObject Cube(Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
            => Prim(PrimitiveType.Cube, parent, pos, scale, mat, euler);

        static GameObject Cyl(Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
            => Prim(PrimitiveType.Cylinder, parent, pos, scale, mat, euler);

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat,
            Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = mat != null ? mat.name : type.ToString();

            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localScale = scale;
            if (euler.HasValue) t.localEulerAngles = euler.Value;

            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        static GameObject MeshObj(Mesh mesh, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = new GameObject(mesh != null ? mesh.name : "Mesh");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        static readonly Dictionary<string, Material> MatCache = new Dictionary<string, Material>();
        static Shader _standard;

        static Shader StandardShader
        {
            get
            {
                if (_standard == null) _standard = Shader.Find("Standard");
                return _standard;
            }
        }

        static Material MatOf(SkinDefinition skin, string usage, Color color, float smooth, float metal,
            Color? emission = null)
        {
            string key = (skin != null ? skin.skinId : "null") + "/" + usage;
            if (MatCache.TryGetValue(key, out var m) && m != null) return m;

            m = new Material(StandardShader) { name = key, hideFlags = HideFlags.DontSave };
            m.color = color;
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metal);

            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor("_EmissionColor", emission.Value);
            }

            MatCache[key] = m;
            return m;
        }
    }
}
