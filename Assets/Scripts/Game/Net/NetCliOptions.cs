using System;
using BombTavern.Runtime;
using UnityEngine;

namespace BombTavern.Net
{
    /// <summary>
    /// 命令行联机参数。让同一个可执行文件既能当客户端也能当主机，
    /// 便于开专用主机、也便于自动化联机自检（起两个进程互连）。
    ///
    /// 用法：
    ///   BombTavern.exe -btHost                    以主机身份启动
    ///   BombTavern.exe -btClient 192.168.1.7      以客户端身份连接
    ///   BombTavern.exe -btPort 7777               指定端口
    /// </summary>
    public static class NetCliOptions
    {
        public const string HostArg = "-btHost";
        public const string ClientArg = "-btClient";
        public const string PortArg = "-btPort";
        public const string NameArg = "-btName";

        public sealed class Options
        {
            public GameMode mode = GameMode.SinglePlayer;
            public string address;
            public int port;
            public string playerName;
            public bool specified;
        }

        /// <summary>解析命令行。没有任何联机参数时返回 specified = false。</summary>
        public static Options Parse()
        {
            var o = new Options();
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch (Exception) { return o; }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], HostArg, StringComparison.OrdinalIgnoreCase))
                {
                    o.mode = GameMode.LanHost;
                    o.specified = true;
                }
                else if (string.Equals(args[i], ClientArg, StringComparison.OrdinalIgnoreCase))
                {
                    o.mode = GameMode.LanClient;
                    o.specified = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) o.address = args[i + 1];
                }
                else if (string.Equals(args[i], PortArg, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int p) && p > 0) o.port = p;
                }
                else if (string.Equals(args[i], NameArg, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) o.playerName = args[++i];
                }
            }

            if (o.mode == GameMode.LanClient && string.IsNullOrEmpty(o.address))
                o.address = "127.0.0.1";

            return o;
        }
    }
}
