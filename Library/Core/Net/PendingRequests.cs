// PendingRequests.cs : implementation of the PendingRequests class.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core.Util;

namespace Core.Net;

/// <summary>대기 중이던 요청이 응답 없이 끝난 이유.</summary>
public enum PendingFailure
{
    /// <summary>제한 시간 안에 응답이 오지 않음.</summary>
    Timeout,

    /// <summary>요청을 보낸 세션(소유자)이 끊김.</summary>
    OwnerClosed,

    /// <summary>호출자가 취소함.</summary>
    Cancelled,

    /// <summary>PendingRequests 가 종료됨.</summary>
    Shutdown,
}

/// <summary>RequestAsync 가 응답 대신 실패로 끝났을 때 던지는 예외.</summary>
public sealed class PendingRequestException : Exception
{
    public PendingRequestException(PendingFailure failure)
        : base($"요청이 응답 없이 끝났습니다: {failure}") => Failure = failure;

    public PendingFailure Failure { get; }
}

/// <summary>
/// 요청 id 로 응답을 요청에 짝지어 주는 대기 테이블. 요청마다 제한 시간이 있고, 소유자(보통 세션)가 끊기면 그 소유자의 요청을 한꺼번에 정리한다.
///
/// 흐름:
///   1) Register / RequestAsync 가 요청 id(항상 1 이상)를 발급한다. 호출자는 이 id 를 패킷에 실어 보낸다.
///   2) 응답 패킷이 오면 수신 쪽이 TryComplete(id, owner, response) 를 부른다. 콜백은 정확히 한 번만 실행된다.
///   3) 응답이 오지 않으면 Advance 가 제한 시간을 넘긴 요청을 Timeout 으로 끝낸다.
///   4) 세션이 끊기면 CancelOwner 로 그 세션의 요청을 OwnerClosed 로 끝낸다.
///
/// TryComplete 는 요청을 만든 소유자와 응답한 소유자가 같을 때만 성공한다. 그래서 다른 세션이 id 를 추측해서 남의 요청에 응답할 수 없다.
/// 스레드 안전하다. 콜백(onResponse / onFailure)은 내부 락 밖에서 호출되므로 콜백 안에서 이 객체를 다시 불러도 된다.
/// 시간은 ms 이며 기본은 Environment.TickCount64 이다. Advance 는 호출자가 주기적으로(예: 100ms~1s) 불러 준다.
///
/// 사용 예:
///   var pending = new PendingRequests&lt;Reply&gt;();
///   // 송신 쪽
///   Reply r = await pending.RequestAsync(session.Id, 3000, id => session.Send(MakeRequest(id)));
///   // 수신 쪽 (응답 패킷 핸들러)
///   pending.TryComplete(reply.RequestId, session.Id, reply);
///   // 세션 종료 시
///   pending.CancelOwner(session.Id);
///   // 주기 작업
///   pending.Advance();
/// </summary>
public sealed class PendingRequests<TResponse> : IDisposable
{
    private sealed class Entry
    {
        public long Id;
        public long Owner;
        public Action<TResponse>? OnResponse;
        public Action<PendingFailure>? OnFailure;
        public TimerHandle Timer;
    }

    private readonly object _gate = new object();
    private readonly Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();
    private readonly Dictionary<long, HashSet<long>> _byOwner = new Dictionary<long, HashSet<long>>();
    private readonly TimerWheel _wheel;
    private readonly Func<long> _clock;
    private readonly int _maxPerOwner;
    private readonly Action<Exception>? _onError;
    private readonly List<Entry> _expired = new List<Entry>();
    private long _nextId;
    private bool _disposed;

