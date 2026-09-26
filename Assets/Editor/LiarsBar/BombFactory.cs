using UnityEngine;
using static LiarsBar.EditorTools.LiarsBarKit;

namespace LiarsBar.EditorTools
{
    public enum HatStyle { None, Bowler, TopHat, Party }

    public struct BombStyle
    {
        public string Name;          // 英文标识
        public string Display;       // 中文显示名
        public Color Accent;
        public string MaterialKey;
        public HatStyle Hat;
        public bool Grumpy;          // 皱眉
        public bool OpenMouth;       // 张嘴
        public float PupilOffset;    // 眼珠偏移，制造不同神态
    }

    /// <summary>程序化拼装卡通炸弹角色。</summary>
    public static class BombFactory
    {
        // 卡通炸弹回转剖面：x = 半径, y = 高度（总高 1.0，最大半径 0.65）
        static readonly Vector2[] BodyProfile =
        {
            new Vector2(0.001f, 0.000f),
            new Vector2(0.260f, 0.015f),
            new Vector2(0.440f, 0.070f),
            new Vector2(0.570f, 0.190f),
            new Vector2(0.645f, 0.360f),
            new Vector2(0.650f, 0.520f),
            new Vector2(0.600f, 0.660f),
            new Vector2(0.480f, 0.770f),
            new Vector2(0.320f, 0.840f),
            new Vector2(0.200f, 0.875f),
            new Vector2(0.175f, 0.930f),
            new Vector2(0.175f, 0.985f),
            new Vector2(0.001f, 1.000f),
        };

        const float BodyScale = 0.86f;

        // 整体缩放。
        // 原来是 0.84，站在桌边视角看对面角色「小得像摆件」——因为角色的头
        // 刚好和桌面齐平，跨桌几乎看不见脸。放大到 1.25 之后上半身明显高出桌面，
        // 对面是谁一眼可辨；同时还没大到把椅子吞掉。
        const float RootScale = 1.25f;

        public static GameObject Build(BombStyle style, Transform parent, Vector3 localPos, float yawDegrees)
        {
            var shell = Mat("BombShell", Pal.BombShell, 0.45f, 0.25f);
            var metal = Mat("Metal", Pal.Metal, 0.62f, 0.85f);
            var ink = Mat("Ink", Pal.Ink, 0.30f);
            var white = Mat("EyeWhite", Pal.White, 0.55f);
            var fuse = Mat("Fuse", Pal.Fuse, 0.12f);
            var spark = Mat("Spark", Pal.Spark, 0.0f, 0f, Pal.Spark * 2.4f);
            var boot = Mat("Boot", new Color(0.13f, 0.13f, 0.16f), 0.35f);
            var accent = Mat("Accent_" + style.MaterialKey, style.Accent, 0.35f);

            var root = Empty("Bomb_" + style.Name, parent, localPos);
            root.transform.localEulerAngles = new Vector3(0f, yawDegrees, 0f);
            root.transform.localScale = Vector3.one * RootScale;

            var bodyRoot = Empty("BodyRoot", root.transform, Vector3.zero);
            var t = bodyRoot.transform;

            // ---- 弹体
            var bodyMesh = Revolve("BombBodyMesh", BodyProfile, 32);
            MeshObj(bodyMesh, "Body", t, Vector3.zero, Vector3.one * BodyScale, shell);

            // ---- 腰环（角色配色）
            Cylinder("Band", t, new Vector3(0f, 0.33f, 0f), new Vector3(1.11f, 0.020f, 1.11f), accent);
            // ---- 引信底座
            Cylinder("Cap", t, new Vector3(0f, 0.865f, 0f), new Vector3(0.30f, 0.032f, 0.30f), metal);
            Cylinder("CapRing", t, new Vector3(0f, 0.898f, 0f), new Vector3(0.345f, 0.012f, 0.345f), accent);

            // ---- 引信（两节，略带弯折）
            Cylinder("Fuse1", t, new Vector3(0f, 0.945f, 0f), new Vector3(0.055f, 0.036f, 0.055f), fuse,
                new Vector3(6f, 0f, 0f));
            Cylinder("Fuse2", t, new Vector3(0.012f, 0.995f, 0.020f), new Vector3(0.050f, 0.032f, 0.050f), fuse,
                new Vector3(24f, 0f, 0f));
            Sphere("Spark", t, new Vector3(0.020f, 1.038f, 0.052f), Vector3.one * 0.085f, spark);

            // ---- 眼睛
            BuildEye(t, "EyeL", -0.150f, style.PupilOffset, white, ink);
            BuildEye(t, "EyeR", 0.150f, style.PupilOffset, white, ink);

            // ---- 眉毛
            float brow = style.Grumpy ? 20f : -8f;
            Cube("BrowL", t, new Vector3(-0.152f, 0.600f, 0.450f), new Vector3(0.150f, 0.032f, 0.050f), ink,
                new Vector3(0f, 0f, -brow));
            Cube("BrowR", t, new Vector3(0.152f, 0.600f, 0.450f), new Vector3(0.150f, 0.032f, 0.050f), ink,
                new Vector3(0f, 0f, brow));

            // ---- 嘴
            if (style.OpenMouth)
                Sphere("Mouth", t, new Vector3(0.0f, 0.345f, 0.525f), new Vector3(0.115f, 0.075f, 0.07f), ink);
            else
                Cube("Mouth", t, new Vector3(0.030f, 0.355f, 0.528f), new Vector3(0.115f, 0.026f, 0.06f), ink,
                    new Vector3(0f, 0f, -9f));

            // ---- 手臂 + 手套
            BuildArm(t, -1f, accent, shell);
            BuildArm(t, 1f, accent, shell);

            // ---- 小短腿 / 靴子
            Sphere("BootL", t, new Vector3(-0.205f, 0.055f, 0.305f), new Vector3(0.180f, 0.110f, 0.250f), boot);
            Sphere("BootR", t, new Vector3(0.205f, 0.055f, 0.305f), new Vector3(0.180f, 0.110f, 0.250f), boot);

            // ---- 帽子
            BuildHat(t, style, accent, metal);

            var comp = root.AddComponent<LiarsBar.BombCharacter>();
            comp.displayName = style.Display;
            comp.accentColor = style.Accent;
            comp.tension = style.Grumpy ? 0.9f : 0.3f;
            comp.bobAmplitude = style.Grumpy ? 0.022f : 0.04f;

            return root;
        }

