using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BombTavern.Config;
using BombTavern.Core;

namespace BombTavern.Net
{
    /// <summary>
    /// 状态同步快照。
    ///
    /// 模型：**主机权威 + 快照下发**。客户端完全不跑模拟，
    /// 主机每接受一条指令就把整份权威状态写给所有客户端，客户端整体覆盖。
    /// 相比确定性锁步，这条路不依赖跨平台位级一致，天然免疫浮点漂移导致的掉线式失步。
    ///
    /// 保密设计（这是快照同步必须处理好的地方）：
    ///   - <see cref="RiskCell.trueRisk"/> **永不上线**，它是判定的唯一真相，留在主机。
    ///     客户端拿到的是 <c>staleness</c>（显示值偏差）这个「公开提示」，
    ///     足以驱动「信息过期抖动」，但推不出真实概率。
    ///   - 私有盘面模式下只下发**收件人自己那块盘面**，别人的盘面一个字节都不发。
    ///   - <c>knownTruth</c>（探查道具查到的私人读数）只下发收件人自己的。
    ///
    /// 载荷用二进制 + Base64 塞进现有一行一消息的文本协议里。
    /// 单次快照约 2KB（120 格 + 4 名玩家），一条指令发一次，回合制下带宽压力可以忽略。
    /// </summary>
    public static class StateSnapshot
    {
        public const byte Version = 2;

        const byte FlagSharedBoard = 1 << 0;

        // 格子状态位
        const byte CellRevealed = 1 << 0;
        const byte CellFlagged = 1 << 1;
        const byte CellSafeZone = 1 << 2;

        /// <summary>
        /// 这一格已经炸过。必须上线：疤痕如果只是本地表现，
        /// 客户端玩家会看不到主机侧踩爆过的格子，同一块盘面两边读到不同历史。
        /// </summary>
        const byte CellBlasted = 1 << 3;

        // ============================================================ 写
        /// <summary>
        /// 生成发给 <paramref name="forSeat"/> 的快照。
        /// forSeat &lt; 0 表示「旁观者」：只发公开信息，没有私有盘面与私人读数。
        /// </summary>
        /// <param name="events">
        /// 本次要顺带下发的事件（主机侧取自上一条快照之后的新事件）。
        /// 事件不走状态覆盖 —— HUD 事件流、横幅、角色爆炸动画都靠它驱动。
        /// 标记为 secret 且不属于收件人的事件会被丢弃，避免泄露道具的目标和作用结果。
        /// 「谁使用了什么道具」由另一条不含目标信息的公开事件同步。
        /// </param>
        public static byte[] Write(GameSession session, int forSeat, IReadOnlyList<GameEvent> events = null)
        {
            if (session == null) return null;

            using (var ms = new MemoryStream(4096))
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                bool shared = session.Config != null && session.Config.sharedBoard;

                w.Write(Version);
                w.Write((byte)(shared ? FlagSharedBoard : 0));

                w.Write(session.RoundIndex);
                w.Write(session.TurnIndex);
                w.Write(session.CurrentSeat);
                w.Write(session.IsOver);
                w.Write((int)session.OverReason);
                w.Write(session.WinnerSeat);

                w.Write(session.HoverSeat);
                w.Write(session.HoverX);
                w.Write(session.HoverY);
                w.Write(session.SharedFreezeTurns);

                WritePlayers(w, session, forSeat);
                WriteBoards(w, session, forSeat, shared);
                WriteEvents(w, events, forSeat);

                w.Flush();
                return ms.ToArray();
            }
        }

        static void WritePlayers(BinaryWriter w, GameSession session, int forSeat)
        {
            var players = session.Players ?? new PlayerState[0];
            w.Write(players.Length);

            for (int i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (p == null)
                {
                    w.Write(-1);
                    continue;
                }

                w.Write(p.seatIndex);
                w.Write(p.displayName ?? string.Empty);
                w.Write(p.isBot);

                w.Write(p.maxLives);
                w.Write(p.lives);
                w.Write(p.eliminated);

                w.Write(p.itemFindChance);
                w.Write(p.pityCounter);
                w.Write(p.totalFindRolls);
                w.Write(p.totalItemsFound);
                w.Write(p.shieldCharges);
                w.Write(p.freezeTurnsRemaining);
                w.Write(p.itemsUsedThisTurn);

                w.Write(p.totalReveals);
                w.Write(p.totalHits);
                w.Write(p.totalProbes);
                w.Write(p.totalModifies);
                w.Write(p.totalFlags);
                w.Write(p.totalItemsUsed);

                // ---- 背包：只发 itemId，客户端用本地目录还原定义
                var inv = p.inventory ?? new List<ItemInstance>();
                w.Write(inv.Count);
                for (int k = 0; k < inv.Count; k++)
                {
                    var def = inv[k].definition;
                    w.Write(def != null ? (def.itemId ?? string.Empty) : string.Empty);
                    w.Write(inv[k].remainingUses);
                }

                // ---- 私人探查读数：只有本人能收到
                if (p.seatIndex == forSeat && p.knownTruth != null)
                {
                    w.Write(p.knownTruth.Length);
                    for (int k = 0; k < p.knownTruth.Length; k++) w.Write(p.knownTruth[k]);
                }
                else
                {
                    w.Write(0);
                }
            }
        }

