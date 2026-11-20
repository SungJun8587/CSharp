// WriteBehindPlayerStore.cs : IPlayerStore 위에 쓰기 지연을 얹는 어댑터. 합치기/배치/재시도/백오프/종료 비우기는 Core.Util.WriteBehindWorker 가 한다.
// 호출한 스레드(Gateway 패킷 처리)는 DB 를 기다리지 않는다. 이 클래스가 하는 일은 PlayerState 를 복사해 넣고(넣은 뒤 바뀌지 않게),
// 읽기에서 아직 DB 에 못 내려간 최신 상태를 먼저 돌려주는 것이다(방금 저장한 상태를 옛 DB 값으로 읽는 일이 없게).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Util;
using Game.ZoneCommon;

namespace Game.Persistence;

public sealed class WriteBehindPlayerStore : IDisposable
{
    private readonly IPlayerStore _inner;
    private readonly WriteBehindWorker<ulong, PlayerState> _worker;

    public WriteBehindPlayerStore(IPlayerStore inner, int flushIntervalMs = 1000, int batchSize = 100, int maxAttempts = 5, int shutdownTimeoutMs = 10000, int retryBackoffMs = 1000)
    {
        _inner = inner;
        _worker = new WriteBehindWorker<ulong, PlayerState>("PlayerStore",
            (items, ct) => _inner.SaveBatchAsync(items.Select(kv => kv.Value).ToList(), ct).GetAwaiter().GetResult(),
            (key, state, ct) => _inner.SaveAsync(state, ct).GetAwaiter().GetResult(),
            flushIntervalMs, batchSize, maxAttempts, shutdownTimeoutMs, retryBackoffMs);
    }

    /// <summary>아직 내려 쓰지 못한 캐릭터 수.</summary>
    public int Pending => _worker.Pending;
    public long Saved => _worker.Saved;
    public long FailedBatches => _worker.FailedBatches;
    public long Dropped => _worker.Dropped;
    /// <summary>가장 최근 한 번 내려 쓰는 데 걸린 시간(ms)과 지금까지의 최대.</summary>
    public long LastFlushMs => _worker.LastFlushMs;
    public long MaxFlushMs => _worker.MaxFlushMs;

    /// <summary>상태를 저장 대기열에 넣는다(바로 돌아온다). 같은 캐릭터의 앞선 대기는 이 상태로 대체된다.</summary>
    public void Enqueue(PlayerState state) => _worker.Enqueue(state.CharId, PlayerStateCopy.Clone(state));

    /// <summary>캐릭터 상태를 읽는다. 대기열에 더 새 상태가 있으면 그것을, 아니면 저장소에서 읽는다. 저장소 읽기 실패는 예외로 전달한다.</summary>
    public Task<PlayerState?> LoadAsync(ulong charId, CancellationToken ct)
    {
        if (_worker.TryGetPending(charId, out PlayerState? pending))
            return Task.FromResult<PlayerState?>(PlayerStateCopy.Clone(pending));
        return _inner.LoadAsync(charId, ct);
    }

    /// <summary>대기 중인 것을 모두 내려 쓸 때까지(또는 제한 시간까지) 기다린다. 다 내려 썼으면 true.</summary>
    public bool Flush(int timeoutMs) => _worker.Flush(timeoutMs);

    public void Dispose()
    {
        _worker.Dispose();
        (_inner as IDisposable)?.Dispose();
    }
}
