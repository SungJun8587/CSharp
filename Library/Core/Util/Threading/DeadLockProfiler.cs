// DeadLockProfiler.cs : 락 이름 기반 획득 순서 그래프로 데드락 가능성을 실제로 막히기 전에 찾아내는 진단 도구.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Core.Util;

/// <summary>위반을 어떻게 알릴지.</summary>
public enum DeadLockMode
{
    /// <summary>로그를 남기고 <see cref="LockOrderException"/>을 던진다(기본). 락을 얻기 전에 던지므로 호출부는 락을 쥐지 않은 채 빠져나온다.</summary>
    Throw,
    /// <summary>로그만 남기고 계속 진행한다.</summary>
    Log,
    /// <summary>로그를 남기고 프로세스를 즉시 종료한다(<see cref="Environment.FailFast(string)"/>).</summary>
    FailFast,
}

/// <summary>락 획득 순서가 이미 기록된 순서와 모순될 때 던진다.</summary>
public sealed class LockOrderException : InvalidOperationException
{
    public LockOrderException(string message) : base(message) { }
}

/// <summary>
/// 락 획득 순서의 모순(= 데드락 가능성)을 탐지한다. 스레드가 락 A를 쥔 채 락 B를 얻으면 "A → B" 간선을 그래프에 기록하고,
/// 새 간선이 사이클을 만들면(B에서 A로 가는 경로가 이미 있으면) 그 즉시 알린다. 두 스레드가 실제로 서로를 기다리기 전,
/// 순서가 어긋난 호출이 한 번만 지나가도 잡히므로 재현이 어려운 데드락을 개발 중에 드러낼 수 있다.
///
/// <para><b>이름 기준</b>: 락은 인스턴스가 아니라 <i>이름</i>으로 구분한다. 같은 용도의 락 여러 개(세션마다 하나씩 등)는 같은 이름을 쓰면
/// 하나의 노드로 묶인다. 같은 이름이 중첩되는 것은 순서 관계가 아니므로 판정하지 않는다(<see cref="StrictSelfLock"/>로 켤 수 있다).</para>
/// <para><b>호출 위치</b>: <see cref="PushLock"/>은 락을 <i>얻기 전에</i> 부른다. 얻은 뒤에 부르면 이미 막힌 다음이라 알릴 수 없다.
/// <see cref="PopLock"/>은 락을 <i>푼 뒤에</i> 부른다. 대기 없이 성공 여부만 보는 try 계열은 <see cref="PushTryLock"/>을 성공 뒤에 부른다
/// (기다리지 않으므로 사이클 판정은 하지 않고 스택에만 올린다).</para>
/// <para><b>비용</b>: 이미 기록된 간선은 락 없이 해시 조회 한 번으로 끝난다. 새 간선일 때만 전역 락 안에서 경로를 찾는다. 락 클래스들의 호출은
/// <c>USE_DEADLOCK_PROFILER</c> 심볼이 정의된 빌드(Core.Util 기본값: Debug)에서만 코드에 들어가며, 릴리스에서는 완전히 사라진다.</para>
/// <para>이 클래스 자신은 프로파일 대상 락을 쓰지 않는다. 기록 후 <see cref="Enabled"/>를 바꾸면 짝이 어긋나 오탐이 날 수 있으므로 시작 시 한 번만 정한다.</para>
/// </summary>
public static class DeadLockProfiler
{
    private static readonly object s_gate = new object();                     // 그래프 변경과 경로 탐색
    private static readonly ConcurrentDictionary<string, int> s_ids = new ConcurrentDictionary<string, int>();
    private static readonly List<string> s_names = new List<string>();        // id → 이름 (s_gate 안)
    private static readonly List<HashSet<int>> s_adj = new List<HashSet<int>>();   // id → 다음 락 (s_gate 안)
    private static readonly ConcurrentDictionary<long, byte> s_edges = new ConcurrentDictionary<long, byte>();   // 기록된 간선 (락 없는 빠른 확인)

    [ThreadStatic] private static List<int>? t_held;     // 이 스레드가 쥔 락 id (획득 순)

    private static volatile bool s_enabled = true;
    private static volatile bool s_strictSelf;
    private static volatile bool s_strictUnlock = true;
    private static volatile DeadLockMode s_mode = DeadLockMode.Throw;
    private static volatile Action<string>? s_handler;

    /// <summary>락 클래스의 호출이 이 빌드에 들어 있는지(<c>USE_DEADLOCK_PROFILER</c> 정의 여부).</summary>
    public static bool HooksCompiledIn => LockProfile.Compiled;

    /// <summary>끄면 모든 호출이 즉시 반환한다. 시작 시 한 번만 정한다.</summary>
    public static bool Enabled { get => s_enabled; set => s_enabled = value; }

    public static DeadLockMode Mode { get => s_mode; set => s_mode = value; }

    /// <summary>같은 이름이 이미 쥔 채로 다시 오면(재진입 불가 락의 자기 대기) 위반으로 본다. 같은 이름의 서로 다른 인스턴스를 중첩해 쥐는 정상 사용도 걸리므로 기본은 꺼 둔다.</summary>
    public static bool StrictSelfLock { get => s_strictSelf; set => s_strictSelf = value; }

