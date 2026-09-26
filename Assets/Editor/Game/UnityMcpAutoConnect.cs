#if UNITY_EDITOR
using MCPForUnity.Editor;
using UnityEditor;

namespace BombTavern.Editor
{
    /// <summary>
    /// 项目级 Unity MCP 自愈入口。
    /// MCP for Unity 记住 HTTP 模式但 HTTP Server 未启动时，编辑器会处于
    /// 「包已安装、却没有任何桥接端口」的假连接状态。项目打开后统一切回
    /// CodeBuddy/Codex 都能直接拉起的 stdio 桥接，并在编译结束后再执行一次。
    /// </summary>
    [InitializeOnLoad]
    public static class UnityMcpAutoConnect
    {
        static UnityMcpAutoConnect()
        {
            EditorApplication.delayCall += EnsureConnected;
        }

        [MenuItem("工具/炸弹酒馆/8. 重连 Unity MCP", false, 18)]
        public static void EnsureConnected()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureConnected;
                return;
            }

            McpCiBoot.StartStdioForCi();
        }
    }
}
#endif
