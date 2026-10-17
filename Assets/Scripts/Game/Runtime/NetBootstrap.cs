using BombTavern.Net;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 联机引导。必须在 GameDirector / LanNetworkManager 的 Start 之前，把
    /// 「本端到底扮演什么角色」定下来。要解决两个真实的坑：
    ///
    ///   1) 场景装配时 LanNetworkManager 是 disabled 的（单机根本不需要它跑）。
    ///      MonoBehaviour 被禁用时 Start 永远不会执行 —— 所以只改它的 mode 没用，
    ///      必须在这里重新 enabled，它才会真正开房 / 连主机。
    ///
    ///   2) GameDirector.mode 是场景里烘死的 SinglePlayer，而它的 Start 会按 mode 决定
    ///      要不要自动开一局单机。如果不在它 Start 之前改成联机模式，
    ///      主机和客户端会**各自跑一局互不相干的单机对局**，看起来「连上了」其实根本没联上。
    ///
    /// 优先级：命令行 &gt; 开始界面 (SessionContext) &gt; 单机。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class NetBootstrap : MonoBehaviour
    {
        void Awake()
        {
            var director = FindObjectOfType<GameDirector>();
            var lan = FindObjectOfType<LanNetworkManager>();

            var cli = NetCliOptions.Parse();
            GameMode wanted = cli.specified
                ? cli.mode
                : (SessionContext.Instance != null ? SessionContext.Instance.mode : GameMode.SinglePlayer);

#if UNITY_WEBGL && !UNITY_EDITOR
            wanted = GameMode.SinglePlayer;
            if (SessionContext.Instance != null) SessionContext.Instance.mode = wanted;
            if (lan != null) lan.enabled = false;
#endif
            // 必须在 GameDirector.Start 之前写进去
            if (director != null) director.mode = wanted;

            if (lan == null) return;

            lan.mode = wanted;
            if (cli.specified)
            {
                if (!string.IsNullOrEmpty(cli.address)) lan.connectAddress = cli.address;
                if (cli.port > 0) lan.port = cli.port;
            }
            if (wanted != GameMode.SinglePlayer) lan.enabled = true;

            if (wanted == GameMode.SinglePlayer) return;

            Debug.Log($"[联机] 引导完成：mode={wanted} " +
                      $"addr={(string.IsNullOrEmpty(lan.connectAddress) ? "-" : lan.connectAddress)} " +
                      $"port={(lan.port > 0 ? lan.port : 0)}");
        }
    }
}
