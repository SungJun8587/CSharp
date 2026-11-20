// Log.cs : implementation of the Log and LogManager classes.
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Core.Util;

//***************************************************************************
// @brief 로그의 레벨 및 종류를 정의하는 열거형입니다.
//***************************************************************************
public enum LogType : short
{
    /// <summary>디버그용 로그.</summary>
    Debug,

    /// <summary>흐름 추적용 로그.</summary>
    Trace,

    /// <summary>일반 정보용 로그.</summary>
    Info,

    /// <summary>경고 메시지용 로그.</summary>
    Warning,

    /// <summary>에러 및 실패 로그.</summary>
    Error,

    /// <summary>로그 타입의 총 개수 (인덱스용).</summary>
    MaxNum,
}

//***************************************************************************
// @brief 로그 파일 생성/분할 형식을 정의하는 열거형입니다.
// @detail 파일명 = 접두사 + 시각 문자열 + ".log".
//         Default 는 Daily 와 동일하게 처리됩니다.
//***************************************************************************
public enum LogFileFormat : byte
{
    /// <summary>기본값(일 단위와 동일).</summary>
    Default = 0,

    /// <summary>초 단위 분할 (yyyyMMddHHmmss).</summary>
    Sec = 1,

    /// <summary>분 단위 분할 (yyyyMMddHHmm).</summary>
    Min = 2,

    /// <summary>시간 단위 분할 (yyyyMMddHH).</summary>
    Hours = 3,

    /// <summary>일 단위 분할 (yyyyMMdd).</summary>
    Daily = 4,

    /// <summary>월 단위 분할 (yyyyMM).</summary>
    Monthly = 5,
}

//***************************************************************************
// @brief 로그 출력 대상 플래그입니다.
// @detail C++ 의 _FILE_LOG / _CONSOLE_LOG / _OUTPUT_LOG 컴파일 스위치에 대응하는 런타임 설정입니다.
//***************************************************************************
[Flags]
public enum LogOutput
{
    None = 0,

    /// <summary>로그 파일에 기록.</summary>
    File = 1,

    /// <summary>콘솔에 색상과 함께 출력.</summary>
    Console = 2,

    /// <summary>디버거 출력 창(Trace)에 기록.</summary>
    DebugOutput = 4,

    All = File | Console | DebugOutput,
}

//***************************************************************************
// @brief 개별 로그 파일 생성, 파일 쓰기 및 콘솔 출력을 관리하는 클래스입니다.
// @detail 디렉토리와 파일 접두사를 설정받아 형식별 로그 파일을 생성하고,
//         스레드 안전(Thread-safe)하게 파일과 콘솔에 로그를 기록합니다.
//         콘솔은 프로세스 전역 자원이므로, 파일 쓰기용 락(_sync)과는
//         별도로 모든 Log 인스턴스가 공유하는 정적 락(s_consoleLock)으로
//         색상 설정~출력~복원 구간을 원자적으로 보호합니다.
//         파일 핸들은 현재 파일명이 유지되는 동안 열어 두고(매 줄 flush),
//         시각이 바뀌어 파일명이 달라지면 새 파일로 전환합니다.
//***************************************************************************
public sealed class Log : IDisposable
{
    /// <summary>한 번에 기록할 수 있는 최대 문자 수(널 종료 문자 포함). 초과분은 잘린다.</summary>
    public const int MaxBufferSize = 8300;

    /// <summary>로그 파일 기본 확장자.</summary>
    public const string DefaultFileExtension = ".log";

    // 콘솔 출력 동기화용 정적 락 (전역, 모든 Log 인스턴스 공유)
    private static readonly object s_consoleLock = new object();

    // 로그 타입별 콘솔 색상. null 이면 색상을 바꾸지 않는다(디버그 = 기본색).
    private static readonly ConsoleColor?[] s_typeColors =
    {
        null,                   // Debug   : 기본색(흰색)
        ConsoleColor.Blue,      // Trace   : 파란색
        ConsoleColor.Green,     // Info    : 초록색
        ConsoleColor.Yellow,    // Warning : 노란색
        ConsoleColor.Red,       // Error   : 빨간색
    };

