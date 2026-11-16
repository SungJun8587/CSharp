// Program.cs : Gateway 서버 진입점.
using System;
using Core.Util;
using Game.ServerCommon;

namespace Game.GatewayServer;

public static class Program
{
    public static int Main(string[] argv) => ServerMain.Run<GatewayOptions>(
        argv,
        System.IO.Path.Combine("Config", "gatewayserver_config.json"),
        () => new GatewayServerConfig(),
        ReadArgs,
        Start);

    private static bool ReadArgs(MainArgs args, GatewayOptions o)
    {
        bool ok = true;
        ok &= args.ReadString("client-ip", ref o.ClientBindIp);
        ok &= args.ReadUShort("client-port", ref o.ClientPort, 1, 65535);
        ok &= args.ReadInt("max-clients", ref o.MaxClientSessions, 1, int.MaxValue);
        ok &= args.ReadString("zone-ip", ref o.ZoneBindIp);
        ok &= args.ReadUShort("zone-port", ref o.ZonePort, 1, 65535);
        ok &= args.ReadInt("max-zones", ref o.MaxZoneSessions, 1, int.MaxValue);
        ok &= args.ReadUInt("heartbeat-ms", ref o.HeartbeatIntervalMs, 0, uint.MaxValue);
        ok &= args.ReadUInt("heartbeat-timeout-ms", ref o.HeartbeatTimeoutMs, 0, uint.MaxValue);
        ok &= args.ReadUInt("client-send-queue-bytes", ref o.ClientMaxSendQueueBytes, 0, uint.MaxValue);
        return ok;
    }

    private static IServerHost? Start(GatewayOptions o)
    {
        GatewayServerMain server = new GatewayServerMain();
        if (!server.Start(o))
        {
            LogManager.Error("GatewayServerMain.Start failed.");
            server.Dispose();
            return null;
        }
        Console.WriteLine($"GatewayServer started. client port={o.ClientPort}, zone port={o.ZonePort}. Press Ctrl+C to stop.");
        return server;
    }
}
