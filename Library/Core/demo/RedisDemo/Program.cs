// RedisDemo : Core.Redis 단위 검사 + 실제 redis-server 통합 검사.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Net;
using Core.Redis;
using Core.Util;

internal static class Program
{
    static int s_pass, s_fail;

    static void Check(bool ok, string name)
    {
        if (ok) { s_pass++; Console.WriteLine("  ok   " + name); }
        else { s_fail++; Console.WriteLine("  FAIL " + name); }
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    sealed class Server : IDisposable
    {
        public readonly ushort Port;
        readonly string _pass;
        Process? _proc;
        public Server(ushort port, string pass = "") { Port = port; _pass = pass; Start(); }
        public void Start()
        {
            var psi = new ProcessStartInfo("redis-server") { RedirectStandardOutput = true, UseShellExecute = false };
            psi.ArgumentList.Add("--port"); psi.ArgumentList.Add(Port.ToString());
            psi.ArgumentList.Add("--bind"); psi.ArgumentList.Add("127.0.0.1");
            psi.ArgumentList.Add("--save"); psi.ArgumentList.Add("");
            psi.ArgumentList.Add("--appendonly"); psi.ArgumentList.Add("no");
            if (_pass.Length > 0) { psi.ArgumentList.Add("--requirepass"); psi.ArgumentList.Add(_pass); }
            _proc = Process.Start(psi)!;
            _proc.OutputDataReceived += (_, _) => { };
            for (int i = 0; i < 100; ++i)
            {
                try { using var c = new TcpClient(); c.Connect(IPAddress.Loopback, Port); return; }
                catch { Thread.Sleep(50); }
            }
            throw new Exception("redis-server start failed");
        }
        public void Kill() { try { _proc?.Kill(); _proc?.WaitForExit(); } catch { } _proc = null; }
        public void Dispose() => Kill();
    }

    static RedisValue Sync(RedisClient c, params string[] args)
    {
        var tcs = new TaskCompletionSource<RedisValue>();
        if (!c.SendCommand(args, v => tcs.TrySetResult(v))) return RedisHelper.MakeError("rejected");
        return tcs.Task.Wait(5000) ? tcs.Task.Result : RedisHelper.MakeError("timeout");
    }

    static RedisValue Sync(IRedisCommandSender s, params string[] args)
    {
        var tcs = new TaskCompletionSource<RedisValue>();
        if (!s.SendCommand(args, v => tcs.TrySetResult(v))) return RedisHelper.MakeError("rejected");
        return tcs.Task.Wait(8000) ? tcs.Task.Result : RedisHelper.MakeError("timeout");
    }

    static RedisValue Sync(RedisConnectionPool pool, params string[] args)
    {
        var tcs = new TaskCompletionSource<RedisValue>();
        if (!pool.SendCommand(args, v => tcs.TrySetResult(v))) return RedisHelper.MakeError("rejected");
        return tcs.Task.Wait(8000) ? tcs.Task.Result : RedisHelper.MakeError("timeout");
    }

    static bool WaitFor(Func<bool> cond, int ms = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; Thread.Sleep(20); }
        return cond();
    }

