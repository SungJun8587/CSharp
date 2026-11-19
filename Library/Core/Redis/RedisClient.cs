// RedisClient.cs : 소켓 하나에 대응하는 비동기 Redis 클라이언트.
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Core.Net;
using Core.Util;

namespace Core.Redis;

/// <summary>
/// 비동기 소켓 단위의 Redis 클라이언트. 명령을 RESP로 인코딩해 보내고, 수신 루프가 응답을 파싱해 콜백을 실행한다.
///
/// <para><b>콜백 계약</b>: <see cref="SendCommand"/>에 넘긴 콜백은 반환값과 무관하게 정확히 한 번 호출된다 —
/// 응답이 오면 응답으로, 즉시 거절되면(연결 없음, 미완료 명령 존재, 크기 초과) 에러 값으로, 연결이 끊기면
/// <see cref="Disconnect"/>가 에러 값으로 완료시킨다. 반환값은 "접수되어 전송 경로에 올랐는지"를 알려주는 참고 정보이며,
/// false를 받은 호출부가 콜백을 따로 부를 필요는 없다.</para>
///
/// <para><b>스레드 계약</b>: 한 시점에 미완료 명령은 하나만 존재한다고 가정한다(요청-응답 연결). 풀은 커넥션을 대여한 뒤
/// 응답을 받을 때까지 재대여하지 않는 방식으로 이를 지킨다. 위반하면 <see cref="RedisConst.ErrCommandInFlight"/> 에러로 거절된다.
/// <see cref="Disconnect"/>는 여러 스레드에서 동시에 호출해도 안전하며, 실제 정리는 한 번만 일어난다.</para>
///
/// <para><b>연결 단위 상태</b>: 소켓, 파서, 송신 큐, 대기 콜백은 모두 연결(<c>Conn</c>)마다 새로 만들어진다. 끊긴 연결의 수신/송신
/// 루프가 늦게 끝나더라도 자기 연결의 상태만 만지므로, 재연결한 새 연결로 응답이나 콜백이 섞여 들어가지 않는다.</para>
///
/// <para><b>콜백 스레드</b>: 응답 콜백은 수신 루프(스레드 풀)에서 호출된다. 콜백 안에서 오래 걸리는 일을 하지 않는다.</para>
/// </summary>
public sealed class RedisClient : IDisposable
{
    private sealed class Conn
    {
        public readonly Socket Socket;
        public readonly RedisParser Parser = new RedisParser();

        // 아래 필드는 RedisClient._commandLock으로 보호한다.
        public readonly Queue<SendItem> SendQueue = new Queue<SendItem>();
        public bool Sending;                    // 송신 펌프가 돌고 있는지
        public RedisCallback? Pending;          // 응답을 기다리는 중인 콜백(한 시점에 최대 1개)
        public long CommandStartMs;             // Pending을 등록한 시각(단조 시계 ms, 없으면 0)

        public Conn(Socket socket) => Socket = socket;
    }

    private readonly struct SendItem
    {
        public readonly byte[] Buffer;
        public readonly int Length;

        public SendItem(byte[] buffer, int length)
        {
            Buffer = buffer;
            Length = length;
        }
    }

    private static readonly RedisCallback s_noopCallback = static _ => { };

    private readonly object _commandLock = new object();    // 현재 연결의 Pending/SendQueue/Sending 동기화
    private Conn? _conn;                                    // Interlocked/Volatile로만 접근
    private int _connecting;                                // ConnectAsync 동시 진입 방지
    private RedisPushCallback? _pushCallback;

    /// <summary>소켓 연결 여부.</summary>
    public bool IsConnected => Volatile.Read(ref _conn) != null;

    /// <summary>
    /// 이 연결을 구독(Pub/Sub) 모드로 쓰기 위해 push 메시지 콜백을 지정한다. 지정하면 서버가 자발적으로 보내는
    /// ["message", ...] / ["pmessage", ...] 배열은 대기 중인 명령 콜백을 소비하지 않고 이 콜백으로만 전달된다.
    /// 그 외 응답(SUBSCRIBE/UNSUBSCRIBE 확인, PING 응답, 에러)은 미완료 명령의 콜백을 완료시킨다.
    /// 연결하기 전에 한 번만 호출한다. 구독 모드에서는 SUBSCRIBE 하나가 채널 수만큼 확인 응답을 돌려주므로
    /// 한 명령에 채널 하나씩만 보내야 응답 하나가 명령 하나에 대응한다.
    /// </summary>
    public void SetPushCallback(RedisPushCallback? callback) => _pushCallback = callback;

