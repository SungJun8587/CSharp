// BattleZone.cs : 전투 존. FieldZone에 입장/퇴장, 클라이언트 패킷 처리, 통지 패킷 송신을 얹는다.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Core.Util;
using Game.Generated;
using Game.ZoneCommon;
using Game.ZoneCore;
using ProtoCancel = Game.Generated.SkillCancelReason;
using CoreCancel = Game.ZoneCore.SkillCancelReason;

namespace Game.ZoneServer;

/// <summary>
/// 존 스레드 하나가 이 객체의 모든 상태를 다룬다. 외부 스레드는 Post()로 HandleXxx를 넘긴다.
/// 클라이언트에게 가는 모든 패킷은 존 스레드에서 만들어져 <see cref="IZoneLink"/>로 나간다.
/// </summary>
public sealed partial class BattleZone : FieldZone<ZoneEntity>
{
    private readonly IZoneLink _link;
    private readonly Vec2 _spawnPos;
    private readonly HeroData _heroes;
    private readonly MonsterSystem _monsters;
    private readonly BossSystem? _bosses;
    private readonly DropSystem _drops;
    private readonly Dictionary<ulong, ZonePlayer> _sessionToPlayer = new Dictionary<ulong, ZonePlayer>();
    private readonly List<ulong> _recipients = new List<ulong>(256);     // 브로드캐스트 수신자 세션 ID 모음 (재사용으로 힙 할당 방지)
    private readonly Action<ZoneEntity> _addRecipientFn;
    private readonly Action<ZoneEntity> _combatVisitFn;
    private ZoneEntity? _combatOther;
    private bool _combatOtherVisited;
    private int _playerCount;

    // 존 이동/재접속 상태 유지: Gateway가 보관했다가 입장 직전에 돌려주는 상태를 입장 때 적용한다.
    private const long RestoreTtlMs = 15000;                // 복원 상태가 입장을 기다리는 최대 시간
    private const long RestoreSweepIntervalMs = 1000;       // 만료된 복원 상태를 청소하는 최소 간격
    private long _nextRestoreSweepMs;
    private const float GateReach = 6.0f;                   // 관문 안쪽 가장자리로 인정하는 깊이(뷰어는 3에서 반응한다. 지연을 감안해 넉넉히)
    private readonly Dictionary<ulong, PendingRestore> _pendingRestores = new Dictionary<ulong, PendingRestore>();
    private List<ZoneGateLink> _gates = new List<ZoneGateLink>();
    private float _minX, _minY, _maxX = 1000.0f, _maxY = 1000.0f;

    private readonly struct PendingRestore
    {
        public readonly PlayerState State;
        public readonly long ExpireMs;
        public PendingRestore(PlayerState state, long expireMs) { State = state; ExpireMs = expireMs; }
    }

    public BattleZone(FieldZoneConfig config, IZoneLink link, Vec2 spawnPos, HeroData? heroData, MonsterData? monsterData,
        MonsterSystemConfig monsterConfig, BossData? bossData = null, DropData? dropData = null)
        : base(config)
    {
        _link = link;
        _spawnPos = spawnPos;
        _heroes = heroData ?? HeroData.MakeDefault();
        _monsters = new MonsterSystem(this, monsterData, monsterConfig);
        _monsters.SetSkillData(_heroes);        // 몬스터 스킬(Monster 시트의 SkillId)은 영웅과 같은 스킬 시트를 쓴다
        if (bossData != null)
        {
            _bosses = new BossSystem(this, _monsters, bossData, _heroes, monsterData);
            _monsters.SetBossSystem(_bosses);
        }

        _drops = new DropSystem(this, dropData);
        _addRecipientFn = AddRecipient;
        _combatVisitFn = CombatVisit;
    }

    /// <summary>이 존에서 나가는 관문과 존 영역을 정한다. 구동 전에 부른다. 관문이 없으면 존 이동 상태 유지는 체력/가방만 한다.</summary>
    public void SetGateLinks(List<ZoneGateLink> gates, float minX, float minY, float maxX, float maxY)
    {
        _gates = gates;
        _minX = minX; _minY = minY; _maxX = maxX; _maxY = maxY;
    }

    /// <summary>
    /// Gateway가 보낸 보관 상태를 받아 둔다(존 스레드). 같은 캐릭터의 다음 입장 한 번에만 쓰이고, RestoreTtlMs 안에 입장하지 않으면 버린다.
    /// 입장 요청(ZoneEnterReq)보다 먼저 도착한다(같은 연결, 같은 입력 큐 순서).
    /// </summary>
    public void HandleRestore(PlayerState state)
    {
        long now = Environment.TickCount64;
        if (_pendingRestores.Count != 0 && now >= _nextRestoreSweepMs)       // 만료 청소는 1초에 한 번만(입장이 몰릴 때 매번 전체를 훑지 않는다)
        {
            _nextRestoreSweepMs = now + RestoreSweepIntervalMs;
            List<ulong>? expired = null;
            foreach (KeyValuePair<ulong, PendingRestore> kv in _pendingRestores)
                if (kv.Value.ExpireMs <= now)
                    (expired ??= new List<ulong>()).Add(kv.Key);
            if (expired != null)
                foreach (ulong id in expired)
                    _pendingRestores.Remove(id);
        }
        _pendingRestores[state.CharId] = new PendingRestore(state, now + RestoreTtlMs);
    }

    /// <summary>입장하는 캐릭터의 복원 상태를 꺼낸다(없거나 만료됐으면 null).</summary>
    private PlayerState? TakeRestore(ulong charId)
    {
        if (!_pendingRestores.Remove(charId, out PendingRestore r))
            return null;
        return r.ExpireMs > Environment.TickCount64 ? r.State : null;
    }

