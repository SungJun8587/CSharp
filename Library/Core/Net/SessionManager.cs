// SessionManager.cs : implementation of the SessionManager class.
using System;

namespace Core.Net;

/// <summary>
/// 서비스가 보유한 세션 목록. 키는 소켓 핸들이 아닌 단조 증가하는 sessionId 이므로
/// 소켓 핸들 재사용과 무관하게 세션을 구분한다.
/// 등록/방송/통계는 TLS 서비스와 공유하는 <see cref="SessionTable{T}"/>가 맡는다(ConcurrentDictionary 기반이라 Broadcast 중에도 접속/종료가 블로킹되지 않는다).
/// </summary>
public sealed class SessionManager
{
    private readonly SessionTable<Session> _table = new SessionTable<Session>();

    /// <summary>현재 등록된 세션 수.</summary>
    public int Count => _table.Count;

    /// <summary>등록된 세션이 하나도 없는지.</summary>
    public bool AreAllSessionsClosed => _table.IsEmpty;

    /// <summary>세션 id 로 세션을 찾는다.</summary>
    public bool TryGet(long sessionId, out Session? session) => _table.TryGet(sessionId, out session);

    /// <summary>
    /// 모든 세션에 같은 버퍼를 보낸다. 큐 삽입은 호출 스레드에서 하고, 소켓 송신(SendAsync syscall)이 필요한 세션이 많으면(33개 이상)
    /// 64개씩 ThreadPool 로 나눠, 방송하는 스레드(존 틱 등)가 세션 수만큼 syscall 을 하느라 멈추지 않게 한다.
    /// </summary>
    public void Broadcast(SendBuffer buffer) => _table.Broadcast(buffer);

    /// <summary>모든 세션의 종료를 시작한다.</summary>
    public void BeginCloseAllSessions(CloseReason reason = CloseReason.ServiceClosing) => _table.DisconnectAll(reason);

    internal void CheckIdle(long nowTick, int timeoutMs) => _table.CheckIdle(nowTick, timeoutMs);

    internal void Accumulate(ref int connected, ref long received, ref long sent, ref long pendingSend)
        => _table.Accumulate(ref connected, ref received, ref sent, ref pendingSend);

    internal long GenerateSessionId() => _table.NextId();

    internal bool Add(Session session) => _table.Add(session);

    internal bool Remove(Session session) => _table.Remove(session);
}
