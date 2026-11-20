// IPlayerStore.cs : 캐릭터 상태 저장소 인터페이스. Gateway 는 이것만 안다(MySQL 인지 메모리인지, 같은 프로세스인지 별도 DB 서비스인지 모른다).
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.ZoneCommon;

namespace Game.Persistence;

public interface IPlayerStore
{
    /// <summary>캐릭터 상태를 읽는다. 저장된 적이 없으면 null(새 캐릭터). 읽기에 실패하면 예외를 던진다(없는 것과 구별해야 한다:
    /// 실패를 "없음"으로 취급하고 입장시키면 나중에 빈 상태가 저장돼 좋은 데이터를 덮어쓴다).</summary>
    Task<PlayerState?> LoadAsync(ulong charId, CancellationToken ct);

    /// <summary>캐릭터 상태 하나를 저장한다(있으면 덮어쓴다). 실패하면 예외.</summary>
    Task SaveAsync(PlayerState state, CancellationToken ct);

    /// <summary>여러 캐릭터를 한 번에 저장한다. 구현이 한 트랜잭션으로 묶을 수 있으면 그렇게 한다. 하나라도 실패하면 예외(어디까지 저장됐는지는 알 수 없다 - 같은 상태를 다시 저장해도 안전해야 한다).</summary>
    Task SaveBatchAsync(IReadOnlyList<PlayerState> states, CancellationToken ct);
}
