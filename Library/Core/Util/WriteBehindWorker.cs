// WriteBehindWorker.cs : WriteBehindQueue 를 전용 스레드로 비워 저장 함수에 넘기는 쓰기 지연 워커.
using System;
using System.Collections.Generic;
using System.Threading;

namespace Core.Util;

/// <summary>
/// DB 저장 같은 느린 쓰기를 호출 스레드 밖으로 빼는 워커. <see cref="WriteBehindQueue{TKey,TValue}"/>에 같은 키를 합쳐 모으고,
/// 전용 스레드가 flushIntervalMs 이상 묵은 것을 최대 batchSize개씩 꺼내 저장 함수에 넘긴다.
///  - 읽기: 아직 저장 중이거나 대기 중인 최신 값을 <see cref="TryGetPending"/>으로 먼저 볼 수 있다(방금 저장한 값을 옛 저장소 값으로 읽는 일이 없게).
///  - 실패: 묶음 저장이 실패하면 한 건씩 나눠 다시 시도해 문제 있는 한 건 때문에 전체가 막히지 않게 하고, 한 건이 maxAttempts번 실패하면 버린다.
///    실패 뒤에는 <see cref="BackoffState"/>로 쉬었다가(retryBackoffMs부터 maxBackoffMs까지 두 배씩) 다시 시도한다.
///  - 종료: Dispose 는 남은 것을 모두 내려 쓸 때까지 shutdownTimeoutMs 안에서 기다린다. Flush 는 같은 일을 종료 없이 한다.
///    Flush/Dispose 는 묵는 시간(flushIntervalMs)만 건너뛰고 실패 뒤 백오프는 그대로 지킨다. 저장소가 죽어 있는 동안 재시도 횟수가 순식간에 소진돼 데이터가 버려지지 않게 하려는 것이며,
///    그 경우 못 내려 쓴 것은 제한 시간이 끝날 때 로그로 남고 Pending 으로 보인다.
/// 값은 넣은 뒤 바뀌지 않아야 한다(호출자가 복사해서 넣는다). 저장 함수는 워커 스레드에서 불리며 실패하면 예외를 던진다.
/// 큐 대기가 늘어도 키 수 이상 쌓이지 않는다.
/// </summary>
public sealed class WriteBehindWorker<TKey, TValue> : IDisposable where TKey : notnull
{
    private readonly string _name;
    private readonly Action<IReadOnlyList<KeyValuePair<TKey, TValue>>, CancellationToken> _saveBatch;
    private readonly Action<TKey, TValue, CancellationToken> _saveOne;
    private readonly int _flushIntervalMs;
    private readonly int _batchSize;
    private readonly int _maxAttempts;
    private readonly int _shutdownTimeoutMs;
    private readonly int _pollMs;
    private readonly WriteBehindQueue<TKey, TValue> _queue = new WriteBehindQueue<TKey, TValue>();
    private readonly BackoffState _backoff;
    private readonly object _lock = new object();
    private readonly Dictionary<TKey, TValue> _inflight = new Dictionary<TKey, TValue>();      // 저장하러 꺼냈지만 아직 끝나지 않은 것
    private readonly Dictionary<TKey, int> _attempts = new Dictionary<TKey, int>();             // 연속 실패 횟수(실패한 키만)
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly Thread _worker;
    private bool _stopping;
    private bool _flushNow;
    private long _saved, _failedBatches, _dropped, _lastFlushMs, _maxFlushMs;

