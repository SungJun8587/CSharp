// BitUtil.cs : 비트 연산과 비트 플래그 도우미.
using System;
using System.Numerics;

namespace Core.Util;

public static class BitUtil
{
    public static bool IsPowerOfTwo(ulong x) => x != 0 && (x & (x - 1)) == 0;

    /// <summary>x 이상인 가장 작은 2의 거듭제곱(x가 0이면 1). 2^31을 넘으면 예외.</summary>
    public static int NextPowerOfTwo(int x)
    {
        if (x <= 1) return 1;
        if (x > (1 << 30)) throw new ArgumentOutOfRangeException(nameof(x));
        return 1 << (32 - BitOperations.LeadingZeroCount((uint)(x - 1)));
    }

    public static int PopCount(ulong x) => BitOperations.PopCount(x);

    /// <summary>가장 낮은 1비트의 위치(없으면 -1).</summary>
    public static int LowestSetBit(ulong x) => x == 0 ? -1 : BitOperations.TrailingZeroCount(x);

    // C#의 시프트는 횟수를 63으로 가려 버리므로(1UL << 64 == 1) 범위 밖 비트가 조용히 다른 비트가 되지 않게 검사한다.
    private static void CheckBit(int bit)
    {
        if ((uint)bit > 63u)
            throw new ArgumentOutOfRangeException(nameof(bit), "0~63");
    }

    public static bool Test(ulong flags, int bit) { CheckBit(bit); return (flags >> bit & 1UL) != 0; }
    public static ulong Set(ulong flags, int bit) { CheckBit(bit); return flags | (1UL << bit); }
    public static ulong Clear(ulong flags, int bit) { CheckBit(bit); return flags & ~(1UL << bit); }
    public static ulong Toggle(ulong flags, int bit) { CheckBit(bit); return flags ^ (1UL << bit); }

    private static void CheckField(int offset, int width)
    {
        if (offset < 0 || width < 1 || width > 64 || offset + width > 64)
            throw new ArgumentOutOfRangeException(nameof(width), "[offset, offset+width)가 0~64 범위여야 한다");
    }

    /// <summary>value에서 [offset, offset+width) 비트를 꺼낸다(width 1~64).</summary>
    public static ulong GetBits(ulong value, int offset, int width)
    {
        CheckField(offset, width);
        ulong mask = width >= 64 ? ulong.MaxValue : (1UL << width) - 1;
        return (value >> offset) & mask;
    }

    /// <summary>value의 [offset, offset+width) 비트를 bits로 바꾼다(넘치는 상위 비트는 버린다).</summary>
    public static ulong SetBits(ulong value, int offset, int width, ulong bits)
    {
        CheckField(offset, width);
        ulong mask = width >= 64 ? ulong.MaxValue : (1UL << width) - 1;
        return (value & ~(mask << offset)) | ((bits & mask) << offset);
    }
}
