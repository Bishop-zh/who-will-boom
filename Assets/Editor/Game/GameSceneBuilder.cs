using System.Collections.Generic;
using BombTavern.Config;
using BombTavern.Net;
using BombTavern.Runtime;
using LiarsBar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BombTavern.EditorTools
{
    /// <summary>
    /// 把玩法内容装配进酒馆场景，产出可直接运行的对局场景。
    /// 批处理入口： -executeMethod BombTavern.EditorTools.GameSceneBuilder.BuildPlayScene
    /// </summary>
    public static class GameSceneBuilder
    {
        const string TavernScene = "Assets/Scenes/LiarsBar_BombTavern.unity";
        const string PlayScene = "Assets/Scenes/BombTavern_Play.unity";
        const string GameRootName = "__BombTavernGame";

        /// <summary>座位数与 GameSession.MaxSeats 保持一致。</summary>
        const int SeatCount = 4;

        // 座位角度与就坐半径的权威来源在 GameConfig（带 Tooltip，便于策划改）。
        // 这里只在 config 缺失时兜底，保证场景仍能装配出来。
        static readonly float[] FallbackSeatAngles = { 45f, 135f, 225f, 315f };
        const float SeatRadius = 1.98f;

        static float[] SeatAnglesOf(GameConfig config)
            => config != null && config.seatAnglesDegrees != null && config.seatAnglesDegrees.Length > 0
                ? config.seatAnglesDegrees
                : FallbackSeatAngles;

        static float StandRadiusOf(GameConfig config)
            => config != null && config.seatStandRadius > 0.1f ? config.seatStandRadius : 1.62f;

        /// <summary>
        /// 每人面板距桌心的距离（米）。
        /// 必须小于桌面半径（1.10），但仍要让面板下沿抬到桌面之上，否则会切进桌子里。
        /// </summary>
        const float PanelRadius = 1.00f;

        /// <summary>
        /// 面板中心高度（米）。桌面在 0.84。
        ///
        /// 这个值不能再低了：面板长边在倾斜后会产生竖直投影，
        /// 中心高必须 ≥ 桌面 + 半个竖直投影，否则**屏幕下沿会扎进桌面**，
        /// 装在那一端的血量灯泡就整个被桌子埋掉（看不见血量就是这个原因）。
        /// </summary>
        const float PanelHeight = 1.12f;

        /// <summary>
        /// 面板相对竖直方向的倾角（度）。**越大越平**。
        ///
        /// 原来 60° 时面板像竖起来的讲台，对手正好被他自己那块挡住；
        /// 70° 接近平放，视线能越过去，低头也依然读得清格子。
        /// </summary>
        const float PanelTiltDegrees = 70f;

        /// <summary>面板盘面区长边（米）：10 行 × 步距。用于反推面板的竖直投影。</summary>
        const float PanelBoardDepth = 10f * (PanelCellSize + PanelCellGap) - PanelCellGap;

        /// <summary>
        /// 屏幕边框留白（米）。要比血量灯泡的直径宽，
        /// 灯泡才嵌得进**上边框**里 —— 上边框是倾斜面板的最高处，绝不会被桌子挡住。
        /// </summary>
        const float PanelBezelMargin = 0.047f;

        /// <summary>桌面高度，与 TavernFactory.TableTopY 保持一致。</summary>
        const float TableTopY = 0.84f;

        /// <summary>面板格子尺寸：四块面板要并排放进桌面，格子必须小一些。</summary>
        const float PanelCellSize = 0.030f;
        const float PanelCellGap = 0.004f;

        /// <summary>第一人称相机（眼睛）高度（米）。</summary>
        const float EyeHeight = 1.32f;

        /// <summary>
        /// 开局俯视角（度，正值表示低头）。面板中心在视线水平线下方约 23°，
        /// 取 21 可以让开局就能完整看到自己那块面板，同时兼顾桌上的其余面板。
        /// </summary>
        const float StartPitchDegrees = 21f;

        /// <summary>玩家出生点到桌心的距离（米）。站在桌沿、椅子之前。</summary>
        const float StandRadius = 1.62f;

        // 与 TavernFactory 一致的房间尺寸
        const float RoomSize = 13f;
        const float WallHeight = 4f;

        // 与 BombFactory 的角色命名保持一致的座位顺序
        static readonly string[] CharacterNames =
        {
            "Bomb_RedBolt", "Bomb_BlueFlame", "Bomb_GreenFuse", "Bomb_GoldTooth",
        };

        [MenuItem("工具/炸弹酒馆/2. 生成可玩对局场景", false, 11)]
        public static void BuildPlaySceneFromMenu() => BuildPlayScene();

        public static void BuildPlayScene()
        {
            if (!System.IO.File.Exists(TavernScene))
            {
                Debug.LogError($"[炸弹酒馆] 找不到酒馆场景 {TavernScene}，请先执行「0. 生成酒馆基础场景」。");
                return;
            }

            // 注意：必须在 OpenScene 之后再解析配置。
            // OpenScene 会触发资产重载，若之前已修好引用会被重新读盘冲掉。
            var scene = EditorSceneManager.OpenScene(TavernScene, OpenSceneMode.Single);

            var config = LoadOrCreateConfig();
            if (config == null) return;

            // 清掉上一次生成的内容
            var oldRoot = GameObject.Find(GameRootName);
            if (oldRoot != null) Object.DestroyImmediate(oldRoot);

            // 酒馆自带的环绕相机与第一人称玩法冲突，移除以免出现两个 AudioListener
            var oldCam = GameObject.Find("Main Camera");
            if (oldCam != null) Object.DestroyImmediate(oldCam);

            var root = new GameObject(GameRootName);

            // 新版播放器直接以本场景启动，不再经过旧菜单场景。
            // 因此会话上下文也必须由可玩场景提供，继续承载昵称、联机模式与皮肤选择。
            var oldContext = Object.FindObjectOfType<SessionContext>();
            if (oldContext != null) Object.DestroyImmediate(oldContext.gameObject);
            var contextGo = new GameObject("SessionContext");
            var context = contextGo.AddComponent<SessionContext>();
            context.skinCatalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(
                GameAssetCreator.SkinDir + "/SkinCatalog.asset");
            context.playerName = "红雷";
            context.connectAddress = string.Empty;
            context.skinId = context.skinCatalog != null && context.skinCatalog.defaultSkin != null
                ? context.skinCatalog.defaultSkin.skinId
                : "round_black";

            // ---- 碰撞体：酒馆陈设是纯视觉几何体（没有 Collider），
            //      第一人称必须有地板与墙，否则会无限下落、走出房间。
            CreateCollisionShell(root.transform);

            // ---- 角色引用（按座位顺序）
            var characters = new BombCharacter[SeatCount];
            for (int i = 0; i < CharacterNames.Length && i < characters.Length; i++)
            {
                var go = GameObject.Find(CharacterNames[i]);
                if (go != null) characters[i] = go.GetComponent<BombCharacter>();
            }

            // ---- 每个座位前面一块面板，全部指向同一份共用盘面
            var tablets = new TabletScreen[SeatCount];
            for (int i = 0; i < SeatCount; i++)
                tablets[i] = CreateSeatPanel(root.transform, i, config);

            // 桌面中央留给面板，杯盘收起来（吧台那边依然有酒瓶等陈设）
            var props = GameObject.Find("CenterTable/Props");
            if (props != null) props.SetActive(false);

            // 地板兜底碰撞体：原地板是一张单面 Plane，极薄。玩家一旦被椅子的碰撞体
            // 挤到它附近，就可能直接穿过去掉出场景。这里在下面垫一块厚盒子，彻底封死。
            EnsureFloorCollider(root.transform);

            // ---- 第一人称玩家装置
            var playerCamera = CreatePlayerRig(root.transform, config, out var rig, out var controller);

            // ---- 对局驱动
            var directorGo = new GameObject("GameDirector");
            directorGo.transform.SetParent(root.transform, false);
            var director = directorGo.AddComponent<GameDirector>();
            director.config = config;
            director.mode = GameMode.SinglePlayer;
            director.localSeat = 0;
            director.tablets = tablets;
            director.characters = characters;

            // ---- 交互与 HUD
            var interactor = rig.AddComponent<TabletInteractor>();
            interactor.director = director;
            interactor.viewCamera = playerCamera;

            // 开局前的观战式主菜单：中央显示真场景（AI 先打一局演示），功能面板贴四角。
            // 单机启动时由它接管开场；联机（命令行或菜单选过）会自动让位给大厅。
            var menuGo = new GameObject("PreGameMenu");
            menuGo.transform.SetParent(root.transform, false);
            var preMenu = menuGo.AddComponent<PreGameMenu>();
            preMenu.director = director;
            preMenu.rig = controller;
            preMenu.interactor = interactor;

            // HUD 走 UGUI：CanvasScaler 按参考分辨率等比缩放，
            // 后面上手机端时不用再改一遍布局（IMGUI 的手算像素矩形做不到这点）。
            var hudGo = new GameObject("Hud");
            hudGo.transform.SetParent(root.transform, false);
            var hud = hudGo.AddComponent<GameHudUgui>();
            hud.director = director;
            hud.interactor = interactor;

            // 音频总管：订阅对局事件播程序化合成的音效。
            // 单独一个物体，避免和 HUD 的生命周期绑在一起。
            var audioGo = new GameObject("AudioDirector");
            audioGo.transform.SetParent(root.transform, false);
            var audio = audioGo.AddComponent<AudioDirector>();
            audio.director = director;

            // 平板特效：把道具效果精确落在被作用的格子上
            var boardVfxGo = new GameObject("BoardVfx");
            boardVfxGo.transform.SetParent(root.transform, false);
            var boardVfx = boardVfxGo.AddComponent<BoardVfx>();
            boardVfx.director = director;

            // ---- 联机组件（默认关闭；需要联机时启用并选择 Host / Client）
            var lanGo = new GameObject("LanNetworkManager");
            lanGo.transform.SetParent(root.transform, false);
            var lan = lanGo.AddComponent<LanNetworkManager>();
            lan.director = director;
            // 场景默认是单机：运行时由 SessionContext 覆盖成 LanHost / LanClient。
            // 绝不能默认写成 LanHost —— 否则大厅界面会在单机对局中一直挂着不消失。
            lan.mode = GameMode.SinglePlayer;
            lan.port = config.defaultPort;
            lan.enabled = false;

            // 联机引导：它必须在所有人 Start 之前跑，才能在需要联机时
            // 把上面这个 disabled 的组件重新启用，并把 GameDirector.mode 改对。
            var netBootGo = new GameObject("NetBootstrap");
            netBootGo.transform.SetParent(root.transform, false);
            netBootGo.AddComponent<NetBootstrap>();

            var discoveryGo = new GameObject("LanRoomDiscovery");
            discoveryGo.transform.SetParent(root.transform, false);
            discoveryGo.AddComponent<LanRoomDiscovery>();

            // ---- 无头自动验证探针（只有带 -btAutoplay 启动参数时才生效，平时自动禁用自己）
            var probeGo = new GameObject("AutoplayProbe");
            probeGo.transform.SetParent(root.transform, false);
            var probe = probeGo.AddComponent<AutoplayHeadlessProbe>();
            probe.director = director;
            // 截图时多等一会儿，让盘面上有足够多翻开的格子可供人工核对
            probe.screenshotDelay = 10f;

            // ---- 房间大厅（联机时显示；房主点开始才开局）
            var lobbyGo = new GameObject("LobbyUI");
            lobbyGo.transform.SetParent(root.transform, false);
            var lobby = lobbyGo.AddComponent<LobbyUI>();
            lobby.lan = lan;
            lobby.director = director;

            // ---- 四人实时语音房（联机时走 TCP 中继，单机回退局域网 UDP；V 键对讲）
            var voiceGo = new GameObject("VoiceChat");
            voiceGo.transform.SetParent(root.transform, false);
            var voice = voiceGo.AddComponent<VoiceChatManager>();
            voice.director = director;
            voice.lan = lan;
            hud.voice = voice;      // HUD 用它在玩家卡片上标记「说话中」

            // ---- 皮肤目录
            director.skinCatalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(
                GameAssetCreator.SkinDir + "/SkinCatalog.asset");

            // 注意：这里**不**在编辑期调用 StartSession。
            // 平板的上百个格子由 TabletScreen 在运行时生成，
            // 编辑期生成的格子会被写进场景，既让场景臃肿又容易造成序列化异常。
            Debug.Log($"[炸弹酒馆] 配置检查 → difficulty={(config.difficulty != null ? config.difficulty.name : "空")} " +
                      $"drift={(config.driftProfile != null ? "有" : "空")} gacha={(config.gacha != null ? "有" : "空")} " +
                      $"ramp={(config.colorRamp != null ? "有" : "空")} sharedBoard={config.sharedBoard}");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, PlayScene);
            RegisterScene(PlayScene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[炸弹酒馆] 可玩场景已生成：{PlayScene}");
        }

        static GameConfig LoadOrCreateConfig()
        {
            const string configPath = GameAssetCreator.ConfigDir + "/GameConfig.asset";

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(configPath);
            if (config == null || NeedsRebuild(config))
            {
                Debug.Log("[炸弹酒馆] 配置缺失或不完整，重新生成默认配置资产。");
                GameAssetCreator.CreateAll();
                config = AssetDatabase.LoadAssetAtPath<GameConfig>(configPath);
            }

            if (config == null)
                Debug.LogError($"[炸弹酒馆] 仍然无法加载 GameConfig，请检查 {configPath}");

            return config;
        }

        /// <summary>
        /// 只要有一条关键引用是空的就整体重建：
        /// 批处理下逐个补引用容易补到"尚未导入"的空对象，反而把引用写坏。
        /// </summary>
        static bool NeedsRebuild(GameConfig c)
        {
            if (c.difficulty == null || c.driftProfile == null || c.colorRamp == null) return true;
            if (c.gacha == null || c.gacha.dropPool == null) return true;
            if (c.gacha.dropPool.items == null || c.gacha.dropPool.items.Count == 0) return true;
            return false;
        }

        /// <summary>
        /// 为纯视觉的酒馆几何体补上碰撞体：地板、四面墙、圆桌、吧台。
        /// 尺寸与 TavernFactory 的摆法对应。
        /// </summary>
        static void CreateCollisionShell(Transform parent)
        {
            var shell = new GameObject("CollisionShell");
            shell.transform.SetParent(parent, false);

            AddBox(shell.transform, "FloorCol", new Vector3(0f, -0.05f, 0f), new Vector3(30f, 0.1f, 30f));

            float half = RoomSize * 0.5f;
            float wh = WallHeight;
            AddBox(shell.transform, "WallN", new Vector3(0f, wh * 0.5f, half), new Vector3(RoomSize, wh, 0.3f));
            AddBox(shell.transform, "WallS", new Vector3(0f, wh * 0.5f, -half), new Vector3(RoomSize, wh, 0.3f));
            AddBox(shell.transform, "WallE", new Vector3(half, wh * 0.5f, 0f), new Vector3(0.3f, wh, RoomSize));
            AddBox(shell.transform, "WallW", new Vector3(-half, wh * 0.5f, 0f), new Vector3(0.3f, wh, RoomSize));

            // 圆桌用一个方盒近似，避免玩家走进桌面
            AddBox(shell.transform, "TableCol", new Vector3(0f, 0.43f, 0f), new Vector3(2.1f, 0.86f, 2.1f));

            // 吧台
            AddBox(shell.transform, "BarCol", new Vector3(-0.6f, 0.66f, -5.15f), new Vector3(6.9f, 1.33f, 1.05f));
        }

        static void AddBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            col.center = Vector3.zero;
        }

        /// <summary>
        /// 在指定座位前面放一块面板（立在桌面上、朝玩家仰起）。
        /// 所有面板在运行时都会指向同一份共用盘面数据。
        /// </summary>
        static TabletScreen CreateSeatPanel(Transform parent, int seatIndex, GameConfig config)
        {
            var angles = SeatAnglesOf(config);
            float ang = angles[seatIndex % angles.Length] * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));

            var pivot = new GameObject($"PanelPivot_{seatIndex}");
            pivot.transform.SetParent(parent, false);
            pivot.transform.position = dir * PanelRadius + Vector3.up * PanelHeight;
            // 屏幕法线朝向坐在外侧的炸弹角色，并向后仰，读起来像讲台上的平板
            pivot.transform.rotation = Quaternion.LookRotation(dir, Vector3.up) *
                                       Quaternion.Euler(PanelTiltDegrees, 0f, 0f);

            var go = new GameObject($"Panel_{seatIndex}");
            go.transform.SetParent(pivot.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            // 支架：顶在面板**下沿**，而不是面板中心的正下方。
            //
            // 面板是倾斜的，立柱若从中心竖直往上顶，就一定会从面板面里穿出来（穿模）；
            // 顶在下沿才是真正「托住」它，从任何角度看都不会穿插。
            float tiltRad = PanelTiltDegrees * Mathf.Deg2Rad;
            float slabHalfZ = (PanelBoardDepth + PanelBezelMargin * 2f) * 0.5f;
            float edgeDrop = slabHalfZ * Mathf.Sin(tiltRad);   // 下沿比中心低多少
            float edgeOut = slabHalfZ * Mathf.Cos(tiltRad);    // 下沿比中心外移多少

            float edgeY = PanelHeight - edgeDrop;
            float postHeight = Mathf.Max(0.04f, edgeY - TableTopY);
            var postPos = dir * (PanelRadius + edgeOut) + Vector3.up * (TableTopY + postHeight * 0.5f);

            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = $"PanelStand_{seatIndex}";
            stand.transform.SetParent(parent, false);
            stand.transform.position = postPos;
            stand.transform.rotation = Quaternion.identity;
            // Cylinder 原始高度为 2，所以 y 缩放取半高
            stand.transform.localScale = new Vector3(0.075f, postHeight * 0.5f, 0.075f);
            stand.GetComponent<Renderer>().sharedMaterial =
                CreateMaterialAsset("PanelStand", new Color(0.22f, 0.18f, 0.12f), 0.4f, 0.5f, false);

            var tablet = go.AddComponent<TabletScreen>();
            // 面板身份：决定「谁的面板可以被指向」，必须在装配期写入
            tablet.panelSeatIndex = seatIndex;

            // 血量灯泡：装在这块面板上沿。做成实体建模是为了让人「坐在自己位置上
            // 一眼看出对面还剩几条命」，不用去读 HUD 文字。
            var lamps = go.AddComponent<HealthLamps>();
            lamps.seatIndex = seatIndex;
            // 灯泡嵌在**上边框**里。上边框是倾斜面板的最高处，永远不会被桌面挡到；
            // 装在下沿则会被桌子埋掉（这正是之前「找不到血量」的原因）。
            lamps.bulbRadius = PanelBezelMargin * 0.44f;   // 直径留一点余量，不超出边框
            lamps.bulbSpacing = PanelBezelMargin * 1.30f;

            // 面板格子小一些，四块才不会在桌子中间打架
            tablet.cellSize = PanelCellSize;
            tablet.cellGap = PanelCellGap;
            tablet.bezelMargin = PanelBezelMargin;
            tablet.lampStripDepth = 0f;      // 灯泡走上边框，不再需要底部灯条

            // 使用工程内的材质资产：运行时 new 出来的材质若被写进场景会导致序列化损坏
            tablet.tileMaterial = CreateMaterialAsset("PanelTile", new Color(0.85f, 0.85f, 0.85f), 0.22f, 0f, true);
            tablet.slabMaterial = CreateMaterialAsset("PanelSlab", new Color(0.11f, 0.11f, 0.14f), 0.45f, 0.35f, false);

            return tablet;
        }

        /// <summary>创建（或复用）一个材质资产，供场景引用而不是把运行时材质写进场景。</summary>
        static Material CreateMaterialAsset(string name, Color color, float smoothness, float metallic, bool emissive)
        {
            const string dir = GameAssetCreator.RootDir + "/Materials";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                if (!AssetDatabase.IsValidFolder(GameAssetCreator.RootDir))
                    AssetDatabase.CreateFolder("Assets/LiarsBar", "Game");
                AssetDatabase.CreateFolder(GameAssetCreator.RootDir, "Materials");
            }

            string path = $"{dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = Shader.Find("Standard");
            mat.color = color;
            mat.SetFloat("_Glossiness", smoothness);
            mat.SetFloat("_Metallic", metallic);

            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                mat.SetColor("_EmissionColor", Color.white);
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Camera CreatePlayerRig(Transform parent, GameConfig config,
            out GameObject rig, out FirstPersonController controller)
        {
            rig = new GameObject("PlayerRig");
            rig.transform.SetParent(parent, false);

            var cc = rig.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.28f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;

            controller = rig.AddComponent<FirstPersonController>();

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
            camGo.tag = "MainCamera";

            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.028f, 0.035f);
            camGo.AddComponent<AudioListener>();

            controller.viewCamera = cam;
            controller.eyeHeight = EyeHeight;
            controller.startPitch = StartPitchDegrees;
            // 玩法要求玩家坐着玩：只能转视角，不能走动
            controller.seatedMode = true;

            // 站在自己座位与桌子之间、面朝桌心那块共用大屏。
            // 站在椅子前方而不是椅子里，椅背就不会挡住视线。
            var buildAngles = SeatAnglesOf(config);
            float ang = buildAngles[0] * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            rig.transform.position = dir * StandRadiusOf(config) + Vector3.up * 0.05f;
            rig.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);

            return cam;
        }

        /// <summary>
        /// 在地板下方补一个不可见的厚盒碰撞体。纯粹是安全网：
        /// 即使玩家被挤穿那张单面 Plane，也依然踩在实地上，不会掉出场景。
        /// </summary>
        static void EnsureFloorCollider(Transform parent)
        {
            const string name = "FloorCollider";

            var existing = parent.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            go.transform.localScale = new Vector3(RoomSize, 0.5f, RoomSize);

            // 只要碰撞体，不要渲染体 —— 它就是一块看不见的地板
            go.AddComponent<BoxCollider>();
        }

        static void RegisterScene(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == path)) return;
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