    /// <param name="maxPerOwner">소유자 하나가 동시에 가질 수 있는 대기 요청 수. 넘으면 Register 가 0 을 돌려준다.</param>
    /// <param name="tickMs">타임아웃 해상도(ms).</param>
    /// <param name="clock">현재 시각(ms). null 이면 Environment.TickCount64.</param>
    /// <param name="onError">콜백에서 예외가 났을 때 알릴 곳. null 이면 삼킨다.</param>
    public PendingRequests(int maxPerOwner = 1024, int tickMs = 10, Func<long>? clock = null, Action<Exception>? onError = null)
    {
        _onError = onError;
        if (maxPerOwner <= 0) throw new ArgumentOutOfRangeException(nameof(maxPerOwner));
        _maxPerOwner = maxPerOwner;
        _clock = clock ?? (() => NetClock.NowMs);
        _wheel = new TimerWheel(_clock(), tickMs, onError);
    }

    /// <summary>현재 대기 중인 요청 수.</summary>
    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>한 소유자의 대기 요청 수.</summary>
    public int CountOf(long owner)
    {
        lock (_gate) return _byOwner.TryGetValue(owner, out HashSet<long>? set) ? set.Count : 0;
    }

    /// <summary>
    /// 요청을 등록하고 id 를 돌려준다.
    /// </summary>
    /// <param name="owner">요청을 보낸 주체(세션 id 등). 응답할 때 같은 값이어야 한다.</param>
    /// <param name="timeoutMs">응답을 기다릴 최대 시간(ms). 1 이상.</param>
    /// <param name="onResponse">응답이 왔을 때 한 번 호출.</param>
    /// <param name="onFailure">응답 없이 끝났을 때 한 번 호출. onResponse 와 둘 중 하나만 호출된다.</param>
    /// <returns>요청 id (1 이상). 소유자 한도 초과나 종료 상태면 0 이며 콜백은 호출되지 않는다.</returns>
    public long Register(long owner, int timeoutMs, Action<TResponse> onResponse, Action<PendingFailure>? onFailure = null)
    {
        if (onResponse == null) throw new ArgumentNullException(nameof(onResponse));
        if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));

        lock (_gate)
        {
            if (_disposed) return 0;

            if (!_byOwner.TryGetValue(owner, out HashSet<long>? set))
            {
                set = new HashSet<long>();
                _byOwner.Add(owner, set);
            }
            if (set.Count >= _maxPerOwner)
            {
                if (set.Count == 0) _byOwner.Remove(owner);
                return 0;
            }

            long id = ++_nextId;
            var entry = new Entry { Id = id, Owner = owner, OnResponse = onResponse, OnFailure = onFailure };

            // 휠은 마지막 Advance 시각 기준이므로, Advance 가 한동안 없었다면 그 만큼을 더해 실제 경과를 반영한다.
            long lag = Math.Max(0, _clock() - _wheel.CurrentMs);
            entry.Timer = _wheel.Schedule(timeoutMs + lag, () => _expired.Add(entry));

            _entries.Add(id, entry);
            set.Add(id);
            return id;
        }
    }

    /// <summary>
    /// 요청을 보내고 응답을 기다린다. send 는 발급된 id 를 받아 패킷을 보내는 동작이다.
    /// 시간 초과, 소유자 종료, 취소는 PendingRequestException 으로 전달된다.
    /// </summary>
    /// <exception cref="InvalidOperationException">소유자 한도 초과이거나 종료 상태일 때</exception>
    public Task<TResponse> RequestAsync(long owner, int timeoutMs, Action<long> send, CancellationToken cancellationToken = default)
    {
        if (send == null) throw new ArgumentNullException(nameof(send));
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<TResponse>(cancellationToken);

        var tcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        long id = Register(owner, timeoutMs,
            response => tcs.TrySetResult(response),
            failure => tcs.TrySetException(new PendingRequestException(failure)));
        if (id == 0)
            throw new InvalidOperationException("대기 요청을 등록할 수 없습니다(소유자 한도 초과 또는 종료됨).");

        CancellationTokenRegistration reg = default;
        if (cancellationToken.CanBeCanceled)
        {
            reg = cancellationToken.Register(() =>
            {
                // 토큰 취소는 OperationCanceledException 으로 전달해야 하므로 Cancel 이 예외를 먼저 넣기 전에 태스크를 취소 상태로 만든다.
                tcs.TrySetCanceled(cancellationToken);
                Cancel(id);
            });
            tcs.Task.ContinueWith(_ => reg.Dispose(), TaskScheduler.Default);
        }

        try
        {
            send(id);
        }
        catch
        {
            // 보내지 못한 요청이 타임아웃까지 남지 않게 바로 치운다.
            Remove(id, out _);
            reg.Dispose();
            throw;
        }
        return tcs.Task;
    }

    /// <summary>
    /// 응답을 요청에 연결한다. id 가 없거나(이미 완료/만료) owner 가 요청자와 다르면 false 이며 아무 일도 하지 않는다.
    /// </summary>
    public bool TryComplete(long id, long owner, TResponse response)
    {
        Entry? entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out entry) || entry.Owner != owner)
                return false;
            RemoveLocked(entry);
        }

        Invoke(entry.OnResponse, response);
        return true;
    }

    /// <summary>요청을 취소한다(onFailure 에 Cancelled). 이미 끝났으면 false.</summary>
    public bool Cancel(long id)
    {
        if (!Remove(id, out Entry? entry)) return false;
        Invoke(entry!.OnFailure, PendingFailure.Cancelled);
        return true;
    }

    /// <summary>한 소유자의 대기 요청을 모두 끝낸다. 세션이 끊길 때 부른다.</summary>
    /// <returns>끝낸 요청 수</returns>
    public int CancelOwner(long owner, PendingFailure reason = PendingFailure.OwnerClosed)
    {
        List<Entry> taken;
        lock (_gate)
        {
            if (!_byOwner.TryGetValue(owner, out HashSet<long>? set))
                return 0;

            taken = new List<Entry>(set.Count);
            foreach (long id in set)
                if (_entries.TryGetValue(id, out Entry? e)) taken.Add(e);
            foreach (Entry e in taken)
                RemoveLocked(e);
        }

        foreach (Entry e in taken)
            Invoke(e.OnFailure, reason);
        return taken.Count;
    }

    /// <summary>제한 시간을 넘긴 요청을 Timeout 으로 끝낸다.</summary>
    /// <returns>만료시킨 요청 수</returns>
    public int Advance() => Advance(_clock());

    /// <inheritdoc cref="Advance()"/>
    /// <param name="nowMs">현재 시각(ms). 생성 시 clock 과 같은 시간축이어야 한다.</param>
    public int Advance(long nowMs)
    {
        Entry[] fired;
        lock (_gate)
        {
            if (_disposed) return 0;
            _wheel.Advance(nowMs);
            if (_expired.Count == 0) return 0;

            fired = _expired.ToArray();
            _expired.Clear();
            foreach (Entry e in fired)
                RemoveLocked(e, cancelTimer: false);
        }

        foreach (Entry e in fired)
            Invoke(e.OnFailure, PendingFailure.Timeout);
        return fired.Length;
    }

    /// <summary>남은 요청을 모두 Shutdown 으로 끝내고 이후 등록을 막는다.</summary>
    public void Dispose()
    {
        List<Entry> rest;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            rest = new List<Entry>(_entries.Values);
            _entries.Clear();
            _byOwner.Clear();
            _expired.Clear();
        }

        foreach (Entry e in rest)
            Invoke(e.OnFailure, PendingFailure.Shutdown);
    }

    private bool Remove(long id, out Entry? entry)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out entry))
                return false;
            RemoveLocked(entry);
            return true;
        }
    }

    // _gate 를 잡은 상태에서만 부른다.
    private void RemoveLocked(Entry entry, bool cancelTimer = true)
    {
        _entries.Remove(entry.Id);
        if (_byOwner.TryGetValue(entry.Owner, out HashSet<long>? set))
        {
            set.Remove(entry.Id);
            if (set.Count == 0) _byOwner.Remove(entry.Owner);
        }
        if (cancelTimer) _wheel.Cancel(entry.Timer);
    }

    private void Invoke<T>(Action<T>? callback, T arg)
    {
        if (callback == null) return;
        try { callback(arg); }
        catch (Exception e)
        {
            try { _onError?.Invoke(e); } catch { /* 오류 보고 실패는 무시 */ }
        }
    }
}
