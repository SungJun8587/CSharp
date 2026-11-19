// AtomicEnum.cs : 열거형 값의 원자적 교환/비교 교환.
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 열거형 필드에 대한 <see cref="Interlocked"/>. 기반 형식이 4바이트(int/uint)나 8바이트(long/ulong)인 열거형만 된다.
/// byte/short 기반 열거형(예: <c>enum X : byte</c>)은 .NET 8에 해당 원자 연산이 없어 <see cref="NotSupportedException"/>이다. 그런 열거형은 <see cref="AtomicEnum{TEnum}"/>을 쓴다.
/// </summary>
public static class InterlockedEx
{
    private const string UnsupportedMessage = "기반 형식이 4바이트 또는 8바이트인 열거형만 쓸 수 있다. byte/short 열거형은 AtomicEnum<T>를 쓴다.";

    /// <summary>location이 comparand와 같으면 value로 바꾸고, 바꾸기 전 값을 돌려준다.</summary>
    public static TEnum CompareExchange<TEnum>(ref TEnum location, TEnum value, TEnum comparand) where TEnum : struct, Enum
    {
        switch (Unsafe.SizeOf<TEnum>())
        {
            case 4:
            {
                int ret = Interlocked.CompareExchange(ref Unsafe.As<TEnum, int>(ref location), Unsafe.As<TEnum, int>(ref value), Unsafe.As<TEnum, int>(ref comparand));
                return Unsafe.As<int, TEnum>(ref ret);
            }
            case 8:
            {
                long ret = Interlocked.CompareExchange(ref Unsafe.As<TEnum, long>(ref location), Unsafe.As<TEnum, long>(ref value), Unsafe.As<TEnum, long>(ref comparand));
                return Unsafe.As<long, TEnum>(ref ret);
            }
            default:
                throw new NotSupportedException(UnsupportedMessage);
        }
    }

    /// <summary>location을 value로 바꾸고, 바꾸기 전 값을 돌려준다.</summary>
    public static TEnum Exchange<TEnum>(ref TEnum location, TEnum value) where TEnum : struct, Enum
    {
        switch (Unsafe.SizeOf<TEnum>())
        {
            case 4:
            {
                int ret = Interlocked.Exchange(ref Unsafe.As<TEnum, int>(ref location), Unsafe.As<TEnum, int>(ref value));
                return Unsafe.As<int, TEnum>(ref ret);
            }
            case 8:
            {
                long ret = Interlocked.Exchange(ref Unsafe.As<TEnum, long>(ref location), Unsafe.As<TEnum, long>(ref value));
                return Unsafe.As<long, TEnum>(ref ret);
            }
            default:
                throw new NotSupportedException(UnsupportedMessage);
        }
    }
}

/// <summary>
/// 스레드 안전한 열거형 상태 값. 세션/서버 상태처럼 여러 스레드가 보고 바꾸는 열거형에 쓴다. 기반 형식(byte/short/int/long, 부호 유무)에 관계없이 동작한다.
/// 상태 전이는 <see cref="TryTransition"/>으로 "기대한 상태일 때만" 바꾸면 한 스레드만 성공한다.
/// </summary>
public sealed class AtomicEnum<TEnum> where TEnum : struct, Enum
{
    private long _bits;     // 열거형의 비트를 0으로 확장해 보관한다(부호 있는 형식도 되돌릴 때 같은 비트가 나온다).

    public AtomicEnum(TEnum initial = default) { _bits = ToBits(initial); }

    /// <summary>현재 값(획득 의미의 읽기).</summary>
    public TEnum Value => FromBits(Volatile.Read(ref _bits));

    /// <summary>값을 바꾼다(전체 장벽).</summary>
    public void Set(TEnum value) => Interlocked.Exchange(ref _bits, ToBits(value));

    /// <summary>값을 바꾸고 이전 값을 돌려준다.</summary>
    public TEnum Exchange(TEnum value) => FromBits(Interlocked.Exchange(ref _bits, ToBits(value)));

    /// <summary>현재 값이 comparand와 같으면 value로 바꾸고, 바꾸기 전 값을 돌려준다.</summary>
    public TEnum CompareExchange(TEnum value, TEnum comparand)
        => FromBits(Interlocked.CompareExchange(ref _bits, ToBits(value), ToBits(comparand)));

    /// <summary>현재 값이 from일 때만 to로 바꾼다. 여러 스레드가 같은 전이를 시도하면 하나만 true를 얻는다.</summary>
    public bool TryTransition(TEnum from, TEnum to)
    {
        long f = ToBits(from);
        return Interlocked.CompareExchange(ref _bits, ToBits(to), f) == f;
    }

    public override string ToString() => Value.ToString();

    private static long ToBits(TEnum v)
    {
        switch (Unsafe.SizeOf<TEnum>())
        {
            case 1: return Unsafe.As<TEnum, byte>(ref v);
            case 2: return Unsafe.As<TEnum, ushort>(ref v);
            case 4: return Unsafe.As<TEnum, uint>(ref v);
            default: return Unsafe.As<TEnum, long>(ref v);
        }
    }

    private static TEnum FromBits(long bits)
    {
        switch (Unsafe.SizeOf<TEnum>())
        {
            case 1: { byte b = (byte)bits; return Unsafe.As<byte, TEnum>(ref b); }
            case 2: { ushort s = (ushort)bits; return Unsafe.As<ushort, TEnum>(ref s); }
            case 4: { uint i = (uint)bits; return Unsafe.As<uint, TEnum>(ref i); }
            default: return Unsafe.As<long, TEnum>(ref bits);
        }
    }
}
