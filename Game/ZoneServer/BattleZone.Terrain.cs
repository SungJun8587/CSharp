// BattleZone.Terrain.cs : 시험장(존 0) 지형 치트. 클라이언트가 지형 번호(1~10)를 보내면 그 존의 장애물로 바꾸고 존 전원에게 알린다.
using System;
using Game.ZoneCommon;
using Game.ZoneCore;

namespace Game.ZoneServer;

public sealed partial class BattleZone
{
    private const ulong TerrainChangeCooldownMs = 500;      // 연타해도 파일을 매번 읽지 않게 한다

    private uint _cheatTerrain;             // 치트로 바꾼 지형(0이면 구동 때 정해진 지형 그대로)
    private ulong _lastTerrainChangeMs;

    /// <summary>
    /// 지형 번호를 받아 그 존의 장애물 맵을 만들어 돌려준다(없거나 읽지 못하면 null). 이 값을 정해 줄 때만 지형 치트가 켜진다.
    /// 시험장 서버(존 0)에만 연결한다. 구동 전에 정한다.
    /// </summary>
    public Func<uint, BlockMap?>? TerrainLoader;

    /// <summary>ZcTerrainReq 처리. 존 스레드에서 호출된다. 치트가 꺼져 있거나 값이 이상하면 조용히 무시한다.</summary>
    private void HandleTerrainReq(ZonePlayer player, ReadOnlySpan<byte> packet)
    {
        if (!ZcTerrainReq.TryRead(packet, out ZcTerrainReq req))
        {
            Reject(player, Limits.MalformedWeight);
            return;
        }

        Func<uint, BlockMap?>? loader = TerrainLoader;
        if (loader == null)
        {
            Core.Util.LogManager.Warning("[BattleZone] terrain cheat ignored: this zone has no terrain loader (only zone 0 enables it). zoneId={0}, charId={1}", ZoneId, player.Id);
            return;
        }

        ulong now = NowMs;
        if (now - _lastTerrainChangeMs < TerrainChangeCooldownMs && _lastTerrainChangeMs != 0)
            return;
        if (req.Terrain == 0)
            return;
        if (req.Terrain == _cheatTerrain)
        {
            SendPacket(player, new ZcTerrainNotify { Terrain = _cheatTerrain });       // 이미 그 지형이다. 요청자의 화면만 맞춰 준다
            return;
        }

        BlockMap? map = loader(req.Terrain);
        if (map == null)
        {
            Core.Util.LogManager.Warning("[BattleZone] terrain cheat: load failed. zoneId={0}, terrain={1}", ZoneId, req.Terrain);
            return;
        }

        _lastTerrainChangeMs = now;
        _cheatTerrain = req.Terrain;
        int moved = ReplaceBlockMap(map);
        Core.Util.LogManager.Info("[BattleZone] terrain cheat: zoneId={0}, terrain={1}, charId={2}, moved={3}", ZoneId, req.Terrain, player.Id, moved);

        ZcTerrainNotify notify = new ZcTerrainNotify { Terrain = req.Terrain };
        foreach (ZonePlayer p in _sessionToPlayer.Values)
            SendPacket(p, notify);
    }

    /// <summary>치트로 지형이 바뀐 상태라면 새로 입장한 플레이어에게 현재 지형을 알린다(클라이언트가 GameData 의 기본 지형과 달라지지 않게).</summary>
    private void SendTerrainIfChanged(ZonePlayer player)
    {
        if (_cheatTerrain != 0)
            SendPacket(player, new ZcTerrainNotify { Terrain = _cheatTerrain });
    }
}
