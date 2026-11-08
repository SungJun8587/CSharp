// Listener.cs : implementation of the Listener class.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Net;

/// <summary>
/// 리슨 소켓을 소유하고 AcceptAsync 를 여러 개 동시에 걸어 두는 클래스.
/// Accept 실패 시에는 워커를 블로킹하지 않도록 지연 후 별도 타이머 태스크에서 재시도하며,
/// Stop() 은 모든 Accept 작업과 예약된 재시도가 끝날 때까지 기다린 뒤 반환한다.
/// </summary>
internal sealed class Listener
{
    private sealed class AcceptSlot
    {
        public SocketAsyncEventArgs Args = new SocketAsyncEventArgs();
        public int FailCount;
    }

    private readonly ServerService _service;
    private readonly IPEndPoint _endPoint;
    private readonly NetServiceOptions _options;
    private readonly List<AcceptSlot> _slots = new List<AcceptSlot>();
    private readonly TaskCompletionSource<bool> _drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    private Socket? _listenSocket;
    private int _closing;
    private int _outstanding;

    public Listener(ServerService service, IPEndPoint endPoint, NetServiceOptions options)
    {
        _service = service;
        _endPoint = endPoint;
        _options = options;
    }

    public bool Start()
    {
        _listenSocket = SocketUtils.CreateListenSocket(_endPoint, _options.Backlog);
        if (_listenSocket == null)
            return false;

        int acceptCount = Math.Max(1, _options.AcceptCount);
        _outstanding = acceptCount;

        for (int i = 0; i < acceptCount; i++)
        {
            var slot = new AcceptSlot();
            slot.Args.UserToken = slot;
            slot.Args.Completed += OnAcceptCompleted;
            _slots.Add(slot);
        }

        foreach (AcceptSlot slot in _slots)
            RegisterAccept(slot);

        return true;
    }

    /// <summary>리슨 소켓을 닫고 진행 중인 Accept/재시도가 모두 끝날 때까지 기다린다(스레드를 블로킹).</summary>
    public void Stop()
    {
        if (!BeginStop())
            return;

        _drained.Task.Wait(NetConstants.DefaultCloseTimeoutMs);
        DisposeSlots();
    }

    /// <summary>Stop() 의 비동기 버전. 스레드를 블로킹하지 않고 Accept/재시도가 끝나기를 기다린다.</summary>
    public async Task StopAsync()
    {
        if (!BeginStop())
            return;

        await Task.WhenAny(_drained.Task, Task.Delay(NetConstants.DefaultCloseTimeoutMs)).ConfigureAwait(false);
        DisposeSlots();
    }

    /// <summary>종료 플래그를 세우고 리슨 소켓을 닫는다. 이미 종료 중이면 false.</summary>
    private bool BeginStop()
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0)
            return false;

        SocketUtils.CloseSocket(_listenSocket);
        return true;
    }

    private void DisposeSlots()
    {
        foreach (AcceptSlot slot in _slots)
            slot.Args.Dispose();
    }

    /// <summary>실제 바인드된 로컬 주소(포트 0 으로 바인드한 경우 확인용).</summary>
    public IPEndPoint? LocalEndPoint => _listenSocket?.LocalEndPoint as IPEndPoint;

    private void RegisterAccept(AcceptSlot slot)
    {
        // 동기 완료 시 재귀 대신 루프로 처리한다.
        while (true)
        {
            if (Volatile.Read(ref _closing) == 1)
            {
                Release();
                return;
            }

            slot.Args.AcceptSocket = null;

            bool isPending;
            try
            {
                isPending = _listenSocket!.AcceptAsync(slot.Args);
            }
            catch (ObjectDisposedException)
            {
                Release();
                return;
            }

            if (isPending)
                return;

            if (!ProcessAccept(slot))
                return;
        }
    }

    /// <returns>즉시 다음 Accept 를 걸어야 하면 true. 체인이 종료/예약되었으면 false.</returns>
    private bool ProcessAccept(AcceptSlot slot)
    {
        SocketAsyncEventArgs args = slot.Args;

        if (args.SocketError == SocketError.Success)
        {
            Socket? accepted = args.AcceptSocket;
            args.AcceptSocket = null;
            slot.FailCount = 0;

            if (accepted != null)
            {
                try
                {
                    _service.OnAccepted(accepted);
                }
                catch (Exception e)
                {
                    _service.Log(NetLogLevel.Error, "Accept 된 소켓 처리 실패", e);
                    SocketUtils.CloseSocket(accepted);
                }
            }

            return true;
        }

        if (Volatile.Read(ref _closing) == 1 || SocketUtils.IsListenClosed(args.SocketError))
        {
            Release();
            return false;
        }

        // 일시적 오류(소켓 고갈 등): 워커를 붙잡지 않고 지연 후 재시도. 재시도가 예약된 동안에도 outstanding 은 유지된다.
        slot.FailCount++;
        int backoff = Math.Min(slot.FailCount, NetConstants.MaxAcceptRetryBackoff);
        int delayMs = NetConstants.AcceptRetryDelayMs * backoff;
        _service.Log(NetLogLevel.Warning, $"Accept 실패: {SocketUtils.GetErrorText(args.SocketError)}, {delayMs}ms 후 재시도 (연속 {slot.FailCount}회)");
        Task.Delay(delayMs).ContinueWith(_ => RegisterAccept(slot), TaskScheduler.Default);
        return false;
    }

    private void OnAcceptCompleted(object? sender, SocketAsyncEventArgs args)
    {
        var slot = (AcceptSlot)args.UserToken!;
        if (ProcessAccept(slot))
            RegisterAccept(slot);
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _outstanding) == 0)
            _drained.TrySetResult(true);
    }
}
