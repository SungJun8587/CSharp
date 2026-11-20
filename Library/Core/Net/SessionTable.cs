// SessionTable.cs : 세션 레지스트리 공통부(SessionManager 와 TlsService 가 공유).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Core.Net;

/// <summary>세션 레지스트리/방송/통계가 세션 종류(Session, TlsSession)와 무관하게 쓰는 최소 표면.</summary>
internal interface INetSession
{
    long SessionId { get; }
    bool IsConnected { get; }
    long BytesReceived { get; }
    long BytesSent { get; }
    long PendingSendBytes { get; }
    void Disconnect(CloseReason reason);
    void CheckIdle(long nowTick, int timeoutMs);

    /// <summary>송신 큐에 넣기만 한다. needStart 가 true 이면 호출자가 이어서 <see cref="StartSend"/>를 불러야 한다.</summary>
    bool EnqueueSend(SendBuffer buffer, out bool needStart);

    void StartSend();
}

/// <summary>
/// 세션 id 발급, 등록/해제, 일괄 종료, 유휴 검사, 통계 합산, 방송을 한 곳에 모은다.
/// 방송은 큐 삽입을 호출 스레드에서 하고, 송신 시작(소켓 syscall / TLS 암호화)이 필요한 세션이 많으면 ThreadPool 로 나눈다.
/// </summary>
internal sealed class SessionTable<T> where T : class, INetSession
{
    // 이 수 이하면 호출 스레드에서 바로 시작한다(ThreadPool 로 넘기는 비용이 더 크다). 넘으면 BroadcastChunk 개씩 나눠 넘긴다.
    private const int InlineBroadcastLimit = 32;
    private const int BroadcastChunk = 64;

    private readonly ConcurrentDictionary<long, T> _sessions = new ConcurrentDictionary<long, T>();
    private long _lastSessionId;

    public int Count => _sessions.Count;
    public bool IsEmpty => _sessions.IsEmpty;

    public long NextId() => Interlocked.Increment(ref _lastSessionId);

    public bool TryGet(long sessionId, out T? session) => _sessions.TryGetValue(sessionId, out session);

    public bool Add(T session) => _sessions.TryAdd(session.SessionId, session);

    public bool Remove(T session) => _sessions.TryRemove(session.SessionId, out _);

    public void DisconnectAll(CloseReason reason)
    {
        foreach (var pair in _sessions)
            pair.Value.Disconnect(reason);
    }

    public void CheckIdle(long nowTick, int timeoutMs)
    {
        foreach (var pair in _sessions)
            pair.Value.CheckIdle(nowTick, timeoutMs);
    }

    public void Accumulate(ref int connected, ref long received, ref long sent, ref long pendingSend)
    {
        foreach (var pair in _sessions)
        {
            T session = pair.Value;
            if (!session.IsConnected)
                continue;

            connected++;
            received += session.BytesReceived;
            sent += session.BytesSent;
            pendingSend += session.PendingSendBytes;
        }
    }

    public void Broadcast(SendBuffer buffer)
    {
        List<T>? ready = null;
        foreach (var pair in _sessions)
        {
            T session = pair.Value;
            if (session.EnqueueSend(buffer, out bool needStart) && needStart)
                (ready ??= new List<T>()).Add(session);
        }

        if (ready == null)
            return;

        if (ready.Count <= InlineBroadcastLimit)
        {
            foreach (T session in ready)
                session.StartSend();
            return;
        }

        for (int i = 0; i < ready.Count; i += BroadcastChunk)
        {
            ThreadPool.UnsafeQueueUserWorkItem(static work =>
            {
                for (int k = 0; k < work.Count; ++k)
                    work.Sessions[work.Start + k].StartSend();
            }, new BroadcastWork(ready, i, Math.Min(BroadcastChunk, ready.Count - i)), preferLocal: false);
        }
    }

    private sealed class BroadcastWork
    {
        public readonly List<T> Sessions;
        public readonly int Start;
        public readonly int Count;
        public BroadcastWork(List<T> sessions, int start, int count) { Sessions = sessions; Start = start; Count = count; }
    }
}
