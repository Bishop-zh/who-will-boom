using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static LiarsBar.EditorTools.LiarsBarKit;

namespace LiarsBar.EditorTools
{
    /// <summary>
    /// 一键生成「骗子酒馆」风格场景：木屋酒馆 + 圆桌 + 4 个卡通炸弹角色。
    /// 菜单：工具 / 炸弹酒馆 / 0. 生成酒馆基础场景
    /// </summary>
    public static class LiarsBarSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/LiarsBar_BombTavern.unity";
        const string RootName = "骗子酒馆_LiarsBar";

        static string MarkerPath =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", ".liarsbar_build_request");

        // ============================================================ 菜单入口
        [MenuItem("工具/炸弹酒馆/0. 生成酒馆基础场景", false, 0)]
        public static void BuildFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => BuildScene(false);
                return;
            }
            BuildScene(false);
        }

        [MenuItem("工具/炸弹酒馆/0b. 修复基础场景角色", false, 1)]
        public static void SpawnBombsOnly()
        {
            var root = GameObject.Find(RootName);
            if (root == null) { Debug.LogWarning("[骗子酒馆] 请先生成场景。"); return; }
            SpawnCharacters(root.transform);
            Debug.Log("[骗子酒馆] 已补齐卡通炸弹角色。");
        }

        // ============================================================ 自动构建
        [InitializeOnLoadMethod]
        static void CheckAutoBuild()
        {
            if (!File.Exists(MarkerPath)) return;
            try { File.Delete(MarkerPath); } catch { /* 忽略 */ }
            EditorApplication.delayCall += () => BuildScene(true);
        }

        // ============================================================ 主流程
        public static void BuildScene(bool auto)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (!auto) EditorUtility.DisplayDialog("骗子酒馆", "请先退出播放模式。", "好的");
                return;
            }

            EnsureFolders();

            var prev = SceneManager.GetActiveScene();
            if (!auto && prev.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject(RootName);

            // ---- 建筑与陈设
            TavernFactory.BuildRoom(root.transform);
            TavernFactory.BuildBarCounter(root.transform);
            TavernFactory.BuildCenterTable(root.transform);
            for (int i = 0; i < TavernFactory.SeatAngles.Length; i++)
                TavernFactory.BuildChair(root.transform, i);

            var table = root.transform.Find("CenterTable");
            if (table != null) TavernFactory.BuildTableProps(table);

            // ---- 卡通炸弹角色
            SpawnCharacters(root.transform);

            // ---- 灯光 / 相机 / 氛围
            BuildLighting(root.transform);
            BuildCamera(root.transform);
            ApplyRenderSettings();

            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[骗子酒馆] 场景已生成：{ScenePath}（4 个卡通炸弹角色 + 酒馆陈设）");

        }

        // ============================================================ 角色
        static void SpawnCharacters(Transform root)
        {
            var old = root.Find("Characters");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var parent = Empty("Characters", root, Vector3.zero).transform;

            var styles = new[]
            {
                new BombStyle { Name = "RedBolt",  Display = "红雷",   Accent = Pal.Red,    MaterialKey = "red",
                                Hat = HatStyle.Bowler,  Grumpy = true,  OpenMouth = false, PupilOffset = -0.2f },
                new BombStyle { Name = "BlueFlame",Display = "蓝焰",   Accent = Pal.Blue,   MaterialKey = "blue",
                                Hat = HatStyle.None,    Grumpy = false, OpenMouth = true,  PupilOffset = 0.35f },
                new BombStyle { Name = "GreenFuse",Display = "青引",   Accent = Pal.Green,  MaterialKey = "green",
                                Hat = HatStyle.Party,   Grumpy = false, OpenMouth = true,  PupilOffset = 0.1f },
                new BombStyle { Name = "GoldTooth",Display = "金牙",   Accent = Pal.Yellow, MaterialKey = "yellow",
                                Hat = HatStyle.TopHat,  Grumpy = true,  OpenMouth = false, PupilOffset = -0.35f },
            };

            for (int i = 0; i < styles.Length && i < TavernFactory.SeatAngles.Length; i++)
            {
                float ang = TavernFactory.SeatAngles[i] * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));

                // 往桌前挪一点、坐高一点：角色落在椅子前沿而不是正中间。
                // 这样做是为了对面那位 —— 他离视点最远，稍微前移 + 抬高，
                // 上半身就能明显升到桌沿之上，跨桌看得见脸。
                var pos = dir * (TavernFactory.SeatRadius - 0.14f);
                pos.y = 0.58f;
                float yaw = TavernFactory.SeatAngles[i] + 180f;  // 面朝桌心

                var go = BombFactory.Build(styles[i], parent, pos, yaw);
                var comp = go.GetComponent<BombCharacter>();
                if (comp != null) comp.seatIndex = i;
            }
        }

        // ============================================================ 灯光
        static void BuildLighting(Transform root)
        {
            var lights = Empty("Lighting", root, Vector3.zero).transform;

            var metal = Mat("Metal", Pal.Metal, 0.62f, 0.85f);
            var brass = Mat("Brass", Pal.Brass, 0.72f, 0.9f);
            var shade = Mat("LampShade", new Color(0.16f, 0.13f, 0.11f), 0.35f);
            var glow = Mat("LampGlow", Pal.LampGlow, 0f, 0f, Pal.LampGlow * 2.6f);
            var ember = Mat("Ember", Pal.FireGlow, 0f, 0f, Pal.FireGlow * 2.4f);
            var stone = Mat("Stone", new Color(0.26f, 0.25f, 0.24f), 0.12f);

            // ---- 桌面吊灯
            var lamp = Empty("HangingLamp", lights, new Vector3(0f, 0f, 0f)).transform;
            Cylinder("Cord", lamp, new Vector3(0f, 3.60f, 0f), new Vector3(0.035f, 0.42f, 0.035f), metal);
            Cylinder("Shade", lamp, new Vector3(0f, 3.14f, 0f), new Vector3(1.10f, 0.16f, 1.10f), shade);
            Cylinder("ShadeInner", lamp, new Vector3(0f, 2.99f, 0f), new Vector3(0.94f, 0.012f, 0.94f), glow);
            Sphere("Bulb", lamp, new Vector3(0f, 2.90f, 0f), Vector3.one * 0.20f, glow);
            AddLight(lamp, "MainLamp", new Vector3(0f, 2.82f, 0f), new Color(1f, 0.84f, 0.60f), 2.6f, 11f,
                LightShadows.Soft);

            // ---- 壁灯
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f;
                var s = Empty("Sconce_" + i, lights, new Vector3(sx * 5.35f, 2.55f, 2.2f)).transform;
                s.localRotation = Quaternion.Euler(0f, sx * -90f, 0f);
                Cube("Bracket", s, new Vector3(0f, 0f, -0.10f), new Vector3(0.10f, 0.42f, 0.20f), metal);
                Cylinder("Cup", s, new Vector3(0f, 0.24f, 0.10f), new Vector3(0.44f, 0.08f, 0.44f), brass);
                Sphere("Flame", s, new Vector3(0f, 0.34f, 0.10f), Vector3.one * 0.14f, glow);
                AddLight(s, "SconceLight", new Vector3(0f, 0.30f, 0.22f), new Color(1f, 0.78f, 0.52f), 1.2f, 6.5f,
                    LightShadows.None);
            }

            // ---- 壁炉
            var fp = Empty("Fireplace", lights, new Vector3(-4.3f, 0f, 6.22f)).transform;
            Cube("Surround", fp, new Vector3(0f, 0.95f, 0f), new Vector3(2.30f, 1.90f, 0.40f), stone);
            Cube("Opening", fp, new Vector3(0f, 0.72f, 0.24f), new Vector3(1.42f, 1.24f, 0.20f),
                Mat("FireHole", new Color(0.05f, 0.04f, 0.04f), 0.1f));
            Cube("Mantle", fp, new Vector3(0f, 2.02f, 0.06f), new Vector3(2.62f, 0.16f, 0.56f),
                Mat("WoodWarm", Pal.WoodWarm, 0.30f));
            Sphere("Logs", fp, new Vector3(0f, 0.22f, 0.20f), new Vector3(0.90f, 0.34f, 0.36f), ember);
            Sphere("Embers", fp, new Vector3(0f, 0.14f, 0.30f), new Vector3(1.10f, 0.16f, 0.30f), ember);
            AddLight(fp, "FireLight", new Vector3(0f, 0.85f, 0.75f), new Color(1f, 0.48f, 0.16f), 2.2f, 9f,
                LightShadows.None);

            // ---- 环境补光（冷色，压暗）
            var fillGo = new GameObject("FillLight");
            fillGo.transform.SetParent(lights, false);
            fillGo.transform.localPosition = new Vector3(2.5f, 3.6f, 3.5f);
            fillGo.transform.localRotation = Quaternion.Euler(48f, -140f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.42f, 0.50f, 0.72f);
            fill.intensity = 0.24f;
            fill.shadows = LightShadows.None;
        }

        static void AddLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range,
            LightShadows shadows)
        {
            var go = Empty(name, parent, pos);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = shadows;
            if (shadows != LightShadows.None) l.shadowStrength = 0.85f;
        }

        // ============================================================ 相机
        static void BuildCamera(Transform root)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.035f, 0.030f, 0.040f);
            camGo.AddComponent<AudioListener>();

            var orbit = camGo.AddComponent<TavernCameraOrbit>();
            orbit.target = new Vector3(0f, 1.05f, 0f);
            orbit.distance = 4.8f;
            orbit.height = 1.55f;
        }

        // ============================================================ 氛围
        static void ApplyRenderSettings()
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.055f, 0.045f, 0.050f);
            RenderSettings.fogDensity = 0.028f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.13f, 0.13f, 0.17f);
            RenderSettings.ambientEquatorColor = new Color(0.10f, 0.08f, 0.07f);
            RenderSettings.ambientGroundColor = new Color(0.045f, 0.035f, 0.030f);
            RenderSettings.ambientIntensity = 0.85f;

            QualitySettings.shadowDistance = 30f;
        }
    }
}
