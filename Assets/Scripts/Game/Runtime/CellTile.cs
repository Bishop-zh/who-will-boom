using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 扫雷屏上单个格子的碰撞代理，用于把射线命中翻译成盘面坐标。
    ///
    /// 注意：本类必须单独放在 CellTile.cs 中（Unity 要求类名与文件名一致），
    /// 否则组件在序列化时会变成"missing script"，进而导致场景数据损坏。
    /// </summary>
    [DisallowMultipleComponent]
    public class CellTile : MonoBehaviour
    {
        /// <summary>这块屏归属的座位；共用盘面时为 -1。</summary>
        public int ownerSeat = -1;

        /// <summary>格子在盘面上的坐标。</summary>
        public int x;
        public int y;

        /// <summary>格子渲染器。</summary>
        public Renderer tileRenderer;
    }
}
