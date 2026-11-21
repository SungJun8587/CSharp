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
using System.Threading.Tasks;
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

    // 0 번 존(시험장): 존 서버가 0 번으로 등록하고, 클라이언트가 0 번 입장을 요청하면 0 번으로 답이 오고, 존 1 과 0 을 오갈 수 있다.
    static void TestZoneZero()
    {
        string data = FindData();
        using GatewayServerMain gw = new GatewayServerMain();
        using ZoneServerMain z0 = new ZoneServerMain();
        using ZoneServerMain z1 = new ZoneServerMain();
        gw.Start(new GatewayOptions { ClientBindIp = "127.0.0.1", ClientPort = 0, ZoneBindIp = "127.0.0.1", ZonePort = 0, ClientMaxPacketsPerSec = 0 });
        ushort port = (ushort)gw.ZoneEndPoint!.Port;
        Check(z0.Start(new ZoneServerOptions { ZoneId = 0, Capacity = 50, TickIntervalMs = 50, GatewayIp = "127.0.0.1", GatewayPort = port, GameDataDir = data, MetricsIntervalMs = 0 }), "zone 0 starts");
        Check(z1.Start(new ZoneServerOptions { ZoneId = 1, Capacity = 50, TickIntervalMs = 50, GatewayIp = "127.0.0.1", GatewayPort = port, GameDataDir = data, MetricsIntervalMs = 0 }), "zone 1 starts");
        for (int i = 0; i < 200 && !(z0.IsRegistered && z1.IsRegistered); ++i) Thread.Sleep(25);
        Check(z0.IsRegistered, "zone 0 registers with the gateway");
        using ClientService svc = new ClientService(gw.ClientEndPoint!, () => new Client(), new NetServiceOptions { MaxSessionCount = 4 });
        Client c = Connect(svc);
        Check(Login(c, 777001), "login (zone 0 test)");
        GwEnterZoneRes? r = Enter(c, 0, 1);
        Check(r != null && r.Value.Success == 1 && r.Value.ZoneId == 0, "enter zone 0 succeeds and the answer says zone 0");
        Check(OnZone(z0, zz => zz.PlayerCount) == 1, "the player is on the zone 0 server");
        int kinds = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "Monster.json"))).RootElement.GetProperty("Monster").GetArrayLength();
        int bosses = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "Boss.json"))).RootElement.GetProperty("Boss").GetArrayLength();
        Check(OnZone(z0, zz => zz.MonsterCount) == (kinds - bosses) * 3 + bosses, "zone 0 spawns every regular monster x3 and every boss x1 from ZoneRule alone");
        c.Tx(new GwLeaveZoneReq());
        c.Take((ushort)GatewayPacketType.GwLeaveZoneRes);
        GwEnterZoneRes? r1 = Enter(c, 1, 1);
        Check(r1 != null && r1.Value.Success == 1 && r1.Value.ZoneId == 1, "then zone 1 still works");
        Check(OnZone(z0, zz => zz.PlayerCount) == 0 && OnZone(z1, zz => zz.PlayerCount) == 1, "players moved from zone 0 to zone 1");
    }

    // 시험장 지형 치트: 존 0 만 ZcTerrainReq 를 받아 장애물 맵을 바꾸고(존 전원에게 ZcTerrainNotify), 새 장애물 안에 있던 엔티티는 밖으로 옮긴다.
    // 다른 존은 요청을 무시한다. 바뀐 뒤 들어온 플레이어는 입장 때 현재 지형을 받는다.
    static void TestTerrainCheat()
    {
        string data = FindData();
        using GatewayServerMain gw = new GatewayServerMain();
        using ZoneServerMain z0 = new ZoneServerMain();
        using ZoneServerMain z1 = new ZoneServerMain();
        gw.Start(new GatewayOptions { ClientBindIp = "127.0.0.1", ClientPort = 0, ZoneBindIp = "127.0.0.1", ZonePort = 0, ClientMaxPacketsPerSec = 0 });
        ushort port = (ushort)gw.ZoneEndPoint!.Port;
        Check(z0.Start(new ZoneServerOptions { ZoneId = 0, Capacity = 50, TickIntervalMs = 50, GatewayIp = "127.0.0.1", GatewayPort = port, GameDataDir = data, MetricsIntervalMs = 0 }), "terrain: zone 0 starts");
        Check(z1.Start(new ZoneServerOptions { ZoneId = 1, Capacity = 50, TickIntervalMs = 50, GatewayIp = "127.0.0.1", GatewayPort = port, GameDataDir = data, MetricsIntervalMs = 0 }), "terrain: zone 1 starts");
        for (int i = 0; i < 200 && !(z0.IsRegistered && z1.IsRegistered); ++i) Thread.Sleep(25);
        using ClientService svc = new ClientService(gw.ClientEndPoint!, () => new Client(), new NetServiceOptions { MaxSessionCount = 8 });

        Check(GameDataLoader.LoadBlockMap(data, 1, 0, 0, 1000, 1000, out BlockMap? map1) && map1 != null, "terrain: map 1 loads");
        Check(GameDataLoader.LoadBlockMap(data, 5, 0, 0, 1000, 1000, out BlockMap? map5) && map5 != null, "terrain: map 5 loads");
        Vec2 inside5 = new Vec2 { X = 796.4f, Y = 525.8f };                 // 지형 5 의 첫 장애물 안(지형 1 에서는 비어 있어야 한다)
        Check(map5!.IsBlocked(inside5) && !map1!.IsBlocked(inside5), "terrain: the probe point is blocked only in terrain 5");

        Client a = Connect(svc);
        Check(Login(a, 777101) && Enter(a, 0, 1)?.Success == 1, "terrain: client A enters zone 0");
        int blocked0 = OnZone(z0, zz => zz.BlockMap!.BlockedCellCount);
        Check(blocked0 == map1!.BlockedCellCount, "terrain: zone 0 starts with the ZoneRule terrain (1)");
        Check(OnZone(z0, zz => zz.Teleport(777101UL, inside5)), "terrain: A is placed where terrain 5 has an obstacle");

        a.Tx(new ZcTerrainReq { Terrain = 5 });
        byte[]? n = a.Take(TerrainPacketType.Notify);
        Check(n != null && ZcTerrainNotify.TryRead(n, out ZcTerrainNotify tn) && tn.Terrain == 5, "terrain: the zone answers with ZcTerrainNotify(5)");
        Check(OnZone(z0, zz => zz.BlockMap!.BlockedCellCount) == map5.BlockedCellCount, "terrain: zone 0 now uses the terrain 5 obstacles");
        Vec2 pos = OnZone(z0, zz => zz.FindEntity(777101UL)!.Pos);
        Check(!map5.IsBlocked(pos) && Vec2.DistanceSq(pos, inside5) < 80.0f * 80.0f, "terrain: A was moved out of the new obstacle");
        byte[]? fix = a.Take((ushort)ZonePacketType.ZcCorrectPosNotify, 2000);
        Check(fix != null, "terrain: A got a position correction");

        Client b = Connect(svc);
        Check(Login(b, 777102) && Enter(b, 0, 1)?.Success == 1, "terrain: client B enters zone 0 later");
        byte[]? nb = b.Take(TerrainPacketType.Notify, 3000);
        Check(nb != null && ZcTerrainNotify.TryRead(nb, out ZcTerrainNotify tb) && tb.Terrain == 5, "terrain: a late entrant is told the current terrain");

        a.Tx(new ZcTerrainReq { Terrain = 3 });                              // 쿨다운(500ms) 안의 연타는 무시한다
        Thread.Sleep(700);
        a.Tx(new ZcTerrainReq { Terrain = 3 });
        Check(a.Take(TerrainPacketType.Notify, 3000) != null && b.Take(TerrainPacketType.Notify, 3000) != null, "terrain: a change reaches everyone in the zone");

        a.Tx(new ZcTerrainReq { Terrain = 99 });
        Thread.Sleep(700);
        Check(OnZone(z0, zz => zz.BlockMap != null), "terrain: an unknown terrain keeps the current map");

        Client c = Connect(svc);
        Check(Login(c, 777103) && Enter(c, 1, 1)?.Success == 1, "terrain: client C enters zone 1");
        c.Tx(new ZcTerrainReq { Terrain = 4 });
        Check(c.Take(TerrainPacketType.Notify, 1000) == null, "terrain: other zones ignore the cheat");
    }

    // 저장 실패를 흉내 내는 저장소: FailAll 이면 모두, PoisonId 는 항상 실패한다.
    sealed class FlakyStore : Game.Persistence.IPlayerStore
    {
        public readonly Game.Persistence.InMemoryPlayerStore Inner = new Game.Persistence.InMemoryPlayerStore();
        public volatile bool FailAll;
        public int FailNext;                 // 앞으로 이 횟수만큼의 저장 호출을 실패시킨다
        public ulong PoisonId;
        public int SaveCalls;
        public Task<PlayerState?> LoadAsync(ulong id, CancellationToken ct) => FailAll ? Task.FromException<PlayerState?>(new InvalidOperationException("db down")) : Inner.LoadAsync(id, ct);
        public Task SaveAsync(PlayerState s, CancellationToken ct)
        {
            Interlocked.Increment(ref SaveCalls);
            if (FailAll || s.CharId == PoisonId || Interlocked.Decrement(ref FailNext) >= 0) return Task.FromException(new InvalidOperationException("db error"));
            return Inner.SaveAsync(s, ct);
        }
        public Task SaveBatchAsync(IReadOnlyList<PlayerState> states, CancellationToken ct)
        {
            Interlocked.Increment(ref SaveCalls);
            if (FailAll || states.Any(x => x.CharId == PoisonId) || Interlocked.Decrement(ref FailNext) >= 0) return Task.FromException(new InvalidOperationException("db error"));
            return Inner.SaveBatchAsync(states, ct);
        }
    }

    static PlayerState PS(ulong id, int hp) { PlayerState p = new PlayerState { CharId = id, HeroId = 1, HasVitals = true, Hp = hp, Mp = 1 }; p.Items.Add(new KeyValuePair<uint, uint>(1001, 3)); return p; }

    static bool WaitFor(Func<bool> f, int ms = 5000) { long end = Environment.TickCount64 + ms; while (Environment.TickCount64 < end) { if (f()) return true; Thread.Sleep(10); } return f(); }

    // 경험치/레벨: 표 검사, 저장된 레벨 복원, 처치 경험치, 레벨 업(능력치 재계산 + 완전 회복), 퇴장 시 저장.
    static void TestLevel()
    {
        string data = FindData();
        LevelData bad = new LevelData();
        Check(!bad.Validate(out _), "an empty level table is invalid");
        Check(!bad.AddLevel(new LevelData.Row { Level = 2, ExpToNext = 10, StatPercent = 100 }), "levels must start at 1");
        bad.AddLevel(new LevelData.Row { Level = 1, ExpToNext = 10, StatPercent = 110 });
        Check(!bad.Validate(out _), "level 1 must have statPercent 100");

        LevelData lv = new LevelData();
        lv.AddLevel(new LevelData.Row { Level = 1, ExpToNext = 10, StatPercent = 100 });
        lv.AddLevel(new LevelData.Row { Level = 2, ExpToNext = 20, StatPercent = 150 });
        lv.AddLevel(new LevelData.Row { Level = 3, ExpToNext = 0, StatPercent = 200 });
        Check(lv.Validate(out _) && lv.MaxLevel == 3, "a consecutive table validates");
        CombatStats cs = CombatStats.Default; cs.MaxHp = 1000; cs.Attack = 10; cs.AttackRange = 2.5f; cs.AttackIntervalMs = 800;
        CombatStats sc = lv.Scale(cs, 2);
        Check(sc.MaxHp == 1500 && sc.Attack == 15 && sc.AttackRange == 2.5f && sc.AttackIntervalMs == 800, "scale multiplies hp/attack only");
        Check(lv.Scale(cs, 99).MaxHp == 2000 && lv.ClampLevel(0) == 1, "out-of-range levels are clamped");

        PlayerState w = PS(1, 10); w.Level = 7; w.Exp = 123456;
        Span<byte> buf = stackalloc byte[ZoneStateSave.PacketSize];
        new ZoneStateSave { State = w }.Write(buf);
        Check(ZoneStateSave.TryRead(buf, out ZoneStateSave back) && back.State.Level == 7 && back.State.Exp == 123456 && back.State.Items.Count == 1 && back.State.Items[0].Value == 3, "level/exp survive the wire format");

        ZcExpNotify en = new ZcExpNotify { Level = 5, Flags = ZcExpNotify.FlagLevelUp, Exp = 9, ExpToNext = 99, Gained = 4 };
        Span<byte> eb = stackalloc byte[ZcExpNotify.Size];
        en.Write(eb);
        Span<byte> cb = stackalloc byte[ZcCurrencyNotify.Size];
        new ZcCurrencyNotify { CurrencyId = 1, Amount = 77, Gained = 5 }.Write(cb);
        Check(ZcCurrencyNotify.TryRead(cb, out ZcCurrencyNotify c2) && c2.CurrencyId == 1 && c2.Amount == 77 && c2.Gained == 5, "ZcCurrencyNotify round trip");
        PlayerState ws = PS(2, 10); ws.Wallet.Add(new KeyValuePair<uint, uint>(1, 123456)); ws.Wallet.Add(new KeyValuePair<uint, uint>(2, 9));
        Span<byte> wb = stackalloc byte[ZoneStateSave.PacketSize];
        new ZoneStateSave { State = ws }.Write(wb);
        Check(ZoneStateSave.TryRead(wb, out ZoneStateSave wback) && wback.State.Wallet.Count == 2 && wback.State.Wallet[0].Value == 123456 && wback.State.Wallet[1].Key == 2 && wback.State.Items.Count == 1, "wallet survives the wire format");
        CurrencyData cd = new CurrencyData();
        cd.AddCurrency(new CurrencyData.Currency { Id = 1, Name = "gold", MaxAmount = 100 });
        cd.AddReward(new CurrencyData.Reward { MonsterId = 1, CurrencyId = 1, MinAmount = 5, MaxAmount = 50, ChancePermille = 500 });
        Check(cd.Validate(out _), "a valid currency table validates");
        cd.AddReward(new CurrencyData.Reward { MonsterId = 2, CurrencyId = 1, MinAmount = 5, MaxAmount = 500, ChancePermille = 500 });
        Check(!cd.Validate(out _), "a reward above the currency cap is invalid");
        CurrencyData cd2 = new CurrencyData();
        cd2.AddCurrency(new CurrencyData.Currency { Id = 1, Name = "gold", MaxAmount = 100 });
        cd2.AddReward(new CurrencyData.Reward { MonsterId = 1, CurrencyId = 9, MinAmount = 1, MaxAmount = 2, ChancePermille = 1000 });
        Check(!cd2.Validate(out _), "a reward of an unknown currency is invalid");
        CurrencyData cd3 = new CurrencyData();
        cd3.AddCurrency(new CurrencyData.Currency { Id = 2, Name = "blue", MaxAmount = 100, Paid = true });
        Check(cd3.IsPaid(2) && !cd3.IsPaid(1) && cd3.Validate(out _), "a paid currency is flagged and validates without rewards");
        cd3.AddReward(new CurrencyData.Reward { MonsterId = 1, CurrencyId = 2, MinAmount = 1, MaxAmount = 2, ChancePermille = 1000 });
        Check(!cd3.Validate(out _), "a paid currency cannot be a monster reward");
        Check(ZcExpNotify.TryRead(eb, out ZcExpNotify e2) && e2.Level == 5 && e2.Exp == 9 && e2.ExpToNext == 99 && e2.Gained == 4 && e2.Flags == ZcExpNotify.FlagLevelUp, "ZcExpNotify round trip");

        var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "Level.json"))).RootElement.GetProperty("Level");
        uint need3 = doc[2].GetProperty("ExpToNext").GetUInt32(), pct4 = doc[3].GetProperty("StatPercent").GetUInt32();
        uint slimeExp = 0;
        foreach (var r in System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "MonsterExp.json"))).RootElement.GetProperty("MonsterExp").EnumerateArray())
            if (r.GetProperty("MonsterId").GetUInt32() == 1) slimeExp = r.GetProperty("Exp").GetUInt32();
        Check(slimeExp > 0, "slime has an exp value");

        // 골드 확률이 1000분율이라 시험은 항상 주도록 확률만 1000으로 바꾼 사본을 쓴다.
        string cdata = Path.Combine(Path.GetTempPath(), "travel_tests_currency_" + Environment.ProcessId);
        Directory.CreateDirectory(cdata);
        foreach (string f in Directory.GetFiles(data, "*.json")) File.Copy(f, Path.Combine(cdata, Path.GetFileName(f)), true);
        File.WriteAllText(Path.Combine(cdata, "MonsterCurrency.json"), System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Path.Combine(data, "MonsterCurrency.json")), "\"ChancePermille\": \\d+", "\"ChancePermille\": 1000"));
        uint goldMin = 0, goldMax = 0;
        foreach (var r in System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "MonsterCurrency.json"))).RootElement.GetProperty("MonsterCurrency").EnumerateArray())
            if (r.GetProperty("MonsterId").GetUInt32() == 1) { goldMin = r.GetProperty("MinAmount").GetUInt32(); goldMax = r.GetProperty("MaxAmount").GetUInt32(); }
        Check(goldMax >= goldMin && goldMin > 0, "slime has a gold range");

        Game.Persistence.InMemoryPlayerStore store = new Game.Persistence.InMemoryPlayerStore();
        const ulong Id = 880101;
        PlayerState seed = PS(Id, 50); seed.Wallet.Add(new KeyValuePair<uint, uint>(1, 1000)); seed.Level = 3; seed.Exp = need3 - 1;      // 레벨 3에서 한 마리만 잡아도 레벨 업
        store.SaveAsync(seed, CancellationToken.None).GetAwaiter().GetResult();

        using (GatewayServerMain gw = new GatewayServerMain())
        using (ZoneServerMain z1 = new ZoneServerMain())
        {
            Check(gw.Start(new GatewayOptions { ClientBindIp = "127.0.0.1", ClientPort = 0, ZoneBindIp = "127.0.0.1", ZonePort = 0, PlayerStore = store, SaveFlushIntervalMs = 50 }), "level: gateway starts");
            Check(z1.Start(new ZoneServerOptions { ZoneId = 1, Capacity = 50, TickIntervalMs = 50, GatewayIp = "127.0.0.1", GatewayPort = (ushort)gw.ZoneEndPoint!.Port, GameDataDir = cdata }), "level: zone starts");
            for (int i = 0; i < 200 && !z1.IsRegistered; ++i) Thread.Sleep(25);
            using ClientService svc = new ClientService(gw.ClientEndPoint!, () => new Client(), new NetServiceOptions { MaxSessionCount = 4 });

            Client c = Connect(svc);
            Check(Login(c, Id), "level: login");
            GwEnterZoneRes? r = Enter(c, 1, 1);
            Check(r != null && r.Value.Success == 1, "level: enter");
            int baseHp = 4500;
            Check(OnZone(z1, zz => (int)((ZonePlayer)zz.FindEntityBase(Id)!).Level) == 3, "the saved level is restored");
            Check(OnZone(z1, zz => zz.FindEntityBase(Id)!.CombatStats.MaxHp) == baseHp * (int)doc[2].GetProperty("StatPercent").GetUInt32() / 100, "max hp follows the level");

            Check(OnZone(z1, zz => (long)((ZonePlayer)zz.FindEntityBase(Id)!).Wallet.GetValueOrDefault(1u)) == 1000, "the saved gold is restored");

            OnZone(z1, zz =>
            {
                Monster? slime = null;
                zz.ForEachInRadius(new Vec2(500, 500), 5000f, e => { if (slime == null && e is Monster m && m.Template.Id == 1 && !m.IsBoss) slime = m; });
                zz.DealDamage(slime!, zz.FindEntityBase(Id), 1_000_000);
                return 0;
            });
            Check(OnZone(z1, zz => (int)((ZonePlayer)zz.FindEntityBase(Id)!).Level) == 4, "a kill that completes the bar raises the level");
            Check(OnZone(z1, zz => (long)((ZonePlayer)zz.FindEntityBase(Id)!).Exp) == slimeExp - 1, "surplus exp carries over to the new level");
            Check(OnZone(z1, zz => zz.FindEntityBase(Id)!.CombatStats.MaxHp) == baseHp * (int)pct4 / 100, "level up recomputes max hp");
            Check(OnZone(z1, zz => zz.FindEntityBase(Id)!.Hp == zz.FindEntityBase(Id)!.CombatStats.MaxHp), "level up refills hp");

            uint gold = OnZone(z1, zz => ((ZonePlayer)zz.FindEntityBase(Id)!).Wallet.GetValueOrDefault(1u));
            Check(gold >= 1000 + goldMin && gold <= 1000 + goldMax, "a kill pays gold from the monster's range");

            c.Tx(new GwLeaveZoneReq());
            Check(WaitFor(() => { PlayerState? p = store.Peek(Id); return p != null && p.Wallet.Count == 1 && p.Wallet[0].Value == gold; }), "gold is saved on leave");
            Check(WaitFor(() => { PlayerState? p = store.Peek(Id); return p != null && p.Level == 4 && p.Exp == slimeExp - 1; }), "level and exp are saved on leave");
        }
    }

    // 스킬 개방/구매/장착: 무료 스킬은 레벨이 되면 고정 칸(1~6)으로 자동 지급되고, 상점 스킬은 레벨/재화를 검사해 사서 레벨로 열린 칸 7·8 에 끼운다. 상태는 저장된다.
    static void TestSkillBook()
    {
        // 와이어 왕복: 보유 스킬과 장착 칸이 상태 저장 패킷을 거쳐도 그대로다.
        PlayerState ws = PS(2, 10); ws.Skills.Add(new KeyValuePair<uint, byte>(60, 7)); ws.Skills.Add(new KeyValuePair<uint, byte>(2, 2));
        Span<byte> wb = stackalloc byte[ZoneStateSave.PacketSize];
        new ZoneStateSave { State = ws }.Write(wb);
        Check(ZoneStateSave.TryRead(wb, out ZoneStateSave back) && back.State.Skills.Count == 2 && back.State.Skills[0].Key == 60 && back.State.Skills[0].Value == 7 && back.State.Skills[1].Value == 2, "skills survive the state wire");
        Check(Game.Persistence.PlayerStateCopy.Clone(ws).Skills.Count == 2, "clone keeps skills");
        Span<byte> bk = stackalloc byte[ZcSkillBookNotify.Size];
        new ZcSkillBookNotify { Count = 2, SkillIds = new ushort[] { 3, 9 }, Slots = new byte[] { 1, 0 }, ShopSlot7Level = 25, ShopSlot8Level = 35 }.Write(bk);
        Check(ZcSkillBookNotify.TryRead(bk, out ZcSkillBookNotify bn) && bn.Count == 2 && bn.SkillIds[1] == 9 && bn.Slots[0] == 1 && bn.ShopSlot7Level == 25 && bn.ShopSlot8Level == 35, "ZcSkillBookNotify round trip");

        string data = FindData();
        uint price60 = 0, level61 = 0;
        foreach (var r in System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "HeroSkill.json"))).RootElement.GetProperty("HeroSkill").EnumerateArray())
        {
            if (r.GetProperty("HeroId").GetUInt32() != 1) continue;
            if (r.GetProperty("SkillId").GetUInt32() == 60) price60 = r.GetProperty("Price").GetUInt32();
            if (r.GetProperty("SkillId").GetUInt32() == 61) level61 = r.GetProperty("UnlockLevel").GetUInt32();
        }
        Check(price60 > 0 && level61 > SkillRules.SlotUnlockLevel(7), "hero 1 has shop skills 60 and 61 (61 needs a higher level)");

        Game.Persistence.InMemoryPlayerStore store = new Game.Persistence.InMemoryPlayerStore();
        const ulong Id = 880301;
        PlayerState seed = PS(Id, 50); seed.Level = SkillRules.SlotUnlockLevel(7); seed.Wallet.Add(new KeyValuePair<uint, uint>(1, price60 + 2000));
        store.SaveAsync(seed, CancellationToken.None).GetAwaiter().GetResult();

        using (GatewayServerMain gw = new GatewayServerMain())
        using (ZoneServerMain z1 = new ZoneServerMain())
        {
            Check(gw.Start(new GatewayOptions { ClientBindIp = "127.0.0.1", ClientPort = 0, ZoneBindIp = "127.0.0.1", ZonePort = 0, PlayerStore = store, SaveFlushIntervalMs = 50 }), "skills: gateway start");
            Check(z1.Start(new ZoneServerOptions { ZoneId = 1, Capacity = 50, TickIntervalMs = 50, GatewayIp = "127.0.0.1", GatewayPort = (ushort)gw.ZoneEndPoint!.Port, GameDataDir = data }), "skills: zone start");
            for (int i = 0; i < 200 && !z1.IsRegistered; ++i) Thread.Sleep(25);
            using ClientService svc = new ClientService(gw.ClientEndPoint!, () => new Client(), new NetServiceOptions { MaxSessionCount = 4 });
            Client c = Connect(svc);
            Check(Login(c, Id), "skills: login");
            GwEnterZoneRes? r = Enter(c, 1, 1);
            Check(r != null && r.Value.Success == 1, "skills: enter");

            Dictionary<uint, byte> Book() => OnZone(z1, zz => new Dictionary<uint, byte>(((ZonePlayer)zz.FindEntityBase(Id)!).Skills));
            uint Gold() => OnZone(z1, zz => ((ZonePlayer)zz.FindEntityBase(Id)!).Wallet.GetValueOrDefault(1u));

            Dictionary<uint, byte> b0 = Book();
            Check(b0.Count == 6 && b0[1] == 1 && b0[2] == 2 && b0[7] == 3 && b0[16] == 4 && b0[17] == 5 && b0[28] == 6 && !b0.ContainsKey(60), "the six free skills are granted into their fixed slots");

            c.Tx(new ZcSkillBuyReq { SkillId = 61 });       // 레벨이 모자란다
            c.Tx(new ZcSkillBuyReq { SkillId = 999 });      // 내 영웅의 스킬이 아니다
            c.Tx(new ZcSkillBuyReq { SkillId = 7 });        // 이미 보유(무료)
            Thread.Sleep(300);
            Check(Book().Count == 6 && Gold() == price60 + 2000, "rejected purchases change nothing");

            c.Tx(new ZcSkillBuyReq { SkillId = 60 });
            Check(WaitFor(() => Book().ContainsKey(60)), "a shop skill is bought");
            Check(Book()[60] == 7 && Gold() == 2000, "the price is charged and the skill goes into the first open shop slot");
            c.Tx(new ZcSkillBuyReq { SkillId = 60 });
            Thread.Sleep(200);
            Check(Gold() == 2000, "buying twice does not charge twice");

            c.Tx(new ZcSkillEquipReq { SkillId = 17, Slot = 7 });       // 무료 스킬은 고정 칸
            c.Tx(new ZcSkillEquipReq { SkillId = 60, Slot = 8 });       // 칸 8 은 아직 안 열렸다
            c.Tx(new ZcSkillEquipReq { SkillId = 60, Slot = 3 });       // 고정 칸에는 끼울 수 없다
            Thread.Sleep(300);
            Dictionary<uint, byte> b1 = Book();
            Check(b1[17] == 5 && b1[60] == 7, "fixed skills stay put, locked and fixed slots are refused");
            c.Tx(new ZcSkillEquipReq { SkillId = 60, Slot = 0 });
            Check(WaitFor(() => Book()[60] == 0), "a shop skill can be unequipped");
            c.Tx(new ZcSkillEquipReq { SkillId = 60, Slot = 7 });
            Check(WaitFor(() => Book()[60] == 7), "and equipped again");

            c.Tx(new GwLeaveZoneReq());
            Check(WaitFor(() =>
            {
                PlayerState? p = store.Peek(Id);
                if (p == null) return false;
                Dictionary<uint, byte> d = new Dictionary<uint, byte>();
                foreach (var kv in p.Skills) d[kv.Key] = kv.Value;
                return d.Count == 7 && d[60] == 7 && d[28] == 6 && p.Wallet.Count == 1 && p.Wallet[0].Value == 2000;
            }), "skills, slots and the remaining gold are saved");
        }
    }

    // 몬스터 기본 공격: 바로 때리지 않고 예비 동작(시전)을 거친 뒤 맞힌다. 예비 동작 중 대상이 사거리 밖으로 피하면 빗나간다.
    static void TestMonsterWindup()
    {
        string data = FindData();
        const ulong Id = 880301;
        Game.Persistence.InMemoryPlayerStore store = new Game.Persistence.InMemoryPlayerStore();
        store.SaveAsync(PS(Id, 4000), CancellationToken.None).GetAwaiter().GetResult();
        using GatewayServerMain gw = new GatewayServerMain();
        using ZoneServerMain z1 = new ZoneServerMain();
        gw.Start(new GatewayOptions { ClientBindIp = "127.0.0.1", ClientPort = 0, ZoneBindIp = "127.0.0.1", ZonePort = 0, PlayerStore = store });
        Check(z1.Start(new ZoneServerOptions { ZoneId = 1, Capacity = 50, TickIntervalMs = 10, GatewayIp = "127.0.0.1", GatewayPort = (ushort)gw.ZoneEndPoint!.Port, GameDataDir = data, MetricsIntervalMs = 0 }), "windup: zone starts");
        for (int i = 0; i < 200 && !z1.IsRegistered; ++i) Thread.Sleep(25);
        using ClientService svc = new ClientService(gw.ClientEndPoint!, () => new Client(), new NetServiceOptions { MaxSessionCount = 4 });
        Client c = Connect(svc);
        Check(Login(c, Id), "windup: login");
        GwEnterZoneRes? r = Enter(c, 1, 1);
        Check(r != null && r.Value.Success == 1, "windup: enter");

        ulong monId = OnZone(z1, zz =>
        {
            Monster? slime = null;
            zz.ForEachInRadius(new Vec2(500, 500), 5000f, e => { if (slime == null && e is Monster m && m.Template.Id == 1 && !m.IsBoss) slime = m; });
            zz.Teleport(Id, new Vec2(slime!.Pos.X + 1.2f, slime.Pos.Y));      // 몬스터를 옮기면 복귀 한계를 넘어 돌아가 버리므로 플레이어를 옮긴다
            slime.AddThreat(Id, 100);
            return slime.Id;
        });

        int startHp = OnZone(z1, zz => zz.FindEntityBase(Id)!.Hp);
        bool sawWindup = false, hitAfterWindup = false;
        long end = Environment.TickCount64 + 6000;
        while (Environment.TickCount64 < end && !hitAfterWindup)
        {
            (bool casting, int hp) = OnZone(z1, zz => (zz.FindEntityBase(monId)!.IsCasting, zz.FindEntityBase(Id)!.Hp));
            if (casting) sawWindup = true;
            if (hp < startHp) hitAfterWindup = sawWindup;
            if (hp < startHp) break;
            Thread.Sleep(5);
        }
        Check(sawWindup, "a monster prepares (casts) before its basic attack lands");
        Check(hitAfterWindup, "the basic attack lands after the windup");

        // 예비 동작 중에 멀리 피하면 빗나간다.
        int hp0 = OnZone(z1, zz => zz.FindEntityBase(Id)!.Hp);
        bool dodged = false;
        for (long end2 = Environment.TickCount64 + 6000; Environment.TickCount64 < end2 && !dodged;)
        {
            bool casting = OnZone(z1, zz => zz.FindEntityBase(monId)!.IsCasting);
            if (!casting) { Thread.Sleep(5); continue; }
            int before = OnZone(z1, zz => zz.FindEntityBase(Id)!.Hp);
            OnZone(z1, zz => { Vec2 mp = zz.FindEntityBase(monId)!.Pos; foreach (Vec2 d in new[] { new Vec2(30f, 0f), new Vec2(-30f, 0f), new Vec2(0f, 30f), new Vec2(0f, -30f) }) if (zz.Teleport(Id, new Vec2(mp.X + d.X, mp.Y + d.Y))) break; return 0; });      // 다른 몬스터가 없을 만큼 멀리
            Thread.Sleep(700);
            dodged = OnZone(z1, zz => zz.FindEntityBase(Id)!.Hp) >= before;      // 체력 재생이 있으므로 줄지 않았는지만 본다
        }
        Check(dodged, "leaving the reach during the windup makes the attack miss");
    }

    // 쓰기 큐: 같은 캐릭터 합치기, 대기 중 최신 상태 읽기, 실패 후 재시도, 문제 있는 한 건 격리(버림), 종료 때 내려 쓰기.
    static void TestWriteBehind()
    {
        FlakyStore fs = new FlakyStore();
        using (Game.Persistence.WriteBehindPlayerStore wb = new Game.Persistence.WriteBehindPlayerStore(fs, flushIntervalMs: 200, batchSize: 10, maxAttempts: 3, retryBackoffMs: 20))
        {
            wb.Enqueue(PS(1, 10)); wb.Enqueue(PS(1, 20)); wb.Enqueue(PS(1, 30));
            PlayerState? pend = wb.LoadAsync(1, CancellationToken.None).GetAwaiter().GetResult();
            Check(pend != null && pend.Hp == 30, "load sees the newest pending state before it reaches the db");
            Check(wb.Flush(3000), "flush drains the queue");
            Check(fs.Inner.Peek(1)?.Hp == 30 && fs.Inner.Count == 1, "same character is coalesced: only the newest state is written");
            Check(fs.Inner.Peek(1)!.Items.Count == 1 && fs.Inner.Peek(1)!.Items[0].Key == 1001, "items are saved");

            fs.FailNext = 2; wb.Enqueue(PS(2, 55));      // 묶음 저장과 한 명씩 저장이 한 번씩 실패한 뒤 다음 시도에서 성공한다
            Check(WaitFor(() => fs.Inner.Peek(2)?.Hp == 55), "a failed save is retried and lands once the db is back");
            Check(wb.Dropped == 0, "nothing dropped after a recovered failure");

            fs.FailNext = 0; fs.PoisonId = 666;
            long dropped0 = wb.Dropped;
            wb.Enqueue(PS(666, 1)); wb.Enqueue(PS(3, 77)); wb.Enqueue(PS(4, 78));
            Check(WaitFor(() => fs.Inner.Peek(3)?.Hp == 77 && fs.Inner.Peek(4)?.Hp == 78), "one poison record does not block the others (batch falls back to single saves)");
            Check(WaitFor(() => wb.Dropped == dropped0 + 1 && wb.Pending == 0), "the poison record is dropped after max attempts");

            fs.FailAll = true;
            Task<PlayerState?> failed = wb.LoadAsync(999, CancellationToken.None);
            Check(WaitFor(() => failed.IsCompleted) && failed.IsFaulted, "a db read failure surfaces as an error, not as 'new character'");
            fs.FailAll = false;

            wb.Enqueue(PS(5, 90));
        }
        Check(fs.Inner.Peek(5)?.Hp == 90, "dispose flushes what is still queued");
    }

    // Gateway + 저장소: 떠날 때 상태가 저장되고, 새 Gateway(메모리 상태 없음)가 저장소에서 읽어 복원하며, 읽기 실패는 입장을 거부한다.
    static void TestPersistence()
    {
        string data = FindData();
        Game.Persistence.InMemoryPlayerStore store = new Game.Persistence.InMemoryPlayerStore();
        const ulong LeaveId = 880001, SeededId = 880002, NewId = 880003;

        using (GatewayServerMain gw = new GatewayServerMain())
        using (ZoneServerMain z1 = new ZoneServerMain())
        {
            Check(gw.Start(new GatewayOptions { ClientBindIp = "127.0.0.1", ClientPort = 0, ZoneBindIp = "127.0.0.1", ZonePort = 0, PlayerStore = store, SaveFlushIntervalMs = 50 }), "gateway with a store starts");
            Check(z1.Start(new ZoneServerOptions { ZoneId = 1, Capacity = 50, TickIntervalMs = 50, GatewayIp = "127.0.0.1", GatewayPort = (ushort)gw.ZoneEndPoint!.Port, GameDataDir = data, MetricsIntervalMs = 0 }), "zone starts (persistence test)");
            for (int i = 0; i < 200 && !z1.IsRegistered; ++i) Thread.Sleep(25);
            using ClientService svc = new ClientService(gw.ClientEndPoint!, () => new Client(), new NetServiceOptions { MaxSessionCount = 8 });

            Client c = Connect(svc);
            Check(Login(c, NewId), "login new character");
            GwEnterZoneRes? r = Enter(c, 1, 1);
            Check(r != null && r.Value.Success == 1, "a character without a saved row enters normally");
            int maxHp = OnZone(z1, zz => zz.FindEntityBase(NewId)!.CombatStats.MaxHp);
            Check(OnZone(z1, zz => zz.FindEntityBase(NewId)!.Hp) == maxHp, "new character starts at full hp");
            c.Tx(new GwLeaveZoneReq());
            Check(WaitFor(() => store.Peek(NewId) != null), "leaving the zone writes the character to the store");

            // 저장소에 미리 넣어 둔 상태(예: 이전 접속 때 저장된 것)를 새로 접속하면 읽어 와 복원한다. Gateway 메모리에는 없다.
            store.SaveAsync(PS(SeededId, 50), CancellationToken.None).GetAwaiter().GetResult();
            Client c2 = Connect(svc);
            Check(Login(c2, SeededId), "login seeded character");
            GwEnterZoneRes? r2 = Enter(c2, 1, 1);
            Check(r2 != null && r2.Value.Success == 1, "seeded character enters");
            Check(OnZone(z1, zz => zz.FindEntityBase(SeededId)?.Hp ?? -1) == 50, "hp is restored from the store");
        }

        // 저장소 읽기가 실패하면 입장시키지 않는다(빈 상태로 들어가 좋은 데이터를 덮어쓰지 않도록).
        FlakyStore down = new FlakyStore { FailAll = true };
        using (GatewayServerMain gw = new GatewayServerMain())
        using (ZoneServerMain z1 = new ZoneServerMain())
        {
            Check(gw.Start(new GatewayOptions { ClientBindIp = "127.0.0.1", ClientPort = 0, ZoneBindIp = "127.0.0.1", ZonePort = 0, PlayerStore = down }), "gateway with a failing store starts");
            Check(z1.Start(new ZoneServerOptions { ZoneId = 1, Capacity = 50, TickIntervalMs = 50, GatewayIp = "127.0.0.1", GatewayPort = (ushort)gw.ZoneEndPoint!.Port, GameDataDir = data, MetricsIntervalMs = 0 }), "zone starts (failing store test)");
            for (int i = 0; i < 200 && !z1.IsRegistered; ++i) Thread.Sleep(25);
            using ClientService svc = new ClientService(gw.ClientEndPoint!, () => new Client(), new NetServiceOptions { MaxSessionCount = 4 });
            Client c = Connect(svc);
            Check(Login(c, 880010), "login (failing store)");
            GwEnterZoneRes? r = Enter(c, 1, 1);
            Check(r != null && r.Value.Success == 0, "enter is refused when the store cannot be read");
            Check(OnZone(z1, zz => zz.PlayerCount) == 0, "and nobody was placed in the zone");
            down.FailAll = false;
            GwEnterZoneRes? r2 = Enter(c, 1, 1);
            Check(r2 != null && r2.Value.Success == 1, "after the db is back the same character can enter");
        }
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
        TestZoneZero();
        TestTerrainCheat();
        TestMonsterWindup();
        TestSkillBook();
        TestWriteBehind();
        TestPersistence();
        TestLevel();
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
