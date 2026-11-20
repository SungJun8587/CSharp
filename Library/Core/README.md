# Core

C# 공용 라이브러리. 어셈블리 셋으로 나뉘며 `Core.Net`, `Core.Redis` 모두 `Core.Util`을 참조하고 `Core.Redis`는 `Core.Net`(SocketUtils)도 참조한다.

| 프로젝트 | 네임스페이스 | 내용 |
|---|---|---|
| `Util/Core.Util.csproj` | `Core.Util` | 의존성 없는 공용 유틸: 로그(`Logging/`), 파일 쓰기(`FileUtil`, `FileReplace`), 문자열(`Text/`), 설정/JSON 로드(`Config/`), 풀·제한기·지표·보안·컬렉션 등(아래 "공용 유틸" 표). 스레드 유틸(`Util/Threading`): 락(`SpinMutex`, `RWSpinLock`, `PLock`, `PRWLock`), 큐(`LockFreeQueue`, `BlockingTaskQueue`, `DoubleBufferQueue`, `ChunkedSwapQueue`, `SpinLockQueue`), 샤딩 맵(`ClusterSpinMap`), 고정 스레드 실행기(`WorkerPool`), 락 순서 진단(`DeadLockProfiler`), 스레드 관리(`ThreadManager`), `SrwLock`, `CriticalSection`, `SyncValue`, 열거형 원자 연산(`AtomicEnum<T>`, `InterlockedEx`), 결과를 기다리지 않는 작업(`BackgroundJob`), 캐시라인 패딩(`CacheLine`, `PaddedInt32/64`). 앞으로의 Util 클래스는 여기에 추가한다 |
| `Net/Core.Net.csproj`   | `Core.Net`  | 네트워크: 세션, 리스너, 송수신 버퍼, TCP/UDP/TLS 서비스(TCP·TLS 서비스는 슬롯·통계·유휴 검사·종료 대기를 공통부로 공유하고, TLS 송신은 작은 패킷을 TLS 레코드 크기까지 합쳐 쓴다), 패킷 디스패처(`PacketDispatcher<TContext>`: 직접 등록 또는 `[PacketHandler]` 자동 등록), 소켓 유틸(`SocketUtils.ConfigureTcp`로 TCP/TLS 서비스가 같은 소켓 옵션·KeepAlive 적용, `NetAddress.TryCreate`/`TryCreateAsync`는 잘못된 호스트/포트에 예외 없이 false, `HostResolver`는 IP 문자열은 DNS 없이·이름은 제한 시간과 동시 해석 수 상한(4)을 두고 해석하며 Redis 클라이언트도 이를 쓴다), 사용할 만한 로컬 IPv4 주소 고르기(`LocalAddress`), 요청 id 로 응답을 짝짓는 타임아웃 대기 테이블(`PendingRequests<TResponse>`), `INetLogger`와 `LogManagerNetLogger`(LogManager에 연결). TCP 세션(`Session`)과 TLS 세션(`TlsSession`)은 송신 큐(`SendQueue`: 소유권·종료 예약·상한)와 세션 레지스트리/방송/통계(`SessionTable<T>`)를 공유한다 |
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

문자열 인자/응답은 UTF-8로 인코딩/디코딩하고(`RedisValue.StrVal`), 바이너리는 `RedisBinaryArgs`로 보내고(`IReadOnlyList<string>`이라 기존 SendCommand에 그대로 넘긴다) `RedisValue.BytesVal`로 손상 없이 읽는다. 구독 메시지는 `SubscribeBinary`/`PSubscribeBinary`가 페이로드를 바이트 그대로 준다. 풀은 연결당 동시에 명령 하나만 보내고, 연결이 모두 사용 중이면 대기 큐(최대 2000건, 5초)에 둔다. 접수가 거부되면(`SendCommand`가 false) 콜백은 호출되지 않는다.

