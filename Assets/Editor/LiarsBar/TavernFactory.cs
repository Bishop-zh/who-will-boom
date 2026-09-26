using UnityEngine;
using static LiarsBar.EditorTools.LiarsBarKit;

namespace LiarsBar.EditorTools
{
    /// <summary>骗子酒馆场景建筑与陈设工厂。</summary>
    public static class TavernFactory
    {
        public const float RoomSize = 13f;
        public const float WallHeight = 4.0f;
        public const float TableTopY = 0.84f;

        // 四个座位的角度（度），与桌心距离
        public const float SeatRadius = 1.98f;
        public static readonly float[] SeatAngles = { 45f, 135f, 225f, 315f };

        static Material M_WoodDark, M_Wood, M_WoodLight, M_WoodWarm, M_Felt, M_FeltDark,
                        M_Brass, M_Metal, M_Iron, M_Card, M_Glass, M_Ink,
                        M_BottleGreen, M_BottleAmber, M_BottleRed;

        static void InitMats()
        {
            M_WoodDark = Mat("WoodDark", Pal.WoodDark, 0.18f);
            M_Wood = Mat("Wood", Pal.Wood, 0.22f);
            M_WoodLight = Mat("WoodLight", Pal.WoodLight, 0.28f);
            M_WoodWarm = Mat("WoodWarm", Pal.WoodWarm, 0.30f);
            M_Felt = Mat("Felt", Pal.Felt, 0.06f);
            M_FeltDark = Mat("FeltDark", Pal.FeltDark, 0.06f);
            M_Brass = Mat("Brass", Pal.Brass, 0.72f, 0.9f);
            M_Metal = Mat("Metal", Pal.Metal, 0.62f, 0.85f);
            M_Iron = Mat("Iron", Pal.Iron, 0.35f, 0.6f);
            M_Card = Mat("Card", Pal.Card, 0.35f);
            M_Glass = Mat("Glass", Pal.Glass, 0.92f, 0.1f);
            M_Ink = Mat("Ink", Pal.Ink, 0.3f);
            M_BottleGreen = Mat("BottleGreen", Pal.BottleGreen, 0.88f, 0.1f);
            M_BottleAmber = Mat("BottleAmber", Pal.BottleAmber, 0.88f, 0.1f);
            M_BottleRed = Mat("BottleRed", Pal.BottleRed, 0.88f, 0.1f);
        }

