// Session.cs : implementation of the Session class.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Net;

/// <summary>
/// 연결 하나를 나타내는 추상 세션. 콘텐츠 코드는 이 클래스를 상속해 OnRecv 등을 구현한다.
/// 수신/송신은 각각 SocketAsyncEventArgs 하나로 직렬화되며, 진행 중인 I/O 개수(_pendingIoCount)가
/// 0 이 되고 연결이 끊긴 상태일 때 정확히 한 번 OnDisconnected 가 호출된다.
/// 콜백(OnConnected/OnRecv/OnSend/OnDisconnected)은 I/O 완료 스레드에서 직접 실행되므로 오래 블로킹하면 안 된다.
/// </summary>
public abstract class Session
{
    private enum SendResult
    {
        Completed,
        Partial,
        Failed,
    }

    private Socket? _socket;
    private NetService? _service;

    private readonly RecvBuffer _recvBuffer;
    private readonly SocketAsyncEventArgs _recvArgs = new SocketAsyncEventArgs();
    private readonly SocketAsyncEventArgs _sendArgs = new SocketAsyncEventArgs();

    private int _connected;
    private int _pendingIoCount;
    private int _finalized;
    private int _closeReason;

    // 유휴 감시: 마지막 수신 시각, 불완전 패킷의 진행 시각(Environment.TickCount64 기준)
    private long _lastActivityTick;
    private long _lastProgressTick;
    private volatile bool _hasPartial;
    private bool _idleIncludesSend;

    // 통계(수신/송신은 각각 한 번에 하나만 진행되지만 읽는 쪽이 다른 스레드이므로 Interlocked 사용)
    private long _bytesReceived;
    private long _bytesSent;

    // 세션별 직렬 잡 큐(필요할 때 생성)
    private JobQueue? _jobs;
    private bool _jobsOwned;

    // 정상 종료 대기(DisconnectAfterSend)
    private volatile bool _gracefulClosing;
    private int _gracefulTimeoutMs;

    // 수신 상태(수신은 한 번에 하나만 진행되므로 락 불필요)
    private bool _zeroByteRecv;
    private bool _recvIsZeroByte;
    private bool _zeroByteDone;

    // 송신 상태
    private readonly object _sendLock = new object();
    private readonly Queue<SendBuffer> _sendQueue = new Queue<SendBuffer>();
    private bool _sendRegistered;
    private bool _closeAfterSend;
    private CloseReason _afterSendReason;
    private long _pendingSendBytes;
    private long _maxSendQueueBytes;

    // 현재 전송 중인 배치. 한 시점에 한 스레드만 접근한다(_sendRegistered 로 소유권 이전).
    private readonly List<SendBuffer> _sending = new List<SendBuffer>();
    private readonly List<ArraySegment<byte>> _segments = new List<ArraySegment<byte>>();
    private bool _sendUsesBufferList;
    private int _sendIndex;
    private int _sendOffset;
    private int _sendBatchBytes;

    protected Session(int recvBufferSize = NetConstants.BufferSizeDefault)
    {
        _recvBuffer = new RecvBuffer(recvBufferSize);
        _recvArgs.Completed += OnIoCompleted;
        _sendArgs.Completed += OnIoCompleted;
    }

    /// <summary>서비스가 발급한 세션 식별자(소켓 핸들 재사용과 무관하게 유일).</summary>
    public long SessionId { get; private set; }

    /// <summary>상대 주소. 연결 직후 채워진다.</summary>
    public IPEndPoint? RemoteEndPoint { get; private set; }

    /// <summary>연결되어 있는지 여부.</summary>
    public bool IsConnected => Volatile.Read(ref _connected) == 1;

    /// <summary>송신 대기(큐 + 전송 중) 중인 바이트 수.</summary>
    public long PendingSendBytes => Interlocked.Read(ref _pendingSendBytes);

    internal Socket? Socket => _socket;

    //****************************************************************
    // 콘텐츠 재정의 지점
    //****************************************************************

