using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BombTavern.Net
{
    /// <summary>一条可分享给对方的连接地址。</summary>
    public struct ShareableAddress
    {
        /// <summary>对方要填进「主机地址」栏的内容。</summary>
        public string address;

        /// <summary>网卡名，用来帮玩家分辨「这个地址是走哪条路的」。</summary>
        public string adapter;

        /// <summary>这一条属于哪种联机方式。</summary>
        public Kind kind;

        public enum Kind
        {
            /// <summary>普通局域网（192.168 / 10.x / 172.16-31）。</summary>
            Lan,
            /// <summary>Radmin VPN 虚拟网卡（26.x）。</summary>
            Radmin,
            /// <summary>其它虚拟网卡（ZeroTier / Hamachi / 各种 VPN）。</summary>
            Virtual,
        }

        public ShareableAddress(string address, string adapter, Kind kind)
        {
            this.address = address;
            this.adapter = adapter;
            this.kind = kind;
        }

        public string Label
        {
            get
            {
                switch (kind)
                {
                    case Kind.Radmin: return "Radmin VPN";
                    case Kind.Virtual: return "虚拟网卡";
                    default: return "局域网";
                }
            }
        }
    }

    /// <summary>
    /// 枚举本机可用于联机的地址。
    ///
    /// 为什么需要它：房主必须把自己的地址**念给对方**，而一台机器通常有好几个 IPv4
    /// （物理网卡、Radmin VPN 的 26.x、ZeroTier、WSL、虚拟交换机…），
    /// 随便报一个往往连不通。这里把它们全列出来并标好来源，省掉猜的过程。
    ///
    /// 关于内网穿透的重要区别：
    /// - **Radmin VPN**：它给每台机器一张虚拟网卡（26.x.x.x），
    ///   双方连上同一个 Radmin 网络后**彼此直连**，等价于同一局域网。
    ///   所以房主报 26.x 那个地址即可，本游戏的 TCP 监听（0.0.0.0）天然可用，
    ///   不需要任何额外改造。
    /// - **樱花内网穿透（frp）**：它是**由房主本机主动连出去**的隧道，
    ///   公网入口在 frp 服务器上。所以房主这边**没有**「可以报给对方的本机 IP」，
    ///   该填的是 frp 面板分配给你的「域名:远程端口」——那是客户端要连的地址。
    /// </summary>
    public static class NetAddressUtility
    {
        /// <summary>Radmin VPN 网段（26.0.0.0/8），用于识别它的虚拟网卡。</summary>
        static bool IsRadminAddress(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            return b.Length == 4 && b[0] == 26;
        }

        static bool IsLanAddress(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            if (b.Length != 4) return false;
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            return false;
        }

        /// <summary>列出本机所有可供对方连接的 IPv4 地址，Radmin 排在前面（最常用）。</summary>
        public static List<ShareableAddress> ListShareable()
        {
            var list = new List<ShareableAddress>();

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    IPInterfaceProperties props;
                    try { props = ni.GetIPProperties(); }
                    catch (Exception) { continue; }

                    string adapterName = ni.Name;

                    foreach (var ua in props.UnicastAddresses)
                    {
                        var ip = ua.Address;
                        if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ip)) continue;

                        ShareableAddress.Kind kind;
                        if (IsRadminAddress(ip)) kind = ShareableAddress.Kind.Radmin;
                        else if (IsLanAddress(ip)) kind = ShareableAddress.Kind.Lan;
                        else kind = ShareableAddress.Kind.Virtual;

                        list.Add(new ShareableAddress(ip.ToString(), adapterName, kind));
                    }
                }
            }
            catch (Exception)
            {
                // 拿不到网卡信息（受限环境）不该让界面崩，退回只给回环地址
            }

            // Radmin 最可能需要，排最前；其余按「局域网 → 虚拟网卡」排
            list.Sort((a, b) =>
            {
                int ra = Rank(a.kind), rb = Rank(b.kind);
                if (ra != rb) return ra - rb;
                return string.CompareOrdinal(a.address, b.address);
            });

            return list;
        }

        static int Rank(ShareableAddress.Kind k)
        {
            switch (k)
            {
                case ShareableAddress.Kind.Radmin: return 0;
                case ShareableAddress.Kind.Lan: return 1;
                default: return 2;
            }
        }

        /// <summary>
        /// 粗略校验用户在「主机地址」里填的东西。
        /// 允许 IPv4、IPv6、以及域名（内网穿透给的通常是域名），
        /// 但挡掉明显的输入错误（空白、带 http://、带端口号）。
        /// </summary>
        public static bool TryValidateHostInput(string input, out string reason)
        {
            return TryParseEndpointInput(input, 7788, out _, out _, out reason);
        }

        /// <summary>同时接受“地址”和“地址:端口”，便于直接粘贴房主或樱花面板给出的入口。</summary>
        public static bool TryParseEndpointInput(string input, int defaultPort,
            out string host, out int port, out string reason)
        {
            host = null;
            port = defaultPort > 0 ? defaultPort : 7788;
            reason = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                reason = "请填写主机地址。";
                return false;
            }

            string s = input.Trim();

            if (s.Contains("://"))
            {
                reason = "只填地址，不要带 http:// 或 https://。";
                return false;
            }
            if (s.StartsWith("[") && s.Contains("]"))
            {
                int close = s.IndexOf(']');
                host = s.Substring(1, close - 1);
                if (close + 1 < s.Length && s[close + 1] == ':' &&
                    !int.TryParse(s.Substring(close + 2), out port))
                { reason = "端口格式不正确。"; return false; }
            }
            else
            {
                int first = s.IndexOf(':');
                int last = s.LastIndexOf(':');
                if (first > 0 && first == last)
                {
                    host = s.Substring(0, first);
                    if (!int.TryParse(s.Substring(first + 1), out port))
                    { reason = "冒号后应为数字端口。"; return false; }
                }
                else host = s;
            }
            if (string.IsNullOrWhiteSpace(host) || host.Contains(" "))
            {
                reason = "地址里不能有空格。";
                return false;
            }
            if (port < 1 || port > 65535)
            { reason = "端口必须在 1—65535 之间。"; return false; }

            return true;
        }
    }
}