    /// <summary>
    /// 켜면(기본) 해제 순서가 획득 순서(LIFO)와 다를 때 위반으로 본다(C++의 <c>INVALID_UNLOCK</c>). 끄면 다른 순서의 해제를 허용한다.
    /// 쥐고 있는 락이 없는데 푸는 경우(<c>MULTIPLE_UNLOCK</c>)는 항상 위반이다.
    /// </summary>
    public static bool StrictUnlockOrder { get => s_strictUnlock; set => s_strictUnlock = value; }

    /// <summary>위반 메시지를 받는 추가 콜백(예: 화면 표시, 테스트 수집). <see cref="Mode"/>의 동작에 더해 호출된다.</summary>
    public static Action<string>? ViolationHandler { get => s_handler; set => s_handler = value; }

    // ---- 스레드별 스택 ----

    /// <summary>락을 얻기 <i>전에</i> 부른다. 순서가 기록과 모순되면 알리고(기본: 예외), 아니면 이 스레드의 보유 스택에 올린다.</summary>
    /// <param name="name">null이면 무시한다(이름을 붙이지 않은 락)</param>
    /// <param name="allowRecursive">재진입 가능한 락이면 true. <see cref="StrictSelfLock"/> 판정에서 제외한다.</param>
    public static void PushLock(string? name, bool allowRecursive = false)
    {
        if (name == null || !s_enabled)
            return;

        int id = IdOf(name);
        List<int> held = t_held ??= new List<int>(8);

        if (held.Count > 0)
        {
            int prev = held[held.Count - 1];
            if (prev != id)
                CheckEdge(prev, id, held);
            else if (s_strictSelf && !allowRecursive)
                Report($"재진입 불가 락 '{name}'을 이미 쥔 채 다시 얻으려 합니다 (자기 대기). 스레드 '{ThreadName()}', 보유: {FormatHeld(held)}");
        }

        held.Add(id);
    }

    /// <summary>기다리지 않는 획득(try 계열)이 <i>성공한 뒤</i> 부른다. 사이클 판정 없이 보유 스택에만 올린다.</summary>
    public static void PushTryLock(string? name)
    {
        if (name == null || !s_enabled)
            return;

        (t_held ??= new List<int>(8)).Add(IdOf(name));
    }

    /// <summary>
    /// 락을 <i>푼 뒤에</i> 부른다. 가장 최근에 쥔 락이어야 한다(<see cref="StrictUnlockOrder"/>가 꺼져 있으면 아무 순서나 허용).
    /// 어긋나면 스택을 바로잡은 뒤(이후 오탐이 이어지지 않게) 알린다.
    /// </summary>
    public static void PopLock(string? name)
    {
        if (name == null || !s_enabled)
            return;

        List<int>? held = t_held;
        if (held == null || held.Count == 0)
        {
            Report($"MULTIPLE_UNLOCK: 쥐고 있는 락이 없는데 '{name}'을 풀었습니다 (획득/해제 짝 불일치). 스레드 '{ThreadName()}'");
            return;
        }

        int id = s_ids.TryGetValue(name, out int found) ? found : -1;
        int top = held.Count - 1;
        if (id >= 0 && held[top] == id)
        {
            held.RemoveAt(top);
            return;
        }

        int idx = id >= 0 ? held.LastIndexOf(id) : -1;
        string detail = idx >= 0
            ? $"INVALID_UNLOCK: '{name}'을 풀었지만 가장 최근에 쥔 락은 '{NameOf(held[top])}'입니다 (해제 순서가 획득 순서와 다름). 스레드 '{ThreadName()}', 보유: {FormatHeld(held)}"
            : $"INVALID_UNLOCK: 쥐지 않은 락 '{name}'을 풀었습니다. 스레드 '{ThreadName()}', 보유: {FormatHeld(held)}";

        if (idx >= 0)
            held.RemoveAt(idx);

        if (idx < 0 || s_strictUnlock)
            Report(detail);
    }

    /// <summary>이 스레드가 지금 쥔 락 이름(획득 순). 진단용.</summary>
    public static string[] HeldLocks()
    {
        List<int>? held = t_held;
        if (held == null || held.Count == 0)
            return Array.Empty<string>();

        var result = new string[held.Count];
        lock (s_gate)
        {
            for (int i = 0; i < held.Count; ++i)
                result[i] = s_names[held[i]];
        }
        return result;
    }

