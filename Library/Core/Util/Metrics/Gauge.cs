// Gauge.cs : 오르내리는 현재 값.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Core.Util;

/// <summary>오르내리는 현재 값(접속자 수, 큐 길이 등).</summary>
public sealed class Gauge
{
    private long _bits = BitConverter.DoubleToInt64Bits(0.0);
    public double Value => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _bits));
    public void Set(double v) => Interlocked.Exchange(ref _bits, BitConverter.DoubleToInt64Bits(v));

    public void Add(double delta)
    {
        long old, nw;
        do
        {
            old = Interlocked.Read(ref _bits);
            nw = BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(old) + delta);
        } while (Interlocked.CompareExchange(ref _bits, nw, old) != old);
    }
}