    // 한글 환경의 메모장 등에서도 바로 열리도록 BOM 이 있는 UTF-8 (새 파일의 첫 기록에만 BOM 이 쓰인다).
    private static readonly Encoding s_encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private readonly object _sync = new object(); // 파일 쓰기 동기화용 락 (인스턴스별 = 로그 타입별)

    private string _directory = string.Empty;           // 로그 디렉토리 경로
    private string _fileNamePrefix = string.Empty;      // 로그 파일명 접두사
    private LogFileFormat _logFmt = LogFileFormat.Sec;  // 로그 파일 분할 포맷 값
    private LogOutput _output = LogOutput.File | LogOutput.Console;

    private StreamWriter? _writer;      // 현재 열려 있는 로그 파일
    private string? _currentPath;       // 현재 열려 있는 로그 파일 경로

    // 파일명 캐시: 파일명은 분할 단위(초/분/시/일/월)가 바뀔 때만 달라지므로, 그때까지는 매 줄 문자열을 만들지 않는다.
    private string? _cachedPath;
    private DateTime _cachedUntil = DateTime.MinValue;

    // 파일 열기/쓰기에 실패하면 이 시각(단조 ms)까지는 파일 쓰기를 건너뛴다. 디스크가 가득 찼거나 권한이 없을 때 줄마다 열기를 재시도하지 않게 한다.
    private long _retryAfterMs;
    private bool _retryPending;
    private const int FileRetryDelayMs = 5000;

    /// <summary>
    /// true(기본)이면 호출 스레드는 줄을 만들어 큐에 넣기만 하고, 파일/콘솔 쓰기는 공용 쓰기 스레드가 한다.
    /// 느린 디스크나 콘솔이 게임 틱을 붙잡지 않는다. 큐가 가득 차면 새 줄을 버리고 <see cref="LogPump.Dropped"/>로 센다. false 이면 호출 스레드에서 바로 쓴다.
    /// 같은 Log 의 줄 순서는 어느 쪽이든 유지된다.
    /// </summary>
    public bool Async { get; set; } = true;

    //***************************************************************************
    // @brief 로그 시스템을 초기화합니다.
    // @param directory 로그 파일을 저장할 디렉토리 경로 (없으면 첫 기록 시 생성)
    // @param fileNamePrefix 로그 파일 이름의 접두사
    // @param format 로그 파일 생성/분할 형식 (분/시/일/월 등)
    // @param output 출력 대상 (파일/콘솔/디버그 출력)
    //***************************************************************************
    public void Init(string directory, string fileNamePrefix, LogFileFormat format,
                      LogOutput output = LogOutput.File | LogOutput.Console)
    {
        lock (_sync)
        {
            CloseWriter();
            _cachedPath = null;
            _retryPending = false;

            _directory = directory ?? string.Empty;
            _fileNamePrefix = fileNamePrefix ?? string.Empty;
            _logFmt = format;
            _output = output;
        }
    }

    //***************************************************************************
    // @brief 지정된 로그 타입으로 로그를 기록합니다.
    // @param type 로그의 레벨 타입 (DEBUG, INFO 등)
    // @param message 기록할 로그 문자열
    // @param withTimestamp true 이면 "[yyyy-MM-dd HH:mm:ss] # " 접두를 붙이고, false 이면 문자열 그대로 기록
    //***************************************************************************
    public void LogWrite(LogType type, string message, bool withTimestamp = true)
    {
        Write(type, message, withTimestamp);
    }

    //***************************************************************************
    // @brief 큐에 쌓인 줄을 모두 쓴 뒤 열려 있는 로그 파일을 닫습니다. 이후 기록 시 필요하면 다시 열립니다.
    //***************************************************************************
    public void Dispose()
    {
        LogPump.Flush();
        lock (_sync)
        {
            CloseWriter();
        }
    }

