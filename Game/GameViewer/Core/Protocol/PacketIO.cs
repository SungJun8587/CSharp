//***************************************************************************
// PacketIO.cs : 리틀 엔디언 패킹 패킷 읽기/쓰기 도우미 (#pragma pack(1) 구조체와 1:1)
//***************************************************************************
using System;

namespace GameViewer.Core.Protocol
{
    /// <summary>수신 버퍼에서 잘라낸 완전한 패킷 하나(헤더 포함 원문).</summary>
    public struct Frame
    {
        public ushort Type;
        public byte[] Data;
        public Frame(ushort type, byte[] data) { Type = type; Data = data; }
    }

    /// <summary>패킷 원문 읽기. 범위를 넘으면 PacketFormatException.</summary>
    public sealed class PacketReader
    {
        private readonly byte[] _b;
        private int _p;

        public PacketReader(byte[] data, int start)
        {
            _b = data; _p = start;
        }

        private void Need(int n)
        {
            if (_p + n > _b.Length)
                throw new PacketFormatException("packet too short");
        }

        public byte U8() { Need(1); return _b[_p++]; }
        public ushort U16() { Need(2); ushort v = (ushort)(_b[_p] | (_b[_p + 1] << 8)); _p += 2; return v; }
        public uint U32()
        {
            Need(4);
            uint v = (uint)(_b[_p] | (_b[_p + 1] << 8) | (_b[_p + 2] << 16) | (_b[_p + 3] << 24));
            _p += 4; return v;
        }
        public int I32() { return unchecked((int)U32()); }
        public ulong U64()
        {
            ulong lo = U32();
            ulong hi = U32();
            return lo | (hi << 32);
        }
        public float F32()
        {
            Need(4);
            float v = BitConverter.ToSingle(_b, _p);
            _p += 4; return v;
        }
    }

    /// <summary>패킷 원문 쓰기. 생성 시 헤더(size/type)를 채우고 Finish()에서 크기를 확정한다.</summary>
    public sealed class PacketWriter
    {
        private readonly byte[] _b;
        private int _p;

        public PacketWriter(ushort type, int totalSize)
        {
            _b = new byte[totalSize];
            U16((ushort)totalSize);
            U16(type);
        }

        public void U8(byte v) { _b[_p++] = v; }
        public void U16(ushort v) { _b[_p++] = (byte)v; _b[_p++] = (byte)(v >> 8); }
        public void U32(uint v)
        {
            _b[_p++] = (byte)v; _b[_p++] = (byte)(v >> 8); _b[_p++] = (byte)(v >> 16); _b[_p++] = (byte)(v >> 24);
        }
        public void U64(ulong v) { U32((uint)v); U32((uint)(v >> 32)); }
        public void F32(float v)
        {
            byte[] t = BitConverter.GetBytes(v);
            for (int i = 0; i < 4; ++i) _b[_p++] = t[i];
        }

        public byte[] Finish()
        {
            if (_p != _b.Length)
                throw new InvalidOperationException("packet size mismatch: wrote " + _p + " of " + _b.Length);
            return _b;
        }
    }

    public sealed class PacketFormatException : Exception
    {
        public PacketFormatException(string msg) : base(msg) { }
    }

    /// <summary>
    /// TCP 바이트 스트림을 패킷 단위로 자른다. 서버의 ProcessPacketFrames 와 같은 규칙:
    /// size &lt; 4 또는 size &gt; max 면 프로토콜 위반(예외), 덜 온 패킷은 다음 Push 를 기다린다.
    /// </summary>
    public sealed class PacketFramer
    {
        private byte[] _buf = new byte[8192];
        private int _len;
        private readonly int _maxPacket;

        public PacketFramer(int maxPacket) { _maxPacket = maxPacket; }

        public void Push(byte[] data, int offset, int count, System.Collections.Generic.List<Frame> output)
        {
            if (_len + count > _buf.Length)
            {
                int cap = _buf.Length;
                while (cap < _len + count) cap *= 2;
                Array.Resize(ref _buf, cap);
            }
            Buffer.BlockCopy(data, offset, _buf, _len, count);
            _len += count;

            int pos = 0;
            while (_len - pos >= ProtocolConst.HeaderSize)
            {
                int size = _buf[pos] | (_buf[pos + 1] << 8);
                if (size < ProtocolConst.HeaderSize || size > _maxPacket)
                    throw new PacketFormatException("invalid packet size " + size);
                if (_len - pos < size)
                    break;
                byte[] pkt = new byte[size];
                Buffer.BlockCopy(_buf, pos, pkt, 0, size);
                output.Add(new Frame((ushort)(_buf[pos + 2] | (_buf[pos + 3] << 8)), pkt));
                pos += size;
            }
            if (pos > 0)
            {
                Buffer.BlockCopy(_buf, pos, _buf, 0, _len - pos);
                _len -= pos;
            }
        }
    }
}
