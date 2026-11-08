// Program.cs : Core.Net PacketDispatcher test.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Core.Net;

namespace Core.Demo;

internal sealed class Ctx
{
    public readonly List<string> Log = new();
}

internal sealed class OtherCtx { }

internal static class AttrHandlers
{
    [PacketHandler(10)]
    internal static void OnTen(Ctx c, ReadOnlySpan<byte> p) => c.Log.Add("ten:" + p.Length);

    [PacketHandler(11, MinSize = 8)]
    private static void OnEleven(Ctx c, ReadOnlySpan<byte> p) => c.Log.Add("eleven:" + p.Length);

    // 다른 컨텍스트용 핸들러는 건너뛴다.
    [PacketHandler(10)]
    internal static void OtherTen(OtherCtx c, ReadOnlySpan<byte> p) { }

    // 표시가 없는 메서드는 무시한다.
    internal static void NotAHandler(Ctx c, ReadOnlySpan<byte> p) { }
}

internal static class BadSignature
{
    [PacketHandler(20)]
    internal static void Bad(Ctx c, byte[] p) { }
}

internal static class InstanceHandler
{
    internal sealed class Holder
    {
        [PacketHandler(21)]
        internal void Bad(Ctx c, ReadOnlySpan<byte> p) { }
    }
}

internal static class Program
{
    private static int s_failures;

    private static void Check(string name, bool ok)
    {
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
        if (!ok)
            s_failures++;
    }

    private static byte[] Packet(ushort id, int size, int bufferSize = -1)
    {
        var b = new byte[bufferSize < 0 ? size : bufferSize];
        if (b.Length >= PacketHeader.HeaderSize)
            PacketHeader.Write(b, (ushort)size, id);
        return b;
    }

    private static bool Throws<T>(Action a) where T : Exception
    {
        try { a(); return false; }
        catch (T) { return true; }
    }

    private sealed class ChatSession : PacketSession
    {
        public static readonly PacketDispatcher<ChatSession> Dispatcher = new();
        public static readonly List<string> Handled = new();

        protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
        {
            if (Dispatcher.Dispatch(this, packet) != PacketDispatchResult.Handled)
                Disconnect(CloseReason.ProtocolError);
        }

        public static void OnChat(ChatSession s, ReadOnlySpan<byte> p)
        {
            lock (Handled) Handled.Add("chat:" + p.Length + ":" + s.SessionId);
        }
    }

