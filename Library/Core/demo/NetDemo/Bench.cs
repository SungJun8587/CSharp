// Bench.cs : UDP / TLS / TCP echo load measurements (run with the "bench" argument).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Core.Net;

namespace Core.Demo;

internal static class Bench
{
    private sealed class PlainEcho : PacketSession
    {
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) => Send(packet);
    }

    private sealed class TlsEcho : TlsPacketSession
    {
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) => Send(packet);
    }

    private static X509Certificate2 MakeCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());
        using X509Certificate2 temp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(temp.Export(X509ContentType.Pfx));
    }

    public static void Run(bool zeroByte)
    {
        Console.WriteLine($"[bench] ZeroByteRecv={zeroByte}, cores={Environment.ProcessorCount}");
        BenchUdp();
        BenchTlsHandshakeAndMemory(zeroByte);
        BenchEcho(zeroByte);
    }

    private static void BenchUdp()
    {
        var loop = new IPEndPoint(IPAddress.Loopback, 0);
        var opts = new UdpServiceOptions { ReceiveCount = 8, SocketRecvBufferSize = 8 << 20, SocketSendBufferSize = 8 << 20 };

        UdpService? server = null;
        server = new DelegateUdpService(loop, (data, remote) => server!.SendTo(data, remote), opts);
        server.Start();

        long got = 0;
        using var client = new DelegateUdpService(loop, (_, _) => Interlocked.Increment(ref got), opts);
        client.Start();

        const int senders = 4, perSender = 50000, size = 256;
        var payload = new byte[size];
        var sw = Stopwatch.StartNew();
        var threads = new List<Thread>();
        for (int t = 0; t < senders; t++)
        {
            var th = new Thread(() =>
            {
                for (int i = 0; i < perSender; i++)
                    client.SendTo(payload, server.LocalEndPoint!);
            });
            threads.Add(th);
            th.Start();
        }
        foreach (var th in threads) th.Join();
        double sendSec = sw.Elapsed.TotalSeconds;

        long last = -1;
        while (Interlocked.Read(ref got) != last)
        {
            last = Interlocked.Read(ref got);
            Thread.Sleep(500);
        }
        double totalSec = sw.Elapsed.TotalSeconds - 0.5;

        long total = (long)senders * perSender;
        UdpStatsSnapshot ss = server.GetStats();
        Console.WriteLine($"[UDP] 송신 {total:N0}개 x {size}B, 송신 시간 {sendSec:F2}s ({total / sendSec:N0} pkt/s)");
        Console.WriteLine($"      서버 수신 {ss.DatagramsReceived:N0}, 클라이언트 에코 수신 {got:N0} " +
                          $"(손실 {100.0 * (total - got) / total:F1}%), 서버 송신 실패 {ss.SendFailures}");
        Console.WriteLine($"      왕복 완료 처리량 {got / totalSec:N0} pkt/s");

        // 윈도 제어: 미완료 왕복이 window 개를 넘지 않게 보내 손실 없이 지속 가능한 처리량을 잰다.
        const int window = 1000, windowTotal = 200000;
        long gotBase = Interlocked.Read(ref got);
        long sentWindow = 0;
        var sw2 = Stopwatch.StartNew();
        while (sentWindow < windowTotal)
        {
            if (sentWindow - (Interlocked.Read(ref got) - gotBase) < window)
            {
                client.SendTo(payload, server.LocalEndPoint!);
                sentWindow++;
            }
            else
            {
                Thread.Yield();
            }
        }

        var waitSw = Stopwatch.StartNew();
        while (Interlocked.Read(ref got) - gotBase < windowTotal && waitSw.ElapsedMilliseconds < 3000)
            Thread.Sleep(1);

        double winSec = sw2.Elapsed.TotalSeconds;
        long winGot = Interlocked.Read(ref got) - gotBase;
        Console.WriteLine($"      [윈도 {window}] {winGot:N0}/{windowTotal:N0} 에코 수신 (손실 {100.0 * (windowTotal - winGot) / windowTotal:F2}%), " +
                          $"지속 왕복 처리량 {winGot / winSec:N0} pkt/s");

        server.Close(3000);
    }

    private static void BenchTlsHandshakeAndMemory(bool zeroByte)
    {
        using X509Certificate2 cert = MakeCert();
        var serverOpts = new TlsServiceOptions { ServerCertificate = cert, ZeroByteRecv = zeroByte, MaxSessionCount = 5000 };
        var clientOpts = new TlsServiceOptions
        {
            TargetHost = "localhost", ZeroByteRecv = zeroByte, MaxSessionCount = 5000,
            RemoteCertificateValidation = (_, _, _, _) => true,
        };

        var server = new TlsServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new TlsEcho(), serverOpts);
        server.Start();
        var client = new TlsClientService(server.LocalEndPoint!, () => new TlsEcho(), clientOpts);

        const int connections = 500, concurrency = 50;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long memBefore = GC.GetTotalMemory(true);
        int gen2Before = GC.CollectionCount(2);

        var sw = Stopwatch.StartNew();
        for (int done = 0; done < connections; done += concurrency)
            Task.WaitAll(Enumerable.Range(0, concurrency).Select(_ => client.ConnectAsync()).ToArray());
        double hsSec = sw.Elapsed.TotalSeconds;

        Thread.Sleep(1000);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long memAfter = GC.GetTotalMemory(true);

        Console.WriteLine($"[TLS] 핸드셰이크 {connections}개(동시 {concurrency}): {hsSec:F2}s ({connections / hsSec:N0} conn/s), 서버 세션 {server.SessionCount}");
        Console.WriteLine($"      유휴 {connections}연결(클라이언트+서버 양쪽) 관리 힙 증가 {(memAfter - memBefore) / 1024.0 / 1024:F1}MB " +
                          $"= 연결 쌍당 {(memAfter - memBefore) / connections / 1024.0:F1}KB, Gen2 GC +{GC.CollectionCount(2) - gen2Before}");

        client.Close(10000);
        server.Close(10000);
    }

    private static void BenchEcho(bool zeroByte)
    {
        using X509Certificate2 cert = MakeCert();
        const int clients = 20, packets = 5000, size = 256;

        // TCP(SAEA) 에코 서버
        var tcpServer = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new PlainEcho(),
            new NetServiceOptions { ZeroByteRecv = zeroByte });
        tcpServer.Start();

        // TLS 에코 서버
        var tlsServer = new TlsServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new TlsEcho(),
            new TlsServiceOptions { ServerCertificate = cert, ZeroByteRecv = zeroByte });
        tlsServer.Start();

        // 워밍업 후 측정(각 2회, 두 번째 값을 사용)
        double tcp = 0, tls = 0;
        for (int round = 0; round < 2; round++)
        {
            tcp = RunEcho(clients, packets, size, _ =>
            {
                var tc = new TcpClient { NoDelay = true };
                tc.Connect(tcpServer.LocalEndPoint!);
                return tc.GetStream();
            });

            tls = RunEcho(clients, packets, size, _ =>
            {
                var tc = new TcpClient { NoDelay = true };
                tc.Connect(tlsServer.LocalEndPoint!);
                var ssl = new SslStream(tc.GetStream(), false, (_, _, _, _) => true);
                ssl.AuthenticateAsClient("localhost");
                return ssl;
            });
        }

        Console.WriteLine($"[에코] {clients}연결 x {packets}패킷 x {size}B, 파이프라인 왕복");
        Console.WriteLine($"      TCP(SAEA 세션)   {tcp:N0} pkt/s");
        Console.WriteLine($"      TLS(TlsSession)  {tls:N0} pkt/s  (TCP 대비 {100.0 * tls / tcp:F0}%)");

        tcpServer.Close(5000);
        tlsServer.Close(5000);
    }

    private static double RunEcho(int clients, int packets, int size, Func<int, Stream> open)
    {
        Stream[] streams = Enumerable.Range(0, clients).Select(open).ToArray();
        long need = (long)size * packets;

        var sw = Stopwatch.StartNew();
        var tasks = new List<Task<bool>>();

        foreach (Stream s in streams)
        {
            var packet = new byte[size];
            PacketHeader.Write(packet, (ushort)size, 1);

            Task writer = Task.Run(async () =>
            {
                for (int i = 0; i < packets; i++)
                    await s.WriteAsync(packet).ConfigureAwait(false);
            });

            tasks.Add(Task.Run(async () =>
            {
                var buf = new byte[size * 64];
                long got = 0;
                while (got < need)
                {
                    int n = await s.ReadAsync(buf).ConfigureAwait(false);
                    if (n <= 0)
                        break;
                    got += n;
                }
                await writer.ConfigureAwait(false);
                return got == need;
            }));
        }

        Task.WaitAll(tasks.ToArray());
        double sec = sw.Elapsed.TotalSeconds;

        bool ok = tasks.All(t => t.Result);
        foreach (Stream s in streams) s.Dispose();

        if (!ok)
            Console.WriteLine("      (경고: 일부 연결이 에코를 모두 받지 못했습니다)");

        return (double)clients * packets / sec;
    }
}
