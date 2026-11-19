//***************************************************************************
// GatewayClient.cs : Gateway 클라이언트 포트에 붙는 TCP 클라이언트 (BotClient 와 같은 프로토콜).
// 연결/수신/송신은 라이브러리 Core.Net(ClientService + PacketSession)이 맡는다. 패킷 경계는 헤더로 잘라 큐에 넣고,
// 소비는 호출자(UI/게임 루프) 스레드가 TryDequeue 로 한다. 그래서 월드 상태는 한 스레드에서만 만져진다.
//***************************************************************************
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GameViewer.Core.Protocol;
using CloseReason = global::Core.Net.CloseReason;
using NetAddress = global::Core.Net.NetAddress;
using NetServiceOptions = global::Core.Net.NetServiceOptions;
using PacketSession = global::Core.Net.PacketSession;
using Session = global::Core.Net.Session;
using ClientService = global::Core.Net.ClientService;

namespace GameViewer.Core.Net
{
    public sealed class GatewayClient : IDisposable
    {
        /// <summary>접속 한 건 분량의 서비스 옵션: 세션 1개, 소켓 KeepAlive 로 끊긴 연결을 감지한다.</summary>
        private static readonly NetServiceOptions ServiceOptions = new NetServiceOptions { MaxSessionCount = 1 };

        private readonly ConcurrentQueue<Frame> _queue = new ConcurrentQueue<Frame>();
        private readonly object _sync = new object();
        private ClientService _service;
        private volatile Link _link;

        public bool Connected { get { Link l = _link; return l != null && l.IsConnected; } }
        /// <summary>마지막 연결이 끊긴 사유. 내가 닫았거나 아직 끊기지 않았으면 null.</summary>
        public string DisconnectReason { get { Link l = _link; return l == null ? null : l.Reason; } }
        public long BytesIn { get { Link l = _link; return l == null ? 0 : l.BytesReceived; } }
        public long BytesOut { get { Link l = _link; return l == null ? 0 : l.BytesSent; } }

        /// <summary>연결한다. 실패하면 false 와 사유(error)를 돌려준다. 호출 스레드를 timeoutMs 까지 막으므로 UI 스레드에서 부르지 않는다.</summary>
        public bool Connect(string host, int port, int timeoutMs, out string error)
        {
            error = null;
            Close();

            using (CancellationTokenSource cts = new CancellationTokenSource(timeoutMs))
            {
                ClientService service = null;
                try
                {
                    // 호출 스레드는 동기 대기 전용이므로 동기화 컨텍스트가 없고, 대기 중 교착 위험이 없다.
                    var resolved = NetAddress.TryCreateAsync(host, port, timeoutMs).GetAwaiter().GetResult();
                    if (!resolved.Ok) { error = "cannot resolve " + host + ":" + port; return false; }

                    service = new ClientService(resolved.Address.EndPoint, () => new Link(this), ServiceOptions);
                    Link link = (Link)service.ConnectAsync(cts.Token).GetAwaiter().GetResult();
                    lock (_sync) { _service = service; _link = link; }
                    return true;
                }
                catch (OperationCanceledException)
                {
                    error = "connect timeout (" + host + ":" + port + ")";
                }
                catch (SocketException e)
                {
                    error = e.Message;
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                if (service != null) service.CloseAsync(200).ContinueWith(t => { _ = t.Exception; });
                return false;
            }
        }

        public bool TryDequeue(out Frame frame) { return _queue.TryDequeue(out frame); }

        /// <summary>패킷 한 개를 보낸다. 연결이 끊겼으면 false.</summary>
        public bool Send(byte[] packet)
        {
            Link l = _link;
            return l != null && l.Send(packet);
        }

        public void Close()
        {
            ClientService service; Link link;
            lock (_sync) { service = _service; link = _link; _service = null; }
            if (link != null) link.Disconnect(CloseReason.Local);
            if (service != null) service.CloseAsync(500).ContinueWith(t => { _ = t.Exception; });
        }

        public void Dispose() { Close(); }

        /// <summary>Core.Net 세션: 완성된 패킷을 Frame 으로 복사해 큐에 넣는다.</summary>
        private sealed class Link : PacketSession
        {
            private const int RecvUnitBytes = 1024;      // 수신 버퍼 용량 16KB >= 패킷 상한
            private readonly GatewayClient _owner;
            private volatile string _reason;
            public Link(GatewayClient owner) : base(RecvUnitBytes) { _owner = owner; }

            public string Reason { get { return _reason; } }

            protected override int MaxRecvPacketSize { get { return ProtocolConst.MaxClientPacketBytes; } }

            protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
            {
                if (_owner._link != this && _owner._link != null) return;     // 교체된 이전 연결의 늦은 패킷 (_link 가 아직 비어 있으면 방금 맺은 연결)
                _owner._queue.Enqueue(new Frame(global::Core.Net.PacketHeader.ReadId(packet), packet.ToArray()));
            }

            protected override void OnDisconnected(CloseReason reason)
            {
                switch (reason)
                {
                    case CloseReason.Local: break;
                    case CloseReason.RemoteClosed: _reason = "server closed the connection"; break;
                    case CloseReason.ProtocolError: _reason = "protocol error: invalid packet"; break;
                    default: _reason = reason.ToString(); break;
                }
            }
        }
    }
}