    /// <summary>관문으로 들어온 도착 지점이 유효한지(이 존, 영역 안, 장애물 위가 아님)를 보고 시작 위치를 정한다.</summary>
    private Vec2 ResolveStartPos(PlayerState? restore)
    {
        if (restore == null || restore.ArrivalZone != ZoneId)
            return _spawnPos;
        float x = Math.Clamp(restore.ArrivalX, _minX + 1.0f, _maxX - 1.0f);
        float y = Math.Clamp(restore.ArrivalY, _minY + 1.0f, _maxY - 1.0f);
        Vec2 p = new Vec2(x, y);
        BlockMap? bm = BlockMap;
        return bm != null && bm.IsBlocked(p) ? _spawnPos : p;
    }

    /// <summary>입장 정원. 0이면 제한 없음. 정원은 Gateway가 먼저 막지만, 존도 자기 정원을 넘는 입장은 거부한다. 구동 전에 정한다.</summary>
    public uint Capacity;

    /// <summary>요청 빈도 제한을 쓸지. 구동 전에 정한다. 끄면 위반 점수도 쌓지 않는다(테스트/부하 측정용).</summary>
    public bool GuardEnabled = true;

    /// <summary>요청 빈도 제한 규칙. 구동 전에 조정한다.</summary>
    public RequestLimits Limits { get; } = new RequestLimits();

    /// <summary>송수신/위반 카운터.</summary>
    public ZoneMetrics Metrics { get; } = new ZoneMetrics();

    /// <summary>스폰된 몬스터 수 (관측용).</summary>
    public int MonsterCount => _monsters.MonsterCount;

    /// <summary>드롭/지면 아이템 시스템 (현황 조회와 GM 드롭용).</summary>
    public DropSystem Drops => _drops;

    /// <summary>현재 플레이어 수. 하트비트 스레드가 읽으므로 원자적으로 갱신한다.</summary>
    public uint PlayerCount => (uint)Volatile.Read(ref _playerCount);

    /// <summary>입장 위치.</summary>
    public Vec2 SpawnPos => _spawnPos;

    // ------------------------------------------------------------------------------------------------
    // 구동 전 검사/배치
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// 영웅/몬스터/보스 데이터가 이 존 설정(영역, AOI 시야, 사거리)과 맞는지 검사한다. Start() 전에 호출한다.
    /// 입장 위치가 영역 안인지, 영웅 공격/스킬 사거리가 AOI 보장 시야 안인지, 몬스터/보스 데이터가 유효한지를 본다.
    /// </summary>
    public bool ValidateMonsters(out string error)
    {
        if (!_heroes.Validate(out error))
            return false;
        if (!IsInBounds(_spawnPos))
        {
            error = "player spawn position is outside the zone bounds";
            return false;
        }
        if (IsBlocked(_spawnPos))
        {
            error = "player spawn position is inside an obstacle";
            return false;
        }

        float monsterSpeed = _monsters.GetMaxSpawnSpeed();
        // 이동속도 버프를 받은 영웅이 낼 수 있는 최고 속도로 검사한다(위치 허용 오차가 속도에 비례해 늘어난다).
        float buffMul = 1.0f + _heroes.GetMaxSpeedBuffPercent() * 0.01f;
        float viewRange = GetGuaranteedViewRange();

        foreach (KeyValuePair<uint, HeroTemplate> pair in _heroes.Heroes)
        {
            HeroTemplate hero = pair.Value;
            float reachSpeed = Math.Max(hero.Speed * buffMul, monsterSpeed);
            if (!IsAttackRangeSupported(hero.Stats.AttackRange, reachSpeed))
            {
                error = $"hero {hero.Id}: attack reach (range {hero.Stats.AttackRange:F1}) exceeds the guaranteed AOI view range {viewRange:F1}";
                return false;
            }
            foreach (uint skillId in hero.Skills)
            {
                SkillTemplate? skill = _heroes.FindSkill(skillId);
                if (skill != null && !IsAttackRangeSupported(skill.Range + skill.Radius, reachSpeed))
                {
                    error = $"hero {hero.Id} skill {skill.Id}: range + radius ({skill.Range + skill.Radius:F1}) exceeds the guaranteed AOI view range {viewRange:F1}";
                    return false;
                }
            }
        }

        float heroMaxSpeed = _heroes.GetMaxSpeed() * buffMul;
        if (!_monsters.Validate(out error, heroMaxSpeed))
            return false;
        if (_bosses != null && !_bosses.Validate(out error, heroMaxSpeed))
            return false;

        error = string.Empty;
        return true;
    }

    /// <summary>스폰 테이블대로 몬스터를 배치한다. Start() 전에 한 번 호출한다.</summary>
    public uint SpawnMonsters() => _monsters.SpawnAll();

    /// <summary>틱 스레드를 멈추고 몬스터 시스템과 보스 시스템의 연결을 뗀다.</summary>
    public void Shutdown()
    {
        Stop();
        _monsters.SetBossSystem(null);
    }

