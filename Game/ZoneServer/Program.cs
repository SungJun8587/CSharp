// Program.cs : 존 서버 진입점.
using System;
using Core.Util;
using Game.Generated;
using Game.ZoneCommon;

namespace Game.ZoneServer;

public static class Program
{
    public static int Main(string[] argv) => ServerMain.Run<ZoneServerOptions>(
        argv,
        System.IO.Path.Combine("Config", "zoneserver_config.json"),
        () => new ZoneServerConfig(),
        ReadArgs,
        Start,
        PollAbort);

    private static bool ReadArgs(MainArgs args, ZoneServerOptions o)
    {
        bool ok = true;
        ok &= args.ReadUInt("zone-id", ref o.ZoneId, 0, uint.MaxValue);
        uint kind = (uint)o.Kind;
        ok &= args.ReadUInt("kind", ref kind, 0, 255);
        o.Kind = (ZoneKind)kind;
        ok &= args.ReadUInt("capacity", ref o.Capacity, 0, uint.MaxValue);
        string name = string.Empty;
        ok &= args.ReadString("name", ref name);
        if (name.Length != 0)
            o.ServerName = name;
        ok &= args.ReadString("gateway-ip", ref o.GatewayIp);
        ok &= args.ReadUShort("gateway-port", ref o.GatewayPort, 1, 65535);
        ok &= args.ReadUInt("tick-ms", ref o.TickIntervalMs, 1, 60000);
        ok &= args.ReadFloat("min-x", ref o.MinX, -1e9, 1e9);
        ok &= args.ReadFloat("min-y", ref o.MinY, -1e9, 1e9);
        ok &= args.ReadFloat("max-x", ref o.MaxX, -1e9, 1e9);
        ok &= args.ReadFloat("max-y", ref o.MaxY, -1e9, 1e9);
        ok &= args.ReadFloat("cell-size", ref o.CellSize, 0.01, 1e9);
        ok &= args.ReadFloat("spawn-x", ref o.SpawnX, -1e9, 1e9);
        ok &= args.ReadFloat("spawn-y", ref o.SpawnY, -1e9, 1e9);
        return ok;
    }

    private static IServerHost? Start(ZoneServerOptions o)
    {
        ZoneServerMain server = new ZoneServerMain();
        if (!server.Start(o))
        {
            LogManager.Error("ZoneServerMain.Start failed. zoneId={0}", o.ZoneId);
            server.Dispose();
            return null;
        }
        Console.WriteLine($"ZoneServer started. zoneId={o.ZoneId}. Press Ctrl+C to stop.");
        return server;
    }

    private static string? PollAbort(IServerHost host)
        => ((ZoneServerMain)host).IsRegisterRejected ? "Gateway rejected zone registration. Shutting down." : null;
}