    static void UnitTests()
    {
        Console.WriteLine("[unit]");
        Check(RedisHelper.ParseInteger<int>("123".AsSpan(), out int a) && a == 123, "ParseInteger 123");
        Check(RedisHelper.ParseInteger<int>("-5".AsSpan(), out a) && a == -5, "ParseInteger -5");
        Check(!RedisHelper.ParseInteger<int>("".AsSpan(), out a), "ParseInteger empty rejected");
        Check(!RedisHelper.ParseInteger<int>("+1".AsSpan(), out a), "ParseInteger '+' rejected");
        Check(!RedisHelper.ParseInteger<uint>("-1".AsSpan(), out uint u), "ParseInteger unsigned negative rejected");
        Check(!RedisHelper.ParseInteger<byte>("256".AsSpan(), out byte b), "ParseInteger overflow rejected");
        Check(RedisHelper.ParseInteger<long>(Encoding.ASCII.GetBytes("9223372036854775807"), out long l) && l == long.MaxValue, "ParseInteger utf8 long max");

        long b1 = RedisHelper.ComputeReconnectBackoffMs(1), b3 = RedisHelper.ComputeReconnectBackoffMs(3), b99 = RedisHelper.ComputeReconnectBackoffMs(99);
        Check(b1 > 0 && b3 > b1 && b99 <= RedisConst.ReconnectBackoffMaxMs, "backoff grows and is capped");
        var bo = new ReconnectBackoff();
        Check(!bo.ShouldSkip(0), "backoff: fresh does not skip");
        bo.OnFail(1000);
        Check(bo.ShouldSkip(1001) && !bo.ShouldSkip(1000 + b1 + 1), "backoff: skip then allow");
        bo.Reset();
        Check(!bo.ShouldSkip(1001), "backoff: reset");

        Check(RedisCommandBuilder.BuildString(new[] { "SET", "k", "한글" }) == "*3\r\n$3\r\nSET\r\n$1\r\nk\r\n$6\r\n한글\r\n", "command builder (utf8 length)");
        Check(Encoding.UTF8.GetString(RedisCommandBuilder.Build(new[] { "PING" })) == "*1\r\n$4\r\nPING\r\n", "command builder PING");

        // 파서: 모든 경계에서 분할 입력
        byte[] wire = Encoding.UTF8.GetBytes("*3\r\n:42\r\n$5\r\nhello\r\n*2\r\n+OK\r\n-ERR x\r\n");
        bool allSplit = true;
        for (int cut = 0; cut <= wire.Length; ++cut)
        {
            var p = new RedisParser();
            p.Feed(wire.AsSpan(0, cut));
            var r1 = p.TryParse(out var v);
            if (cut < wire.Length && r1 == RedisParseResult.Success) { allSplit = false; Console.WriteLine($"    early success cut={cut}"); break; }
            if (r1 != RedisParseResult.Success) { p.Feed(wire.AsSpan(cut)); r1 = p.TryParse(out v); }
            if (r1 != RedisParseResult.Success || v!.Type != RedisType.Array || v.ArrayVal.Length != 3
                || v.ArrayVal[0].Int64Val != 42 || v.ArrayVal[1].StrVal != "hello"
                || v.ArrayVal[2].ArrayVal[0].StrVal != "OK" || v.ArrayVal[2].ArrayVal[1].Type != RedisType.Error)
            { allSplit = false; Console.WriteLine($"    split fail at cut={cut} r1={r1}"); break; }
        }
        Check(allSplit, "parser: split at every boundary");

        var pn = new RedisParser(); pn.Feed(Encoding.ASCII.GetBytes("$-1\r\n*-1\r\n"));
        Check(pn.TryParse(out var nv) == RedisParseResult.Success && nv!.IsNull, "parser: null bulk");
        Check(pn.TryParse(out nv) == RedisParseResult.Success && nv!.IsNull, "parser: null array");

        var pb = new RedisParser(); pb.Feed(Encoding.ASCII.GetBytes("$3\r\nabcXY"));
        Check(pb.TryParse(out _) == RedisParseResult.ProtocolError, "parser: bad trailing CRLF");
        var pt = new RedisParser(); pt.Feed(Encoding.ASCII.GetBytes("?x\r\n"));
        Check(pt.TryParse(out _) == RedisParseResult.ProtocolError, "parser: unknown type");
        var pa = new RedisParser(); pa.Feed(Encoding.ASCII.GetBytes("*4194305\r\n"));
        Check(pa.TryParse(out _) == RedisParseResult.ProtocolError, "parser: array cap");
        var pd = new RedisParser(); pd.Feed(Encoding.ASCII.GetBytes(new string('*', 100).Replace("*", "*1\r\n")));
        Check(pd.TryParse(out _) == RedisParseResult.ProtocolError, "parser: depth cap");
        ParserTests.Run(Check);

        var rs = new RedisResultSet(new RedisValue { Type = RedisType.Array, ArrayVal = new[] {
            new RedisValue { Type = RedisType.Integer, Int64Val = 7 }, new RedisValue { Type = RedisType.BulkString, StrVal = "12" },
            new RedisValue { Type = RedisType.BulkString, StrVal = "abc" } } });
        Check(rs.Size == 3 && rs.GetData(out int r0) && r0 == 7 && rs.GetData(out int r1b) && r1b == 12, "resultset ints");
        Check(!rs.GetData(out int bad) && rs.GetData(out string s) && s == "abc", "resultset: failed parse keeps cursor");

        Check(RedisScript.ComputeSha1Hex("abc") == "a9993e364706816aba3e25717850c26c9cd0d89d", "SHA1 vector");
    }

