// SyncValue.cs : 락으로 보호되는 값 하나 (C++ CSyncValue 대응).
namespace Core.Util;

/// <summary>
/// 락으로 보호되는 값 하나. 설정하는 스레드와 읽는 스레드가 다른 설정값용이다.
/// <see cref="Get"/>은 값의 복사본을 돌려주므로 호출한 쪽이 락 없이 쓸 수 있다(참조 타입이면 같은 객체를 가리키는 얕은 복사다).
/// 값이 없을 수 있으면 <c>SyncValue&lt;T?&gt;</c>로 표현한다. 값 하나를 읽고 쓰는 용도 전용이며, 여러 값을 원자적으로 함께 바꿔야 하면 별도의 락이 필요하다.
/// 구조체 값은 두 필드 이상이면 락 없이 읽을 때 찢어져 보일 수 있어(torn read) 락으로 보호한다.
/// </summary>
public sealed class SyncValue<T>
{
    private readonly object _lock = new object();
    private T _value;

    /// <summary>T의 기본값으로 시작한다.</summary>
    public SyncValue() => _value = default!;

    public SyncValue(T value) => _value = value;

    public void Set(T value)
    {
        lock (_lock)
            _value = value;
    }

    public T Get()
    {
        lock (_lock)
            return _value;
    }
}
