// StatModifierSet.cs : 능력치 기본값 + 보정(가산/비율/배수/고정) 계산.
using System;
using System.Collections.Generic;

namespace Core.Util;

public enum ModKind : byte
{
    /// <summary>기본값에 더한다(+10).</summary>
    Flat,
    /// <summary>비율을 모아서 한 번에 곱한다(0.1 = +10%, 여러 개는 합산).</summary>
    PercentAdd,
    /// <summary>독립 배수(1.5 = ×1.5, 여러 개는 곱한다).</summary>
    Multiply,
    /// <summary>결과를 이 값으로 고정한다(여러 개면 가장 나중에 더한 것).</summary>
    Override,
}

/// <summary>
/// 능력치(0..statCount-1)별 기본값과 보정 목록으로 최종값을 계산한다.
/// 결과 = clamp( (기본 + ΣFlat) × max(0, 1 + ΣPercentAdd) × ΠMultiply ), Override가 있으면 그 값을 clamp.
/// 보정은 출처(source, 장비/버프 ID)별로 한꺼번에 뺄 수 있고, 최종값은 바뀔 때만 다시 계산해 캐시한다.
/// DrainChanged로 "지난번 이후 값이 달라진 능력치"만 모아 클라이언트에 보낼 수 있다. 스레드 안전하지 않다.
/// </summary>
public sealed class StatModifierSet
{
    private struct Mod
    {
        public ModKind Kind;
        public double Value;
        public int Source;
    }

    private readonly double[] _base;
    private readonly List<Mod>?[] _mods;
    private readonly double[] _min;
    private readonly double[] _max;
    private readonly double[] _cache;
    private readonly bool[] _dirty;            // 캐시가 낡았음
    private readonly double[] _reported;       // DrainChanged로 마지막에 알린 값
    private readonly bool[] _pending;          // 알림 후보에 올라 있음
    private readonly List<int> _pendingList = new List<int>();

    public StatModifierSet(int statCount)
    {
        if (statCount < 1) throw new ArgumentOutOfRangeException(nameof(statCount));
        StatCount = statCount;
        _base = new double[statCount];
        _mods = new List<Mod>?[statCount];
        _min = new double[statCount];
        _max = new double[statCount];
        _cache = new double[statCount];
        _dirty = new bool[statCount];
        _reported = new double[statCount];
        _pending = new bool[statCount];
        Array.Fill(_min, double.NegativeInfinity);
        Array.Fill(_max, double.PositiveInfinity);
    }

    public int StatCount { get; }

    public double GetBase(int stat) { Check(stat); return _base[stat]; }

    public void SetBase(int stat, double value)
    {
        Check(stat);
        Finite(value);
        _base[stat] = value;
        Touch(stat);
    }

    public void SetClamp(int stat, double min, double max)
    {
        Check(stat);
        if (double.IsNaN(min) || double.IsNaN(max) || min > max) throw new ArgumentException("min/max가 올바르지 않다");
        _min[stat] = min;
        _max[stat] = max;
        Touch(stat);
    }

    public void Add(int stat, ModKind kind, double value, int source)
    {
        Check(stat);
        Finite(value);
        (_mods[stat] ??= new List<Mod>()).Add(new Mod { Kind = kind, Value = value, Source = source });
        Touch(stat);
    }

    /// <summary>한 출처의 보정을 모든 능력치에서 뺀다. 뺀 개수를 돌려준다.</summary>
    public int RemoveSource(int source)
    {
        int removed = 0;
        for (int s = 0; s < StatCount; ++s)
            removed += RemoveSource(s, source);
        return removed;
    }

    /// <summary>한 출처의 보정을 한 능력치에서만 뺀다.</summary>
    public int RemoveSource(int stat, int source)
    {
        Check(stat);
        List<Mod>? list = _mods[stat];
        if (list == null) return 0;
        int w = 0;                                  // 람다 없이 제자리 압축(호출마다 할당하지 않는다)
        for (int r = 0; r < list.Count; ++r)
            if (list[r].Source != source)
                list[w++] = list[r];
        int removed = list.Count - w;
        if (removed > 0)
        {
            list.RemoveRange(w, removed);
            Touch(stat);
        }
        return removed;
    }

    public int ModifierCount
    {
        get
        {
            int n = 0;
            foreach (List<Mod>? l in _mods) n += l?.Count ?? 0;
            return n;
        }
    }

    public double Get(int stat)
    {
        Check(stat);
        if (_dirty[stat])
        {
            _cache[stat] = Compute(stat);
            _dirty[stat] = false;
        }
        return _cache[stat];
    }

    /// <summary>지난번 DrainChanged 이후 최종값이 실제로 달라진 능력치 번호를 output에 추가한다. 추가한 개수를 돌려준다.</summary>
    public int DrainChanged(List<int> output)
    {
        int n = 0;
        foreach (int stat in _pendingList)
        {
            _pending[stat] = false;
            double v = Get(stat);
            if (v != _reported[stat])
            {
                _reported[stat] = v;
                output.Add(stat);
                ++n;
            }
        }
        _pendingList.Clear();
        return n;
    }

    private double Compute(int stat)
    {
        double flat = 0, pct = 0, mul = 1, over = 0;
        bool hasOver = false;
        List<Mod>? list = _mods[stat];
        if (list != null)
        {
            for (int i = 0; i < list.Count; ++i)
            {
                Mod m = list[i];
                switch (m.Kind)
                {
                    case ModKind.Flat: flat += m.Value; break;
                    case ModKind.PercentAdd: pct += m.Value; break;
                    case ModKind.Multiply: mul *= m.Value; break;
                    default: over = m.Value; hasOver = true; break;
                }
            }
        }
        double v = hasOver ? over : (_base[stat] + flat) * Math.Max(0.0, 1.0 + pct) * mul;
        if (double.IsNaN(v)) v = 0;
        return Math.Clamp(v, _min[stat], _max[stat]);
    }

    private void Touch(int stat)
    {
        _dirty[stat] = true;
        if (!_pending[stat])
        {
            _pending[stat] = true;
            _pendingList.Add(stat);
        }
    }

    private void Check(int stat)
    {
        if ((uint)stat >= (uint)StatCount) throw new ArgumentOutOfRangeException(nameof(stat));
    }

    private static void Finite(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentOutOfRangeException(nameof(v), "유한한 값이어야 한다");
    }
}
