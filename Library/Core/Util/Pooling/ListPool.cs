// ListPool.cs : List<T> 풀(틱/패킷 처리용 임시 리스트).
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>
/// 틱이나 패킷 처리처럼 매번 임시 List가 필요한 곳에서 할당을 없앤다. 돌려줄 때 비워지고, 너무 커진 리스트는 풀에 남기지 않는다.
/// </summary>
public static class ListPool<T>
{
    /// <summary>이보다 용량이 커진 리스트는 풀에 돌려주지 않고 버린다(큰 배열이 풀에 눌러앉는 것을 막는다).</summary>
    public const int MaxRetainedCapacity = 4096;

    private static readonly ObjectPool<List<T>> s_pool = new ObjectPool<List<T>>(() => new List<T>(16), 32);

    public static List<T> Rent() => s_pool.Rent();

    public static void Return(List<T> list)
    {
        if (list == null || list.Capacity > MaxRetainedCapacity)
            return;
        list.Clear();
        s_pool.Return(list);
    }

    /// <summary>using으로 쓰는 임대. <c>using var lease = ListPool&lt;int&gt;.Lease(); lease.Value.Add(1);</c></summary>
    public static PooledList<T> Lease() => new PooledList<T>(Rent());
}

public readonly struct PooledList<T> : IDisposable
{
    public List<T> Value { get; }
    internal PooledList(List<T> value) => Value = value;
    public void Dispose() => ListPool<T>.Return(Value);
}
