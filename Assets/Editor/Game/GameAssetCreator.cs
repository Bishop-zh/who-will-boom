using System.Collections.Generic;
using System.IO;
using BombTavern.Config;
using UnityEditor;
using UnityEngine;

namespace BombTavern.EditorTools
{
    /// <summary>
    /// 一键生成全部默认配置资产（对局配置 / 三档难度 / 漂移 / 掉落保底 / 配色 / 道具目录）。
    /// 可由菜单执行，也可由批处理调用： -executeMethod BombTavern.EditorTools.GameAssetCreator.CreateAll
    /// </summary>
    public static class GameAssetCreator
    {
        public const string RootDir = "Assets/LiarsBar/Game";
        public const string ConfigDir = RootDir + "/Config";
        public const string ItemDir = RootDir + "/Items";
        public const string SkinDir = RootDir + "/Skins";

        public static GameConfig ConfigAsset { get; private set; }
        public static ItemCatalog CatalogAsset { get; private set; }

        [MenuItem("工具/炸弹酒馆/1. 生成默认配置资产", false, 10)]
        public static void CreateAll()
        {
            EnsureDirs();
            AssetDatabase.Refresh();

            // ---------- 第一阶段：先把所有资产文件建出来并导入 ----------
            // 自愈：历史版本若把多个类塞进同一个 .cs，会导致 .asset 无法反序列化，
            // 表现为"文件存在但 LoadAssetAtPath 返回 null"。这里检测到就删掉重建。
            PurgeBrokenAsset<ItemCatalog>(ItemDir + "/ItemCatalog.asset");
            PurgeBrokenAsset<GameConfig>(ConfigDir + "/GameConfig.asset");
            PurgeBrokenAsset<GachaConfig>(ConfigDir + "/Gacha_Default.asset");
            PurgeBrokenAsset<RiskColorRamp>(ConfigDir + "/ColorRamp_Risk.asset");
            PurgeBrokenAsset<DriftProfile>(ConfigDir + "/Drift_Default.asset");
            PurgeBrokenAsset<DifficultyPreset>(ConfigDir + "/Difficulty_Intermediate.asset");

            CreateOrLoad<RiskColorRamp>(ConfigDir + "/ColorRamp_Risk.asset");
            CreateOrLoad<DriftProfile>(ConfigDir + "/Drift_Default.asset");
            CreateOrLoad<GachaConfig>(ConfigDir + "/Gacha_Default.asset");

            CreateDifficulty("Difficulty_Beginner", "初级", "9x9，平均风险 12.3%（对齐传统扫雷初级）。", 9, 9, 0.123f, 0.10f);
            CreateDifficulty("Difficulty_Intermediate", "中级", "12x10，平均风险 15.6%（对齐传统扫雷中级）。", 12, 10, 0.156f, 0.12f);
            CreateDifficulty("Difficulty_Expert", "高级", "16x12，平均风险 20.6%（对齐传统扫雷高级）。", 16, 12, 0.206f, 0.15f);

            var catalog = CreateOrLoad<ItemCatalog>(ItemDir + "/ItemCatalog.asset");
            BuildItems(catalog);
            BuildSkins();
            CreateOrLoad<GameConfig>(ConfigDir + "/GameConfig.asset");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ---------- 第二阶段：资产都已导入，此时再建立互相引用 ----------
            // 直接用 LoadOrImport 重新取实例，避免引用到尚未导入的空对象。
            var ramp = LoadOrImport<RiskColorRamp>(ConfigDir + "/ColorRamp_Risk.asset");
            var drift = LoadOrImport<DriftProfile>(ConfigDir + "/Drift_Default.asset");
            var gacha = LoadOrImport<GachaConfig>(ConfigDir + "/Gacha_Default.asset");
            var intermediate = LoadOrImport<DifficultyPreset>(ConfigDir + "/Difficulty_Intermediate.asset");
            var catalogRef = LoadOrImport<ItemCatalog>(ItemDir + "/ItemCatalog.asset");
            var config = LoadOrImport<GameConfig>(ConfigDir + "/GameConfig.asset");

            if (config != null)
            {
                config.difficulty = intermediate;
                config.driftProfile = drift;
                config.gacha = gacha;
                config.colorRamp = ramp;
                EditorUtility.SetDirty(config);
            }
            if (gacha != null)
            {
                gacha.dropPool = catalogRef;
                EditorUtility.SetDirty(gacha);
            }
            if (catalogRef != null) EditorUtility.SetDirty(catalogRef);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ConfigAsset = config;
            CatalogAsset = catalogRef;

            int itemCount = catalogRef != null ? catalogRef.items.Count : 0;
            bool wired = config != null && config.difficulty != null && config.driftProfile != null &&
                         config.gacha != null && config.colorRamp != null &&
                         gacha != null && gacha.dropPool != null;

            if (wired)
                Debug.Log($"[炸弹酒馆] 默认配置已生成并完成接线：{ConfigDir}/GameConfig.asset，道具 {itemCount} 件");
            else
                Debug.LogError("[炸弹酒馆] 配置接线不完整，请再次执行本步骤。");
        }

