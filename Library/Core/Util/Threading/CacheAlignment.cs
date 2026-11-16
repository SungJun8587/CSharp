// CacheAlignment.cs : 캐시라인 정렬용 공용 상수와 패딩 원자 변수 (C++ CacheAlignment.h 대응).
using System.Runtime.InteropServices;
using System.Threading;

namespace Core.Util;

/// <summary>캐시라인 크기. .NET에는 하드웨어 값을 묻는 API가 없어 일반적인 64바이트로 고정한다.</summary>
public static class CacheLine
{
    public const int Size = 64;
}

/// <summary>
/// 서로 다른 원소를 서로 다른 스레드가 건드리는 배열(예: 커넥션 풀 슬롯별 참조 카운트)에서 인접 원소끼리 캐시라인을 공유하지 않게 하는 32비트 원자 변수.
/// 원소 하나가 캐시라인 두 개(128바이트)를 차지해 옆 라인 선읽기(adjacent-line prefetch)로 인한 간섭도 피한다. C++의 <c>CachePaddedAtomic&lt;int32&gt;</c> 대응.
/// 배열 원소의 메서드를 그대로 부른다: <c>counters[i].Increment()</c>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 2 * CacheLine.Size)]
public struct PaddedInt32
{
    [FieldOffset(0)] private int _value;

    public int Load() => Volatile.Read(ref _value);
    public void Store(int value) => Volatile.Write(ref _value, value);
    public int Exchange(int value) => Interlocked.Exchange(ref _value, value);
    /// <summary>현재 값이 expected이면 desired로 바꾼다. 바꾸기 전 값을 돌려준다(expected와 같으면 성공).</summary>
    public int CompareExchange(int expected, int desired) => Interlocked.CompareExchange(ref _value, desired, expected);
    public int Add(int delta) => Interlocked.Add(ref _value, delta);
    public int Increment() => Interlocked.Increment(ref _value);
    public int Decrement() => Interlocked.Decrement(ref _value);
}

/// <summary>64비트 버전. <see cref="PaddedInt32"/> 참고.</summary>
[StructLayout(LayoutKind.Explicit, Size = 2 * CacheLine.Size)]
public struct PaddedInt64
{
    [FieldOffset(0)] private long _value;

    public long Load() => Volatile.Read(ref _value);
    public void Store(long value) => Volatile.Write(ref _value, value);
    public long Exchange(long value) => Interlocked.Exchange(ref _value, value);
    public long CompareExchange(long expected, long desired) => Interlocked.CompareExchange(ref _value, desired, expected);
    public long Add(long delta) => Interlocked.Add(ref _value, delta);
    public long Increment() => Interlocked.Increment(ref _value);
    public long Decrement() => Interlocked.Decrement(ref _value);
}

/// <summary>
/// 힙 객체 하나가 캐시라인을 독점하는 64비트 커서(락프리 큐의 생산자/소비자 위치 등). 필드 위치를 명시해 CLR이 재배치하지 못하게 한다.
/// 명시적 레이아웃은 제네릭 타입 안에 둘 수 없어 별도 클래스로 둔다.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 3 * CacheLine.Size)]
internal sealed class PaddedCursor
{
    [FieldOffset(CacheLine.Size)] public long Value;
}