        static void WriteBoards(BinaryWriter w, GameSession session, int forSeat, bool shared)
        {
            var jobs = new List<RiskBoard>(2);
            var seats = new List<int>(2);

            if (shared)
            {
                // 共用盘面：人人可见，含自己的私人读数（读数在玩家块里单独发）
                if (session.SharedBoard != null) { jobs.Add(session.SharedBoard); seats.Add(-1); }
            }
            else if (forSeat >= 0 && session.Players != null &&
                     forSeat < session.Players.Length && session.Players[forSeat] != null)
            {
                // 私有盘面：只发收件人自己那块
                var mine = session.Players[forSeat].board;
                if (mine != null) { jobs.Add(mine); seats.Add(forSeat); }
            }

            w.Write(jobs.Count);
            for (int i = 0; i < jobs.Count; i++)
            {
                w.Write(seats[i]);
                WriteBoard(w, jobs[i]);
            }
        }

        static void WriteBoard(BinaryWriter w, RiskBoard board)
        {
            w.Write(board.Columns);
            w.Write(board.Rows);

            var cells = board.RawCells;
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];

                byte flags = 0;
                if (c.revealed) flags |= CellRevealed;
                if (c.flagged) flags |= CellFlagged;
                if (c.safeZone) flags |= CellSafeZone;
                if (c.blasted) flags |= CellBlasted;