    /// <summary>
    /// 응답을 기다리는 명령이 있으면 그 명령을 보낸 뒤 지난 시간(ms)을, 없으면 0을 반환한다.
    /// 서버가 응답 없이 멈추거나 경로가 반열림 상태가 되면 콜백이 오지 않으므로, 풀이 이 값으로 응답 지연을 감지해 연결을 끊는다.
    /// </summary>
    public long GetPendingCommandAgeMs()
    {
        Conn? conn = Volatile.Read(ref _conn);
        if (conn == null)
            return 0;

        long start = Interlocked.Read(ref conn.CommandStartMs);
        return start == 0 ? 0 : TimeUtil.MonotonicNowMillis() - start;
    }

    public void Dispose() => Disconnect();

    // ===================================================================================
    // 접속
    // ===================================================================================

    /// <summary>
    /// Redis 서버에 접속하고, (필요하면) 인증(AUTH)과 논리 DB 선택(SELECT)까지 마친다. <see cref="ConnectAsync"/>의 동기 버전.
    /// </summary>
    public bool Connect(string host, ushort port, int dbIndex = 0, int connectTimeoutMs = RedisConst.ConnectTimeoutMs,
        string userId = "", string password = "")
        => ConnectAsync(host, port, dbIndex, connectTimeoutMs, userId, password).GetAwaiter().GetResult();

    /// <summary>
    /// Redis 서버에 접속하고, 인증(AUTH)과 논리 DB 선택(SELECT)까지 마친다.
    /// </summary>
    /// <param name="host">서버 주소. IP 문자열(IPv4/IPv6) 또는 DNS 이름. 이름 해석도 전체 제한 시간에 포함된다.</param>
    /// <param name="dbIndex">접속 직후 SELECT로 고정할 논리 DB. 0(기본 DB)이면 SELECT를 생략한다. 재연결 때도 같은 인자로 호출하면 항상 같은 DB를 본다.</param>
    /// <param name="connectTimeoutMs">연결과 (AUTH/SELECT를 쓰는 경우) 그 응답을 기다리는 최대 시간(ms).</param>
    /// <param name="userId">ACL 사용자 이름(Redis 6+). 비어 있으면 <c>AUTH &lt;password&gt;</c>로 보낸다.</param>
    /// <param name="password">비밀번호. 비어 있으면 인증을 건너뛴다. SELECT보다 먼저 인증한다. 로그에 남기지 않는다.</param>
    /// <returns>성공 여부. 이미 연결되어 있거나 다른 접속이 진행 중이면 false.</returns>
    public async Task<bool> ConnectAsync(string host, ushort port, int dbIndex = 0, int connectTimeoutMs = RedisConst.ConnectTimeoutMs,
        string userId = "", string password = "")
    {
        if (connectTimeoutMs <= 0 || string.IsNullOrEmpty(host))
            return false;

        if (Interlocked.Exchange(ref _connecting, 1) != 0)
            return false;

        try
        {
            if (Volatile.Read(ref _conn) != null)
                return false;

            long deadlineMs = TimeUtil.MonotonicNowMillis() + connectTimeoutMs;
            Socket? socket = await ConnectSocketAsync(host, port, deadlineMs, connectTimeoutMs).ConfigureAwait(false);
            if (socket == null)
                return false;

            // 소켓 옵션 실패는 치명적이지 않다 — 연결 자체는 쓸 수 있다.
            //  - NoDelay: 요청-응답 연결이라 합쳐 보낼 이득이 없고, Nagle이 큰 명령의 마지막 조각을 붙잡을 수 있다.
            //  - KeepAlive: 풀의 커넥션은 요청이 없는 동안 오래 놀 수 있는데, 그 사이 방화벽/NAT/로드밸런서가 idle 연결을
            //    조용히 정리하면 양쪽 모두 모른 채로 있다가 다음 명령을 보낼 때에야 실패로 발견한다.
            if (!SocketUtils.SetNoDelay(socket, true))
                LogManager.Error("RedisClient.Connect: TCP_NODELAY 설정 실패 — 계속 진행");

            if (!SocketUtils.SetKeepAlive(socket, true, RedisConst.KeepAliveIdleSec, RedisConst.KeepAliveIntervalSec))
                LogManager.Error("RedisClient.Connect: keepalive 설정 실패 — keepalive 없이 계속 진행");

            var conn = new Conn(socket);
            Volatile.Write(ref _conn, conn);
            _ = ReceiveLoopAsync(conn);

            // 비밀번호가 있으면 SELECT보다 먼저 인증한다(인증 전에는 다른 명령이 거부된다).
            if (!string.IsNullOrEmpty(password) && !await AuthenticateAsync(userId, password, connectTimeoutMs).ConfigureAwait(false))
            {
                Disconnect();
                return false;
            }

            if (dbIndex > 0 && !await SendCommandAndWaitAsync(new[] { "SELECT", dbIndex.ToString() }, connectTimeoutMs).ConfigureAwait(false))
            {
                Disconnect();
                return false;
            }

            return true;
        }
        finally
        {
            Volatile.Write(ref _connecting, 0);
        }
    }