    // ------------------------------------------------------------------------------------------------
    // Gateway 요청 (존 스레드에서 호출)
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// 입장 요청을 처리한다. 입장 응답을 AddEntity()보다 먼저 보낸다. AddEntity()가 AOI 진입 통지로 스폰 패킷을 보내므로,
    /// 순서를 바꾸면 클라이언트가 입장 성공 응답보다 스폰 패킷을 먼저 받는다. heroId 0은 기본 영웅.
    /// </summary>
    public void HandleEnter(ulong clientSessionId, ulong charId, uint heroId = 0)
    {
        ZoneEnterRes res = new ZoneEnterRes
        {
            ClientSessionId = clientSessionId,
            CharId = charId,
            SpawnX = _spawnPos.X,
            SpawnY = _spawnPos.Y,
        };

        // 0은 "공격자/대상 없음", 최상위 비트는 몬스터 ID 대역이라 플레이어 ID로 쓸 수 없다.
        if (charId == 0 || (charId & MonsterIds.Base) != 0)
        {
            SendEnterRes(res, ZoneEnterResult.InvalidCharId);
            return;
        }

        HeroTemplate? hero = _heroes.Resolve(heroId);
        if (hero == null)
        {
            SendEnterRes(res, ZoneEnterResult.InvalidHero);
            return;
        }

        if (FindEntity(charId) != null || _sessionToPlayer.ContainsKey(clientSessionId))
        {
            SendEnterRes(res, ZoneEnterResult.DuplicateEntity);
            return;
        }

        if (Capacity != 0 && (uint)_sessionToPlayer.Count >= Capacity)
        {
            LogThrottle.Warning("battlezone.zone-full", 1000, "[BattleZone] enter rejected: zone is full. zoneId={0}, capacity={1}, charId={2}", ZoneId, Capacity, charId);
            SendEnterRes(res, ZoneEnterResult.ZoneFull);
            return;
        }

        // 존 이동/재접속: Gateway가 보관했던 상태가 있으면 입장 위치/체력/MP/가방에 반영한다.
        PlayerState? restore = TakeRestore(charId);
        Vec2 startPos = ResolveStartPos(restore);
        res.SpawnX = startPos.X;
        res.SpawnY = startPos.Y;

        SendEnterRes(res, ZoneEnterResult.Ok);

        ZonePlayer player = new ZonePlayer(charId, clientSessionId, this, hero.Id);
        player.SetSpawnPos(_spawnPos);              // 죽으면 존의 기본 리스폰 위치에서 부활한다
        player.SetStartPos(startPos);
        player.SetSpeed(hero.Speed);
        player.SetCombatStats(hero.Stats);
        if (restore != null)
        {
            if (restore.HasVitals && restore.HeroId == hero.Id)
                player.RestoreVitals(restore.Hp, restore.Mp);       // 다른 영웅으로 바꿔 들어오면 체력 비율이 의미가 없으니 가득 채운 채 시작한다
            foreach (KeyValuePair<uint, uint> it in restore.Items)
                player.Inventory.TryAdd(it.Key, it.Value, out _);
        }

        if (AddEntity(player) == null)
        {
            // 사전 검사를 통과했는데도 실패한 비정상 경로 — 이미 성공 응답을 보냈으므로 추방 통지로 정정한다.
            ZoneKickNotify kick = new ZoneKickNotify { ClientSessionId = clientSessionId, Reason = (byte)ZoneKickReason.ZoneError };
            SendPacketToGateway(kick);
            return;
        }

        _sessionToPlayer.Add(clientSessionId, player);
        Interlocked.Increment(ref _playerCount);
        SendStat(player);
        SendDuel(player, DuelState.ZoneRules, 0, 0, RuleFlags);     // 이 존의 PvP/결투 규칙을 알린다(화면 표시용)
    }

    private void SendEnterRes(ZoneEnterRes res, ZoneEnterResult result)
    {
        res.Success = (byte)(result == ZoneEnterResult.Ok ? 1 : 0);
        res.Reason = (byte)result;
        SendPacketToGateway(res);
    }

    /// <summary>퇴장 요청(연결 종료 포함). 엔티티 제거 시 AOI 이탈 통지로 주변 플레이어에게 디스폰이 나간다.</summary>
    public void HandleLeave(ulong clientSessionId, ulong charId)
    {
        if (!_sessionToPlayer.TryGetValue(clientSessionId, out ZonePlayer? player) || player.Id != charId)
            return;

        DuelOnLeft(player);

        // 떠나는 순간의 상태를 Gateway에 맡긴다. 다음 입장(다른 존으로 넘어가는 경우 포함)에서 돌려받는다.
        PlayerState state = SnapshotOf(player);

        // 제거 중 AOI 이탈 통지로 이 플레이어 본인에게 이웃 수만큼 디스폰이 나가지 않도록 먼저 표시한다.
        player.MarkLeaving();
        _sessionToPlayer.Remove(clientSessionId);
        if (RemoveEntity(charId))
            Interlocked.Decrement(ref _playerCount);

        SendPacketToGateway(new ZoneStateSave { State = state });
    }

    /// <summary>플레이어의 체력/MP/가방과, 관문 안쪽에서 떠나는 경우의 도착 지점을 담는다. 죽어 있으면 체력은 가져가지 않는다(부활/입장 때 가득 채움).</summary>
    private PlayerState SnapshotOf(ZonePlayer player)
    {
        PlayerState st = new PlayerState { CharId = player.Id, HeroId = player.HeroId, HasVitals = player.IsAlive, Hp = player.Hp, Mp = player.Mp };
        foreach (KeyValuePair<uint, uint> it in player.Inventory.ToList())
        {
            if (st.Items.Count >= PlayerState.MaxItems)
                break;
            st.Items.Add(it);
        }
        if (player.IsAlive)
        {
            ZoneGateLink? g = ZoneLinkTable.Find(_gates, player.Pos, _minX, _minY, _maxX, _maxY, GateReach);
            if (g != null)
            {
                st.ArrivalZone = g.TargetZone;
                st.ArrivalX = g.TargetX;
                st.ArrivalY = g.TargetY;
            }
        }
        return st;
    }

    /// <summary>
    /// Gateway와 새로 등록되었을 때 호출한다. 이전 연결에서 입장했던 플레이어를 모두 내보낸다.
    /// (Gateway가 재시작했다면 세션 ID가 무효이고, 정리하지 않으면 같은 캐릭터의 재입장이 DuplicateEntity로 거부된다)
    /// </summary>
    /// <returns>내보낸 플레이어 수</returns>
    public uint HandleGatewayReset()
    {
        if (_sessionToPlayer.Count == 0)
            return 0;

        List<ulong> charIds = new List<ulong>(_sessionToPlayer.Count);
        foreach (ZonePlayer p in _sessionToPlayer.Values)
        {
            p.MarkLeaving();
            charIds.Add(p.Id);
        }
        _sessionToPlayer.Clear();

        uint removed = 0;
        foreach (ulong id in charIds)
        {
            if (RemoveEntity(id))
                ++removed;
        }
        Volatile.Write(ref _playerCount, 0);
        return removed;
    }

