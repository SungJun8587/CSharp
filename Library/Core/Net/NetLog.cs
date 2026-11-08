// NetLog.cs : interface of the INetLogger and NetLogLevel types.
using System;

namespace Core.Net;

/// <summary>
/// Core.Net 내부 로그의 심각도. 값이 클수록 중요하며, 최소 레벨 필터링에 그대로 비교해 쓸 수 있다.
/// </summary>
public enum NetLogLevel
{
    /// <summary>세션 접속/종료 같은 상세 흐름.</summary>
    Trace = 0,

    /// <summary>진단용 상세 정보.</summary>
    Debug = 1,

    /// <summary>서비스 시작/종료 등 일반 정보.</summary>
    Info = 2,

    /// <summary>비정상 종료(타임아웃, 큐 초과, 프로토콜 오류), 접속 거부, Accept 실패 등.</summary>
    Warning = 3,

    /// <summary>콜백 예외, 리슨 실패 등 오류.</summary>
    Error = 4,
}

/// <summary>
/// Core.Net 이 내부 이벤트를 흘려보내는 로거 인터페이스. 특정 로깅 라이브러리에 의존하지 않도록
/// NetServiceOptions.Logger 로 주입한다. 구현은 스레드 안전해야 하며 I/O 스레드에서 호출되므로 오래 블로킹하면 안 된다.
/// 구현이 던지는 예외는 Core.Net 이 삼키므로 서비스 동작에 영향을 주지 않는다.
/// </summary>
public interface INetLogger
{
    /// <summary>해당 레벨을 기록할지 여부. false 이면 메시지 문자열을 만들지 않는 경로에서 호출을 생략한다.</summary>
    bool IsEnabled(NetLogLevel level);

    /// <summary>로그를 기록한다.</summary>
    /// <param name="level">심각도.</param>
    /// <param name="message">메시지.</param>
    /// <param name="exception">관련 예외(없으면 null).</param>
    void Log(NetLogLevel level, string message, Exception? exception = null);
}
