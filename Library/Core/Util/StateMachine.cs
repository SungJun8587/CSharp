// StateMachine.cs : 열거형 상태 기반 상태 기계 도우미.
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>
/// 상태(enum)마다 Enter/Update/Exit 동작과 허용된 전이를 등록하는 상태 기계. 몬스터 AI, 듀얼 단계, 세션 단계 같은 곳에 쓴다.
/// 전이 표를 등록하면 허용되지 않은 전이는 거절된다(<see cref="TryChange"/>가 false). 전이 표를 쓰지 않으면 모든 전이를 허용한다.
/// <para>Enter/Exit 안에서 다시 <see cref="TryChange"/>를 불러도 된다(순서대로 처리된다). 스레드 안전하지 않다.</para>
/// </summary>
public sealed class StateMachine<TState> where TState : struct, Enum
{
    private sealed class Handlers
    {
        public Action<TState>? Enter;       // 인자: 이전 상태
        public Action<TState>? Exit;        // 인자: 다음 상태
        public Action<long>? Update;        // 인자: 현재 시각(ms)
    }

    private readonly Dictionary<TState, Handlers> _handlers = new Dictionary<TState, Handlers>();
    private readonly Dictionary<TState, HashSet<TState>> _allowed = new Dictionary<TState, HashSet<TState>>();
    private bool _useTable;
    private bool _changing;
    private readonly Queue<TState> _pending = new Queue<TState>();

    public TState Current { get; private set; }
    public TState Previous { get; private set; }

    /// <summary>현재 상태에 들어온 시각(ms). <see cref="Update"/>/<see cref="TryChange"/>에 넘긴 시각 기준.</summary>
    public long EnteredAtMs { get; private set; }

    /// <summary>상태가 바뀔 때마다(Exit 뒤, Enter 앞) 불린다. (이전, 다음)</summary>
    public event Action<TState, TState>? Changed;

    public StateMachine(TState initial, long nowMs = 0)
    {
        Current = initial;
        Previous = initial;
        EnteredAtMs = nowMs;
    }

    private Handlers Get(TState s)
    {
        if (!_handlers.TryGetValue(s, out Handlers? h))
            _handlers[s] = h = new Handlers();
        return h;
    }

    public StateMachine<TState> OnEnter(TState s, Action<TState> a) { Get(s).Enter = a; return this; }
    public StateMachine<TState> OnExit(TState s, Action<TState> a) { Get(s).Exit = a; return this; }
    public StateMachine<TState> OnUpdate(TState s, Action<long> a) { Get(s).Update = a; return this; }

    /// <summary>from에서 to로의 전이를 허용한다. 하나라도 등록하면 등록되지 않은 전이는 모두 거절된다.</summary>
    public StateMachine<TState> Allow(TState from, params TState[] to)
    {
        _useTable = true;
        if (!_allowed.TryGetValue(from, out HashSet<TState>? set))
            _allowed[from] = set = new HashSet<TState>();
        foreach (TState t in to) set.Add(t);
        return this;
    }

    public bool CanChange(TState to)
    {
        if (!_useTable) return true;
        return _allowed.TryGetValue(Current, out HashSet<TState>? set) && set.Contains(to);
    }

    /// <summary>전이를 시도한다. 같은 상태로의 전이는 아무 일도 하지 않고 false. 허용되지 않으면 false.</summary>
    public bool TryChange(TState to, long nowMs = 0)
    {
        if (EqualityComparer<TState>.Default.Equals(to, Current))
            return false;
        if (!CanChange(to))
            return false;

        if (_changing)
        {
            _pending.Enqueue(to);       // Enter/Exit 안에서 부른 전이는 현재 전이가 끝난 뒤 처리
            return true;
        }

        _changing = true;
        try
        {
            Apply(to, nowMs);
            while (_pending.Count > 0)
            {
                TState next = _pending.Dequeue();
                if (!EqualityComparer<TState>.Default.Equals(next, Current) && CanChange(next))
                    Apply(next, nowMs);
            }
        }
        finally
        {
            _changing = false;
            _pending.Clear();
        }
        return true;
    }

    private void Apply(TState to, long nowMs)
    {
        TState from = Current;
        if (_handlers.TryGetValue(from, out Handlers? hf))
            hf.Exit?.Invoke(to);
        Previous = from;
        Current = to;
        EnteredAtMs = nowMs;
        Changed?.Invoke(from, to);
        if (_handlers.TryGetValue(to, out Handlers? ht))
            ht.Enter?.Invoke(from);
    }

    /// <summary>현재 상태의 Update를 부른다.</summary>
    public void Update(long nowMs)
    {
        if (_handlers.TryGetValue(Current, out Handlers? h))
            h.Update?.Invoke(nowMs);
    }

    /// <summary>현재 상태에 머문 시간(ms).</summary>
    public long TimeInState(long nowMs) => nowMs - EnteredAtMs;
}
