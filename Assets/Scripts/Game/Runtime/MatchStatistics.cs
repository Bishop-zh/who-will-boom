using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using BombTavern.Core;

namespace BombTavern.Runtime
{
    [Serializable] public sealed class MatchRecord
    {
        public string id, playerName;
        public long endedUtcTicks;
        public GameMode mode;
        public bool won;
        public int reveals, hits, itemsUsed, rounds;
    }

    [Serializable] public sealed class MatchStatisticsData
    {
        public int version = 1, total, wins, single, multiplayer, streak, bestStreak;
        public List<MatchRecord> recent = new List<MatchRecord>();
        public float WinRate => total > 0 ? (float)wins / total : 0;
    }

    /// <summary>本机完成对局历史，原子保存；总计不限历史条数，近期详情保留1000局。</summary>
    public sealed class MatchStatisticsStore
    {
        public const int HistoryLimit = 1000;
        public static readonly MatchStatisticsStore Local = new MatchStatisticsStore(
            LocalPath());
        static string LocalPath()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-btStatsAcceptance");
            return Path.Combine(index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : Application.persistentDataPath, "match-statistics-v1.json");
        }
        readonly string path;
        bool recovered;
        public MatchStatisticsData Data { get; private set; } = new MatchStatisticsData();
        public string Error { get; private set; } = "";
        public MatchStatisticsStore(string path) { this.path = path; Reload(); }

        public void Reload()
        {
            Error = "";
            recovered = false;
            if (!File.Exists(path)) { Data = new MatchStatisticsData(); return; }
            try { Data = Read(path); }
            catch (Exception error)
            {
                try { Data = Read(path + ".bak"); recovered = true; Error = "主记录损坏，已读取备份。"; }
                catch { Error = "无法读取战绩记录：" + error.Message; }
            }
        }
        static MatchStatisticsData Read(string file)
        {
            var data = JsonUtility.FromJson<MatchStatisticsData>(File.ReadAllText(file));
            if (data == null || data.version != 1 || data.recent == null || data.total < 0 || data.wins < 0 || data.wins > data.total || data.single < 0 || data.multiplayer < 0 || data.single + data.multiplayer != data.total)
                throw new InvalidDataException("战绩格式不合法");
            if (data.streak < 0 || data.bestStreak < data.streak || data.bestStreak > data.wins || data.recent.Count > HistoryLimit || data.recent.Count > data.total || data.recent.Exists(r => !Valid(r)))
                throw new InvalidDataException("战绩详情不合法");
            var ids = new HashSet<string>();
            foreach (var record in data.recent) if (!ids.Add(record.id)) throw new InvalidDataException("战绩记录重复");
            return data;
        }
        static bool Valid(MatchRecord record) => record != null && !string.IsNullOrEmpty(record.id) &&
            record.endedUtcTicks > DateTime.MinValue.Ticks && record.endedUtcTicks <= DateTime.MaxValue.Ticks &&
            Enum.IsDefined(typeof(GameMode), record.mode) && record.reveals >= 0 && record.hits >= 0 && record.itemsUsed >= 0 && record.rounds > 0;
        public bool Record(MatchRecord record)
        {
            if (!Valid(record)) return false;
            if (Data.recent.Exists(r => r.id == record.id)) return false;
            if (Error.StartsWith("无法读取")) return false; // 保留损坏文件，避免空数据覆盖真实历史。
            var next = JsonUtility.FromJson<MatchStatisticsData>(JsonUtility.ToJson(Data));
            next.total++;
            if (record.won) { next.wins++; next.streak++; next.bestStreak = Math.Max(next.bestStreak, next.streak); }
            else next.streak = 0;
            if (record.mode == GameMode.SinglePlayer) next.single++; else next.multiplayer++;
            next.recent.Insert(0, record);
            if (next.recent.Count > HistoryLimit) next.recent.RemoveRange(HistoryLimit, next.recent.Count - HistoryLimit);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(next, true));
                if (File.Exists(path)) File.Replace(temporary, path, recovered ? null : path + ".bak");
                else File.Move(temporary, path);
                Data = next; recovered = false; Error = ""; return true;
            }
            catch (Exception error) { Error = "战绩保存失败：" + error.Message; Debug.LogWarning(Error); return false; }
        }
    }

    /// <summary>同一对局只结算一次；只记录曾参与进行中对局的本地真人，排除菜单AI演示。</summary>
    public sealed class MatchStatisticsTracker
    {
        GameSession observed;
        bool participated, recorded;
        string id;
        MatchRecord pending;
        double nextAttempt;
        public void Observe(GameSession session, bool eligible, GameMode mode, int seat, MatchStatisticsStore store)
        {
            if (!ReferenceEquals(observed, session))
            { observed = session; participated = recorded = false; pending = null; nextAttempt = 0; id = Guid.NewGuid().ToString("N"); }
            if (session == null || !eligible || recorded) return;
            var player = session.PlayerAt(seat);
            if (player == null || player.isBot) return;
            if (!session.IsOver) { participated = true; return; }
            if (!participated) return; // 仅收到已结束快照/晚入场观战不计成绩。
            if (pending == null) pending = new MatchRecord { id = id, playerName = player.displayName,
                endedUtcTicks = DateTime.UtcNow.Ticks, mode = mode, won = session.WinnerSeat == seat,
                reveals = player.totalReveals, hits = player.totalHits, itemsUsed = player.totalItemsUsed, rounds = session.RoundIndex + 1 };
            if (Time.realtimeSinceStartupAsDouble < nextAttempt) return;
            recorded = store.Record(pending) || store.Data.recent.Exists(r => r.id == id);
            nextAttempt = Time.realtimeSinceStartupAsDouble + 5;
        }
    }
}