RESP 파서는 증분 방식이다. 응답이 여러 번에 나뉘어 도착해도 이미 훑은 부분을 다시 훑지 않으므로 원소가 많은 배열(`LRANGE`, `HGETALL` 등)도 수신 크기에 비례해 처리한다. 배열 원소 수 상한은 `RedisConst.MaxArrayElements`(4,194,304), 중첩 깊이 상한은 `MaxParseDepth`(64)이고 넘으면 `ProtocolError`로 연결을 끊는다. 파서는 수신 루프만 소유하며, 연결을 닫을 때의 초기화도 수신 루프가 끝나는 시점에 수행한다.

## 스레드 유틸
    using (mutex.Lock()) { ... }                         // SpinMutex: 아주 짧은 구역. 긴 구역은 PLock(Monitor)
    using (rw.Read()) { ... }  using (rw.Write()) { ... } // RWSpinLock(쓰기 우선) / 긴 구역은 PRWLock

    var q = new LockFreeQueue<Job>(4096);                // 고정 크기 MPMC. TryEnqueue/TryDequeue, 블로킹은 Enqueue/Dequeue(timeoutMs)
    var bq = new BlockingTaskQueue<Job>(maxQueueSize: 1000);   // Push/PushBatch/Pop/PopChunk, SetProducerDone()으로 소비자 종료
    var dbq = new DoubleBufferQueue<Packet>();           // 여러 생산자 -> 한 소비자. 틱마다 SwapInto(ref list)
    var jobs = new JobQueue();                           // 여러 스레드에서 Push한 잡을 순서대로 한 번에 하나씩 ThreadPool에서 실행(락 없이 방/플레이어 상태 보호)
    jobs.PushAfter(500, () => { ... });                  // 지연 실행(공용 JobTimer). ScheduledJob.Cancel()로 취소, jobs.Close()가 남은 예약을 함께 취소

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

## 공용 유틸 (Core.Util)

모두 `namespace Core.Util;` 이며(폴더는 정리용일 뿐 네임스페이스에 영향이 없다) 관련 있는 것끼리 폴더로 묶는다.
검증은 `demo/UtilToolsDemo`(`dotnet run`).

**폴더 기준**: 관련 파일이 둘 이상이거나 앞으로 늘어날 가능성이 높으면 폴더로 묶고, 하나뿐일 가능성이 높으면 `Util` 루트에 둔다.

