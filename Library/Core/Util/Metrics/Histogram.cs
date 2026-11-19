// Histogram.cs : 값의 분포(경계 칸 + 분위수 근사).
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 값의 분포(틱 시간, 지연 등). 경계 배열(오름차순)로 칸을 나눠 누적 개수를 센다. 락이 없다.
/// 칸 i는 값 &lt;= bounds[i]이고 마지막 칸은 +Inf다. 분위수는 칸 경계로 근사한다(맨 위 칸에 든 값은 마지막 경계로 보고하므로 과소평가될 수 있다).
/// </summary>
public sealed class Histogram
{
    public static readonly double[] DefaultBounds = { 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 5000 };

    private readonly double[] _bounds;
    private readonly long[] _counts;
    private long _count;
    private long _sumBits = BitConverter.DoubleToInt64Bits(0.0);

    public Histogram(double[]? bounds = null)
    {
        _bounds = (double[])(bounds ?? DefaultBounds).Clone();     // 호출자가 나중에 배열을 바꿔도 영향이 없도록 복사
        for (int i = 0; i < _bounds.Length; ++i)
            if (double.IsNaN(_bounds[i]) || (i > 0 && _bounds[i] <= _bounds[i - 1]))
                throw new ArgumentException("경계는 NaN 없이 오름차순이어야 한다", nameof(bounds));
        _counts = new long[_bounds.Length + 1];
    }

    public IReadOnlyList<double> Bounds => _bounds;
    public long Count => Interlocked.Read(ref _count);
    public double Sum => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _sumBits));

    /// <summary>값을 기록한다. NaN은 무시한다(합계가 영구히 오염되므로). 무한대는 맨 위 칸에 세되 합계에는 더하지 않는다.</summary>
    public void Observe(double value)
    {
        if (double.IsNaN(value))
            return;
        int i = Array.BinarySearch(_bounds, value);
        if (i < 0) i = ~i;                  // 첫 번째로 value 이상인 경계
        Interlocked.Increment(ref _counts[i]);
        Interlocked.Increment(ref _count);
        if (double.IsInfinity(value))
            return;
        long old, nw;
        do
        {
            old = Interlocked.Read(ref _sumBits);
            nw = BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(old) + value);
        } while (Interlocked.CompareExchange(ref _sumBits, nw, old) != old);
    }

    /// <summary>칸별 개수 복사본(길이 = 경계 수 + 1).</summary>
    public long[] Snapshot()
    {
        long[] r = new long[_counts.Length];
        for (int i = 0; i < r.Length; ++i)
            r[i] = Interlocked.Read(ref _counts[i]);
        return r;
    }

    /// <summary>q(0~1) 분위수의 근사값(해당 칸의 위 경계). 기록이 없으면 0, 마지막 칸이면 마지막 경계.</summary>
    public double Quantile(double q)
    {
        long[] s = Snapshot();
        long total = 0;
        foreach (long c in s) total += c;
        if (total == 0 || _bounds.Length == 0) return 0;
        long target = (long)Math.Ceiling(Math.Clamp(q, 0, 1) * total);
        if (target < 1) target = 1;
        long run = 0;
        for (int i = 0; i < s.Length; ++i)
        {
            run += s[i];
            if (run >= target)
                return i < _bounds.Length ? _bounds[i] : _bounds[_bounds.Length - 1];
        }
        return _bounds[_bounds.Length - 1];
    }
}