    //***************************************************************************
    // @brief 형식과 시각으로 로그 파일명(접두사 포함, 확장자 포함)을 만듭니다.
    // @param prefix 파일명 접두사
    // @param format 분할 형식
    // @param time 기준 시각
    // @return 예: format=Daily, prefix="1_INFO" → "1_INFO20261108.log"
    //***************************************************************************
    public static string BuildFileName(string prefix, LogFileFormat format, DateTime time)
    {
        string pattern = format switch
        {
            LogFileFormat.Sec => "yyyyMMddHHmmss",
            LogFileFormat.Min => "yyyyMMddHHmm",
            LogFileFormat.Hours => "yyyyMMddHH",
            LogFileFormat.Monthly => "yyyyMM",
            _ => "yyyyMMdd", // Daily, Default
        };

        return prefix + time.ToString(pattern, CultureInfo.InvariantCulture) + DefaultFileExtension;
    }

    //***************************************************************************
    // @brief 실제 파일 쓰기, 콘솔 출력, 디버그 출력을 수행하는 내부 함수입니다.
    //***************************************************************************
    private void Write(LogType type, string message, bool withTimestamp)
    {
        DateTime now = DateTime.Now;
        message = Truncate(message ?? string.Empty);

        string line = withTimestamp
            ? "[" + now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] # " + message
            : message;

        if (Async)
        {
            // 파일/디버그 출력과 콘솔을 서로 다른 쓰기 스레드로 보낸다: 콘솔이 막혀도(파이프가 가득 찬 경우 등) 파일 로그는 밀리지 않는다.
            LogOutput output = _output;
            if ((output & (LogOutput.File | LogOutput.DebugOutput)) != 0)
                LogPump.File.Enqueue(this, type, now, line, output & (LogOutput.File | LogOutput.DebugOutput));
            if ((output & LogOutput.Console) != 0)
                LogPump.Console.Enqueue(this, type, now, line, LogOutput.Console);
            return;
        }