        /// <summary>删除无法反序列化的坏资产（文件在、但脚本引用解析不出来）。</summary>
        static void PurgeBrokenAsset<T>(string path) where T : Object
        {
            if (!File.Exists(path)) return;
            if (AssetDatabase.LoadAssetAtPath<T>(path) != null) return;

            AssetDatabase.DeleteAsset(path);
            Debug.LogWarning($"[炸弹酒馆] {path} 无法解析（多半是类名与文件名不一致导致），已删除并重建。");
        }

        /// <summary>加载资产；若磁盘上存在但尚未导入，则强制同步导入后重试。</summary>
        static T LoadOrImport<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            if (!File.Exists(path)) return null;

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        /// <summary>诊断引用链：Refresh 前后各读一次，确认是否是"资产尚未导入"导致的空引用。</summary>
        public static void DiagnoseChain()
        {
            const string gachaPath = ConfigDir + "/Gacha_Default.asset";
            const string cfgPath = ConfigDir + "/GameConfig.asset";
            const string catPath = ItemDir + "/ItemCatalog.asset";

            DumpChain("Refresh 之前");

            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(gachaPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(cfgPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(catPath, ImportAssetOptions.ForceSynchronousImport);

            DumpChain("Refresh 之后");
            EditorApplication.Exit(0);
        }

        static void DumpChain(string phase)
        {
            var cat = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemDir + "/ItemCatalog.asset");
            var gacha = AssetDatabase.LoadAssetAtPath<GachaConfig>(ConfigDir + "/Gacha_Default.asset");
            var cfg = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigDir + "/GameConfig.asset");

            Debug.Log($"[链路诊断/{phase}] " +
                      $"catalog={(cat != null ? cat.items.Count + "件" : "空")}  " +
                      $"gacha={(gacha != null)}  gacha.dropPool={(gacha != null && gacha.dropPool != null ? "有" : "空")}  " +
                      $"config={(cfg != null)}  config.gacha={(cfg != null && cfg.gacha != null ? "有" : "空")}  " +
                      $"config.gacha.dropPool={(cfg != null && cfg.gacha != null && cfg.gacha.dropPool != null ? "有" : "空")}");
        }

        /// <summary>诊断用：确认资产是否被正确导入、引用是否可解析。</summary>
        public static void Diagnose()
        {
            string diffPath = ConfigDir + "/Difficulty_Intermediate.asset";
            string cfgPath = ConfigDir + "/GameConfig.asset";

            Debug.Log($"[诊断] 文件存在={File.Exists(diffPath)}  路径存在={AssetDatabase.AssetPathToGUID(diffPath) != ""}");
            Debug.Log($"[诊断] AssetPathToGUID(GameConfig)={AssetDatabase.AssetPathToGUID(cfgPath)}");
            Debug.Log($"[诊断] 主资产类型={AssetDatabase.GetMainAssetTypeAtPath(diffPath)?.FullName ?? "null"}");
            Debug.Log($"[诊断] LoadAssetAtPath<DifficultyPreset>={(AssetDatabase.LoadAssetAtPath<DifficultyPreset>(diffPath) != null)}");
            Debug.Log($"[诊断] FindAssets(t:DifficultyPreset)={AssetDatabase.FindAssets("t:DifficultyPreset").Length}");

            var cfg = AssetDatabase.LoadAssetAtPath<GameConfig>(cfgPath);
            Debug.Log($"[诊断] GameConfig 加载={(cfg != null)}  difficulty={(cfg != null && cfg.difficulty != null)}  ramp={(cfg != null && cfg.colorRamp != null)}");
            Debug.Log($"[诊断] LoadAllAssetsAtPath 数量={AssetDatabase.LoadAllAssetsAtPath(diffPath).Length}");
        }

