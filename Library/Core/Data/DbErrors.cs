// DbErrors.cs : MySQL 서버 오류 번호 중 "다시 해 보면 될 수 있는" 것들.
using System;
using System.Collections.Generic;

namespace Core.Data;

public static class DbErrors
{
    public const int TooManyConnections = 1040;
    public const int UnableToConnect = 1042;          // MySqlConnector 가 서버에 닿지 못했을 때 내는 번호(MySqlErrorCode.UnableToConnectToHost)
    public const int DuplicateEntry = 1062;
    public const int LockWaitTimeout = 1205;
    public const int LockDeadlock = 1213;
    public const int ServerShutdown = 1053;
    public const int ServerGone = 2006;
    public const int ServerLost = 2013;
    public const int QueryInterrupted = 1317;
    public const int ConnectionKilled = 1927;
    public const int QueryTimeout = 3024;             // 서버의 max_execution_time 초과
    public const int CantConnectLocal = 2002;
    public const int CantConnectHost = 2003;

    private static readonly HashSet<int> Transient = new HashSet<int>
    {
        TooManyConnections, UnableToConnect, LockWaitTimeout, LockDeadlock, ServerShutdown, ServerGone, ServerLost, QueryInterrupted, ConnectionKilled,
    };

    private static readonly HashSet<int> Outage = new HashSet<int>
    {
        TooManyConnections, UnableToConnect, ServerShutdown, CantConnectLocal, CantConnectHost, ServerGone, ServerLost, ConnectionKilled,
    };

    /// <summary>DB 에 닿지 못했거나 연결이 끊긴 오류 번호인가(서킷 브레이커가 세는 실패). 교착/락 대기 초과/SQL 오류는 서버가 응답한 것이라 포함하지 않는다.</summary>
    public static bool IsOutageCode(int mysqlErrorNumber) => Outage.Contains(mysqlErrorNumber);

    /// <summary>
    /// 커넥션 풀의 연결이 모두 사용 중이라 기다리다 시간이 초과된 오류인가(MySqlConnector 의 메시지로 판별한다). DB 가 죽은 것이 아니라 이쪽 부하라서
    /// 서킷 브레이커가 세지 않고 재시도도 하지 않는다(재시도하면 풀 대기만 더 늘어난다).
    /// </summary>
    public static bool IsPoolExhaustedMessage(string? message)
        => message != null && message.IndexOf("pooled connections are in use", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// 명령 시간 초과(DefaultCommandTimeout/CommandTimeout)인가(MySqlConnector 의 메시지로 판별한다). 연결 문제가 아니라 쿼리가 느린 것이라
    /// 서킷 브레이커가 세지 않고 재시도도 하지 않는다(재시도하면 느린 쿼리가 곱해져 DB 부하만 늘어난다).
    /// </summary>
    public static bool IsCommandTimeoutMessage(string? message)
        => message != null && message.IndexOf("Command Timeout expired", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>같은 작업을 다시 시도해 볼 만한 오류 번호인가(교착, 락 대기 초과, 연결 끊김/과다).</summary>
    public static bool IsTransientCode(int mysqlErrorNumber) => Transient.Contains(mysqlErrorNumber);
}
