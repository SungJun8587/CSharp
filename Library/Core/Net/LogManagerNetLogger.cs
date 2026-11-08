// LogManagerNetLogger.cs : implementation of the LogManagerNetLogger class.
using System;
using Core.Util;

namespace Core.Net;

/// <summary>
/// INetLogger 를 Core.Util.LogManager(로그 파일/콘솔 출력)에 연결하는 어댑터.
/// 사용: new NetServiceOptions { Logger = new LogManagerNetLogger() }
/// </summary>
public sealed class LogManagerNetLogger : INetLogger
{
    private readonly LogManager _manager;
    private readonly NetLogLevel _minLevel;
    private readonly string _tag;

    /// <param name="minLevel">이 레벨 이상만 기록한다. 세션 접속/종료까지 보려면 Trace.</param>
    /// <param name="tag">각 줄 앞에 붙는 구분 태그. 예: "[Net] 리슨 시작: ..."</param>
    /// <param name="manager">기록할 LogManager. null 이면 LogManager.Instance.</param>
    public LogManagerNetLogger(NetLogLevel minLevel = NetLogLevel.Info, string tag = "Net", LogManager? manager = null)
    {
        _minLevel = minLevel;
        _tag = tag ?? string.Empty;
        _manager = manager ?? LogManager.Instance;
    }

    public bool IsEnabled(NetLogLevel level) => level >= _minLevel;

    public void Log(NetLogLevel level, string message, Exception? exception = null)
    {
        LogType type = level switch
        {
            NetLogLevel.Trace => LogType.Trace,
            NetLogLevel.Debug => LogType.Debug,
            NetLogLevel.Info => LogType.Info,
            NetLogLevel.Warning => LogType.Warning,
            _ => LogType.Error,
        };

        string text = exception == null ? message : message + " | " + exception;

        // 메시지에 '{' 가 있어도 서식으로 해석되지 않도록 항상 인자로 넘긴다.
        _manager.Write(type, true, "[{0}] {1}", _tag, text);
    }
}