        WriteNow(type, now, line, _output);
    }

    // 쓰기 스레드(또는 동기 모드의 호출 스레드)에서 parts 에 해당하는 출력을 한다.
    internal void WriteNow(LogType type, DateTime now, string line, LogOutput parts)
    {
        lock (_sync)
        {
            if ((parts & LogOutput.File) != 0)
                WriteFile(now, line);

            if ((parts & LogOutput.Console) != 0)
                WriteConsole(type, Truncate(line));

            if ((parts & LogOutput.DebugOutput) != 0)
                System.Diagnostics.Trace.WriteLine(Truncate(line));
        }
    }

    private void WriteFile(DateTime now, string line)
    {
        if (_directory.Length == 0)
            return; // 초기화되지 않음

        if (_retryPending && TimeUtil.MonotonicNowMillis() < _retryAfterMs)
            return; // 직전 실패 뒤 쉬는 중(그동안의 줄은 파일에 남지 않는다. 콘솔/디버그 출력은 계속된다)

        try
        {
            string path = CurrentPath(now);

            if (_writer == null || !string.Equals(path, _currentPath, StringComparison.Ordinal))
            {
                CloseWriter();
                Directory.CreateDirectory(_directory);

                // 다른 프로세스가 읽을 수는 있지만 사용 중인 파일을 삭제/이름 변경할 수는 없다.
                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream, s_encoding) { AutoFlush = true };
                _currentPath = path;
            }

            _writer.WriteLine(line);
            _retryPending = false;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
        {
            // 로그 기록 실패가 호출자를 죽이지 않도록 삼키고, FileRetryDelayMs 뒤에 파일을 다시 연다.
            CloseWriter();
            _retryPending = true;
            _retryAfterMs = TimeUtil.MonotonicNowMillis() + FileRetryDelayMs;
        }
    }

    private string CurrentPath(DateTime now)
    {
        if (_cachedPath != null && now < _cachedUntil && now >= _cachedUntil - BoundarySpan(_logFmt, now))
            return _cachedPath;

        _cachedPath = Path.Combine(_directory, BuildFileName(_fileNamePrefix, _logFmt, now));
        _cachedUntil = NextBoundary(_logFmt, now);
        return _cachedPath;
    }

    // now 가 속한 분할 단위의 다음 경계 시각.
    private static DateTime NextBoundary(LogFileFormat format, DateTime now)
    {
        switch (format)
        {
            case LogFileFormat.Sec: return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind).AddSeconds(1);
            case LogFileFormat.Min: return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind).AddMinutes(1);
            case LogFileFormat.Hours: return new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind).AddHours(1);
            case LogFileFormat.Monthly: return new DateTime(now.Year, now.Month, 1, 0, 0, 0, now.Kind).AddMonths(1);
            default: return now.Date.AddDays(1);
        }
    }

    // 시각이 거꾸로 가는 경우(시계 조정)를 걸러 내려는 최대 경계 길이. 캐시 구간 [until - span, until) 밖이면 다시 계산한다.
    private static TimeSpan BoundarySpan(LogFileFormat format, DateTime now) => format switch
    {
        LogFileFormat.Sec => TimeSpan.FromSeconds(1),
        LogFileFormat.Min => TimeSpan.FromMinutes(1),
        LogFileFormat.Hours => TimeSpan.FromHours(1),
        LogFileFormat.Monthly => TimeSpan.FromDays(31),
        _ => TimeSpan.FromDays(1) + TimeSpan.FromHours(1),      // 일 경계(서머타임 25시간 포함)
    };

    private static void WriteConsole(LogType type, string line)
    {
        ConsoleColor? color = null;
        int index = (int)type;
        if (index >= 0 && index < s_typeColors.Length)
            color = s_typeColors[index];

        lock (s_consoleLock)
        {
            try
            {
                if (color.HasValue)
                    Console.ForegroundColor = color.Value;

                Console.WriteLine(line);
            }
            catch (IOException)
            {
                // 콘솔이 없는 프로세스(서비스 등)
            }
            finally
            {
                if (color.HasValue)
                {
                    try { Console.ResetColor(); }
                    catch (IOException) { }
                }
            }
        }
    }

    private void CloseWriter()
    {
        try { _writer?.Dispose(); }
        catch (IOException) { }

        _writer = null;
        _currentPath = null;
    }

    private static string Truncate(string text)
    {
        // C++ 버퍼가 널 문자를 포함한 MaxBufferSize 문자이므로 실제 문자는 MaxBufferSize - 1 개까지.
        return text.Length < MaxBufferSize ? text : text.Substring(0, MaxBufferSize - 1);
    }
}