        // ============================================================ 皮肤
        /// <summary>
        /// 四名固定角色（每局各一名，随机分配到四个座位）：
        /// 黑色球形炸弹 / 红色成捆炸弹 / 棕色二踢脚 / 灰色炮弹。
        /// 手套色同时作为该玩家在 HUD 与格子印记上的标识色。
        /// </summary>
        static readonly string[] FixedSkinIds =
        {
            "black_sphere", "red_bundle", "brown_cracker", "gray_shell",
        };

        static SkinCatalog BuildSkins()
        {
            if (!AssetDatabase.IsValidFolder(SkinDir))
            {
                if (!AssetDatabase.IsValidFolder(RootDir)) AssetDatabase.CreateFolder("Assets/LiarsBar", "Game");
                AssetDatabase.CreateFolder(RootDir, "Skins");
            }

            var catalog = CreateOrLoad<SkinCatalog>(SkinDir + "/SkinCatalog.asset");

            var skins = new List<SkinDefinition>
            {
                // 1) 黑色球形炸弹 —— 标识色：红
                MakeSkin("black_sphere", "黑色球形炸弹", BombBodyType.RoundBomb,
                    new Color(0.13f, 0.13f, 0.16f), new Color(0.22f, 0.22f, 0.26f),
                    new Color(0.62f, 0.62f, 0.66f), new Color(0.88f, 0.26f, 0.25f),
                    BombEmblemShape.Disc, new Color(0.90f, 0.30f, 0.28f),
                    BombHatType.None, new Color(0.3f, 0.3f, 0.36f), grumpy: false),

                // 2) 红色成捆炸弹 —— 标识色：黄
                MakeSkin("red_bundle", "红色成捆炸弹", BombBodyType.DynamiteBundle,
                    new Color(0.72f, 0.16f, 0.14f), new Color(0.55f, 0.11f, 0.10f),
                    new Color(0.24f, 0.18f, 0.12f), new Color(0.95f, 0.76f, 0.22f),
                    BombEmblemShape.Cross, new Color(0.98f, 0.82f, 0.30f),
                    BombHatType.None, new Color(0.3f, 0.3f, 0.36f), grumpy: true),

                // 3) 棕色二踢脚 —— 标识色：青
                MakeSkin("brown_cracker", "棕色二踢脚", BombBodyType.DoubleCracker,
                    new Color(0.44f, 0.28f, 0.16f), new Color(0.32f, 0.20f, 0.12f),
                    new Color(0.80f, 0.66f, 0.34f), new Color(0.25f, 0.75f, 0.66f),
                    BombEmblemShape.Ring, new Color(0.30f, 0.82f, 0.72f),
                    BombHatType.None, new Color(0.3f, 0.3f, 0.36f), grumpy: false),

                // 4) 灰色炮弹 —— 标识色：蓝
                MakeSkin("gray_shell", "灰色炮弹", BombBodyType.ArtilleryShell,
                    new Color(0.46f, 0.47f, 0.50f), new Color(0.33f, 0.34f, 0.37f),
                    new Color(0.70f, 0.62f, 0.30f), new Color(0.36f, 0.55f, 0.92f),
                    BombEmblemShape.Disc, new Color(0.42f, 0.62f, 0.98f),
                    BombHatType.None, new Color(0.3f, 0.3f, 0.36f), grumpy: true,
                    bodySmoothness: 0.62f, bodyMetallic: 0.70f),
            };

            catalog.skins = skins;
            catalog.defaultSkin = skins[0];
            EditorUtility.SetDirty(catalog);

            RemoveStaleSkins();
            return catalog;
        }

