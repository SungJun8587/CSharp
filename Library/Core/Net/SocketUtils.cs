// SocketUtils.cs : implementation of the SocketUtils and NetAddress classes.
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Core.Net;

/// <summary>
/// 소켓 생성, 옵션 설정, 오류 분류 유틸리티.
/// 옵션 설정 함수는 예외 대신 bool(성공 여부)을 반환한다.
/// </summary>
public static class SocketUtils
{
    //****************************************************************
    // 소켓 생성
    //****************************************************************

    /// <summary>TCP 소켓을 생성한다.</summary>
    public static Socket CreateTcpSocket(AddressFamily family = AddressFamily.InterNetwork)
        => new Socket(family, SocketType.Stream, ProtocolType.Tcp);

    /// <summary>UDP 소켓을 생성한다.</summary>
    public static Socket CreateUdpSocket(AddressFamily family = AddressFamily.InterNetwork)
        => new Socket(family, SocketType.Dgram, ProtocolType.Udp);

    /// <summary>
    /// bind + listen 까지 마친 TCP 리슨 소켓을 생성한다.
    /// </summary>
    /// <param name="endPoint">바인드할 주소.</param>
    /// <param name="backlog">listen backlog.</param>
    /// <param name="reuseAddress">
    /// 주소 재사용 여부. Windows의 SO_REUSEADDR는 포트 탈취를 허용하므로 Windows에서는 적용하지 않는다.
    /// </param>
    /// <returns>성공 시 소켓, 실패 시 null.</returns>
    public static Socket? CreateListenSocket(IPEndPoint endPoint, int backlog, bool reuseAddress = true)
    {
        Socket? socket = null;
        try
        {
            socket = CreateTcpSocket(endPoint.AddressFamily);

            if (endPoint.AddressFamily == AddressFamily.InterNetworkV6 &&
                endPoint.Address.Equals(IPAddress.IPv6Any))
            {
                socket.DualMode = true;
            }

            if (reuseAddress && !OperatingSystem.IsWindows())
                SetReuseAddress(socket, true);

            socket.Bind(endPoint);
            socket.Listen(backlog);
            return socket;
        }
        catch (SocketException)
        {
            socket?.Dispose();
            return null;
        }
    }

    //****************************************************************
    // 소켓 옵션
    //****************************************************************

    /// <summary>TCP_NODELAY 설정.</summary>
    public static bool SetNoDelay(Socket socket, bool enable)
        => TrySet(() => socket.NoDelay = enable);

    /// <summary>SO_LINGER 설정. enable=true, seconds=0 이면 abortive close(RST).</summary>
    public static bool SetLinger(Socket socket, bool enable, int seconds)
        => TrySet(() => socket.LingerState = new LingerOption(enable, seconds));

