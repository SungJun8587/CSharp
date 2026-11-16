// RedisDefine.cs : Redis 모듈의 열거형, 상수, 에러 메시지.
namespace Core.Redis;

/// <summary>RESP 프로토콜의 기본 데이터 타입.</summary>
public enum RedisType
{
    Unknown,
    SimpleString,   // +
    Error,          // -
    Integer,        // :
    BulkString,     // $
    Array           // *
}

/// <summary>RESP 파싱 결과.</summary>
public enum RedisParseResult
{
    Success,        // RESP 값 하나를 완전히 파싱함
    NeedMoreData,   // 데이터가 부족하여 추가 수신이 필요함 — 정상적인 상황
    ProtocolError   // RESP 형식이 잘못됨 — 호출부는 이 연결을 끊어야 한다
}

/// <summary>연결을 끊은 사유. 대기 중이던 명령의 콜백에 전달되는 에러 문자열을 결정한다.</summary>
public enum RedisDisconnectReason
{
    ConnectionClosedRecv,   // 수신이 0바이트로 끝남 — 상대가 연결을 닫음
    ConnectionClosedSend,   // 송신 중 상대가 연결을 닫음
    SendFailed,             // 송신 자체가 실패
    ProtocolError,          // RESP 파싱 실패 — 네트워크 문제가 아니라 수신 데이터 해석 실패
    CommandTimeout,         // 명령 응답이 제한 시간 안에 오지 않아 풀이 연결을 끊음(서버 정지/반열림 연결)
    Generic                 // 그 외 일반적인 종료(Dispose, Connect() 실패 등)
}

/// <summary>Redis 모듈 상수와 클라이언트가 직접 만드는 에러 응답 문자열.</summary>
public static class RedisConst
{
    // ---- 파서 한계값 (RedisParser) ----

    /// <summary>미완성 RESP 스트림의 최대 누적 크기.</summary>
    public const int MaxPendingBufferSize = 16 * 1024 * 1024;

    /// <summary>하나의 배열이 가질 수 있는 최대 원소 수 — 악의적/손상된 스트림의 대량 할당 시도를 막는다.</summary>
    public const long MaxArrayElements = 4096;

    /// <summary>중첩 배열의 최대 재귀 깊이 — 스택 오버플로 방지.</summary>
    public const int MaxParseDepth = 64;

    /// <summary>파서 누적 버퍼가 비었을 때 이 크기를 넘게 커져 있으면 작은 버퍼로 되돌린다.</summary>
    public const int ParserRetainLimit = 1 << 20;

    // ---- 클라이언트 (RedisClient) ----

    /// <summary>수신 버퍼 크기. 한 번의 수신이 받는 최대 바이트.</summary>
    public const int RecvBufferSize = 16 * 1024;

    /// <summary>명령 하나(RESP로 인코딩한 전체 크기)의 상한. Redis 서버의 proto-max-bulk-len(512MB)에 맞춘다.</summary>
    public const int MaxCommandSize = 512 * 1024 * 1024;

    /// <summary>TCP keepalive 유휴 시간(초)과 프로브 간격(초).</summary>
    public const int KeepAliveIdleSec = 30;
    public const int KeepAliveIntervalSec = 10;

    /// <summary>접속(이름 해석 포함) 기본 제한 시간.</summary>
    public const int ConnectTimeoutMs = 3000;

    /// <summary>동시에 살아 있을 수 있는 이름 해석 작업 수의 프로세스 전체 상한.</summary>
    public const int MaxActiveResolvers = 4;

    // ---- 커넥션 풀 (RedisConnectionPool) ----

    /// <summary>재연결 점검 주기 기본값(초).</summary>
    public const int DefaultReconnectIntervalSec = 3;

    /// <summary>대기 큐에 쌓을 수 있는 요청 상한. 넘으면 새 요청은 거부된다.</summary>
    public const int MaxPendingCommands = 2000;

    /// <summary>대기 큐에서 이 시간을 넘긴 요청은 연결 오류로 끝낸다(점검 주기 단위로 확인).</summary>
    public const long PendingTimeoutMs = 5000;

    /// <summary>명령을 보낸 뒤 이 시간 안에 응답이 없으면 커넥션을 끊는다.</summary>
    public const long PoolCommandTimeoutMs = 10000;

    /// <summary>Init()과 재연결 스윕이 커넥션을 동시에 맺는 최대 워커 수.</summary>
    public const int MaxConnectWorkers = 16;

    /// <summary>"이전 연결 정리가 덜 끝났다"며 건너뛴 클라이언트를 다시 점검하기까지의 짧은 대기(ms).</summary>
    public const int ReconnectRecheckMs = 100;

    // ---- 재연결 지수 백오프 (풀과 구독 연결 공용) ----
    public const long ReconnectBackoffBaseMs = 500;
    public const long ReconnectBackoffMaxMs = 30000;
    public const int ReconnectBackoffMaxShift = 6;
    public const int ReconnectBackoffJitterMs = 250;

    // ---- 구독 연결 (RedisSubscriber) ----
    public const int SubscriberReconnectIntervalSec = 1;
    public const long SubscriberPingIntervalMs = 5000;
    public const long SubscriberCommandTimeoutMs = 5000;

    // ---- 클라이언트가 직접 만드는 에러 응답 문자열 ----
    // IsConnectionError()는 ErrConnectionClosed/ErrSendFailed/ErrProtocolError로 시작하는 에러를 "연결 문제"(재시도 대상)로 본다.
    public const string ErrConnectionClosed = "ERR connection closed";
    public const string ErrConnectionClosedRecv = "ERR connection closed (recv 0 bytes)";
    public const string ErrConnectionClosedSend = "ERR connection closed (send 0 bytes)";
    public const string ErrCommandTimeout = "ERR connection closed (command timeout)";
    public const string ErrPoolCleared = "ERR connection closed (pool cleared)";
    public const string ErrPendingTimeout = "ERR connection closed (pending timeout)";
    public const string ErrSendFailed = "ERR send failed";
    public const string ErrProtocolError = "ERR protocol error";
    public const string ErrCommandInFlight = "ERR command already in flight";
    public const string ErrSendRejected = "SendCommand rejected";   // 풀 없음/대기 큐 포화로 전송이 거절됨
    public const string ErrCommandTooLarge = "ERR command too large";
}