    /// <summary>기록된 순서 관계("A → B" 목록)를 사람이 읽을 형태로 돌려준다. 진단용.</summary>
    public static string DumpGraph()
    {
        var sb = new StringBuilder();
        lock (s_gate)
        {
            for (int from = 0; from < s_adj.Count; ++from)
            {
                foreach (int to in s_adj[from])
                    sb.Append(s_names[from]).Append(" -> ").Append(s_names[to]).Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>기록된 간선을 모두 지운다(이름과 스레드별 보유 스택은 그대로). 테스트나 단계 전환용.</summary>
    public static void ResetGraph()
    {
        lock (s_gate)
        {
            foreach (HashSet<int> set in s_adj)
                set.Clear();
            s_edges.Clear();
        }
    }

    // ---- 내부 ----

    private static int IdOf(string name)
    {
        if (s_ids.TryGetValue(name, out int id))
            return id;

        lock (s_gate)
        {
            if (s_ids.TryGetValue(name, out id))
                return id;

            id = s_names.Count;
            s_names.Add(name);
            s_adj.Add(new HashSet<int>());
            s_ids[name] = id;       // 목록을 채운 뒤 공개한다
            return id;
        }
    }

    // from을 쥔 채 to를 얻는다. 이미 기록된 간선이면 끝, 새 간선이면 to에서 from으로 가는 경로가 있는지 보고 있으면 사이클이다.
    private static void CheckEdge(int from, int to, List<int> held)
    {
        long key = ((long)from << 32) | (uint)to;
        if (s_edges.ContainsKey(key))
            return;

        string? violation = null;
        lock (s_gate)
        {
            if (s_edges.ContainsKey(key))
                return;

            // 검사와 추가를 한 락 안에서 해야 서로 반대 순서의 두 간선이 동시에 통과하지 못한다.
            List<int>? path = FindPath(to, from);
            if (path != null)
            {
                var sb = new StringBuilder();
                sb.Append("락 순서 위반 (데드락 가능성): 스레드 '").Append(ThreadName()).Append("'이(가) '")
                  .Append(s_names[from]).Append("'을 쥔 채 '").Append(s_names[to]).Append("'을 얻으려 하지만, 반대 순서가 이미 기록되어 있습니다. 사이클: ")
                  .Append(s_names[from]);
                foreach (int id in path)
                    sb.Append(" -> ").Append(s_names[id]);
                sb.Append(" (현재 보유: ").Append(FormatHeldLocked(held)).Append(')');
                violation = sb.ToString();      // 사이클을 만드는 간선은 기록하지 않는다 → 같은 위반이 반복되면 매번 잡힌다
            }
            else
            {
                s_adj[from].Add(to);
                s_edges[key] = 1;
            }
        }

        if (violation != null)
            Report(violation);
    }

    // start에서 target까지의 경로(start 포함, target 포함). 없으면 null. s_gate 안에서 부른다.
    private static List<int>? FindPath(int start, int target)
    {
        var parent = new Dictionary<int, int> { [start] = -1 };
        var stack = new Stack<int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            if (cur == target)
            {
                var path = new List<int>();
                for (int n = cur; n != -1; n = parent[n])
                    path.Add(n);
                path.Reverse();
                return path;
            }

            foreach (int next in s_adj[cur])
            {
                if (parent.TryAdd(next, cur))
                    stack.Push(next);
            }
        }

        return null;
    }

    private static string NameOf(int id)
    {
        lock (s_gate)
            return s_names[id];
    }

    private static string ThreadName()
    {
        Thread t = Thread.CurrentThread;
        return t.Name ?? ("#" + t.ManagedThreadId);
    }

    private static string FormatHeld(List<int>? held)
    {
        lock (s_gate)
            return FormatHeldLocked(held);
    }

    private static string FormatHeldLocked(List<int>? held)
    {
        if (held == null || held.Count == 0)
            return "(없음)";

        var sb = new StringBuilder();
        for (int i = 0; i < held.Count; ++i)
        {
            if (i > 0) sb.Append(" > ");
            sb.Append(s_names[held[i]]);
        }
        return sb.ToString();
    }

    private static void Report(string message)
    {
        try { LogManager.Error("{0}", message); } catch { /* 로거 실패가 진단을 가리면 안 된다 */ }

        Action<string>? handler = s_handler;
        if (handler != null)
        {
            try { handler(message); } catch { }
        }

        switch (s_mode)
        {
            case DeadLockMode.Throw:
                throw new LockOrderException(message);
            case DeadLockMode.FailFast:
                Environment.FailFast(message);
                break;
        }
    }
}

/// <summary>
/// 락 클래스가 부르는 얇은 진입점. <c>[Conditional]</c>이라 <c>USE_DEADLOCK_PROFILER</c>가 정의되지 않은 빌드에서는 호출 자체가 컴파일에서 빠진다
/// (인자 계산도 하지 않는다). 심볼은 이 어셈블리의 정의를 따른다.
/// </summary>
internal static class LockProfile
{
#if USE_DEADLOCK_PROFILER
    public const bool Compiled = true;
#else
    public const bool Compiled = false;
#endif

    [Conditional("USE_DEADLOCK_PROFILER")]
    public static void Before(string? name, bool recursive = false) => DeadLockProfiler.PushLock(name, recursive);

    [Conditional("USE_DEADLOCK_PROFILER")]
    public static void Acquired(string? name) => DeadLockProfiler.PushTryLock(name);

    [Conditional("USE_DEADLOCK_PROFILER")]
    public static void Released(string? name) => DeadLockProfiler.PopLock(name);
}
