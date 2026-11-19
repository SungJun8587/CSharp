// BitReader.cs : 비트 단위 읽기.
using System;

namespace Core.Util;

/// <summary>비트 단위 읽기. <see cref="BitWriter"/>와 같은 순서. 범위를 넘으면 false를 돌려주고 예외를 던지지 않는다.</summary>
public ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _buf;
    private int _bitPos;

    public BitReader(ReadOnlySpan<byte> buffer)
    {
        _buf = buffer;
        _bitPos = 0;
    }

    public int BitsLeft => _buf.Length * 8 - _bitPos;

    public bool Read(int bits, out ulong value)
    {
        value = 0;
        if (bits < 1 || bits > 64 || bits > BitsLeft)
            return false;

        int got = 0;
        while (got < bits)
        {
            int idx = _bitPos >> 3, off = _bitPos & 7;
            int take = Math.Min(8 - off, bits - got);
            ulong chunk = (ulong)((_buf[idx] >> off) & ((1 << take) - 1));
            value |= chunk << got;
            _bitPos += take;
            got += take;
        }
        return true;
    }

    public bool ReadBool(out bool value)
    {
        bool ok = Read(1, out ulong v);
        value = v != 0;
        return ok;
    }
}
