using System.Collections.Generic;
using BombTavern.Config;
using BombTavern.Core;

namespace BombTavern.Items
{
    /// <summary>
    /// 道具执行上下文。
    ///
    /// 自定义效果通过它使用会话能力，而不是把 GameSession 的内部字段全部公开出去 ——
    /// 那样注册表就变成了「绕过封装的后门」，架构会比改 switch 更糟。
    /// </summary>
    public sealed class ItemContext
    {
        public GameSession session;
        public PlayerState user;

        /// <summary>已解析好的目标盘面。作用于自身的道具（护盾 / 冻结 / 换手）会为 null。</summary>
        public RiskBoard board;

        public ItemDefinition def;
        public GameCommand cmd;

        internal System.Action<PlayerState, RiskBoard, int, int> probeInto;
        internal System.Action<GameEvent> raise;

        public int Columns => board != null ? board.Columns : 0;
        public int Rows => board != null ? board.Rows : 0;

        /// <summary>把某格的真实风险写进使用者的已知情报，等同于「探查」这一动作。</summary>
        public void Probe(int x, int y)
        {
            if (board == null) return;
            probeInto?.Invoke(user, board, x, y);
        }

        /// <summary>抛一条对局事件。表现层与联机同步都靠它驱动。</summary>
        public void Raise(GameEvent e) => raise?.Invoke(e);

        /// <summary>快捷：抛一条公共的「使用了这个道具」事件，不携带目标与效果位置。</summary>
        public void RaiseItemUsed(int x = -1, int y = -1, float value = 1f)
        {
            if (user == null) return;
            raise?.Invoke(new GameEvent
            {
                type = GameEventType.ItemUsed,
                seatIndex = user.seatIndex,
                x = -1,
                y = -1,
                otherSeat = -1,
                value = value,
                message = def != null ? def.displayName : null,
                secret = false,
            });
        }

        /// <summary>这个道具是不是「暗算别人」的（目标盘面不属于使用者）。</summary>
        public bool TargetsOpponent
            => session != null && board != null && user != null
               && board != (session.Config != null && session.Config.sharedBoard
                   ? session.SharedBoard
                   : (user.board ?? session.SharedBoard));
    }

    /// <summary>一种道具效果的处理器。</summary>
    public interface IItemEffect
    {
        /// <summary>本处理器负责的效果类型。</summary>
        ItemEffectType Type { get; }

        /// <summary>目标是否合法。返回 false 时请求被拒，且**不消耗道具**。</summary>
        bool CanApply(ItemContext ctx, out string reason);

        /// <summary>执行效果。返回 false 表示未生效（同样不消耗道具）。</summary>
        bool Apply(ItemContext ctx);
    }

    /// <summary>
    /// 效果注册表 —— 道具系统的扩展点。
    ///
    /// 加一个「靠调参表达不出来」的新效果，只需要三步：
    ///   1) 在 <see cref="ItemEffectType"/> 里加一个枚举值
    ///   2) 写一个实现 <see cref="IItemEffect"/> 的类
    ///   3) 在任意初始化时机调用 <see cref="Register"/>
    /// **不需要再改 GameSession 里那个 switch** —— 这正是这张表存在的唯一理由。
    ///
    /// 没有注册处理器的类型会落回 GameSession 内置的默认实现，
    /// 所以现有几种道具的行为完全不受影响。
    /// </summary>
    public static class ItemEffectRegistry
    {
        static readonly Dictionary<ItemEffectType, IItemEffect> Map =
            new Dictionary<ItemEffectType, IItemEffect>();

        /// <summary>注册（或覆盖）某个效果类型的处理器。重复注册后者生效，便于热调参。</summary>
        public static void Register(IItemEffect effect)
        {
            if (effect == null) return;
            Map[effect.Type] = effect;
        }

        public static bool TryGet(ItemEffectType type, out IItemEffect effect)
            => Map.TryGetValue(type, out effect) && effect != null;

        public static void Clear() => Map.Clear();

        /// <summary>已注册的自定义效果数量，自检时用来确认注册是否生效。</summary>
        public static int Count => Map.Count;
    }
}
