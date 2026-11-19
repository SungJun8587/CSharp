// Signal.cs : 한 종류의 이벤트를 위한 가벼운 시그널.
using System;

namespace Core.Util;

/// <summary>한 종류의 이벤트를 위한 가벼운 시그널(C# event와 비슷하지만 해제 핸들과 예외 격리가 있다). 스레드 안전하지 않다.</summary>
public sealed class Signal<T>
{
    private Action<T>[] _items = Array.Empty<Action<T>>();

    public int Count => _items.Length;

    public IDisposable Connect(Action<T> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        Action<T>[] next = new Action<T>[_items.Length + 1];
        Array.Copy(_items, next, _items.Length);
        next[next.Length - 1] = handler;
        _items = next;
        return new Disconnector(this, handler);
    }

    public void Emit(T value)
    {
        Action<T>[] items = _items;
        for (int i = 0; i < items.Length; ++i)
        {
            try { items[i](value); }
            catch (Exception ex) { LogThrottle.Error("signal.handler-failed", 5000, "[Signal] handler failed: {0}", ex); }
        }
    }

    private void Remove(Action<T> handler)
    {
        int i = Array.IndexOf(_items, handler);
        if (i < 0) return;
        Action<T>[] next = new Action<T>[_items.Length - 1];
        Array.Copy(_items, 0, next, 0, i);
        Array.Copy(_items, i + 1, next, i, next.Length - i);
        _items = next;
    }

    private sealed class Disconnector : IDisposable
    {
        private Signal<T>? _owner;
        private readonly Action<T> _handler;
        public Disconnector(Signal<T> owner, Action<T> handler) { _owner = owner; _handler = handler; }
        public void Dispose()
        {
            Signal<T>? o = _owner;
            _owner = null;
            o?.Remove(_handler);
        }
    }
}
