// SendBuffer.cs : implementation of the SendBuffer, SendBufferChunk and SendBufferManager classes.
using System;

namespace Core.Net;

/// <summary>
/// 송신용 버퍼 조각. SendBufferManager.Open() 으로 얻어 내용을 채운 뒤 Close(writeSize) 로 확정한다.
/// Close() 이후에는 불변이므로 여러 세션에 같은 인스턴스를 Send/Broadcast 해도 안전하다.
/// 수명은 GC가 관리하므로 전송이 끝날 때까지 청크 배열이 유지된다.
/// </summary>
public sealed class SendBuffer
{
    private readonly SendBufferChunk? _owner;
    private readonly byte[] _array;
    private readonly int _offset;
    private readonly int _allocSize;
    private int _writeSize;
    private bool _closed;

    internal SendBuffer(SendBufferChunk? owner, byte[] array, int offset, int allocSize)
    {
        _owner = owner;
        _array = array;
        _offset = offset;
        _allocSize = allocSize;
    }

    /// <summary>Open() 시 예약한 크기.</summary>
    public int AllocSize => _allocSize;

    /// <summary>Close() 로 확정된 실제 데이터 크기.</summary>
    public int WriteSize => _writeSize;

    /// <summary>내용을 채울 쓰기 영역(예약 크기 전체). Close() 이전에만 사용한다.</summary>
    public Span<byte> Buffer => new Span<byte>(_array, _offset, _allocSize);

    /// <summary>확정된 데이터 구간.</summary>
    public ReadOnlySpan<byte> Data => new ReadOnlySpan<byte>(_array, _offset, _writeSize);

    /// <summary>소켓 송신에 넘길 구간.</summary>
    internal ArraySegment<byte> Segment => new ArraySegment<byte>(_array, _offset, _writeSize);

    /// <summary>
    /// 실제로 쓴 크기를 확정한다. 예약보다 적게 썼다면 남은 영역은 청크에 반환된다.
    /// </summary>
    /// <returns>메서드 체이닝용 자기 자신.</returns>
    public SendBuffer Close(int writeSize)
    {
        if (_closed)
            throw new InvalidOperationException("이미 Close 된 SendBuffer 입니다.");
        if (writeSize < 0 || writeSize > _allocSize)
            throw new ArgumentOutOfRangeException(nameof(writeSize));

        _closed = true;
        _writeSize = writeSize;
        _owner?.Close(this, writeSize);
        return this;
    }
}

/// <summary>
/// 하나의 큰 byte[] 에서 SendBuffer 를 순차 할당하는 청크. 스레드 로컬로만 사용되므로 락이 없다.
/// 청크가 가득 차면 버려지고(GC), 아직 전송 중인 SendBuffer 가 배열을 참조하는 동안은 유지된다.
/// </summary>
internal sealed class SendBufferChunk
{
    private readonly byte[] _array;
    private int _usedSize;
    private SendBuffer? _openBuffer;

    public SendBufferChunk(int chunkSize)
    {
        _array = new byte[chunkSize];
    }

    public bool IsOpen => _openBuffer != null;

    public int FreeSize => _array.Length - _usedSize;

    public SendBuffer Open(int allocSize)
    {
        if (allocSize > FreeSize)
            throw new InvalidOperationException("청크 여유 공간이 부족합니다.");

        _openBuffer = new SendBuffer(this, _array, _usedSize, allocSize);
        return _openBuffer;
    }

    public void Close(SendBuffer buffer, int writeSize)
    {
        if (!ReferenceEquals(_openBuffer, buffer))
            return;

        _usedSize += writeSize;
        _openBuffer = null;
    }
}

/// <summary>
/// 스레드 로컬 청크에서 SendBuffer 를 발급한다.
/// Open() 과 Close() 는 같은 스레드에서, 다른 Open() 이 끼어들지 않는 짧은 구간 안에서 짝으로 호출해야 한다.
/// </summary>
public static class SendBufferManager
{
    [ThreadStatic]
    private static SendBufferChunk? t_chunk;

    /// <summary>
    /// allocSize 만큼의 쓰기 영역을 가진 SendBuffer 를 연다.
    /// 청크 크기보다 큰 요청은 전용 배열로 발급된다.
    /// </summary>
    public static SendBuffer Open(int allocSize)
    {
        if (allocSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(allocSize));

        if (allocSize > NetConstants.SendChunkSize)
            return new SendBuffer(null, new byte[allocSize], 0, allocSize);

        SendBufferChunk? chunk = t_chunk;
        if (chunk == null || chunk.IsOpen || chunk.FreeSize < allocSize)
        {
            chunk = new SendBufferChunk(NetConstants.SendChunkSize);
            t_chunk = chunk;
        }

        return chunk.Open(allocSize);
    }

    /// <summary>data 를 복사해 곧바로 전송 가능한 SendBuffer 를 만든다.</summary>
    public static SendBuffer Create(ReadOnlySpan<byte> data)
    {
        SendBuffer buffer = Open(data.Length);
        data.CopyTo(buffer.Buffer);
        return buffer.Close(data.Length);
    }
}
