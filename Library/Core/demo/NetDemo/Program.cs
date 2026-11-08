// Program.cs : Core.Net smoke / stress test (echo + broadcast + shutdown).
using System;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Core.Net;

namespace Core.Demo;

internal static class Program
{
    private const ushort BroadcastId = 0xFFFF;

    private static bool s_zeroByte = true;
    private static CloseReason s_lastServerReason;
    private static int s_goodbyeBytes;
    private static int s_goodbyeClosed;
    private static int s_floodAccepted;

    private static NetServiceOptions MakeOptions(long maxSendQueue = 16 * 1024 * 1024)
        => new NetServiceOptions { MaxSessionCount = 100, ZeroByteRecv = s_zeroByte, MaxSendQueueBytes = maxSendQueue };

    // 요청을 받으면 큰 응답을 보낸 뒤 DisconnectAfterSend 로 종료한다.
    private sealed class GoodbyeSession : PacketSession
    {
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
        {
            SendBuffer b = SendBufferManager.Open(60000);
            PacketHeader.Write(b.Buffer, 60000, 7);
            b.Buffer.Slice(PacketHeader.HeaderSize).Fill(0xAB);
            Send(b.Close(60000));
            DisconnectAfterSend(CloseReason.Local);
        }
    }

    private sealed class GoodbyeClient : PacketSession
    {
        protected override void OnConnected()
        {
            SendBuffer b = SendBufferManager.Open(PacketHeader.HeaderSize);
            PacketHeader.Write(b.Buffer, PacketHeader.HeaderSize, 1);
            Send(b.Close(PacketHeader.HeaderSize));
        }
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) => Interlocked.Add(ref s_goodbyeBytes, packet.Length);
        protected override void OnDisconnected(CloseReason reason) => Interlocked.Increment(ref s_goodbyeClosed);
    }

    // 연결 즉시 큰 데이터를 계속 밀어 넣는 서버 세션(상대가 읽지 않으면 송신 큐 상한을 넘는다).
    private sealed class FloodSession : PacketSession
    {
        protected override void OnConnected()
        {
            Interlocked.Increment(ref s_floodAccepted);
            for (int i = 0; i < 2000; i++)
            {
                SendBuffer b = SendBufferManager.Open(60000);
                PacketHeader.Write(b.Buffer, 60000, 1);
                if (!Send(b.Close(60000)))
                    break;
            }
        }
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) { }
        protected override void OnDisconnected(CloseReason reason) => s_lastServerReason = reason;
    }

    private static int s_serverConnected;
    private static int s_serverDisconnected;
    private static int s_clientConnected;
    private static int s_clientDisconnected;
    private static int s_connectFailed;
    private static int s_bad;
    private static long s_echoBytes;
    private static int s_clientDone;
    private static int s_broadcastRecv;

    private sealed class EchoSession : PacketSession
    {
        protected override void OnConnected() => Interlocked.Increment(ref s_serverConnected);
        protected override void OnDisconnected(CloseReason reason) => Interlocked.Increment(ref s_serverDisconnected);
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) => Send(packet);
    }

    private sealed class TestClient : PacketSession
    {
        private readonly int _index;
        private readonly int[] _sizes;
        private int _received;

        public TestClient(int index, int packetCount)
        {
            _index = index;
            var rnd = new Random(index * 7919 + 13);
            _sizes = new int[packetCount];
            for (int i = 0; i < packetCount; i++)
                _sizes[i] = rnd.Next(PacketHeader.HeaderSize, 60000);
        }

        protected override void OnConnected()
        {
            Interlocked.Increment(ref s_clientConnected);

            // 전부 파이프라인으로 밀어 넣어 배치 전송/부분 전송 경로를 자극한다.
            for (int seq = 0; seq < _sizes.Length; seq++)
            {
                int size = _sizes[seq];
                SendBuffer buffer = SendBufferManager.Open(size);
                Span<byte> span = buffer.Buffer;
                PacketHeader.Write(span, (ushort)size, (ushort)seq);
                span.Slice(PacketHeader.HeaderSize, size - PacketHeader.HeaderSize).Fill(Pattern(seq));
                Send(buffer.Close(size));
            }
        }

        protected override void OnDisconnected(CloseReason reason) => Interlocked.Increment(ref s_clientDisconnected);

        protected override void OnConnectFailed(SocketError error) => Interlocked.Increment(ref s_connectFailed);

        protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
        {
            ushort id = PacketHeader.ReadId(packet);

            if (id == BroadcastId)
            {
                Interlocked.Increment(ref s_broadcastRecv);
                return;
            }

            int seq = _received++;
            bool ok = id == seq
                      && seq < _sizes.Length
                      && PacketHeader.ReadSize(packet) == _sizes[seq]
                      && packet.Length == _sizes[seq];

            if (ok)
            {
                byte expect = Pattern(seq);
                foreach (byte b in packet.Slice(PacketHeader.HeaderSize))
                {
                    if (b != expect) { ok = false; break; }
                }
            }

            if (!ok)
                Interlocked.Increment(ref s_bad);

            Interlocked.Add(ref s_echoBytes, packet.Length);

            if (_received == _sizes.Length)
                Interlocked.Increment(ref s_clientDone);
        }

        private byte Pattern(int seq) => (byte)(seq * 7 + _index);
    }

    private static readonly System.Collections.Concurrent.ConcurrentQueue<CloseReason> s_reasons = new();

    private static bool WaitReason(CloseReason expected, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            while (s_reasons.TryDequeue(out CloseReason r))
            {
                if (r == expected)
                    return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    private sealed class ReasonSession : PacketSession
    {
        protected override void OnDisconnected(CloseReason reason) => s_reasons.Enqueue(reason);
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) => Send(packet);
    }

    // 연결 즉시 8바이트를 보내고 DisconnectAfterSend. 상대가 닫지 않아도 타임아웃 후 정리되어야 한다.
    private sealed class LingerSession : PacketSession
    {
        protected override void OnConnected()
        {
            Send(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            DisconnectAfterSend(CloseReason.Local);
        }
        protected override void OnDisconnected(CloseReason reason) => s_reasons.Enqueue(reason);
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) { }
    }

    private sealed class SmallBufferSession : PacketSession
    {
        public SmallBufferSession() : base(1000) { }
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) { }
    }

    // ---- 무작위 종료 부하(회귀 테스트) ----
    private static class ChaosStat
    {
        public static int SConn, SDisc, CConn, CDisc, Exc, Msgs;
        public static void Reset() { SConn = SDisc = CConn = CDisc = Exc = Msgs = 0; }
    }

    private sealed class ChaosSrv : PacketSession
    {
        protected override void OnConnected() => Interlocked.Increment(ref ChaosStat.SConn);
        protected override void OnDisconnected(CloseReason reason) => Interlocked.Increment(ref ChaosStat.SDisc);
        protected override void OnException(Exception exception) => Interlocked.Increment(ref ChaosStat.Exc);
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
        {
            Send(packet);
            if (Random.Shared.Next(200) == 0)
                Disconnect();
        }
    }

    private sealed class ChaosCli : PacketSession
    {
        private void SendRandom()
        {
            int size = Random.Shared.Next(4, 30000);
            SendBuffer b = SendBufferManager.Open(size);
            PacketHeader.Write(b.Buffer, (ushort)size, 1);
            Send(b.Close(size));
        }

        protected override void OnConnected()
        {
            Interlocked.Increment(ref ChaosStat.CConn);
            for (int i = 0; i < 30; i++)
                SendRandom();
        }
        protected override void OnDisconnected(CloseReason reason) => Interlocked.Increment(ref ChaosStat.CDisc);
        protected override void OnException(Exception exception) => Interlocked.Increment(ref ChaosStat.Exc);
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
        {
            Interlocked.Increment(ref ChaosStat.Msgs);
            int r = Random.Shared.Next(100);
            if (r == 0) Disconnect();
            else if (r == 1) DisconnectAfterSend();
            else SendRandom();
        }
    }

    private static bool RunChaos(bool zeroByte, bool startOnThreadPool, int durationMs)
    {
        ChaosStat.Reset();
        var opt = new NetServiceOptions { MaxSessionCount = 10000, ZeroByteRecv = zeroByte, StartOnThreadPool = startOnThreadPool };

        var server = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new ChaosSrv(), opt);
        server.Start();
        var client = new ClientService(server.LocalEndPoint!, () => new ChaosCli(), opt);

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < durationMs)
        {
            int need = 100 - client.Sessions.Count;
            for (int i = 0; i < need; i++)
                client.ConnectOneMoreSession();
            Thread.Sleep(5);
        }

        bool c = client.Close(10000);
        bool s = server.Close(10000);
        WaitFor(() => ChaosStat.SDisc == ChaosStat.SConn && ChaosStat.CDisc == ChaosStat.CConn, 3000);

        bool ok = c && s
                  && ChaosStat.SConn > 0
                  && ChaosStat.SConn == ChaosStat.SDisc
                  && ChaosStat.CConn == ChaosStat.CDisc
                  && ChaosStat.Exc == 0
                  && client.Sessions.Count == 0 && server.Sessions.Count == 0;

        Console.WriteLine($"    chaos zb={zeroByte} tp={startOnThreadPool}: msgs={ChaosStat.Msgs} server {ChaosStat.SConn}/{ChaosStat.SDisc} client {ChaosStat.CConn}/{ChaosStat.CDisc} exc={ChaosStat.Exc}");
        return ok;
    }

    private sealed class MemLogger : INetLogger
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<(NetLogLevel Level, string Message, Exception? Exception)> Items = new();
        public bool IsEnabled(NetLogLevel level) => true;
        public void Log(NetLogLevel level, string message, Exception? exception = null) => Items.Enqueue((level, message, exception));
        public bool Has(NetLogLevel level, string contains, bool withException = false)
            => Items.Any(i => i.Level == level && i.Message.Contains(contains) && (!withException || i.Exception != null));
    }

    private sealed class ThrowingLogger : INetLogger
    {
        public bool IsEnabled(NetLogLevel level) => throw new InvalidOperationException("logger broken");
        public void Log(NetLogLevel level, string message, Exception? exception = null) => throw new InvalidOperationException("logger broken");
    }

    private sealed class ThrowSession : PacketSession
    {
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) => throw new InvalidOperationException("boom");
    }

    private static int s_capConnected;

    private sealed class CapSession : PacketSession
    {
        protected override void OnConnected() => Interlocked.Increment(ref s_capConnected);
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) { }
    }

    private sealed class MidBufferSession : PacketSession
    {
        public MidBufferSession() : base(6000) { }
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) { }
    }

    // ---- JobQueue 통합: 세션들이 하나의 방(JobQueue)으로 작업을 보내 직렬 처리 ----
    private static readonly JobQueue s_room = new JobQueue();
    private static int s_roomCount;      // 일부러 비원자 연산: 직렬 실행이 깨지면 값이 유실된다
    private static int s_roomInside;
    private static int s_roomOverlap;

    private sealed class RoomSession : PacketSession
    {
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
        {
            s_room.Push(() =>
            {
                if (Interlocked.Increment(ref s_roomInside) != 1)
                    Interlocked.Increment(ref s_roomOverlap);

                s_roomCount++;
                Interlocked.Decrement(ref s_roomInside);
            });
        }
    }

    private sealed class CountingQueue : JobQueue
    {
        public int Exceptions;
        public CountingQueue(int maxJobsPerRun = 256) : base(maxJobsPerRun) { }
        protected override void OnException(Exception exception) => Interlocked.Increment(ref Exceptions);
    }

    // ---- Session.Jobs / 통계 / 송신 포함 유휴 ----
    private static int s_jobPackets;
    private static int s_jobDisconnectRan;
    private static JobSession? s_jobSession;

    private sealed class JobSession : PacketSession
    {
        protected override void OnConnected()
        {
            s_jobSession = this;
            Send(new byte[10]);   // 통계 확인용: 송신 10바이트
        }

        protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
            => Jobs.Push(() => Interlocked.Increment(ref s_jobPackets));

        protected override void OnDisconnected(CloseReason reason)
            => Jobs.Push(() => Interlocked.Increment(ref s_jobDisconnectRan));
    }

    private static readonly System.Collections.Concurrent.ConcurrentQueue<CloseReason> s_pushReasons = new();

    // 연결 후 일정 시간 동안 100ms 마다 송신만 하는 세션(상대는 아무것도 보내지 않음)
    private sealed class PushOnlySession : PacketSession
    {
        private Timer? _timer;
        private long _until;

        protected override void OnConnected()
        {
            _until = Environment.TickCount64 + 1500;
            _timer = new Timer(_ =>
            {
                if (Environment.TickCount64 < _until)
                    Send(new byte[] { 1, 2, 3, 4 });
            }, null, 100, 100);
        }

        protected override void OnDisconnected(CloseReason reason)
        {
            _timer?.Dispose();
            s_pushReasons.Enqueue(reason);
        }

        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) { }
    }

    // ---- UdpService ----
    private sealed class UdpEchoService : UdpService
    {
        public UdpEchoService(UdpServiceOptions? options = null)
            : base(new IPEndPoint(IPAddress.Loopback, 0), options) { }

        public int Throwing;   // 1 이면 OnRecv 에서 예외
        public int ExceptionCount;

        protected override void OnRecv(ReadOnlySpan<byte> data, IPEndPoint remote)
        {
            if (Volatile.Read(ref Throwing) == 1)
                throw new InvalidOperationException("udp boom");

            SendTo(data, remote);
        }

        protected override void OnException(Exception exception) => Interlocked.Increment(ref ExceptionCount);
    }

    private sealed class UdpCollectService : UdpService
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte[]> Received = new();

        public UdpCollectService() : base(new IPEndPoint(IPAddress.Loopback, 0)) { }

        protected override void OnRecv(ReadOnlySpan<byte> data, IPEndPoint remote)
            => Received[BitConverter.ToInt32(data)] = data.ToArray();
    }

    // ---- TLS ----
    private static readonly System.Collections.Concurrent.ConcurrentQueue<CloseReason> s_tlsServerReasons = new();
    private static readonly System.Collections.Concurrent.ConcurrentQueue<CloseReason> s_tlsClientReasons = new();

    private static X509Certificate2 MakeTestCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());
        using X509Certificate2 temp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(temp.Export(X509ContentType.Pfx));
    }

    private sealed class TlsEchoSession : TlsPacketSession
    {
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) => Send(packet);
        protected override void OnDisconnected(CloseReason reason) => s_tlsServerReasons.Enqueue(reason);
    }

    private sealed class TlsCollectSession : TlsPacketSession
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte[]> Received = new();
        public int Disconnected;

        protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
            => Received[BitConverter.ToInt32(packet.Slice(PacketHeader.HeaderSize))] = packet.ToArray();

        protected override void OnDisconnected(CloseReason reason)
        {
            s_tlsClientReasons.Enqueue(reason);
            Interlocked.Increment(ref Disconnected);
        }
    }

    // 연결 즉시 5000바이트 패킷 20개를 보내고 DisconnectAfterSend
    private sealed class TlsPushThenCloseSession : TlsPacketSession
    {
        protected override void OnConnected()
        {
            for (int i = 0; i < 20; i++)
            {
                var packet = new byte[5000];
                PacketHeader.Write(packet, (ushort)packet.Length, 1);
                BitConverter.TryWriteBytes(packet.AsSpan(PacketHeader.HeaderSize), i);
                Send(packet);
            }
            DisconnectAfterSend(CloseReason.Local);
        }

        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) { }
        protected override void OnDisconnected(CloseReason reason) => s_tlsServerReasons.Enqueue(reason);
    }

    // TLS: 연결 직후 다른 스레드에서 Send 를 쏟아붓는 동안 DisconnectAfterSend 가 끼어든다
    private sealed class TlsRaceSession : TlsPacketSession
    {
        protected override void OnConnected()
        {
            var sender = new Thread(() =>
            {
                var packet = new byte[64];
                PacketHeader.Write(packet, (ushort)packet.Length, 1);
                for (int i = 0; i < 200; i++)
                    Send(packet);
            });
            sender.Start();
            Thread.SpinWait(Random.Shared.Next(0, 4000));
            DisconnectAfterSend(CloseReason.Local);
        }

        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) { }
        protected override void OnDisconnected(CloseReason reason) => s_tlsServerReasons.Enqueue(reason);
    }

    private static bool WaitFor(Func<bool> cond, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond())
                return true;
            Thread.Sleep(10);
        }

        return cond();
    }

    private static int Main(string[] args)
    {
        s_zeroByte = Array.IndexOf(args, "nozb") < 0;
        Console.WriteLine($"ZeroByteRecv={s_zeroByte}");
        if (Array.IndexOf(args, "bench") >= 0)
        {
            Bench.Run(s_zeroByte);
            return 0;
        }

        const int clientCount = 20;
        const int packetCount = 200;
        int failures = 0;

        void Check(string name, bool ok)
        {
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
            if (!ok)
                failures++;
        }

        // 서버
        var server = new ServerService(new IPEndPoint(IPAddress.Loopback, 0),
            () => new EchoSession(),
            MakeOptions());

        Check("서버 Start", server.Start());
        IPEndPoint serverAddr = server.LocalEndPoint!;

        // 클라이언트
        int nextIndex = -1;
        var client = new ClientService(serverAddr,
            () => new TestClient(Interlocked.Increment(ref nextIndex), packetCount),
            MakeOptions());

        var sw = Stopwatch.StartNew();
        Check("클라이언트 연결 게시", client.ConnectSessions(clientCount) == clientCount);
        Check("에코 전송/검증 완료", WaitFor(() => Volatile.Read(ref s_clientDone) == clientCount, 60000));
        sw.Stop();

        Console.WriteLine($"    echo {s_echoBytes / (1024.0 * 1024):F1} MB in {sw.ElapsedMilliseconds} ms, bad={s_bad}");
        Check("에코 데이터 무결성", s_bad == 0);
        Check("서버 세션 수", server.Sessions.Count == clientCount && s_serverConnected == clientCount);

        // 브로드캐스트
        SendBuffer bc = SendBufferManager.Open(PacketHeader.HeaderSize + 16);
        PacketHeader.Write(bc.Buffer, PacketHeader.HeaderSize + 16, BroadcastId);
        server.Sessions.Broadcast(bc.Close(PacketHeader.HeaderSize + 16));
        Check("브로드캐스트 수신", WaitFor(() => Volatile.Read(ref s_broadcastRecv) == clientCount, 5000));

        // 연결 실패 통지
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var deadAddr = (IPEndPoint)probe.LocalEndpoint;
        probe.Stop();

        var failClient = new ClientService(deadAddr, () => new TestClient(999, 1));
        failClient.ConnectOneMoreSession();
        Check("연결 실패 통지(OnConnectFailed)", WaitFor(() => Volatile.Read(ref s_connectFailed) == 1, 5000));
        Check("실패한 세션 정리", WaitFor(() => failClient.Sessions.Count == 0, 2000));

        // 전송 후 종료: 큰 응답이 끝까지 도착한 뒤 끊겨야 한다.
        var gbServer = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new GoodbyeSession(), MakeOptions());
        gbServer.Start();
        var gbClient = new ClientService(gbServer.LocalEndPoint!, () => new GoodbyeClient(), MakeOptions());
        gbClient.ConnectOneMoreSession();
        Check("DisconnectAfterSend 종료 감지", WaitFor(() => Volatile.Read(ref s_goodbyeClosed) == 1, 5000));
        Check("DisconnectAfterSend 응답 전량 수신", s_goodbyeBytes == 60000);
        gbClient.Close(2000); gbServer.Close(2000);

        // 송신 큐 상한: 상대가 읽지 않으면 SendQueueOverflow 로 끊겨야 한다.
        var floodServer = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new FloodSession(), MakeOptions(1 * 1024 * 1024));
        floodServer.Start();
        using (var silent = new TcpClient())
        {
            silent.Connect(floodServer.LocalEndPoint!);
            WaitFor(() => Volatile.Read(ref s_floodAccepted) == 1, 5000);
            Check("송신 큐 상한 초과 시 연결 종료", WaitFor(() => floodServer.Sessions.Count == 0, 10000));
        }
        Check("종료 원인 SendQueueOverflow", s_lastServerReason == CloseReason.SendQueueOverflow);
        floodServer.Close(2000);

        // PacketSession 수신 버퍼 검증
        bool threw = false;
        try { _ = new SmallBufferSession(); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Check("PacketSession 최대 패킷보다 작은 버퍼 거부", threw);

        // 유휴 / slowloris 타임아웃
        var idleServer = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new ReasonSession(),
            new NetServiceOptions { IdleTimeoutMs = 500, ZeroByteRecv = s_zeroByte });
        idleServer.Start();

        using (var silent = new TcpClient())
        {
            silent.Connect(idleServer.LocalEndPoint!);
            Check("아무것도 보내지 않는 연결 → IdleTimeout", WaitReason(CloseReason.IdleTimeout, 3000));
        }

        using (var drip = new TcpClient())
        {
            drip.Connect(idleServer.LocalEndPoint!);
            NetworkStream ns = drip.GetStream();
            var header = new byte[PacketHeader.HeaderSize];
            PacketHeader.Write(header, 1000, 1);       // 1000바이트 패킷을 선언하고
            ns.Write(header, 0, header.Length);

            var sw2 = Stopwatch.StartNew();
            bool dropped = false;
            while (sw2.ElapsedMilliseconds < 4000 && !dropped)
            {
                try { ns.WriteByte(0); } catch (Exception) { dropped = true; }   // 바이트를 하나씩 흘린다
                dropped |= WaitReason(CloseReason.IdleTimeout, 100);
            }
            Check("바이트를 흘리는 연결(slowloris) → IdleTimeout", dropped);
        }

        using (var active = new TcpClient())
        {
            active.Connect(idleServer.LocalEndPoint!);
            NetworkStream ns = active.GetStream();
            var ping = new byte[PacketHeader.HeaderSize];
            PacketHeader.Write(ping, PacketHeader.HeaderSize, 1);
            for (int i = 0; i < 10; i++)
            {
                ns.Write(ping, 0, ping.Length);
                Thread.Sleep(150);
            }
            Check("정상적으로 통신하는 연결은 유지", idleServer.Sessions.Count == 1);
        }
        idleServer.Close(3000);

        // 정상 종료 타임아웃: 상대가 닫지 않아도 정리되고, 상대는 데이터 뒤에 FIN 을 받는다
        var lingerServer = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new LingerSession(),
            new NetServiceOptions { GracefulCloseTimeoutMs = 300, ZeroByteRecv = s_zeroByte });
        lingerServer.Start();
        using (var lazy = new TcpClient())
        {
            lazy.Connect(lingerServer.LocalEndPoint!);
            NetworkStream ns = lazy.GetStream();
            var got = new byte[16];
            int total = 0, n;
            while (total < 8 && (n = ns.Read(got, total, got.Length - total)) > 0)
                total += n;
            int eof = ns.Read(got, 0, got.Length);
            Check("DisconnectAfterSend: 데이터 수신 후 FIN", total == 8 && eof == 0);
            Check("DisconnectAfterSend: 상대가 닫지 않아도 타임아웃 후 정리", WaitReason(CloseReason.Local, 3000));
        }
        lingerServer.Close(2000);

        // 무작위 종료 부하(회귀)
        Check("무작위 종료 부하 (ZeroByteRecv 켬)", RunChaos(true, false, 2500));
        Check("무작위 종료 부하 (ZeroByteRecv 끔)", RunChaos(false, false, 2500));
        Check("무작위 종료 부하 (ThreadPool 시작)", RunChaos(true, true, 2500));

        // 로깅 훅: 서비스 시작/콜백 예외/비정상 종료 사유가 로거로 전달된다
        var memLog = new MemLogger();
        var logServer = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new ThrowSession(),
            new NetServiceOptions { Logger = memLog, ZeroByteRecv = s_zeroByte });
        logServer.Start();
        using (var tc = new TcpClient())
        {
            tc.Connect(logServer.LocalEndPoint!);
            var pkt = new byte[PacketHeader.HeaderSize];
            PacketHeader.Write(pkt, PacketHeader.HeaderSize, 1);
            tc.GetStream().Write(pkt, 0, pkt.Length);
            Check("로그: 콜백 예외가 Error 로 전달", WaitFor(() => memLog.Has(NetLogLevel.Error, "콜백 예외", true), 3000));
            Check("로그: 예외 종료가 Warning(ProtocolError)", WaitFor(() => memLog.Has(NetLogLevel.Warning, "ProtocolError"), 3000));
        }
        Check("로그: 리슨 시작 Info", memLog.Has(NetLogLevel.Info, "리슨 시작"));
        Check("로그: 세션 연결 Trace", memLog.Has(NetLogLevel.Trace, "연결됨"));
        logServer.Close(2000);
        Check("로그: 서버 종료 Info", memLog.Has(NetLogLevel.Info, "서버 종료 시작"));

        var memLog2 = new MemLogger();
        var floodLogServer = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new FloodSession(),
            new NetServiceOptions { Logger = memLog2, MaxSendQueueBytes = 1024 * 1024, ZeroByteRecv = s_zeroByte });
        floodLogServer.Start();
        using (var silent2 = new TcpClient())
        {
            silent2.Connect(floodLogServer.LocalEndPoint!);
            Check("로그: 송신 큐 초과가 Warning 으로 전달", WaitFor(() => memLog2.Has(NetLogLevel.Warning, "SendQueueOverflow"), 10000));
        }
        floodLogServer.Close(2000);

        // 로거가 예외를 던져도 서비스는 정상 동작
        var badLogServer = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new ReasonSession(),
            new NetServiceOptions { Logger = new ThrowingLogger(), ZeroByteRecv = s_zeroByte });
        bool badStarted = badLogServer.Start();
        bool echoed = false;
        using (var tc2 = new TcpClient())
        {
            tc2.Connect(badLogServer.LocalEndPoint!);
            NetworkStream ns = tc2.GetStream();
            var pkt = new byte[8];
            PacketHeader.Write(pkt, 8, 1);
            ns.Write(pkt, 0, pkt.Length);
            var back = new byte[8];
            int got = 0, nn;
            ns.ReadTimeout = 3000;
            while (got < 8 && (nn = ns.Read(back, got, 8 - got)) > 0)
                got += nn;
            echoed = got == 8;
        }
        Check("로거가 예외를 던져도 서비스 정상", badStarted && echoed && badLogServer.Close(2000));

        // MaxSessionCount: 100개가 동시에 접속해도 정확히 상한(10)까지만 수락, 닫히면 슬롯 반환
        var capServer = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new CapSession(),
            new NetServiceOptions { MaxSessionCount = 10, AcceptCount = 16, ZeroByteRecv = s_zeroByte });
        capServer.Start();
        var capClients = new List<TcpClient>();
        System.Threading.Tasks.Parallel.For(0, 100, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 32 }, _ =>
        {
            var c = new TcpClient();
            try { c.Connect(capServer.LocalEndPoint!); } catch (SocketException) { }
            lock (capClients) capClients.Add(c);
        });
        WaitFor(() => Volatile.Read(ref s_capConnected) >= 10, 5000);
        Thread.Sleep(500); // 초과 수락이 있었다면 드러날 시간
        Check("MaxSessionCount 정확히 준수", Volatile.Read(ref s_capConnected) == 10 && capServer.Sessions.Count == 10);
        foreach (var c in capClients) c.Dispose();
        WaitFor(() => capServer.Sessions.Count == 0, 5000);
        using (var again = new TcpClient())
        {
            again.Connect(capServer.LocalEndPoint!);
            Check("세션이 닫히면 슬롯 반환", WaitFor(() => Volatile.Read(ref s_capConnected) == 11, 5000));
        }
        capServer.Close(2000);

        // 수신 버퍼 용량 정렬: 2의 거듭제곱으로 올림
        Check("수신 버퍼 용량 2의 거듭제곱", new RecvBuffer(10240).Capacity == 131072 && new RecvBuffer(4096).Capacity == 65536);
        bool midOk = false;
        try { _ = new MidBufferSession(); midOk = true; } catch (ArgumentOutOfRangeException) { }
        Check("PacketSession 올림 용량 기준 검증(6000 → 65536 허용)", midOk);

        // ConnectAsync / CloseAsync / Dispose
        var asyncServer = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new ReasonSession(),
            new NetServiceOptions { ZeroByteRecv = s_zeroByte });
        asyncServer.Start();
        var asyncClient = new ClientService(asyncServer.LocalEndPoint!, () => new ReasonSession(),
            new NetServiceOptions { ZeroByteRecv = s_zeroByte });

        Session connectedSession = asyncClient.ConnectAsync().GetAwaiter().GetResult();
        Check("ConnectAsync: 연결된 세션 반환", connectedSession.IsConnected && asyncClient.Sessions.Count == 1);

        bool sockEx = false;
        SocketError sockErr = SocketError.Success;
        var deadClient = new ClientService(deadAddr, () => new ReasonSession());
        try { deadClient.ConnectAsync().GetAwaiter().GetResult(); }
        catch (SocketException se) { sockEx = true; sockErr = se.SocketErrorCode; }
        Check("ConnectAsync: 실패 시 SocketException", sockEx && sockErr == SocketError.ConnectionRefused);
        Check("ConnectAsync: 실패한 세션 정리", WaitFor(() => deadClient.Sessions.Count == 0, 2000));

        var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        bool canceled = false;
        try { asyncClient.ConnectAsync(preCancelled.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { canceled = true; }
        Check("ConnectAsync: 이미 취소된 토큰", canceled && asyncClient.Sessions.Count == 1);

        var racing = new CancellationTokenSource();
        Task<Session> raceTask = asyncClient.ConnectAsync(racing.Token);
        racing.Cancel();
        bool raceOk;
        try { raceOk = raceTask.GetAwaiter().GetResult().IsConnected || true; }
        catch (OperationCanceledException) { raceOk = true; }
        catch (Exception) { raceOk = false; }
        Check("ConnectAsync: 연결 중 취소 (성공 또는 취소로만 완료)", raceOk);

        var closeTask = asyncClient.CloseAsync(3000);
        Check("CloseAsync: 클라이언트 정리", closeTask.GetAwaiter().GetResult() && asyncClient.Sessions.Count == 0);
        Check("CloseAsync: 서버 정리", asyncServer.CloseAsync(3000).GetAwaiter().GetResult() && asyncServer.Sessions.Count == 0);

        bool connectAfterClose = false;
        try { asyncClient.ConnectAsync().GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { connectAfterClose = true; }
        Check("ConnectAsync: 종료 후에는 InvalidOperationException", connectAfterClose);

        ServerService disposed;
        using (disposed = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new ReasonSession()))
        {
            disposed.Start();
        }
        Check("Dispose: 서비스 종료", disposed.IsClosing);
        var disposedAsync = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new ReasonSession());
        disposedAsync.Start();
        disposedAsync.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Check("DisposeAsync: 서비스 종료", disposedAsync.IsClosing);

        // JobQueue: 8개 생산자 × 20000 잡 — 동시 실행 없음, 생산자별 순서 보존, 전부 실행
        {
            var queue = new JobQueue(maxJobsPerRun: 64);
            const int producers = 8, perProducer = 20000;
            int inside = 0, overlap = 0, outOfOrder = 0, executed = 0;
            var last = new int[producers];
            Array.Fill(last, -1);

            var threads = new List<Thread>();
            for (int p = 0; p < producers; p++)
            {
                int producer = p;
                var th = new Thread(() =>
                {
                    for (int i = 0; i < perProducer; i++)
                    {
                        int seq = i;
                        queue.Push(() =>
                        {
                            if (Interlocked.Increment(ref inside) != 1)
                                Interlocked.Increment(ref overlap);
                            if (last[producer] != seq - 1)
                                Interlocked.Increment(ref outOfOrder);
                            last[producer] = seq;
                            executed++;                 // 비원자: 직렬 실행이 깨지면 유실된다
                            Interlocked.Decrement(ref inside);
                        });
                    }
                });
                threads.Add(th);
                th.Start();
            }
            foreach (var th in threads) th.Join();
            WaitFor(() => queue.PendingCount == 0, 20000);
            Check("JobQueue: 직렬 실행(동시 실행 0)", overlap == 0);
            Check("JobQueue: 생산자별 순서 보존", outOfOrder == 0);
            Check("JobQueue: 전부 실행 (양보 재스케줄 경로 포함)", executed == producers * perProducer);
        }

        // JobQueue: 예외가 나도 이후 잡은 계속 실행
        {
            var queue = new CountingQueue();
            int ran = 0;
            queue.Push(() => throw new InvalidOperationException("job boom"));
            queue.Push(() => Interlocked.Increment(ref ran));
            queue.Push(() => throw new InvalidOperationException("job boom 2"));
            queue.Push(() => Interlocked.Increment(ref ran));
            WaitFor(() => queue.PendingCount == 0, 3000);
            Check("JobQueue: 예외 격리", queue.Exceptions == 2 && ran == 2);
        }

        // JobTimer: 지연 순서/시간, 취소, 큐 직렬성
        {
            var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var started = Stopwatch.StartNew();
            var elapsed = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
            var timerQueue = new JobQueue();

            timerQueue.PushAfter(300, () => { order.Enqueue("300"); elapsed["300"] = started.ElapsedMilliseconds; });
            timerQueue.PushAfter(100, () => { order.Enqueue("100"); elapsed["100"] = started.ElapsedMilliseconds; });
            ScheduledJob cancelHandle = timerQueue.PushAfter(150, () => order.Enqueue("150-canceled"));
            timerQueue.PushAfter(200, () => { order.Enqueue("200"); elapsed["200"] = started.ElapsedMilliseconds; });
            cancelHandle.Cancel();

            WaitFor(() => order.Count >= 3, 3000);
            Thread.Sleep(200);
            string seq = string.Join(",", order);
            Check("JobTimer: 지연 순서와 취소", seq == "100,200,300" && cancelHandle.IsCanceled);
            Check("JobTimer: 지연 시간 정확도", elapsed["100"] >= 90 && elapsed["100"] < 400
                  && elapsed["200"] >= 190 && elapsed["300"] >= 290 && elapsed["300"] < 600);

            // 같은 큐의 긴 잡이 끝나기 전에는 만료된 지연 잡도 시작하지 않는다
            long longEnd = 0, delayedStart = 0;
            var serial = new JobQueue();
            serial.Push(() => { Thread.Sleep(400); longEnd = Stopwatch.GetTimestamp(); });
            serial.PushAfter(50, () => delayedStart = Stopwatch.GetTimestamp());
            WaitFor(() => Volatile.Read(ref delayedStart) != 0, 3000);
            Check("JobTimer: 큐 직렬성 유지(긴 잡 뒤에 실행)", delayedStart != 0 && delayedStart >= longEnd);

            // 큐 없이 예약하면 ThreadPool 에서 실행
            int direct = 0;
            JobTimer.Shared.Reserve(50, () => Interlocked.Increment(ref direct));
            Check("JobTimer: 큐 없이 예약", WaitFor(() => Volatile.Read(ref direct) == 1, 2000));

            // Dispose 후에는 예약 불가, 대기 중이던 예약은 실행되지 않는다
            var own = new JobTimer();
            int neverRan = 0;
            own.Reserve(200, () => Interlocked.Increment(ref neverRan));
            own.Dispose();
            bool disposedThrows = false;
            try { own.Reserve(10, () => { }); }
            catch (ObjectDisposedException) { disposedThrows = true; }
            Thread.Sleep(400);
            Check("JobTimer: Dispose 후 예약 거부/버림", disposedThrows && neverRan == 0);
        }

        // 세션 → JobQueue 통합: 8개 연결이 보낸 8000개 패킷이 한 큐에서 직렬 처리
        {
            var roomServer = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new RoomSession(),
                new NetServiceOptions { ZeroByteRecv = s_zeroByte });
            roomServer.Start();

            var clients = new List<TcpClient>();
            var tasks = new List<Task>();
            for (int c = 0; c < 8; c++)
            {
                var tc = new TcpClient();
                tc.Connect(roomServer.LocalEndPoint!);
                clients.Add(tc);
                tasks.Add(Task.Run(() =>
                {
                    var one = new byte[PacketHeader.HeaderSize];
                    PacketHeader.Write(one, PacketHeader.HeaderSize, 1);
                    var ns = tc.GetStream();
                    for (int i = 0; i < 1000; i++)
                        ns.Write(one, 0, one.Length);
                }));
            }
            Task.WaitAll(tasks.ToArray());
            WaitFor(() => Volatile.Read(ref s_roomCount) == 8000, 10000);
            Check("세션 → JobQueue: 8000개 모두 직렬 처리", s_roomCount == 8000 && s_roomOverlap == 0);
            foreach (var tc in clients) tc.Dispose();
            roomServer.Close(3000);
        }

        // JobTimer: 취소가 쌓이면 힙을 정리한다
        {
            using var own = new JobTimer();
            var handles = new List<ScheduledJob>();
            int ranKept = 0;
            for (int i = 0; i < 1000; i++)
                handles.Add(own.Reserve(60000, () => { }));
            for (int i = 0; i < 950; i++)
                handles[i].Cancel();
            for (int i = 0; i < 5; i++)
                own.Reserve(100, () => Interlocked.Increment(ref ranKept));

            Check("JobTimer: 취소 누적 시 힙 정리", own.ScheduledCount <= 200);
            Check("JobTimer: 정리 후에도 정상 예약은 실행", WaitFor(() => Volatile.Read(ref ranKept) == 5, 2000));
        }

        // JobTimer: 고해상도 모드 정확도
        {
            using var precise = new JobTimer(highResolution: true);
            var preciseWatch = Stopwatch.StartNew();
            var errors = new System.Collections.Concurrent.ConcurrentBag<long>();
            for (int i = 0; i < 20; i++)
            {
                long due = 30 + i * 7;
                precise.Reserve((int)due, () => errors.Add(preciseWatch.ElapsedMilliseconds - due));
            }
            WaitFor(() => errors.Count == 20, 3000);
            long max = errors.Count == 0 ? long.MaxValue : errors.Max();
            long min = errors.Count == 0 ? long.MinValue : errors.Min();
            Check("JobTimer(highResolution): 20개 모두 실행, 오차 -2~+20ms", errors.Count == 20 && min >= -2 && max <= 20);
        }

        // JobQueue.Close: 새 잡 거부, 대기 잡은 실행 / discard 이면 버림
        {
            var q = new JobQueue();
            int ran = 0;
            q.Push(() => Thread.Sleep(150));
            for (int i = 0; i < 5; i++) q.Push(() => Interlocked.Increment(ref ran));
            q.Close();
            bool rejected = !q.Push(() => Interlocked.Increment(ref ran));
            Check("JobQueue.Close: 새 Push 거부", rejected && q.IsClosed);
            Check("JobQueue.Close: 이미 들어간 잡은 끝까지 실행", WaitFor(() => Volatile.Read(ref ran) == 5, 3000));
            Check("JobQueue.Close: 닫힌 큐의 PushAfter 는 취소된 핸들", q.PushAfter(10, () => { }).IsCanceled);

            var q2 = new JobQueue();
            int ran2 = 0;
            q2.Push(() => Thread.Sleep(200));
            for (int i = 0; i < 5; i++) q2.Push(() => Interlocked.Increment(ref ran2));
            q2.Close(discardPending: true);
            WaitFor(() => q2.PendingCount == 0, 3000);
            Check("JobQueue.Close(discardPending): 시작 전 잡은 버림", ran2 == 0);
        }

        // Session.Jobs + 통계
        {
            s_jobPackets = 0; s_jobDisconnectRan = 0; s_jobSession = null;
            var js = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new JobSession(),
                new NetServiceOptions { ZeroByteRecv = s_zeroByte, MaxSessionCount = 1 });
            js.Start();

            using (var tc = new TcpClient())
            {
                tc.Connect(js.LocalEndPoint!);
                var ns = tc.GetStream();
                var one = new byte[PacketHeader.HeaderSize];
                PacketHeader.Write(one, PacketHeader.HeaderSize, 1);
                for (int i = 0; i < 100; i++) ns.Write(one, 0, one.Length);

                var tenBuf = new byte[10];
                int got = 0;
                tc.ReceiveTimeout = 3000;
                while (got < 10) { int n = ns.Read(tenBuf, got, 10 - got); if (n <= 0) break; got += n; }

                WaitFor(() => Volatile.Read(ref s_jobPackets) == 100, 3000);
                JobQueue? sessionJobs = s_jobSession?.Jobs;
                Check("Session.Jobs: 세션 큐에서 100개 처리", s_jobPackets == 100);

                // 두 번째 접속은 MaxSessionCount=1 로 거부된다
                using (var tc2 = new TcpClient())
                {
                    tc2.Connect(js.LocalEndPoint!);
                    Thread.Sleep(300);
                }

                NetStatsSnapshot st = js.GetStats();
                Check("통계: 현재 세션/누적 접속/거부", st.CurrentSessions == 1 && st.TotalConnected == 1 && st.TotalRejected == 1);
                Check("통계: 수신 400B / 송신 10B", st.BytesReceived == 400 && st.BytesSent == 10);

                tc.Close();
                WaitFor(() => js.Sessions.Count == 0, 3000);
                WaitFor(() => Volatile.Read(ref s_jobDisconnectRan) == 1, 3000);
                Check("Session.Jobs: OnDisconnected 에서 넣은 잡도 실행", s_jobDisconnectRan == 1);
                Check("Session.Jobs: 종료 후 큐가 닫혀 새 Push 거부", sessionJobs != null && sessionJobs.IsClosed && !sessionJobs.Push(() => { }));
            }

            NetStatsSnapshot after = js.GetStats();
            Check("통계: 종료 후 누적값 유지", after.CurrentSessions == 0 && after.TotalDisconnected == 1
                  && after.BytesReceived == 400 && after.BytesSent == 10);
            js.Close(3000);
        }

        // 공유 큐 지정(AttachJobQueue): 세션이 끝나도 닫히지 않음
        {
            var shared = new JobQueue();
            var sessionForAttach = new JobSession();
            sessionForAttach.AttachJobQueue(shared);
            bool throwsTwice = false;
            try { sessionForAttach.AttachJobQueue(new JobQueue()); }
            catch (InvalidOperationException) { throwsTwice = true; }
            Check("AttachJobQueue: 공유 큐 지정, 중복 지정은 예외", ReferenceEquals(sessionForAttach.Jobs, shared) && throwsTwice && !shared.IsClosed);
        }

        // IdleIncludesSend: 송신만 하는 연결이 하트비트 없이도 유지되는지
        foreach (bool includeSend in new[] { false, true })
        {
            while (s_pushReasons.TryDequeue(out _)) { }

            var ps = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new PushOnlySession(),
                new NetServiceOptions { ZeroByteRecv = s_zeroByte, IdleTimeoutMs = 600, IdleIncludesSend = includeSend });
            ps.Start();

            using var tc = new TcpClient();
            tc.Connect(ps.LocalEndPoint!);
            var drain = Task.Run(() =>
            {
                var buf = new byte[64];
                try { while (tc.GetStream().Read(buf, 0, buf.Length) > 0) { } } catch (Exception) { }
            });

            Thread.Sleep(1200);   // 푸시가 진행되는 구간(0~1.5s)
            bool aliveMid = ps.Sessions.Count == 1;
            bool reasonArrived = WaitFor(() => !s_pushReasons.IsEmpty, 4000);
            s_pushReasons.TryPeek(out CloseReason reason);

            if (includeSend)
                Check("IdleIncludesSend=true: 송신 중에는 유지, 송신이 멈추면 IdleTimeout", aliveMid && reasonArrived && reason == CloseReason.IdleTimeout);
            else
                Check("IdleIncludesSend=false: 수신이 없으면 송신 중이어도 IdleTimeout", !aliveMid && reasonArrived && reason == CloseReason.IdleTimeout);

            ps.Close(3000);
        }

        // UdpService: 에코 무결성, 통계, 예외 격리, 종료
        {
            using var udpServer = new UdpEchoService();
            udpServer.Start();
            using var udpClient = new UdpCollectService();
            udpClient.Start();

            const int total = 200;
            var expected = new Dictionary<int, byte[]>();
            var rnd = new Random(7);
            for (int i = 0; i < total; i++)
            {
                var payload = new byte[4 + rnd.Next(0, 1400)];
                rnd.NextBytes(payload);
                BitConverter.TryWriteBytes(payload, i);
                expected[i] = payload;
            }

            bool sendOk = true;
            for (int batch = 0; batch < total; batch += 20)
            {
                for (int i = batch; i < batch + 20; i++)
                    sendOk &= udpClient.SendTo(expected[i], udpServer.LocalEndPoint!);

                WaitFor(() => udpClient.Received.Count >= batch + 20, 3000);
            }

            bool intact = udpClient.Received.Count == total;
            foreach (var pair in expected)
                intact &= udpClient.Received.TryGetValue(pair.Key, out var got) && got.AsSpan().SequenceEqual(pair.Value);

            long expectedBytes = expected.Values.Sum(b => (long)b.Length);
            Check("UdpService: 200개 에코 무결성", sendOk && intact);
            Check("UdpService: 통계(수신/송신 개수·바이트)",
                  udpServer.DatagramsReceived == total && udpServer.DatagramsSent == total
                  && udpServer.BytesReceived == expectedBytes && udpClient.BytesSent == expectedBytes
                  && udpServer.SendFailures == 0);

            // OnRecv 예외는 OnException 으로 가고 수신은 계속된다
            Volatile.Write(ref udpServer.Throwing, 1);
            udpClient.SendTo(new byte[] { 1, 0, 0, 0, 9 }, udpServer.LocalEndPoint!);
            WaitFor(() => Volatile.Read(ref udpServer.ExceptionCount) == 1, 2000);
            Volatile.Write(ref udpServer.Throwing, 0);
            udpClient.Received.Clear();
            udpClient.SendTo(new byte[] { 5, 0, 0, 0, 1, 2 }, udpServer.LocalEndPoint!);
            Check("UdpService: OnRecv 예외 격리 후 수신 계속",
                  WaitFor(() => udpClient.Received.ContainsKey(5), 2000) && udpServer.ExceptionCount == 1);

            // 작은 데이터그램(헤더 크기 미만)도 정상 전달, 빈 데이터그램도 크래시 없음
            udpClient.SendTo(ReadOnlySpan<byte>.Empty, udpServer.LocalEndPoint!);
            Thread.Sleep(200);

            bool closedInTime = udpServer.Close(3000);
            Check("UdpService: Close 완료 및 이후 SendTo 거부",
                  closedInTime && !udpServer.SendTo(new byte[] { 1 }, udpClient.LocalEndPoint!));

            // 존재하지 않는 포트로 보내도(ICMP 도달 불가) 수신 루프가 죽지 않는다
            udpClient.SendTo(new byte[] { 7, 0, 0, 0 }, new IPEndPoint(IPAddress.Loopback, 9));
            Thread.Sleep(200);
            using var udpPeer = new UdpEchoService();
            udpPeer.Start();
            udpClient.Received.Clear();
            udpClient.SendTo(new byte[] { 8, 0, 0, 0 }, udpPeer.LocalEndPoint!);
            Check("UdpService: 도달 불가 이후에도 수신 유지", WaitFor(() => udpClient.Received.ContainsKey(8), 2000));

            bool asyncClosed = udpClient.CloseAsync(3000).GetAwaiter().GetResult();
            Check("UdpService: CloseAsync", asyncClosed);
        }

        // ---- TLS ----
        {
            X509Certificate2 cert = MakeTestCert();
            var acceptAll = new System.Net.Security.RemoteCertificateValidationCallback((_, _, _, _) => true);
            var tlsLoop = new IPEndPoint(IPAddress.Loopback, 0);

            TlsServiceOptions ServerOpts(int handshakeMs = 5000, int maxSessions = 100, int idleMs = 0) => new TlsServiceOptions
            {
                ServerCertificate = cert, ZeroByteRecv = s_zeroByte, HandshakeTimeoutMs = handshakeMs,
                MaxSessionCount = maxSessions, IdleTimeoutMs = idleMs,
            };
            TlsServiceOptions ClientOpts(System.Net.Security.RemoteCertificateValidationCallback? cb = null) => new TlsServiceOptions
            {
                TargetHost = "localhost", ZeroByteRecv = s_zeroByte, RemoteCertificateValidation = cb ?? acceptAll,
            };

            // 에코 무결성 + 협상 정보 + 통계
            while (s_tlsServerReasons.TryDequeue(out _)) { }
            while (s_tlsClientReasons.TryDequeue(out _)) { }
            var tlsServer = new TlsServerService(tlsLoop, () => new TlsEchoSession(), ServerOpts());
            tlsServer.Start();
            var tlsClient = new TlsClientService(tlsServer.LocalEndPoint!, () => new TlsCollectSession(), ClientOpts());
            var cs = (TlsCollectSession)tlsClient.ConnectAsync().GetAwaiter().GetResult();

            const int tlsTotal = 300;
            var sent = new Dictionary<int, byte[]>();
            var rng = new Random(11);
            for (int i = 0; i < tlsTotal; i++)
            {
                int size = PacketHeader.HeaderSize + 4 + rng.Next(0, i % 10 == 0 ? 60000 : 3000);
                var packet = new byte[size];
                rng.NextBytes(packet);
                PacketHeader.Write(packet, (ushort)size, 1);
                BitConverter.TryWriteBytes(packet.AsSpan(PacketHeader.HeaderSize), i);
                sent[i] = packet;
                cs.Send(packet);
            }

            WaitFor(() => cs.Received.Count == tlsTotal, 15000);
            bool tlsIntact = cs.Received.Count == tlsTotal;
            foreach (var pair in sent)
                tlsIntact &= cs.Received.TryGetValue(pair.Key, out var got) && got.AsSpan().SequenceEqual(pair.Value);

            Check("TLS: 300개 에코 무결성(최대 60000B 패킷 포함)", tlsIntact);
            Check("TLS: 협상 정보(TLS1.2+, 서버 인증서 수신)",
                  (cs.SslProtocol == SslProtocols.Tls12 || cs.SslProtocol == SslProtocols.Tls13) && cs.RemoteCertificate != null);

            long totalBytes = sent.Values.Sum(b => (long)b.Length);
            WaitFor(() => cs.BytesReceived == totalBytes, 5000);
            NetStatsSnapshot tst = tlsServer.GetStats();
            Check("TLS: 통계(바이트·접속)", tst.CurrentSessions == 1 && tst.TotalConnected == 1
                  && tst.BytesReceived == totalBytes && cs.BytesSent == totalBytes && cs.BytesReceived == totalBytes);

            // 클라이언트가 먼저 끊으면 서버는 RemoteClosed
            cs.Disconnect();
            WaitFor(() => tlsServer.SessionCount == 0, 5000);
            s_tlsServerReasons.TryPeek(out CloseReason serverReason);
            Check("TLS: 클라이언트 종료 → 서버 세션 정리(RemoteClosed)", tlsServer.SessionCount == 0 && serverReason == CloseReason.RemoteClosed);

            // 서버 Close → 클라이언트가 끊김을 감지
            var cs2 = (TlsCollectSession)tlsClient.ConnectAsync().GetAwaiter().GetResult();
            bool serverClosed = tlsServer.Close(5000);
            WaitFor(() => Volatile.Read(ref cs2.Disconnected) == 1, 5000);
            Check("TLS: 서버 Close → 클라이언트 세션 종료", serverClosed && cs2.Disconnected == 1);
            tlsClient.Close(3000);

            // 인증서 검증 거부 → AuthenticationException, 슬롯 반환
            var s3 = new TlsServerService(tlsLoop, () => new TlsEchoSession(), ServerOpts(maxSessions: 1));
            s3.Start();
            var rejectingClient = new TlsClientService(s3.LocalEndPoint!, () => new TlsCollectSession(),
                ClientOpts((_, _, _, _) => false));
            bool authFailed = false;
            try { rejectingClient.ConnectAsync().GetAwaiter().GetResult(); }
            catch (AuthenticationException) { authFailed = true; }
            WaitFor(() => s3.SessionCount == 0 && rejectingClient.SessionCount == 0, 3000);
            Check("TLS: 서버 인증서 거부 → AuthenticationException, 세션/슬롯 정리", authFailed && s3.SessionCount == 0 && rejectingClient.SessionCount == 0);

            // 거부 후에도 같은 서버(MaxSessionCount=1)에 정상 접속 가능 = 슬롯이 반환됨
            var okClient = new TlsClientService(s3.LocalEndPoint!, () => new TlsCollectSession(), ClientOpts());
            var okSession = okClient.ConnectAsync().GetAwaiter().GetResult();
            Check("TLS: 실패한 핸드셰이크의 슬롯 반환(MaxSessionCount=1 에서 재접속)", okSession.IsConnected);
            okClient.Close(3000);

            // 평문 접속(핸드셰이크 불가)과 침묵하는 연결은 핸드셰이크 제한 시간 내에 정리
            using (var plain = new TcpClient())
            {
                plain.Connect(s3.LocalEndPoint!);
                plain.GetStream().Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 0, 10);
                Check("TLS: 평문 접속은 핸드셰이크 실패로 정리", WaitFor(() => s3.SessionCount == 0, 6000));
            }
            s3.Close(3000);

            var s4 = new TlsServerService(tlsLoop, () => new TlsEchoSession(), ServerOpts(handshakeMs: 500, maxSessions: 2));
            s4.Start();
            using (var silent1 = new TcpClient())
            using (var silent2 = new TcpClient())
            using (var silent3 = new TcpClient())
            {
                silent1.Connect(s4.LocalEndPoint!);
                silent2.Connect(s4.LocalEndPoint!);
                silent3.Connect(s4.LocalEndPoint!);   // MaxSessionCount=2 초과 → 거부
                Thread.Sleep(200);
                bool rejected = s4.GetStats().TotalRejected == 1;
                bool timedOut = WaitFor(() => s4.SessionCount == 0, 4000);
                Check("TLS: 핸드셰이크 시간 초과 정리 + MaxSessionCount 초과 거부", rejected && timedOut);
            }
            s4.Close(3000);

            // DisconnectAfterSend: 20 × 5000B 를 모두 받은 뒤 종료
            while (s_tlsServerReasons.TryDequeue(out _)) { }
            var s5 = new TlsServerService(tlsLoop, () => new TlsPushThenCloseSession(), ServerOpts());
            s5.Start();
            var c5 = new TlsClientService(s5.LocalEndPoint!, () => new TlsCollectSession(), ClientOpts());
            var cs5 = (TlsCollectSession)c5.ConnectAsync().GetAwaiter().GetResult();
            WaitFor(() => Volatile.Read(ref cs5.Disconnected) == 1, 6000);
            bool allGot = cs5.Received.Count == 20 && Enumerable.Range(0, 20).All(cs5.Received.ContainsKey);
            WaitFor(() => !s_tlsServerReasons.IsEmpty, 6000);
            s_tlsServerReasons.TryPeek(out CloseReason pushReason);
            Check("TLS: DisconnectAfterSend — 데이터 전부 전달 후 종료, 서버 사유=Local", allGot && cs5.Disconnected == 1 && pushReason == CloseReason.Local);
            c5.Close(3000);
            s5.Close(3000);

            // 유휴 시간 초과
            while (s_tlsServerReasons.TryDequeue(out _)) { }
            var s6 = new TlsServerService(tlsLoop, () => new TlsEchoSession(), ServerOpts(idleMs: 600));
            s6.Start();
            var c6 = new TlsClientService(s6.LocalEndPoint!, () => new TlsCollectSession(), ClientOpts());
            c6.ConnectAsync().GetAwaiter().GetResult();
            bool idleReason = WaitFor(() => !s_tlsServerReasons.IsEmpty, 4000);
            s_tlsServerReasons.TryPeek(out CloseReason idle);
            Check("TLS: 아무것도 보내지 않는 연결 → IdleTimeout", idleReason && idle == CloseReason.IdleTimeout);
            c6.Close(3000);
            s6.Close(3000);

            // 유휴 연결 20개가 CPU 를 쓰지 않는지(0바이트 읽기가 즉시 반환되며 도는 일이 없는지)
            var s7 = new TlsServerService(tlsLoop, () => new TlsEchoSession(), ServerOpts());
            s7.Start();
            var c7 = new TlsClientService(s7.LocalEndPoint!, () => new TlsCollectSession(), ClientOpts());
            for (int i = 0; i < 20; i++)
                c7.ConnectAsync().GetAwaiter().GetResult();
            Thread.Sleep(300);
            TimeSpan cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
            Thread.Sleep(1500);
            double cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds;
            Console.WriteLine($"       (유휴 TLS 연결 20개, 1.5초간 CPU {cpuMs:F0}ms)");
            Check("TLS: 유휴 연결이 CPU 를 쓰지 않음", cpuMs < 400 && s7.SessionCount == 20);
            c7.Close(3000);
            s7.Close(3000);

            // 공유 큐/세션 잡 큐
            var q = new JobQueue();
            var tlsJobSession = new TlsEchoSession();
            tlsJobSession.AttachJobQueue(q);
            Check("TLS: AttachJobQueue", ReferenceEquals(tlsJobSession.Jobs, q));
        }

        // JobQueue.Close 는 이 큐의 지연 예약을 취소한다
        {
            var q = new JobQueue();
            int ranDelayed = 0;
            var handles = new List<ScheduledJob>();
            for (int i = 0; i < 100; i++)
                handles.Add(q.PushAfter(200, () => Interlocked.Increment(ref ranDelayed)));
            q.Close();
            Thread.Sleep(500);
            Check("JobQueue.Close: 지연 예약 100개 취소, 실행 0", handles.All(h => h.IsCanceled) && ranDelayed == 0);
        }

        // 팩토리: UDP(델리게이트)·TLS 생성, UDP 통계 스냅샷
        {
            int hits = 0;
            UdpService? echo = null;
            echo = new DelegateUdpService(new IPEndPoint(IPAddress.Loopback, 0), (d, r) => echo!.SendTo(d, r));
            echo.Start();
            using var factoryProbe = new DelegateUdpService(new IPEndPoint(IPAddress.Loopback, 0), (_, _) => Interlocked.Increment(ref hits));
            factoryProbe.Start();
            factoryProbe.SendTo(new byte[] { 1, 2, 3 }, echo.LocalEndPoint!);
            bool factoryEchoed = WaitFor(() => Volatile.Read(ref hits) == 1, 2000);
            UdpStatsSnapshot us = echo.GetStats();
            Check("DelegateUdpService + UdpStatsSnapshot",
                  factoryEchoed && us.DatagramsReceived == 1 && us.DatagramsSent == 1 && us.BytesReceived == 3 && us.SendFailures == 0);
            echo.Close(2000);

            using X509Certificate2 fcert = MakeTestCert();
            var fserver = new TlsServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new TlsEchoSession(),
                new TlsServiceOptions { ServerCertificate = fcert });
            fserver.Start();
            var fclient = new TlsClientService(fserver.LocalEndPoint!, () => new TlsCollectSession(),
                new TlsServiceOptions { TargetHost = "localhost", RemoteCertificateValidation = (_, _, _, _) => true });
            TlsSession fs = fclient.ConnectAsync().GetAwaiter().GetResult();
            Check("TlsServer/ClientService 생성·접속", fs.IsConnected);
            fclient.Close(3000);
            fserver.Close(3000);
        }

        // TLS: Send 와 DisconnectAfterSend 경합 반복 — 사유는 항상 Local(정상 종료), SendError/RecvError 없음
        {
            using X509Certificate2 rcert = MakeTestCert();
            while (s_tlsServerReasons.TryDequeue(out _)) { }
            var rserver = new TlsServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new TlsRaceSession(),
                new TlsServiceOptions { ServerCertificate = rcert, ZeroByteRecv = s_zeroByte, GracefulCloseTimeoutMs = 2000 });
            rserver.Start();
            var rclient = new TlsClientService(rserver.LocalEndPoint!, () => new TlsCollectSession(),
                new TlsServiceOptions { TargetHost = "localhost", ZeroByteRecv = s_zeroByte, RemoteCertificateValidation = (_, _, _, _) => true });

            const int rounds = 60;
            for (int i = 0; i < rounds; i++)
            {
                var rs = (TlsCollectSession)rclient.ConnectAsync().GetAwaiter().GetResult();
                WaitFor(() => Volatile.Read(ref rs.Disconnected) == 1, 6000);
            }

            WaitFor(() => s_tlsServerReasons.Count == rounds, 8000);
            var reasons = s_tlsServerReasons.ToArray();
            bool onlyLocal = reasons.Length == rounds && reasons.All(r => r == CloseReason.Local);
            Check($"TLS: Send/DisconnectAfterSend 경합 {rounds}회 — 모두 정상 종료(Local)", onlyLocal);
            rclient.Close(3000);
            rserver.Close(3000);
        }

        // 종료
        Check("클라이언트 Close", client.Close(5000));
        Check("클라이언트 OnDisconnected", Volatile.Read(ref s_clientDisconnected) == clientCount);
        Check("서버 세션 정리", WaitFor(() => Volatile.Read(ref s_serverDisconnected) == clientCount, 5000));
        Check("서버 Close", server.Close(5000));

        // 서버가 먼저 닫히는 경우
        int before = s_clientDisconnected;
        var server2 = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new EchoSession());
        server2.Start();
        var client2 = new ClientService(server2.LocalEndPoint!, () => new TestClient(1000, 1));
        client2.ConnectSessions(5);
        WaitFor(() => client2.Sessions.Count == 5 && server2.Sessions.Count == 5, 5000);
        Check("서버 먼저 Close", server2.Close(5000));
        Check("클라이언트가 서버 종료를 감지", WaitFor(() => client2.Sessions.Count == 0, 5000));
        Check("클라이언트2 Close", client2.Close(1000));
        Check("OnDisconnected 정확히 1회씩", s_clientDisconnected - before == 5);

        Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }
}
