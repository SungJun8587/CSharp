// Zone.cs : implementation of the Zone class.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Core.Util;

namespace Game.ZoneCore;

/// <summary>
/// 존 생성 설정값.
/// <list type="bullet">
/// <item>TickIntervalMs : 고정 틱 간격(ms). 필드 전투는 50~100ms 권장</item>
/// <item>MaxCatchUpTicks : 한 번의 깨어남에서 밀린 틱을 연속 처리할 최대 횟수. 초과분은 건너뛰어(skipped) 죽음의 나선(spiral of death)을 막는다</item>
/// <item>MaxInboxSize : 입력 큐 상한. 초과 시 Post()가 false를 반환(백프레셔)</item>
/// <item>MaxJobsPerTick : 틱 하나에서 실행할 입력 작업 상한. 0이면 무제한. 넘은 작업은 다음 틱에 이어서 처리된다(순서 유지)</item>
/// <item>TimerResolutionMs : Windows에서 존 스레드가 살아 있는 동안 시스템 타이머 해상도를 이 값(ms)으로 올린다. 프로세스 전역 설정이므로
/// 호스트 애플리케이션이 이미 설정했다면 0으로 두어 건드리지 않게 한다. Windows 외 플랫폼에서는 무시된다</item>
/// </list>
/// </summary>
public sealed class ZoneConfig
{
    public uint ZoneId;                     // 존 식별자
    public uint TickIntervalMs = 50;        // 고정 틱 간격(ms)
    public uint MaxCatchUpTicks = 5;        // 연속 따라잡기 틱 상한
    public uint MaxInboxSize = 65536;       // 입력 큐 최대 적재 수
    public uint MaxJobsPerTick;             // 틱당 입력 작업 처리 상한 (0 = 무제한)
    public uint TimerResolutionMs = 1;      // Windows 타이머 해상도(ms), 0이면 변경하지 않음

    public ZoneConfig Clone() => (ZoneConfig)MemberwiseClone();
}

/// <summary>존 틱 루프 통계 스냅샷. 모니터링/부하 테스트용.</summary>
public struct ZoneStats
{
    public ulong TickCount;         // 실행된 총 틱 수
    public ulong SkippedTicks;      // 따라잡기를 포기하고 건너뛴 틱 수
    public ulong OverrunTicks;      // 처리 시간이 틱 간격을 초과한 틱 수
    public ulong ProcessedJobs;     // 처리된 입력 작업 수
    public ulong FailedJobs;        // 예외로 중단된 작업 수
    public ulong RejectedJobs;      // 큐 포화/정지로 거부된 Post 수
    public ulong LastTickUs;        // 마지막 틱 처리 시간(us)
    public ulong MaxTickUs;         // 시작 후 최대 틱 처리 시간(us)
    public ulong TotalTickUs;       // 시작 후 틱 처리 시간 합계(us). 평균 = TotalTickUs / TickCount
    public ulong[]? TickBuckets;    // 틱 처리 시간 분포. Zone.TickBucketEdgesUs 경계로 나눈 TickBucketCount개 구간의 틱 수 (마지막 구간은 마지막 경계 이상)
}

/// <summary>
/// 필드(맵/인스턴스) 하나를 담당하는 단일 스레드 시뮬레이션 단위.
///
/// 존 내부 상태(엔티티, 몬스터, 버프 등)는 오직 존 스레드에서만 접근하므로 별도의 락이 필요 없다.
/// 외부 스레드(네트워크 워커 등)는 Post()로 작업을 넣고, 존 스레드가 매 틱 시작 시 입력 큐를 비우며 처리한다.
///
/// 틱 순서: DrainInbox -> 타이머 -> OnUpdate(파생 클래스 확장 지점).
/// 시간은 고정 타임스텝(논리 시간)으로 진행하여 결정적(deterministic)이다.
/// 밀려서 건너뛴 틱은 논리 시간에도 반영되지 않는다(과부하 시 시뮬레이션이 느려질 뿐 점프하지 않는다).
///
/// 생명주기: Start()는 한 번만 가능하다(정지 후 재시작 불가 — 입력 큐가 영구 정지되므로).
/// 사용자 코드(OnStart/OnUpdate/OnStop/작업/타이머)에서 던진 예외는 격리되어 로그만 남기고 존은 계속 동작한다.
/// </summary>
public class Zone : IDisposable
{
    /// <summary>틱 처리 시간 분포의 구간 경계(us). 값이 경계 미만이면 그 구간에 센다: 1ms, 2ms, 5ms, 10ms, 25ms, 50ms 미만, 그 이상.</summary>
    public static readonly uint[] TickBucketEdgesUs = { 1000, 2000, 5000, 10000, 25000, 50000 };