    /// <summary>연결이 수립되어 수신이 시작되기 직전에 호출된다.</summary>
    protected virtual void OnConnected() { }

    /// <summary>
    /// 연결 종료가 완전히 정리된 뒤 정확히 한 번 호출된다. 이 시점에는 진행 중인 I/O 가 없다.
    /// </summary>
    protected virtual void OnDisconnected(CloseReason reason) { }

    /// <summary>
    /// 클라이언트 연결 시도가 실패했을 때 호출된다. 이 경우 OnConnected/OnDisconnected 는 호출되지 않는다.
    /// </summary>
    protected virtual void OnConnectFailed(SocketError error) { }

    /// <summary>
    /// 데이터가 수신될 때마다 호출된다. 이미 수신된 모든 데이터가 buffer 로 전달된다.
    /// </summary>
    /// <param name="buffer">처리 대기 중인 데이터. 반환 이후에는 무효이므로 보관하지 말 것.</param>
    /// <returns>소비한 바이트 수. 음수이거나 buffer 보다 크면 프로토콜 오류로 보고 연결을 끊는다.</returns>
    protected abstract int OnRecv(ReadOnlySpan<byte> buffer);

    /// <summary>한 번의 송신 배치가 완료될 때마다 전송된 총 바이트 수와 함께 호출된다.</summary>
    protected virtual void OnSend(int numOfBytes) { }

    /// <summary>
    /// 콜백에서 발생한 예외 처리. 기본 구현은 서비스에 로거가 있으면 로거로(Error), 없으면 Trace 로 기록한다.
    /// </summary>
    protected virtual void OnException(Exception exception)
    {
        if (_service != null && _service.HasLogger)
            _service.Log(NetLogLevel.Error, $"세션 {SessionId} 콜백 예외", exception);
        else
            System.Diagnostics.Trace.TraceError("[Session {0}] {1}", SessionId, exception);
    }

    //****************************************************************
    // 공개 API
    //****************************************************************

    /// <summary>
    /// SendBuffer 를 송신 큐에 넣는다. 큐에 쌓인 버퍼는 scatter-gather 로 묶여 한 번에 전송된다.
    /// 송신 대기 바이트가 MaxSendQueueBytes 를 넘으면 SendQueueOverflow 로 연결을 끊는다.
    /// </summary>
    /// <returns>연결이 끊겼거나, 비어 있는 버퍼이거나, 종료 예약(DisconnectAfterSend)된 세션이면 false.</returns>
    public bool Send(SendBuffer buffer)
    {
        if (buffer == null || buffer.WriteSize == 0 || !IsConnected)
            return false;

        int size = buffer.WriteSize;
        long queued = Interlocked.Add(ref _pendingSendBytes, size);

        if (_maxSendQueueBytes > 0 && queued > _maxSendQueueBytes)
        {
            Interlocked.Add(ref _pendingSendBytes, -size);
            Disconnect(CloseReason.SendQueueOverflow);
            return false;
        }

        bool needRegister;
        lock (_sendLock)
        {
            if (_closeAfterSend)
            {
                Interlocked.Add(ref _pendingSendBytes, -size);
                return false;
            }

            _sendQueue.Enqueue(buffer);
            needRegister = !_sendRegistered;
            if (needRegister)
                _sendRegistered = true;
        }

        if (needRegister)
            RegisterSend();

        return true;
    }

    /// <summary>data 를 복사해 송신 큐에 넣는다.</summary>
    public bool Send(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || !IsConnected)
            return false;

