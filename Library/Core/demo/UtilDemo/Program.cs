// Program.cs : Core.Util (LogManager) smoke test.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Core.Util;

namespace Core.Demo;

internal static class Program
{
    private static int s_failures;

    private static void Check(string name, bool ok)
    {
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
        if (!ok)
            s_failures++;
    }

    private static string[] ReadLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, new UTF8Encoding(true));
        return sr.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class CaptureListener : TraceListener
    {
        public readonly List<string> Lines = new();
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { lock (Lines) Lines.Add(message ?? ""); }
    }

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "logcore_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string today = DateTime.Now.ToString("yyyyMMdd");

        // 1) 파일명 규칙
        var t = new DateTime(2026, 11, 8, 8, 16, 5);
        Check("파일명 Sec", Log.BuildFileName("P_", LogFileFormat.Sec, t) == "P_20261108081605.log");
        Check("파일명 Min", Log.BuildFileName("P_", LogFileFormat.Min, t) == "P_202611080816.log");
        Check("파일명 Hours", Log.BuildFileName("P_", LogFileFormat.Hours, t) == "P_2026110808.log");
        Check("파일명 Daily", Log.BuildFileName("P_", LogFileFormat.Daily, t) == "P_20261108.log");
        Check("파일명 Monthly", Log.BuildFileName("P_", LogFileFormat.Monthly, t) == "P_202611.log");
        Check("파일명 Default = Daily", Log.BuildFileName("P_", LogFileFormat.Default, t) == "P_20261108.log");

        // 2) LogManager 파일 기록 (타입별 파일, 시각 접두 on/off, 서식)
        string d2 = Path.Combine(root, "mgr", "sub"); // 존재하지 않는 하위 디렉토리도 생성되어야 한다
        LogManager.Instance.Create(d2, LogOutput.File);
        LogManager.Info("hello {0} {1:X}", 42, 255);
        LogManager.Instance.Write(LogType.Error, false, "raw line");
        LogManager.Warning("no args {0}");            // 인자가 없으면 서식 처리 없이 그대로
        LogManager.Debug("bad {0} {1}", "only-one");  // 서식 오류 → 원본 기록
        LogManager.Instance.Shutdown();

        string[] info = ReadLines(Path.Combine(d2, $"1_INFO{today}.log"));
        Check("Info 파일/시각 접두/서식", info.Length == 1 &&
              Regex.IsMatch(info[0], @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] # hello 42 FF$"));
        string[] err = ReadLines(Path.Combine(d2, $"1_ERROR{today}.log"));
        Check("시각 접두 없이 기록", err.Length == 1 && err[0] == "raw line");
        string[] warn = ReadLines(Path.Combine(d2, $"1_WARNING{today}.log"));
        Check("인자 없으면 서식 처리 안 함", warn.Length == 1 && warn[0].EndsWith("# no args {0}"));
        string[] dbg = ReadLines(Path.Combine(d2, $"1_DEBUG{today}.log"));
        Check("서식 오류여도 로그 유실 없음", dbg.Length == 1 && dbg[0].EndsWith("# bad {0} {1}"));

        // 3) BOM: 새 파일에만 한 번, 이어쓰기에는 추가되지 않고 한글이 보존된다
        LogManager.Info("한글 로그 테스트");
        LogManager.Instance.Shutdown();
        LogManager.Info("다시 열어 이어쓰기");
        LogManager.Instance.Shutdown();
        byte[] raw = File.ReadAllBytes(Path.Combine(d2, $"1_INFO{today}.log"));
        int bomCount = 0;
        for (int i = 0; i + 2 < raw.Length; i++)
            if (raw[i] == 0xEF && raw[i + 1] == 0xBB && raw[i + 2] == 0xBF) bomCount++;
        string[] info2 = ReadLines(Path.Combine(d2, $"1_INFO{today}.log"));
        Check("UTF-8 BOM 한 번만, 한글 보존", bomCount == 1 && info2.Length == 3 && info2[1].EndsWith("한글 로그 테스트") && info2[2].EndsWith("다시 열어 이어쓰기"));

        // 4) 최대 길이 잘림 (메시지 8299자)
        string d4 = Path.Combine(root, "trunc");
        var logT = new Log();
        logT.Init(d4, "T_", LogFileFormat.Daily, LogOutput.File);
        logT.LogWrite(LogType.Info, new string('x', 20000), false);
        logT.Dispose();
        Check("메시지 8299자로 잘림", ReadLines(Path.Combine(d4, $"T_{today}.log"))[0].Length == Log.MaxBufferSize - 1);

        // 5) 동시성: 8스레드 × 2000줄이 한 줄도 깨지지 않고 모두 기록
        string d5 = Path.Combine(root, "conc");
        var logC = new Log();
        logC.Init(d5, "C_", LogFileFormat.Daily, LogOutput.File);
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, th =>
        {
            for (int i = 0; i < 2000; i++)
                logC.LogWrite(LogType.Info, $"thread={th} seq={i} payload=abcdefghijklmnopqrstuvwxyz");
        });
        string[] conc = ReadLines(Path.Combine(d5, $"C_{today}.log"));
        var rx = new Regex(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] # thread=\d seq=\d+ payload=abcdefghijklmnopqrstuvwxyz$");
        Check("동시 기록 16000줄 무결성", conc.Length == 16000 && conc.All(l => rx.IsMatch(l)));
        // 열려 있는 동안에도(매 줄 flush) 다른 핸들로 읽을 수 있다
        Check("기록 중에도 읽기 가능(flush)", ReadLines(Path.Combine(d5, $"C_{today}.log")).Length == 16000);
        logC.Dispose();

        // 6) 시각이 바뀌면 새 파일로 전환 (Sec 형식)
        string d6 = Path.Combine(root, "roll");
        var logR = new Log();
        logR.Init(d6, "R_", LogFileFormat.Sec, LogOutput.File);
        logR.LogWrite(LogType.Info, "first");
        Thread.Sleep(1100);
        logR.LogWrite(LogType.Info, "second");
        logR.Dispose();
        Check("초 단위 형식에서 파일 전환", Directory.GetFiles(d6, "R_*.log").Length == 2);

        // 7) 콘솔 출력 + 디버그 출력
        var cap = new CaptureListener();
        Trace.Listeners.Add(cap);
        var oldOut = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        var logO = new Log();
        logO.Init(Path.Combine(root, "out"), "O_", LogFileFormat.Daily, LogOutput.Console | LogOutput.DebugOutput);
        logO.LogWrite(LogType.Warning, "to console");
        logO.LogWrite(LogType.Error, "plain", false);
        Console.SetOut(oldOut);
        Trace.Listeners.Remove(cap);
        string consoleText = sw.ToString();
        Check("콘솔 출력", consoleText.Contains("# to console") && consoleText.Contains("plain"));
        Check("디버그 출력(Trace)", cap.Lines.Count == 2 && cap.Lines[0].EndsWith("# to console") && cap.Lines[1] == "plain");
        Check("File 미지정이면 파일 미생성", !Directory.Exists(Path.Combine(root, "out")));

        // 8) 파일 기록 실패(디렉토리 위치에 파일이 있음)여도 예외 없이 콘솔 출력은 계속된다
        string blocker = Path.Combine(root, "blocker");
        File.WriteAllText(blocker, "i am a file");
        var sw2 = new StringWriter();
        Console.SetOut(sw2);
        bool threw = false;
        try
        {
            var logF = new Log();
            logF.Init(blocker, "F_", LogFileFormat.Daily, LogOutput.File | LogOutput.Console);
            logF.LogWrite(LogType.Info, "still printed");
            logF.LogWrite(LogType.Info, "second try");
            logF.Dispose();
        }
        catch (Exception) { threw = true; }
        Console.SetOut(oldOut);
        Check("파일 실패 시 예외 없음 + 콘솔 계속", !threw && sw2.ToString().Contains("still printed") && sw2.ToString().Contains("second try"));

        // 9) 초기화 전에는 파일을 만들지 않는다
        string emptyDir = Path.Combine(root, "cwd");
        Directory.CreateDirectory(emptyDir);
        string oldCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(emptyDir);
        string cwdBefore = emptyDir;
        var logU = new Log();
        var sw3 = new StringWriter();
        Console.SetOut(sw3);
        logU.LogWrite(LogType.Info, "uninitialized");
        Console.SetOut(oldOut);
        Check("미초기화 시 콘솔만", sw3.ToString().Contains("uninitialized") && Directory.GetFiles(cwdBefore, "*").Length == 0);
        Directory.SetCurrentDirectory(oldCwd);

        // 10) TextUtil / AsciiChar
        Check("Trim: NBSP, 전각 공백, BOM 제거", TextUtil.Trim("\u00A0\u3000\uFEFF a b \t\u00A0") == "a b");
        Check("Quote: 작은따옴표 포함이면 큰따옴표", TextUtil.Quote("it's") == "\"it's\"" && TextUtil.Quote("abc") == "'abc'");
        Check("Quote: 보이지 않는 공백은 코드 값", TextUtil.Quote("a\u00A0b") == "'a\\u00A0b'");
        Check("JsonString: 제어 문자 이스케이프, 비ASCII 유지", TextUtil.JsonString("a\"\\\n\u0001한") == "\"a\\\"\\\\\\n\\u0001한\"");
        Check("ParseDouble: 10진/지수만, 부호는 하나", TextUtil.ParseDouble("+1.5e3", out double pd) && pd == 1500 && !TextUtil.ParseDouble("0x10", out _) && !TextUtil.ParseDouble("+-5", out _) && !TextUtil.ParseDouble(" 5", out _));
        Check("AsciiChar.Lower: ASCII만 변환", AsciiChar.Lower("ABC가나Z") == "abc가나z");

        // 11) FileUtil
        string fdir = Path.Combine(root, "fileutil");
        Directory.CreateDirectory(fdir);
        string fpath = Path.Combine(fdir, "a.txt");
        Check("WriteFileIfChanged: 새 파일은 쓴다", FileUtil.WriteFileIfChanged(fpath, FileUtil.Utf8("a\nb\n")));
        Check("WriteFileIfChanged: 같은 내용은 쓰지 않는다", !FileUtil.WriteFileIfChanged(fpath, FileUtil.Utf8("a\nb\n")));
        Check("WriteFileIfChanged: CR 차이는 같은 내용", !FileUtil.WriteFileIfChanged(fpath, FileUtil.Utf8("a\r\nb\r\n")));
        Check("WriteFileIfChanged: 다른 내용은 쓴다", FileUtil.WriteFileIfChanged(fpath, FileUtil.Utf8("a\nc\n")) && File.ReadAllText(fpath) == "a\nc\n");
        Check("임시 파일이 남지 않는다", Directory.GetFiles(fdir).Length == 1);
        byte[] bom = FileUtil.Utf8WithBom("가");
        Check("Utf8WithBom: BOM 3바이트 + 본문", bom.Length == 6 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF);
        string moveFrom = Path.Combine(fdir, "from.tmp");
        File.WriteAllText(moveFrom, "new");
        FileReplace.Move(moveFrom, fpath);
        Check("FileReplace.Move: 기존 파일 덮어쓰기", File.ReadAllText(fpath) == "new" && !File.Exists(moveFrom));

        try { Directory.Delete(root, true); } catch (IOException) { }

        Console.WriteLine(s_failures == 0 ? "ALL PASSED" : $"{s_failures} FAILED");
        return s_failures == 0 ? 0 : 1;
    }
}