    /// <param name="name">스레드 이름과 로그 머리말.</param>
    /// <param name="saveBatch">여러 건을 한 번에 저장한다(가능하면 한 트랜잭션). 하나라도 실패하면 예외.</param>
    /// <param name="saveOne">한 건을 저장한다. 묶음이 실패했을 때 건별 재시도에 쓴다. 실패하면 예외.</param>
    /// <param name="flushIntervalMs">키가 처음 들어온 뒤 이만큼 묵으면 내려 쓴다(그동안 같은 키는 합쳐진다).</param>
    /// <param name="batchSize">한 번에 저장할 최대 건수.</param>
    /// <param name="maxAttempts">한 건이 연속으로 이만큼 실패하면 버린다.</param>
    /// <param name="shutdownTimeoutMs">Dispose 가 남은 것을 비우려고 기다리는 최대 시간.</param>
    /// <param name="retryBackoffMs">실패 뒤 첫 대기 시간. 실패가 이어지면 두 배씩 늘어난다.</param>
    /// <param name="maxBackoffMs">대기 시간 상한.</param>
    public WriteBehindWorker(string name, Action<IReadOnlyList<KeyValuePair<TKey, TValue>>, CancellationToken> saveBatch, Action<TKey, TValue, CancellationToken> saveOne,
        int flushIntervalMs = 1000, int batchSize = 100, int maxAttempts = 5, int shutdownTimeoutMs = 10000, int retryBackoffMs = 1000, int maxBackoffMs = 30000)
    {
        _name = name;
        _saveBatch = saveBatch ?? throw new ArgumentNullException(nameof(saveBatch));
        _saveOne = saveOne ?? throw new ArgumentNullException(nameof(saveOne));
        _flushIntervalMs = Math.Max(10, flushIntervalMs);
        _batchSize = Math.Max(1, batchSize);
        _maxAttempts = Math.Max(1, maxAttempts);
        _shutdownTimeoutMs = Math.Max(0, shutdownTimeoutMs);
        _pollMs = Math.Clamp(_flushIntervalMs / 4, 10, 250);
        int baseMs = Math.Max(1, retryBackoffMs);
        _backoff = new BackoffState(baseMs, Math.Max(baseMs, maxBackoffMs));
        _worker = new Thread(WorkerLoop) { Name = name + "Writer", IsBackground = true };
        _worker.Start();
    }

    /// <summary>아직 내려 쓰지 못한 키 수(대기 + 저장 중).</summary>
    public int Pending
    {
        get
        {
            lock (_lock)
            {
                int n = _queue.Count;
                foreach (TKey k in _inflight.Keys)
                    if (!_queue.TryGet(k, out _))
                        ++n;
                return n;
            }
        }
    }

    public long Saved => Interlocked.Read(ref _saved);
    public long FailedBatches => Interlocked.Read(ref _failedBatches);
    public long Dropped => Interlocked.Read(ref _dropped);
    /// <summary>가장 최근 한 번 내려 쓰는 데 걸린 시간(ms)과 지금까지의 최대.</summary>
    public long LastFlushMs => Interlocked.Read(ref _lastFlushMs);
    public long MaxFlushMs => Interlocked.Read(ref _maxFlushMs);

