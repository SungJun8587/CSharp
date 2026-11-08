// RecvBuffer.cs : implementation of the RecvBuffer class.
using System;
using System.Buffers;

namespace Core.Net;

/// <summary>
/// 세션 단위 수신 버퍼. 단위 크기 * BufferCount 용량의 선형 버퍼를 사용하며,
/// 링 방식 대신 Clean() 에서 남은 데이터를 앞으로 당기는 시프트 방식으로 연속 영역을 유지한다.
/// 배열은 ArrayPool 에서 필요할 때 대여하고(LOH 상시 할당 방지), 데이터가 없을 때 반납할 수 있다.
/// 세션당 수신이 직렬로만 일어나므로 스레드 안전하지 않다.
/// </summary>
public sealed class RecvBuffer
{
    private byte[]? _buffer;
    private readonly int _bufferSize;
    private int _readPos;
    private int _writePos;

    /// <param name="bufferSize">단위 버퍼 크기. 용량은 ComputeCapacity(bufferSize) 참고.</param>
    public RecvBuffer(int bufferSize = NetConstants.BufferSizeDefault)
    {
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize));

        _bufferSize = bufferSize;
        Capacity = ComputeCapacity(bufferSize);
    }

    private const int MaxCapacity = 1 << 30;

    /// <summary>
    /// 단위 크기로부터 수신 버퍼 용량을 계산한다: (bufferSize * RecvBufferCount) 를 2의 거듭제곱으로 올림.
    /// ArrayPool 이 2의 거듭제곱 크기 버킷으로 대여하므로, 같은 크기로 맞춰 대여한 배열을 낭비 없이 쓴다.
    /// </summary>
    public static int ComputeCapacity(int bufferSize)
    {
        long wanted = (long)bufferSize * NetConstants.RecvBufferCount;
        if (bufferSize <= 0 || wanted > MaxCapacity)
            throw new ArgumentOutOfRangeException(nameof(bufferSize));

        return (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)wanted);
    }

    /// <summary>전체 용량(논리 크기). 풀에서 대여한 실제 배열은 이보다 클 수 있다.</summary>
    public int Capacity { get; }

    /// <summary>아직 처리하지 않은 수신 데이터 크기.</summary>
    public int DataSize => _writePos - _readPos;

    /// <summary>뒤쪽에 더 쓸 수 있는 여유 크기.</summary>
    public int FreeSize => Capacity - _writePos;

    /// <summary>처리 대기 중인 데이터 구간.</summary>
    public ReadOnlySpan<byte> ReadSpan
        => _buffer == null ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(_buffer, _readPos, DataSize);

    /// <summary>소켓 수신에 넘길 내부 배열. EnsureRented() 이후에만 유효.</summary>
    internal byte[] Array => _buffer!;

    /// <summary>소켓 수신이 채워 넣을 시작 위치.</summary>
    internal int WritePos => _writePos;

    /// <summary>배열이 없으면 풀에서 대여한다.</summary>
    internal void EnsureRented()
    {
        if (_buffer == null)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(Capacity);
            _readPos = _writePos = 0;
        }
    }

    /// <summary>처리할 데이터가 남아 있지 않으면 배열을 풀에 반납한다.</summary>
    internal void ReleaseIfEmpty()
    {
        if (_buffer != null && DataSize == 0)
            Release();
    }

    /// <summary>배열을 무조건 풀에 반납하고 커서를 초기화한다. 진행 중인 수신이 없을 때만 호출해야 한다.</summary>
    internal void Release()
    {
        if (_buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }

        _readPos = _writePos = 0;
    }

    /// <summary>numOfBytes 만큼 처리 완료로 표시한다. 데이터보다 크면 false.</summary>
    public bool OnRead(int numOfBytes)
    {
        if (numOfBytes < 0 || numOfBytes > DataSize)
            return false;

        _readPos += numOfBytes;
        return true;
    }

    /// <summary>numOfBytes 만큼 수신되어 채워졌음을 표시한다. 여유보다 크면 false.</summary>
    public bool OnWrite(int numOfBytes)
    {
        if (numOfBytes < 0 || numOfBytes > FreeSize)
            return false;

        _writePos += numOfBytes;
        return true;
    }

    /// <summary>
    /// 커서를 정리한다. 데이터가 없으면 커서만 0으로 리셋하고,
    /// 여유 공간이 단위 크기보다 작아졌을 때만 남은 데이터를 앞으로 복사한다.
    /// </summary>
    public void Clean()
    {
        if (_buffer == null)
            return;

        int dataSize = DataSize;

        if (dataSize == 0)
        {
            _readPos = _writePos = 0;
        }
        else if (FreeSize < _bufferSize)
        {
            Buffer.BlockCopy(_buffer, _readPos, _buffer, 0, dataSize);
            _readPos = 0;
            _writePos = dataSize;
        }
    }
}