        /// <summary>删掉不在固定四人组里的旧皮肤资产，避免目录里残留历史角色。</summary>
        static void RemoveStaleSkins()
        {
            var keep = new HashSet<string>(FixedSkinIds);
            var guids = AssetDatabase.FindAssets("t:SkinDefinition", new[] { SkinDir });

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<SkinDefinition>(path);
                if (def == null) continue;
                if (keep.Contains(def.skinId)) continue;

                AssetDatabase.DeleteAsset(path);
                Debug.Log($"[炸弹酒馆] 移除旧皮肤资产 {path}");
            }
        }

        static SkinDefinition MakeSkin(string id, string name, BombBodyType body,
            Color bodyA, Color bodyB, Color band, Color glove,
            BombEmblemShape emblem, Color emblemColor, BombHatType hat, Color hatColor,
            bool grumpy, float bodySmoothness = 0.45f, float bodyMetallic = 0.25f)
        {
            var skin = CreateOrLoad<SkinDefinition>($"{SkinDir}/Skin_{id}.asset");
            skin.skinId = id;
            skin.displayName = name;
            skin.bodyType = body;
            skin.bodyColor = bodyA;
            skin.bodyColorB = bodyB;
            skin.bandColor = band;
            skin.gloveColor = glove;
            skin.bootColor = new Color(0.13f, 0.13f, 0.16f);
            skin.fuseColor = new Color(0.54f, 0.42f, 0.29f);
            skin.emblem = emblem;
            skin.emblemColor = emblemColor;
            skin.hat = hat;
            skin.hatColor = hatColor;
            skin.grumpy = grumpy;
            skin.eyeScale = 1f;
            skin.pupilColor = new Color(0.08f, 0.08f, 0.10f);
            skin.bodySmoothness = bodySmoothness;
            skin.bodyMetallic = bodyMetallic;
            EditorUtility.SetDirty(skin);
            return skin;
        }

        static void EnsureDirs()
        {
            MakeFolder("Assets/LiarsBar");
            MakeFolder(RootDir);
            MakeFolder(ConfigDir);
            MakeFolder(ItemDir);
            MakeFolder(SkinDir);
        }

        static void MakeFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var asset = LoadOrImport<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static DifficultyPreset CreateDifficulty(string fileName, string display, string desc,
            int cols, int rows, float baseRisk, float variance)
        {
            var preset = CreateOrLoad<DifficultyPreset>($"{ConfigDir}/{fileName}.asset");
            preset.displayName = display;
            preset.description = desc;
            preset.columns = cols;
            preset.rows = rows;
            preset.baseRisk = baseRisk;
            preset.riskVariance = variance;
            preset.clusterDensity = 0.06f;
            preset.clusterBoost = 2.6f;
            preset.clusterRadius = 3.5f;
            preset.guaranteedSafeRatio = 0.08f;
            preset.safeRiskCeiling = 0.02f;
            preset.revealAllAtStart = true;
            preset.startRevealedCount = 0;
            preset.neighborNumbersAreLive = false;
            EditorUtility.SetDirty(preset);
            return preset;
        }

        // ============================================================ 道具
        static ItemDefinition MakeItem(string id, string name, string desc, ItemRarity rarity,
            ItemEffectType effect, bool requiresTarget, ItemTargetScope scope,
            float riskDelta, int radius, int duration, int shieldCharges,
            bool refreshDisplay, float weight, bool starting)
        {
            var def = CreateOrLoad<ItemDefinition>($"{ItemDir}/Item_{id}.asset");
            def.itemId = id;
            def.displayName = name;
            def.description = desc;
            def.rarity = rarity;
            def.effect = effect;
            def.requiresTarget = requiresTarget;
            def.targetScope = scope;
            def.riskDelta = riskDelta;
            def.radius = radius;
            def.durationTurns = duration;
            def.shieldCharges = shieldCharges;
            def.refreshDisplayAfterModify = refreshDisplay;
            def.dropWeight = weight;
            def.canBeStartingItem = starting;
            def.enabled = true;                       // 新建/重建时一律启用
            def.category = CategoryOf(effect);
            EditorUtility.SetDirty(def);
            return def;
        }