        return Send(SendBufferManager.Create(data));
    }

    /// <summary>
    /// 연결을 즉시 끊는다. 여러 번/여러 스레드에서 호출해도 한 번만 동작하며,
    /// 아직 전송되지 않은 큐의 데이터는 버려진다.
    /// </summary>
    public void Disconnect(CloseReason reason = CloseReason.Local)
    {
        if (Volatile.Read(ref _connected) == 0)
            return;

        // 먼저 호출된 원인만 기록한다(종료 정리가 다른 스레드에서 먼저 끝나도 원인이 유실되지 않도록 선기록).
        Interlocked.CompareExchange(ref _closeReason, (int)reason, 0);

        if (Interlocked.Exchange(ref _connected, 0) == 0)
            return;

        SocketUtils.CloseSocket(_socket);

        if (Volatile.Read(ref _pendingIoCount) == 0)
            TryFinalize();
    }

    /// <summary>
    /// 지금까지 Send 한 데이터를 모두 전송한 뒤 연결을 끊는다(예: 로그아웃 응답 후 종료).
    /// 호출 이후의 Send 는 거부된다. 전송 중 오류가 나면 그 즉시 끊긴다.
    /// </summary>
    public void DisconnectAfterSend(CloseReason reason = CloseReason.Local)
    {
        if (!IsConnected)
            return;

        bool closeNow;
        lock (_sendLock)
        {
            if (_closeAfterSend)
                return;

            _closeAfterSend = true;
            _afterSendReason = reason;
            closeNow = !_sendRegistered && _sendQueue.Count == 0;
        }

        if (closeNow)
            BeginGracefulClose(reason);
    }

    /// <summary>이 세션이 받은 누적 바이트.</summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>이 세션이 보낸 누적 바이트.</summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>
    /// 이 세션 전용 직렬 잡 큐(처음 접근할 때 생성). I/O 콜백에서 콘텐츠 처리를 분리할 때 쓴다.
    /// 세션이 끝나면(OnDisconnected 호출 뒤) 큐가 닫혀 새 잡은 거부되고, 이미 들어 있는 잡은 끝까지 실행된다.
    /// 방(Room)처럼 여러 세션이 큐 하나를 공유하려면 AttachJobQueue 로 공유 큐를 지정한다.
    /// </summary>
    public JobQueue Jobs
    {
        get
        {
            JobQueue? queue = Volatile.Read(ref _jobs);
            if (queue != null)
                return queue;

            var created = new JobQueue();
            queue = Interlocked.CompareExchange(ref _jobs, created, null);
            if (queue != null)
                return queue;

            _jobsOwned = true;

            // 이미 종료 정리가 끝난 뒤에 처음 접근했다면 바로 닫는다.
            if (Volatile.Read(ref _finalized) != 0)
                created.Close();

            return created;
        }
    }

    /// <summary>
    /// 세션의 잡 큐로 외부 큐(예: 방 하나가 공유하는 큐)를 지정한다. 외부 큐는 세션이 끝나도 닫지 않는다.
    /// Jobs 를 이미 사용했다면 InvalidOperationException.
    /// </summary>
    public void AttachJobQueue(JobQueue queue)
    {
        if (queue == null)
            throw new ArgumentNullException(nameof(queue));

        if (Interlocked.CompareExchange(ref _jobs, queue, null) != null)
            throw new InvalidOperationException("이미 잡 큐가 지정되었거나 Jobs 가 사용되었습니다.");
    }

    //****************************************************************
    // 서비스 내부용
    //****************************************************************

    /// <summary>OnException 재정의에서 예외가 나도 프로세스가 죽지 않도록 감싼다.</summary>
    private void SafeOnException(Exception exception)
    {
        try { OnException(exception); }
        catch (Exception) { }
    }

    /// <summary>유휴 시간 초과 여부를 검사하고 초과했으면 연결을 끊는다.</summary>
    internal void CheckIdle(long nowTick, int timeoutMs)
    {
        if (!IsConnected)
            return;

        bool idle = nowTick - Volatile.Read(ref _lastActivityTick) > timeoutMs;
        bool stalled = _hasPartial && nowTick - Volatile.Read(ref _lastProgressTick) > timeoutMs;

        if (idle || stalled)
            Disconnect(CloseReason.IdleTimeout);
    }

    internal void Attach(NetService service, Socket socket, long sessionId)
    {
        _service = service;
        _socket = socket;
        SessionId = sessionId;
        _maxSendQueueBytes = service.Options.MaxSendQueueBytes;
        _zeroByteRecv = service.Options.ZeroByteRecv;
        _gracefulTimeoutMs = service.Options.GracefulCloseTimeoutMs;
        _idleIncludesSend = service.Options.IdleIncludesSend;
    }

    /// <summary>연결 수립 후 호출: 연결 상태로 전환하고 수신을 시작한다.</summary>
    internal void Start()
    {
        try { RemoteEndPoint = _socket?.RemoteEndPoint as IPEndPoint; }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }

        long now = Environment.TickCount64;
        Volatile.Write(ref _lastActivityTick, now);
        Volatile.Write(ref _lastProgressTick, now);

        Interlocked.Exchange(ref _connected, 1);
        _service?.OnSessionStarted();

        if (_service != null && _service.IsLogEnabled(NetLogLevel.Trace))
            _service.Log(NetLogLevel.Trace, $"세션 {SessionId} 연결됨: {RemoteEndPoint}");

        try { OnConnected(); }
        catch (Exception e) { SafeOnException(e); }

        RegisterRecv();
    }

    /// <summary>
    /// 연결 시도 실패 처리. 연결된 적이 없으므로 OnDisconnected 대신 OnConnectFailed 를 호출하고 자원을 정리한다.
    /// </summary>
    internal void FailConnect(SocketError error)
    {
        if (Interlocked.Exchange(ref _finalized, 1) != 0)
            return;

        _service?.Log(NetLogLevel.Warning, $"세션 {SessionId} 연결 실패: {SocketUtils.GetErrorText(error)}");

        try { OnConnectFailed(error); }
        catch (Exception e) { SafeOnException(e); }

        SocketUtils.CloseSocket(_socket);
        _recvArgs.Dispose();
        _sendArgs.Dispose();
        _service?.RemoveSession(this);
    }

    /// <summary>
    /// 정상 종료: 송신 측만 닫아(FIN) 전송한 데이터가 상대에게 끝까지 전달되게 하고,
    /// 상대가 연결을 닫으면(수신 0바이트) 정리한다. 상대가 닫지 않으면 GracefulCloseTimeoutMs 후 강제로 닫는다.
    /// 종료 사유는 먼저 기록해 두므로 이후 RemoteClosed 로 덮어쓰이지 않는다.
    /// </summary>
    private void BeginGracefulClose(CloseReason reason)
    {
        Interlocked.CompareExchange(ref _closeReason, (int)reason, 0);
        _gracefulClosing = true;

        try
        {
            _socket!.Shutdown(SocketShutdown.Send);
        }
        catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
        {
            Disconnect(reason);
            return;
        }

        if (_gracefulTimeoutMs <= 0)
        {
            Disconnect(reason);
            return;
        }

        Task.Delay(_gracefulTimeoutMs).ContinueWith(_ => Disconnect(reason), TaskScheduler.Default);
    }

    //****************************************************************
    // 수신
    //****************************************************************

    private void RegisterRecv()
    {
        // 동기 완료 시 재귀 대신 루프로 처리해 스택 증가를 막는다.
        while (true)
        {
            Interlocked.Increment(ref _pendingIoCount);
            if (Volatile.Read(ref _connected) == 0)
            {
                DecrementPending();
                return;
            }

            if (_zeroByteRecv && !_zeroByteDone && _recvBuffer.DataSize == 0)
            {
                // 데이터 도착만 기다린다. 이 동안에는 수신 버퍼를 보유하지 않는다.
                _recvIsZeroByte = true;
                _recvArgs.SetBuffer(Memory<byte>.Empty);
            }
            else
            {
                _recvBuffer.EnsureRented();

                if (_recvBuffer.FreeSize == 0)
                {
                    // 하나의 메시지가 수신 버퍼 용량보다 큼.
                    Disconnect(CloseReason.ProtocolError);
                    DecrementPending();
                    return;
                }

                _recvIsZeroByte = false;
                _recvArgs.SetBuffer(_recvBuffer.Array, _recvBuffer.WritePos, _recvBuffer.FreeSize);
            }

            bool isPending;
            try
            {
                isPending = _socket!.ReceiveAsync(_recvArgs);
            }
            catch (ObjectDisposedException)
            {
                Disconnect(CloseReason.RecvError);
                DecrementPending();
                return;
            }

            if (isPending)
                return;

            if (!ProcessRecv(_recvArgs))
                return;
        }
    }

    /// <returns>계속 수신을 이어가야 하면 true.</returns>
    private bool ProcessRecv(SocketAsyncEventArgs args)
    {
        int numOfBytes = args.BytesTransferred;

        if (args.SocketError != SocketError.Success)
        {
            bool normal = SocketUtils.IsNormalDisconnect(args.SocketError);
            Disconnect(normal ? CloseReason.RemoteClosed : CloseReason.RecvError);
            DecrementPending();
            return false;
        }

        // 데이터 도착(또는 FIN)이 확인된 시점을 유휴 기준으로 기록한다.
        long now = Environment.TickCount64;
        Volatile.Write(ref _lastActivityTick, now);

        if (_recvIsZeroByte)
        {
            // 0바이트 수신 성공 = 읽을 데이터가 도착했거나 상대가 닫음. 실제 읽기에서 구분된다.
            _zeroByteDone = true;
            DecrementPending();
            return true;
        }

        if (numOfBytes == 0)
        {
            Disconnect(CloseReason.RemoteClosed);
            DecrementPending();
            return false;
        }

        Interlocked.Add(ref _bytesReceived, numOfBytes);

        bool hadData = _recvBuffer.DataSize > 0;

        if (!_recvBuffer.OnWrite(numOfBytes))
        {
            Disconnect(CloseReason.ProtocolError);
            DecrementPending();
            return false;
        }

        int processed;
        if (_gracefulClosing)
        {
            // 정상 종료 대기 중에는 새로 들어오는 데이터를 버린다.
            processed = _recvBuffer.DataSize;
        }
        else
        {
            try
            {
                processed = OnRecv(_recvBuffer.ReadSpan);
            }
            catch (Exception e)
            {
                SafeOnException(e);
                processed = -1;
            }
        }

        if (processed < 0 || !_recvBuffer.OnRead(processed))
        {
            Disconnect(CloseReason.ProtocolError);
            DecrementPending();
            return false;
        }

        _recvBuffer.Clean();

        // 불완전한 패킷이 남아 있으면 '진행 시각'은 패킷이 새로 시작되거나 하나라도 완성될 때만 갱신한다.
        // 한 바이트씩 흘려 보내는 연결은 활동 시각만 갱신되고 진행 시각은 멈추므로 stalled 로 걸러진다.
        if (_recvBuffer.DataSize == 0)
        {
            _hasPartial = false;
        }
        else
        {
            if (processed > 0 || !hadData)
                Volatile.Write(ref _lastProgressTick, now);

            _hasPartial = true;
        }

        if (_zeroByteRecv)
        {
            _zeroByteDone = false;
            _recvBuffer.ReleaseIfEmpty();
        }

        DecrementPending();
        return true;
    }

    //****************************************************************
    // 송신
    //****************************************************************

    /// <summary>큐가 빌 때까지 배치를 만들어 전송한다. 비동기 대기에 들어가면 완료 콜백이 이어받는다.</summary>
    private void RegisterSend()
    {
        while (BuildBatch())
        {
            if (!PostSend())
                return;
        }
    }

    /// <summary>
    /// 큐에서 최대 MaxSendBatch 개를 꺼내 전송 배치로 만든다.
    /// 큐가 비면 송신 소유권을 반납하고 false (종료 예약이 있으면 이때 연결을 끊는다).
    /// </summary>
    private bool BuildBatch()
    {
        bool closeNow = false;

        lock (_sendLock)
        {
            if (_sendQueue.Count == 0)
            {
                _sendRegistered = false;
                closeNow = _closeAfterSend;
            }
            else
            {
                _sending.Clear();
                _sendIndex = 0;
                _sendOffset = 0;
                _sendBatchBytes = 0;

                int count = Math.Min(NetConstants.MaxSendBatch, _sendQueue.Count);
                for (int i = 0; i < count; i++)
                {
                    SendBuffer buffer = _sendQueue.Dequeue();
                    _sending.Add(buffer);
                    _sendBatchBytes += buffer.WriteSize;
                }

                return true;
            }
        }

        if (closeNow)
            BeginGracefulClose(_afterSendReason);

        return false;
    }

    /// <summary>
    /// 현재 배치를 전송한다. 부분 전송이면 남은 구간으로 다시 요청한다.
    /// </summary>
    /// <returns>배치를 동기적으로 모두 보냈으면 true. 비동기 대기 중이거나 실패했으면 false.</returns>
    private bool PostSend()
    {
        while (true)
        {
            Interlocked.Increment(ref _pendingIoCount);
            if (Volatile.Read(ref _connected) == 0)
            {
                DecrementPending();
                return false;
            }

            BuildSegments();

            bool isPending;
            try
            {
                isPending = _socket!.SendAsync(_sendArgs);
            }
            catch (ObjectDisposedException)
            {
                Disconnect(CloseReason.SendError);
                DecrementPending();
                return false;
            }

            if (isPending)
                return false;

            switch (ProcessSend(_sendArgs))
            {
                case SendResult.Completed:
                    return true;
                case SendResult.Failed:
                    return false;
                default:
                    break; // Partial: 남은 구간 재전송
            }
        }
    }

    private void BuildSegments()
    {
        int remainCount = _sending.Count - _sendIndex;

        if (remainCount == 1)
        {
            // 버퍼 하나: BufferList(복사 + WSABUF 배열) 대신 단일 버퍼 경로를 사용한다.
            ArraySegment<byte> segment = _sending[_sendIndex].Segment;

            if (_sendUsesBufferList)
            {
                _sendArgs.BufferList = null;
                _sendUsesBufferList = false;
            }

            _sendArgs.SetBuffer(segment.Array!, segment.Offset + _sendOffset, segment.Count - _sendOffset);
            return;
        }

        _segments.Clear();

        for (int i = _sendIndex; i < _sending.Count; i++)
        {
            ArraySegment<byte> segment = _sending[i].Segment;
            if (i == _sendIndex && _sendOffset > 0)
                segment = new ArraySegment<byte>(segment.Array!, segment.Offset + _sendOffset, segment.Count - _sendOffset);

            _segments.Add(segment);
        }

        if (!_sendUsesBufferList)
        {
            _sendArgs.SetBuffer(null, 0, 0);
            _sendUsesBufferList = true;
        }

        // SocketAsyncEventArgs 는 BufferList 를 설정 시점에 내부로 복사하므로 매번 다시 설정해야 한다.
        _sendArgs.BufferList = null;
        _sendArgs.BufferList = _segments;
    }

    private SendResult ProcessSend(SocketAsyncEventArgs args)
    {
        int numOfBytes = args.BytesTransferred;

        if (args.SocketError != SocketError.Success || numOfBytes <= 0)
        {
            bool normal = SocketUtils.IsNormalDisconnect(args.SocketError);
            Disconnect(normal ? CloseReason.RemoteClosed : CloseReason.SendError);
            DecrementPending();
            return SendResult.Failed;
        }

        Interlocked.Add(ref _bytesSent, numOfBytes);

        if (_idleIncludesSend)
            Volatile.Write(ref _lastActivityTick, Environment.TickCount64);

        // 전송된 바이트만큼 배치 커서를 전진시킨다.
        int remain = numOfBytes;
        while (remain > 0 && _sendIndex < _sending.Count)
        {
            int left = _sending[_sendIndex].WriteSize - _sendOffset;
            if (remain >= left)
            {
                remain -= left;
                _sendIndex++;
                _sendOffset = 0;
            }
            else
            {
                _sendOffset += remain;
                remain = 0;
            }
        }

        if (_sendIndex < _sending.Count)
        {
            DecrementPending();
            return SendResult.Partial;
        }

        int total = _sendBatchBytes;
        _sending.Clear();
        Interlocked.Add(ref _pendingSendBytes, -total);

        try { OnSend(total); }
        catch (Exception e) { SafeOnException(e); }

        DecrementPending();
        return SendResult.Completed;
    }

    private void OnSendCompleted(SocketAsyncEventArgs args)
    {
        SendResult result = ProcessSend(args);

        if (result == SendResult.Failed)
            return;

        if (result == SendResult.Partial && !PostSend())
            return;

        RegisterSend();
    }

    //****************************************************************
    // 공통
    //****************************************************************

    private void OnIoCompleted(object? sender, SocketAsyncEventArgs args)
    {
        // I/O 완료 콜백에서 예외가 새어 나가면 ThreadPool 스레드에서 프로세스가 종료되므로 여기서 차단한다.
        try
        {
            switch (args.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    if (ProcessRecv(args))
                        RegisterRecv();
                    break;

                case SocketAsyncOperation.Send:
                    OnSendCompleted(args);
                    break;

                default:
                    // 사용하지 않는 작업 종류. 완료된 I/O 로 계산해 정리 흐름이 막히지 않게 한다.
                    Disconnect(CloseReason.ProtocolError);
                    DecrementPending();
                    break;
            }
        }
        catch (Exception e)
        {
            SafeOnException(e);
            Disconnect(CloseReason.ProtocolError);
        }
    }

    /// <summary>
    /// 종료 사유를 로그로 남긴다. 정상 종료는 Trace, 오류/타임아웃/프로토콜 위반/큐 초과는 Warning.
    /// </summary>
    private void LogClosed(CloseReason reason)
    {
        if (_service == null)
            return;

        bool abnormal = reason == CloseReason.RecvError
                        || reason == CloseReason.SendError
                        || reason == CloseReason.ProtocolError
                        || reason == CloseReason.SendQueueOverflow
                        || reason == CloseReason.IdleTimeout;

        NetLogLevel level = abnormal ? NetLogLevel.Warning : NetLogLevel.Trace;
        if (_service.IsLogEnabled(level))
            _service.Log(level, $"세션 {SessionId} 종료: {RemoteEndPoint} ({reason})");
    }

    /// <summary>
    /// 진행 중 I/O 를 하나 줄인다. 마지막 I/O 이면서 이미 연결이 끊긴 상태면 종료 정리를 수행한다.
    /// </summary>
    private void DecrementPending()
    {
        if (Interlocked.Decrement(ref _pendingIoCount) == 0 && Volatile.Read(ref _connected) == 0)
            TryFinalize();
    }

    /// <summary>
    /// 종료 정리. Disconnect() 쪽과 마지막 I/O 완료 쪽 어느 한 곳에서만 실행되도록 CAS 로 보장한다.
    /// </summary>
    private void TryFinalize()
    {
        if (Interlocked.Exchange(ref _finalized, 1) != 0)
            return;

        lock (_sendLock)
        {
            _sendQueue.Clear();
            _sending.Clear();
        }

        CloseReason closeReason = (CloseReason)Volatile.Read(ref _closeReason);
        LogClosed(closeReason);

        try { OnDisconnected(closeReason); }
        catch (Exception e) { SafeOnException(e); }

        // 세션 전용 잡 큐는 OnDisconnected 안에서 넣은 잡까지 받도록 콜백 뒤에 닫는다(외부 큐는 건드리지 않음).
        if (_jobsOwned)
            Volatile.Read(ref _jobs)?.Close();

        // 진행 중인 I/O 가 없으므로 수신 버퍼를 풀에 안전하게 반납할 수 있다.
        _recvBuffer.Release();
        _recvArgs.Dispose();
        _sendArgs.Dispose();
        _service?.OnSessionClosed(Interlocked.Read(ref _bytesReceived), Interlocked.Read(ref _bytesSent));
        _service?.RemoveSession(this);
    }
}
