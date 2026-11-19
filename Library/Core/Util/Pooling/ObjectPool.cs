// ObjectPool.cs : 고정 크기 오브젝트 풀.
using System;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 자주 만들고 버리는 객체(버퍼, 스크래치 컨테이너 등)를 다시 쓰는 풀. 락 없이 슬롯 배열을 CAS로 오간다(여러 스레드에서 써도 된다).
/// <para>가져간 객체는 <see cref="Return"/>으로 돌려준다. 풀이 가득 찼으면 돌려준 객체는 그냥 버려져 GC가 거둔다(풀이 무한정 커지지 않는다).</para>
/// <para><b>주의</b>: 같은 객체를 두 번 돌려주는 것은 막지 못한다(두 곳이 같은 객체를 쓰게 된다). 돌려준 뒤에는 참조를 버린다.</para>
/// </summary>
public sealed class ObjectPool<T> where T : class
{
    private readonly Func<T> _factory;
    private readonly Action<T>? _reset;
    private readonly T?[] _items;
    private T? _fast;           // 같은 스레드가 곧바로 되가져가는 흔한 경우를 위한 첫 슬롯

    /// <param name="factory">풀이 비었을 때 새로 만드는 방법</param>
    /// <param name="maxRetained">풀이 보관하는 최대 개수(1 이상)</param>
    /// <param name="reset">돌려줄 때 객체를 비우는 동작(없으면 그대로 보관)</param>
    public ObjectPool(Func<T> factory, int maxRetained = 64, Action<T>? reset = null)
    {
        if (maxRetained < 1)
            throw new ArgumentOutOfRangeException(nameof(maxRetained));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _reset = reset;
        _items = new T?[maxRetained - 1];
    }

    public T Rent()
    {
        T? x = _fast;
        if (x != null && ReferenceEquals(Interlocked.CompareExchange(ref _fast, null, x), x))
            return x;

        T?[] items = _items;
        for (int i = 0; i < items.Length; ++i)
        {
            x = items[i];
            if (x != null && ReferenceEquals(Interlocked.CompareExchange(ref items[i], null, x), x))
                return x;
        }
        return _factory();
    }

    public void Return(T obj)
    {
        if (obj == null)
            return;
        _reset?.Invoke(obj);

        if (_fast == null && Interlocked.CompareExchange(ref _fast, obj, null) == null)
            return;
        T?[] items = _items;
        for (int i = 0; i < items.Length; ++i)
            if (items[i] == null && Interlocked.CompareExchange(ref items[i], obj, null) == null)
                return;
        // 가득 찼다: 버린다.
    }

    /// <summary>using으로 쓰면 범위를 벗어날 때 자동으로 돌려준다.</summary>
    public PooledObject<T> Lease() => new PooledObject<T>(this, Rent());
}

/// <summary><see cref="ObjectPool{T}.Lease"/>가 돌려주는 임대 표지. Dispose에서 풀에 돌려준다.</summary>
public readonly struct PooledObject<T> : IDisposable where T : class
{
    private readonly ObjectPool<T>? _pool;
    public T Value { get; }

    internal PooledObject(ObjectPool<T> pool, T value)
    {
        _pool = pool;
        Value = value;
    }

    public void Dispose() => _pool?.Return(Value);
}
