// BitWriter.cs : 비트 단위 쓰기.
using System;

namespace Core.Util;

/// <summary>
/// 비트 단위 쓰기(패킷에서 좌표나 작은 값을 촘촘히 담을 때). 낮은 비트부터 채운다. 생성할 때 버퍼를 0으로 지운다.
/// 바이트 경계 단위로 처리하므로 비트를 하나씩 돌지 않는다. 공간이 모자라면 아무것도 쓰지 않고 false를 돌려준다.
/// </summary>
public ref struct BitWriter
{
    private readonly Span<byte> _buf;
    private int _bitPos;

    public BitWriter(Span<byte> buffer)
    {
        _buf = buffer;
        _bitPos = 0;
        buffer.Clear();
    }

    public int BitLength => _bitPos;
    public int ByteLength => (_bitPos + 7) >> 3;

    /// <summary>value의 아래쪽 bits(1~64)비트를 쓴다. 공간이 모자라면 false.</summary>
    public bool Write(ulong value, int bits)
    {
        if (bits < 1 || bits > 64 || bits > _buf.Length * 8 - _bitPos)
            return false;

        if (bits < 64)
            value &= (1UL << bits) - 1;
        int remaining = bits;
        while (remaining > 0)
        {
            int idx = _bitPos >> 3, off = _bitPos & 7;
            int take = Math.Min(8 - off, remaining);
            _buf[idx] |= (byte)((value & ((1UL << take) - 1)) << off);
            value >>= take;
            _bitPos += take;
            remaining -= take;
        }
        return true;
    }

    public bool WriteBool(bool v) => Write(v ? 1UL : 0UL, 1);
}