    /// <summary>
    /// Gateway가 중계한 클라이언트 패킷(헤더 포함) 한 건을 처리한다. 크기는 중계 단계에서 검증됐다.
    /// 종류별 빈도 제한(GuardEnabled)을 먼저 적용하고, 버려진 요청이나 비정상 패킷은 위반 점수를 올린다.
    /// 점수가 한계에 이르면 이 패킷 처리가 끝난 뒤 추방한다(처리 도중에 엔티티를 지우지 않기 위해).
    /// </summary>
    public void HandleClientPacket(ulong clientSessionId, ReadOnlySpan<byte> packet)
    {
        if (!_sessionToPlayer.TryGetValue(clientSessionId, out ZonePlayer? player))
            return;
        if (packet.Length < PacketIO.HeaderSize)
            return;

        ZonePacketType type = (ZonePacketType)PacketIO.ReadType(packet);
        RequestKind kind = Classify(type);
        Metrics.CountIn(kind, packet.Length);

        try
        {
            if (GuardEnabled && !player.Guard.TryTake(kind, NowMs, Limits))
            {
                Metrics.CountDropped();
                Penalize(player, Limits.DroppedWeight);
                return;
            }

            switch (type)
            {
                case ZonePacketType.ZcMoveReq:
                {
                    if (!ZcMoveReq.TryRead(packet, out ZcMoveReq req))
                    {
                        Reject(player, Limits.MalformedWeight);
                        return;
                    }
                    // NaN/Inf는 위치 검증과 AOI 셀 계산을 오염시키므로 입구에서 거른다.
                    if (!float.IsFinite(req.PosX) || !float.IsFinite(req.PosY) || !float.IsFinite(req.DestX) || !float.IsFinite(req.DestY))
                    {
                        Reject(player, Limits.NonFiniteWeight);
                        return;
                    }
                    RequestMove(player, new Vec2(req.PosX, req.PosY), new Vec2(req.DestX, req.DestY));
                    break;
                }
                case ZonePacketType.ZcAttackReq:
                {
                    if (!ZcAttackReq.TryRead(packet, out ZcAttackReq req))
                    {
                        Reject(player, Limits.MalformedWeight);
                        return;
                    }
                    RequestAttack(player, req.TargetId);
                    break;
                }
                case ZonePacketType.ZcSkillReq:
                    HandleSkillReq(player, packet);
                    break;
                case ZonePacketType.ZcPickupReq:
                    HandlePickup(player, packet);
                    break;
                case ZonePacketType.ZcChatReq:
                    HandleChat(player, packet);
                    break;
                case (ZonePacketType)DuelPacketType.Req:
                    HandleDuel(player, packet);
                    break;
                case ZonePacketType.ZcPingReq:
                {
                    // 왕복 시간 측정. 존 입력 큐를 거쳐 오므로 존 스레드가 밀려 있으면 그만큼 길게 측정된다(의도).
                    if (!ZcPingReq.TryRead(packet, out ZcPingReq req))
                    {
                        Reject(player, Limits.MalformedWeight);
                        return;
                    }
                    ZcPongNotify pong = new ZcPongNotify { Seq = req.Seq, ClientMs = req.ClientMs, ServerTimeMs = NowMs };
                    SendPacket(player, pong);
                    break;
                }
                default:
                    Reject(player, Limits.UnknownTypeWeight);
                    break;
            }
        }
        finally
        {
            if (player.KickPending)
                Kick(player, ZoneKickReason.RateLimited);
        }
    }

    private static RequestKind Classify(ZonePacketType type) => type switch
    {
        ZonePacketType.ZcMoveReq => RequestKind.Move,
        ZonePacketType.ZcAttackReq => RequestKind.Attack,
        ZonePacketType.ZcSkillReq => RequestKind.Skill,
        ZonePacketType.ZcPickupReq => RequestKind.Pickup,
        ZonePacketType.ZcPingReq => RequestKind.Ping,
        ZonePacketType.ZcChatReq => RequestKind.Chat,
        (ZonePacketType)DuelPacketType.Req => RequestKind.Duel,
        _ => RequestKind.Other,
    };

    /// <summary>읽을 수 없거나 비정상 값이어서 버린 요청. 센 뒤 위반 점수를 올린다.</summary>
    private void Reject(ZonePlayer player, double weight)
    {
        Metrics.CountMalformed();
        Penalize(player, weight);
    }

    /// <summary>위반 점수를 올린다. 한계에 이르면 현재 패킷 처리가 끝난 뒤 추방하도록 표시한다.</summary>
    private void Penalize(ZonePlayer player, double weight)
    {
        if (GuardEnabled && player.Guard.AddViolation(weight, NowMs, Limits))
            player.KickPending = true;
    }

    /// <summary>위반 누적으로 플레이어를 존에서 내보낸다. Gateway에 추방을 알리고(클라이언트는 존 소실 통지를 받는다) 존에서 제거한다.</summary>
    private void Kick(ZonePlayer player, ZoneKickReason reason)
    {
        player.KickPending = false;
        Metrics.CountKick();
        LogManager.Warning("[BattleZone] player kicked. zoneId={0}, charId={1}, reason={2}, score={3:F1}", ZoneId, player.Id, reason, player.Guard.Score);

        ZoneKickNotify kick = new ZoneKickNotify { ClientSessionId = player.ClientSessionId, Reason = (byte)reason };
        SendPacketToGateway(kick);

        HandleLeave(player.ClientSessionId, player.Id);
    }