| 위치 | 파일 | 용도 |
|---|---|---|
| Logging/ | `Log`, `LogManager`(Log.cs), `LogThrottle` | 로그 기록(기본 비동기: 호출 스레드는 큐에 넣기만 하고 쓰기 스레드가 씀 — 파일/디버그용과 콘솔용 스레드가 따로라 콘솔이 막혀도 파일 로그는 밀리지 않음, 큐 상한 10만 줄 초과분은 버리고 셈, `Flush`/`Shutdown`이 비움, `Async=false`로 동기 전환; `MinLevel` 미만은 서식 처리 전에 버림; 파일 열기 실패 시 5초간 파일 쓰기를 쉼), 같은 로그를 간격당 한 줄로 제한(막힌 횟수 표시) |
| Text/ | `AsciiChar`, `TextUtil` | 로캘과 무관한 ASCII 처리, 문자열 다루기 |
| Config/ | `JsonTableLoader`, `ConfigLoader`, `HotConfig<T>` | 엄격한 JSON 로드(기획 테이블/설정), 파일 변경 시 원자적 교체 |
| Pooling/ | `ObjectPool<T>`, `ListPool<T>`, `StringBuilderPool` | 락 없는 객체 재사용, 임시 List/StringBuilder 할당 제거 |
| Resilience/ | `TokenBucket`, `SlidingWindowCounter`, `KeyedRateLimiter<T>`, `RetryPolicy`, `BackoffState`, `CircuitBreaker` | 과부하·장애 대응: 평균 속도/창 단위 상한/키별 제한, 지수 백오프+지터 재시도, 대상별 재연결 백오프 상태, 서킷 브레이커 |
| Metrics/ | `Counter`, `Gauge`, `Histogram`, `MetricRegistry` | 지표 수집과 Prometheus 텍스트 출력 |
| Security/ | `AuthTokenSigner`, `NonceCache` | HMAC-SHA256 서명 토큰(키 교체, 만료), 재사용 방지 |
| Collections/ | `LruCache`, `TtlCache`, `SpatialHash<T>`, `HashRing<T>`, `MinHeap<T>`, `HandleTable<T>`, `RingBuffer<T>`, `BitSet`, `FlagSet64`, `HistoryBuffer<T>` | LRU/만료 캐시, 균일 격자 범위 질의(AOI), 일관 해싱, 핸들로 갱신·삭제하는 최소 힙, 세대 번호 ID 테이블, 고정 크기 원형 큐, 비트 집합/64플래그 값 |
| Gameplay/ | `CooldownSet<T>`, `BuffTimerSet<T>`, `StatModifierSet`, `GridPathfinder` | 쿨다운+공용 쿨다운, 버프 지속시간·중첩(만료 힙), 능력치 보정 계산(출처별 제거·변경 목록), 격자 A*+경로 다듬기 |
| Random/ | `FastRandom`, `WeightedTable` | xoshiro256** 난수, 셔플, 가중치 선택(alias O(1)) |
| Binary/ | `BitUtil`, `BitWriter`, `BitReader`, `CompressionUtil`, `CompressionFrame` | 비트 연산/직렬화, Brotli·Deflate와 압축 프레임 |
| Events/ | `EventBus`, `Signal<T>` | 타입 기반 이벤트, 발행 중 구독 변경 안전 |
| Threading/ | 락, 큐, 워커 풀, `JobQueue`/`JobTimer` 등 | (아래 "스레드 유틸" 참고) |
| (루트) | `FileUtil`, `TimeUtil`, `ITimeProvider`, `SnowflakeIdGenerator`, `CommandLine`, `StateMachine<T>`, `TimerWheel`, `NamedTimers<TKey>`, `WriteBehindQueue<K,V>`, `WriteBehindWorker<K,V>`, `ShutdownCoordinator`, `GameCalendar`, `DirectoryHash` | 파일 쓰기, 시간, 시계 추상화(시스템/수동), 노드별 64비트 ID, 명령줄 파서, 상태 기계, 타이머 휠, 같은 키를 합쳐 모아 내보내는 쓰기 지연 큐, 순서·시간 제한이 있는 종료 절차 |

스레드 안전성은 클래스 주석을 따른다. 락이 없는 단일 스레드용(`TimerWheel`, `NamedTimers`, `TokenBucket`, `LruCache`, `SpatialHash`, `EventBus`, `StateMachine`)과
여러 스레드용(`ObjectPool`, `Metrics`, `SnowflakeIdGenerator`, `KeyedRateLimiter`, `LogThrottle`, `NonceCache`, `HashRing` 읽기)이 구분된다.

### 사용 메모
- 검증: `dotnet run -c Release --project demo/UtilToolsDemo`(정확성·동시성 스트레스), `... -- bench`(단일 스레드 처리량), `... -- soak 300`(혼합 부하 300초, 힙·항목 수 증가 판정).
- **풀링 효과**: 작은 `List<int>`는 풀을 써도 새로 만드는 것과 속도가 비슷하다(측정: 약 28ns vs 29ns). 풀은 GC 압력(gen0 빈도)을 줄이는 용도이고,
  크거나 만드는 비용이 큰 객체에서 이득이 크다. 핫패스에 무조건 넣지 말고 측정한다.
- **키 교체(`AuthTokenSigner`)**: 새 키를 검증용으로 먼저 모든 서버에 배포 → 발급 서버의 현재 키 변경 → 토큰 만료 시간이 지난 뒤 옛 키 제거.
- **압축**: 네트워크/저장 경계에서는 `CompressionFrame.Pack/Unpack`을 쓴다(원본 길이를 할당 전에 검사해 압축 폭탄을 막는다).
- **`LogThrottle` 키**는 정적 문자열만 쓴다. 동적 값을 키에 넣으면 상한(1024) 이후 공용 키로 합쳐진다.
- **`SnowflakeIdGenerator` 주의**: 같은 ID 공간을 쓰는 프로세스는 `nodeId`가 서로 달라야 한다(설정으로 유일하게 관리). 프로세스를 재시작하면서 시계가 같은 ms 안에서 거꾸로 가면
  ID가 겹칠 수 있다(한 프로세스 안에서는 시계가 뒤로 가도 ID가 줄지 않는다).
