# Core

C# 공용 라이브러리. 어셈블리 셋으로 나뉘며 `Core.Net`, `Core.Redis` 모두 `Core.Util`을 참조하고 `Core.Redis`는 `Core.Net`(JobQueue, SocketUtils)도 참조한다.

| 프로젝트 | 네임스페이스 | 내용 |
|---|---|---|
| `Util/Core.Util.csproj` | `Core.Util` | 의존성 없는 공용 유틸: 로그(`LogManager`), 파일 쓰기(`FileUtil`, `FileReplace`), 문자열(`TextUtil`), `AsciiChar`. 스레드 유틸(`Util/Threading`): 락(`SpinMutex`, `RWSpinLock`, `PLock`, `PRWLock`), 큐(`LockFreeQueue`, `BlockingTaskQueue`, `DoubleBufferQueue`, `ChunkedSwapQueue`, `SpinLockQueue`), 샤딩 맵(`ClusterSpinMap`), 고정 스레드 실행기(`WorkerPool`), 락 순서 진단(`DeadLockProfiler`), 스레드 관리(`ThreadManager`), `SrwLock`, `CriticalSection`, `SyncValue`, 캐시라인 패딩(`CacheLine`, `PaddedInt32/64`). 앞으로의 Util 클래스는 여기에 추가한다 |
| `Net/Core.Net.csproj`   | `Core.Net`  | 네트워크: 세션, 리스너, 송수신 버퍼, TCP/UDP/TLS 서비스, JobQueue/JobTimer, 패킷 디스패처(`PacketDispatcher<TContext>`: 직접 등록 또는 `[PacketHandler]` 자동 등록), 소켓 유틸, `INetLogger`와 `LogManagerNetLogger`(LogManager에 연결) |
| `Redis/Core.Redis.csproj` | `Core.Redis` | Redis 클라이언트: 연결(`RedisClient`), 커넥션 풀(`RedisConnectionPool`), Pub/Sub(`RedisSubscriber`), 다중 노드 진입점(`RedisService`), Lua 스크립트(`RedisScript`), 연결 오류 재시도(`RedisRetry`), 서버 하트비트(`RedisServerHeartbeat`) |

네트워크가 필요 없는 도구는 `Core.Util`만 참조한다.

## 빌드와 데모
    dotnet build Core.sln
    dotnet run --project demo/UtilDemo/Core.UtilDemo.csproj      # Util 테스트
    dotnet run --project demo/NetDemo/Core.NetDemo.csproj        # Net 스모크/스트레스 테스트
    dotnet run --project demo/NetLogDemo/Core.NetLogDemo.csproj  # Net + Util 로그 연결 테스트
    dotnet run --project demo/DispatcherDemo/Core.DispatcherDemo.csproj  # 패킷 디스패처 테스트
    dotnet run --project demo/ThreadDemo/Core.ThreadDemo.csproj  # 스레드 유틸 정확성/스트레스 테스트
    dotnet run --project demo/RedisDemo/Core.RedisDemo.csproj    # Redis 테스트 (PATH에 redis-server 필요)

각 데모는 마지막 줄에 `ALL PASSED`가 나와야 한다.

## 사용
    using Core.Net;
    using Core.Util;
    var options = new NetServiceOptions { Logger = new LogManagerNetLogger() };

## 패킷 디스패처
    var dispatcher = new PacketDispatcher<ChatSession>();
    dispatcher.Register((ushort)ChatPacket.Chat, 6, ChatHandlers.OnChat);   // 직접 등록
    dispatcher.RegisterFromAssembly(typeof(ChatHandlers).Assembly);         // [PacketHandler(type, MinSize = n)] static 메서드 자동 등록

    protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
    {
        if (dispatcher.Dispatch(this, packet) != PacketDispatchResult.Handled)
            Disconnect(CloseReason.ProtocolError);
    }

등록은 시작할 때 한 번 하고, 첫 `Dispatch`(또는 `Freeze`) 뒤에는 바뀌지 않아 `Dispatch`는 락 없이 여러 스레드에서 불러도 된다.

## Redis
    var jobs = new JobQueue();
    var redis = new RedisService(jobs);
    redis.Init(new[] { new RedisNode(1, "127.0.0.1", 6379), new RedisNode(2, "127.0.0.1", 6380) }, poolSize: 4);   // 첫 노드가 기본 노드

    redis.SendCommand(RedisHelper.MakeArgs("GET", "k"), res => { ... });     // 콜백은 jobs에서 직렬 실행
    var res = await redis.SendCommandAsync(2, new[] { "GET", "k" });         // 노드 지정 + await

    static readonly RedisScript Consume = new RedisScript("return redis.call('GET', KEYS[1])");
    Consume.Run(redis, new[] { "key" }, Array.Empty<string>(), res => { ... });   // EVALSHA, NOSCRIPT이면 EVAL로 한 번 재전송

    RedisRetry.SendCommandWithRetry(redis, jobs, null, args, maxRetry: 3, retryDelayMs: 200, onResult);  // 연결 오류만 재시도
    redis.Subscribe("chan", (channel, payload) => { ... });                   // 재연결 시 자동 재구독

문자열은 UTF-8로 인코딩/디코딩한다(바이너리 안전하지 않음). 풀은 연결당 동시에 명령 하나만 보내고, 연결이 모두 사용 중이면 대기 큐(최대 2000건, 5초)에 둔다. 접수가 거부되면(`SendCommand`가 false) 콜백은 호출되지 않는다.