    /// <summary>
    /// 채팅 요청. 채널별 수신자: Say는 내 시야(AOI) 안 플레이어와 본인, Zone은 존의 모든 플레이어, Whisper는 대상과 본인.
    /// 본문은 검증/정리한 문자열을 다시 인코딩해 보내므로 클라이언트가 보낸 바이트를 그대로 퍼뜨리지 않는다.
    /// 거부는 요청한 본인에게만 알리며, 형식 위반(없는 채널, 잘못된 본문)은 위반 점수도 올린다.
    /// </summary>
    private void HandleChat(ZonePlayer player, ReadOnlySpan<byte> packet)
    {
        if (!ZcChatReq.TryRead(packet, out ZcChatReq req))
        {
            Reject(player, Limits.MalformedWeight);
            return;
        }

        ChatChannel channel = (ChatChannel)req.Channel;
        if (channel != ChatChannel.Say && channel != ChatChannel.Zone && channel != ChatChannel.Whisper)
        {
            SendChatReject(player, req.Channel, ChatRejectReason.UnknownChannel);
            Reject(player, Limits.MalformedWeight);
            return;
        }
        if (!ChatText.TryNormalize(req.Text, out string text))
        {
            SendChatReject(player, req.Channel, ChatRejectReason.InvalidText);
            Reject(player, Limits.MalformedWeight);
            return;
        }

        ZonePlayer? target = null;
        if (channel == ChatChannel.Whisper)
        {
            target = FindEntityBase(req.TargetId) as ZonePlayer;
            if (target == null || ReferenceEquals(target, player) || target.IsLeaving)
            {
                SendChatReject(player, req.Channel, ChatRejectReason.NoTarget);
                return;
            }
        }
        else if (channel == ChatChannel.Zone)
        {
            ulong now = NowMs;
            if (now < player.ChatZoneReadyMs)
            {
                SendChatReject(player, req.Channel, ChatRejectReason.Cooldown);
                return;
            }
            player.ChatZoneReadyMs = now + GameConsts.ChatZoneCooldownMs;
        }

        ZcChatNotify notify = new ZcChatNotify { Channel = req.Channel, SenderId = player.Id, TargetId = target?.Id ?? 0UL, Text = text };
        Span<byte> buf = stackalloc byte[ZcChatNotify.Size];
        notify.Write(buf);
        Metrics.CountChat();

        switch (channel)
        {
            case ChatChannel.Say:
                BroadcastAround(player, buf);
                break;
            case ChatChannel.Zone:
                _recipients.Clear();
                foreach (ZonePlayer p in _sessionToPlayer.Values)
                    if (!p.IsLeaving)
                        _recipients.Add(p.ClientSessionId);
                FlushRecipients(buf);
                break;
            default:
                SendToPlayer(target!, buf);
                SendToPlayer(player, buf);      // 보낸 사람에게도 에코해서 화면에 같은 모양으로 남게 한다
                break;
        }
    }

    private void SendChatReject(ZonePlayer to, byte channel, ChatRejectReason reason)
    {
        Metrics.CountChatReject();
        ZcChatRejectNotify msg = new ZcChatRejectNotify { Channel = channel, Reason = (byte)reason };
        SendPacket(to, msg);
    }

    /// <summary>스킬 시전 요청. 영웅이 가진 스킬인지 확인한 뒤 CastSkill에 맡긴다. 좌표/ID는 모두 클라이언트 값이다.</summary>
    private void HandleSkillReq(ZonePlayer player, ReadOnlySpan<byte> packet)
    {
        if (!ZcSkillReq.TryRead(packet, out ZcSkillReq req))
        {
            Reject(player, Limits.MalformedWeight);
            return;
        }
        if (!float.IsFinite(req.X) || !float.IsFinite(req.Y))
        {
            Reject(player, Limits.NonFiniteWeight);
            return;
        }

        HeroTemplate? hero = _heroes.Resolve(player.HeroId);
        SkillTemplate? skill = hero != null ? _heroes.FindHeroSkill(hero, req.SkillId) : null;
        if (skill == null)
        {
            SendSkillReject(player, req.SkillId, SkillRejectReason.UnknownSkill);
            return;
        }

        CastSkill(player, skill, req.TargetId, new Vec2(req.X, req.Y), ref player.SkillReadyAt(req.SkillId));
    }

    /// <summary>
    /// 줍기 요청. 검증(사망/존재/소유권/거리) -> 가방 자리 -> 획득 순이며, 거부는 요청한 본인에게만 알린다.
    /// 성공하면 지면 아이템이 사라지고(주변 플레이어에게 디스폰) 본인에게 획득 통지가 나간다.
    /// </summary>
    private void HandlePickup(ZonePlayer player, ReadOnlySpan<byte> packet)
    {
        if (!ZcPickupReq.TryRead(packet, out ZcPickupReq req))
        {
            Reject(player, Limits.MalformedWeight);
            return;
        }

        PickupResult result = _drops.ValidatePickup(player, req.EntityId, out GroundItem? item);
        if (result != PickupResult.Ok)
        {
            SendPickupReject(player, req.EntityId, result switch
            {
                PickupResult.OutOfRange => PickupRejectReason.OutOfRange,
                PickupResult.NotOwner => PickupRejectReason.NotOwner,
                PickupResult.Dead => PickupRejectReason.Dead,
                _ => PickupRejectReason.NotFound,
            });
            return;
        }

        if (!player.Inventory.TryAdd(item!.ItemId, item.Count, out uint total))
        {
            SendPickupReject(player, req.EntityId, PickupRejectReason.InventoryFull);
            return;
        }

        _drops.Consume(item);
        Metrics.CountPicked();

        ZcItemGainNotify gain = new ZcItemGainNotify { ItemId = item.ItemId, Count = item.Count, Total = total };
        SendPacket(player, gain);
    }

    private void SendPickupReject(ZonePlayer to, ulong entityId, PickupRejectReason reason)
    {
        Metrics.CountPickupReject();
        ZcPickupRejectNotify n = new ZcPickupRejectNotify { EntityId = entityId, Reason = (byte)reason };
        SendPacket(to, n);
    }

    /// <summary>본인에게 체력/MP 현황을 보낸다. MP를 쓰지 않는 영웅(MaxMp 0)에게는 보내지 않는다.</summary>
    private void SendStat(ZonePlayer player)
    {
        if (player.MaxMp <= 0)
            return;

        ZcStatNotify n = new ZcStatNotify { Hp = player.Hp, MaxHp = player.CombatStats.MaxHp, Mp = player.Mp, MaxMp = player.MaxMp };
        SendPacket(player, n);
    }

