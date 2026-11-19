// FlagSet64.cs : 64개까지의 플래그를 한 값에 담는 불변 구조체.
using System;
using System.Numerics;

namespace Core.Util;

/// <summary>
/// 0~63번 플래그를 64비트 하나에 담는 값 형식. 상태 이상/권한/능력 표시처럼 작고 자주 복사되는 집합에 쓴다.
/// 불변이라 그대로 비교/복사/패킷 전송(Bits)할 수 있다.
/// </summary>
public readonly struct FlagSet64 : IEquatable<FlagSet64>
{
    public readonly ulong Bits;

    public FlagSet64(ulong bits) { Bits = bits; }

    public static readonly FlagSet64 Empty = default;

    public static FlagSet64 Of(params int[] flags)
    {
        ulong b = 0;
        foreach (int f in flags) b |= Mask(f);
        return new FlagSet64(b);
    }

    public bool IsEmpty => Bits == 0;
    public int Count => BitOperations.PopCount(Bits);

    public bool Has(int flag) => (Bits & Mask(flag)) != 0;
    public bool HasAny(FlagSet64 o) => (Bits & o.Bits) != 0;
    public bool HasAll(FlagSet64 o) => (Bits & o.Bits) == o.Bits;

    public FlagSet64 With(int flag) => new FlagSet64(Bits | Mask(flag));
    public FlagSet64 Without(int flag) => new FlagSet64(Bits & ~Mask(flag));
    public FlagSet64 Toggle(int flag) => new FlagSet64(Bits ^ Mask(flag));

    /// <summary>from 이상에서 처음 켜진 플래그 번호. 없으면 -1.</summary>
    public int NextSet(int from)
    {
        if (from < 0) from = 0;
        if (from > 63) return -1;
        ulong w = Bits & (ulong.MaxValue << from);
        return w == 0 ? -1 : BitOperations.TrailingZeroCount(w);
    }

    public static FlagSet64 operator |(FlagSet64 a, FlagSet64 b) => new FlagSet64(a.Bits | b.Bits);
    public static FlagSet64 operator &(FlagSet64 a, FlagSet64 b) => new FlagSet64(a.Bits & b.Bits);
    public static FlagSet64 operator ^(FlagSet64 a, FlagSet64 b) => new FlagSet64(a.Bits ^ b.Bits);
    public static FlagSet64 operator ~(FlagSet64 a) => new FlagSet64(~a.Bits);
    public static bool operator ==(FlagSet64 a, FlagSet64 b) => a.Bits == b.Bits;
    public static bool operator !=(FlagSet64 a, FlagSet64 b) => a.Bits != b.Bits;

    public bool Equals(FlagSet64 other) => Bits == other.Bits;
    public override bool Equals(object? obj) => obj is FlagSet64 f && f.Bits == Bits;
    public override int GetHashCode() => Bits.GetHashCode();
    public override string ToString() => "0x" + Bits.ToString("x16");

    private static ulong Mask(int flag)
    {
        if ((uint)flag > 63) throw new ArgumentOutOfRangeException(nameof(flag), "플래그 번호는 0~63");
        return 1UL << flag;
    }
}
