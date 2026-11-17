// GatewayServerConfig.cs : Gateway 실행 옵션과 설정 파일(gatewayserver_config.json).
using Core.Util;
using Game.Generated;
using Game.ZoneCommon;

namespace Game.GatewayServer;

/// <summary>Gateway 실행 옵션. 설정 파일 값 위에 명령행 인자가 덮어쓴다.</summary>
public sealed class GatewayOptions
{
    public string ClientBindIp = "0.0.0.0";
    public ushort ClientPort = 7000;
    public int MaxClientSessions = 5000;
    public string ZoneBindIp = "127.0.0.1";
    public ushort ZonePort = 7100;
    public int MaxZoneSessions = 64;
    public uint HeartbeatIntervalMs = ProtocolConst.DefaultHeartbeatIntervalMs;    // 존 서버에 지시하는 하트비트 주기
    public uint HeartbeatTimeoutMs = ProtocolConst.DefaultHeartbeatTimeoutMs;      // 이 시간 동안 하트비트가 없으면 연결을 끊음
    public uint ClientMaxPacketsPerSec = 120;               // 클라이언트 세션당 초당 수신 패킷 상한. 넘으면 연결을 끊는다. 0이면 제한 없음. 존 서버의 종류별 빈도 제한(RequestGuard) 합계보다 커야 한다
    public uint ClientMaxSendQueueBytes = 1024 * 1024;     // 클라이언트 세션당 송신 대기 한도. 넘으면 끊는다(응답 없는 클라이언트가 메모리를 쌓지 못하게). 0이면 무제한
}

/// <summary>gatewayserver_config.json 읽기/쓰기. C++ 설정 파일과 같은 키를 쓴다.</summary>
public sealed class GatewayServerConfig : Game.ZoneCommon.IServerConfig<GatewayOptions>
{
    public string ServerName = "GatewayServer";
    public int WorkerThreadCnt;                 // 쓰지 않는다(소켓 I/O는 .NET 스레드 풀). 키 호환용
    public int ZoneWorkerThreadCnt = 2;         // 위와 같다
    public readonly GatewayOptions Defaults = new GatewayOptions();

    public bool Init(string path)
    {
        JsonConfigReader? r = JsonConfigReader.Load(path);
        if (r == null)
            return false;

        r.Ignore("ServiceName", "DisplayName", "GroupId", "ChannelId", "KeepAliveSec", "ServerNode", "DBNode", "RedisNode");

        GatewayOptions o = Defaults;
        uint clientPort = o.ClientPort;
        uint zonePort = o.ZonePort;
        r.ReadString("Name", ref ServerName);
        r.ReadString("IP", ref o.ClientBindIp);
        r.ReadUInt("Port", ref clientPort, 1, 65535);
        r.ReadInt("MaxSessionCount", ref o.MaxClientSessions, 1, int.MaxValue);
        r.ReadInt("WorkerThreadCnt", ref WorkerThreadCnt, 0, 1024);
        r.ReadString("ZoneIP", ref o.ZoneBindIp);
        r.ReadUInt("ZonePort", ref zonePort, 1, 65535);
        r.ReadInt("ZoneMaxSessionCount", ref o.MaxZoneSessions, 1, int.MaxValue);
        r.ReadInt("ZoneWorkerThreadCnt", ref ZoneWorkerThreadCnt, 0, 1024);
        r.ReadUInt("HeartbeatIntervalMs", ref o.HeartbeatIntervalMs, 1, uint.MaxValue);
        r.ReadUInt("HeartbeatTimeoutMs", ref o.HeartbeatTimeoutMs, 1, uint.MaxValue);
        r.ReadUInt("ClientMaxPacketsPerSec", ref o.ClientMaxPacketsPerSec, 0, uint.MaxValue);
        r.ReadUInt("ClientMaxSendQueueBytes", ref o.ClientMaxSendQueueBytes, 0, uint.MaxValue);
        o.ClientPort = (ushort)clientPort;
        o.ZonePort = (ushort)zonePort;
        return r.Validate();
    }

    public bool Save(string path)
    {
        JsonConfigWriter? w = JsonConfigWriter.Open(path);
        if (w == null)
            return false;

        GatewayOptions o = Defaults;
        w.Write("Name", ServerName);
        w.Write("IP", o.ClientBindIp);
        w.Write("Port", (uint)o.ClientPort);
        w.Write("MaxSessionCount", o.MaxClientSessions);
        w.Write("WorkerThreadCnt", WorkerThreadCnt);
        w.Write("ZoneIP", o.ZoneBindIp);
        w.Write("ZonePort", (uint)o.ZonePort);
        w.Write("ZoneMaxSessionCount", o.MaxZoneSessions);
        w.Write("ZoneWorkerThreadCnt", ZoneWorkerThreadCnt);
        w.Write("HeartbeatIntervalMs", o.HeartbeatIntervalMs);
        w.Write("HeartbeatTimeoutMs", o.HeartbeatTimeoutMs);
        w.Write("ClientMaxPacketsPerSec", o.ClientMaxPacketsPerSec);
        w.Write("ClientMaxSendQueueBytes", o.ClientMaxSendQueueBytes);
        return w.Save(path);
    }

    public void PrintInfo()
    {
        GatewayOptions o = Defaults;
        LogManager.Info("--------------- [Gateway Server Setting] ---------------");
        LogManager.Info("ServerName : {0}", ServerName);
        LogManager.Info("Client : {0}:{1}, MaxSessionCount : {2}", o.ClientBindIp, o.ClientPort, o.MaxClientSessions);
        LogManager.Info("Zone : {0}:{1}, MaxSessionCount : {2}", o.ZoneBindIp, o.ZonePort, o.MaxZoneSessions);
        LogManager.Info("HeartbeatIntervalMs : {0}, HeartbeatTimeoutMs : {1}", o.HeartbeatIntervalMs, o.HeartbeatTimeoutMs);
        LogManager.Info("ClientMaxPacketsPerSec : {0}, ClientMaxSendQueueBytes : {1}", o.ClientMaxPacketsPerSec, o.ClientMaxSendQueueBytes);
    }

    public GatewayOptions ToOptions() => Defaults;
}