    // 주소 목록을 순서대로 시도하되, 전체 시간은 deadline을 넘지 않게 남은 시간으로 자른다.
    private static async Task<Socket?> ConnectSocketAsync(string host, ushort port, long deadlineMs, int timeoutMs)
    {
        IPAddress[]? addresses = await HostResolver.ResolveAsync(host, timeoutMs).ConfigureAwait(false);
        if (addresses == null || addresses.Length == 0)
            return null;

        foreach (IPAddress address in addresses)
        {
            long remaining = deadlineMs - TimeUtil.MonotonicNowMillis();
            if (remaining <= 0)
                break;

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                using var cts = new CancellationTokenSource((int)Math.Min(remaining, int.MaxValue));
                await socket.ConnectAsync(new IPEndPoint(address, port), cts.Token).ConfigureAwait(false);
                return socket;
            }
            catch (Exception)
            {
                SocketUtils.CloseSocket(socket);
            }
        }

        return null;
    }

    // ===================================================================================
    // 종료
    // ===================================================================================

    /// <summary>
    /// 소켓 연결을 종료하고 대기 중인 콜백을 에러 값으로 완료시킨다. 여러 스레드에서 동시에 호출되어도(예: 수신 루프와 송신 루프가
    /// 거의 동시에 끊김을 발견한 경우) 연결은 한 번만 정리되며, 먼저 정리에 성공한 쪽의 사유가 채택된다.
    /// </summary>
    public bool Disconnect(RedisDisconnectReason reason = RedisDisconnectReason.Generic)
    {
        Conn? conn = Interlocked.Exchange(ref _conn, null);
        if (conn == null)
            return true;

        CloseConn(conn, reason);
        return true;
    }

    // 자기 연결이 아직 현재 연결일 때만 정리한다(끊긴 연결의 루프가 새 연결을 건드리지 않게).
    private void Disconnect(Conn conn, RedisDisconnectReason reason)
    {
        if (Interlocked.CompareExchange(ref _conn, null, conn) != conn)
            return;

        CloseConn(conn, reason);
    }

    private void CloseConn(Conn conn, RedisDisconnectReason reason)
    {
        // 파서는 수신 루프만 만진다(스레드 안전하지 않다). 여기서 건드리지 않고, 수신 루프가 끝날 때 스스로 비운다.
        SocketUtils.CloseSocket(conn.Socket);

        // SendCommand()의 "연결 확인 → 콜백 등록"과 여기의 "콜백 회수"를 같은 락으로 묶는다 — 어느 한쪽에서만 콜백이
        // 발견되어 정확히 한 번 호출된다. 락 안에서 콜백을 부르면 콜백이 이 객체를 다시 건드릴 때 교착 위험이 있으므로 락 밖에서 부른다.
        RedisCallback? pending;
        lock (_commandLock)
        {
            pending = conn.Pending;
            conn.Pending = null;
            Interlocked.Exchange(ref conn.CommandStartMs, 0);
            conn.SendQueue.Clear();     // 송신 중인 버퍼는 송신 루프가 쥐고 있어 여기서 반납하지 않는다
        }

        pending?.Invoke(RedisHelper.MakeError(ReasonToString(reason)));
    }

    private static string ReasonToString(RedisDisconnectReason reason) => reason switch
    {
        RedisDisconnectReason.ConnectionClosedRecv => RedisConst.ErrConnectionClosedRecv,
        RedisDisconnectReason.ConnectionClosedSend => RedisConst.ErrConnectionClosedSend,
        RedisDisconnectReason.SendFailed => RedisConst.ErrSendFailed,
        RedisDisconnectReason.ProtocolError => RedisConst.ErrProtocolError,
        RedisDisconnectReason.CommandTimeout => RedisConst.ErrCommandTimeout,
        _ => RedisConst.ErrConnectionClosed
    };

    // ===================================================================================
    // 송신
    // ===================================================================================

    /// <summary>
    /// 명령어를 RESP로 변환해 비동기 전송하고 콜백을 등록한다. 클래스 문서의 콜백 계약대로, 반환값과 무관하게 콜백은 정확히 한 번 호출된다.
    /// </summary>
    /// <returns>접수되어 전송 경로에 올랐는지(false여도 콜백은 이미 에러 값으로 호출되었다)</returns>
    public bool SendCommand(IReadOnlyList<string> args, RedisCallback? callback)
    {
        // 인코딩은 락 밖에서 한다(최대 512MB까지 커질 수 있어 Disconnect()를 막지 않도록).
        string? errorMsg = null;
        byte[]? buffer = null;
        int size = RedisCommandBuilder.CalcEncodedSize(args);
        if (size == 0)
        {
            errorMsg = RedisConst.ErrCommandTooLarge;
        }
        else
        {
            buffer = ArrayPool<byte>.Shared.Rent(size);
            RedisCommandBuilder.Encode(args, buffer);
        }

        Conn? pumpConn = null;
        if (errorMsg == null)
        {
            lock (_commandLock)
            {
                Conn? conn = Volatile.Read(ref _conn);
                if (conn == null)
                {
                    errorMsg = RedisConst.ErrConnectionClosed;
                }
                else if (conn.Pending != null)
                {
                    // "한 시점에 미완료 명령은 하나" 계약 위반. 기존 명령의 상태를 조용히 덮어쓰는 대신 바로 드러낸다.
                    LogManager.Error("RedisClient.SendCommand: 이미 미완료 명령이 있는 상태에서 재호출됨 — 계약 위반");
                    errorMsg = RedisConst.ErrCommandInFlight;
                }
                else
                {
                    conn.Pending = callback ?? s_noopCallback;
                    Interlocked.Exchange(ref conn.CommandStartMs, TimeUtil.MonotonicNowMillis());
                    conn.SendQueue.Enqueue(new SendItem(buffer!, size));

                    if (!conn.Sending)
                    {
                        conn.Sending = true;
                        pumpConn = conn;
                    }
                }
            }
        }

        if (errorMsg != null)
        {
            if (buffer != null)
                ArrayPool<byte>.Shared.Return(buffer);

            callback?.Invoke(RedisHelper.MakeError(errorMsg));
            return false;
        }

        if (pumpConn != null)
            _ = SendPumpAsync(pumpConn);

        return true;
    }

    // 송신 큐를 비울 때까지 하나씩 보낸다. 응답이 송신 완료보다 먼저 처리돼 풀이 커넥션을 바로 재대여하는 경우에도
    // 다음 명령은 큐에서 앞 명령 뒤에 이어 보낸다.
    private async Task SendPumpAsync(Conn conn)
    {
        while (true)
        {
            SendItem item;
            lock (_commandLock)
            {
                if (!ReferenceEquals(Volatile.Read(ref _conn), conn) || !conn.SendQueue.TryDequeue(out item))
                {
                    conn.Sending = false;
                    return;
                }
            }

            try
            {
                int sent = 0;
                while (sent < item.Length)
                {
                    int n = await conn.Socket.SendAsync(new ReadOnlyMemory<byte>(item.Buffer, sent, item.Length - sent), SocketFlags.None).ConfigureAwait(false);
                    if (n <= 0)
                        throw new SocketException((int)SocketError.ConnectionReset);

                    sent += n;
                }
            }
            catch (Exception)
            {
                ArrayPool<byte>.Shared.Return(item.Buffer);
                Disconnect(conn, RedisDisconnectReason.SendFailed);
                return;
            }

            ArrayPool<byte>.Shared.Return(item.Buffer);
        }
    }

    // ===================================================================================
    // 수신
    // ===================================================================================

    // 수신 → 파싱 → 콜백 루프. 파싱된 값을 먼저 다 모은 뒤 콜백을 하나씩 처리한다(콜백이 이 객체를 재진입해도 안전).
    private async Task ReceiveLoopAsync(Conn conn)
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(RedisConst.RecvBufferSize);
        var values = new List<RedisValue>();

        try
        {
            while (true)
            {
                int n;
                try
                {
                    n = await conn.Socket.ReceiveAsync(new Memory<byte>(buf), SocketFlags.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    Disconnect(conn, RedisDisconnectReason.ConnectionClosedRecv);
                    return;
                }

                if (n == 0)
                {
                    Disconnect(conn, RedisDisconnectReason.ConnectionClosedRecv);
                    return;
                }

                // Feed()가 상한 초과로 실패하면 스트림을 신뢰할 수 없으므로 프로토콜 오류로 취급해 연결을 끊는다.
                bool protocolError = !conn.Parser.Feed(new ReadOnlySpan<byte>(buf, 0, n));
                if (!protocolError)
                {
                    while (true)
                    {
                        RedisParseResult result = conn.Parser.TryParse(out RedisValue? value);
                        if (result == RedisParseResult.Success)
                        {
                            values.Add(value!);
                            continue;
                        }

                        if (result == RedisParseResult.ProtocolError)
                            protocolError = true;

                        break;  // NeedMoreData(정상) 또는 ProtocolError — 어느 쪽이든 이번 처리는 끝
                    }
                }

                foreach (RedisValue value in values)
                {
                    try
                    {
                        OnReceiptResponse(conn, value);
                    }
                    catch (Exception e)
                    {
                        // 콜백 예외로 수신 루프가 죽으면 이 연결의 응답이 영영 처리되지 않는다.
                        LogManager.Error("RedisClient: 응답 콜백에서 예외 발생 - {0}", e.Message);
                    }
                }
                values.Clear();

                if (protocolError)
                {
                    // 연결 끊김이 아니라 수신한 바이트를 RESP로 해석하지 못한 경우다 — 별도 사유로 구분한다.
                    Disconnect(conn, RedisDisconnectReason.ProtocolError);
                    return;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
            conn.Parser.Reset();        // 큰 응답으로 커진 누적 버퍼를 연결과 함께 놓는다
        }
    }

    private void OnReceiptResponse(Conn conn, RedisValue value)
    {
        // 구독 모드: ["message", ...] / ["pmessage", ...] 는 명령에 대한 응답이 아니라 서버가 보낸 push이므로
        // 대기 중인 명령 콜백을 건드리지 않고 push 콜백으로만 보낸다. 그렇지 않으면 메시지가 SUBSCRIBE 확인 응답보다
        // 먼저 도착했을 때 확인 응답용 콜백이 메시지에 소비된다.
        RedisPushCallback? push = _pushCallback;
        if (push != null && value.Type == RedisType.Array && value.ArrayVal.Length > 0)
        {
            string kind = value.ArrayVal[0].StrVal;
            if (kind == "message" || kind == "pmessage")
            {
                push(value);
                return;
            }
        }

        RedisCallback? callback;
        lock (_commandLock)
        {
            callback = conn.Pending;
            conn.Pending = null;
            Interlocked.Exchange(ref conn.CommandStartMs, 0);
        }

        callback?.Invoke(value);
    }

    // ===================================================================================
    // 접속 직후 명령 (AUTH / SELECT)
    // ===================================================================================

    private async Task<bool> AuthenticateAsync(string userId, string password, int timeoutMs)
    {
        string[] args = string.IsNullOrEmpty(userId) ? new[] { "AUTH", password } : new[] { "AUTH", userId, password };

        bool ok = await SendCommandAndWaitAsync(args, timeoutMs).ConfigureAwait(false);
        if (!ok)
            LogManager.Error("RedisClient.Authenticate: AUTH 실패(비밀번호 오류이거나 응답 없음)");

        return ok;
    }

    // 명령을 보내고 에러가 아닌 응답을 제한 시간 안에 받았는지 기다린다. SendCommand()는 반환값과 무관하게 콜백을
    // 정확히 한 번 호출하므로, 반환값으로 조기 판단하지 않고 항상 완료를 기다리면 충분하다.
    private async Task<bool> SendCommandAndWaitAsync(IReadOnlyList<string> args, int timeoutMs)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SendCommand(args, res => tcs.TrySetResult(res.Type != RedisType.Error));

        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