    /// <summary>SO_REUSEADDR 설정.</summary>
    public static bool SetReuseAddress(Socket socket, bool enable)
        => TrySet(() => socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, enable));

    /// <summary>SO_SNDBUF 설정.</summary>
    public static bool SetSendBufferSize(Socket socket, int size)
        => TrySet(() => socket.SendBufferSize = size);

    /// <summary>SO_RCVBUF 설정.</summary>
    public static bool SetRecvBufferSize(Socket socket, int size)
        => TrySet(() => socket.ReceiveBufferSize = size);

    /// <summary>TCP KeepAlive 설정.</summary>
    /// <param name="socket">대상 소켓.</param>
    /// <param name="enable">KeepAlive 사용 여부.</param>
    /// <param name="timeSec">유휴 후 첫 프로브까지 대기 시간(초).</param>
    /// <param name="intervalSec">프로브 간격(초).</param>
    public static bool SetKeepAlive(Socket socket, bool enable, int timeSec, int intervalSec)
        => TrySet(() =>
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, enable);
            if (!enable)
                return;
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, timeSec);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, intervalSec);
        });

    //****************************************************************
    // 종료
    //****************************************************************

    /// <summary>
    /// 소켓을 안전하게 닫는다(null 허용, 중복 호출 허용).
    /// 진행 중인 비동기 작업은 OperationAborted 로 완료된다.
    /// </summary>
    public static void CloseSocket(Socket? socket)
    {
        if (socket == null)
            return;

        try { socket.Shutdown(SocketShutdown.Both); }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }

        try { socket.Close(); }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    //****************************************************************
    // 오류 분류
    //****************************************************************

    /// <summary>
    /// 상대방 종료/로컬 종료로 인해 흔히 발생하는 정상적인 끊김 오류인지 판별한다.
    /// </summary>
    public static bool IsNormalDisconnect(SocketError error)
    {
        switch (error)
        {
            case SocketError.ConnectionReset:
            case SocketError.ConnectionAborted:
            case SocketError.OperationAborted:
            case SocketError.Shutdown:
            case SocketError.NotConnected:
            case SocketError.Disconnecting:
            case SocketError.Interrupted:
            case SocketError.TimedOut:
                return true;
            default:
                return false;
        }
    }

    /// <summary>리슨 소켓이 닫혀서 Accept 가 중단된 오류인지 판별한다.</summary>
    public static bool IsListenClosed(SocketError error)
        => error == SocketError.OperationAborted
        || error == SocketError.Interrupted
        || error == SocketError.Shutdown
        || error == SocketError.InvalidArgument
        || error == SocketError.NotSocket;

    /// <summary>SocketError 를 로그용 한국어 문자열로 변환한다.</summary>
    public static string GetErrorText(SocketError error)
    {
        return error switch
        {
            SocketError.Success => "성공",
            SocketError.ConnectionReset => "상대방이 연결을 강제로 끊음",
            SocketError.ConnectionAborted => "연결이 중단됨",
            SocketError.ConnectionRefused => "연결이 거부됨",
            SocketError.TimedOut => "시간 초과",
            SocketError.HostUnreachable => "호스트에 도달할 수 없음",
            SocketError.NetworkUnreachable => "네트워크에 도달할 수 없음",
            SocketError.AddressAlreadyInUse => "주소가 이미 사용 중",
            SocketError.OperationAborted => "작업이 취소됨",
            SocketError.NotConnected => "연결되지 않은 소켓",
            SocketError.TooManyOpenSockets => "열 수 있는 소켓 수 초과",
            SocketError.NoBufferSpaceAvailable => "버퍼 공간 부족",
            _ => $"{error} ({(int)error})",
        };
    }

    private static bool TrySet(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (SocketException) { return false; }
        catch (ObjectDisposedException) { return false; }
        catch (PlatformNotSupportedException) { return false; }
    }
}

/// <summary>
/// IPEndPoint 래퍼. 호스트명/IP 문자열로부터 주소를 만든다.
/// </summary>
public readonly struct NetAddress
{
    /// <summary>실제 엔드포인트.</summary>
    public IPEndPoint EndPoint { get; }

    public NetAddress(IPEndPoint endPoint)
    {
        EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
    }

    /// <summary>모든 IPv4 인터페이스(0.0.0.0)의 지정 포트.</summary>
    public static NetAddress Any(int port) => new NetAddress(new IPEndPoint(IPAddress.Any, port));

    /// <summary>
    /// IP 문자열 또는 호스트명을 해석해 주소를 만든다. 호스트명이면 IPv4를 우선한다.
    /// </summary>
    public static bool TryCreate(string host, int port, out NetAddress address)
    {
        address = default;

        if (IPAddress.TryParse(host, out IPAddress? ip))
        {
            address = new NetAddress(new IPEndPoint(ip, port));
            return true;
        }

        try
        {
            IPAddress[] list = Dns.GetHostAddresses(host);
            IPAddress? pick = list.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                              ?? list.FirstOrDefault();
            if (pick == null)
                return false;

            address = new NetAddress(new IPEndPoint(pick, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public override string ToString() => EndPoint?.ToString() ?? string.Empty;

    public static implicit operator IPEndPoint(NetAddress address) => address.EndPoint;
}
