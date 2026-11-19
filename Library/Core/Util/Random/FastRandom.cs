// FastRandom.cs : 빠른 난수(xoshiro256**), 셔플, 가중치 선택.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Core.Util;

/// <summary>
/// xoshiro256** 난수기. 암호용이 아니다(드랍, 스폰 위치, 데미지 변동 같은 게임 로직용). 인스턴스는 스레드 안전하지 않다 —
/// 스레드마다 하나씩 쓰거나(<see cref="Shared"/>) 존 스레드가 자기 것을 가진다. 같은 시드면 같은 수열이 나와 재현·테스트가 된다.
/// </summary>
public sealed class FastRandom
{
    [ThreadStatic] private static FastRandom? t_shared;

    /// <summary>현재 스레드 전용 인스턴스(처음 쓸 때 만들어진다).</summary>
    public static FastRandom Shared => t_shared ??= new FastRandom();

    private ulong _s0, _s1, _s2, _s3;

    public FastRandom() : this(NewSeed()) { }

    public FastRandom(ulong seed)
    {
        // splitmix64로 상태를 채운다(시드 0도 안전).
        _s0 = SplitMix(ref seed);
        _s1 = SplitMix(ref seed);
        _s2 = SplitMix(ref seed);
        _s3 = SplitMix(ref seed);
    }

    private static ulong NewSeed()
    {
        Span<byte> b = stackalloc byte[8];
        RandomNumberGenerator.Fill(b);
        return BitConverter.ToUInt64(b);
    }

    private static ulong SplitMix(ref ulong x)
    {
        ulong z = (x += 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextUInt64()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>0 이상 maxExclusive 미만(편향 없음).</summary>
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)NextBelow((uint)maxExclusive);
    }

    /// <summary>minInclusive 이상 maxExclusive 미만.</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(minInclusive + (long)NextBelow((uint)((long)maxExclusive - minInclusive)));
    }

    // Lemire의 곱셈 방식(나머지 연산의 편향을 거절 표본으로 없앤다).
    private uint NextBelow(uint range)
    {
        ulong m = (NextUInt64() >> 32) * range;
        uint low = (uint)m;
        if (low < range)
        {
            uint threshold = (uint)(-(int)range) % range;
            while (low < threshold)
            {
                m = (NextUInt64() >> 32) * range;
                low = (uint)m;
            }
        }
        return (uint)(m >> 32);
    }

    /// <summary>[0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public float NextFloat() => (float)((NextUInt64() >> 40) * (1.0 / (1UL << 24)));

    public double NextDouble(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>확률 p(0~1)로 true.</summary>
    public bool Chance(double p) => NextDouble() < p;

    /// <summary>가중치 배열에서 하나를 뽑아 인덱스를 돌려준다. 가중치가 0 이하인 항목은 뽑히지 않고, 전부 0 이하면 -1.</summary>
    public int PickWeighted(IReadOnlyList<double> weights)
    {
        double total = 0;
        for (int i = 0; i < weights.Count; ++i)
            if (weights[i] > 0) total += weights[i];
        if (total <= 0) return -1;

        double r = NextDouble() * total;
        int last = -1;
        for (int i = 0; i < weights.Count; ++i)
        {
            double w = weights[i];
            if (w <= 0) continue;
            last = i;
            if (r < w) return i;
            r -= w;
        }
        return last;    // 부동소수 오차 대비
    }

    /// <summary>Fisher–Yates 제자리 셔플.</summary>
    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; --i)
        {
            int j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public void Shuffle<T>(Span<T> span)
    {
        for (int i = span.Length - 1; i > 0; --i)
        {
            int j = Next(i + 1);
            (span[i], span[j]) = (span[j], span[i]);
        }
    }

    /// <summary>list에서 하나(비어 있으면 예외).</summary>
    public T Pick<T>(IReadOnlyList<T> list)
    {
        if (list.Count == 0)
            throw new InvalidOperationException("빈 목록");
        return list[Next(list.Count)];
    }
}
