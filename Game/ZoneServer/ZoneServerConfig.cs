// ZoneServerConfig.cs : 존 서버 실행 옵션과 설정 파일(zoneserver_config.json).
using System.Collections.Generic;
using Core.Util;
using Game.Protocol;
using Game.ServerCommon;
using Game.ZoneCore;

namespace Game.ZoneServer;

/// <summary>존 서버 실행 옵션. 설정 파일 값 위에 명령행 인자가 덮어쓴다.</summary>
public sealed class ZoneServerOptions
{
    public uint ZoneId = 1;                         // 이 프로세스가 호스팅하는 존 ID (Gateway 전체에서 유일)
    public ZoneKind Kind = ZoneKind.Field;
    public uint Capacity = 500;                     // 최대 수용 인원 (Gateway가 이 값으로 입장을 제한)
    public string ServerName = "ZoneServer";
    public string GatewayIp = "127.0.0.1";          // Gateway의 존 서버용 포트 주소
    public ushort GatewayPort = 7100;
    public uint TickIntervalMs = 50;
    public float MinX = 0.0f;
    public float MinY = 0.0f;
    public float MaxX = 1000.0f;
    public float MaxY = 1000.0f;
    public float CellSize = 50.0f;                  // AOI 셀 크기 (최소 보장 시야 반경 이상)
    public float SpawnX = 100.0f;
    public float SpawnY = 100.0f;
    public uint RespawnDelayMs = 5000;
    public bool AllowPvp = true;
    public uint RespawnInvulnMs = 3000;
    public string GameDataDir = "GameData";         // 비어 있으면 데이터 없이 구동(몬스터 없음, 영웅은 내장 기본 영웅 하나)
    public uint MonsterThinkIntervalMs = 100;
    public uint MaxMonsterCount = (uint)MonsterData.MaxSpawnPerZone;
    public uint MetricsIntervalMs = 30000;          // 메트릭 로그 주기(ms). 0이면 남기지 않는다
    public bool RequestGuardEnabled = true;         // 클라이언트 요청 빈도 제한과 위반 추방
}

/// <summary>zoneserver_config.json 읽기/쓰기. C++ 설정 파일과 같은 키를 쓴다.</summary>
public sealed class ZoneServerConfig : Game.ServerCommon.IServerConfig<ZoneServerOptions>
{
    public string ServerName = "ZoneServer";
    public int WorkerThreadCnt = 1;                 // C# 포팅에서는 쓰지 않는다(소켓 I/O는 .NET 스레드 풀). 키 호환용
    public readonly List<ServerNode> ServerNodes = new List<ServerNode>();
    public readonly ZoneServerOptions Defaults = new ZoneServerOptions();

    public ZoneServerConfig()
    {
        ServerName = Defaults.ServerName;
    }

    public bool Init(string path)
    {
        JsonConfigReader? r = JsonConfigReader.Load(path);
        if (r == null)
            return false;

        r.Ignore("ServiceName", "DisplayName", "GroupId", "ChannelId", "IP", "Port", "KeepAliveSec", "MaxSessionCount", "DBNode", "RedisNode");

        ZoneServerOptions o = Defaults;
        uint kind = (uint)o.Kind;

        r.ReadString("Name", ref ServerName);
        r.ReadInt("WorkerThreadCnt", ref WorkerThreadCnt, 1, 1024);
        r.ReadServerNodes("ServerNode", ServerNodes);
        r.ReadUInt("ZoneId", ref o.ZoneId, 0, uint.MaxValue);
        r.ReadUInt("ZoneKind", ref kind, (uint)ZoneKind.Field, (uint)ZoneKind.Dungeon);
        r.ReadUInt("Capacity", ref o.Capacity, 1, uint.MaxValue);
        r.ReadUInt("TickIntervalMs", ref o.TickIntervalMs, 1, uint.MaxValue);
        r.ReadFloat("MinX", ref o.MinX);
        r.ReadFloat("MinY", ref o.MinY);
        r.ReadFloat("MaxX", ref o.MaxX);
        r.ReadFloat("MaxY", ref o.MaxY);
        r.ReadFloat("CellSize", ref o.CellSize);
        r.ReadFloat("SpawnX", ref o.SpawnX);
        r.ReadFloat("SpawnY", ref o.SpawnY);
        r.ReadUInt("RespawnDelayMs", ref o.RespawnDelayMs, 0, uint.MaxValue - 1);   // uint.MaxValue는 "리스폰 안 함" 예약값
        r.ReadUInt("RespawnInvulnMs", ref o.RespawnInvulnMs, 0, uint.MaxValue);
        r.ReadBool("AllowPvp", ref o.AllowPvp);
        r.ReadString("GameDataDir", ref o.GameDataDir);
        r.ReadUInt("MonsterThinkIntervalMs", ref o.MonsterThinkIntervalMs, 1, 60000);
        r.ReadUInt("MaxMonsterCount", ref o.MaxMonsterCount, 1, (uint)MonsterData.MaxSpawnPerZone);
        r.ReadUInt("MetricsIntervalMs", ref o.MetricsIntervalMs, 0, 3600000);
        r.ReadBool("RequestGuardEnabled", ref o.RequestGuardEnabled);

        o.Kind = (ZoneKind)kind;
        return r.Validate();
    }

