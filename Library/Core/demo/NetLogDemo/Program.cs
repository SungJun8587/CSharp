// Program.cs : Core.Net + Core.Util (LogManagerNetLogger) smoke test.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Core.Net;
using Core.Util;

namespace Core.Demo;

internal static class Program
{
    private sealed class ThrowSession : PacketSession
    {
        protected override void OnRecvPacket(ReadOnlySpan<byte> packet) => throw new InvalidOperationException("boom {not a format}");
    }

    private static int s_failures;

    private static void Check(string name, bool ok)
    {
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
        if (!ok)
            s_failures++;
    }

    private static string Read(string dir, string prefix)
    {
        string path = Path.Combine(dir, $"{prefix}{DateTime.Now:yyyyMMdd}.log");
        if (!File.Exists(path))
            return string.Empty;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    private static bool WaitFor(Func<bool> cond, int ms)
    {
        for (int i = 0; i < ms / 10 && !cond(); i++)
            Thread.Sleep(10);
        return cond();
    }

    private static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bridge_test_" + Guid.NewGuid().ToString("N"));
        LogManager.Instance.Create(dir, LogOutput.File);

        var logger = new LogManagerNetLogger(NetLogLevel.Trace, "Net");
        Check("IsEnabled: 최소 레벨 필터", logger.IsEnabled(NetLogLevel.Trace)
              && !new LogManagerNetLogger(NetLogLevel.Warning).IsEnabled(NetLogLevel.Info)
              && new LogManagerNetLogger(NetLogLevel.Warning).IsEnabled(NetLogLevel.Error));

        var server = new ServerService(new IPEndPoint(IPAddress.Loopback, 0), () => new ThrowSession(),
            new NetServiceOptions { Logger = logger });
        server.Start();

        using (var tc = new TcpClient())
        {
            tc.Connect(server.LocalEndPoint!);
            var pkt = new byte[PacketHeader.HeaderSize];
            PacketHeader.Write(pkt, PacketHeader.HeaderSize, 1);
            tc.GetStream().Write(pkt, 0, pkt.Length);
            WaitFor(() => Read(dir, "1_WARNING").Contains("ProtocolError"), 3000);
        }

        server.Close(2000);
        LogManager.Instance.Shutdown();

        string info = Read(dir, "1_INFO");
        string trace = Read(dir, "1_TRACE");
        string warn = Read(dir, "1_WARNING");
        string error = Read(dir, "1_ERROR");

        Check("Info 파일에 리슨 시작 + 태그", info.Contains("# [Net] 리슨 시작: 127.0.0.1:"));
        Check("Info 파일에 서버 종료", info.Contains("# [Net] 서버 종료 시작"));
        Check("Trace 파일에 세션 연결", trace.Contains("# [Net] 세션 1 연결됨"));
        Check("Warning 파일에 비정상 종료 사유", warn.Contains("(ProtocolError)"));
        Check("Error 파일에 콜백 예외(서식 문자 포함 메시지 보존)",
              error.Contains("콜백 예외") && error.Contains("InvalidOperationException") && error.Contains("boom {not a format}"));

        try { Directory.Delete(dir, true); } catch (IOException) { }
        Console.WriteLine(s_failures == 0 ? "ALL PASSED" : $"{s_failures} FAILED");
        return s_failures == 0 ? 0 : 1;
    }
}