    static void ClientTests()
    {
        Console.WriteLine("[client]");
        using var srv = new Server((ushort)FreePort());
        using var c = new RedisClient();
        Check(c.Connect("127.0.0.1", srv.Port), "connect");
        Check(Sync(c, "PING").StrVal == "PONG", "PING");
        Check(Sync(c, "SET", "k", "한글값").StrVal == "OK" && Sync(c, "GET", "k").StrVal == "한글값", "SET/GET utf8");
        string big = new string('x', 3_000_000);
        Check(Sync(c, "SET", "big", big).StrVal == "OK" && Sync(c, "GET", "big").StrVal.Length == big.Length, "3MB value roundtrip");
        // 원소가 많은 배열 응답(예전 상한 4096 초과)과 해시 전체 조회
        Check(Sync(c, "EVAL", "for i=1,100000 do redis.call('RPUSH', KEYS[1], 'item:'..i) end return 1", "1", "biglist").Int64Val == 1, "build 100000-element list");
        var sw = Stopwatch.StartNew();
        RedisValue range = Sync(c, "LRANGE", "biglist", "0", "-1");
        sw.Stop();
        Check(range.Type == RedisType.Array && range.ArrayVal.Length == 100_000 && range.ArrayVal[0].StrVal == "item:1" && range.ArrayVal[99_999].StrVal == "item:100000",
            $"LRANGE 100000 elements ({sw.ElapsedMilliseconds}ms)");
        Check(Sync(c, "EVAL", "for i=1,3000 do redis.call('HSET', KEYS[1], 'f'..i, i) end return 1", "1", "bighash").Int64Val == 1, "build 3000-field hash");
        RedisValue all = Sync(c, "HGETALL", "bighash");
        Check(all.Type == RedisType.Array && all.ArrayVal.Length == 6000, "HGETALL 3000 fields (6000 elements)");
        Check(Sync(c, "PING").StrVal == "PONG", "connection still usable after large replies");
        Check(Sync(c, "NOSUCHCMD").Type == RedisType.Error, "server error returned as value");
        Check(Sync(c, "INCR", "n").Int64Val == 1, "INCR integer");

        // 한 클라이언트에 진행 중 명령이 있으면 두 번째는 거부 + 콜백 한 번
        int cbCount = 0;
        var gate = new ManualResetEventSlim();
        bool first = c.SendCommand(new[] { "BLPOP", "nolist", "1" }, _ => gate.Set());
        bool second = c.SendCommand(new[] { "PING" }, _ => Interlocked.Increment(ref cbCount));
        gate.Wait(3000);
        Thread.Sleep(100);
        Check(first && !second && cbCount == 1, "in-flight rejection calls callback exactly once");

        // 연결 끊기면 대기 콜백이 에러로 한 번만
        int errCb = 0; var got = new ManualResetEventSlim();
        c.SendCommand(new[] { "BLPOP", "nolist2", "5" }, v => { if (v.Type == RedisType.Error) Interlocked.Increment(ref errCb); got.Set(); });
        Thread.Sleep(100);
        c.Disconnect();
        got.Wait(2000); Thread.Sleep(100);
        Check(errCb == 1 && !c.IsConnected, "disconnect fails pending exactly once");
        Check(!c.SendCommand(new[] { "PING" }, null), "send after disconnect rejected");

        // DB 선택
        using var c2 = new RedisClient();
        c2.Connect("127.0.0.1", srv.Port, 3);
        Sync(c2, "SET", "dbk", "3");
        using var c3 = new RedisClient();
        c3.Connect("127.0.0.1", srv.Port, 0);
        Check(Sync(c3, "GET", "dbk").IsNull && Sync(c2, "GET", "dbk").StrVal == "3", "SELECT db index");

        // 연결 실패
        using var c4 = new RedisClient();
        Check(!c4.Connect("127.0.0.1", (ushort)FreePort(), 0, 500), "connect refused -> false");

        // AUTH
        using var srvA = new Server((ushort)FreePort(), "secret");
        using var ca = new RedisClient();
        Check(ca.Connect("127.0.0.1", srvA.Port) && Sync(ca, "PING").Type == RedisType.Error, "auth required: unauthenticated command gets NOAUTH error");
        using var cb = new RedisClient();
        Check(cb.Connect("127.0.0.1", srvA.Port, 0, 3000, "", "secret") && Sync(cb, "PING").StrVal == "PONG", "auth ok");
    }