    // ------------------------------------------------------------------------------------------------
    // 스폰/디스폰 (ZonePlayer가 AOI 통지에서 호출)
    // ------------------------------------------------------------------------------------------------

    /// <summary>시야에 들어온 엔티티의 현재 상태를 해당 플레이어에게 보낸다.</summary>
    public void SendSpawn(ZonePlayer to, ZoneEntity about)
    {
        ZcSpawnNotify n = new ZcSpawnNotify
        {
            EntityId = about.Id,
            EntityType = (byte)about.Type,
            PosX = about.Pos.X,
            PosY = about.Pos.Y,
            DestX = about.Dest.X,
            DestY = about.Dest.Y,
            Speed = about.Speed,
            Moving = (byte)(about.IsMoving ? 1 : 0),
            Hp = about.Hp,
            MaxHp = about.CombatStats.MaxHp,
            Dead = (byte)(about.IsAlive ? 0 : 1),
        };
        if (about is ZonePlayer ap)
            n.TemplateId = ap.HeroId;
        else if (about is GroundItem gi)
            n.TemplateId = gi.ItemId;       // 지면 아이템: templateId = 아이템 ID, hp = maxHp = 수량
        else
            n.TemplateId = about is Monster m && _monsters.IsAiMonster(about) ? m.Template.Id : 0;

        SendPacket(to, n);
    }

    /// <summary>시야에서 벗어난 엔티티를 해당 플레이어에게 알린다.</summary>
    public void SendDespawn(ZonePlayer to, ZoneEntity about)
    {
        ZcDespawnNotify n = new ZcDespawnNotify { EntityId = about.Id };
        SendPacket(to, n);
    }

    // ------------------------------------------------------------------------------------------------
    // FieldZone 통지 -> 패킷
    // ------------------------------------------------------------------------------------------------

    protected override void OnEntityRemoved(ZoneEntity entity)
    {
        _drops.OnRemoved(entity);
        _monsters.OnRemoved(entity);
    }

    protected override void OnEntityMoveStarted(ZoneEntity entity)
    {
        ZcMoveNotify n = new ZcMoveNotify
        {
            EntityId = entity.Id,
            StartX = entity.Pos.X,
            StartY = entity.Pos.Y,
            DestX = entity.Dest.X,
            DestY = entity.Dest.Y,
            Speed = entity.Speed,
            ServerTimeMs = NowMs,
        };
        BroadcastPacket(entity, n);
    }

    protected override void OnEntityMoveStopped(ZoneEntity entity, bool arrived)
    {
        ZcStopNotify n = new ZcStopNotify { EntityId = entity.Id, PosX = entity.Pos.X, PosY = entity.Pos.Y, Arrived = (byte)(arrived ? 1 : 0) };
        BroadcastPacket(entity, n);
    }

    protected override void OnEntityTeleported(ZoneEntity entity)
    {
        ZcStopNotify stop = new ZcStopNotify { EntityId = entity.Id, PosX = entity.Pos.X, PosY = entity.Pos.Y, Arrived = 0 };
        BroadcastPacket(entity, stop);

        ZcCorrectPosNotify correct = new ZcCorrectPosNotify { Reason = (byte)CorrectPosReason.Teleported, PosX = entity.Pos.X, PosY = entity.Pos.Y };
        SendPacketToEntity(entity, correct);
    }

    protected override void OnMoveRejected(ZoneEntity entity, MoveResult reason)
    {
        CorrectPosReason code = reason switch
        {
            MoveResult.OutOfBounds => CorrectPosReason.OutOfBounds,
            MoveResult.Immobile => CorrectPosReason.Immobile,
            MoveResult.Dead => CorrectPosReason.Dead,
            MoveResult.Blocked => CorrectPosReason.Blocked,
            MoveResult.Stunned => CorrectPosReason.Stunned,
            MoveResult.Rooted => CorrectPosReason.Rooted,
            _ => CorrectPosReason.PositionMismatch,
        };
        if (reason == MoveResult.PositionMismatch && entity is ZonePlayer offender)
            Penalize(offender, Limits.MismatchWeight);
        ZcCorrectPosNotify n = new ZcCorrectPosNotify { Reason = (byte)code, PosX = entity.Pos.X, PosY = entity.Pos.Y };
        SendPacketToEntity(entity, n);
    }

    protected override void OnEntityDamaged(ZoneEntity? attacker, ZoneEntity victim, int damage)
    {
        ZcDamageNotify n = new ZcDamageNotify
        {
            AttackerId = attacker?.Id ?? 0,
            TargetId = victim.Id,
            Damage = damage,
            Hp = victim.Hp,
            MaxHp = victim.CombatStats.MaxHp,
        };
        BroadcastCombatPacket(victim, attacker, n);
        _monsters.OnDamaged(attacker, victim, damage);
    }

    protected override void OnEntityDied(ZoneEntity victim, ZoneEntity? killer)
    {
        ZcDeathNotify n = new ZcDeathNotify { EntityId = victim.Id, KillerId = killer?.Id ?? 0 };
        BroadcastCombatPacket(victim, killer, n);
        DuelOnDied(victim);
        if (victim is Monster dead && _monsters.IsAiMonster(victim))
            _drops.OnMonsterDied(dead, killer);
        _monsters.OnDied(victim);
    }

    protected override void OnEntityRespawned(ZoneEntity entity)
    {
        ulong now = NowMs;
        ZcRespawnNotify n = new ZcRespawnNotify
        {
            EntityId = entity.Id,
            PosX = entity.Pos.X,
            PosY = entity.Pos.Y,
            Hp = entity.Hp,
            MaxHp = entity.CombatStats.MaxHp,
            InvulnerableMs = IsInvulnerable(entity) && entity.InvulnerableUntilMs > now ? (uint)(entity.InvulnerableUntilMs - now) : 0,
        };
        BroadcastPacket(entity, n);
        _monsters.OnRespawned(entity);
    }

