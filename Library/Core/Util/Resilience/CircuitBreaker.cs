// CircuitBreaker.cs : 서킷 브레이커.
using System;
using System.Threading.Tasks;

namespace Core.Util;

/// <summary>서킷이 열려 있어 호출을 막았을 때 던지는 예외.</summary>
public sealed class CircuitOpenException : Exception
{
    public CircuitOpenException() : base("circuit open") { }
}

public enum CircuitState { Closed, Open, HalfOpen }

/// <summary>
/// 서킷 브레이커. 연속 실패가 threshold번이면 열려서 openMs 동안 호출을 즉시 거절한다(죽은 DB/서버를 계속 두드려 더 느려지는 것을 막는다).
/// 시간이 지나면 반열림이 되어 시험 호출 한 건만 통과시키고, 성공하면 닫히고 실패하면 다시 열린다. 스레드 안전하다.
/// </summary>
public sealed class CircuitBreaker
{
    private readonly int _threshold;
    private readonly long _openMs;
    private readonly Func<long> _clock;
    private readonly object _lock = new object();
    private CircuitState _state = CircuitState.Closed;
    private int _failures;
    private long _openedAtMs;
    private bool _probing;
    private long _probeStartedMs;

    public CircuitBreaker(int failureThreshold = 5, int openMs = 10_000, Func<long>? clock = null)
    {
        if (failureThreshold < 1 || openMs < 1) throw new ArgumentOutOfRangeException(nameof(failureThreshold));
        _threshold = failureThreshold;
        _openMs = openMs;
        _clock = clock ?? TimeUtil.MonotonicNowMillis;
    }

    public CircuitState State
    {
        get
        {
            lock (_lock)
            {
                if (_state == CircuitState.Open && _clock() - _openedAtMs >= _openMs)
                    return CircuitState.HalfOpen;
                return _state;
            }
        }
    }

    /// <summary>호출해도 되는지. true면 호출 뒤 <see cref="OnSuccess"/>나 <see cref="OnFailure"/> 중 하나를 반드시 부른다.</summary>
    public bool TryAllow()
    {
        lock (_lock)
        {
            if (_state == CircuitState.Closed)
                return true;
            if (_state == CircuitState.Open)
            {
                if (_clock() - _openedAtMs < _openMs)
                    return false;
                _state = CircuitState.HalfOpen;
                _probing = false;
            }
            // 반열림: 시험 호출은 한 건만. 시험 호출자가 결과를 보고하지 않고 사라진 경우를 위해 openMs가 지나면 다시 허용한다.
            long now = _clock();
            if (_probing && now - _probeStartedMs < _openMs)
                return false;
            _probing = true;
            _probeStartedMs = now;
            return true;
        }
    }

    /// <summary>성공을 보고한다. 이미 열린 뒤에 늦게 도착한 성공(열리기 전에 시작한 호출)은 무시한다 — 시험 호출 없이 닫히면 안 된다.</summary>
    public void OnSuccess()
    {
        lock (_lock)
        {
            if (_state == CircuitState.Open)
                return;
            _failures = 0;
            _probing = false;
            _state = CircuitState.Closed;
        }
    }

    /// <summary>호출이 성공도 실패도 아닌 경우(취소, 호출자 인자 오류 등). 상태는 그대로 두고 반열림 시험 호출 자리만 돌려준다.</summary>
    public void OnNeutral()
    {
        lock (_lock)
            _probing = false;
    }

    public void OnFailure()
    {
        lock (_lock)
        {
            if (_state == CircuitState.Open)
                return;         // 열린 뒤에 늦게 도착한 실패로 열림 시간이 다시 시작되면 안 된다
            _probing = false;
            if (_state == CircuitState.HalfOpen || ++_failures >= _threshold)
            {
                _state = CircuitState.Open;
                _openedAtMs = _clock();
                _failures = 0;
            }
        }
    }

    /// <summary>예외가 실패로 셀 만한 것인가의 기본 판정: 취소는 실패가 아니다.</summary>
    public static bool DefaultIsFailure(Exception ex) => !(ex is OperationCanceledException);

    /// <summary>서킷을 거쳐 실행한다. 열려 있으면 <see cref="CircuitOpenException"/>. isFailure가 false를 돌려주는 예외는 실패로 세지 않고 그대로 던진다.</summary>
    public T Execute<T>(Func<T> action, Func<Exception, bool>? isFailure = null)
    {
        if (!TryAllow())
            throw new CircuitOpenException();
        try
        {
            T r = action();
            OnSuccess();
            return r;
        }
        catch (Exception ex)
        {
            if ((isFailure ?? DefaultIsFailure)(ex)) OnFailure(); else OnNeutral();
            throw;
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, Func<Exception, bool>? isFailure = null)
    {
        if (!TryAllow())
            throw new CircuitOpenException();
        try
        {
            T r = await action().ConfigureAwait(false);
            OnSuccess();
            return r;
        }
        catch (Exception ex)
        {
            if ((isFailure ?? DefaultIsFailure)(ex)) OnFailure(); else OnNeutral();
            throw;
        }
    }
}
