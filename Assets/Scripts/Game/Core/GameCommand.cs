using System;
using System.Globalization;
using System.Text;

namespace BombTavern.Core
{
    /// <summary>玩家指令类型。联机只同步指令，各端本地复算。</summary>
    public enum CommandType
    {
        RevealCell = 0,
        ToggleFlag = 1,
        UseItem = 2,
        EndTurn = 3,
        /// <summary>公开当前行动者的鼠标悬停格子（不消耗回合，仅用于观战显示）。</summary>
        SetHover = 4,
    }

    /// <summary>
    /// 一条玩家指令。联机锁步：主机与客户端交换该结构，
    /// 双端用相同种子复算，结果必然一致。
    /// </summary>
    [Serializable]
    public struct GameCommand
    {
        public CommandType type;
        public int seatIndex;

        /// <summary>目标格子坐标。</summary>
        public int x;
        public int y;

        /// <summary>使用的道具在背包中的下标。</summary>
        public int inventorySlot;

        /// <summary>部分道具需要指定其他玩家（例如换手）。</summary>
        public int targetSeat;

        /// <summary>发出该指令时玩家所处的回合序号，用于校验乱序。</summary>
        public int turnIndex;

        public static GameCommand Reveal(int seat, int x, int y, int turnIndex) =>
            new GameCommand { type = CommandType.RevealCell, seatIndex = seat, x = x, y = y, inventorySlot = -1, targetSeat = -1, turnIndex = turnIndex };

        public static GameCommand Flag(int seat, int x, int y, int turnIndex) =>
            new GameCommand { type = CommandType.ToggleFlag, seatIndex = seat, x = x, y = y, inventorySlot = -1, targetSeat = -1, turnIndex = turnIndex };

        public static GameCommand UseItem(int seat, int slot, int x, int y, int targetSeat, int turnIndex) =>
            new GameCommand { type = CommandType.UseItem, seatIndex = seat, x = x, y = y, inventorySlot = slot, targetSeat = targetSeat, turnIndex = turnIndex };

        public static GameCommand End(int seat, int turnIndex) =>
            new GameCommand { type = CommandType.EndTurn, seatIndex = seat, x = -1, y = -1, inventorySlot = -1, targetSeat = -1, turnIndex = turnIndex };

        public static GameCommand Hover(int seat, int x, int y, int turnIndex) =>
            new GameCommand { type = CommandType.SetHover, seatIndex = seat, x = x, y = y, inventorySlot = -1, targetSeat = -1, turnIndex = turnIndex };

        /// <summary>序列化为一行文本，便于 TCP 传输与日志记录。</summary>
        public string ToWire()
        {
            var sb = new StringBuilder(48);
            sb.Append((int)type).Append('|')
              .Append(seatIndex).Append('|')
              .Append(x).Append('|')
              .Append(y).Append('|')
              .Append(inventorySlot).Append('|')
              .Append(targetSeat).Append('|')
              .Append(turnIndex);
            return sb.ToString();
        }

        public static bool TryParse(string wire, out GameCommand cmd)
        {
            cmd = default;
            if (string.IsNullOrEmpty(wire)) return false;
            var parts = wire.Trim().Split('|');
            if (parts.Length < 7) return false;
            if (!TryInt(parts[0], out int t)) return false;
            if (!TryInt(parts[1], out int seat)) return false;
            if (!TryInt(parts[2], out int x)) return false;
            if (!TryInt(parts[3], out int y)) return false;
            if (!TryInt(parts[4], out int slot)) return false;
            if (!TryInt(parts[5], out int target)) return false;
            if (!TryInt(parts[6], out int turn)) return false;

            cmd = new GameCommand
            {
                type = (CommandType)t,
                seatIndex = seat,
                x = x,
                y = y,
                inventorySlot = slot,
                targetSeat = target,
                turnIndex = turn,
            };
            return true;
        }

        static bool TryInt(string s, out int v) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        public override string ToString() => ToWire();
    }
}
