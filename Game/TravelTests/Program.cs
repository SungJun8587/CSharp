// 존 이동 상태 유지 통합 테스트.
//  1) 관문 안쪽에서 떠나면 이어지는 존의 도착 지점에서 시작하고, 체력/가방이 이어진다.
//  2) 연결을 끊고 같은 캐릭터가 바로 다시 들어와도(상태가 늦게 도착하는 경주 상황 포함) 체력/가방이 이어진다.
//  3) 새 캐릭터는 가득 찬 체력으로 기본 위치에서 시작한다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Core.Net;
using Game.GatewayServer;
using Game.Generated;
using Game.ZoneCommon;
using Game.ZoneCore;
using Game.ZoneServer;

namespace Game.TravelTests;

public sealed class Client : PacketSession
{
    public Client() : base(1024) { }
    protected override int MaxRecvPacketSize => ProtocolConst.MaxClientPacketBytes;
    private readonly object _lock = new object();
    private readonly List<byte[]> _rx = new List<byte[]>();
    protected override void OnDisconnected(CloseReason reason) { }
    protected override void OnRecvPacket(ReadOnlySpan<byte> packet) { lock (_lock) _rx.Add(packet.ToArray()); }

    public void Tx<T>(T p) where T : struct, IPacket { byte[] b = new byte[T.PacketSize]; p.Write(b); Send(b); }

    public byte[]? Take(ushort type, int timeoutMs = 6000)
    {
        long end = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            lock (_lock)
                for (int i = 0; i < _rx.Count; ++i)
                    if (PacketIO.ReadType(_rx[i]) == type) { byte[] r = _rx[i]; _rx.RemoveAt(i); return r; }
            if (Environment.TickCount64 >= end) return null;
            Thread.Sleep(5);
        }
    }
}

public static class Program
{
    static int s_fail, s_pass;
    static void Check(bool ok, string what) { if (ok) ++s_pass; else { ++s_fail; Console.WriteLine("  FAIL: " + what); } }

