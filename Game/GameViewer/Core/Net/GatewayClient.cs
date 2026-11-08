//***************************************************************************
// GatewayClient.cs : Gateway 클라이언트 포트에 붙는 TCP 클라이언트 (BotClient 와 같은 프로토콜).
// 수신은 전용 스레드가 패킷 단위로 잘라 큐에 넣고, 소비는 호출자(UI/게임 루프) 스레드가 TryDequeue 로 한다.
// 그래서 월드 상태는 한 스레드에서만 만져진다. Unity 에서는 Update() 에서 TryDequeue 를 돌리면 된다.
//***************************************************************************
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using GameViewer.Core.Protocol;

namespace GameViewer.Core.Net
{
    public sealed class GatewayClient : IDisposable
    {
        private TcpClient _tcp;
        private NetworkStream _stream;
        private Thread _recvThread;
        private readonly object _sendLock = new object();
        private readonly ConcurrentQueue<Frame> _queue = new ConcurrentQueue<Frame>();
        private volatile bool _connected;
        private volatile string _disconnectReason;
        private long _bytesIn, _bytesOut;

        public bool Connected { get { return _connected; } }
        public string DisconnectReason { get { return _disconnectReason; } }
        public long BytesIn { get { return Interlocked.Read(ref _bytesIn); } }
        public long BytesOut { get { return Interlocked.Read(ref _bytesOut); } }

        /// <summary>연결한다. 실패하면 false 와 사유(error)를 돌려준다. 호출 스레드를 timeoutMs 까지 막는다.</summary>
        public bool Connect(string host, int port, int timeoutMs, out string error)
        {
            error = null;
            try
            {
                _tcp = new TcpClient();
                _tcp.NoDelay = true;
                IAsyncResult ar = _tcp.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    try { _tcp.Close(); } catch { }
                    error = "connect timeout (" + host + ":" + port + ")";
                    return false;
                }
                _tcp.EndConnect(ar);
                _stream = _tcp.GetStream();
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }

            _connected = true;
            _recvThread = new Thread(RecvLoop);
            _recvThread.IsBackground = true;
            _recvThread.Name = "GatewayClient.Recv";
            _recvThread.Start();
            return true;
        }

        private void RecvLoop()
        {
            byte[] buf = new byte[8192];
            PacketFramer framer = new PacketFramer(ProtocolConst.MaxClientPacketBytes);
            List<Frame> frames = new List<Frame>();
            try
            {
                while (_connected)
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n <= 0) { _disconnectReason = "server closed the connection"; break; }
                    Interlocked.Add(ref _bytesIn, n);
                    frames.Clear();
                    framer.Push(buf, 0, n, frames);
                    for (int i = 0; i < frames.Count; ++i) _queue.Enqueue(frames[i]);
                }
            }
            catch (PacketFormatException e)
            {
                _disconnectReason = "protocol error: " + e.Message;
            }
            catch (Exception e)
            {
                if (_connected) _disconnectReason = e.Message;
            }
            _connected = false;
        }

        public bool TryDequeue(out Frame frame) { return _queue.TryDequeue(out frame); }

        /// <summary>패킷 한 개를 보낸다. 연결이 끊겼으면 false.</summary>
        public bool Send(byte[] packet)
        {
            if (!_connected) return false;
            try
            {
                lock (_sendLock)
                {
                    _stream.Write(packet, 0, packet.Length);
                }
                Interlocked.Add(ref _bytesOut, packet.Length);
                return true;
            }
            catch (Exception e)
            {
                _disconnectReason = e.Message;
                _connected = false;
                return false;
            }
        }

        public void Close()
        {
            _connected = false;
            try { if (_tcp != null) _tcp.Close(); } catch { }
        }

        public void Dispose() { Close(); }
    }
}