## 스레드 유틸
    using (mutex.Lock()) { ... }                         // SpinMutex: 아주 짧은 구역. 긴 구역은 PLock(Monitor)
    using (rw.Read()) { ... }  using (rw.Write()) { ... } // RWSpinLock(쓰기 우선) / 긴 구역은 PRWLock

    var q = new LockFreeQueue<Job>(4096);                // 고정 크기 MPMC. TryEnqueue/TryDequeue, 블로킹은 Enqueue/Dequeue(timeoutMs)
    var bq = new BlockingTaskQueue<Job>(maxQueueSize: 1000);   // Push/PushBatch/Pop/PopChunk, SetProducerDone()으로 소비자 종료
    var dbq = new DoubleBufferQueue<Packet>();           // 여러 생산자 -> 한 소비자. 틱마다 SwapInto(ref list)

    var map = new ClusterSpinMap<long, Player>(clusterCount: 16);   // 클러스터별 RWSpinLock
    using var pool = new WorkerPool(threadCount: 4, name: "IO");    // Post(Action), Stop(drain)

스핀 락은 재진입이 안 되고 보유 중 블로킹 호출을 하면 안 된다. 짝이 맞지 않는 Exit는 예외로 드러난다.

### C++ 대응표
| C++ | C# (`Core.Util`) | 비고 |
|---|---|---|
| `SpinLock<Preset>`, `SpinLockGuard`, `TrySpinLockGuard`, `SPIN_LOCK` | `SpinMutex`, `Lock()`, `TryLock()`, `LockHere()` | 프리셋: `SpinPreset.LightWeight/Default/HeavyContention/OverSubscribed` |
| `RWSpinLock<Preset>`, `ReadLockGuard`, `WriteLockGuard`, `CustomLockGuard`, `LockType` | `RWSpinLock`, `Read()`, `Write()`, `Lock(LockType)`, `TryRead()/TryWrite()` | 상태 비트 배치와 쓰기 우선 정책 동일. 프리셋 `ReadHeavy/Default/WriteContention` |
| `CacheAlignment.h` (`kCacheLineSize`, `CachePaddedAtomic`) | `CacheLine.Size`, `PaddedInt32`, `PaddedInt64` | 락과 큐 커서는 스스로 캐시라인을 독점한다 |
| `CSRWLock`, `ExclusiveLockGuard`, `SharedLockGuard`, `Try*Guard`, `CSRWCustomLockGuard` | `SrwLock`, `Exclusive()`, `Shared()`, `TryExclusive()/TryShared()`, `Lock(LockType)` | Windows는 네이티브 SRWLOCK, 그 외는 `RWSpinLock` |
| `PLock`, `PRWLock`, 각 가드 | `PLock`(Monitor), `PRWLock`(ReaderWriterLockSlim) | 이식성과 조건 대기를 위해 Monitor 기반. 네이티브가 필요하면 `SrwLock` |
| `CDeadLockProfiler` | `DeadLockProfiler` | 위반 시 CRASH 대신 `Mode`(Throw/Log/FailFast) |
| `CThreadManager` | `ThreadManager` | `ThreadStartHook`/`ThreadExitHook`이 `InitTLS`/`DestroyTLS` 자리 |
| `CSyncValue<T>` | `SyncValue<T>` | |
| `CCriticalSection`, `CCSLockGuard` | `CriticalSection`, `Lock()` | 재진입, 스핀 횟수 인자 유지 |

### 데드락 프로파일러
락 이름으로 "A를 쥔 채 B를 얻음" 순서를 그래프에 기록하고, 반대 순서(사이클)가 나오면 락을 얻기 *전에* `LockOrderException`으로 알린다. 두 스레드가 실제로 서로를 기다리기 전에 순서가 어긋난 호출 한 번만으로 잡힌다.

    using (sessionLock.Lock("Session")) using (roomLock.Lock("Room")) { ... }   // Session -> Room 기록
    using (roomLock.Lock("Room")) using (sessionLock.Lock("Session")) { ... }   // 반대 순서: Session을 얻기 전에 예외

- 락 4종(`SpinMutex`, `RWSpinLock`, `PLock`, `PRWLock`)의 `Enter/Exit/TryEnter/Lock()/Read()/Write()`가 선택 인자 `name`을 받는다. 이름을 안 주면 추적하지 않는다. 같은 용도의 락 여러 개는 같은 이름을 쓴다(이름이 노드).
- 락 클래스 안의 호출은 `USE_DEADLOCK_PROFILER` 심볼이 있는 빌드에서만 들어간다. `Core.Util`은 Debug에서 정의하고 릴리스에서는 코드에서 사라진다. 릴리스에서도 쓰려면 csproj의 `DefineConstants`에 추가한다.
- 설정: `DeadLockProfiler.Mode`(`Throw` 기본 / `Log` / `FailFast`), `ViolationHandler`, `StrictSelfLock`(같은 이름 재획득을 자기 대기로 판정), `Enabled`(시작 시 한 번만). 진단: `HeldLocks()`, `DumpGraph()`.
- 해제 순서는 기본이 엄격하다(`StrictUnlockOrder`): 가장 최근에 쥔 락이 아니면 `INVALID_UNLOCK`, 쥔 락이 없으면 `MULTIPLE_UNLOCK`으로 알린다(C++의 CRASH 대응). 끄면 다른 순서의 해제를 허용한다.
- 락의 `LockHere()/ReadHere()/WriteHere()`는 호출한 메서드 이름을 락 이름으로 쓴다(C++ 매크로의 `__func__`).
- 직접 쓰려면 락을 얻기 전에 `PushLock(name)`, 푼 뒤에 `PopLock(name)`, 기다리지 않는 try 성공 뒤에는 `PushTryLock(name)`을 부른다.