    static string FindData()
    {
        string d = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && d != null; ++i, d = Path.GetDirectoryName(d)!)
            if (File.Exists(Path.Combine(d, "GameData", "Hero.json"))) return Path.Combine(d, "GameData");
        throw new Exception("GameData not found");
    }

    static T OnZone<T>(ZoneServerMain z, Func<BattleZone, T> f)
    {
        T result = default!; using ManualResetEventSlim done = new ManualResetEventSlim();
        if (!z.Zone!.Post(zz => { result = f((BattleZone)zz); done.Set(); })) throw new Exception("post failed");
        if (!done.Wait(5000)) throw new Exception("zone timeout");
        return result;
    }

    static Client Connect(ClientService svc) => (Client)svc.ConnectAsync().GetAwaiter().GetResult();

    static bool Login(Client c, ulong id)
    {
        c.Tx(new GwLoginReq { CharId = id });
        byte[]? r = c.Take((ushort)GatewayPacketType.GwLoginRes);
        return r != null && GwLoginRes.TryRead(r, out GwLoginRes v) && v.Success == 1;
    }

    static GwEnterZoneRes? Enter(Client c, uint zone, uint hero)
    {
        c.Tx(new GwEnterZoneReq { ZoneId = zone, HeroId = hero });
        byte[]? r = c.Take((ushort)GatewayPacketType.GwEnterZoneRes);
        return r != null && GwEnterZoneRes.TryRead(r, out GwEnterZoneRes v) ? v : null;
    }

    public sealed class AllocListener : System.Diagnostics.Tracing.EventListener
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> Types = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource src)
        {
            if (src.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(src, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
        }
        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (e.EventName != null && e.EventName.StartsWith("GCAllocationTick") && e.Payload != null)
            {
                int i = e.PayloadNames!.IndexOf("TypeName");
                if (i >= 0) Types.AddOrUpdate((string)e.Payload[i]!, 1, (_, v) => v + 1);
            }
        }
    }

    public sealed class Bot : PacketSession
    {
        public Bot() : base(1024) { }
        protected override int MaxRecvPacketSize => ProtocolConst.MaxClientPacketBytes;
        public long Rx, RxBytes;
        protected override void OnDisconnected(CloseReason reason) { }
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) { Interlocked.Increment(ref Rx); Interlocked.Add(ref RxBytes, packet.Length); }
        public void Tx<T>(T p) where T : struct, IPacket { byte[] b = new byte[T.PacketSize]; p.Write(b); Send(b); }
    }

    // 부하 측정: 존 1(몬스터 528마리)에 봇 N명을 한곳에 몰아 넣고 이동 요청을 보내 틱 처리 시간 분포를 본다. 실행: Game.TravelTests.dll load [봇수] [초]
    static int Load(int bots, int seconds)
    {
        string data = FindData();
        using GatewayServerMain gw = new GatewayServerMain();
        using ZoneServerMain z1 = new ZoneServerMain();
        gw.Start(new GatewayOptions { ClientBindIp = "127.0.0.1", ClientPort = 0, ZoneBindIp = "127.0.0.1", ZonePort = 0, ClientMaxPacketsPerSec = 0 });
        z1.Start(new ZoneServerOptions { ZoneId = 1, Capacity = (uint)bots + 10, TickIntervalMs = 50, GatewayIp = "127.0.0.1", GatewayPort = (ushort)gw.ZoneEndPoint!.Port, GameDataDir = data, SpawnX = 500, SpawnY = 500, MetricsIntervalMs = 0 });
        for (int i = 0; i < 200 && !z1.IsRegistered; ++i) Thread.Sleep(25);
        ClientService svc = new ClientService(gw.ClientEndPoint!, () => new Bot(), new NetServiceOptions { MaxSessionCount = bots + 8 });
        List<Bot> list = new List<Bot>();
        for (int i = 0; i < bots; ++i)
        {
            Bot b = (Bot)svc.ConnectAsync().GetAwaiter().GetResult();
            b.Tx(new GwLoginReq { CharId = (ulong)(9000 + i) });
            Thread.Sleep(5);
            b.Tx(new GwEnterZoneReq { ZoneId = 1, HeroId = (uint)(i % 4 + 1) });
            list.Add(b);
        }
        Thread.Sleep(1500);
        using AllocListener al = new AllocListener();
        Random rnd = new Random(1);
        ZoneStats s0 = z1.Zone!.GetStats();
        long end = Environment.TickCount64 + seconds * 1000L;
        long zoneA0 = 0, zoneA1 = 0; using (ManualResetEventSlim m = new ManualResetEventSlim()) { z1.Zone!.Post(zz => { zoneA0 = GC.GetAllocatedBytesForCurrentThread(); m.Set(); }); m.Wait(); }
        long gc0 = GC.CollectionCount(0), gc2 = GC.CollectionCount(2); long mem0 = GC.GetTotalAllocatedBytes();
        while (Environment.TickCount64 < end)
        {
            if (Environment.GetEnvironmentVariable("IDLE") == null)
            foreach (Bot b in list)
                b.Tx(new ZcMoveReq { PosX = 500, PosY = 500, DestX = 350 + (float)rnd.NextDouble() * 300, DestY = 350 + (float)rnd.NextDouble() * 300 });
            Thread.Sleep(300);
        }
        using (ManualResetEventSlim m = new ManualResetEventSlim()) { z1.Zone!.Post(zz => { zoneA1 = GC.GetAllocatedBytesForCurrentThread(); m.Set(); }); m.Wait(); }
        Console.WriteLine($"zone thread alloc = {(zoneA1 - zoneA0) / 1048576} MB");
        ZoneStats s1 = z1.Zone!.GetStats();
        ulong ticks = s1.TickCount - s0.TickCount;
        Console.WriteLine($"bots={bots} sec={seconds} ticks={ticks} avgTick={(s1.TotalTickUs - s0.TotalTickUs) / Math.Max(1UL, ticks)}us maxTick={s1.MaxTickUs}us overrun={s1.OverrunTicks - s0.OverrunTicks} skipped={s1.SkippedTicks - s0.SkippedTicks}");
        Console.Write("buckets(<1,2,5,10,25,50ms,>=50): ");
        for (int i = 0; i < s1.TickBuckets!.Length; ++i) Console.Write((s1.TickBuckets[i] - s0.TickBuckets![i]) + " ");
        Console.WriteLine();
        Console.WriteLine($"rx pkts={list.Sum(b => b.Rx)} bytes={list.Sum(b => b.RxBytes)} gc0={GC.CollectionCount(0) - gc0} gc2={GC.CollectionCount(2) - gc2} allocMB={(GC.GetTotalAllocatedBytes() - mem0) / 1048576}");
        foreach (var kv in al.Types.OrderByDescending(k => k.Value).Take(8)) Console.WriteLine($"  alloc {kv.Key}: {kv.Value} ticks(~100KB)");
        svc.Close();
        return 0;
    }

    static ZcDuelNotify? DuelRx(Client c, DuelState want, int timeoutMs = 6000)
    {
        long end = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < end)
        {
            byte[]? r = c.Take(DuelPacketType.Notify, 200);
            if (r != null && ZcDuelNotify.TryRead(r, out ZcDuelNotify v) && v.State == (byte)want) return v;
        }
        return null;
    }

    static void DuelTx(Client c, DuelAction a, ulong target) => c.Tx(new ZcDuelReq { Action = (byte)a, TargetId = target });


    // ---- 혼돈 시험: 봇이 이동/공격/스킬/결투/존 이동/재접속/쓰레기 패킷을 무작위로 쏟아붓는다. 끝난 뒤 서버 상태에 찌꺼기가 남지 않았는지,
    // 존 작업이 예외로 중단되지 않았는지, 틱이 밀리지 않았는지 본다. 실행: Game.TravelTests.dll chaos [봇수] [초]
    static void DumpCounts(string label, object o)
    {
        for (Type? t = o.GetType(); t != null && t != typeof(object); t = t.BaseType)
            foreach (System.Reflection.FieldInfo f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
            {
                object? v = f.GetValue(o);
                if (v is System.Collections.ICollection c && c.Count != 0) Console.WriteLine($"    {label}.{f.Name} = {c.Count}");
                else if (v != null && v.GetType().Name.StartsWith("ConcurrentDictionary") && v is System.Collections.IEnumerable e)
                { int n = 0; foreach (object _ in e) ++n; if (n != 0) Console.WriteLine($"    {label}.{f.Name} = {n}"); }
            }
    }

    static int Chaos(int bots, int seconds)
    {
        string realData = FindData();
        using GatewayServerMain gw = new GatewayServerMain();
        ZoneServerMain[] zs = new ZoneServerMain[3];
        gw.Start(new GatewayOptions { ClientBindIp = "127.0.0.1", ClientPort = 0, ZoneBindIp = "127.0.0.1", ZonePort = 0, ClientMaxPacketsPerSec = 0 });
        for (int i = 0; i < 3; ++i)
        {
            zs[i] = new ZoneServerMain();
            zs[i].Start(new ZoneServerOptions { ZoneId = (uint)(i + 1), Capacity = 500, TickIntervalMs = 50, GatewayIp = "127.0.0.1", GatewayPort = (ushort)gw.ZoneEndPoint!.Port, GameDataDir = realData, SpawnX = 500, SpawnY = 500, MetricsIntervalMs = 0, AllowPvp = i == 2 });
        }
        for (int i = 0; i < 400 && !(zs[0].IsRegistered && zs[1].IsRegistered && zs[2].IsRegistered); ++i) Thread.Sleep(25);
        ClientService svc = new ClientService(gw.ClientEndPoint!, () => new Bot(), new NetServiceOptions { MaxSessionCount = bots * 4 });
        long end = Environment.TickCount64 + seconds * 1000L;
        long actions = 0, reconnects = 0;
        Thread[] th = new Thread[bots];
        for (int b = 0; b < bots; ++b)
        {
            int id = b;
            th[b] = new Thread(() =>
            {
                Random rnd = new Random(id * 7919 + 1);
                ulong charId = (ulong)(50000 + id);
                Bot? bot = null; bool inZone = false;
                byte[] junk = new byte[64];
                while (Environment.TickCount64 < end)
                {
                    try
                    {
                        if (bot == null || !bot.IsConnected)
                        {
                            bot = (Bot)svc.ConnectAsync().GetAwaiter().GetResult(); inZone = false; Interlocked.Increment(ref reconnects);
                            bot.Tx(new GwLoginReq { CharId = charId }); Thread.Sleep(30);
                        }
                        Interlocked.Increment(ref actions);
                        int a = rnd.Next(100);
                        ulong other = (ulong)(50000 + rnd.Next(bots));
                        if (!inZone || a < 2) { bot.Tx(new GwEnterZoneReq { ZoneId = (uint)rnd.Next(1, 4), HeroId = (uint)rnd.Next(0, 6) }); inZone = true; }
                        else if (a < 30) bot.Tx(new ZcMoveReq { PosX = 500, PosY = 500, DestX = (float)rnd.NextDouble() * 1000, DestY = (float)rnd.NextDouble() * 1000 });
                        else if (a < 45) bot.Tx(new ZcAttackReq { TargetId = rnd.Next(2) == 0 ? other : (0x8000000000000000UL | (ulong)rnd.Next(1, 700)) });
                        else if (a < 62) bot.Tx(new ZcSkillReq { SkillId = (uint)rnd.Next(0, 40), TargetId = rnd.Next(2) == 0 ? other : (0x8000000000000000UL | (ulong)rnd.Next(1, 700)), X = (float)rnd.NextDouble() * 1000, Y = (float)rnd.NextDouble() * 1000 });
                        else if (a < 72) bot.Tx(new ZcDuelReq { Action = (byte)rnd.Next(0, 6), TargetId = other });
                        else if (a < 76) bot.Tx(new ZcPickupReq { EntityId = 0x4000000000000000UL | (ulong)rnd.Next(1, 100) });
                        else if (a < 80) bot.Tx(new ZcPingReq { Seq = (uint)rnd.Next(), ClientMs = (ulong)rnd.Next() });
                        else if (a < 84) bot.Tx(new GwLeaveZoneReq());
                        else if (a < 87) { bot.Disconnect(CloseReason.Local); bot = null; }
                        else if (a < 93)
                        {
                            // 쓰레기: 올바른 크기 머리말 + 무작위 몸통. 타입은 알려진 요청/알 수 없는 값 섞기
                            int len = rnd.Next(4, 64); rnd.NextBytes(junk);
                            ushort type = rnd.Next(3) == 0 ? (ushort)rnd.Next(2000, 2100) : (ushort)new int[] { 2001, 2003, 2005, 2009, 2013, 2021, 2030 }[rnd.Next(7)];
                            byte[] f = new byte[len]; Array.Copy(junk, f, len); f[0] = (byte)len; f[1] = (byte)(len >> 8); f[2] = (byte)type; f[3] = (byte)(type >> 8);
                            bot.Send(f);
                        }
                        else bot.Tx(new ZcChatReq { Channel = (byte)rnd.Next(0, 5), TargetId = other });
                    }
                    catch (Exception) { bot = null; }
                    Thread.Sleep(rnd.Next(20, 120));
                }
                try { bot?.Disconnect(CloseReason.Local); } catch (Exception) { }
            });
            th[b].Start();
        }
        foreach (Thread t in th) t.Join();
        Thread.Sleep(4000);       // 모든 연결이 끊긴 뒤 서버가 정리할 시간(상태 보관 마감 2초 포함)
        Console.WriteLine($"chaos bots={bots} sec={seconds} actions={actions} connects={reconnects}");
        int bad = 0;
        for (int i = 0; i < 3; ++i)
        {
            ZoneStats st = zs[i].Zone!.GetStats();
            int players = (int)OnZone(zs[i], zz => zz.PlayerCount);
            Console.WriteLine($"  zone {i + 1}: ticks={st.TickCount} avg={st.TotalTickUs / Math.Max(1UL, st.TickCount)}us max={st.MaxTickUs}us overrun={st.OverrunTicks} skipped={st.SkippedTicks} failedJobs={st.FailedJobs} rejectedJobs={st.RejectedJobs} players={players}");
            if (st.FailedJobs != 0 || players != 0) ++bad;
            DumpCounts("zone" + (i + 1), zs[i].Zone!);
        }
        DumpCounts("gateway", gw);
        Console.WriteLine(bad == 0 ? "  invariants OK" : "  INVARIANT VIOLATION");
        svc.Close();
        foreach (ZoneServerMain z in zs) z.Dispose();
        return bad;
    }

    /// <summary>연결은 받지만 등록에 응답하지 않는 Gateway.</summary>
    sealed class SilentSession : Session
    {
        public static int Connected;
        protected override void OnConnected() => Interlocked.Increment(ref Connected);
        protected override int OnRecv(ReadOnlySpan<byte> buffer) => buffer.Length;
    }

    static void SilentGateway()
    {
        ServerService silent = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new SilentSession());
        Check(silent.Start(), "silent gateway starts");
        using ZoneServerMain z = new ZoneServerMain();
        try
        {
            Check(z.Start(new ZoneServerOptions { ZoneId = 9, GatewayIp = "127.0.0.1", GatewayPort = (ushort)silent.LocalEndPoint!.Port, GameDataDir = "", RegisterTimeoutMs = 300, MetricsIntervalMs = 0 }), "zone starts against a silent gateway");
            // 응답이 없으면 제한 시간 뒤에 연결을 끊고 백오프 뒤에 다시 접속한다(2회 이상 접속).
            for (int i = 0; i < 400 && Volatile.Read(ref SilentSession.Connected) < 2; ++i) Thread.Sleep(25);
            Check(Volatile.Read(ref SilentSession.Connected) >= 2, "zone drops the link after the register timeout and reconnects");
            Check(!z.IsRegistered, "zone stays unregistered while the gateway is silent");
        }
        finally
        {
            z.Dispose();
            silent.Close();
        }
    }

    // 같은 존 ID 의 이전 인스턴스가 아직 살아 있는 동안 뜬 인스턴스는 바로 종료하지 않고 재시도하며, 이전 인스턴스가 사라지면 등록된다.
    static void DuplicateZoneRetry()
    {
        using GatewayServerMain gw = new GatewayServerMain();
        Check(gw.Start(new GatewayOptions { ClientBindIp = "127.0.0.1", ClientPort = 0, ZoneBindIp = "127.0.0.1", ZonePort = 0, HeartbeatIntervalMs = 200 }), "gateway starts (duplicate zone test)");
        ushort port = (ushort)gw.ZoneEndPoint!.Port;
        ZoneServerMain a = new ZoneServerMain();
        using ZoneServerMain b = new ZoneServerMain();
        try
        {
            Check(a.Start(new ZoneServerOptions { ZoneId = 5, GatewayIp = "127.0.0.1", GatewayPort = port, GameDataDir = "", MetricsIntervalMs = 0 }), "zone A starts");
            for (int i = 0; i < 200 && !a.IsRegistered; ++i) Thread.Sleep(25);
            Check(a.IsRegistered, "zone A registered");

            Check(b.Start(new ZoneServerOptions { ZoneId = 5, GatewayIp = "127.0.0.1", GatewayPort = port, GameDataDir = "", MetricsIntervalMs = 0, DuplicateRetryMs = 30000 }), "zone B (same id) starts");
            Thread.Sleep(2500);
            Check(!b.IsRegistered && !b.IsRegisterRejected, "zone B keeps retrying while A holds the id");

            a.Dispose();
            for (int i = 0; i < 400 && !b.IsRegistered; ++i) Thread.Sleep(25);
            Check(b.IsRegistered && !b.IsRegisterRejected, "zone B registers after A is gone");
        }
        finally
        {
            a.Dispose();
        }

        // 재시도 시간이 0 이면 예전처럼 바로 거부로 끝난다.
        using ZoneServerMain c = new ZoneServerMain();
        using ZoneServerMain d = new ZoneServerMain();
        Check(c.Start(new ZoneServerOptions { ZoneId = 6, GatewayIp = "127.0.0.1", GatewayPort = port, GameDataDir = "", MetricsIntervalMs = 0 }), "zone C starts");
        for (int i = 0; i < 200 && !c.IsRegistered; ++i) Thread.Sleep(25);
        Check(d.Start(new ZoneServerOptions { ZoneId = 6, GatewayIp = "127.0.0.1", GatewayPort = port, GameDataDir = "", MetricsIntervalMs = 0, DuplicateRetryMs = 0 }), "zone D (same id, no retry) starts");
        for (int i = 0; i < 200 && !d.IsRegisterRejected; ++i) Thread.Sleep(25);
        Check(d.IsRegisterRejected, "with DuplicateRetryMs=0 the duplicate is rejected at once");
    }

    sealed class FakeConfig : IServerConfig<int>
    {
        public bool Init(string path) => true;
        public bool Save(string path) => true;
        public void PrintInfo() { }
        public int ToOptions() => 0;
    }

    sealed class FakeHost : IServerHost
    {
        public void Stop() { }
        public void Dispose() { }
    }

    // 서버가 스스로 중단을 요청하면 종료 코드는 실패(-1)여야 한다. 프로세스 관리자가 정상 종료로 보고 재시작을 멈추지 않도록.
    static void AbortExitCode()
    {
        int code = ServerMain.Run<int>(new string[0], "none.json", () => new FakeConfig(), (_, _) => true, _ => new FakeHost(), _ => "abort requested by test");
        Check(code == -1, "ServerMain.Run returns -1 when the server aborts itself");
        int startFail = ServerMain.Run<int>(new string[0], "none.json", () => new FakeConfig(), (_, _) => true, _ => null);
        Check(startFail == -1, "ServerMain.Run returns -1 when start fails");
    }

    public static int Main(string[] args)
    {
        AbortExitCode();
        SilentGateway();
        DuplicateZoneRetry();
        if (args.Length > 0 && args[0] == "chaos") return Chaos(args.Length > 1 ? int.Parse(args[1]) : 60, args.Length > 2 ? int.Parse(args[2]) : 20);
        if (args.Length > 0 && args[0] == "load") return Load(args.Length > 1 ? int.Parse(args[1]) : 100, args.Length > 2 ? int.Parse(args[2]) : 10);
        string realData = FindData();
        string data = Path.Combine(Path.GetTempPath(), "travel_tests_data_" + Environment.ProcessId);
        Directory.CreateDirectory(data);
        foreach (string f in Directory.GetFiles(realData, "*.json")) File.Copy(f, Path.Combine(data, Path.GetFileName(f)), true);
        // 존별 규칙 시험용: 1 = 결투만, 2 = 결투 불가, 3 = 자유 PvP(결투 불가)
        File.WriteAllText(Path.Combine(data, "ZoneRule.json"), "{\"ZoneRule\":[{\"ZoneId\":1,\"AllowPvp\":false,\"AllowDuel\":true},{\"ZoneId\":2,\"AllowPvp\":false,\"AllowDuel\":false},{\"ZoneId\":3,\"AllowPvp\":true,\"AllowDuel\":false}]}");
        using GatewayServerMain gw = new GatewayServerMain();
        using ZoneServerMain z1 = new ZoneServerMain();
        using ZoneServerMain z2 = new ZoneServerMain();
        using ZoneServerMain z3 = new ZoneServerMain();
        ClientService? svc = null;
        try
        {
            Check(gw.Start(new GatewayOptions { ClientBindIp = "127.0.0.1", ClientPort = 0, ZoneBindIp = "127.0.0.1", ZonePort = 0, HeartbeatIntervalMs = 200, HeartbeatTimeoutMs = 5000 }), "gateway starts");
            int zp = gw.ZoneEndPoint!.Port;
            // 존 1: 동쪽 관문 안쪽(996,500)에서 입장한다. 존 2는 기본 위치.
            Check(z1.Start(new ZoneServerOptions { ZoneId = 1, Capacity = 50, TickIntervalMs = 10, GatewayIp = "127.0.0.1", GatewayPort = (ushort)zp, GameDataDir = data, SpawnX = 996, SpawnY = 500 }), "zone 1 starts (monsters, ZoneLink loaded)");
            Check(z2.Start(new ZoneServerOptions { ZoneId = 2, Capacity = 50, TickIntervalMs = 10, GatewayIp = "127.0.0.1", GatewayPort = (ushort)zp, GameDataDir = data, SpawnX = 100, SpawnY = 100 }), "zone 2 starts");
            Check(z3.Start(new ZoneServerOptions { ZoneId = 3, Capacity = 50, TickIntervalMs = 10, GatewayIp = "127.0.0.1", GatewayPort = (ushort)zp, GameDataDir = data, SpawnX = 100, SpawnY = 100 }), "zone 3 starts");
            for (int i = 0; i < 200 && !(z1.IsRegistered && z2.IsRegistered && z3.IsRegistered); ++i) Thread.Sleep(25);
            Check(z1.IsRegistered && z2.IsRegistered && z3.IsRegistered, "all zones registered");

            svc = new ClientService(gw.ClientEndPoint!, () => new Client(), new NetServiceOptions { MaxSessionCount = 16 });

            // ---- 1) 관문을 통한 존 이동
            Client a = Connect(svc);
            Check(Login(a, 7001), "login 7001");
            GwEnterZoneRes? e1 = Enter(a, 1, 1);
            Check(e1 != null && e1.Value.Success == 1, "enter zone 1");
            Check(e1 != null && Math.Abs(e1.Value.SpawnX - 996f) < 0.01f, "zone 1 spawn = test position (inside east gate)");

            int maxHp = OnZone(z1, zz => zz.FindEntityBase(7001)!.CombatStats.MaxHp);
            int hurt = OnZone(z1, zz =>
            {
                ZoneEntity p = zz.FindEntityBase(7001)!;
                zz.DealDamage(p, null, 37);
                ((ZonePlayer)p).Inventory.TryAdd(501, 3, out _);
                ((ZonePlayer)p).Inventory.TryAdd(777, 12, out _);
                return p.Hp;
            });
            Check(hurt == maxHp - 37, $"damage applied ({hurt} of {maxHp})");

            a.Tx(new GwLeaveZoneReq());
            Check(a.Take((ushort)GatewayPacketType.GwLeaveZoneRes) != null, "leave response");
            GwEnterZoneRes? e2 = Enter(a, 2, 1);            // 바로 이어서 입장: 존 1의 상태가 아직 안 왔어도 보류됐다가 이어진다
            Check(e2 != null && e2.Value.Success == 1, "enter zone 2 right after leaving");
            Check(e2 != null && Math.Abs(e2.Value.SpawnX - 14f) < 0.01f && Math.Abs(e2.Value.SpawnY - 500f) < 0.01f, $"arrival at gate target (14,500), got ({e2?.SpawnX},{e2?.SpawnY})");
            int hp2 = OnZone(z2, zz => zz.FindEntityBase(7001)?.Hp ?? -1);
            Check(hp2 == hurt, $"hp carried across zones (expected {hurt}, got {hp2})");
            (uint, uint) inv = OnZone(z2, zz => { ZonePlayer p = (ZonePlayer)zz.FindEntityBase(7001)!; return (p.Inventory.GetCount(501), p.Inventory.GetCount(777)); });
            Check(inv == (3u, 12u), $"inventory carried ({inv.Item1},{inv.Item2})");
            Vec2 spawnAfter = OnZone(z2, zz => zz.FindEntityBase(7001)!.SpawnPos);
            Check(Math.Abs(spawnAfter.X - 100f) < 0.01f, "respawn point stays the zone default");

            // ---- 2) 연결을 끊고 바로 다시 접속(존 2의 W 관문 밖이라 도착 지점은 없다)
            OnZone(z2, zz => { zz.DealDamage(zz.FindEntityBase(7001)!, null, 20); return 0; });
            int hp3 = OnZone(z2, zz => zz.FindEntityBase(7001)!.Hp);
            a.Disconnect(CloseReason.Local);
            Client b = Connect(svc);
            bool ok = false;
            for (int i = 0; i < 40 && !ok; ++i) { ok = Login(b, 7001); if (!ok) { b.Disconnect(CloseReason.Local); Thread.Sleep(50); b = Connect(svc); } }
            Check(ok, "relogin after disconnect");
            GwEnterZoneRes? e3 = Enter(b, 1, 1);
            Check(e3 != null && e3.Value.Success == 1, "re-enter zone 1");
            Check(e3 != null && Math.Abs(e3.Value.SpawnX - 996f) < 0.01f, "no stale arrival point (spawns at the zone's own start)");
            int hp4 = OnZone(z1, zz => zz.FindEntityBase(7001)?.Hp ?? -1);
            Check(hp4 == hp3, $"hp survives reconnect (expected {hp3}, got {hp4})");
            uint c501 = OnZone(z1, zz => ((ZonePlayer)zz.FindEntityBase(7001)!).Inventory.GetCount(501));
            Check(c501 == 3, $"inventory survives reconnect ({c501})");

            // ---- 3) 새 캐릭터는 가득 찬 체력
            Client n = Connect(svc);
            Check(Login(n, 7002), "login 7002");
            Check(Enter(n, 1, 1)?.Success == 1, "enter zone 1 (new char)");
            int hpN = OnZone(z1, zz => zz.FindEntityBase(7002)!.Hp);
            Check(hpN == maxHp, $"new character starts at full hp ({hpN}/{maxHp})");

            // ---- 3b) 연결을 끊은 클라이언트의 보류 입장이 정리되고, 같은 캐릭터가 다시 접속해도 정상 입장한다
            Client d = Connect(svc);
            Check(Login(d, 7003), "login 7003");
            Check(Enter(d, 1, 1)?.Success == 1, "7003 enter");
            d.Tx(new GwLeaveZoneReq());
            d.Tx(new GwEnterZoneReq { ZoneId = 2, HeroId = 1 });
            d.Tx(new GwEnterZoneReq { ZoneId = 2, HeroId = 1 });          // 보류 중 중복 입장: 한 번은 응답을 받아야 한다
            int responses = 0; byte[]? rr;
            while ((rr = d.Take((ushort)GatewayPacketType.GwEnterZoneRes, 3000)) != null) { ++responses; if (responses >= 2) break; }
            Check(responses >= 1, "duplicate enter during park gets a response");
            d.Disconnect(CloseReason.Local);

            // ---- 5) 결투: 신청/수락 전에는 서로 공격할 수 없고, 수락 뒤 시작하면 기존 전투 코드로 싸운다
            Client p1 = Connect(svc), p2 = Connect(svc);
            Check(Login(p1, 7101) && Login(p2, 7102), "login duelists");
            Check(Enter(p1, 1, 1)?.Success == 1 && Enter(p2, 1, 1)?.Success == 1, "duelists enter zone 1");
            Func<ulong, int> hpOf = id => OnZone(z1, zz => zz.FindEntityBase(id)?.Hp ?? -1);
            int hp2Before = hpOf(7102);
            p1.Tx(new ZcAttackReq { TargetId = 7102 });
            Thread.Sleep(300);
            Check(hpOf(7102) == hp2Before, "no pvp without a duel");

            DuelTx(p1, DuelAction.Request, 7102);
            Check(DuelRx(p1, DuelState.RequestSent) != null, "challenger gets RequestSent");
            Check(DuelRx(p2, DuelState.Requested)?.OpponentId == 7101, "target gets Requested from 7101");
            DuelTx(p2, DuelAction.Accept, 7101);
            Check(DuelRx(p1, DuelState.Countdown) != null && DuelRx(p2, DuelState.Countdown) != null, "countdown starts for both");
            p1.Tx(new ZcAttackReq { TargetId = 7102 });             // 카운트다운 중에는 아직 공격 불가
            Thread.Sleep(300);
            Check(hpOf(7102) == hp2Before, "no damage during countdown");
            Check(DuelRx(p1, DuelState.Started, 6000) != null && DuelRx(p2, DuelState.Started, 3000) != null, "duel starts after countdown");
            for (int i = 0; i < 5 && hpOf(7102) == hp2Before; ++i) { p1.Tx(new ZcAttackReq { TargetId = 7102 }); Thread.Sleep(400); }
            Check(hpOf(7102) < hp2Before, "damage lands on the duel opponent (existing combat code)");
            Client third = Connect(svc);
            Check(Login(third, 7103) && Enter(third, 1, 1)?.Success == 1, "third player enters");
            int hp3Before = hpOf(7103);
            for (int i = 0; i < 3; ++i) { p1.Tx(new ZcAttackReq { TargetId = 7103 }); Thread.Sleep(400); }
            Check(hpOf(7103) == hp3Before, "duelist cannot hit a bystander");
            DuelTx(third, DuelAction.Request, 7101);
            Check(DuelRx(third, DuelState.RejectBusy) != null, "busy duelist cannot be challenged");

            OnZone(z1, zz => { ZoneEntity v = zz.FindEntityBase(7102)!; zz.DealDamage(v, zz.FindEntityBase(7101), v.Hp + 1000); return 0; });
            ZcDuelNotify? ended = DuelRx(p1, DuelState.Ended);
            Check(ended != null && ended.Value.WinnerId == 7101, "duel ends with the killer as winner");
            Check(DuelRx(p2, DuelState.Ended)?.WinnerId == 7101, "loser is told the same winner");

            // 거절 / 기권 / 떠남
            Thread.Sleep(300);
            DuelTx(p1, DuelAction.Request, 7103);
            Check(DuelRx(third, DuelState.Requested) != null, "request to 7103");
            DuelTx(third, DuelAction.Decline, 7101);
            Check(DuelRx(p1, DuelState.Declined) != null, "decline reaches challenger");
            DuelTx(p1, DuelAction.Request, 7103);
            DuelRx(third, DuelState.Requested);
            DuelTx(third, DuelAction.Accept, 7101);
            Check(DuelRx(p1, DuelState.Started, 6000) != null, "second duel starts");
            DuelTx(third, DuelAction.Cancel, 0);
            ZcDuelNotify? ff = DuelRx(p1, DuelState.Cancelled);
            Check(ff != null && ff.Value.WinnerId == 7101, "forfeit gives the win to the other side");
            DuelTx(p1, DuelAction.Request, 7103);
            DuelRx(third, DuelState.Requested);
            third.Tx(new GwLeaveZoneReq());
            Check(DuelRx(p1, DuelState.OpponentLeft) != null, "leaving zone ends the pending duel");
            DuelTx(p1, DuelAction.Request, 999999);
            Check(DuelRx(p1, DuelState.RejectNoTarget) != null, "unknown target is rejected");

            // ---- 6) 존별 규칙: 입장 시 규칙 통지, 결투 불가 존의 거부, 자유 PvP 존의 직접 공격
            Client r1 = Connect(svc);
            Check(Login(r1, 7201), "login 7201");
            Check(Enter(r1, 2, 1)?.Success == 1, "7201 enters zone 2 (duel off)");
            Check(DuelRx(r1, DuelState.ZoneRules)?.RemainMs == 0, "zone 2 reports no pvp, no duel");
            Client r2 = Connect(svc);
            Check(Login(r2, 7202) && Enter(r2, 2, 1)?.Success == 1, "7202 enters zone 2");
            DuelTx(r1, DuelAction.Request, 7202);
            Check(DuelRx(r1, DuelState.RejectNotAllowed) != null, "duel refused in a no-duel zone");
            int hpSafe = OnZone(z2, zz => zz.FindEntityBase(7202)!.Hp);
            for (int i = 0; i < 4; ++i) { r1.Tx(new ZcAttackReq { TargetId = 7202 }); Thread.Sleep(400); }
            Check(OnZone(z2, zz => zz.FindEntityBase(7202)!.Hp) == hpSafe, "safe zone (no pvp, no duel): players cannot hurt each other"); 
            r1.Tx(new GwLeaveZoneReq()); r1.Take((ushort)GatewayPacketType.GwLeaveZoneRes);
            r2.Tx(new GwLeaveZoneReq()); r2.Take((ushort)GatewayPacketType.GwLeaveZoneRes);
            Check(Enter(r1, 3, 1)?.Success == 1 && Enter(r2, 3, 1)?.Success == 1, "both enter zone 3 (open pvp)");
            Check(DuelRx(r1, DuelState.ZoneRules)?.RemainMs == 1, "zone 3 reports pvp only");
            int hpR2 = OnZone(z3, zz => zz.FindEntityBase(7202)!.Hp);
            for (int i = 0; i < 5 && OnZone(z3, zz => zz.FindEntityBase(7202)!.Hp) == hpR2; ++i) { r1.Tx(new ZcAttackReq { TargetId = 7202 }); Thread.Sleep(400); }
            Check(OnZone(z3, zz => zz.FindEntityBase(7202)!.Hp) < hpR2, "free pvp zone lets players attack without a duel");
            Check(ZoneRuleTable.TryLoad(realData, 10, out ZoneRuleEntry? real10) && real10 != null && real10.AllowPvp && !real10.AllowDuel, "real data: zone 10 is the open pvp zone");
            Check(ZoneRuleTable.TryLoad(realData, 1, out ZoneRuleEntry? real1) && real1 != null && !real1.AllowPvp && !real1.AllowDuel, "real data: zone 1 is a safe zone (no pvp, no duel)");
            Check(ZoneRuleTable.TryLoad(realData, 2, out ZoneRuleEntry? real2) && real2 != null && !real2.AllowPvp && real2.AllowDuel, "real data: zone 2 is duel only");
            Check(ZoneRuleTable.TryLoad(realData, 999, out ZoneRuleEntry? none) && none == null, "no row = defaults");

            // ---- 7) 데이터 파일 검증: 존 규칙/관문 파일은 다른 게임 데이터처럼 엄격하게 읽는다
            string bad = Path.Combine(Path.GetTempPath(), "travel_tests_bad_" + Environment.ProcessId);
            Directory.CreateDirectory(bad);
            try
            {
                File.WriteAllText(Path.Combine(bad, "ZoneRule.json"), "{\"ZoneRule\":[{\"ZoneId\":1,\"AllowPvp\":false,\"AllowDuel\":true,\"Extra\":1}]}");
                Check(!ZoneRuleTable.TryLoad(bad, 1, out _), "zone rule: unknown key is rejected");
                File.WriteAllText(Path.Combine(bad, "ZoneRule.json"), "{\"ZoneRule\":[{\"ZoneId\":1,\"AllowPvp\":false,\"AllowDuel\":true},{\"ZoneId\":1,\"AllowPvp\":true,\"AllowDuel\":true}]}");
                Check(!ZoneRuleTable.TryLoad(bad, 1, out _), "zone rule: duplicate zone row is rejected");
                Check(ZoneRuleTable.TryLoad(bad, 2, out ZoneRuleEntry? other2) && other2 == null, "zone rule: other zone ignores the file");
                File.WriteAllText(Path.Combine(bad, "ZoneRule.json"), "{\"ZoneRule\":[{\"ZoneId\":1,\"AllowPvp\":\"yes\",\"AllowDuel\":true}]}");
                Check(!ZoneRuleTable.TryLoad(bad, 1, out _), "zone rule: wrong type is rejected");
                File.WriteAllText(Path.Combine(bad, "ZoneLink.json"), "{\"ZoneLink\":[{\"ZoneId\":1,\"Edge\":\"X\",\"Pos\":500,\"Width\":24,\"TargetZone\":2,\"TargetX\":14,\"TargetY\":500}]}");
                Check(ZoneLinkTable.Load(bad, 1) == null, "zone link: bad edge is rejected");
                File.WriteAllText(Path.Combine(bad, "ZoneLink.json"), "{\"ZoneLink\":[{\"ZoneId\":1,\"Edge\":\"E\",\"Pos\":500,\"Width\":0,\"TargetZone\":2,\"TargetX\":14,\"TargetY\":500}]}");
                Check(ZoneLinkTable.Load(bad, 1) == null, "zone link: zero width is rejected");
                Check(ZoneLinkTable.Load(realData, 1)?.Count > 0, "zone link: real data loads");
            }
            finally { Directory.Delete(bad, true); }

            // ---- 4) 다른 영웅으로 다시 들어오면 체력은 이어가지 않는다(가방은 유지)
            b.Tx(new GwLeaveZoneReq()); b.Take((ushort)GatewayPacketType.GwLeaveZoneRes);
            Check(Enter(b, 1, 3)?.Success == 1, "re-enter with another hero");
            int maxHp3 = OnZone(z1, zz => zz.FindEntityBase(7001)!.CombatStats.MaxHp);
            int hp5 = OnZone(z1, zz => zz.FindEntityBase(7001)!.Hp);
            Check(hp5 == maxHp3, $"other hero starts full ({hp5}/{maxHp3})");
            Check(OnZone(z1, zz => ((ZonePlayer)zz.FindEntityBase(7001)!).Inventory.GetCount(777)) == 12, "inventory kept across hero change");
        }
        catch (Exception ex) { ++s_fail; Console.WriteLine("EXCEPTION: " + ex); }
        finally { svc?.Close(); try { Directory.Delete(data, true); } catch (IOException) { } }
        Console.WriteLine($"passed {s_pass}, failed {s_fail}");
        return s_fail == 0 ? 0 : 1;
    }
}
