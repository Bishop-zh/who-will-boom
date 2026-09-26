using System;
using System.Globalization;
using System.Text;

namespace BombTavern.Net
{
    /// <summary>局域网消息类型。</summary>
    public enum NetMessageType
    {
        /// <summary>客户端 → 主机：我来了。</summary>
        Hello = 0,
        /// <summary>主机 → 客户端：分配座位（大厅阶段，此时尚未开局）。</summary>
        Roster = 1,
        /// <summary>主机 → 全部：正式开局，携带对局种子与玩家数。收到后所有端开始同一局模拟。</summary>
        Welcome = 2,
        /// <summary>主机 → 客户端：补发历史指令（客户端重连时用）。</summary>
        SyncCommand = 3,
        /// <summary>双向：一条玩家指令。</summary>
        Command = 4,
        /// <summary>主机 → 客户端：提示信息。</summary>
        Notice = 5,
        /// <summary>主机 → 全体：完整座位表（昵称 + 皮肤），多条用 ';' 分隔。</summary>
        RosterAll = 6,
        /// <summary>
        /// 主机 → 客户端：权威状态快照（Base64 二进制）。
        /// 状态同步模式下的唯一状态来源，客户端收到后整体覆盖本地。
        /// </summary>
        Snapshot = 7,
        /// <summary>
        /// 主机 → 客户端：公开悬停位置的轻量更新。
        /// 鼠标一动就推一份 2KB 的完整快照太浪费，悬停单独走这条小消息。
        /// </summary>
        Hover = 8,
        /// <summary>
        /// 双向语音包。客户端 → 主机，主机再中继给其余客户端。
        /// 走 TCP 是为了让公网/内网穿透也能用语音 —— 隧道通常只转发 TCP，
        /// 语音跟游戏同一条连接就不需要额外开 UDP 端口。
        /// </summary>
        Voice = 9,
    }

    /// <summary>
    /// 文本行协议：每条消息一行，格式 "类型|载荷"，UTF-8 + \n 分隔。
    /// 纯文本便于抓包排查；指令量极小，无带宽压力。
    /// </summary>
    public static class NetProtocol
    {
        public const char Sep = '|';

        public static string Encode(NetMessageType type, string payload)
            => ((int)type).ToString(CultureInfo.InvariantCulture) + Sep + (payload ?? string.Empty);

        public static bool TryDecode(string line, out NetMessageType type, out string payload)
        {
            type = NetMessageType.Notice;
            payload = string.Empty;
            if (string.IsNullOrEmpty(line)) return false;

            int idx = line.IndexOf(Sep);
            if (idx < 0) return false;
            if (!int.TryParse(line.Substring(0, idx), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t))
                return false;

            type = (NetMessageType)t;
            payload = line.Substring(idx + 1);
            return true;
        }

        // ---------------- Roster：yourSeat|hostSeat|playerCount|humanCount
        public static string EncodeRoster(int yourSeat, int hostSeat, int playerCount, int humanCount)
            => string.Join(Sep.ToString(),
                yourSeat.ToString(CultureInfo.InvariantCulture),
                hostSeat.ToString(CultureInfo.InvariantCulture),
                playerCount.ToString(CultureInfo.InvariantCulture),
                humanCount.ToString(CultureInfo.InvariantCulture));

        public static bool TryDecodeRoster(string payload, out int yourSeat, out int hostSeat,
            out int playerCount, out int humanCount)
        {
            yourSeat = 0; hostSeat = 0; playerCount = 0; humanCount = 0;
            if (string.IsNullOrEmpty(payload)) return false;
            var p = payload.Split(Sep);
            if (p.Length < 4) return false;
            return int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out yourSeat)
                && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostSeat)
                && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out playerCount)
                && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out humanCount);
        }

        // ---------------- Welcome：seed|hostSeat|playerCount|humanCount
        public static string EncodeWelcome(ulong seed, int hostSeat, int playerCount, int humanCount)
            => string.Join(Sep.ToString(),
                seed.ToString(CultureInfo.InvariantCulture),
                hostSeat.ToString(CultureInfo.InvariantCulture),
                playerCount.ToString(CultureInfo.InvariantCulture),
                humanCount.ToString(CultureInfo.InvariantCulture));

        public static bool TryDecodeWelcome(string payload, out ulong seed, out int hostSeat,
            out int playerCount, out int humanCount)
        {
            seed = 0; hostSeat = 0; playerCount = 0; humanCount = 0;
            if (string.IsNullOrEmpty(payload)) return false;
            var p = payload.Split(Sep);
            if (p.Length < 4) return false;
            return ulong.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)
                && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostSeat)
                && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out playerCount)
                && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out humanCount);
        }

        // ---------------- Snapshot：二进制快照 → Base64 文本行
        // 复用现有一行一消息的文本协议，不再另开一套二进制分帧，
        // 好处是抓包依旧可读、断线处理逻辑完全复用。
        public static string EncodeSnapshot(byte[] data)
            => data == null ? string.Empty : Convert.ToBase64String(data);

        public static bool TryDecodeSnapshot(string payload, out byte[] data)
        {
            data = null;
            if (string.IsNullOrEmpty(payload)) return false;
            try
            {
                data = Convert.FromBase64String(payload);
                return data.Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // ---------------- Hover：seat|x|y
        public static string EncodeHover(int seat, int x, int y)
            => string.Join(Sep.ToString(),
                seat.ToString(CultureInfo.InvariantCulture),
                x.ToString(CultureInfo.InvariantCulture),
                y.ToString(CultureInfo.InvariantCulture));

        public static bool TryDecodeHover(string payload, out int seat, out int x, out int y)
        {
            seat = -1; x = -1; y = -1;
            if (string.IsNullOrEmpty(payload)) return false;
            var p = payload.Split(Sep);
            if (p.Length < 3) return false;
            return int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out seat)
                && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
                && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
        }

        // ---------------- Voice：seat|base64(16 位小端 PCM)
        public static string EncodeVoice(int seat, byte[] pcm)
            => seat.ToString(CultureInfo.InvariantCulture) + Sep + Convert.ToBase64String(pcm);

        public static bool TryDecodeVoice(string payload, out int seat, out byte[] pcm)
        {
            seat = -1; pcm = null;
            if (string.IsNullOrEmpty(payload)) return false;

            int idx = payload.IndexOf(Sep);
            if (idx <= 0) return false;
            if (!int.TryParse(payload.Substring(0, idx), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out seat)) return false;

            try { pcm = Convert.FromBase64String(payload.Substring(idx + 1)); }
            catch (FormatException) { return false; }

            return pcm != null && pcm.Length >= 2;
        }

        public static string IntList(int[] values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