    private static int Main()
    {
        // 1) 직접 등록과 디스패치
        var d = new PacketDispatcher<Ctx>();
        var ctx = new Ctx();
        d.Register(1, 6, (c, p) => c.Log.Add("one:" + p.Length));
        Check("Register: Count", d.Count == 1 && !d.IsFrozen);
        Check("Dispatch: Handled + 컨텍스트 전달", d.Dispatch(ctx, Packet(1, 6)) == PacketDispatchResult.Handled && ctx.Log[0] == "one:6");
        Check("Dispatch 후 Frozen", d.IsFrozen);
        Check("Dispatch: 헤더 size 길이로 잘라 전달", d.Dispatch(ctx, Packet(1, 6, 10)) == PacketDispatchResult.Handled && ctx.Log[1] == "one:6");

        // 2) 검증 결과
        Check("UnknownType", d.Dispatch(ctx, Packet(2, 8)) == PacketDispatchResult.UnknownType);
        Check("SizeViolation: 버퍼가 헤더보다 작음", d.Dispatch(ctx, new byte[3]) == PacketDispatchResult.SizeViolation);
        Check("SizeViolation: size 가 헤더보다 작음", d.Dispatch(ctx, Packet(1, 3, 8)) == PacketDispatchResult.SizeViolation);
        Check("SizeViolation: size 가 버퍼를 넘음", d.Dispatch(ctx, Packet(1, 20, 8)) == PacketDispatchResult.SizeViolation);
        Check("SizeViolation: 타입별 최소 크기 미만", d.Dispatch(ctx, Packet(1, 5)) == PacketDispatchResult.SizeViolation);
        Check("SizeViolation 이면 핸들러를 부르지 않는다", ctx.Log.Count == 2);

        // 3) 등록 규칙
        var d2 = new PacketDispatcher<Ctx>();
        d2.Register(5, 4, (c, p) => { });
        Check("중복 등록은 예외", Throws<InvalidOperationException>(() => d2.Register(5, 4, (c, p) => { })));
        Check("minSize < 헤더 크기는 예외", Throws<ArgumentOutOfRangeException>(() => d2.Register(6, 3, (c, p) => { })));
        Check("minSize > 65535 는 예외", Throws<ArgumentOutOfRangeException>(() => d2.Register(6, 70000, (c, p) => { })));
        Check("handler null 은 예외", Throws<ArgumentNullException>(() => d2.Register(6, 4, null!)));
        Check("실패한 등록은 Count 에 반영되지 않는다", d2.Count == 1);
        d2.Freeze();
        Check("Freeze 뒤 등록은 예외", Throws<InvalidOperationException>(() => d2.Register(7, 4, (c, p) => { })));
        d2.Freeze();
        Check("Freeze 는 여러 번 불러도 된다", d2.IsFrozen && d2.Count == 1);

        // 4) 핸들러 예외는 그대로 전파된다
        var d3 = new PacketDispatcher<Ctx>();
        d3.Register(1, 4, (c, p) => throw new InvalidOperationException("boom"));
        Check("핸들러 예외 전파", Throws<InvalidOperationException>(() => d3.Dispatch(ctx, Packet(1, 4))));

        // 5) 어트리뷰트 자동 등록
        var d4 = new PacketDispatcher<Ctx>();
        var c4 = new Ctx();
        int n = d4.RegisterFromType(typeof(AttrHandlers));
        Check("RegisterFromType: 등록 수(다른 컨텍스트/표시 없는 메서드 제외)", n == 2 && d4.Count == 2);
        Check("자동 등록: 기본 MinSize 는 헤더 크기", d4.Dispatch(c4, Packet(10, 4)) == PacketDispatchResult.Handled && c4.Log[0] == "ten:4");
        Check("자동 등록: MinSize 지정과 private 메서드", d4.Dispatch(c4, Packet(11, 6)) == PacketDispatchResult.SizeViolation
              && d4.Dispatch(c4, Packet(11, 8)) == PacketDispatchResult.Handled && c4.Log[1] == "eleven:8");
        var d5 = new PacketDispatcher<Ctx>();
        Check("시그니처가 틀리면 예외", Throws<InvalidOperationException>(() => d5.RegisterFromType(typeof(BadSignature))));
        Check("instance 메서드는 예외", Throws<InvalidOperationException>(() => d5.RegisterFromType(typeof(InstanceHandler.Holder))));
        var d6 = new PacketDispatcher<OtherCtx>();
        Check("컨텍스트별로 서로 다른 핸들러를 고른다", d6.RegisterFromType(typeof(AttrHandlers)) == 1);
        var d7 = new PacketDispatcher<Ctx>();
        d7.Register(10, 4, (c, p) => { });
        Check("직접 등록과 자동 등록이 겹치면 예외", Throws<InvalidOperationException>(() => d7.RegisterFromType(typeof(AttrHandlers))));
        var d8 = new PacketDispatcher<Ctx>();
        Check("RegisterFromAssembly: 핸들러 없는 어셈블리는 0개", d8.RegisterFromAssembly(typeof(PacketHeader).Assembly) == 0);
        Check("RegisterFromAssembly: 어셈블리 안의 잘못된 핸들러는 예외", Throws<InvalidOperationException>(() => new PacketDispatcher<Ctx>().RegisterFromAssembly(typeof(AttrHandlers).Assembly)));

        // 6) 동시 Dispatch (등록 뒤 읽기 전용)
        var d9 = new PacketDispatcher<Ctx>();
        long sum = 0;
        d9.Register(1, 4, (c, p) => Interlocked.Increment(ref sum));
        d9.Freeze();
        var threads = new Thread[8];
        bool allOk = true;
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() =>
            {
                byte[] pk = Packet(1, 4);
                for (int k = 0; k < 100000; k++)
                    if (d9.Dispatch(null!, pk) != PacketDispatchResult.Handled) allOk = false;
            });
            threads[i].Start();
        }
        foreach (Thread t in threads) t.Join();
        Check("여러 스레드 동시 Dispatch", allOk && Interlocked.Read(ref sum) == 800000);

        // 7) 실제 TCP: PacketSession 안에서 사용
        ChatSession.Dispatcher.Register(100, 6, ChatSession.OnChat);
        var server = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new ChatSession());
        server.Start();
        using (var tc = new TcpClient())
        {
            tc.Connect(server.LocalEndPoint!);
            NetworkStream ns = tc.GetStream();
            byte[] good = Packet(100, 6);
            ns.Write(good, 0, good.Length);
            Check("TCP: 등록된 패킷이 핸들러에 도달", WaitFor(() => { lock (ChatSession.Handled) return ChatSession.Handled.Count == 1; }, 3000));

            byte[] unknown = Packet(999, 4);
            ns.Write(unknown, 0, unknown.Length);
            Check("TCP: 알 수 없는 타입이면 서버가 연결을 끊는다", WaitForClosed(tc, 3000));
        }
        server.Close(2000);

        Console.WriteLine(s_failures == 0 ? "ALL PASSED" : $"{s_failures} FAILED");
        return s_failures == 0 ? 0 : 1;
    }

    private static bool WaitFor(Func<bool> cond, int ms)
    {
        for (int i = 0; i < ms / 10 && !cond(); i++)
            Thread.Sleep(10);
        return cond();
    }

    private static bool WaitForClosed(TcpClient tc, int ms)
    {
        var buf = new byte[16];
        tc.ReceiveTimeout = ms;
        try { return tc.GetStream().Read(buf, 0, buf.Length) == 0; }
        catch (System.IO.IOException) { return true; }
    }
}