    /// <summary>틱 처리 시간 분포의 구간 수.</summary>
    public const int TickBucketCount = 7;

    private readonly long[] _tickBuckets = new long[TickBucketCount];   // 구간별 틱 수 (존 스레드만 쓰고 다른 스레드는 Volatile.Read로 읽는다)
    private long _totalTickUs;                                          // 틱 처리 시간 합계
    private readonly ZoneConfig _config;                // 생성 설정

    private Thread? _thread;                            // 존 틱 스레드
    private int _threadId;                              // 존 스레드의 관리 스레드 ID (IsZoneThread 판별용, 0이면 없음)
    private int _started;                               // Start() 호출 여부 (재시작 차단)
    private volatile bool _running;                     // 틱 루프 동작 여부
    private volatile bool _accepting;                   // Post 수락 여부
    private volatile bool _threadAlive;                 // 틱 스레드가 시작되어 아직 끝나지 않았는지 (Stop()이 _running을 내린 뒤에도 정리 작업이 끝날 때까지 true)
    private readonly object _joinLock = new object();   // Stop()의 join 직렬화 (여러 스레드가 동시에 Stop()해도 join은 한 번만)
    private readonly object _sleepLock = new object();  // 틱 대기용 모니터. Stop 시 대기 중인 스레드를 깨운다

    private readonly DoubleBufferQueue<Action<Zone>> _inbox = new DoubleBufferQueue<Action<Zone>>();    // 입력 큐 (MPSC)
    private List<Action<Zone>> _drainBuf = new List<Action<Zone>>(1024);   // 존 스레드 전용 스왑 버퍼 (할당 재사용)
    private readonly Action<Action<Zone>> _runJobFn;     // RunJob (틱마다 타이머 실행에 넘긴다)
    private readonly Action _updateFn;                   // RunUpdate (틱마다 Guarded에 넘긴다)
    private int _drainPos;                              // _drainBuf에서 다음에 실행할 위치 (틱당 예산으로 끊긴 경우 이어서 처리)

    private readonly TimerHeap<Action<Zone>> _timers = new TimerHeap<Action<Zone>>();   // 지연 작업 (존 스레드 전용)

    private int _pendingJobs;                           // 아직 실행되지 않은 입력 작업 수 (MaxInboxSize 백프레셔용)
    private int _activePosters;                         // Post() 실행 중인 호출 수 (종료 시 큐를 닫기 전 대기용)
    private long _rejectedJobs;                         // 통계: 거부 작업 (Post 호출 스레드들이 갱신)

    private long _nowMs;                                // 존 논리 시간(ms)

    // 아래 통계는 존 스레드만 갱신하고 다른 스레드는 읽기만 한다.
    private long _tickCount;
    private long _skippedTicks;
    private long _overrunTicks;
    private long _processedJobs;
    private long _failedJobs;
    private long _lastTickUs;
    private long _maxTickUs;
    private long _windowMaxTickUs;                      // ConsumeMaxTickUs() 이후의 최대 틱 시간 (존 스레드가 갱신, 소비자가 리셋)

    /// <summary>존을 생성한다. 스레드는 Start() 호출 시 시작된다.</summary>
    public Zone(ZoneConfig config)
    {
        _runJobFn = RunJob;         // 인스턴스 메서드 그룹은 쓸 때마다 델리게이트를 새로 만들므로 한 번만 만들어 둔다
        _updateFn = RunUpdate;
        _config = config.Clone();
        if (_config.TickIntervalMs == 0)
            _config.TickIntervalMs = 1;
        if (_config.MaxCatchUpTicks == 0)
            _config.MaxCatchUpTicks = 1;
    }

    /// <summary>동작 중이면 정지하고 스레드를 합류시킨다.</summary>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    /// <summary>틱 스레드가 동작 중인지.</summary>
    public bool IsRunning => _running;

    /// <summary>존 식별자.</summary>
    public uint ZoneId => _config.ZoneId;

