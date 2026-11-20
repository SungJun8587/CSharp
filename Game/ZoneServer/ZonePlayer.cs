// ZonePlayer.cs : 존에 입장한 클라이언트 한 명(캐릭터)의 엔티티.
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Game.ZoneCore;

namespace Game.ZoneServer;

/// <summary>
/// 플레이어 엔티티. 엔티티 ID는 캐릭터 ID다. 시야(AOI)에 다른 엔티티가 들어오고 나가면 해당 클라이언트에 스폰/디스폰을 보낸다.
/// 모든 멤버는 존 스레드에서만 접근한다.
/// </summary>
public sealed class ZonePlayer : ZoneEntity
{
    private readonly BattleZone _zone;
    private readonly Dictionary<uint, ulong> _skillReadyMs = new Dictionary<uint, ulong>();
    private bool _leaving;

    public ZonePlayer(ulong charId, ulong clientSessionId, BattleZone zone, uint heroId)
        : base(charId, EntityType.Player)
    {
        ClientSessionId = clientSessionId;
        HeroId = heroId;
        _zone = zone;
    }

    /// <summary>Gateway 클라이언트 세션 ID (패킷 전송 대상 식별)</summary>
    public ulong ClientSessionId { get; }

    /// <summary>선택한 영웅 ID</summary>
    public uint HeroId { get; }

    /// <summary>레벨(1~). 존 스레드에서만 바꾼다. 영웅 능력치에 레벨 배율이 곱해진다.</summary>
    public uint Level { get; internal set; } = 1;

    /// <summary>현재 레벨에서 쌓은 경험치. 최대 레벨이면 0.</summary>
    public uint Exp { get; internal set; }

    /// <summary>재화 보유량(재화 ID -> 양). 존 스레드에서만 바꾼다.</summary>
    public Dictionary<uint, uint> Wallet { get; } = new Dictionary<uint, uint>();

    /// <summary>보유 스킬(스킬 ID -> 장착 칸 1~6, 0이면 장착 안 함). 존 스레드에서만 바꾼다. 다른 영웅의 스킬도 저장을 위해 그대로 들고 있다.</summary>
    public Dictionary<uint, byte> Skills { get; } = new Dictionary<uint, byte>();

    /// <summary>메모리 인벤토리. 접속 중에만 유지된다.</summary>
    public Inventory Inventory { get; } = new Inventory();

    /// <summary>요청 빈도/위반 상태.</summary>
    internal RequestGuard Guard { get; } = new RequestGuard();

    /// <summary>위반 점수가 한계에 이르러 현재 패킷 처리가 끝나면 추방할 예정이면 true.</summary>
    internal bool KickPending { get; set; }

    /// <summary>존 전체 채팅을 다시 보낼 수 있는 시각(존 논리 시간, ms).</summary>
    internal ulong ChatZoneReadyMs { get; set; }

    /// <summary>퇴장 중이면 true. 이 플레이어에게는 더 이상 패킷을 보내지 않는다.</summary>
    public bool IsLeaving => _leaving;

    public void MarkLeaving() => _leaving = true;

    /// <summary>스킬의 "다시 쓸 수 있는 존 논리 시각"에 대한 참조. 없으면 0으로 만들어 돌려준다.</summary>
    public ref ulong SkillReadyAt(uint skillId) => ref CollectionsMarshal.GetValueRefOrAddDefault(_skillReadyMs, skillId, out _);

    protected override void OnAoiEnter(ZoneEntity other) => _zone.SendSpawn(this, other);

    protected override void OnAoiLeave(ZoneEntity other)
    {
        if (!_leaving)
            _zone.SendDespawn(this, other);
    }
}