    /// <summary>값을 저장 대기열에 넣는다(바로 돌아온다). 같은 키의 앞선 대기는 이 값으로 대체된다. 종료 중이면 false.</summary>
    public bool Enqueue(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_stopping)
                return false;
            _attempts.Remove(key);
            _queue.Enqueue(key, value, TimeUtil.MonotonicNowMillis());
            return true;
        }
    }

    /// <summary>아직 저장소에 내려가지 않은 최신 값을 돌려준다(대기 중인 값이 저장 중인 값보다 새롭다). 없으면 false.</summary>
    public bool TryGetPending(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_queue.TryGet(key, out value))
                return true;
            return _inflight.TryGetValue(key, out value!);
        }
    }

    /// <summary>대기 중인 것을 모두 내려 쓸 때까지(또는 제한 시간까지) 기다린다. 다 내려 썼으면 true.</summary>
    public bool Flush(int timeoutMs)
    {
        long end = TimeUtil.MonotonicNowMillis() + timeoutMs;
        lock (_lock)
        {
            _flushNow = true;
            Monitor.PulseAll(_lock);
            while (_queue.Count != 0 || _inflight.Count != 0)
            {
                long left = end - TimeUtil.MonotonicNowMillis();
                if (left <= 0)
                    return false;
                Monitor.Wait(_lock, (int)Math.Min(left, 50));
            }
            return true;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_stopping)
                return;
            _stopping = true;                   // 이후 Enqueue 는 무시한다. 남은 것은 워커가 비운다
            Monitor.PulseAll(_lock);
        }
        if (!_worker.Join(_shutdownTimeoutMs))
        {
            _cts.Cancel();
            _worker.Join(1000);
            LogManager.Error("[{0}] shutdown timed out. unsaved={1}", _name, Pending);
        }
        _cts.Dispose();
    }

    private void WorkerLoop()
    {
        List<KeyValuePair<TKey, TValue>> batch = new List<KeyValuePair<TKey, TValue>>();
        while (true)
        {
            batch.Clear();
            lock (_lock)
            {
                while (true)
                {
                    if (_queue.Count == 0)
                    {
                        _flushNow = false;
                        Monitor.PulseAll(_lock);                // Flush 대기자를 깨운다
                        if (_stopping)
                            return;
                        Monitor.Wait(_lock, _pollMs);           // 새 키는 Enqueue 가 깨우지 않는다(모아 쓰려고). 주기적으로 확인한다
                        continue;
                    }

                    long now = TimeUtil.MonotonicNowMillis();
                    bool force = _stopping || _flushNow;        // 종료나 Flush 요청이면 묵지 않았어도 바로 쓴다(단, 실패 뒤 백오프는 지킨다)
                    if (_backoff.ShouldSkip(now))
                    {
                        Monitor.Wait(_lock, (int)Math.Clamp(_backoff.NextRetryMs - now, 1, _pollMs));
                        continue;
                    }

                    int n = force ? _queue.DrainAll(batch, _batchSize) : _queue.Drain(batch, now, _flushIntervalMs, _batchSize);
                    if (n > 0)
                    {
                        foreach (KeyValuePair<TKey, TValue> kv in batch)
                            _inflight[kv.Key] = kv.Value;
                        break;
                    }
                    Monitor.Wait(_lock, _pollMs);
                }
            }

            long t0 = TimeUtil.MonotonicNowMillis();
            bool allOk = Save(batch);
            long ms = TimeUtil.MonotonicNowMillis() - t0;
            Interlocked.Exchange(ref _lastFlushMs, ms);
            if (ms > Interlocked.Read(ref _maxFlushMs))
                Interlocked.Exchange(ref _maxFlushMs, ms);
            if (allOk)
                _backoff.Reset();
            else
                _backoff.OnFail(TimeUtil.MonotonicNowMillis());
        }
    }

    /// <summary>한 묶음을 저장한다. 묶음이 실패하면 한 건씩 나눠 시도한다. 모두 성공하면 true.</summary>
    private bool Save(List<KeyValuePair<TKey, TValue>> batch)
    {
        try
        {
            _saveBatch(batch, _cts.Token);
            Complete(batch, 0, batch.Count, null);
            return true;
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref _failedBatches);
            LogManager.Warning("[{0}] batch save failed ({1} items): {2}", _name, batch.Count, e.Message);
        }

        bool allOk = true;
        for (int i = 0; i < batch.Count; ++i)
        {
            try
            {
                _saveOne(batch[i].Key, batch[i].Value, _cts.Token);
                Complete(batch, i, 1, null);
            }
            catch (Exception e)
            {
                allOk = false;
                Complete(batch, i, 1, e);
            }
        }
        return allOk;
    }

    /// <summary>저장이 끝난 항목을 저장 중 목록에서 뺀다. 성공이면 끝. 실패면 시도 횟수를 올리고, 한도를 넘으면 버리고 아니면 큐로 되돌린다
    /// (그사이 같은 키의 더 새 값이 들어왔으면 되돌린 값은 버려진다).</summary>
    private void Complete(List<KeyValuePair<TKey, TValue>> batch, int start, int count, Exception? error)
    {
        lock (_lock)
        {
            for (int i = start; i < start + count; ++i)
            {
                TKey key = batch[i].Key;
                _inflight.Remove(key);
                if (error == null)
                {
                    Interlocked.Increment(ref _saved);
                    _attempts.Remove(key);
                    continue;
                }

                _attempts.TryGetValue(key, out int tries);
                if (++tries >= _maxAttempts)
                {
                    _attempts.Remove(key);
                    Interlocked.Increment(ref _dropped);
                    LogManager.Error("[{0}] dropped after {1} failed attempts. key={2}: {3}", _name, tries, key, error.Message);
                }
                else
                {
                    _attempts[key] = tries;
                    _queue.Requeue(key, batch[i].Value, TimeUtil.MonotonicNowMillis());
                    LogManager.Warning("[{0}] save failed, will retry. key={1}, attempt={2}: {3}", _name, key, tries, error.Message);
                }
            }
            Monitor.PulseAll(_lock);
        }
    }
}
