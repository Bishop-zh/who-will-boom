using System;
using System.Collections.Generic;
using System.Threading;
using BombTavern.Net;
using UnityEditor;
using UnityEngine;

namespace BombTavern.EditorTools
{
    /// <summary>
    /// 四人语音房的传输冒烟测试：一个主机加三个客户端，验证后台发送队列、
    /// 双向广播和语音 PCM 编解码。硬件麦克风录放仍需在普通播放器里测试。
    /// </summary>
    public static class VoiceChatSmokeTest
    {
        public static void Run()
        {
            var host = new TcpLanTransport();
            var clients = new List<TcpLanTransport>();
            try
            {
                int port = 28641;
                host.StartHost(port);
                if (!host.IsRunning) throw new Exception("主机监听失败");

                int hostConnections = 0;
                int hostPackets = 0;
                var clientBroadcasts = new int[3];
                host.PeerConnected += _ => hostConnections++;
                host.MessageReceived += (_, line) =>
                {
                    if (!NetProtocol.TryDecode(line, out var type, out var payload) || type != NetMessageType.Voice)
                        return;
                    if (NetProtocol.TryDecodeVoice(payload, out _, out var pcm) && pcm.Length == 960)
                        hostPackets++;
                };

                for (int i = 0; i < 3; i++)
                {
                    int index = i;
                    var client = new TcpLanTransport();
                    client.MessageReceived += (_, line) =>
                    {
                        if (line == "VOICE_ROOM_BROADCAST") clientBroadcasts[index]++;
                    };
                    clients.Add(client);
                    client.StartClient("127.0.0.1", port);
                }

                PumpUntil(host, clients, () => hostConnections == 3 && clients.TrueForAll(c => c.IsRunning),
                    8000, "四名玩家没有在超时前连入同一房间");

                for (int i = 0; i < clients.Count; i++)
                {
                    var pcm = new byte[960];
                    for (int n = 0; n < pcm.Length; n++) pcm[n] = (byte)(i * 31 + n);
                    clients[i].Send(TcpLanTransport.HostPeerId,
                        NetProtocol.Encode(NetMessageType.Voice, NetProtocol.EncodeVoice(i + 1, pcm)));
                }

                PumpUntil(host, clients, () => hostPackets == 3, 5000,
                    "客户端语音包未全部送达主机");

                host.Broadcast("VOICE_ROOM_BROADCAST");
                PumpUntil(host, clients,
                    () => clientBroadcasts[0] == 1 && clientBroadcasts[1] == 1 && clientBroadcasts[2] == 1,
                    5000, "主机语音广播未送达全部三个客户端");

                Debug.Log("[语音自测] 四人房后台收发、广播和 PCM 编解码全部通过 ✅");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[语音自测] 失败：" + e);
                EditorApplication.Exit(1);
            }
            finally
            {
                for (int i = 0; i < clients.Count; i++) clients[i].Stop();
                host.Stop();
            }
        }

        static void PumpUntil(TcpLanTransport host, List<TcpLanTransport> clients,
            Func<bool> done, int timeoutMilliseconds, string failure)
        {
            int started = Environment.TickCount;
            while (!done())
            {
                host.Pump();
                for (int i = 0; i < clients.Count; i++) clients[i].Pump();
                if (unchecked((uint)(Environment.TickCount - started)) >= timeoutMilliseconds)
                    throw new TimeoutException(failure);
                Thread.Sleep(10);
            }
        }
    }
}
