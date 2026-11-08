// Log.cs : implementation of the Log and LogManager classes.
using System;
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
    // @brief 열려 있는 로그 파일을 닫습니다. 이후 기록 시 필요하면 다시 열립니다.
    //***************************************************************************
    public void Dispose()
    {
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

        lock (_sync)
        {
            if ((_output & LogOutput.File) != 0)
                WriteFile(now, line);

            if ((_output & LogOutput.Console) != 0)
                WriteConsole(type, Truncate(line));

            if ((_output & LogOutput.DebugOutput) != 0)
                System.Diagnostics.Trace.WriteLine(Truncate(line));
        }
    }

    private void WriteFile(DateTime now, string line)
    {
        if (_directory.Length == 0)
            return; // 초기화되지 않음

        try
        {
            string path = Path.Combine(_directory, BuildFileName(_fileNamePrefix, _logFmt, now));

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
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
        {
            // 로그 기록 실패가 호출자를 죽이지 않도록 삼킨다. 다음 기록 때 파일을 다시 연다.
            CloseWriter();
        }
    }

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
        if (index < 0 || index >= _logs.Length)
            return;

        _logs[index].LogWrite(type, Format(format, args), withTimestamp);
    }

    /// <summary>열려 있는 모든 로그 파일을 닫습니다. 이후 기록하면 필요할 때 다시 열립니다.</summary>
    public void Shutdown()
    {
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