//***************************************************************************
// @brief 전체 로그 시스템을 총괄하는 싱글톤 매니저 클래스입니다.
// @detail 각 로그 타입별 Log 인스턴스를 관리하며, 포맷 문자열과 인자를 통해
//         편리하게 로그를 남길 수 있는 인터페이스를 제공합니다.
//         포맷은 C# 복합 서식("{0}", "{1:X}")이며, 인자가 없으면 서식 처리 없이 그대로 기록합니다.
//***************************************************************************
public sealed class LogManager
{
    private static readonly Lazy<LogManager> s_instance =
        new Lazy<LogManager>(() => new LogManager(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Log[] _logs = new Log[(int)LogType.MaxNum]; // 로그 타입별 Log 인스턴스 배열
    private int _minLevel;                                       // 이 레벨 미만은 서식 처리 전에 버린다(LogType 값)

    /// <summary>이 레벨 미만의 로그는 서식을 만들기 전에 버린다(예: 운영에서 Info 이상만). 기본 Debug(전부 기록).</summary>
    public LogType MinLevel
    {
        get => (LogType)Volatile.Read(ref _minLevel);
        set => Volatile.Write(ref _minLevel, (int)value);
    }

    public bool IsEnabled(LogType type) => (int)type >= Volatile.Read(ref _minLevel);

    /// <summary>모든 로그 타입의 비동기 쓰기를 한꺼번에 켜고 끈다(<see cref="Log.Async"/>).</summary>
    public bool Async
    {
        get => _logs[0].Async;
        set { foreach (Log log in _logs) log.Async = value; }
    }

    /// <summary>비동기 큐에 쌓인 줄을 모두 쓸 때까지 기다린다(최대 5초).</summary>
    public void Flush() => LogPump.Flush();

    private LogManager()
    {
        for (int i = 0; i < _logs.Length; i++)
            _logs[i] = new Log();

        // 프로세스 종료 시 열린 로그 파일을 닫는다.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
    }

    //***************************************************************************
    // @brief LogManager 싱글톤 인스턴스를 반환합니다.
    //***************************************************************************
    public static LogManager Instance => s_instance.Value;

    //***************************************************************************
    // @brief 로그 매니저를 생성하고 하위 로그 객체들을 초기화합니다.
    // @param directory 로그 파일을 저장할 디렉토리 경로
    // @param output 출력 대상 (기본: 파일 + 콘솔)
    // @detail 파일명은 "1_DEBUG/1_TRACE/1_INFO/1_WARNING/1_ERROR" + yyyyMMdd + ".log" 입니다.
    //***************************************************************************
    public void Create(string directory, LogOutput output = LogOutput.File | LogOutput.Console)
    {
        _logs[(int)LogType.Debug].Init(directory, "1_DEBUG", LogFileFormat.Daily, output);
        _logs[(int)LogType.Trace].Init(directory, "1_TRACE", LogFileFormat.Daily, output);
        _logs[(int)LogType.Info].Init(directory, "1_INFO", LogFileFormat.Daily, output);
        _logs[(int)LogType.Warning].Init(directory, "1_WARNING", LogFileFormat.Daily, output);
        _logs[(int)LogType.Error].Init(directory, "1_ERROR", LogFileFormat.Daily, output);
    }

    //***************************************************************************
    // @brief 포맷에 맞춘 로그를 생성하고 기록합니다.
    // @param type 로그의 레벨 타입
    // @param withTimestamp true 이면 "[시각] # " 접두를 붙이고, false 이면 그대로 기록
    // @param format 서식 문자열 ("{0}" 형식)
    // @param args 서식 인자
    //***************************************************************************
    public void Write(LogType type, bool withTimestamp, string format, params object?[] args)
    {
        int index = (int)type;
        if (index < 0 || index >= _logs.Length || index < Volatile.Read(ref _minLevel))
            return;

        _logs[index].LogWrite(type, Format(format, args), withTimestamp);
    }

    /// <summary>열려 있는 모든 로그 파일을 닫습니다. 이후 기록하면 필요할 때 다시 열립니다.</summary>
    public void Shutdown()
    {
        LogPump.Flush();
        foreach (Log log in _logs)
            log.Dispose();
    }

    // C++ 의 LOG_DEBUG / LOG_TRACE / LOG_INFO / LOG_WARNING / LOG_ERROR 매크로에 대응 (시각 접두 포함).
    public static void Debug(string format, params object?[] args) => Instance.Write(LogType.Debug, true, format, args);

    public static void Trace(string format, params object?[] args) => Instance.Write(LogType.Trace, true, format, args);

    public static void Info(string format, params object?[] args) => Instance.Write(LogType.Info, true, format, args);

    public static void Warning(string format, params object?[] args) => Instance.Write(LogType.Warning, true, format, args);

    public static void Error(string format, params object?[] args) => Instance.Write(LogType.Error, true, format, args);

    private static string Format(string format, object?[] args)
    {
        if (format == null)
            return string.Empty;

        if (args == null || args.Length == 0)
            return format;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            // 서식과 인자가 맞지 않아도 로그가 유실되지 않도록 원본 서식을 그대로 기록한다.
            return format;
        }
    }
}

//***************************************************************************
// @brief 비동기 쓰기 큐와 쓰기 스레드입니다(File: 파일/디버그 출력용, Console: 콘솔용 — 둘은 서로 막지 않습니다).
// @detail 한 큐의 스레드는 하나라서 같은 Log 의 줄 순서가 유지됩니다.
//         큐는 MaxQueued 로 묶여 있어, 쓰기가 오래 막히면 새 줄을 버리고(Dropped) 메모리가 불어나지 않게 합니다.
//         스레드는 백그라운드라 프로세스 종료를 막지 않으며, 종료 직전 LogManager.Shutdown 이 Flush 로 비웁니다.
//***************************************************************************
internal sealed class LogPump
{
    private readonly struct Entry
    {
        public readonly Log Log;
        public readonly LogType Type;
        public readonly DateTime Time;
        public readonly string Line;
        public readonly LogOutput Parts;
        public Entry(Log log, LogType type, DateTime time, string line, LogOutput parts) { Log = log; Type = type; Time = time; Line = line; Parts = parts; }
    }

