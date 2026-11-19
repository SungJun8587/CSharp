// LogThrottle.cs : 같은 로그를 일정 간격에 한 번만 남기는 제한기.
using System.Collections.Concurrent;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 악의적인 클라이언트나 장애 상황에서 같은 로그가 초당 수만 줄 쏟아지는 것을 막는다.
/// 키(로그의 종류를 가리키는 짧은 문자열)마다 intervalMs에 한 번만 통과시키고, 그 사이에 막힌 횟수를 다음 통과 때 알려 준다.
/// <code>LogThrottle.Warning("zone-full", 5000, "zone full. zoneId={0}", id);   // 5초에 한 줄, 막힌 횟수 덧붙임</code>
/// <para><b>키는 정적인 문자열이어야 한다</b>(세션 ID 같은 값을 넣지 않는다). 키가 <see cref="MaxKeys"/>개를 넘으면 새 키는 공용 키 하나로 합쳐진다.
/// 인자가 1~2개인 오버로드는 <c>params object[]</c> 할당과 boxing을 로그가 실제로 남을 때까지 미룬다(폭주 중 억제되는 호출이 할당하지 않는다).</para>
/// </summary>
public static class LogThrottle
{
    public const int MaxKeys = 1024;

    /// <summary>지금 추적 중인 키 수(진단용).</summary>
    public static int KeyCount => s_entries.Count;
    private const string OverflowKey = "\0overflow";

    private sealed class Entry
    {
        // long.MinValue: 만들어졌지만 아직 한 번도 통과하지 않음. 정리(Trim)가 이 상태의 항목을 지우면 같은 키에서 두 번 통과할 수 있다.
        public long NextMs = long.MinValue;
        public long Suppressed;
    }

    private static readonly ConcurrentDictionary<string, Entry> s_entries = new ConcurrentDictionary<string, Entry>();
    private static long s_nextTrimMs;

    /// <summary>지금 남겨도 되면 true. 남기면 이전에 막힌 횟수를 suppressed로 준다.</summary>
    public static bool TryAcquire(string key, int intervalMs, out long suppressed) => TryAcquire(key, intervalMs, TimeUtil.MonotonicNowMillis(), out suppressed);

    public static bool TryAcquire(string key, int intervalMs, long nowMs, out long suppressed)
    {
        suppressed = 0;
        if (!s_entries.TryGetValue(key, out Entry? e))
        {
            if (s_entries.Count >= MaxKeys)
            {
                Trim(nowMs);
                if (s_entries.Count >= MaxKeys)
                    key = OverflowKey;      // 동적 키가 쏟아져도 항목 수가 늘지 않는다
            }
            e = s_entries.GetOrAdd(key, _ => new Entry());
        }

        lock (e)
        {
            if (nowMs < e.NextMs)
            {
                ++e.Suppressed;
                return false;
            }
            suppressed = e.Suppressed;
            e.Suppressed = 0;
            e.NextMs = nowMs + intervalMs;
            return true;
        }
    }

    /// <summary>모든 키 상태를 지운다(다음 호출은 모두 새 키로 본다). 시험이나 설정 재적용 때 쓴다.</summary>
    public static void Clear()
    {
        s_entries.Clear();
        Interlocked.Exchange(ref s_nextTrimMs, 0);
    }

    // 정리는 최대 1초에 한 번만 한다(키가 가득 찼을 때 새 키마다 전체를 훑지 않도록).
    private static void Trim(long nowMs)
    {
        long next = Interlocked.Read(ref s_nextTrimMs);
        if (nowMs < next || Interlocked.CompareExchange(ref s_nextTrimMs, nowMs + 1000, next) != next)
            return;
        foreach (System.Collections.Generic.KeyValuePair<string, Entry> kv in s_entries)
            if (kv.Value.NextMs != long.MinValue && nowMs >= kv.Value.NextMs && kv.Value.Suppressed == 0)
                s_entries.TryRemove(kv.Key, out _);
    }

    private static bool Gate(string key, int intervalMs, out string suffix)
    {
        suffix = "";
        if (!TryAcquire(key, intervalMs, out long suppressed))
            return false;
        if (suppressed != 0)
            suffix = " (+" + suppressed + " suppressed)";
        return true;
    }

    private static void Emit(LogType type, string text, object?[] args)
    {
        switch (type)
        {
            case LogType.Info: LogManager.Info(text, args); break;
            case LogType.Warning: LogManager.Warning(text, args); break;
            default: LogManager.Error(text, args); break;
        }
    }

    public static void Info(string key, int intervalMs, string format, params object?[] args) { if (Gate(key, intervalMs, out string s)) Emit(LogType.Info, format + s, args); }
    public static void Warning(string key, int intervalMs, string format, params object?[] args) { if (Gate(key, intervalMs, out string s)) Emit(LogType.Warning, format + s, args); }
    public static void Error(string key, int intervalMs, string format, params object?[] args) { if (Gate(key, intervalMs, out string s)) Emit(LogType.Error, format + s, args); }

    public static void Info<T0>(string key, int intervalMs, string format, T0 a0) { if (Gate(key, intervalMs, out string s)) Emit(LogType.Info, format + s, new object?[] { a0 }); }
    public static void Warning<T0>(string key, int intervalMs, string format, T0 a0) { if (Gate(key, intervalMs, out string s)) Emit(LogType.Warning, format + s, new object?[] { a0 }); }
    public static void Error<T0>(string key, int intervalMs, string format, T0 a0) { if (Gate(key, intervalMs, out string s)) Emit(LogType.Error, format + s, new object?[] { a0 }); }

    public static void Info<T0, T1>(string key, int intervalMs, string format, T0 a0, T1 a1) { if (Gate(key, intervalMs, out string s)) Emit(LogType.Info, format + s, new object?[] { a0, a1 }); }
    public static void Warning<T0, T1>(string key, int intervalMs, string format, T0 a0, T1 a1) { if (Gate(key, intervalMs, out string s)) Emit(LogType.Warning, format + s, new object?[] { a0, a1 }); }
    public static void Error<T0, T1>(string key, int intervalMs, string format, T0 a0, T1 a1) { if (Gate(key, intervalMs, out string s)) Emit(LogType.Error, format + s, new object?[] { a0, a1 }); }
}
