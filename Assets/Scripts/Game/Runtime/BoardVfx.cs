using BombTavern.Core;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 把对局事件翻译成**落在具体格子上的**特效。
    ///
    /// 之前道具的效果只是在角色身上冒一片光，玩家看不出「作用在哪一格」——
    /// 反馈弱不是因为强度不够，是因为**没有指向性**。
    /// 这里按事件的作用范围（单格 / 整行 / 圆形区域）精确落点。
    ///
    /// 保密规则：secret 且不是本地座位的事件不放特效，
    /// 否则「平板突然闪一下」就等于告诉别人对手刚用了道具。
    /// </summary>
    [DisallowMultipleComponent]
    public class BoardVfx : MonoBehaviour
    {
        [Header("绑定")]
        [Tooltip("对局驱动。留空则自动查找。")]
        public GameDirector director;

        [Header("强度")]
        [Tooltip("单个事件最多在几格上放特效。区域扫描可能覆盖几十格，不设上限会刷屏。")]
        [Range(1, 40)] public int maxCellsPerEvent = 12;

        [Tooltip("翻开安全格的特效强度（比道具弱，避免每一下都抢注意力）。")]
        [Range(0f, 2f)] public float revealStrength = 0.55f;

        [Tooltip("道具特效的强度。")]
        [Range(0f, 3f)] public float itemStrength = 1.35f;

        void Awake()
        {
            if (director == null) director = FindObjectOfType<GameDirector>();
        }

        void OnDestroy()
        {
            if (director != null) director.Event -= OnEvent;
        }

        void Start()
        {
            if (director != null) director.Event += OnEvent;
        }

        void OnEvent(GameEvent e)
        {
            if (director == null) return;

            // 保密事件不能有可见反馈 —— 闪一下本身就是情报
            bool isMine = e.seatIndex == director.localSeat;
            if (e.secret && !isMine) return;

            var color = director.ColorOfSeat(e.seatIndex);

            switch (e.type)
            {
                case GameEventType.CellRevealed:
                    Pulse(e.seatIndex, e.x, e.y, color, revealStrength);
                    break;

                case GameEventType.CellProbed:
                case GameEventType.CellRiskModified:
                    Pulse(e.seatIndex, e.x, e.y, color, itemStrength);
                    break;

                case GameEventType.CellFlagged:
                    Pulse(e.seatIndex, e.x, e.y, UiTheme.Accent, 0.8f);
                    break;

                case GameEventType.CellHit:
                    Pulse(e.seatIndex, e.x, e.y, UiTheme.Danger, 1.8f);
                    break;

                case GameEventType.RowScanned:
                    ScanRow(e.seatIndex, e.y, color);
                    break;

                case GameEventType.AreaScanned:
                    ScanArea(e.seatIndex, e.x, e.y, color);
                    break;
            }
        }

        /// <summary>整行扫描：沿该行依次点亮，让「扫描」这个动作本身有过程感。</summary>
        void ScanRow(int seat, int row, Color color)
        {
            var tablet = director.TabletOf(seat);
            if (tablet == null) return;

            int cols = tablet.Board != null ? tablet.Board.Columns : 0;
            for (int x = 0; x < cols && x < maxCellsPerEvent; x++)
                Pulse(seat, x, row, color, itemStrength);
        }

        /// <summary>区域扫描：以目标格为中心扩散一圈。</summary>
        void ScanArea(int seat, int cx, int cy, Color color)
        {
            var tablet = director.TabletOf(seat);
            if (tablet == null || tablet.Board == null) return;

            int radius = 2;
            int done = 0;

            for (int ring = 0; ring <= radius; ring++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        // 只画当前这一环，做出由内向外扩散的层次
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue;

                        int x = cx + dx, y = cy + dy;
                        if (!tablet.Board.InBounds(x, y)) continue;

                        Pulse(seat, x, y, color, itemStrength * (1f - ring * 0.18f));
                        if (++done >= maxCellsPerEvent) return;
                    }
                }
            }
        }

        void Pulse(int seat, int x, int y, Color color, float strength)
        {
            var tablet = director.TabletOf(seat);
            if (tablet == null) return;
            if (!tablet.TryGetCellWorldPosition(x, y, out var world)) return;

            // 沿格子所在平板的法线方向喷，粒子才会「从格子里冒出来」
            var normal = tablet.transform.up;
            SimpleVfx.CellPulse(world, normal, color, strength);
        }
    }
}
