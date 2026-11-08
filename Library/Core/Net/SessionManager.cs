// SessionManager.cs : implementation of the SessionManager class.
using System.Collections.Concurrent;
using System.Threading;

namespace Core.Net;

/// <summary>
/// 서비스가 보유한 세션 목록. 키는 소켓 핸들이 아닌 단조 증가하는 sessionId 이므로
/// 소켓 핸들 재사용과 무관하게 세션을 구분한다.
/// ConcurrentDictionary 가 내부적으로 락을 분할(striping)하고 열거는 락 없이 안전하므로,
/// 별도의 클러스터 분할 없이 Broadcast 중에도 접속/종료가 블로킹되지 않는다.
/// </summary>
public sealed class SessionManager
{
    private readonly ConcurrentDictionary<long, Session> _sessions = new ConcurrentDictionary<long, Session>();
    private long _lastSessionId;

    /// <summary>현재 관리 중인 세션 수(연결 시도 중인 세션 포함).</summary>
    public int Count => _sessions.Count;

    /// <summary>모든 세션이 정리되었는지 여부.</summary>
    public bool AreAllSessionsClosed => _sessions.IsEmpty;

    /// <summary>sessionId 로 세션을 찾는다.</summary>
    public bool TryGet(long sessionId, out Session? session)
        => _sessions.TryGetValue(sessionId, out session);

    /// <summary>
    /// 모든 세션에 같은 SendBuffer 를 보낸다. SendBuffer 는 Close 이후 불변이므로 공유해도 안전하다.
    /// </summary>
    public void Broadcast(SendBuffer buffer)
    {
        foreach (var pair in _sessions)
            pair.Value.Send(buffer);
    }

    /// <summary>모든 세션의 연결 종료를 요청한다. 정리 완료는 비동기로 진행된다.</summary>
    public void BeginCloseAllSessions(CloseReason reason = CloseReason.ServiceClosing)
    {
        foreach (var pair in _sessions)
            pair.Value.Disconnect(reason);
    }

    /// <summary>모든 세션의 유휴 상태를 검사한다(서비스의 유휴 타이머가 호출).</summary>
    internal void CheckIdle(long nowTick, int timeoutMs)
    {
        foreach (var pair in _sessions)
            pair.Value.CheckIdle(nowTick, timeoutMs);
    }

    /// <summary>연결된 세션들의 수신/송신 누적 바이트와 송신 대기 바이트를 합산한다.</summary>
    internal void Accumulate(ref int connected, ref long received, ref long sent, ref long pendingSend)
    {
        foreach (var pair in _sessions)
        {
            Session session = pair.Value;
            if (!session.IsConnected)
                continue;

            connected++;
            received += session.BytesReceived;
            sent += session.BytesSent;
            pendingSend += session.PendingSendBytes;
        }
    }

    internal long GenerateSessionId() => Interlocked.Increment(ref _lastSessionId);

    internal bool Add(Session session) => _sessions.TryAdd(session.SessionId, session);

    internal bool Remove(Session session) => _sessions.TryRemove(session.SessionId, out _);
}