        /// <summary>按效果类型归类，方便在目录里按类别筛选与批量启停。</summary>
        static string CategoryOf(ItemEffectType effect)
        {
            switch (effect)
            {
                case ItemEffectType.ProbeCell:
                case ItemEffectType.ScanRow:
                case ItemEffectType.ScanArea:
                case ItemEffectType.RevealNeighbors:
                    return "情报";
                case ItemEffectType.IncreaseRisk:
                case ItemEffectType.DecreaseRisk:
                    return "扰动";
                case ItemEffectType.Shield:
                case ItemEffectType.FreezeDrift:
                    return "防护";
                case ItemEffectType.SkipTurn:
                    return "节奏";
                default:
                    return "其他";
            }
        }

        static void BuildItems(ItemCatalog catalog)
        {
            var items = new List<ItemDefinition>
            {
                MakeItem("probe", "探查器", "查看目标格的真实爆炸概率，把显示值刷新为当前真相。",
                    ItemRarity.Common, ItemEffectType.ProbeCell, true, ItemTargetScope.SelfBoard,
                    0f, 0, 0, 1, true, 34f, true),

                MakeItem("row_scan", "行扫描仪", "刷新该行全部格子的显示概率。",
                    ItemRarity.Common, ItemEffectType.ScanRow, true, ItemTargetScope.SelfBoard,
                    0f, 0, 0, 1, true, 18f, true),

                MakeItem("coolant", "冷却剂", "降低目标格 25% 的真实爆炸概率，并同步显示值。",
                    ItemRarity.Common, ItemEffectType.DecreaseRisk, true, ItemTargetScope.SelfBoard,
                    0.25f, 0, 0, 1, true, 26f, true),

                MakeItem("peek", "邻域透视", "刷新目标格周围 8 格的显示概率。",
                    ItemRarity.Rare, ItemEffectType.RevealNeighbors, true, ItemTargetScope.SelfBoard,
                    0f, 1, 0, 1, true, 20f, true),

                MakeItem("area_scan", "区域扫描", "刷新以目标格为中心、半径 2 格内所有格子的显示概率。",
                    ItemRarity.Rare, ItemEffectType.ScanArea, true, ItemTargetScope.SelfBoard,
                    0f, 2, 0, 1, true, 14f, true),

                MakeItem("shield", "防爆护盾", "抵挡一次爆炸，不损失机会。",
                    ItemRarity.Rare, ItemEffectType.Shield, false, ItemTargetScope.SelfBoard,
                    0f, 0, 0, 1, true, 16f, true),

                MakeItem("cryo", "时间冻结", "接下来 2 个回合内，你的盘面概率不再漂移。",
                    ItemRarity.Epic, ItemEffectType.FreezeDrift, false, ItemTargetScope.SelfBoard,
                    0f, 0, 2, 1, true, 9f, false),

                MakeItem("sabotage", "栽赃器", "暗中提高对手某个格子 30% 的真实爆炸概率，且不刷新他的显示值。",
                    ItemRarity.Rare, ItemEffectType.IncreaseRisk, true, ItemTargetScope.OpponentBoard,
                    0.30f, 0, 0, 1, false, 15f, false),

                MakeItem("disarm", "拆弹钳", "大幅降低目标格 50% 的真实爆炸概率。",
                    ItemRarity.Epic, ItemEffectType.DecreaseRisk, true, ItemTargetScope.SelfBoard,
                    0.50f, 0, 0, 1, true, 7f, false),

                MakeItem("skip_order", "换手令", "让紧接在你之后行动的那名对手跳过一回合。",
                    ItemRarity.Legendary, ItemEffectType.SkipTurn, false, ItemTargetScope.OpponentBoard,
                    0f, 0, 0, 1, true, 5f, false),
            };

            catalog.items = items;
            catalog.startingPool = items.FindAll(i => i.canBeStartingItem);
            // 目录换了一批条目，缓存（可掉落 / 发牌池 / id 索引）必须作废
            catalog.InvalidateCache();
            EditorUtility.SetDirty(catalog);
        }
    }
}