    static void PoolTests()
    {
        Console.WriteLine("[pool]");
        var srv = new Server((ushort)FreePort());
        using var pool = new RedisConnectionPool();
        Check(pool.Init("127.0.0.1", srv.Port, 0, 4, 1), "pool init");
        int N = 2000, done = 0, okCnt = 0;
        var all = new ManualResetEventSlim();
        int rejected = 0;
        Parallel.For(0, N, i =>
        {
            if (!pool.SendCommand(new[] { "INCR", "pc" }, v =>
            {
                if (v.Type == RedisType.Integer) Interlocked.Increment(ref okCnt);
                if (Interlocked.Increment(ref done) == N) all.Set();
            })) { Interlocked.Increment(ref rejected); if (Interlocked.Increment(ref done) == N) all.Set(); }
        });
        all.Wait(15000);
        Check(done == N && okCnt + rejected == N && okCnt > 0, $"concurrent {N} cmds on 4 conns (ok={okCnt}, rejected={rejected})");
        Check(Sync(pool, "GET", "pc").StrVal == okCnt.ToString(), "INCR total matches successes");

        // 서버 kill → 에러 → 재시작 후 자동 복구
        srv.Kill();
        Thread.Sleep(300);
        var dead = Sync(pool, "PING");
        Check(dead.Type == RedisType.Error, "dead server -> error value");
        srv.Start();
        Check(WaitFor(() => Sync(pool, "PING").StrVal == "PONG", 20000), "pool reconnects after server restart");
        pool.Clear();
        Check(!pool.SendCommand(new[] { "PING" }, null), "after Clear send rejected");
        srv.Dispose();
    }

    static void SubscriberTests()
    {
        Console.WriteLine("[subscriber]");
        var srv = new Server((ushort)FreePort());
        var jq = new JobQueue();
        using var sub = new RedisSubscriber(jq);
        Check(sub.Init("127.0.0.1", srv.Port, 1), "subscriber init");
        var msgs = new List<string>();
        var pmsgs = new List<string>();
        sub.Subscribe("ch1", (ch, m) => { lock (msgs) msgs.Add(ch + ":" + m); });
        sub.PSubscribe("p*", (ch, m) => { lock (pmsgs) pmsgs.Add(ch + ":" + m); });
        Check(WaitFor(() => sub.IsConnected), "subscriber connected");
        using var pub = new RedisClient();
        pub.Connect("127.0.0.1", srv.Port);
        Thread.Sleep(300);
        Sync(pub, "PUBLISH", "ch1", "hello");
        Sync(pub, "PUBLISH", "pxx", "pm");
        Check(WaitFor(() => { lock (msgs) return msgs.Contains("ch1:hello"); }), "channel message");
        Check(WaitFor(() => { lock (pmsgs) return pmsgs.Contains("pxx:pm"); }), "pattern message");

        sub.Unsubscribe("ch1");
        Thread.Sleep(200);
        Sync(pub, "PUBLISH", "ch1", "after-unsub");
        Thread.Sleep(300);
        lock (msgs) Check(!msgs.Contains("ch1:after-unsub"), "unsubscribe stops delivery");

        sub.Subscribe("ch2", (ch, m) => { lock (msgs) msgs.Add(ch + ":" + m); });
        Thread.Sleep(200);
        srv.Kill();
        Check(WaitFor(() => !sub.IsConnected, 10000), "subscriber notices disconnect");
        srv.Start();
        Check(WaitFor(() => sub.IsConnected, 20000), "subscriber reconnects");
        using var pub2 = new RedisClient();
        pub2.Connect("127.0.0.1", srv.Port);
        Check(WaitFor(() =>
        {
            Sync(pub2, "PUBLISH", "ch2", "resub");
            lock (msgs) return msgs.Contains("ch2:resub");
        }, 15000), "resubscribed after restart");
        sub.Shutdown();
        srv.Dispose();
    }

