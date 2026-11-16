// ZoneRouter.cs : zoneId -> 존 서버 노드 라우팅 테이블.
using System.Collections.Generic;
using System.Threading;
using Game.Protocol;

namespace Game.GatewayServer;

/// <summary>
/// Gateway가 관리하는 존 서버 하나의 등록 정보와 부하 상태. 가변 값은 Interlocked/Volatile로 락 없이 갱신/조회한다.
/// 노드는 참조로 공유되어, 라우팅 테이블에서 제거된 뒤에도 진행 중인 요청이 안전하게 마칠 수 있다.
/// </summary>
public sealed class ZoneNode
{
    public uint ZoneId;
    public ulong Seq;                           // 등록 순번(Register마다 증가). 같은 zoneId가 새 세션으로 교체됐는지 구분한다
    public ZoneKind Kind = ZoneKind.Field;
    public uint Capacity;                       // 최대 수용 인원
    public string ServerName = string.Empty;
    public GatewayZoneSession Session = null!;  // 이 존 서버와의 연결 세션

    internal int ActiveUsers;                   // Gateway 집계 인원(입장 진행 중 포함) — 입장 제한 기준
    internal long LastHeartbeatMs;              // 마지막 하트비트 수신 시각(Environment.TickCount64 기준)
    internal uint ReportedUsers;                // 존이 보고한 인원(모니터링용)
    internal uint LastTickUs;
    internal uint MaxTickUs;

    public int Active => Volatile.Read(ref ActiveUsers);
    public long LastHeartbeat => Interlocked.Read(ref LastHeartbeatMs);
    public uint Reported => Volatile.Read(ref ReportedUsers);
    public uint LastTick => Volatile.Read(ref LastTickUs);
    public uint MaxTick => Volatile.Read(ref MaxTickUs);

    public void RecordHeartbeat(long nowMs, uint users, uint lastTickUs, uint maxTickUs)
    {
        Interlocked.Exchange(ref LastHeartbeatMs, nowMs);
        Volatile.Write(ref ReportedUsers, users);
        Volatile.Write(ref LastTickUs, lastTickUs);
        Volatile.Write(ref MaxTickUs, maxTickUs);
    }

    public void TouchHeartbeat(long nowMs) => Interlocked.Exchange(ref LastHeartbeatMs, nowMs);
}

/// <summary>
/// 등록/해제는 드물고 조회는 입장 요청·패킷 중계마다 일어나므로 읽기/쓰기 락으로 보호한다.
/// 입장 정원은 노드의 카운터(TryReserveSlot/ReleaseSlot)로 락 없이 CAS 처리한다.
/// </summary>
public sealed class ZoneRouter
{
    private static long s_nodeSeq;      // ZoneNode.Seq 발급용 (프로세스 전역, 0은 "없음"으로 비워 둔다)

    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly Dictionary<uint, ZoneNode> _nodes = new Dictionary<uint, ZoneNode>();

    /// <summary>
    /// 존 서버를 등록한다. 같은 zoneId가 살아있는 다른 세션으로 이미 등록돼 있으면 거부하고,
    /// 이전 세션이 끊긴(또는 끊기는 중인) 상태면 새 노드로 교체한다.
    /// </summary>
    public ZoneRegisterResult Register(GatewayZoneSession session, uint zoneId, ZoneKind kind, uint capacity, string serverName, long nowMs, out ZoneNode? node)
    {
        node = null;
        if (zoneId == 0 || capacity == 0)
            return ZoneRegisterResult.InvalidZone;

        _lock.EnterWriteLock();
        try
        {
            if (_nodes.TryGetValue(zoneId, out ZoneNode? existing) && existing.Session.IsConnected)
                return ZoneRegisterResult.DuplicateZone;

            ZoneNode n = new ZoneNode
            {
                ZoneId = zoneId,
                Seq = (ulong)Interlocked.Increment(ref s_nodeSeq),
                Kind = kind,
                Capacity = capacity,
                ServerName = serverName,
                Session = session,
                LastHeartbeatMs = nowMs,
            };
            _nodes[zoneId] = n;
            node = n;
            return ZoneRegisterResult.Ok;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>zoneId의 노드가 이 세션이 등록한 것일 때만 제거한다.</summary>
    public bool Unregister(uint zoneId, GatewayZoneSession session)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_nodes.TryGetValue(zoneId, out ZoneNode? n) || !ReferenceEquals(n.Session, session))
                return false;
            _nodes.Remove(zoneId);
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public ZoneNode? Find(uint zoneId)
    {
        _lock.EnterReadLock();
        try
        {
            return _nodes.TryGetValue(zoneId, out ZoneNode? n) ? n : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>현재 등록된 노드의 복사본(감시/모니터링용).</summary>
    public List<ZoneNode> Snapshot()
    {
        _lock.EnterReadLock();
        try
        {
            return new List<ZoneNode>(_nodes.Values);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>정원이 남아 있으면 한 자리를 예약한다(CAS).</summary>
    public static bool TryReserveSlot(ZoneNode node)
    {
        int current = Volatile.Read(ref node.ActiveUsers);
        while (current < (long)node.Capacity)
        {
            int seen = Interlocked.CompareExchange(ref node.ActiveUsers, current + 1, current);
            if (seen == current)
                return true;
            current = seen;
        }
        return false;
    }

    /// <summary>예약한 자리를 반환한다. 0 아래로는 내려가지 않는다.</summary>
    public static void ReleaseSlot(ZoneNode node)
    {
        int current = Volatile.Read(ref node.ActiveUsers);
        while (current > 0)
        {
            int seen = Interlocked.CompareExchange(ref node.ActiveUsers, current - 1, current);
            if (seen == current)
                return;
            current = seen;
        }
    }
}
