// InMemoryPlayerStore.cs : 메모리에만 두는 저장소. DB 없이 도는 테스트/개발용(프로세스가 끝나면 사라진다).
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Game.ZoneCommon;

namespace Game.Persistence;

public sealed class InMemoryPlayerStore : IPlayerStore
{
    private readonly ConcurrentDictionary<ulong, PlayerState> _data = new ConcurrentDictionary<ulong, PlayerState>();

    public int Count => _data.Count;

    /// <summary>저장된 상태의 복사본. 없으면 null.</summary>
    public PlayerState? Peek(ulong charId) => _data.TryGetValue(charId, out PlayerState? s) ? PlayerStateCopy.Clone(s) : null;

    public Task<PlayerState?> LoadAsync(ulong charId, CancellationToken ct) => Task.FromResult(Peek(charId));

    public Task SaveAsync(PlayerState state, CancellationToken ct)
    {
        _data[state.CharId] = PlayerStateCopy.Clone(state);
        return Task.CompletedTask;
    }

    public Task SaveBatchAsync(IReadOnlyList<PlayerState> states, CancellationToken ct)
    {
        foreach (PlayerState s in states)
            _data[s.CharId] = PlayerStateCopy.Clone(s);
        return Task.CompletedTask;
    }
}