    static void ServiceTests()
    {
        Console.WriteLine("[service]");
        using var s1 = new Server((ushort)FreePort());
        using var s2 = new Server((ushort)FreePort());
        var jq = new JobQueue();
        using var svc = new RedisService(jq);
        Check(!svc.Init(new[] { new RedisNode(1, "127.0.0.1", s1.Port), new RedisNode(1, "127.0.0.1", s2.Port) }, 2), "duplicate node id rejected");
        Check(svc.Init(new[] { new RedisNode(1, "127.0.0.1", s1.Port), new RedisNode(2, "127.0.0.1", s2.Port) }, 2), "init 2 nodes");
        Check(svc.DefaultNodeId == 1, "default node = first");
        Check(WaitFor(() => svc.SendCommandAsync(new[] { "PING" }).Result.StrVal == "PONG"), "async ping default node");
        svc.SendCommandAsync(1, new[] { "SET", "who", "n1" }).Wait();
        svc.SendCommandAsync(2, new[] { "SET", "who", "n2" }).Wait();
        Check(svc.SendCommandAsync(1, new[] { "GET", "who" }).Result.StrVal == "n1" && svc.SendCommandAsync(2, new[] { "GET", "who" }).Result.StrVal == "n2", "per-node isolation");
        Check(!svc.SendCommand(99, new[] { "PING" }, null), "unknown node rejected");
        Check(svc.SendCommandAsync(99, new[] { "PING" }).Result.Type == RedisType.Error, "async unknown node -> error value");

        // 서비스 콜백은 JobQueue 직렬 실행
        int concurrent = 0, maxConc = 0, cnt = 0;
        var all = new ManualResetEventSlim();
        for (int i = 0; i < 200; ++i)
            svc.SendCommand(new[] { "PING" }, _ =>
            {
                int c = Interlocked.Increment(ref concurrent);
                if (c > maxConc) maxConc = c;
                Thread.SpinWait(500);
                Interlocked.Decrement(ref concurrent);
                if (Interlocked.Increment(ref cnt) == 200) all.Set();
            });
        all.Wait(8000);
        Check(cnt == 200 && maxConc == 1, "callbacks serialized on JobQueue");

        // Pub/Sub via service
        string? got = null;
        svc.Subscribe(2, "sch", (ch, m) => got = m);
        Thread.Sleep(500);
        svc.Publish(2, "sch", "viaSvc");
        Check(WaitFor(() => got == "viaSvc"), "service pub/sub on node 2");
        svc.ShutdownSubscribers();

        // Script
        var script = new RedisScript("return redis.call('SET', KEYS[1], ARGV[1])");
        var tcs = new TaskCompletionSource<RedisValue>();
        script.Run(svc, new[] { "sk" }, new[] { "sv" }, v => tcs.SetResult(v));
        Check(tcs.Task.Wait(5000) && tcs.Task.Result.StrVal == "OK", "script: first run (NOSCRIPT -> EVAL fallback)");
        Sync(svc, "SCRIPT", "FLUSH");
        tcs = new TaskCompletionSource<RedisValue>();
        script.Run(svc, new[] { "sk" }, new[] { "sv2" }, v => tcs.SetResult(v));
        Check(tcs.Task.Wait(5000) && tcs.Task.Result.StrVal == "OK", "script: after SCRIPT FLUSH");
        Check(Sync(svc, "SCRIPT", "EXISTS", script.Sha).ArrayVal[0].Int64Val == 1, "script cached after EVAL");
        tcs = new TaskCompletionSource<RedisValue>();
        script.Run(svc, new[] { "sk" }, new[] { "sv3" }, v => tcs.SetResult(v));
        Check(tcs.Task.Wait(5000) && Sync(svc, "GET", "sk").StrVal == "sv3", "script: EVALSHA hit");

        // Retry: 서버를 죽였다가 지연 중에 되살림
        Check(Sync(svc, "SET", "rk", "1").StrVal == "OK", "pre-retry set");
        s1.Kill();
        Thread.Sleep(200);
        var rt = new TaskCompletionSource<RedisValue>();
        var sw = Stopwatch.StartNew();
        RedisRetry.SendCommandWithRetry(svc, jq, null, new[] { "PING" }, 10, 500, v => rt.TrySetResult(v));
        Thread.Sleep(800);
        s1.Start();
        bool ok = rt.Task.Wait(20000);
        Check(ok && rt.Task.Result.StrVal == "PONG", $"retry succeeds after restart ({sw.ElapsedMilliseconds}ms)");

        var rt2 = new TaskCompletionSource<RedisValue>();
        RedisRetry.SendCommandWithRetry(svc, jq, null, new[] { "NOSUCHCMD" }, 3, 50, v => rt2.TrySetResult(v));
        Check(rt2.Task.Wait(3000) && rt2.Task.Result.Type == RedisType.Error && !RedisHelper.IsConnectionError(rt2.Task.Result), "retry: server error not retried");

        // Heartbeat
        WaitFor(() => Sync(svc, "PING").StrVal == "PONG");
        var hb = new RedisServerHeartbeat(svc, "GameServer", "1", "2", 7777);
        int sessions = 5;
        hb.SetSessionCountProvider(() => sessions);
        Check(!hb.Start(5, 5), "heartbeat: ttl<=interval refused");
        Check(hb.Start(3, 1), "heartbeat start");
        Check(!hb.Start(3, 1), "heartbeat: second start refused");
        Check(WaitFor(() => Sync(svc, "EXISTS", hb.Key).Int64Val == 1), "heartbeat key registered");
        var h = Sync(svc, "HGETALL", hb.Key).ArrayVal;
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < h.Length; i += 2) d[h[i].StrVal] = h[i + 1].StrVal;
        Check(d.Count == 8 && d["serverName"] == "GameServer" && d["port"] == "7777" && d["sessionCount"] == "5" && d["pid"] == Environment.ProcessId.ToString(), "heartbeat fields");
        long ttl = Sync(svc, "TTL", hb.Key).Int64Val;
        Check(ttl > 0 && ttl <= 3, $"heartbeat TTL set ({ttl})");
        string u1 = d["updatedAt"]; sessions = 9;
        Thread.Sleep(1500);
        var h2 = Sync(svc, "HGET", hb.Key, "sessionCount");
        Check(h2.StrVal == "9" && Sync(svc, "HGET", hb.Key, "updatedAt").StrVal != u1, "heartbeat refreshes");
        Sync(svc, "DEL", hb.Key);
        Thread.Sleep(1500);
        Check(Sync(svc, "HLEN", hb.Key).Int64Val == 8, "heartbeat restores full registration after key loss");
        hb.Stop();
        Check(WaitFor(() => Sync(svc, "EXISTS", hb.Key).Int64Val == 0, 3000), "heartbeat stop deletes key");
        hb.Stop();
        Check(true, "heartbeat stop idempotent");

        jq.Close(true);
    }

    static int Main()
    {
        try
        {
            UnitTests();
            ClientTests();
            PoolTests();
            SubscriberTests();
            ServiceTests();
        }
        catch (Exception e)
        {
            Console.WriteLine("EXCEPTION: " + e);
            s_fail++;
        }
        Console.WriteLine($"\npass={s_pass} fail={s_fail}");
        if (s_fail == 0) Console.WriteLine("ALL PASSED");
        return s_fail == 0 ? 0 : 1;
    }
}
