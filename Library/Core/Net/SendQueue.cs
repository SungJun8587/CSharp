// SendQueue.cs : 세션 송신 큐 공통부(Session 과 TlsSession 이 공유).
using System;
using System.Collections.Generic;
using System.Threading;

namespace Core.Net;

/// <summary>
/// 세션 하나의 송신 큐 상태: 대기 큐, 송신 소유권, 종료 예약, 대기 바이트 수와 상한.
///
/// <para>송신 소유권(<c>_running</c>)은 한 번에 한 쪽만 갖는다. 큐가 비어 있다가 <see cref="Enqueue"/>가 처음 넣은 쪽이
/// start=true 를 받아 송신 루프를 시작하고, 루프는 <see cref="DequeueBatch"/>가 큐가 비었다고 알릴 때 소유권을 반납한다.
/// 이 규약 덕분에 소켓/SslStream 에 동시에 쓰는 일이 없다.</para>
/// <para>이 클래스는 소켓을 모른다. 상한 초과(<see cref="EnqueueResult.Overflow"/>)의 처리(연결 끊기)는 호출자가 한다.</para>
/// </summary>
internal sealed class SendQueue
{
    public enum EnqueueResult
    {
        Queued,
        Rejected,       // 종료 예약/닫힘으로 받지 않음
        Overflow,       // 대기 바이트가 상한을 넘음(넣지 않았다)
    }

    private readonly object _lock = new object();
    private readonly Queue<SendBuffer> _queue = new Queue<SendBuffer>();
    private bool _running;
    private bool _closed;
    private volatile bool _closeAfterSend;
    private long _pendingBytes;
    private long _maxBytes;

    public void Init(long maxBytes) => _maxBytes = maxBytes;

    /// <summary>큐에 있거나 전송 중인(아직 완료 처리되지 않은) 바이트 수.</summary>
    public long PendingBytes => Interlocked.Read(ref _pendingBytes);

    /// <summary>DisconnectAfterSend 가 예약되었는지.</summary>
    public bool CloseAfterSend => _closeAfterSend;

    /// <summary>
    /// 큐에 넣는다. Queued 이고 start 가 true 이면 호출자가 송신 루프를 시작해야 한다(소유권을 이 호출이 가져왔다).
    /// </summary>
    public EnqueueResult Enqueue(SendBuffer buffer, out bool start)
    {
        start = false;
        if (_closeAfterSend)
            return EnqueueResult.Rejected;

        int size = buffer.WriteSize;
        long queued = Interlocked.Add(ref _pendingBytes, size);

        if (_maxBytes > 0 && queued > _maxBytes)
        {
            Interlocked.Add(ref _pendingBytes, -size);
            return EnqueueResult.Overflow;
        }

        lock (_lock)
        {
            // 락 안에서 다시 확인한다: 종료 예약/닫힘 뒤에는 송신 루프가 새로 시작되지 않아야 한다.
            if (_closeAfterSend || _closed)
            {
                Interlocked.Add(ref _pendingBytes, -size);
                return EnqueueResult.Rejected;
            }

            _queue.Enqueue(buffer);
            if (!_running)
            {
                _running = true;
                start = true;
            }
        }

        return EnqueueResult.Queued;
    }

    /// <summary>
    /// 종료 예약을 건다. 이미 예약되어 있으면 false. idleNow 가 true 이면 전송할 것이 없으니 호출자가 바로 종료 절차를 시작한다.
    /// </summary>
    public bool TryBeginCloseAfterSend(out bool idleNow)
    {
        lock (_lock)
        {
            idleNow = false;
            if (_closeAfterSend)
                return false;

            _closeAfterSend = true;
            idleNow = !_running && _queue.Count == 0;
            return true;
        }
    }

    /// <summary>
    /// 송신 루프용: 큐에서 최대 maxCount 개(바이트가 maxBytes 를 넘기 전까지, 최소 1개)를 batch 에 옮긴다. 옮긴 개수와 바이트 합을 돌려준다.
    /// 큐가 비어 있으면 0 을 돌려주고 송신 소유권을 반납한다(closeNow: 종료 예약이 있어 호출자가 종료 절차를 시작해야 함).
    /// </summary>
    public int DequeueBatch(List<SendBuffer> batch, int maxCount, int maxBytes, out int totalBytes, out bool closeNow)
    {
        totalBytes = 0;
        closeNow = false;
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                _running = false;
                closeNow = _closeAfterSend;
                return 0;
            }

            int count = 0;
            while (count < maxCount && _queue.Count > 0 && (count == 0 || totalBytes < maxBytes))
            {
                SendBuffer buffer = _queue.Dequeue();
                batch.Add(buffer);
                totalBytes += buffer.WriteSize;
                ++count;
            }

            return count;
        }
    }

    /// <summary>전송을 마친 바이트를 대기 합계에서 뺀다.</summary>
    public void Complete(int bytes) => Interlocked.Add(ref _pendingBytes, -bytes);

    /// <summary>송신 루프가 오류로 끝났을 때 소유권을 반납한다.</summary>
    public void ReleaseOwnership()
    {
        lock (_lock)
            _running = false;
    }

    /// <summary>세션 종료 정리: 대기 중인 것을 버리고 이후 Enqueue 를 거부한다.</summary>
    public void Close()
    {
        lock (_lock)
        {
            _closed = true;
            _queue.Clear();
        }
    }
}