- **`CommandLine`**: 스키마 없이는 `--verbose file.txt`의 `file.txt`가 `verbose`의 값으로 읽힌다. 값을 받지 않는 플래그는 생성자의 `flagNames`로 선언한다.
- **`AuthTokenSigner`**: 정규 base64url 인코딩만 받는다(끝 글자의 남는 비트를 바꾼 변형 문자열은 거절).
- 단일 스레드용(락 없음): `MinHeap`, `HandleTable`, `RingBuffer`, `BitSet`, `CooldownSet`, `BuffTimerSet`, `StatModifierSet`, `HistoryBuffer`, `GridPathfinder`(탐색 배열을 재사용하므로 존/스레드마다 인스턴스 하나). 스레드 안전: `WriteBehindQueue`, `ManualTimeProvider`, `AtomicEnum`, `BackgroundJob`, `GameCalendar`, `DirectoryHash`, `LocalAddress`, `PendingRequests`.
- `MinHeap`은 핸들로 갱신·삭제하고 같은 우선순위를 넣은 순서대로 꺼낼 때 쓴다. 그런 기능이 필요 없으면 BCL `PriorityQueue`가 빠르다(2천 개 유지 시 push+pop 한 쌍이 약 80ns 대 38ns).
- `HandleTable`의 `HandleId.ToUInt64()`는 패킷에 실을 수 있고, 지워진 뒤 자리가 재사용돼도 옛 ID는 조회되지 않는다.
- `NamedTimers<TKey>`: `TimerWheel` 위에 이름 → 타이머 매핑을 얹은 래퍼. `Set`/`SetRepeating`으로 만들고 `Exist`, `EndMs`, `RemainingMs`로 조회, `Cancel`/`Clear`로 취소한다. 같은 이름으로 다시 만들면 기존 타이머를 교체하고, 한 번짜리는 콜백 전에 목록에서 빠지므로 콜백 안에서 같은 이름으로 다시 걸 수 있다. 콜백 예외는 이름과 함께 `onError`로 보고하고 다른 타이머에 영향이 없다. 시간 기준과 스레드 모델, 반복 타이머의 따라잡기(밀린 횟수 건너뜀)는 `TimerWheel`과 같다.
- `BuffTimerSet`은 만료를 최소 힙으로 관리해 `Tick`이 만료된 것만 처리한다. 주기 효과(도트)는 `TimerWheel`에 따로 건다.
- `StatModifierSet` 계산식: `clamp((기본+ΣFlat) × max(0,1+ΣPercentAdd) × ΠMultiply)`, Override가 있으면 그 값을 clamp. `DrainChanged`는 지난번 이후 값이 실제로 달라진 능력치만 준다.
- `GridPathfinder`는 칸당 약 17바이트의 탐색 배열을 맵 크기만큼 미리 잡는다(1024×1024 ≈ 17MB). 맵이 크거나 존이 많으면 노드 상한이 있는 희소 A*가 낫다.
- `GridPathfinder` 비용은 직선 10/대각 14이고 대각 이동은 인접한 두 칸이 모두 비어 있어야 한다. 결과는 이 비용 기준 최단 경로이며, `Smooth`는 직선으로 갈 수 있는 중간 점을 건너뛴다(최단성은 보장하지 않는다).
- `WriteBehindQueue`는 키가 처음 들어온 시각부터 `minAge`가 지나야 나온다(자주 바뀌는 값이 매번 저장되지 않는다). 저장 실패는 `Requeue`로 되돌리고, 종료 때는 `DrainAll`로 비운다.
- `WriteBehindWorker<K,V>`는 이 큐를 전용 스레드로 비워 저장 함수(`saveBatch`/`saveOne`)에 넘긴다. 묶음이 실패하면 한 건씩 나눠 재시도하고, 한 건이 `maxAttempts`번 실패하면 버리며, 실패 뒤에는 `BackoffState`로 쉰다. 저장 중이거나 대기 중인 최신 값은 `TryGetPending`으로 읽고, `Flush`/`Dispose`는 남은 것을 비운다. DB 어댑터는 저장 함수만 넘기면 된다.
- `ShutdownCoordinator`: `Register`(order 오름차순) → `AttachProcessSignals()` → `await WhenRequested` → `await RunAsync()`. 훅마다 시간 제한이 있고 전체 제한을 넘으면 남은 훅은 건너뛴다.
- `ITimeProvider`: 시간을 받는 유틸에 `AsMonotonicClock()`을 넘기면 시험에서 `ManualTimeProvider`로 시간을 움직일 수 있다.
- `GameCalendar`: 시간대(`TimeZoneInfo`)를 넘기고 시각은 unix ms로 주고받는다. `NextDailyMs`/`PrevDailyMs`/`NextWeeklyMs`는 초기화 시각 계산, `GameDayIndex`/`IsSameGameDay`는 자정이 아닌 시각(예: 5시)에 바뀌는 "게임 날짜", `IsInDailyWindow`는 자정을 넘는 구간까지 지원하는 하루 중 구간 판정이다. 날짜는 모두 그 시간대의 벽시계 기준이며, 서머타임에서 건너뛰는 시각은 구간이 끝난 직후로, 두 번 나타나는 시각은 첫 번째로 해석한다. 현재 시각을 읽지 않으므로 `ITimeProvider`의 값을 넘겨 시험한다.
- `DirectoryHash.Compute(dir, include)`: 폴더 기준 상대 경로('/' 구분)·길이·내용으로 SHA-256을 만든다. 폴더 위치와 무관하게 내용이 같으면 같은 값이라 서버/클라이언트의 데이터 폴더(GameData 등)가 같은지 비교하는 데 쓴다. 경로의 대소문자도 해시에 들어간다.
- `AtomicEnum<T>`: 기반 형식과 무관한 스레드 안전 열거형 값. `TryTransition(from, to)`는 같은 전이를 여러 스레드가 시도해도 하나만 성공한다. `InterlockedEx`는 필드를 직접 다루는 ref 버전이며 4/8바이트 열거형만 된다(byte/short 열거형은 `AtomicEnum<T>`).
- `BackgroundJob.Run`: 기다리지 않는 작업을 스레드풀에 올리고, 예외는 `ExceptionHandler`(없으면 `LogThrottle` 오류 로그)로 보낸다. 호출 쪽 ExecutionContext(AsyncLocal)는 넘기지 않으므로 필요한 값은 인자로 캡처한다. 순서 보장이나 개수 제한이 필요하면 `JobQueue`/`WorkerPool`.
- `HistoryBuffer<T>`: 일련번호(틱 번호)로 최근 Capacity개를 보관하는 단일 스레드 버퍼. 슬롯마다 번호를 기억해 안 쓴 번호나 덮어써진 번호는 옛 값을 돌려주지 않고 실패한다. `TryGetAtOrBefore`는 번호가 건너뛰어 쓰였을 때 그 시점 이하에서 가장 가까운 값을 찾는다. 틱별 스냅샷을 쌓아 렉 보상/롤백에 쓴다.
- `LocalAddress.GetIPv4()`: 동작 중인 어댑터의 IPv4만 돌려주고 루프백, 링크 로컬(169.254.x.x), 가상 어댑터(이름/설명에 제외 문자열 포함)는 뺀다. 가상 어댑터 판정은 휴리스틱이라 환경에 맞지 않으면 `excludeTokens`를 지정한다.
- `PendingRequests<TResponse>`: 요청 id(1 이상)로 응답을 요청에 연결한다. `Register`(콜백) 또는 `RequestAsync`(Task)로 등록하고, 응답 핸들러에서 `TryComplete(id, owner, response)`를 부른다. owner 가 요청자와 다르면 거부하므로 다른 세션이 남의 요청에 응답할 수 없다. 응답이 없으면 호출자가 주기적으로 부르는 `Advance()`가 `Timeout`으로 끝내고, 세션이 끊길 때 `CancelOwner(sessionId)`로 그 세션의 요청을 `OwnerClosed`로 끝낸다. 콜백은 요청당 정확히 한 번, 락 밖에서 호출된다. `maxPerOwner`(기본 1024)로 소유자별 대기 수를 제한한다.
