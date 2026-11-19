// EventBus.cs : 타입 기반 이벤트 버스.
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>
/// 이벤트 타입으로 구독하는 동기 이벤트 버스. 발행하면 구독자가 같은 스레드에서 구독 순서대로 즉시 실행된다.
/// 스레드 안전하지 않다 — 존처럼 한 스레드가 쓰는 곳에 하나씩 둔다. 발행 중에 구독/해제해도 안전하다(그 발행에는 반영되지 않는다).
/// 한 구독자가 예외를 던져도 나머지는 실행되고, 예외는 <see cref="OnError"/>로 알린다.
/// </summary>
public sealed class EventBus
{
    private interface IHandlers { }

    private sealed class Handlers<T> : IHandlers
    {
        public Action<T>[] Items = Array.Empty<Action<T>>();
    }

    private readonly Dictionary<Type, IHandlers> _map = new Dictionary<Type, IHandlers>();

    /// <summary>구독자가 예외를 던졌을 때 불린다(없으면 로그).</summary>
    public Action<Exception>? OnError;

    /// <summary>구독한다. 돌려주는 <see cref="IDisposable"/>을 Dispose하면 해제된다.</summary>
    public IDisposable Subscribe<T>(Action<T> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        if (!_map.TryGetValue(typeof(T), out IHandlers? h))
            _map[typeof(T)] = h = new Handlers<T>();
        var hs = (Handlers<T>)h;
        // 불변 배열을 새로 만든다 → 발행 중 구독 변경이 순회를 깨지 않는다.
        Action<T>[] next = new Action<T>[hs.Items.Length + 1];
        Array.Copy(hs.Items, next, hs.Items.Length);
        next[next.Length - 1] = handler;
        hs.Items = next;
        return new Subscription(() => Unsubscribe(hs, handler));
    }

    private static void Unsubscribe<T>(Handlers<T> hs, Action<T> handler)
    {
        int i = Array.IndexOf(hs.Items, handler);
        if (i < 0) return;
        Action<T>[] next = new Action<T>[hs.Items.Length - 1];
        Array.Copy(hs.Items, 0, next, 0, i);
        Array.Copy(hs.Items, i + 1, next, i, next.Length - i);
        hs.Items = next;
    }

    public void Publish<T>(T ev)
    {
        if (!_map.TryGetValue(typeof(T), out IHandlers? h))
            return;
        Action<T>[] items = ((Handlers<T>)h).Items;
        for (int i = 0; i < items.Length; ++i)
        {
            try
            {
                items[i](ev);
            }
            catch (Exception ex)
            {
                if (OnError != null) OnError(ex);
                else LogThrottle.Error("eventbus.handler-failed", 5000, "[EventBus] handler failed: {0}", ex);
            }
        }
    }

    public int SubscriberCount<T>() => _map.TryGetValue(typeof(T), out IHandlers? h) ? ((Handlers<T>)h).Items.Length : 0;

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;
        public Subscription(Action dispose) => _dispose = dispose;
        public void Dispose()
        {
            Action? d = _dispose;
            _dispose = null;
            d?.Invoke();
        }
    }
}