    protected override void OnEntityHealed(ZoneEntity entity, int amount)
    {
        ZcHealNotify n = new ZcHealNotify { EntityId = entity.Id, Amount = amount, Hp = entity.Hp, MaxHp = entity.CombatStats.MaxHp };
        BroadcastPacket(entity, n);
    }

    protected override void OnEntityHealedBy(ZoneEntity source, ZoneEntity target, int healed) => _monsters.OnHealedBy(source, target, healed);

    protected override void OnEntityStatChanged(ZoneEntity entity)
    {
        if (entity is ZonePlayer p)
            SendStat(p);
    }

    protected override void OnSkillHit(ZoneEntity caster, SkillTemplate skill, ZoneEntity victim) => _monsters.OnSkillHit(caster, skill, victim);

    protected override void OnAttackRejected(ZoneEntity attacker, ulong targetId, AttackResult reason)
    {
        AttackRejectReason code = reason switch
        {
            // 대상 없음과 사거리 밖은 구분하지 않는다(엔티티 존재 여부 비노출)
            AttackResult.TargetNotFound => AttackRejectReason.OutOfRange,
            AttackResult.OutOfRange => AttackRejectReason.OutOfRange,
            AttackResult.AttackerDead => AttackRejectReason.AttackerDead,
            AttackResult.TargetDead => AttackRejectReason.TargetDead,
            AttackResult.TargetInvulnerable => AttackRejectReason.TargetInvulnerable,
            AttackResult.NoLineOfSight => AttackRejectReason.NoLineOfSight,
            AttackResult.Casting => AttackRejectReason.Casting,
            AttackResult.Stunned => AttackRejectReason.Stunned,
            _ => AttackRejectReason.InvalidTarget,
        };
        ZcAttackRejectNotify n = new ZcAttackRejectNotify { TargetId = targetId, Reason = (byte)code };
        SendPacketToEntity(attacker, n);
    }

    protected override void OnSkillCast(ZoneEntity caster, SkillTemplate skill, ulong targetId, Vec2 center, uint hitCount)
    {
        ZcSkillNotify n = new ZcSkillNotify { CasterId = caster.Id, SkillId = skill.Id, TargetId = targetId, X = center.X, Y = center.Y, HitCount = hitCount };
        BroadcastPacket(caster, n);
    }

    protected override void OnSkillCastStarted(ZoneEntity caster, SkillTemplate skill, ulong targetId, Vec2 center)
    {
        ZcSkillCastNotify n = new ZcSkillCastNotify { CasterId = caster.Id, SkillId = skill.Id, TargetId = targetId, X = center.X, Y = center.Y, CastTimeMs = skill.CastTimeMs };
        BroadcastPacket(caster, n);
    }

    protected override void OnSkillCastCancelled(ZoneEntity caster, SkillTemplate skill, CoreCancel reason)
    {
        ProtoCancel code = reason switch
        {
            CoreCancel.Moved => ProtoCancel.Moved,
            CoreCancel.Died => ProtoCancel.Died,
            CoreCancel.OutOfRange => ProtoCancel.OutOfRange,
            CoreCancel.NoLineOfSight => ProtoCancel.NoLineOfSight,
            CoreCancel.Stunned => ProtoCancel.Stunned,
            _ => ProtoCancel.InvalidTarget,
        };
        ZcSkillCancelNotify n = new ZcSkillCancelNotify { CasterId = caster.Id, SkillId = skill.Id, Reason = (byte)code };
        BroadcastPacket(caster, n);
    }

    protected override void OnProjectileLaunched(ZoneEntity caster, SkillTemplate skill, uint projectileId, ulong targetId, Vec2 from, Vec2 to, uint flightMs)
    {
        ZcProjectileNotify n = new ZcProjectileNotify
        {
            ProjectileId = projectileId,
            CasterId = caster.Id,
            SkillId = skill.Id,
            TargetId = targetId,
            X0 = from.X,
            Y0 = from.Y,
            X1 = to.X,
            Y1 = to.Y,
            FlightMs = flightMs,
        };
        BroadcastPacket(caster, n);
    }

    protected override void OnProjectileImpact(ZoneEntity caster, SkillTemplate skill, uint projectileId, Vec2 center, uint hitCount)
    {
        ZcSkillImpactNotify n = new ZcSkillImpactNotify { ProjectileId = projectileId, CasterId = caster.Id, SkillId = skill.Id, X = center.X, Y = center.Y, HitCount = hitCount };
        BroadcastPacket(caster, n);
    }

    protected override void OnStatusApplied(ZoneEntity entity, StatusTemplate status, ulong casterId)
    {
        ZcStatusNotify n = new ZcStatusNotify { EntityId = entity.Id, StatusId = status.Id, CasterId = casterId, DurationMs = status.DurationMs, Applied = 1 };
        BroadcastPacket(entity, n);
    }

    /// <summary>사망으로 풀린 경우는 사망 통지가 대신하므로 보내지 않는다.</summary>
    protected override void OnStatusEnded(ZoneEntity entity, uint statusId, StatusEndReason reason)
    {
        if (reason == StatusEndReason.Died)
            return;
        ZcStatusNotify n = new ZcStatusNotify { EntityId = entity.Id, StatusId = statusId, Applied = 0 };
        BroadcastPacket(entity, n);
    }

    protected override void OnSkillRejected(ZoneEntity caster, SkillTemplate skill, SkillResult reason)
    {
        SkillRejectReason code = reason switch
        {
            SkillResult.CasterDead => SkillRejectReason.CasterDead,
            SkillResult.Cooldown => SkillRejectReason.Cooldown,
            SkillResult.OutOfRange => SkillRejectReason.OutOfRange,
            SkillResult.NoLineOfSight => SkillRejectReason.NoLineOfSight,
            SkillResult.Casting => SkillRejectReason.Casting,
            SkillResult.Stunned => SkillRejectReason.Stunned,
            SkillResult.NotEnoughMp => SkillRejectReason.NotEnoughMp,
            _ => SkillRejectReason.InvalidTarget,
        };
        SendSkillReject(caster, skill.Id, code);
    }

