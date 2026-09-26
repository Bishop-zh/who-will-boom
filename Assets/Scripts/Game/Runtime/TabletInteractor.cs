using BombTavern.Config;
using BombTavern.Core;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 扫雷屏交互：射线指向格子、左键翻格、右键插旗；数字键拿出道具，E 键确认使用。
    ///
    /// 共用盘面模式下只有"当前行动者"能操作，其他玩家看到的是同一块屏，
    /// 并且会把当前行动者的悬停位置同步给所有人（使用道具则完全保密）。
    /// </summary>
    [DisallowMultipleComponent]
    public class TabletInteractor : MonoBehaviour
    {
        [Header("绑定")]
        [Tooltip("对局驱动。留空则自动在场景中查找。")]
        public GameDirector director;

        [Tooltip("玩家相机。留空则用 Camera.main。")]
        public Camera viewCamera;

        [Header("交互")]
        [Tooltip("射线最大距离（米）。长桌对面也能看清，但操作需要走近。")]
        public float maxDistance = 7f;

        [Tooltip("悬停格子的抬升高度（米），作为视觉反馈。")]
        public float hoverLiftHeight = 0.004f;

        [Tooltip("是否允许翻格。")]
        public bool allowReveal = true;

        [Tooltip("是否允许插旗。")]
        public bool allowFlag = true;

        [Tooltip("是否把自己的悬停位置公开给其他玩家。")]
        public bool publishHover = true;

        public CellTile Hovered { get; private set; }
        public TabletScreen HoveredTablet { get; private set; }
        public int SelectedItemSlot { get; private set; } = -1;

        public ItemDefinition SelectedItemDefinition
        {
            get
            {
                var session = director != null ? director.Session : null;
                var me = session != null ? session.PlayerAt(director.localSeat) : null;
                return me != null && SelectedItemSlot >= 0 && SelectedItemSlot < me.inventory.Count
                    ? me.inventory[SelectedItemSlot].definition : null;
            }
        }

        CellTile _previous;
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        void Awake()
        {
            if (director == null) director = FindObjectOfType<GameDirector>();
            if (viewCamera == null) viewCamera = Camera.main;
        }

        void Update()
        {
            if (director == null || director.Session == null || viewCamera == null) return;

            // 开局前菜单阶段不接受盘面操作：此时镜头是环绕的旁观机位，
            // 射线指到哪完全不受玩家控制，放行会导致「莫名其妙翻了一格」。
            if (director.IsPregame)
            {
                Hovered = null;
                HoveredTablet = null;
                _previous = null;
                return;
            }

            UpdateHover();
            HandleActions();
        }

        void UpdateHover()
        {
            CellTile found = null;
            TabletScreen foundTablet = null;

            var ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
            int count = Physics.RaycastNonAlloc(ray, Hits, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var tile = Hits[i].collider.GetComponent<CellTile>();
                if (tile == null) continue;
                if (Hits[i].distance >= best) continue;

                var tablet = Hits[i].collider.GetComponentInParent<TabletScreen>();

                // 只认自己面前那块面板：指向别人的面板既不显示格子信息，也点不动。
                // 否则鼠标扫过对手的屏幕就能读到他的盘面数据。
                if (tablet != null && !tablet.IsLocalPanel(director.localSeat)) continue;

                best = Hits[i].distance;
                found = tile;
                foundTablet = tablet;
            }

            Hovered = found;
            HoveredTablet = foundTablet;

            if (found == _previous)
            {
                if (found != null && publishHover && director.localSeat >= 0)
                    director.PublishHover(found.x, found.y);
                return;
            }

            // 本地抬升反馈：旧的落下，新的抬起
            if (_previous != null)
            {
                var oldTablet = _previous.GetComponentInParent<TabletScreen>();
                if (oldTablet != null) oldTablet.SetLocalHover(-1, -1);
            }

            _previous = found;

            if (found != null)
            {
                if (foundTablet != null) foundTablet.SetLocalHover(found.x, found.y);
                if (publishHover) director.PublishHover(found.x, found.y);
            }
        }

        void HandleActions()
        {
            int localSeat = director.localSeat;
            var session = director.Session;

            // 道具槽位：桌面端来自数字键 1~9，触摸端来自 HUD 按钮注入。
            // 数字键只负责「拿出」，E 键才真正提交使用。
            while (GameInput.ConsumeItemSlot(out int slot))
                SelectItem(localSeat, slot);

            ValidateSelection(localSeat);
            if (GameInput.ItemUseConfirmDown)
                ConfirmSelectedItem();

            if (Hovered == null) return;
            if (!session.IsMyTurn(localSeat)) return;

            // 桌面端：光标未锁定说明玩家正在操作菜单/暂停，不应该穿透到盘面。
            // 触摸端没有光标锁的概念，直接放行。
            if (!GameInput.TouchMode && Cursor.lockState != CursorLockMode.Locked) return;

            // 共用盘面：当前行动者当然可以操作；私有盘面：只能动自己那块
            bool operable = director.config != null && director.config.sharedBoard
                ? true
                : Hovered.ownerSeat == localSeat;
            if (!operable) return;

            // 点在界面控件上时不能穿透到盘面。
            //
            // UGUI 的射线检测与 Physics.Raycast 是**两套独立系统**：
            // 点 HUD 的道具格、托管按钮时，物理射线依然会打到平板，
            // 于是「点一下道具格，顺便翻了一格」。必须显式问 EventSystem。
            //
            // 桌面端光标锁定时鼠标被锁在屏幕中心，此时屏幕中心有没有 UI 与玩家意图无关，
            // 所以那种情况下跳过这个检查（否则准星附近有面板就会永远点不动）。
            bool cursorLocked = !GameInput.TouchMode && Cursor.lockState == CursorLockMode.Locked;
            if (!cursorLocked && PointerOverUi()) return;

            // 主操作 = 翻格（鼠标左键 / 触摸短按）；次操作 = 插旗（右键 / 长按）
            if (allowReveal && GameInput.PrimaryDown)
                director.Submit(GameCommand.Reveal(localSeat, Hovered.x, Hovered.y, session.TurnIndex));

            if (allowFlag && GameInput.SecondaryDown)
                director.Submit(GameCommand.Flag(localSeat, Hovered.x, Hovered.y, session.TurnIndex));
        }

        /// <summary>当前指针是否停在 UGUI 控件上。</summary>
        static bool PointerOverUi()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;

            if (GameInput.TouchMode)
            {
                // 触摸端要按手指 ID 查，否则多指时判断不准
                if (Input.touchCount == 0) return false;
                return es.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            }

            return es.IsPointerOverGameObject();
        }

        void SelectItem(int localSeat, int slotIndex)
        {
            var session = director.Session;
            var me = session.PlayerAt(localSeat);
            if (me == null || slotIndex < 0 || slotIndex >= me.inventory.Count) return;
            if (!session.IsMyTurn(localSeat) || director.IsLocalAutoPlayed) return;

            var def = me.inventory[slotIndex].definition;
            if (def == null) return;

            SelectedItemSlot = slotIndex;
            var presenter = FindLocalPropPresenter();
            if (presenter != null) presenter.ShowSelected(def.effect);
        }

        void ValidateSelection(int localSeat)
        {
            if (SelectedItemSlot < 0) return;
            var session = director != null ? director.Session : null;
            var me = session != null ? session.PlayerAt(localSeat) : null;
            if (me == null || me.eliminated || session.IsOver ||
                !session.IsMyTurn(localSeat) || director.IsLocalAutoPlayed ||
                SelectedItemSlot >= me.inventory.Count || me.inventory[SelectedItemSlot].definition == null)
                ClearSelectedItem();
        }

        public bool ConfirmSelectedItem()
        {
            int localSeat = director != null ? director.localSeat : -1;
            var session = director != null ? director.Session : null;
            var me = session != null ? session.PlayerAt(localSeat) : null;
            if (me == null || SelectedItemSlot < 0 || SelectedItemSlot >= me.inventory.Count) return false;
            if (!session.IsMyTurn(localSeat) || director.IsLocalAutoPlayed) return false;

            var def = me.inventory[SelectedItemSlot].definition;
            if (def == null) return false;
            if (def.requiresTarget && Hovered == null) return false;

            int x = Hovered != null ? Hovered.x : -1;
            int y = Hovered != null ? Hovered.y : -1;

            // 目标玩家：
            // - 自用道具：自己
            // - 私有盘面：当前指向的盘面归属者
            // - 共用盘面：不存在"别人的盘"，需要指定对手的道具（如换手令）取下一名存活玩家
            int targetSeat;
            if (def.targetScope == ItemTargetScope.SelfBoard)
                targetSeat = localSeat;
            else if (director.config != null && director.config.sharedBoard)
                targetSeat = NextAliveSeat(session, localSeat);
            else
                targetSeat = Hovered != null ? Hovered.ownerSeat : localSeat;

            bool submitted = director.Submit(GameCommand.UseItem(localSeat, SelectedItemSlot, x, y,
                targetSeat, session.TurnIndex));
            if (submitted) ClearSelectedItem();
            return submitted;
        }

        public void ClearSelectedItem()
        {
            SelectedItemSlot = -1;
            var presenter = FindLocalPropPresenter();
            if (presenter != null) presenter.ClearSelection();
        }

        ItemPropPresenter FindLocalPropPresenter()
        {
            var all = FindObjectsOfType<ItemPropPresenter>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].view == ItemPropPresenter.ViewMode.FirstPersonCamera &&
                    all[i].seatIndex == director.localSeat)
                    return all[i];
            return null;
        }

        static int NextAliveSeat(GameSession session, int from)
        {
            if (session.Players == null) return from;
            for (int step = 1; step <= session.Players.Length; step++)
            {
                int s = (from + step) % session.Players.Length;
                if (!session.Players[s].eliminated) return s;
            }
            return from;
        }
    }
}
