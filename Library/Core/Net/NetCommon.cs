// NetCommon.cs : common definitions of the Core.Net library.
using System;

namespace Core.Net;

/// <summary>
/// 네트워크 라이브러리 공용 상수.
/// </summary>
public static class NetConstants
{
    /// <summary>수신 버퍼 단위 크기(바이트).</summary>
    public const int BufferSizeDefault = 10240;

    /// <summary>수신 버퍼 전체 용량 = 단위 크기 * 이 값.</summary>
    public const int RecvBufferCount = 10;

    /// <summary>송신 청크 크기. LOH(85,000바이트) 진입을 피하기 위해 64KB로 고정.</summary>
    public const int SendChunkSize = 64 * 1024;

    /// <summary>한 번의 Send 호출(scatter-gather)에 묶을 최대 SendBuffer 개수.</summary>
    public const int MaxSendBatch = 64;

    /// <summary>Accept 실패 시 재시도 기본 지연(ms). 연속 실패 횟수에 비례해 증가한다.</summary>
    public const int AcceptRetryDelayMs = 100;

    /// <summary>Accept 재시도 지연 증가 상한 배수.</summary>
    public const int MaxAcceptRetryBackoff = 5;

    /// <summary>서비스 종료 시 모든 세션 정리를 기다리는 기본 시간(ms).</summary>
    public const int DefaultCloseTimeoutMs = 5000;
}

/// <summary>
/// 세션이 종료된 원인.
/// </summary>
public enum CloseReason
{
    /// <summary>원인 미지정(초기값).</summary>
    None = 0,

    /// <summary>로컬에서 Disconnect()를 호출함.</summary>
    Local,

    /// <summary>상대방이 연결을 정상 종료하거나 리셋함.</summary>
    RemoteClosed,

    /// <summary>수신 중 소켓 오류.</summary>
    RecvError,

    /// <summary>송신 중 소켓 오류.</summary>
    SendError,

    /// <summary>패킷 파싱 실패, 수신 버퍼 초과, 콜백 예외 등 프로토콜 위반.</summary>
    ProtocolError,

    /// <summary>서비스 종료로 인한 정리.</summary>
    ServiceClosing,

    /// <summary>송신 대기 데이터가 MaxSendQueueBytes 를 넘어 끊음(수신 측이 너무 느림).</summary>
    SendQueueOverflow,

    /// <summary>IdleTimeoutMs 동안 수신이 없거나, 불완전한 패킷이 완성되지 않아 끊음.</summary>
    IdleTimeout,
}

/// <summary>
/// 서버/클라이언트 서비스 공통 설정.
/// </summary>
public sealed class NetServiceOptions
{
    /// <summary>동시 최대 세션 수. 초과 접속은 즉시 닫는다.</summary>
    public int MaxSessionCount { get; init; } = 1000;

    /// <summary>Nagle 알고리즘 비활성화 여부.</summary>
    public bool NoDelay { get; init; } = true;

    /// <summary>TCP KeepAlive 사용 여부.</summary>
    public bool KeepAlive { get; init; } = true;

    /// <summary>KeepAlive 시작 대기 시간(초).</summary>
    public int KeepAliveTimeSec { get; init; } = 60;

    /// <summary>KeepAlive 프로브 간격(초).</summary>
    public int KeepAliveIntervalSec { get; init; } = 10;

    /// <summary>소켓 송신 버퍼 크기. 0 이하이면 OS 기본값을 유지한다.</summary>
    public int SocketSendBufferSize { get; init; } = 0;

    /// <summary>소켓 수신 버퍼 크기. 0 이하이면 OS 기본값을 유지한다.</summary>
    public int SocketRecvBufferSize { get; init; } = 0;

    /// <summary>
    /// 세션당 송신 대기(큐 + 전송 중) 최대 바이트. 넘으면 SendQueueOverflow 로 연결을 끊는다. 0 이하이면 무제한.
    /// </summary>
    public long MaxSendQueueBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>
    /// true 이면 데이터가 올 때까지 0바이트 수신으로 대기하고, 도착한 뒤에만 수신 버퍼를 대여한다.
    /// 유휴 연결이 많은 서버에서 메모리를 줄이는 대신 메시지마다 수신 호출이 한 번 더 발생한다.
    /// </summary>
    public bool ZeroByteRecv { get; init; } = true;

    /// <summary>
    /// 수신 기준 유휴 제한 시간(ms). 이 시간 동안 아무것도 수신하지 못했거나, 불완전한 패킷이 이 시간 안에
    /// 완성되지 않으면(slowloris 방어) IdleTimeout 으로 끊는다. 0 이하이면 사용하지 않는다.
    /// 상대가 수신만 하는 연결(서버 푸시 전용 등)에는 하트비트가 필요하다.
    /// </summary>
    public int IdleTimeoutMs { get; init; } = 0;

    /// <summary>
    /// true 이면 송신이 진행되는 것(전송 완료)도 활동으로 인정해 유휴 시각을 갱신한다.
    /// 서버가 보내기만 하고 상대는 응답하지 않는 연결(푸시 전용 등)이 하트비트 없이도 끊기지 않게 한다.
    /// 불완전 패킷 정체(slowloris) 검사는 수신 기준 그대로다.
    /// </summary>
    public bool IdleIncludesSend { get; init; } = false;

    /// <summary>
    /// DisconnectAfterSend 후 송신 측만 닫고(FIN) 상대가 연결을 닫기를 기다리는 최대 시간(ms).
    /// 지나면 강제로 닫는다. 0 이하이면 FIN 전송 직후 바로 닫는다.
    /// </summary>
    public int GracefulCloseTimeoutMs { get; init; } = 3000;

    /// <summary>
    /// true 이면 서버가 Accept 한 세션의 OnConnected/수신 시작을 ThreadPool 로 넘겨,
    /// 무거운 OnConnected 가 신규 접속 수락을 지연시키지 않게 한다. 기본값은 Accept 스레드에서 즉시 시작.
    /// </summary>
    public bool StartOnThreadPool { get; init; } = false;

    /// <summary>
    /// 내부 이벤트(서비스 시작/종료, 세션 접속/종료 사유, 접속 거부, 콜백 예외 등)를 받을 로거. null 이면 기록하지 않는다
    /// (단, 세션 콜백 예외는 기존처럼 Trace 로 남는다).
    /// </summary>
    public INetLogger? Logger { get; init; }

    /// <summary>listen backlog (서버 전용).</summary>
    public int Backlog { get; init; } = 512;

    /// <summary>동시에 걸어둘 AcceptAsync 개수 (서버 전용).</summary>
    public int AcceptCount { get; init; } = 16;
}

/// <summary>
/// 서비스 통계 스냅샷. 세션별 카운터를 합산한 값이라 읽는 순간 접속/종료와 겹치면 아주 짧은 시간 동안 오차가 있을 수 있다.
/// </summary>
public readonly record struct NetStatsSnapshot(
    int CurrentSessions,
    long TotalConnected,
    long TotalDisconnected,
    long TotalRejected,
    long BytesReceived,
    long BytesSent,
    long PendingSendBytes);