                w.Write(flags);
                w.Write(c.blastedSeat);
                w.Write(c.displayedRisk);
                w.Write(c.revealedExpectation);
                // 只发「偏差」这个公开提示，不发真实值本身
                w.Write(c.Staleness);
            }
        }

        static void WriteEvents(BinaryWriter w, IReadOnlyList<GameEvent> events, int forSeat)
        {
            int count = events?.Count ?? 0;

            int written = 0;
            for (int i = 0; i < count; i++)
            {
                var e = events[i];
                if (e.secret && e.seatIndex != forSeat) continue;
                written++;
            }

            w.Write(written);
            for (int i = 0; i < count; i++)
            {
                var e = events[i];
                if (e.secret && e.seatIndex != forSeat) continue;

                w.Write((int)e.type);
                w.Write(e.seatIndex);
                w.Write(e.otherSeat);
                w.Write(e.x);
                w.Write(e.y);
                w.Write(e.value);
                w.Write(e.secret);
                w.Write(e.message ?? string.Empty);
            }
        }

        // ============================================================ 读
        /// <summary>
        /// 把快照覆盖到本地会话。返回 false 表示格式不合法（版本/尺寸不匹配）。
        /// <paramref name="outEvents"/> 非空时，会把快照里附带的事件追加进去，
        /// 由调用方再注入给表现层（HUD 事件流 / 角色动画）。
        /// </summary>
        public static bool Apply(GameSession session, byte[] data, ItemCatalog catalog,
            List<GameEvent> outEvents = null)
        {
            if (session == null || data == null || data.Length == 0) return false;

            try
            {
                using (var ms = new MemoryStream(data, false))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (r.ReadByte() != Version) return false;

                    byte flags = r.ReadByte();

                    int round = r.ReadInt32();
                    int turn = r.ReadInt32();
                    int current = r.ReadInt32();
                    bool over = r.ReadBoolean();
                    var reason = (GameOverReason)r.ReadInt32();
                    int winner = r.ReadInt32();

                    int hoverSeat = r.ReadInt32();
                    int hoverX = r.ReadInt32();
                    int hoverY = r.ReadInt32();
                    int freeze = r.ReadInt32();

                    ReadPlayers(r, session, catalog);
                    ReadBoards(r, session);
                    ReadEvents(r, outEvents);

                    session.RestoreAuthoritative(current, turn, round, over, winner, reason,
                        hoverSeat, hoverX, hoverY, freeze);
                    return true;
                }
            }
            catch (Exception)
            {
                // 半包 / 版本不符 / 数据损坏：丢这一帧即可，下一帧会整体覆盖
                return false;
            }
        }

        static void ReadPlayers(BinaryReader r, GameSession session, ItemCatalog catalog)
        {
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int seat = r.ReadInt32();
                if (seat < 0) continue;

                var p = session.PlayerAt(seat);
                if (p == null) { SkipPlayer(r); continue; }

                p.displayName = r.ReadString();
                p.isBot = r.ReadBoolean();

                p.maxLives = r.ReadInt32();
                p.lives = r.ReadInt32();
                p.eliminated = r.ReadBoolean();

                p.itemFindChance = r.ReadSingle();
                p.pityCounter = r.ReadInt32();
                p.totalFindRolls = r.ReadInt32();
                p.totalItemsFound = r.ReadInt32();
                p.shieldCharges = r.ReadInt32();
                p.freezeTurnsRemaining = r.ReadInt32();
                p.itemsUsedThisTurn = r.ReadInt32();

                p.totalReveals = r.ReadInt32();
                p.totalHits = r.ReadInt32();
                p.totalProbes = r.ReadInt32();
                p.totalModifies = r.ReadInt32();
                p.totalFlags = r.ReadInt32();
                p.totalItemsUsed = r.ReadInt32();

                int invCount = r.ReadInt32();
                p.inventory.Clear();
                for (int k = 0; k < invCount; k++)
                {
                    string itemId = r.ReadString();
                    int uses = r.ReadInt32();
                    var def = Resolve(catalog, itemId);
                    if (def != null) p.inventory.Add(new ItemInstance { definition = def, remainingUses = uses });
                }

                int knownLen = r.ReadInt32();
                if (knownLen > 0)
                {
                    if (p.knownTruth == null || p.knownTruth.Length != knownLen)
                        p.knownTruth = new float[knownLen];
                    for (int k = 0; k < knownLen; k++) p.knownTruth[k] = r.ReadSingle();
                }
                else
                {
                    // 别人的私人读数不下发，清空避免残留上一局信息
                    if (p.knownTruth != null) Array.Clear(p.knownTruth, 0, p.knownTruth.Length);
                }
            }
        }

        static void ReadBoards(BinaryReader r, GameSession session)
        {
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int seat = r.ReadInt32();
                int cols = r.ReadInt32();
                int rows = r.ReadInt32();

                RiskBoard board = seat < 0 ? session.SharedBoard :
                    (session.PlayerAt(seat) != null ? session.PlayerAt(seat).board : null);

                int cells = cols * rows;
                if (board == null || board.Columns != cols || board.Rows != rows)
                {
                    SkipCells(r, cells);
                    continue;
                }

                var target = board.RawCells;
                for (int k = 0; k < cells; k++)
                {
                    byte cellFlags = r.ReadByte();
                    byte blastedSeat = r.ReadByte();
                    float displayed = r.ReadSingle();
                    float expectation = r.ReadSingle();
                    float staleness = r.ReadSingle();

                    var c = target[k];
                    c.revealed = (cellFlags & CellRevealed) != 0;
                    c.flagged = (cellFlags & CellFlagged) != 0;
                    c.safeZone = (cellFlags & CellSafeZone) != 0;
                    c.blasted = (cellFlags & CellBlasted) != 0;
                    c.blastedSeat = blastedSeat;
                    c.displayedRisk = displayed;
                    c.revealedExpectation = expectation;
                    // trueRisk 不上线；用「显示值 - 偏差」还原出等价的 Staleness 表现
                    c.trueRisk = displayed - staleness;
                    target[k] = c;
                }
            }
        }

        static void ReadEvents(BinaryReader r, List<GameEvent> outEvents)
        {
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var e = new GameEvent
                {
                    type = (GameEventType)r.ReadInt32(),
                    seatIndex = r.ReadInt32(),
                    otherSeat = r.ReadInt32(),
                    x = r.ReadInt32(),
                    y = r.ReadInt32(),
                    value = r.ReadSingle(),
                    secret = r.ReadBoolean(),
                    message = r.ReadString(),
                };
                outEvents?.Add(e);
            }
        }

        /// <summary>
        /// 跳过一个「本地没有对应 PlayerState」的玩家块。
        /// 字段顺序必须与 WritePlayers 严格一致 —— 少读或多读一个字节，
        /// 后面所有数据都会错位（eliminated 是 bool 只占 1 字节，最容易踩）。
        /// </summary>
        static void SkipPlayer(BinaryReader r)
        {
            r.ReadString();                                  // displayName
            r.ReadBoolean();                                 // isBot
            r.ReadInt32();                                   // maxLives
            r.ReadInt32();                                   // lives
            r.ReadBoolean();                                 // eliminated
            r.ReadSingle();                                  // itemFindChance
            r.ReadInt32();                                   // pityCounter
            r.ReadInt32();                                   // totalFindRolls
            r.ReadInt32();                                   // totalItemsFound
            r.ReadInt32();                                   // shieldCharges
            r.ReadInt32();                                   // freezeTurnsRemaining
            r.ReadInt32();                                   // itemsUsedThisTurn
            for (int i = 0; i < 6; i++) r.ReadInt32();       // 统计

            int inv = r.ReadInt32();
            for (int i = 0; i < inv; i++) { r.ReadString(); r.ReadInt32(); }

            int known = r.ReadInt32();
            for (int i = 0; i < known; i++) r.ReadSingle();
        }

        static void SkipCells(BinaryReader r, int cells)
        {
            for (int i = 0; i < cells; i++)
            {
                r.ReadByte();
                r.ReadByte();
                r.ReadSingle();
                r.ReadSingle();
                r.ReadSingle();
            }
        }

        static ItemDefinition Resolve(ItemCatalog catalog, string itemId)
        {
            if (catalog == null || catalog.items == null || string.IsNullOrEmpty(itemId)) return null;
            for (int i = 0; i < catalog.items.Count; i++)
            {
                var d = catalog.items[i];
                if (d != null && d.itemId == itemId) return d;
            }
            return null;
        }
    }
}
