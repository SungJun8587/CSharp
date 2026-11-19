// WeightedTable.cs : 가중치 고정 표(alias 방식 O(1) 뽑기).
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>
/// 가중치가 고정된 목록에서 자주 뽑을 때 쓰는 표(Vose alias 방식: 준비 O(n), 뽑기 O(1)). 드랍 테이블, 스폰 테이블용.
/// </summary>
public sealed class WeightedTable
{
    private readonly double[] _prob;
    private readonly int[] _alias;

    public int Count => _prob.Length;

    /// <param name="weights">0 이상. 합이 0보다 커야 한다</param>
    public WeightedTable(IReadOnlyList<double> weights)
    {
        int n = weights.Count;
        if (n == 0)
            throw new ArgumentException("빈 가중치", nameof(weights));
        double sum = 0;
        for (int i = 0; i < n; ++i)
        {
            if (weights[i] < 0 || !double.IsFinite(weights[i]))
                throw new ArgumentException("가중치는 0 이상의 유한한 값이어야 한다", nameof(weights));
            sum += weights[i];
        }
        if (sum <= 0)
            throw new ArgumentException("가중치 합이 0이다", nameof(weights));

        _prob = new double[n];
        _alias = new int[n];
        double[] scaled = new double[n];
        var small = new Stack<int>();
        var large = new Stack<int>();
        for (int i = 0; i < n; ++i)
        {
            scaled[i] = weights[i] * n / sum;
            (scaled[i] < 1.0 ? small : large).Push(i);
        }
        while (small.Count > 0 && large.Count > 0)
        {
            int s = small.Pop();
            int l = large.Pop();
            _prob[s] = scaled[s];
            _alias[s] = l;
            scaled[l] = scaled[l] + scaled[s] - 1.0;
            (scaled[l] < 1.0 ? small : large).Push(l);
        }
        while (large.Count > 0) _prob[large.Pop()] = 1.0;
        while (small.Count > 0) _prob[small.Pop()] = 1.0;
    }

    public int Pick(FastRandom rng)
    {
        int i = rng.Next(_prob.Length);
        return rng.NextDouble() < _prob[i] ? i : _alias[i];
    }
}