        static void BuildEye(Transform parent, string name, float x, float pupilOffset, Material white, Material ink)
        {
            var eye = Sphere(name, parent, new Vector3(x, 0.480f, 0.500f), new Vector3(0.155f, 0.175f, 0.130f), white);
            Sphere("Pupil", eye.transform, new Vector3(pupilOffset * 0.4f, -0.02f, 0.52f), new Vector3(0.44f, 0.42f, 0.42f), ink);
            Sphere("Glint", eye.transform, new Vector3(pupilOffset * 0.4f - 0.16f, 0.24f, 0.62f), Vector3.one * 0.16f, white);
        }

        static void BuildArm(Transform parent, float side, Material accent, Material shell)
        {
            string s = side < 0 ? "L" : "R";
            Cylinder("Arm" + s, parent, new Vector3(side * 0.580f, 0.290f, 0.060f),
                new Vector3(0.150f, 0.135f, 0.150f), shell, new Vector3(0f, 0f, side * -40f));
            Sphere("Hand" + s, parent, new Vector3(side * 0.690f, 0.150f, 0.120f),
                Vector3.one * 0.160f, accent);
        }

        static void BuildHat(Transform parent, BombStyle style, Material accent, Material metal)
        {
            switch (style.Hat)
            {
                case HatStyle.Bowler:
                    Cylinder("HatBrim", parent, new Vector3(0f, 0.905f, -0.02f), new Vector3(0.520f, 0.009f, 0.520f), accent);
                    Cylinder("HatCrown", parent, new Vector3(0f, 0.968f, -0.02f), new Vector3(0.300f, 0.040f, 0.300f), accent);
                    Cylinder("HatBand", parent, new Vector3(0f, 0.925f, -0.02f), new Vector3(0.315f, 0.009f, 0.315f), metal);
                    break;
                case HatStyle.TopHat:
                    Cylinder("HatBrim", parent, new Vector3(0f, 0.905f, -0.02f), new Vector3(0.560f, 0.007f, 0.560f), metal);
                    Cylinder("HatCrown", parent, new Vector3(0f, 1.012f, -0.02f), new Vector3(0.340f, 0.095f, 0.340f), metal);
                    Cylinder("HatBand", parent, new Vector3(0f, 0.930f, -0.02f), new Vector3(0.352f, 0.012f, 0.352f), accent);
                    break;
                case HatStyle.Party:
                    Cylinder("PartyCone", parent, new Vector3(0f, 0.995f, -0.01f), new Vector3(0.260f, 0.075f, 0.260f), accent);
                    Cylinder("PartyConeTop", parent, new Vector3(0f, 1.098f, -0.01f), new Vector3(0.140f, 0.055f, 0.140f), accent);
                    Sphere("PartyBall", parent, new Vector3(0f, 1.162f, -0.01f), Vector3.one * 0.095f, metal);
                    break;
            }
        }
    }
}
