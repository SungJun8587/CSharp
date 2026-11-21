// DbMetrics.cs : DB 작업 소요 시간/오류/재시도 계측과 느린 쿼리 기록. MetricRegistry 에 다음 이름으로 올린다(머리말 db 는 바꿀 수 있다).
//   db_ops_total, db_errors_total, db_retries_total, db_slow_total, db_circuit_rejected_total, db_pool_exhausted_total (Counter),
//   db_op_ms (Histogram, 밀리초, 모든 작업), db_op_ms_<작업이름> (작업별 Histogram, 작업 이름의 영문/숫자 외 글자는 _)
// 느린 작업(기본 200ms 이상)은 작업 이름과 함께 경고 로그를 남긴다. SQL 본문은 남기지 않는다(개인정보/크기).
using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using Core.Util;

namespace Core.Data;

public sealed class DbMetrics
{
    private static readonly double[] Bounds = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 };
    private const int MaxOpHistograms = 64;     // 작업 이름이 무한히 늘어 지표가 폭증하지 않게 한다(넘으면 전체 히스토그램에만 기록)

    private readonly MetricRegistry _registry;
    private readonly string _prefix;
    private readonly string _tag;
    private readonly Counter _ops, _errors, _retries, _slow, _rejected, _poolExhausted;
    private readonly Histogram _ms;
    private readonly ConcurrentDictionary<string, Histogram> _perOp = new ConcurrentDictionary<string, Histogram>(StringComparer.Ordinal);
    private readonly Action<string>? _slowSink;
    private int _perOpCount;

    /// <summary>이 값(ms) 이상이면 느린 작업으로 센다. 0 이하면 끈다.</summary>
    public double SlowMs { get; }

    /// <param name="slowSink">느린 작업 알림을 받을 곳. null 이면 LogManager 경고로 남긴다(시험에서 바꿔 끼운다).</param>
    /// <param name="prefix">지표 이름 머리말. 데이터베이스가 여럿이면 각자 다른 값(예: "db_account")을 줘서 수치가 합쳐지지 않게 한다. 영문/숫자/밑줄.</param>
    /// <param name="logTag">로그 머리말(대괄호 안). 기본 "MySqlDb".</param>
    public DbMetrics(MetricRegistry? registry = null, double slowMs = 200, Action<string>? slowSink = null, string prefix = "db", string logTag = "MySqlDb")
    {
        _registry = registry ?? MetricRegistry.Default;
        _prefix = prefix;
        _tag = logTag;
        _ops = _registry.Counter(prefix + "_ops_total", "DB operations (per attempt)");
        _errors = _registry.Counter(prefix + "_errors_total", "DB operation attempts that threw");
        _retries = _registry.Counter(prefix + "_retries_total", "DB operations retried after a transient error");
        _slow = _registry.Counter(prefix + "_slow_total", "DB operations at or above the slow threshold");
        _rejected = _registry.Counter(prefix + "_circuit_rejected_total", "DB operations rejected without trying because the circuit was open");
        _poolExhausted = _registry.Counter(prefix + "_pool_exhausted_total", "DB operations that timed out waiting for a pooled connection");
        _ms = _registry.Histogram(prefix + "_op_ms", "DB operation duration in milliseconds", Bounds);
        SlowMs = slowMs;
        _slowSink = slowSink;
    }

    public long Ops => _ops.Value;
    public long Errors => _errors.Value;
    public long Retries => _retries.Value;
    public long Slow => _slow.Value;
    public long Rejected => _rejected.Value;
    public long PoolExhausted => _poolExhausted.Value;

    public void Retry() => _retries.Inc();
    public void Reject() => _rejected.Inc();
    public void PoolTimeout() => _poolExhausted.Inc();

    private static string Sanitize(string op)
    {
        StringBuilder sb = new StringBuilder(op.Length);
        foreach (char c in op)
            sb.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ? c : '_');
        return sb.ToString();
    }

    private Histogram? ForOp(string op)
    {
        if (_perOp.TryGetValue(op, out Histogram? h))
            return h;
        if (op.Length == 0 || Volatile.Read(ref _perOpCount) >= MaxOpHistograms)
            return null;        // 상한을 넘은 뒤에는 락을 잡는 Count 없이 바로 돌아간다
        string name = _prefix + "_op_ms_" + Sanitize(op);
        return _perOp.GetOrAdd(op, _ =>
        {
            Interlocked.Increment(ref _perOpCount);
            return _registry.Histogram(name, "DB operation duration in milliseconds: " + op, Bounds);
        });
    }

    /// <summary>한 번의 시도를 기록한다(성공/실패 모두).</summary>
    public void Record(string op, double elapsedMs, bool failed)
    {
        _ops.Inc();
        _ms.Observe(elapsedMs);
        ForOp(op)?.Observe(elapsedMs);
        if (failed)
            _errors.Inc();
        if (SlowMs > 0 && elapsedMs >= SlowMs)
        {
            _slow.Inc();
            string msg = string.Format("[{0}] slow op={1} {2:0.#}ms{3}", _tag, op, elapsedMs, failed ? " (failed)" : "");
            if (_slowSink != null) _slowSink(msg);
            else LogManager.Warning("{0}", msg);
        }
    }
}
