//***************************************************************************
// ServerProbe.cs : 서버 목록의 각 서버에 TCP 로 접속해 보고 응답 시간(ms)을 잰다. 시작 화면의 서버 선택에 쓴다.
// 접속만 해 보고 바로 끊으며 패킷은 보내지 않는다. 결과는 다른 스레드가 채우므로 읽는 쪽은 Get() 으로 복사본을 받는다.
// 게임 중 핑(패킷 왕복)이 아니라 접속 가능 여부와 연결 시간이다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace GameViewer.Core.Net
{
    public sealed class ServerEntry
    {
        public string Name; public string Host; public int Port;
        public ServerEntry(string name, string host, int port) { Name = name; Host = host; Port = port; }
    }

    public enum ProbeState { Unknown, Probing, Online, Offline }

    public struct ProbeResult
    {
        public ProbeState State; public int Ms; public string Error;
    }

    public sealed class ServerProbe
    {
        public const int TimeoutMs = 1500;
        public const long RefreshMs = 10000;
        private readonly List<ServerEntry> _servers;
        private readonly ProbeResult[] _res;
        private readonly long[] _startedAt;
        private readonly object _lock = new object();
        private volatile bool _stopped;

        public ServerProbe(List<ServerEntry> servers)
        {
            _servers = servers;
            _res = new ProbeResult[servers.Count];
            _startedAt = new long[servers.Count];
            for (int i = 0; i < _startedAt.Length; ++i) _startedAt[i] = -RefreshMs - 1;
        }

        public int Count { get { return _servers.Count; } }
        public void Stop() { _stopped = true; }

        public ProbeResult Get(int i)
        {
            lock (_lock) return _res[i];
        }

        /// <summary>갱신 주기가 지난 서버만 다시 잰다(없으면 아무것도 안 함). 화면을 그리는 스레드에서 자주 불러도 된다.</summary>
        public void Tick(long now)
        {
            if (_stopped) return;
            for (int i = 0; i < _servers.Count; ++i)
            {
                if (now - _startedAt[i] < RefreshMs) continue;
                lock (_lock) { if (_res[i].State == ProbeState.Probing) continue; _res[i].State = ProbeState.Probing; }
                _startedAt[i] = now;
                int idx = i; ServerEntry e = _servers[i];
                ThreadPool.QueueUserWorkItem(delegate { Run(idx, e); });
            }
        }

        private void Run(int idx, ServerEntry e)
        {
            ProbeResult r = new ProbeResult();
            Socket s = null;
            try
            {
                // 이름 해석(DNS)은 연결 시간에 넣지 않는다: 주소를 먼저 구하고 그 뒤부터 잰다
                System.Net.IPAddress ip;
                if (!System.Net.IPAddress.TryParse(e.Host, out ip))
                {
                    System.Net.IPAddress[] all = System.Net.Dns.GetHostAddresses(e.Host);
                    ip = all.Length > 0 ? all[0] : null;
                    for (int i = 0; i < all.Length; ++i) if (all[i].AddressFamily == AddressFamily.InterNetwork) { ip = all[i]; break; }
                    if (ip == null) throw new SocketException((int)SocketError.HostNotFound);
                }
                s = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                s.Blocking = false;
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                try { s.Connect(new System.Net.IPEndPoint(ip, e.Port)); }
                catch (SocketException se) { if (se.SocketErrorCode != SocketError.WouldBlock && se.SocketErrorCode != SocketError.InProgress) throw; }
                if (!s.Connected)
                {
                    // 연결이 끝나면 쓰기 가능(성공) 또는 오류(실패, Windows 는 오류 목록)로 알려 준다. 폴링 간격 없이 바로 깨어난다.
                    List<Socket> w = new List<Socket> { s }, err = new List<Socket> { s };
                    Socket.Select(null, w, err, TimeoutMs * 1000);
                    if (err.Count > 0) { int code = (int)s.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error); throw new SocketException(code != 0 ? code : (int)SocketError.ConnectionRefused); }
                    if (w.Count == 0) { r.State = ProbeState.Offline; r.Error = "timeout"; lock (_lock) _res[idx] = r; return; }
                    int so = (int)s.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
                    if (so != 0) throw new SocketException(so);
                }
                r.State = ProbeState.Online; r.Ms = (int)sw.ElapsedMilliseconds;
            }
            catch (Exception ex) { r.State = ProbeState.Offline; r.Error = ex.Message; }
            finally { try { if (s != null) s.Close(); } catch { } }
            lock (_lock) _res[idx] = r;
        }

        /// <summary>응답 시간을 사람이 읽는 등급으로: 쾌적/보통/느림.</summary>
        public static string Grade(int ms) { return ms < 60 ? "쾌적" : (ms < 150 ? "보통" : "느림"); }
    }
}
