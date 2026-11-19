// StringBuilderPool.cs : StringBuilder 풀.
using System.Collections.Generic;
using System.Text;

namespace Core.Util;

/// <summary>문자열을 이어 붙일 때 쓰는 StringBuilder 풀.</summary>
public static class StringBuilderPool
{
    private const int MaxRetainedCapacity = 16 * 1024;
    private static readonly ObjectPool<StringBuilder> s_pool = new ObjectPool<StringBuilder>(() => new StringBuilder(256), 16);

    public static StringBuilder Rent() => s_pool.Rent();

    public static void Return(StringBuilder sb)
    {
        if (sb == null || sb.Capacity > MaxRetainedCapacity)
            return;
        sb.Clear();
        s_pool.Return(sb);
    }

    /// <summary>내용을 문자열로 만들고 빌더를 돌려준다.</summary>
    public static string ToStringAndReturn(StringBuilder sb)
    {
        string s = sb.ToString();
        Return(sb);
        return s;
    }
}
