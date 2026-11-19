// Counter.cs : 단조 증가 카운터.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Core.Util;

/// <summary>단조 증가하는 값(처리한 패킷 수 등). 락 없이 여러 스레드에서 올린다.</summary>
public sealed class Counter
{
    private long _value;
    public long Value => Interlocked.Read(ref _value);
    /// <summary>n만큼 올린다. 카운터는 줄어들지 않으므로 음수는 예외(줄어드는 값은 <see cref="Gauge"/>).</summary>
    public void Inc(long n = 1)
    {
        if (n < 0)
            throw new ArgumentOutOfRangeException(nameof(n));
        Interlocked.Add(ref _value, n);
    }
}
