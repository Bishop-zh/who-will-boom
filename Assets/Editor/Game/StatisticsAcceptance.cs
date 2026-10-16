using System;
using System.Collections.Generic;
using System.IO;
using BombTavern.Config;
using BombTavern.Core;
using BombTavern.Runtime;
using UnityEngine;

namespace BombTavern.EditorTools
{
    public static class StatisticsAcceptance
    {
        [Serializable] class Report { public List<string> passed = new List<string>(); }
        static Report report;
        static void Check(bool condition, string name) { if (!condition) throw new Exception("战绩验收失败：" + name); report.passed.Add(name); }
        static MatchRecord Record(string id, bool win, GameMode mode = GameMode.SinglePlayer) => new MatchRecord { id = id, playerName = "验收玩家", won = win, mode = mode, endedUtcTicks = DateTime.UtcNow.Ticks, rounds = 1 };
        public static void Run()
        {
            report = new Report(); string directory = Path.GetFullPath("Build/StatisticsCoreTests/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "records.json"); var store = new MatchStatisticsStore(path);
            Check(store.Data.total == 0 && store.Data.WinRate == 0, "空记录");
            Check(store.Record(Record("a", true)) && store.Record(Record("b", true, GameMode.LanHost)) && store.Record(Record("c", false, GameMode.LanClient)), "三种模式保存");
            Check(store.Data.total == 3 && store.Data.wins == 2 && store.Data.single == 1 && store.Data.multiplayer == 2 && store.Data.bestStreak == 2 && store.Data.streak == 0, "总计、胜率、连胜与模式分布");
            Check(!store.Record(Record("c", true)) && store.Data.total == 3, "重复结算去重");
            Check(new MatchStatisticsStore(path).Data.total == 3, "重开存档保留");
            File.WriteAllText(path, "broken json"); store.Reload();
            Check(store.Data.total == 2 && store.Error.Contains("备份"), "损坏主记录恢复备份");
            Check(store.Record(Record("recovered", false)), "备份恢复后继续保存");
            File.WriteAllText(path, "broken again"); store.Reload(); Check(store.Data.total == 2, "备份恢复后保留有效后备");
            File.WriteAllText(path + ".bak", "broken json"); store.Reload(); string before = File.ReadAllText(path);
            Check(!store.Record(Record("d", true)) && before == File.ReadAllText(path), "主备份均损坏时保留原文件");
            var invalid = Record("bad", true); invalid.endedUtcTicks = -1; Check(!store.Record(invalid), "非法时间拒绝");
            path = Path.Combine(directory, "bounded.json");
            var bounded = new MatchStatisticsData { total = 1000, single = 1000 };
            for (int i = 0; i < 1000; i++) bounded.recent.Add(Record("old" + i, false));
            File.WriteAllText(path, JsonUtility.ToJson(bounded)); var capped = new MatchStatisticsStore(path); capped.Record(Record("new", true));
            Check(capped.Data.total == 1001 && capped.Data.recent.Count == 1000 && capped.Data.recent[0].id == "new", "详情上限不裁剪累计总数");
            var trackerStore = new MatchStatisticsStore(Path.Combine(directory, "tracker.json")); var tracker = new MatchStatisticsTracker();
            var config = ScriptableObject.CreateInstance<GameConfig>(); var session = new GameSession(); session.Initialize(config, 42, 1);
            tracker.Observe(session, false, GameMode.SinglePlayer, 0, trackerStore);
            session.RestoreAuthoritative(0, 1, 0, true, 0, GameOverReason.LastPlayerStanding, -1, -1, -1, 0);
            tracker.Observe(session, false, GameMode.SinglePlayer, 0, trackerStore);
            Check(trackerStore.Data.total == 0, "菜单演示局排除");
            tracker.Observe(session, true, GameMode.SinglePlayer, 0, trackerStore); Check(trackerStore.Data.total == 0, "晚到结束快照不计战绩");
            var played = new GameSession(); played.Initialize(config, 42, 1); tracker.Observe(played, true, GameMode.SinglePlayer, 0, trackerStore);
            played.RestoreAuthoritative(0, 1, 0, true, 0, GameOverReason.LastPlayerStanding, -1, -1, -1, 0);
            tracker.Observe(played, true, GameMode.SinglePlayer, 0, trackerStore); tracker.Observe(played, true, GameMode.SinglePlayer, 0, trackerStore);
            Check(trackerStore.Data.total == 1 && trackerStore.Data.wins == 1, "参与后结算一次");
            var restarted = new GameSession(); restarted.Initialize(config, 42, 1); tracker.Observe(restarted, true, GameMode.LanHost, 0, trackerStore);
            restarted.RestoreAuthoritative(0, 1, 0, true, 1, GameOverReason.LastPlayerStanding, -1, -1, -1, 0);
            tracker.Observe(restarted, true, GameMode.LanHost, 0, trackerStore); Check(trackerStore.Data.total == 2 && trackerStore.Data.multiplayer == 1, "相同种子新局单独统计");
            var client=new GameSession(); client.Initialize(config,43,4); tracker.Observe(client,true,GameMode.LanClient,2,trackerStore);
            client.RestoreAuthoritative(0,12,3,true,2,GameOverReason.LastPlayerStanding,-1,-1,-1,0);
            tracker.Observe(client,true,GameMode.LanClient,2,trackerStore); tracker.Observe(client,true,GameMode.LanClient,2,trackerStore);
            Check(trackerStore.Data.total==3 && trackerStore.Data.multiplayer==2 && trackerStore.Data.recent[0].mode==GameMode.LanClient,"客户端结束快照及重复到达");
            var bot=new GameSession(); bot.Initialize(config,44,1); tracker.Observe(bot,true,GameMode.SinglePlayer,1,trackerStore);
            bot.RestoreAuthoritative(0,1,0,true,1,GameOverReason.LastPlayerStanding,-1,-1,-1,0); tracker.Observe(bot,true,GameMode.SinglePlayer,1,trackerStore);
            Check(trackerStore.Data.total==3,"AI座位不计入本机战绩");
            foreach(var corrupt in new[]{"null","time","mode"})
            {
                string corruptPath=Path.Combine(directory,"corrupt-"+corrupt+".json"); var bad=new MatchStatisticsData{total=1,single=1}; var item=Record("bad",false);
                if(corrupt=="time") item.endedUtcTicks=-1; if(corrupt=="mode") item.mode=(GameMode)999;
                bad.recent.Add(corrupt=="null"?null:item); File.WriteAllText(corruptPath,JsonUtility.ToJson(bad)); var damaged=new MatchStatisticsStore(corruptPath);
                Check(damaged.Error.StartsWith("无法读取") && !damaged.Record(Record("new",true)),"损坏详情拒绝："+corrupt);
            }
            foreach(var pathPrefab in Directory.GetFiles("Assets/Resources/BombTavern/Prefabs/UI","*.prefab"))
            {
                var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(pathPrefab);
                Check(!Array.Exists(prefab.GetComponentsInChildren<Transform>(true),t=>Array.Exists(t.GetComponents<Component>(),c=>c==null)),"预制体无缺失组件："+Path.GetFileName(pathPrefab));
            }
            UnityEngine.Object.DestroyImmediate(config);
            var rulesRoot = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/BombTavern/Prefabs/UI/GameSetupRules.prefab").GetComponent<RectTransform>();
            Check(rulesRoot.anchorMin == Vector2.zero && rulesRoot.anchorMax == Vector2.one && rulesRoot.sizeDelta == Vector2.zero, "对局规则遮罩根节点铺满父画布");
            File.WriteAllText("Build/statistics-core-tests.json", JsonUtility.ToJson(report, true)); Debug.Log("[战绩验收] 核心检查通过 " + report.passed.Count);
        }
    }
}