    public const int MaxQueued = 100_000;

    public static readonly LogPump File = new LogPump("Core.LogWriter");
    public static readonly LogPump Console = new LogPump("Core.LogConsole");

    private readonly string _name;
    private readonly ConcurrentQueue<Entry> _queue = new ConcurrentQueue<Entry>();
    private readonly AutoResetEvent _signal = new AutoResetEvent(false);
    private readonly object _startLock = new object();
    private Thread? _thread;
    private int _count;         // 큐에 있거나 쓰는 중인 줄 수(쓰기가 끝나야 줄어든다)
    private long _dropped;

    private LogPump(string name) { _name = name; }

    /// <summary>큐가 가득 차서 버려진 줄 수(누적, 두 큐 합계).</summary>
    public static long Dropped => System.Threading.Interlocked.Read(ref File._dropped) + System.Threading.Interlocked.Read(ref Console._dropped);

    public void Enqueue(Log log, LogType type, DateTime time, string line, LogOutput parts)
    {
        if (Volatile.Read(ref _count) >= MaxQueued)
        {
            Interlocked.Increment(ref _dropped);
            return;             // 버렸다(호출 스레드에서 동기로 쓰면 막히므로 동기 쓰기로 돌리지 않는다)
        }

        Interlocked.Increment(ref _count);
        _queue.Enqueue(new Entry(log, type, time, line, parts));
        EnsureStarted();
        _signal.Set();
    }

    private void EnsureStarted()
    {
        if (Volatile.Read(ref _thread) != null)
            return;

        lock (_startLock)
        {
            if (_thread != null)
                return;

            var t = new Thread(Run) { IsBackground = true, Name = _name };
            Volatile.Write(ref _thread, t);
            t.Start();
        }
    }

    private void Run()
    {
        while (true)
        {
            while (_queue.TryDequeue(out Entry e))
            {
                try { e.Log.WriteNow(e.Type, e.Time, e.Line, e.Parts); }
                catch { /* 로그 출력이 쓰기 스레드를 죽이지 않게 한다 */ }
                Interlocked.Decrement(ref _count);
            }
            _signal.WaitOne();
        }
    }

    /// <summary>두 큐가 빌 때까지(쓰기가 끝날 때까지) 기다린다. 전체 제한 시간은 timeoutMs.</summary>
    public static void Flush(int timeoutMs = 5000)
    {
        long deadline = TimeUtil.MonotonicNowMillis() + timeoutMs;
        File.WaitEmpty(deadline);
        Console.WaitEmpty(deadline);
    }

    private void WaitEmpty(long deadline)
    {
        if (Volatile.Read(ref _thread) == null)
            return;

        while (Volatile.Read(ref _count) > 0 && TimeUtil.MonotonicNowMillis() < deadline)
            Thread.Sleep(1);
    }
}