        // ============================================================ 房间
        public static void BuildRoom(Transform root)
        {
            InitMats();
            var room = Empty("Room", root, Vector3.zero).transform;
            float h = RoomSize * 0.5f;

            // 地板 + 地板缝
            Prim(PrimitiveType.Plane, "Floor", room, new Vector3(0f, 0f, 0f),
                new Vector3(RoomSize / 10f, 1f, RoomSize / 10f), M_Wood);
            for (int i = -6; i <= 6; i++)
                Cube("FloorSeam_" + i, room, new Vector3(0f, 0.006f, i * 1.0f),
                    new Vector3(RoomSize, 0.012f, 0.035f), M_WoodDark);

            // 天花板
            Cube("Ceiling", room, new Vector3(0f, WallHeight, 0f), new Vector3(RoomSize, 0.25f, RoomSize), M_WoodDark);

            // 四面墙
            Cube("Wall_N", room, new Vector3(0f, WallHeight / 2f, h), new Vector3(RoomSize, WallHeight, 0.3f), M_WoodDark);
            Cube("Wall_S", room, new Vector3(0f, WallHeight / 2f, -h), new Vector3(RoomSize, WallHeight, 0.3f), M_WoodDark);
            Cube("Wall_E", room, new Vector3(h, WallHeight / 2f, 0f), new Vector3(0.3f, WallHeight, RoomSize), M_WoodDark);
            Cube("Wall_W", room, new Vector3(-h, WallHeight / 2f, 0f), new Vector3(0.3f, WallHeight, RoomSize), M_WoodDark);

            // 墙裙
            float w = h - 0.15f;
            Cube("Wain_N", room, new Vector3(0f, 0.55f, w), new Vector3(RoomSize, 1.1f, 0.12f), M_Wood);
            Cube("Wain_S", room, new Vector3(0f, 0.55f, -w), new Vector3(RoomSize, 1.1f, 0.12f), M_Wood);
            Cube("Wain_E", room, new Vector3(w, 0.55f, 0f), new Vector3(0.12f, 1.1f, RoomSize), M_Wood);
            Cube("Wain_W", room, new Vector3(-w, 0.55f, 0f), new Vector3(0.12f, 1.1f, RoomSize), M_Wood);
            // 压条
            Cube("Rail_N", room, new Vector3(0f, 1.12f, w), new Vector3(RoomSize, 0.10f, 0.18f), M_WoodLight);
            Cube("Rail_S", room, new Vector3(0f, 1.12f, -w), new Vector3(RoomSize, 0.10f, 0.18f), M_WoodLight);
            Cube("Rail_E", room, new Vector3(w, 1.12f, 0f), new Vector3(0.18f, 0.10f, RoomSize), M_WoodLight);
            Cube("Rail_W", room, new Vector3(-w, 1.12f, 0f), new Vector3(0.18f, 0.10f, RoomSize), M_WoodLight);

            // 天花横梁
            for (int i = -2; i <= 2; i++)
                Cube("Beam_" + (i + 3), room, new Vector3(i * 2.6f, WallHeight - 0.28f, 0f),
                    new Vector3(0.32f, 0.32f, RoomSize), M_WoodDark);

            // 四角立柱
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Cube("Post", room, new Vector3(sx * w, WallHeight / 2f, sz * w),
                        new Vector3(0.45f, WallHeight, 0.45f), M_WoodDark);

            // 挂画（简化：木框 + 画布）
            for (int i = 0; i < 4; i++)
            {
                float ang = i * 90f + 45f;
                var dir = new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad), 0f, Mathf.Cos(ang * Mathf.Deg2Rad));
                var pos = dir * (w - 0.06f) + Vector3.up * 2.35f;
                var frame = Cube("Frame_" + i, room, pos, new Vector3(1.15f, 0.85f, 0.08f), M_WoodLight);
                frame.transform.localRotation = Quaternion.LookRotation(-dir, Vector3.up);
                var canvas = Cube("Canvas_" + i, frame.transform, new Vector3(0f, 0f, 0.07f),
                    new Vector3(0.92f, 0.66f, 0.02f), Mat("Canvas_" + i, i % 2 == 0 ? Pal.FeltDark : Pal.BottleAmber, 0.2f));
                canvas.transform.localRotation = Quaternion.identity;
            }
        }

        // ============================================================ 吧台
        public static void BuildBarCounter(Transform root)
        {
            var bar = Empty("BarCounter", root, Vector3.zero).transform;

            // 台体 + 台面
            Cube("BarBody", bar, new Vector3(-0.6f, 0.60f, -5.15f), new Vector3(6.4f, 1.20f, 0.85f), M_WoodDark);
            Cube("BarTop", bar, new Vector3(-0.6f, 1.26f, -5.15f), new Vector3(6.9f, 0.14f, 1.05f), M_WoodWarm);
            Cube("BarFoot", bar, new Vector3(-0.6f, 0.06f, -5.15f), new Vector3(6.6f, 0.12f, 0.95f), M_Brass);

            // 靠墙酒柜
            Cube("BackShelf", bar, new Vector3(-0.6f, 1.85f, -6.15f), new Vector3(6.4f, 3.0f, 0.22f), M_WoodDark);
            for (int i = 0; i < 3; i++)
                Cube("ShelfPlank_" + i, bar, new Vector3(-0.6f, 1.15f + i * 0.83f, -6.02f),
                    new Vector3(6.2f, 0.09f, 0.42f), M_WoodLight);

            // 酒瓶
            var bottleMats = new[] { M_BottleGreen, M_BottleAmber, M_BottleRed };
            int idx = 0;
            for (int row = 0; row < 3; row++)
            {
                float y = 1.31f + row * 0.83f;
                for (int i = 0; i < 7; i++)
                {
                    float x = -3.3f + i * 0.9f + (row % 2 == 0 ? 0f : 0.45f);
                    var mat = bottleMats[idx++ % bottleMats.Length];
                    float hgt = 0.42f + (i % 3) * 0.06f;
                    Cylinder("Bottle", bar, new Vector3(x, y + hgt * 0.5f, -6.02f),
                        new Vector3(0.26f, hgt * 0.5f, 0.26f), mat);
                    Cylinder("BottleNeck", bar, new Vector3(x, y + hgt + 0.09f, -6.02f),
                        new Vector3(0.10f, 0.10f, 0.10f), mat);
                }
            }

            // 吧台凳
            for (int i = 0; i < 4; i++)
            {
                float x = -3.0f + i * 1.6f;
                var s = Empty("Stool_" + i, bar, new Vector3(x, 0f, -4.35f)).transform;
                Cylinder("Seat", s, new Vector3(0f, 0.78f, 0f), new Vector3(0.92f, 0.045f, 0.92f), M_WoodWarm);
                Cylinder("SeatTrim", s, new Vector3(0f, 0.74f, 0f), new Vector3(0.86f, 0.02f, 0.86f), M_Brass);
                Cylinder("Post", s, new Vector3(0f, 0.40f, 0f), new Vector3(0.13f, 0.36f, 0.13f), M_Metal);
                Cylinder("Base", s, new Vector3(0f, 0.03f, 0f), new Vector3(0.62f, 0.025f, 0.62f), M_Metal);
            }

            // 墙上飞镖盘
            var dart = Empty("Dartboard", bar, new Vector3(4.4f, 2.35f, -6.05f)).transform;
            Cylinder("Board", dart, Vector3.zero, new Vector3(0.86f, 0.05f, 0.86f),
                Mat("DartCork", new Color(0.78f, 0.68f, 0.48f), 0.15f), new Vector3(90f, 0f, 0f));
            Cylinder("Bull", dart, new Vector3(0f, 0f, 0.08f), new Vector3(0.16f, 0.03f, 0.16f),
                Mat("DartBull", Pal.Red, 0.3f), new Vector3(90f, 0f, 0f));
        }

        // ============================================================ 桌子
        public static void BuildCenterTable(Transform root)
        {
            var table = Empty("CenterTable", root, Vector3.zero).transform;
            Cylinder("Foot", table, new Vector3(0f, 0.05f, 0f), new Vector3(1.15f, 0.05f, 1.15f), M_WoodDark);
            Cylinder("Pedestal", table, new Vector3(0f, 0.36f, 0f), new Vector3(0.34f, 0.32f, 0.34f), M_Wood);
            Cylinder("Apron", table, new Vector3(0f, 0.70f, 0f), new Vector3(2.26f, 0.055f, 2.26f), M_WoodLight);
            Cylinder("Top", table, new Vector3(0f, 0.79f, 0f), new Vector3(2.20f, 0.050f, 2.20f), M_WoodWarm);
            Cylinder("TopRim", table, new Vector3(0f, 0.83f, 0f), new Vector3(2.16f, 0.012f, 2.16f), M_Brass);
            Cylinder("Felt", table, new Vector3(0f, 0.845f, 0f), new Vector3(1.94f, 0.012f, 1.94f), M_Felt);

            // 桌上烟灰缸 / 筹码托盘
            Cylinder("Ashtray", table, new Vector3(-0.82f, 0.90f, -0.42f), new Vector3(0.34f, 0.022f, 0.34f), M_Metal);
            Cylinder("AshtrayIn", table, new Vector3(-0.82f, 0.914f, -0.42f), new Vector3(0.26f, 0.010f, 0.26f), M_Ink);
        }

        // ============================================================ 椅子
        public static GameObject BuildChair(Transform root, int index)
        {
            float ang = SeatAngles[index] * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            var pos = dir * SeatRadius;

            var chair = Empty("Chair_" + index, root, pos);
            chair.transform.localRotation = Quaternion.LookRotation(-dir, Vector3.up);
            var c = chair.transform;

            Cube("Seat", c, new Vector3(0f, 0.47f, 0f), new Vector3(0.86f, 0.09f, 0.86f), M_WoodWarm);
            Cube("SeatTrim", c, new Vector3(0f, 0.42f, 0f), new Vector3(0.92f, 0.03f, 0.92f), M_WoodDark);
            Cube("Back", c, new Vector3(0f, 0.90f, -0.40f), new Vector3(0.86f, 0.84f, 0.10f), M_Wood);
            Cube("BackTop", c, new Vector3(0f, 1.34f, -0.40f), new Vector3(0.94f, 0.10f, 0.15f), M_WoodLight);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Cylinder("Leg", c, new Vector3(sx * 0.34f, 0.22f, sz * 0.34f),
                        new Vector3(0.10f, 0.22f, 0.10f), M_WoodDark);

            return chair;
        }

        // ============================================================ 桌上道具
        public static void BuildTableProps(Transform table)
        {
            var p = Empty("Props", table, Vector3.zero).transform;
            float y = 0.86f;

            // 牌堆
            Cube("Deck", p, new Vector3(-0.42f, y + 0.035f, 0.20f), new Vector3(0.26f, 0.07f, 0.36f), M_Card,
                new Vector3(0f, 14f, 0f));
            Cube("DeckBox", p, new Vector3(-0.42f, y + 0.085f, 0.20f), new Vector3(0.28f, 0.04f, 0.38f),
                Mat("DeckBox", new Color(0.55f, 0.13f, 0.13f), 0.35f), new Vector3(0f, 14f, 0f));

            // 散落的牌
            var cardSpots = new[]
            {
                new Vector3(0.05f, 0.0f, 0.42f), new Vector3(0.28f, 0.0f, 0.30f),
                new Vector3(-0.12f, 0.0f, 0.58f), new Vector3(0.44f, 0.0f, 0.56f),
                new Vector3(0.62f, 0.0f, 0.24f), new Vector3(-0.55f, 0.0f, 0.52f),
            };
            for (int i = 0; i < cardSpots.Length; i++)
                Cube("Card_" + i, p, new Vector3(cardSpots[i].x, y + 0.006f, cardSpots[i].z),
                    new Vector3(0.21f, 0.008f, 0.31f), M_Card,
                    new Vector3(0f, i * 27f - 40f, 0f));

            // 酒瓶
            var bottle = Empty("Whisky", p, new Vector3(0.62f, 0f, -0.30f)).transform;
            Cylinder("Body", bottle, new Vector3(0f, y + 0.15f, 0f), new Vector3(0.22f, 0.15f, 0.22f), M_BottleAmber);
            Cylinder("Shoulder", bottle, new Vector3(0f, y + 0.34f, 0f), new Vector3(0.15f, 0.05f, 0.15f), M_BottleAmber);
            Cylinder("Neck", bottle, new Vector3(0f, y + 0.45f, 0f), new Vector3(0.09f, 0.08f, 0.09f), M_BottleAmber);
            Cylinder("Cork", bottle, new Vector3(0f, y + 0.55f, 0f), new Vector3(0.075f, 0.025f, 0.075f), M_WoodLight);

            // 酒杯
            BuildGlass(p, new Vector3(-0.06f, 0f, -0.30f), y);
            BuildGlass(p, new Vector3(0.16f, 0f, -0.52f), y);

            // 左轮手枪
            var gun = Empty("Revolver", p, new Vector3(-0.28f, y + 0.06f, -0.66f)).transform;
            gun.localRotation = Quaternion.Euler(0f, 32f, 0f);
            Cylinder("Barrel", gun, new Vector3(0.20f, 0.045f, 0f), new Vector3(0.075f, 0.20f, 0.075f), M_Metal,
                new Vector3(0f, 0f, 90f));
            Cylinder("Chamber", gun, new Vector3(-0.03f, 0.045f, 0f), new Vector3(0.145f, 0.10f, 0.145f), M_Metal,
                new Vector3(0f, 0f, 90f));
            Cylinder("ChamberPin", gun, new Vector3(-0.03f, 0.045f, 0f), new Vector3(0.05f, 0.12f, 0.05f), M_Brass,
                new Vector3(0f, 0f, 90f));
            Cube("Grip", gun, new Vector3(-0.17f, -0.06f, 0f), new Vector3(0.10f, 0.22f, 0.13f), M_WoodDark,
                new Vector3(0f, 0f, 16f));
            Cube("Trigger", gun, new Vector3(-0.07f, -0.02f, 0f), new Vector3(0.05f, 0.08f, 0.03f), M_Metal);

            // 金币
            for (int i = 0; i < 5; i++)
                Cylinder("Coin_" + i, p, new Vector3(-0.62f + i * 0.06f, y + 0.012f, 0.10f + (i % 2) * 0.05f),
                    new Vector3(0.16f, 0.008f, 0.16f), M_Brass);

            // 雪茄
            Cylinder("Cigar", p, new Vector3(-0.90f, y + 0.03f, -0.42f), new Vector3(0.055f, 0.11f, 0.055f),
                Mat("Cigar", new Color(0.32f, 0.19f, 0.11f), 0.2f), new Vector3(0f, 24f, 88f));
            Sphere("CigarTip", p, new Vector3(-0.90f, y + 0.03f, -0.53f), Vector3.one * 0.055f,
                Mat("Ember", Pal.FireGlow, 0f, 0f, Pal.FireGlow * 2.2f));
        }

        static void BuildGlass(Transform parent, Vector3 pos, float y)
        {
            var g = Empty("Glass", parent, new Vector3(pos.x, 0f, pos.z)).transform;
            Cylinder("Base", g, new Vector3(0f, y + 0.012f, 0f), new Vector3(0.20f, 0.012f, 0.20f), M_Glass);
            Cylinder("Stem", g, new Vector3(0f, y + 0.08f, 0f), new Vector3(0.045f, 0.07f, 0.045f), M_Glass);
            Cylinder("Bowl", g, new Vector3(0f, y + 0.22f, 0f), new Vector3(0.19f, 0.09f, 0.19f), M_Glass);
            Cylinder("Drink", g, new Vector3(0f, y + 0.19f, 0f), new Vector3(0.165f, 0.055f, 0.165f),
                Mat("Drink", new Color(0.62f, 0.30f, 0.09f), 0.85f));
        }
    }
}