    /// <summary>고정 틱 간격(ms).</summary>
    public uint TickIntervalMs => _config.TickIntervalMs;

    /// <summary>존 논리 시간. 틱마다 틱 간격만큼 증가한다. (존 시작 후 경과한 논리 시간, ms)</summary>
    public ulong NowMs => (ulong)Volatile.Read(ref _nowMs);

    /// <summary>
    /// 틱 스레드를 시작한다. 존당 한 번만 호출할 수 있다.
    /// </summary>
    /// <returns>시작했으면 true, 이미 시작했거나(정지 후 재시작 포함) 스레드 생성에 실패하면 false</returns>
    public bool Start()
    {
        // 정지된 존은 입력 큐가 영구히 닫혀 있으므로 재시작을 허용하지 않는다.
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return false;

        _threadAlive = true;
        _running = true;
        _accepting = true;
        Thread.MemoryBarrier();

        try
        {
            _thread = new Thread(ThreadMain)
            {
                Name = "Zone-" + _config.ZoneId,
                IsBackground = false,
            };
            _thread.Start();
        }
        catch (Exception)
        {
            _accepting = false;
            _running = false;
            _threadAlive = false;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 틱 스레드를 정지한다. 새 Post는 즉시 거부되며, 이미 큐에 들어간 작업은 종료 직전에 마저 처리한다.
    /// 존 스레드 내부에서 호출하면 정지 요청만 하고 join은 외부(또는 Dispose)에서 수행된다.
    /// </summary>
    public void Stop()
    {
        _accepting = false;
        Thread.MemoryBarrier();

        lock (_sleepLock)
        {
            _running = false;
            Monitor.PulseAll(_sleepLock);
        }

        if (IsZoneThread())
            return;

        // 여러 스레드가 동시에 Stop()해도 join은 한 번만 일어나게 직렬화한다.
        lock (_joinLock)
        {
            Thread? t = _thread;
            if (t != null && t.IsAlive)
                t.Join();
        }
    }

    /// <summary>
    /// 외부 스레드(네트워크 워커 등)에서 존으로 작업을 넘긴다. 스레드 안전.
    /// 작업은 다음 틱 시작 시 존 스레드에서 FIFO 순서로 실행된다.
    /// true를 반환한 작업은 Stop() 이후에도 반드시 실행된다(유실 없음).
    /// Post 진입을 _activePosters로 등록한 뒤 수락 여부를 검사하고, 존 스레드는 종료 시 등록된 Post가
    /// 모두 끝나기를 기다린 뒤에 큐를 닫는다. 따라서 "수락 검사 통과 -> 큐 닫힘 -> Push 폐기" 경합이 없다.
    /// </summary>
    /// <returns>적재 성공 시 true, 존이 정지 상태이거나 큐가 가득 찼으면 false</returns>
    public bool Post(Action<Zone> job)
    {
        if (job == null)
            return false;

        Interlocked.Increment(ref _activePosters);

        bool posted = false;

        if (_accepting)
        {
            // DoubleBufferQueue는 크기 상한이 없으므로 대기 작업 수를 별도 카운터로 제한한다.
            if ((long)Interlocked.Increment(ref _pendingJobs) - 1 >= _config.MaxInboxSize)
            {
                Interlocked.Decrement(ref _pendingJobs);
            }
            else
            {
                _inbox.Push(job);
                posted = true;
            }
        }

        Interlocked.Decrement(ref _activePosters);

        if (!posted)
            Interlocked.Increment(ref _rejectedJobs);

        return posted;
    }

    /// <summary>
    /// 존 내부 상태(타이머, 엔티티 테이블 등)를 직접 만져도 되는 호출인지 확인한다.
    /// 존 스레드이거나, 틱 스레드가 시작 전이거나 완전히 끝난 뒤여야 한다.
    /// Stop()이 _running을 내린 뒤에도 틱 스레드는 잔여 작업 처리와 OnStop을 하므로, 그 구간의 외부 스레드 접근을 막기 위해
    /// IsRunning이 아니라 스레드 생존 여부로 판정한다.
    /// </summary>
    protected bool CanTouchZoneState() => IsZoneThread() || !_threadAlive;

    /// <summary>
    /// 지정한 논리 시간(ms) 뒤에 작업을 실행하도록 예약하고 취소용 ID를 돌려준다.
    /// 존 스레드(또는 시작 전)에서만 호출할 수 있다. 다른 스레드는 ScheduleAfter()를 사용한다.
    /// 같은 틱에 재귀적으로 실행되지 않도록 최소 지연은 1ms로 보정한다.
    /// </summary>
    /// <returns>타이머 ID (1 이상), 호출 스레드가 부적절하거나 작업이 비어 있으면 0</returns>
    public ulong ScheduleTimer(uint delayMs, Action<Zone> job)
    {
        if (job == null || !CanTouchZoneState())
            return 0;

        if (delayMs == 0)
            delayMs = 1;

        return _timers.Schedule(NowMs + delayMs, job);
    }

    /// <summary>예약한 타이머를 취소한다. 존 스레드(또는 시작 전)에서만 호출할 수 있다.</summary>
    /// <returns>아직 실행되지 않은 타이머를 취소했으면 true</returns>
    public bool CancelTimer(ulong timerId)
    {
        if (timerId == 0 || !CanTouchZoneState())
            return false;

        return _timers.Cancel(timerId);
    }

    /// <summary>
    /// 어느 스레드에서든 지연 작업을 예약한다. (취소 ID가 필요 없을 때)
    /// 존 스레드가 아니면 Post()를 거쳐 존 스레드가 등록하며, 이 경우 지연은 등록 시점부터 계산된다.
    /// </summary>
    /// <returns>예약(또는 예약 요청 적재)에 성공하면 true, 존이 정지/포화 상태이면 false</returns>
    public bool ScheduleAfter(uint delayMs, Action<Zone> job)
    {
        if (job == null)
            return false;

        if (CanTouchZoneState())
            return ScheduleTimer(delayMs, job) != 0;

        return Post(zone => zone.ScheduleTimer(delayMs, job));
    }

    /// <summary>현재 스레드가 이 존의 틱 스레드인지.</summary>
    public bool IsZoneThread()
    {
        int id = Volatile.Read(ref _threadId);
        return id != 0 && id == Environment.CurrentManagedThreadId;
    }

    private ulong[] SnapshotTickBuckets()
    {
        ulong[] copy = new ulong[TickBucketCount];
        for (int i = 0; i < copy.Length; ++i)
            copy[i] = (ulong)Volatile.Read(ref _tickBuckets[i]);
        return copy;
    }

    /// <summary>통계 스냅샷.</summary>
    public ZoneStats GetStats()
    {
        return new ZoneStats
        {
            TickCount = (ulong)Volatile.Read(ref _tickCount),
            SkippedTicks = (ulong)Volatile.Read(ref _skippedTicks),
            OverrunTicks = (ulong)Volatile.Read(ref _overrunTicks),
            ProcessedJobs = (ulong)Volatile.Read(ref _processedJobs),
            FailedJobs = (ulong)Volatile.Read(ref _failedJobs),
            RejectedJobs = (ulong)Volatile.Read(ref _rejectedJobs),
            LastTickUs = (ulong)Volatile.Read(ref _lastTickUs),
            MaxTickUs = (ulong)Volatile.Read(ref _maxTickUs),
            TotalTickUs = (ulong)Volatile.Read(ref _totalTickUs),
            TickBuckets = SnapshotTickBuckets(),
        };
    }

    /// <summary>
    /// 직전 호출 이후의 최대 틱 처리 시간을 반환하고 0으로 리셋한다.
    /// 하트비트처럼 주기적으로 구간 최대값을 보고하는 용도다. (시작 후 누적 최대는 GetStats().MaxTickUs)
    /// </summary>
    public ulong ConsumeMaxTickUs() => (ulong)Interlocked.Exchange(ref _windowMaxTickUs, 0);

    /// <summary>존 스레드 시작 직후 한 번 호출된다. 스폰 포인트/몬스터 초기화 등에 사용한다.</summary>
    protected virtual void OnStart()
    {
    }

    /// <summary>매 틱마다 호출되는 시뮬레이션 확장 지점. 몬스터 AI, 투사체, 전투 판정, AOI 갱신, 패킷 플러시를 여기서 호출한다.</summary>
    /// <param name="dtMs">고정 틱 간격(ms)</param>
    protected virtual void OnUpdate(uint dtMs)
    {
    }

    /// <summary>존 스레드 종료 직전에 한 번 호출된다. 잔여 작업 처리 이후 실행된다.</summary>
    protected virtual void OnStop()
    {
    }

    /// <summary>
    /// 사용자 코드(가상 함수 확장 지점)를 예외로부터 격리해 실행한다.
    /// 예외가 틱 스레드 밖으로 나가면 프로세스가 종료되므로 반드시 여기서 흡수한다.
    /// </summary>
    private void Guarded(string phase, Action fn)
    {
        try
        {
            fn();
        }
        catch (Exception e)
        {
            LogManager.Error("[Zone] exception in {0}. zoneId={1}, what={2}", phase, _config.ZoneId, e);
        }
    }

    /// <summary>
    /// 존 스레드 메인 루프. 고정 타임스텝 + 누적 보정 방식.
    /// 한 번에 처리하는 따라잡기 틱을 MaxCatchUpTicks로 제한하고, 그래도 밀려 있으면 남은 틱을 건너뛰어 지연이 누적되지 않게 한다.
    /// </summary>
    private void ThreadMain()
    {
        Volatile.Write(ref _threadId, Environment.CurrentManagedThreadId);

        // 대기 해상도가 기본(약 15.6ms)이면 50ms 틱에 큰 지터가 생기므로 설정값만큼 올린다. 스레드 종료 시 자동 복원된다.
        using TimerResolutionScope timerResolution = new TimerResolutionScope(_config.TimerResolutionMs);

        Guarded("OnStart", OnStart);

        long freq = Stopwatch.Frequency;
        long stepTicks = Math.Max(1L, (long)_config.TickIntervalMs * freq / 1000);
        long nextTick = Stopwatch.GetTimestamp() + stepTicks;

        while (_running)
        {
            lock (_sleepLock)
            {
                while (_running)
                {
                    long remain = nextTick - Stopwatch.GetTimestamp();
                    if (remain <= 0)
                        break;
                    int waitMs = (int)Math.Min(int.MaxValue, (remain * 1000 + freq - 1) / freq);
                    Monitor.Wait(_sleepLock, Math.Max(1, waitMs));
                }
            }

            if (!_running)
                break;

            uint ran = 0;
            while (Stopwatch.GetTimestamp() >= nextTick && ran < _config.MaxCatchUpTicks)
            {
                RunTick();
                nextTick += stepTicks;
                ++ran;
            }

            // 따라잡기 한도에 걸려 멈춘 경우에만 건너뛴다. (한도에 걸리지 않았는데 시계를 다시 읽어 판정하면, 방금 도래한 틱 하나를 잘못 건너뛸 수 있다.)
            long now = Stopwatch.GetTimestamp();
            if (ran >= _config.MaxCatchUpTicks && now >= nextTick)
            {
                // 따라잡기 한도를 넘겨 여전히 밀려 있음: 남은 틱을 건너뛴다.
                long skipped = (now - nextTick) / stepTicks + 1;
                Interlocked.Add(ref _skippedTicks, skipped);
                nextTick += stepTicks * skipped;
            }
        }

        // 종료 절차: (1) 이미 수락 검사를 통과한 Post가 Push를 마치기를 기다린다 -> (2) 남은 작업을 처리한다.
        // Stop()이 _accepting을 먼저 내렸으므로 (1) 이후에는 새 Post가 큐에 도달하지 못한다.
        SpinWait spin = new SpinWait();
        while (Volatile.Read(ref _activePosters) != 0)
            spin.SpinOnce();

        // 예산으로 끊긴 잔여분을 먼저 비우고, 그 다음 큐에 남은 작업을 비운다.
        DrainInbox(0);
        DrainInbox(0);

        Guarded("OnStop", OnStop);

        _timers.Clear();

        Volatile.Write(ref _threadId, 0);
        _threadAlive = false;       // 마지막에 내린다: 이후부터 외부 스레드가 존 상태를 만질 수 있다
    }

    /// <summary>한 틱을 실행한다. 논리 시간을 먼저 전진시킨 뒤 입력 처리 -> 타이머 -> 시뮬레이션 순으로 진행한다.</summary>
    private void RunTick()
    {
        long begin = Stopwatch.GetTimestamp();

        ulong nowMs = NowMs + _config.TickIntervalMs;
        Volatile.Write(ref _nowMs, (long)nowMs);

        DrainInbox(_config.MaxJobsPerTick);

        _timers.RunDue(nowMs, _runJobFn);

        Guarded("OnUpdate", _updateFn);

        Volatile.Write(ref _tickCount, _tickCount + 1);

        long elapsedUs = (Stopwatch.GetTimestamp() - begin) * 1_000_000L / Stopwatch.Frequency;

        Volatile.Write(ref _lastTickUs, elapsedUs);
        Volatile.Write(ref _totalTickUs, _totalTickUs + elapsedUs);
        int bucket = 0;
        while (bucket < TickBucketEdgesUs.Length && elapsedUs >= TickBucketEdgesUs[bucket])
            ++bucket;
        Volatile.Write(ref _tickBuckets[bucket], _tickBuckets[bucket] + 1);

        if (elapsedUs > Volatile.Read(ref _maxTickUs))
            Volatile.Write(ref _maxTickUs, elapsedUs);

        // 구간 최대값은 다른 스레드가 Exchange(0)로 리셋하므로 CAS로 갱신한다.
        long prevMax = Volatile.Read(ref _windowMaxTickUs);
        while (elapsedUs > prevMax)
        {
            long seen = Interlocked.CompareExchange(ref _windowMaxTickUs, elapsedUs, prevMax);
            if (seen == prevMax)
                break;
            prevMax = seen;
        }

        if (elapsedUs > (long)_config.TickIntervalMs * 1000)
            Volatile.Write(ref _overrunTicks, _overrunTicks + 1);
    }

    /// <summary>
    /// 입력 큐의 활성 버퍼를 스왑해 통째로 가져오고, 틱 스레드 전용 버퍼에서 FIFO로 실행한다.
    /// 작업 실행 중 Post되는 새 작업은 다음 틱에 처리된다. 한 틱에 스왑은 최대 한 번이다.
    /// 예산(budget)을 넘은 작업은 버퍼에 남겨 두었다가 다음 틱에 이어서 실행하며(순서 유지), 남은 작업이 있는 동안에는 새로 스왑하지 않는다.
    /// </summary>
    /// <param name="budget">이번 호출에서 실행할 최대 작업 수 (0 = 무제한)</param>
    private void DrainInbox(uint budget)
    {
        if (_drainPos >= _drainBuf.Count)
        {
            _drainPos = 0;
            _inbox.SwapInto(ref _drainBuf);
        }

        uint ran = 0;

        while (_drainPos < _drainBuf.Count && (budget == 0 || ran < budget))
        {
            Action<Zone> job = _drainBuf[_drainPos];
            _drainBuf[_drainPos] = null!;       // 캡처한 자원을 즉시 해제 (버퍼 Clear까지 붙들고 있지 않는다)
            ++_drainPos;

            Interlocked.Decrement(ref _pendingJobs);
            RunJob(job);
            ++ran;
        }
    }

    private void RunUpdate() => OnUpdate(_config.TickIntervalMs);

    /// <summary>작업 하나를 실행한다. 예외가 발생해도 존 루프가 죽지 않도록 격리한다.</summary>
    private void RunJob(Action<Zone> job)
    {
        try
        {
            job(this);
            Volatile.Write(ref _processedJobs, _processedJobs + 1);
        }
        catch (Exception e)
        {
            Volatile.Write(ref _failedJobs, _failedJobs + 1);
            ulong failed = (ulong)_failedJobs;
            if (ShouldLog(failed))
                LogManager.Error("[Zone] job threw. zoneId={0}, failedJobs={1}, what={2}", _config.ZoneId, failed, e);
        }
    }

    /// <summary>반복되는 오류 로그가 폭주하지 않도록, 발생 횟수가 2의 거듭제곱일 때만 true를 반환한다.</summary>
    private static bool ShouldLog(ulong occurrence) => (occurrence & (occurrence - 1)) == 0;

    /// <summary>생존 기간 동안 Windows 시스템 타이머 해상도를 올리는 RAII 객체. (프로세스 전역, 참조 카운트) Windows가 아니거나 ms가 0이면 아무 일도 하지 않는다.</summary>
    private sealed class TimerResolutionScope : IDisposable
    {
        private readonly uint _ms;

        public TimerResolutionScope(uint ms)
        {
            if (ms != 0 && OperatingSystem.IsWindows())
            {
                _ms = ms;
                timeBeginPeriod(ms);
            }
        }

        public void Dispose()
        {
            if (_ms != 0)
                timeEndPeriod(_ms);
        }

        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint timeEndPeriod(uint uPeriod);
    }
}