    private void SendSkillReject(ZoneEntity to, uint skillId, SkillRejectReason reason)
    {
        ZcSkillRejectNotify n = new ZcSkillRejectNotify { SkillId = skillId, Reason = (byte)reason };
        SendPacketToEntity(to, n);
    }

    protected override StatusTemplate? FindStatusTemplate(uint statusId) => _heroes.FindStatus(statusId);

    protected override void OnUpdate(uint dtMs)
    {
        base.OnUpdate(dtMs);
        _monsters.Update();
        DuelUpdate();
    }

    protected override uint GetRespawnDelayMs(ZoneEntity victim)
        => _monsters.IsAiMonster(victim) ? _monsters.GetRespawnDelayMs(victim) : base.GetRespawnDelayMs(victim);

    protected override Vec2 GetRespawnPos(ZoneEntity entity)
    {
        Vec2 pos = default;
        return _monsters.GetRespawnPos(entity, ref pos) ? pos : base.GetRespawnPos(entity);
    }

    // ------------------------------------------------------------------------------------------------
    // 송신 도우미
    // ------------------------------------------------------------------------------------------------

    // 패킷 구조체를 직렬화해서 보내는 도우미. 버퍼는 이 메서드 안에서 stackalloc으로 잡고 구조체마다 JIT가 따로 만들므로 할당이 없다.
    // 이미 직렬화한 한 장을 여러 곳에 보내는 경우(채팅)만 아래 Span 버전을 직접 쓴다.

    private void SendPacket<T>(ZonePlayer to, in T packet) where T : struct, IPacket
    {
        Span<byte> buf = stackalloc byte[T.PacketSize];
        packet.Write(buf);
        SendToPlayer(to, buf);
    }

    private void SendPacketToEntity<T>(ZoneEntity to, in T packet) where T : struct, IPacket
    {
        Span<byte> buf = stackalloc byte[T.PacketSize];
        packet.Write(buf);
        SendToEntity(to, buf);
    }

    private void BroadcastPacket<T>(ZoneEntity about, in T packet) where T : struct, IPacket
    {
        Span<byte> buf = stackalloc byte[T.PacketSize];
        packet.Write(buf);
        BroadcastAround(about, buf);
    }

    private void BroadcastCombatPacket<T>(ZoneEntity victim, ZoneEntity? other, in T packet) where T : struct, IPacket
    {
        Span<byte> buf = stackalloc byte[T.PacketSize];
        packet.Write(buf);
        BroadcastCombat(victim, other, buf);
    }

    private void SendPacketToGateway<T>(in T packet) where T : struct, IPacket
    {
        Span<byte> buf = stackalloc byte[T.PacketSize];
        packet.Write(buf);
        _link.SendToGateway(buf);
    }

    private void SendToPlayer(ZonePlayer player, ReadOnlySpan<byte> packet)
    {
        if (player.IsLeaving)
            return;

        Metrics.CountOut(packet.Length);
        _link.SendToClient(player.ClientSessionId, packet);
    }

    /// <summary>엔티티가 플레이어면 해당 클라이언트로 패킷을 보낸다. 플레이어가 아니면 무시한다.</summary>
    private void SendToEntity(ZoneEntity target, ReadOnlySpan<byte> packet)
    {
        if (target is ZonePlayer p)
            SendToPlayer(p, packet);
    }

    /// <summary>엔티티 본인과 시야(AOI) 안 플레이어 전원에게 같은 패킷을 멀티캐스트 중계 하나로 보낸다.</summary>
    private void BroadcastAround(ZoneEntity entity, ReadOnlySpan<byte> packet)
    {
        // 시야에 플레이어가 없는 몬스터(복귀 중 이동/회복, 외딴 곳의 부활 등)는 알릴 대상이 없으니 AOI를 훑지 않는다.
        if (HasNoObserver(entity))
            return;

        _recipients.Clear();
        AddRecipient(entity);
        base.ForEachWatcherInAoi(entity, _addRecipientFn);   // 타입 있는 오버로드는 호출마다 람다를 만들므로 베이스를 직접 부른다
        FlushRecipients(packet);
    }

    /// <summary>
    /// 피해자 본인과 시야 안 플레이어 전원에게 보내고, 전투 상대(공격자/처치자)가 그 시야에 없었으면 따로 보낸다.
    /// 사거리가 시야보다 길거나 AOI 히스테리시스로 서로 보이지 않아도 공격자가 결과를 받도록 보장한다.
    /// </summary>
    private void BroadcastCombat(ZoneEntity victim, ZoneEntity? other, ReadOnlySpan<byte> packet)
    {
        _recipients.Clear();
        AddRecipient(victim);
        _combatOther = other;
        _combatOtherVisited = other == null || ReferenceEquals(other, victim);     // 자기 자신이 상대면 이미 수신자다
        base.ForEachWatcherInAoi(victim, _combatVisitFn);
        if (!_combatOtherVisited)
            AddRecipient(other!);
        _combatOther = null;
        FlushRecipients(packet);
    }

    private void CombatVisit(ZoneEntity neighbor)
    {
        if (ReferenceEquals(neighbor, _combatOther))
            _combatOtherVisited = true;
        AddRecipient(neighbor);
    }

    /// <summary>이 엔티티의 변화를 볼 플레이어가 없다고 확실하면 true. AI 몬스터는 시야 안 플레이어 수를 이미 세고 있다.</summary>
    private bool HasNoObserver(ZoneEntity entity)
        => entity is Monster m && _monsters.IsAiMonster(entity) && m.NearPlayers == 0;

    private void AddRecipient(ZoneEntity target)
    {
        if (target is ZonePlayer p)
            _recipients.Add(p.ClientSessionId);
    }

    private void FlushRecipients(ReadOnlySpan<byte> packet)
    {
        if (_recipients.Count == 0)
            return;
        Metrics.CountBroadcast(_recipients.Count, packet.Length);
        _link.SendToClients(CollectionsMarshal.AsSpan(_recipients), packet);
    }
}
