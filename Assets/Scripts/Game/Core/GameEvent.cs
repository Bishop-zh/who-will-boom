using System;

namespace BombTavern.Core
{
    /// <summary>对局事件类型。表现层只消费事件，不读取内部随机状态。</summary>
    public enum GameEventType
    {
        SessionStarted,
        TurnBegan,
        TurnEnded,
        CellRevealed,
        CellHit,
        ShieldConsumed,
        CellFlagged,
        CellProbed,
        CellRiskModified,
        RowScanned,
        AreaScanned,
        DriftFrozen,
        ItemGained,
        InventoryFull,
        ItemUsed,
        ActionRejected,
        PlayerEliminated,
        DriftApplied,
        /// <summary>当前行动者的悬停格子发生变化（公开信息）。</summary>
        HoverChanged,
        GameOver,
    }

    /// <summary>一条对局事件。</summary>
    public struct GameEvent
    {
        public GameEventType type;
        public int seatIndex;
        public int otherSeat;
        public int x;
        public int y;
        public float value;
        public string message;

        /// <summary>
        /// 是否对其他人保密：只有 seatIndex 本人能在事件流里看到，且不触发公开的视觉变化。
        /// 用于"使用道具时其他玩家看不到"。
        /// </summary>
        public bool secret;

        public static GameEvent Make(GameEventType type, int seat = -1, string message = null) =>
            new GameEvent { type = type, seatIndex = seat, otherSeat = -1, x = -1, y = -1, value = 0f, message = message };

        public override string ToString() =>
            $"{type} seat={seatIndex} ({x},{y}) value={value:F3} {message}";
    }

    /// <summary>对局结束原因。</summary>
    public enum GameOverReason
    {
        None = 0,
        LastPlayerStanding = 1,
        TurnLimitReached = 2,
    }
}