    public bool Save(string path)
    {
        JsonConfigWriter? w = JsonConfigWriter.Open(path);
        if (w == null)
            return false;

        ZoneServerOptions o = Defaults;
        w.Write("Name", ServerName);
        w.Write("WorkerThreadCnt", WorkerThreadCnt);
        w.WriteServerNodes("ServerNode", ServerNodes);
        w.Write("ZoneId", o.ZoneId);
        w.Write("ZoneKind", (uint)o.Kind);
        w.Write("Capacity", o.Capacity);
        w.Write("TickIntervalMs", o.TickIntervalMs);
        w.Write("MinX", o.MinX);
        w.Write("MinY", o.MinY);
        w.Write("MaxX", o.MaxX);
        w.Write("MaxY", o.MaxY);
        w.Write("CellSize", o.CellSize);
        w.Write("SpawnX", o.SpawnX);
        w.Write("SpawnY", o.SpawnY);
        w.Write("RespawnDelayMs", o.RespawnDelayMs);
        w.Write("RespawnInvulnMs", o.RespawnInvulnMs);
        w.Write("AllowPvp", o.AllowPvp);
        w.Write("GameDataDir", o.GameDataDir);
        w.Write("MonsterThinkIntervalMs", o.MonsterThinkIntervalMs);
        w.Write("MaxMonsterCount", o.MaxMonsterCount);
        w.Write("MetricsIntervalMs", o.MetricsIntervalMs);
        w.Write("RequestGuardEnabled", o.RequestGuardEnabled);
        return w.Save(path);
    }

    public void PrintInfo()
    {
        ZoneServerOptions o = Defaults;
        LogManager.Info("--------------- [Zone Server Setting] ---------------");
        LogManager.Info("ServerName : {0}", ServerName);
        LogManager.Info("ZoneId : {0}, ZoneKind : {1}, Capacity : {2}, TickIntervalMs : {3}", o.ZoneId, (uint)o.Kind, o.Capacity, o.TickIntervalMs);
        LogManager.Info("Bounds : ({0}, {1}) - ({2}, {3}), CellSize : {4}, Spawn : ({5}, {6})", o.MinX, o.MinY, o.MaxX, o.MaxY, o.CellSize, o.SpawnX, o.SpawnY);
        LogManager.Info("RespawnDelayMs : {0}, RespawnInvulnMs : {1}, AllowPvp : {2}", o.RespawnDelayMs, o.RespawnInvulnMs, o.AllowPvp);
        LogManager.Info("GameDataDir : {0}, MonsterThinkIntervalMs : {1}, MaxMonsterCount : {2}", o.GameDataDir, o.MonsterThinkIntervalMs, o.MaxMonsterCount);
        LogManager.Info("MetricsIntervalMs : {0}, RequestGuardEnabled : {1}", o.MetricsIntervalMs, o.RequestGuardEnabled);
        foreach (ServerNode n in ServerNodes)
            LogManager.Info("ServerNode ID={0}, Name={1}, {2}:{3}", n.Id, n.Name, n.Ip, n.Port);
    }

    /// <summary>설정 파일 값을 실행 옵션으로 옮긴다. ServerNode가 있으면 첫 노드가 Gateway 주소다.</summary>
    public ZoneServerOptions ToOptions()
    {
        ZoneServerOptions o = Defaults;
        o.ServerName = ServerName;
        if (ServerNodes.Count > 0)
        {
            o.GatewayIp = ServerNodes[0].Ip;
            o.GatewayPort = (ushort)ServerNodes[0].Port;
        }
        return o;
    }
}
