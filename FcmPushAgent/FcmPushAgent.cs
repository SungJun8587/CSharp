using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Dapper;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using MySqlConnector;
using Serilog;

namespace FcmPushAgent
{
    /// <summary>
    /// users 테이블의 (id, push_token) SELECT 결과를 Dapper로 매핑하기 위한 내부 DTO.
    /// Dapper는 ValueTuple을 기본적으로 지원하지 않으므로, 컬럼 별칭(AS Id, AS PushToken)과
    /// 매칭되는 단순 POCO를 사용합니다.
    /// </summary>
    internal class UserTokenRow
    {
        public long Id { get; set; }
        public string PushToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// job 1건의 실행 중 누적 카운터를 담는 컨테이너.
    /// 여러 Consumer Task가 동시에 갱신하므로 모든 접근은 Interlocked를 통해 이루어집니다.
    /// SendBatchAsync에 객체 참조로 전달함으로써 async 메서드에서 금지된 ref 파라미터를 대체합니다.
    /// </summary>
    internal class JobCounters
    {
        public long TotalRead;
        public long TotalSuccess;
        public long TotalFailure;

        /// <summary>
        /// Consumer가 FCM 발송을 완료한 배치 중 가장 최근에 확인된 최소 users.id.
        /// Producer의 lastReadId(읽은 위치)와 달리, 실제로 발송이 끝난 위치를 추적합니다.
        /// GetSafeResumeId는 이 값을 기준으로 마진을 계산해 중복 발송 범위를 최소화합니다.
        /// </summary>
        public long LastConfirmedId;

        public JobCounters(long totalRead, long totalSuccess, long totalFailure, long lastConfirmedId)
        {
            TotalRead = totalRead;
            TotalSuccess = totalSuccess;
            TotalFailure = totalFailure;
            LastConfirmedId = lastConfirmedId;
        }
    }

    /// <summary>
    /// 전역 기본 옵션. push_jobs 테이블은 예약시간/알림내용/진행상태만 갖고 있고,
    /// 발송 처리량/동시성/모니터링 관련 설정은 모두 이 클래스(appsettings.json의 PushDefaults)에서 옵니다.
    /// </summary>
    public class PushSchedulerDefaults
    {
        /// <summary>DB에서 한 번에 읽어올 row 수 (keyset pagination 1회 조회 크기)</summary>
        public int DbFetchSize { get; set; } = 2000;

        /// <summary>FCM Multicast 1회 호출당 토큰 수 (FCM 정책상 최대 500)</summary>
        public int FcmBatchSize { get; set; } = 500;

        /// <summary>job 1개당 동시에 FCM 발송하는 Consumer 워커 수</summary>
        public int ConsumerCount { get; set; } = 8;

        /// <summary>Producer-Consumer 간 Channel 버퍼 최대 크기 (이 이상 쌓이면 Producer가 대기)</summary>
        public int ChannelCapacity { get; set; } = 20000;

        /// <summary>Firebase 서비스 계정 키 파일 경로</summary>
        public string CredentialPath { get; set; } = "serviceAccountKey.json";

        /// <summary>push_jobs 테이블을 몇 초 간격으로 폴링할지</summary>
        public int PollingIntervalSeconds { get; set; } = 10;

        /// <summary>한 워커 프로세스가 동시에 병렬로 처리할 최대 job 개수.</summary>
        public int MaxConcurrentJobs { get; set; } = 3;

        /// <summary>이 시간 이상 lease가 갱신되지 않은 'running' job은 죽은 워커가 잡고 있던 것으로 보고 재선점합니다.</summary>
        public int StaleLeaseMinutes { get; set; } = 5;

        /// <summary>lease를 얼마나 자주 갱신할지 (StaleLeaseMinutes보다 충분히 짧아야 함).</summary>
        public int LeaseRenewalSeconds { get; set; } = 60;

        /// <summary>
        /// FCM에 초당 보낼 수 있는 최대 호출(Multicast) 횟수. 모든 job/Consumer가 이 한도를 공유합니다.
        /// 예: 20이면 초당 최대 20 × FcmBatchSize(500) = 10,000명에게 발송 가능.
        /// FCM 프로젝트 쿼터와 서버 처리 능력에 맞춰 조정하세요.
        /// </summary>
        public int MaxFcmCallsPerSecond { get; set; } = 20;

        /// <summary>
        /// 운영 알림(Slack 호환 Webhook)을 보낼 URL. 비어있으면 알림 기능이 비활성화됩니다.
        /// </summary>
        public string? AlertWebhookUrl { get; set; }

        /// <summary>알림 메시지에 표시할 환경 이름 (예: production, staging)</summary>
        public string EnvironmentLabel { get; set; } = "production";

        /// <summary>
        /// job 완료 시 이 실패율(0.0~1.0)을 초과하면 경고 알림을 보냅니다. 기본 5%.
        /// </summary>
        public double FailureRateAlertThreshold { get; set; } = 0.05;
    }

    /// <summary>
    /// push_jobs 테이블을 주기적으로 폴링하여, 예약시간이 도달한(또는 중단되어 재개해야 하는)
    /// 작업을 찾아 MySQL에 저장된 대량 유저에게 FCM 푸시를 발송하는 스케줄러.
    ///
    /// 운영 고려사항 반영:
    ///  - 예약시간/알림내용을 모두 DB(push_jobs)에서 읽음 -> 재배포 없이 새 발송 등록 가능
    ///  - 체크포인트 기반 재개 (프로세스 중단 후 재실행 시 이어서 진행)
    ///  - 무효(Unregistered) 토큰 자동 정리
    ///  - 취소/예외 발생 시 진행 상황 보존
    ///  - FOR UPDATE SKIP LOCKED + lease 기반 분산 잠금으로 여러 job을 동시에, 여러 인스턴스에서 안전하게 병렬 처리
    ///  - FCM 초당 호출 수를 Rate Limiter로 제어해 쿼터 초과(429 등)를 예방
    ///  - 구조화된 로깅(Serilog) + Webhook 알림으로 실패율/오류를 모니터링
    ///  - 모든 DB 접근은 Dapper를 통해 이루어짐 (raw ADO.NET 대비 매핑/파라미터 바인딩 자동화)
    /// </summary>
    public class FcmPushAgent
    {
        // users/push_jobs 테이블에 접근하기 위한 MySQL 연결 문자열 (Producer 등 직접 쿼리가 필요한 곳에서 사용)
        private readonly string _connectionString;

        // 처리량/동시성/모니터링 전역 기본값
        private readonly PushSchedulerDefaults _defaults;

        // push_jobs 테이블 조회/갱신을 담당하는 레포지토리
        private readonly PushJobRepository _jobRepo;

        // FCM에서 무효 응답을 받은 토큰을 정리하는 레포지토리
        private readonly TokenCleanupRepository _cleanupRepo;

        // 모든 job/Consumer가 공유하는 FCM 초당 호출 제한기.
        // job 단위가 아니라 프로세스 전체에서 하나만 생성해, 여러 job이 동시에 돌아도 합산 호출량이 쿼터를 넘지 않게 합니다.
        private readonly FcmRateLimiter _rateLimiter;

        // 실패율 임계치 초과, job 오류/완료 등을 외부 채널(Slack 등)로 알리는 컴포넌트
        private readonly AlertNotifier _alertNotifier;

        // 이 프로세스를 식별하는 고유 문자열. push_jobs.claimed_by에 기록되어
        // "어떤 워커가 이 job을 처리 중인지" 추적하는 데 사용됩니다.
        private readonly string _workerInstanceId;

        /// <param name="connectionString">MySQL 연결 문자열</param>
        /// <param name="defaults">처리량/동시성/모니터링 전역 기본 옵션</param>
        /// <param name="jobRepo">push_jobs 테이블 레포지토리</param>
        /// <param name="cleanupRepo">무효 토큰 정리 레포지토리</param>
        public FcmPushAgent(
            string connectionString,
            PushSchedulerDefaults defaults,
            PushJobRepository jobRepo,
            TokenCleanupRepository cleanupRepo)
        {
            _connectionString = connectionString;
            _defaults = defaults;
            _jobRepo = jobRepo;
            _cleanupRepo = cleanupRepo;
            // 워커 인스턴스를 식별하기 위한 고유 ID (머신명 + 프로세스ID + 짧은 GUID).
            // 여러 서버에 동일 프로그램을 띄워도 서로 다른 ID를 가지므로 claimed_by로 구분 가능합니다.
            _workerInstanceId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}".Substring(0, 60);

            // 프로세스 전체에서 공유하는 단일 Rate Limiter. (job마다 만들면 합산 호출량이 쿼터를 넘을 수 있음)
            _rateLimiter = new FcmRateLimiter(defaults.MaxFcmCallsPerSecond);
            _alertNotifier = new AlertNotifier(defaults.AlertWebhookUrl, defaults.EnvironmentLabel);
        }

        /// <summary>
        /// push_jobs 테이블을 주기적으로 폴링하며, 실행 가능한 작업이 보이면 즉시 처리합니다.
        /// 여러 job을 동시에(최대 MaxConcurrentJobs개) 병렬로 처리합니다.
        /// 이 메서드는 취소되기 전까지 종료되지 않는 장기 실행 루프입니다.
        /// </summary>
        /// <param name="ct">전체 폴링 루프를 종료시키는 취소 토큰 (예: Ctrl+C)</param>
        public async Task RunForeverAsync(CancellationToken ct)
        {
            InitializeFirebase();

            Log.Information(
                "[스케줄러] worker={WorkerInstanceId} push_jobs 폴링 시작 (주기={PollingIntervalSeconds}초, 최대 동시 job={MaxConcurrentJobs}, FCM 초당 호출 제한={MaxFcmCallsPerSecond})",
                _workerInstanceId, _defaults.PollingIntervalSeconds, _defaults.MaxConcurrentJobs, _defaults.MaxFcmCallsPerSecond);

            // 현재 실행 중인 job task들을 추적 (job_id -> Task).
            // 이 딕셔너리의 크기가 곧 "지금 병렬로 처리 중인 job 개수"입니다.
            var runningTasks = new Dictionary<string, Task>();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 1) 끝난 task 정리: 완료된(성공/실패 무관) job은 슬롯에서 제거해 다음 선점에 자리를 내줍니다.
                    var finishedKeys = runningTasks
                        .Where(kv => kv.Value.IsCompleted)
                        .Select(kv => kv.Key)
                        .ToList();
                    foreach (var key in finishedKeys)
                        runningTasks.Remove(key);

                    // 2) 여유 슬롯만큼 새 job 선점. 예: MaxConcurrentJobs=3이고 2개가 실행 중이면 1개만 더 선점 시도.
                    int availableSlots = _defaults.MaxConcurrentJobs - runningTasks.Count;

                    if (availableSlots > 0)
                    {
                        List<PushJob> claimedJobs;
                        try
                        {
                            // DB에 일시적 장애가 있어도 폴링 루프 자체는 죽지 않도록 try-catch로 감쌉니다.
                            claimedJobs = await _jobRepo.ClaimRunnableJobsAsync(
                                availableSlots,
                                _workerInstanceId,
                                TimeSpan.FromMinutes(_defaults.StaleLeaseMinutes),
                                ct);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "[스케줄러] 작업 선점 중 오류");
                            claimedJobs = new List<PushJob>();
                        }

                        // 선점에 성공한 job마다 별도 Task로 실행 -> 서로 독립적으로 병렬 진행
                        foreach (var job in claimedJobs)
                        {
                            Log.Information("[스케줄러] job '{JobId}' 선점 완료. 병렬 처리를 시작합니다.", job.JobId);
                            runningTasks[job.JobId] = ExecuteJobAsync(job, ct);
                        }
                    }

                    // 3) 다음 폴링까지 대기 (취소 시 Task.Delay가 예외를 던지므로 바로 루프 탈출)
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_defaults.PollingIntervalSeconds), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                // 종료 신호 수신 -> 프로세스를 바로 죽이지 않고, 현재 진행 중인 job들이
                // 각자 진행상황을 DB에 저장(체크포인트)할 시간을 줍니다.
                Log.Information("[스케줄러] 종료 신호 수신. 실행 중인 job {Count}개의 마무리를 기다립니다.", runningTasks.Count);
                try
                {
                    await Task.WhenAll(runningTasks.Values);
                }
                catch
                {
                    // 개별 job의 취소/예외는 ExecuteJobAsync 내부에서 이미 처리되고 로깅되므로 여기서는 무시합니다.
                }
            }
            finally
            {
                // 프로세스 전체 수명 동안 유지되던 Rate Limiter 자원을 정리합니다.
                _rateLimiter.Dispose();
            }
        }

        /// <summary>
        /// 하나의 push_jobs 행(작업)을 끝까지 처리합니다.
        /// 내부적으로 Producer(DB 읽기) 1개, Consumer(FCM 발송) N개, Lease 갱신 루프 1개를
        /// Channel&lt;T&gt;로 연결된 파이프라인으로 동시에 실행합니다.
        /// </summary>
        /// <param name="job">실행할 작업 (ClaimRunnableJobsAsync로 이미 선점되어 status=running인 상태)</param>
        /// <param name="ct">상위 폴링 루프의 취소 토큰. 취소되면 이 job도 중단되고 진행상황이 저장됩니다.</param>
        private async Task ExecuteJobAsync(PushJob job, CancellationToken ct)
        {
            // 처리량 관련 설정은 모두 전역 기본값을 사용합니다 (job별 오버라이드 없음).
            int dbFetchSize = _defaults.DbFetchSize;
            int fcmBatchSize = _defaults.FcmBatchSize;
            int consumerCount = _defaults.ConsumerCount;
            int channelCapacity = _defaults.ChannelCapacity;

            // Producer(DB 읽기)와 Consumer(FCM 발송) 사이의 버퍼.
            // FullMode=Wait이므로 버퍼가 가득 차면 Producer가 자동으로 대기 -> 메모리 사용량이 무한정 늘어나지 않음.
            var channel = Channel.CreateBounded<(long Id, string Token)>(new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false, // Consumer가 여러 개이므로 false
                SingleWriter = true   // Producer는 1개뿐이므로 true (성능 최적화)
            });

            // job별 누적 카운터를 JobCounters 객체로 묶어 관리합니다.
            // async 메서드(SendBatchAsync)에는 ref 파라미터를 쓸 수 없으므로,
            // 객체 참조로 넘기고 내부에서 Interlocked로 안전하게 갱신합니다.
            var counters = new JobCounters(job.TotalRead, job.TotalSuccess, job.TotalFailure, job.LastProcessedId);
            long lastReadId = job.LastProcessedId;

            // 크래시 시 재개 지점을 계산하는 로컬 함수.
            // 기존: lastReadId(Producer가 읽은 위치)에서 채널 전체 용량을 빼는 방식
            //   -> 채널이 거의 비어있어도 최대 용량만큼 뒤로 빠져 중복 발송 범위가 과하게 잡힘.
            // 개선: counters.LastConfirmedId(Consumer가 실제 발송 완료한 위치)에서
            //   Consumer당 진행 중일 수 있는 최대 1개 배치 크기만 여유로 빼는 방식.
            //   -> 실제 미확인 구간(발송 중이었을 수 있는 배치)만 재시도 범위에 포함.
            long GetSafeResumeId()
            {
                long margin = (long)fcmBatchSize * consumerCount;
                return Math.Max(0, counters.LastConfirmedId - margin);
            }

            if (job.Status == JobStatus.Running)
            {
                // ClaimRunnableJobsAsync가 'running'이면서 stale lease였던 job을 다시 선점한 경우 -> 재개
                Log.Information(
                    "[Job {JobId}] 이전 실행 미완료 발견. id={LastProcessedId} 부터 재개합니다. (누적 읽음={TotalRead}, 성공={TotalSuccess}, 실패={TotalFailure})",
                    job.JobId, job.LastProcessedId, counters.TotalRead, counters.TotalSuccess, counters.TotalFailure);
            }
            else
            {
                Log.Information(
                    "[Job {JobId}] 예약시간 도달 ({ScheduledTime}). 발송을 시작합니다.",
                    job.JobId, job.ScheduledTime);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            /// <summary>
            /// Producer 로컬 함수: users 테이블을 id 기준 keyset pagination으로 순회하며 channel에 토큰을 적재합니다.
            /// OFFSET 방식 대신 "WHERE id > lastId ORDER BY id LIMIT n"을 사용해 천만 건 규모에서도 일정한 성능을 유지합니다.
            /// Dapper로 (id, push_token) 결과를 UserTokenRow DTO에 자동 매핑합니다.
            /// </summary>
            async Task ProduceAsync()
            {
                long localLastId = job.LastProcessedId;
                lastReadId = localLastId;

                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(ct);

                // Dapper로 (id, push_token) 결과를 UserTokenRow DTO에 자동 매핑합니다.
                // 컬럼 별칭(AS Id, AS PushToken)을 SQL에 명시해 프로퍼티명과 정확히 일치시킵니다.
                const string sql = @"
                    SELECT id AS Id, push_token AS PushToken
                    FROM users
                    WHERE id > @LastId
                      AND push_token IS NOT NULL
                      AND push_token <> ''
                    ORDER BY id
                    LIMIT @Limit";

                while (!ct.IsCancellationRequested)
                {
                    // QueryAsync는 결과를 한 번에 List로 반환합니다. dbFetchSize(기본 2000건) 단위라
                    // 메모리에 부담이 없고, 기존의 reader 기반 스트리밍과 처리 흐름이 동일합니다.
                    var rows = (await connection.QueryAsync<UserTokenRow>(
                        new CommandDefinition(
                            sql,
                            new { LastId = localLastId, Limit = dbFetchSize },
                            cancellationToken: ct))).ToList();

                    int rowCount = 0;

                    foreach (var row in rows)
                    {
                        localLastId = row.Id;

                        // 채널이 가득 차 있으면 여기서 자동으로 대기 (Consumer가 따라잡을 때까지)
                        await channel.Writer.WriteAsync((localLastId, row.PushToken), ct);

                        rowCount++;
                        Interlocked.Increment(ref counters.TotalRead);
                    }

                    // 이번 배치에서 읽은 행이 0개면 더 이상 읽을 데이터가 없다는 뜻 -> 종료
                    if (rowCount == 0)
                        break;

                    lastReadId = localLastId;

                    // DB 배치 단위로 체크포인트 갱신 (재개 지점 최신화).
                    // 매 row마다가 아니라 배치(dbFetchSize)마다 호출해 DB 부하를 줄입니다.
                    await _jobRepo.UpdateProgressAsync(
                        job.JobId, GetSafeResumeId(), counters.TotalRead, counters.TotalSuccess, counters.TotalFailure, ct);

                    if (counters.TotalRead % 100000 == 0)
                        Log.Information("[Job {JobId}][Producer] {TotalRead}건 읽음 (lastId={LastId})", job.JobId, counters.TotalRead, localLastId);
                }

                // 더 이상 쓸 데이터가 없음을 Consumer들에게 알림 -> Consumer의 WaitToReadAsync가 false를 반환하며 종료
                channel.Writer.Complete();
                Log.Information("[Job {JobId}][Producer] 읽기 완료. 총 {TotalRead}건", job.JobId, counters.TotalRead);
            }

            /// <summary>
            /// Consumer 로컬 함수: channel에서 토큰을 꺼내 fcmBatchSize만큼 모아 FCM에 묶음 발송합니다.
            /// 여러 개(consumerCount)가 동시에 실행되어 발송 처리량을 병렬화합니다.
            /// 실제 FCM 호출 직전에는 공유 Rate Limiter에서 토큰을 획득해야 하므로,
            /// 전체 프로세스의 초당 FCM 호출 수가 MaxFcmCallsPerSecond를 넘지 않습니다.
            /// </summary>
            /// <param name="workerId">로그 식별용 워커 번호 (0부터 시작)</param>
            async Task ConsumeAsync(int workerId)
            {
                var reader = channel.Reader;
                var buffer = new List<(long Id, string Token)>(fcmBatchSize);

                // 채널에 새 데이터가 생기거나 닫힐 때까지 대기. 닫히고 빈 상태가 되면 false 반환 -> 루프 종료.
                while (await reader.WaitToReadAsync(ct))
                {
                    // 한 번에 fcmBatchSize만큼 모아서 보내기 위해 가능한 만큼 즉시 꺼냅니다 (TryRead는 non-blocking).
                    while (buffer.Count < fcmBatchSize && reader.TryRead(out var item))
                    {
                        buffer.Add(item);
                    }

                    if (buffer.Count > 0)
                    {
                        await SendBatchAsync(job, workerId, buffer, counters, ct);
                        buffer.Clear();
                    }
                }

                // 채널이 닫힌 후에도 마지막으로 덜 채워진 배치가 남아있을 수 있으므로 한 번 더 처리
                if (buffer.Count > 0)
                {
                    await SendBatchAsync(job, workerId, buffer, counters, ct);
                }
            }

            // lease를 주기적으로 갱신 -> 처리 시간이 길어져도 다른 워커에게 stale로 오인되어 빼앗기지 않도록 함.
            // 이 job 전용 취소 토큰(leaseCts)을 만들어, 본작업이 끝나면 lease 갱신 루프만 독립적으로 멈출 수 있게 합니다.
            using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            /// <summary>
            /// LeaseRenewalSeconds 주기로 push_jobs.claimed_at을 갱신하는 백그라운드 루프.
            /// 본 작업(Producer/Consumer)이 끝나면 leaseCts.Cancel()로 함께 종료됩니다.
            /// </summary>
            async Task RenewLeaseLoopAsync()
            {
                try
                {
                    while (!leaseCts.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_defaults.LeaseRenewalSeconds), leaseCts.Token);
                        await _jobRepo.RenewLeaseAsync(job.JobId, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    // leaseCts가 취소된 정상 종료 경로이므로 별도 처리 없이 그냥 빠져나옵니다.
                }
            }

            try
            {
                // Producer 1개, Consumer N개, Lease 갱신 루프 1개를 모두 동시에 시작합니다.
                var leaseRenewal = RenewLeaseLoopAsync();
                var producer = ProduceAsync();

                var consumers = new List<Task>();
                for (int i = 0; i < consumerCount; i++)
                {
                    int workerId = i;
                    consumers.Add(ConsumeAsync(workerId));
                }

                // Producer가 모든 row를 다 읽고 channel을 닫을 때까지 대기
                await producer;
                // 모든 Consumer가 남은 데이터를 마저 처리하고 끝날 때까지 대기
                await Task.WhenAll(consumers);

                // 본 작업이 끝났으므로 lease 갱신 루프도 멈춥니다.
                leaseCts.Cancel();
                await SwallowCancellationAsync(leaseRenewal);

                // 최종 진행상황 저장 후 작업을 완료 상태로 전이 (이때 claimed_by/claimed_at도 함께 해제됨)
                await _jobRepo.UpdateProgressAsync(job.JobId, lastReadId, counters.TotalRead, counters.TotalSuccess, counters.TotalFailure, ct);
                await _jobRepo.UpdateStatusAsync(job.JobId, JobStatus.Completed, ct);

                sw.Stop();
                Log.Information(
                    "[Job {JobId}] 완료. 총={TotalRead}명 / 성공={TotalSuccess} / 실패={TotalFailure} / 소요시간={Elapsed}",
                    job.JobId, counters.TotalRead, counters.TotalSuccess, counters.TotalFailure, sw.Elapsed);

                // 모니터링: 완료 요약 알림 (실패 여부와 무관하게 전송)
                await _alertNotifier.NotifyJobCompletedAsync(job.JobId, counters.TotalRead, counters.TotalSuccess, counters.TotalFailure, sw.Elapsed, ct);
                // 모니터링: 실패율이 임계치를 초과하면 별도 경고 알림 (정상 범위면 자동으로 무시됨)
                await _alertNotifier.NotifyIfFailureRateExceededAsync(
                    job.JobId, counters.TotalRead, counters.TotalSuccess, counters.TotalFailure, _defaults.FailureRateAlertThreshold, ct);
            }
            catch (OperationCanceledException)
            {
                // 상위 폴링 루프(Ctrl+C 등)가 취소된 경우. 진행상황을 저장해 다음 실행 시 이어가도록 합니다.
                leaseCts.Cancel();
                await SafeSaveProgressAsync(job.JobId, GetSafeResumeId(), counters.TotalRead, counters.TotalSuccess, counters.TotalFailure);
                Log.Information("[Job {JobId}] 작업이 취소되었습니다. 다음 실행 시 자동으로 이어서 진행됩니다.", job.JobId);
                throw;
            }
            catch (Exception ex)
            {
                // DB 연결 오류 등 예기치 못한 예외. job을 Failed로 만들지 않고 진행상황만 저장해두면,
                // claimed_at이 더 이상 갱신되지 않다가 StaleLeaseMinutes 후 자동으로 재선점되어 재시도됩니다.
                leaseCts.Cancel();
                await SafeSaveProgressAsync(job.JobId, GetSafeResumeId(), counters.TotalRead, counters.TotalSuccess, counters.TotalFailure);
                Log.Error(ex, "[Job {JobId}] 예기치 못한 오류로 중단. 다음 실행 시 자동으로 이어서 진행됩니다.", job.JobId);

                // 모니터링: 예외로 인한 중단은 즉시 알림
                await _alertNotifier.NotifyJobErrorAsync(job.JobId, ex.Message, CancellationToken.None);
            }
        }

        /// <summary>
        /// Task를 기다리되 OperationCanceledException만 조용히 흡수합니다.
        /// lease 갱신 루프처럼 "취소되는 것이 정상 종료"인 Task를 마무리할 때 사용합니다.
        /// </summary>
        /// <param name="task">대기할 Task</param>
        private static async Task SwallowCancellationAsync(Task task)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// 진행 상황 저장을 시도하되 실패해도 예외를 전파하지 않습니다.
        /// 이미 예외/취소 처리 중인 상황에서 추가 예외로 마무리 흐름을 방해하지 않기 위함입니다.
        /// CancellationToken.None을 사용해, 상위 취소 토큰이 이미 취소되었어도 저장은 시도합니다.
        /// </summary>
        private async Task SafeSaveProgressAsync(
            string jobId, long resumeId, long totalRead, long totalSuccess, long totalFailure)
        {
            try
            {
                await _jobRepo.UpdateProgressAsync(jobId, resumeId, totalRead, totalSuccess, totalFailure, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[경고] Job {JobId} 진행상황 저장 실패", jobId);
            }
        }

        /// <summary>
        /// Firebase Admin SDK를 1회 초기화합니다 (FirebaseApp.DefaultInstance가 비어있을 때만).
        /// 여러 job이 동시에 시작되더라도 이미 초기화되어 있으면 다시 초기화하지 않습니다.
        /// </summary>
        private void InitializeFirebase()
        {
            if (FirebaseApp.DefaultInstance == null)
            {
                // GoogleCredential.FromFile은 deprecated 되었으므로,
                // 경고 메시지에서 권장하는 CredentialFactory.FromFile<T>을 사용합니다.
                // 서비스 계정 키 JSON 파일을 ServiceAccountCredential로 읽은 뒤,
                // ToGoogleCredential()로 FirebaseAdmin이 요구하는 GoogleCredential로 변환합니다.
                var credential = CredentialFactory
                    .FromFile<ServiceAccountCredential>(_defaults.CredentialPath)
                    .ToGoogleCredential();

                FirebaseApp.Create(new AppOptions
                {
                    Credential = credential
                });
            }
        }

        /// <summary>
        /// FCM에 토큰 묶음(최대 fcmBatchSize개)을 한 번에 발송합니다.
        ///
        /// 오류 처리 전략:
        ///  - 전체 배치 실패 (네트워크 오류 등): 최대 3회 점증 백오프 후 전체 재시도
        ///  - 일부 토큰 일시적 실패 (QuotaExceeded, Unavailable): 해당 토큰만 분리해 최대 3회 재시도
        ///  - 영구 실패 (Unregistered, InvalidArgument): 재시도 없이 DB에서 토큰 일괄 정리
        ///  - 기타 실패: 로그만 남김
        ///
        /// 배치 처리 완료 후 counters.LastConfirmedId를 갱신해 GetSafeResumeId가
        /// 실제 발송 완료 지점 기준으로 정밀한 재개 지점을 계산할 수 있게 합니다.
        /// </summary>
        /// <param name="job">발송 중인 작업 (알림 제목/본문, 로깅용 jobId 포함)</param>
        /// <param name="workerId">이 배치를 처리하는 Consumer 워커 번호 (로깅용)</param>
        /// <param name="items">발송할 (유저 id, FCM 토큰) 목록. id 오름차순 정렬 상태여야 합니다.</param>
        /// <param name="counters">누적 성공/실패 카운터 및 발송 완료 지점. Interlocked로 스레드 안전하게 갱신됩니다.</param>
        /// <param name="ct">취소 토큰</param>
        private async Task SendBatchAsync(
            PushJob job, int workerId, List<(long Id, string Token)> items,
            JobCounters counters, CancellationToken ct)
        {
            // 이 배치에서 실제로 발송을 시도할 (id, token) 목록.
            // 재시도 루프를 거치며 Transient 실패 토큰만 남도록 줄어듭니다.
            var currentItems = items;

            const int maxRetry = 3;
            for (int attempt = 1; attempt <= maxRetry; attempt++)
            {
                var tokens = currentItems.Select(x => x.Token).ToList();

                var message = new MulticastMessage
                {
                    Tokens = tokens,
                    Notification = new Notification
                    {
                        Title = job.NotificationTitle,
                        Body = job.NotificationBody
                    },
                    Data = new Dictionary<string, string>
                    {
                        { "type", "broadcast" },
                        { "jobId", job.JobId }
                    }
                };

                // FCM 호출 직전 Rate Limiter에서 토큰 1개를 획득할 때까지 대기.
                // 재시도(attempt > 1)도 매번 토큰을 소비해 재시도 트래픽까지 쿼터 안에 포함합니다.
                await _rateLimiter.AcquireAsync(ct);

                try
                {
                    var response = await FirebaseMessaging.DefaultInstance
                        .SendEachForMulticastAsync(message, ct);

                    // 응답을 성공 / 영구 실패 / 일시적 실패 세 가지로 분류합니다.
                    // FCM 응답은 요청 tokens와 같은 순서로 오므로 인덱스로 1:1 매칭합니다.
                    var invalidTokens  = new List<string>();                   // Unregistered, InvalidArgument
                    var transientItems = new List<(long Id, string Token)>();  // QuotaExceeded, Unavailable

                    for (int i = 0; i < response.Responses.Count; i++)
                    {
                        var r = response.Responses[i];
                        if (r.IsSuccess) continue;

                        var errorCode = (r.Exception as FirebaseMessagingException)?.MessagingErrorCode;

                        if (errorCode == MessagingErrorCode.Unregistered ||
                            errorCode == MessagingErrorCode.InvalidArgument)
                        {
                            // 영구 실패: 앱 삭제, 형식 오류 등 -> DB에서 정리
                            invalidTokens.Add(tokens[i]);
                        }
                        else if (errorCode == MessagingErrorCode.QuotaExceeded ||
                                 errorCode == MessagingErrorCode.Unavailable)
                        {
                            // 일시적 실패: 쿼터 초과, 서버 일시 불가 -> 해당 토큰만 재시도 목록에 추가
                            transientItems.Add(currentItems[i]);
                        }
                        else
                        {
                            // 그 외 실패 (SenderIdMismatch 등): 재시도 불가, 로그만 기록
                            Log.Warning(
                                "[Job {JobId}][Worker {WorkerId}] 발송 실패: id={Id}, error={Error}",
                                job.JobId, workerId, currentItems[i].Id, r.Exception?.Message);
                        }
                    }

                    // 영구 실패 토큰을 DB에서 일괄 정리 (Bulk UPDATE IN @Tokens, 한 번의 쿼리)
                    if (invalidTokens.Count > 0)
                    {
                        await _cleanupRepo.InvalidateTokensAsync(invalidTokens, ct);
                        Log.Information(
                            "[Job {JobId}][Worker {WorkerId}] 무효 토큰 {Count}건 정리 완료",
                            job.JobId, workerId, invalidTokens.Count);
                    }

                    // 이번 시도에서 확정된 결과(성공 + 영구실패 + 기타실패)를 카운터에 반영.
                    // Transient 실패분은 아직 재시도 예정이므로 실패로 집계하지 않습니다.
                    int permanentFailures = response.FailureCount - transientItems.Count;
                    Interlocked.Add(ref counters.TotalSuccess, response.SuccessCount);
                    Interlocked.Add(ref counters.TotalFailure, permanentFailures);

                    // 이 배치의 마지막 id(최댓값)를 LastConfirmedId로 갱신합니다.
                    // items는 id 오름차순이므로 마지막 요소가 이 배치의 최대 id입니다.
                    // CAS 루프로 LastConfirmedId를 단조 증가만 허용합니다 (다른 Consumer와의 경합 안전).
                    long batchMaxId = currentItems[^1].Id;
                    long current;
                    do
                    {
                        current = Interlocked.Read(ref counters.LastConfirmedId);
                        if (batchMaxId <= current) break; // 이미 더 앞선 값이 기록되어 있으면 갱신 불필요
                    } while (Interlocked.CompareExchange(ref counters.LastConfirmedId, batchMaxId, current) != current);

                    // Transient 실패 토큰이 없으면 이 배치 처리 완료
                    if (transientItems.Count == 0)
                        return;

                    // Transient 실패 토큰이 남아있으면 해당 토큰만 다음 attempt에서 재시도
                    if (attempt < maxRetry)
                    {
                        Log.Warning(
                            "[Job {JobId}][Worker {WorkerId}] 일시적 실패 토큰 {Count}건 재시도 예정 (attempt {Attempt}/{MaxRetry})",
                            job.JobId, workerId, transientItems.Count, attempt, maxRetry);

                        await Task.Delay(1000 * attempt, ct); // 점증 백오프 (1초, 2초)
                        currentItems = transientItems;         // 다음 루프에서는 실패 토큰만 재발송
                    }
                    else
                    {
                        // 마지막 attempt에도 Transient 실패가 남으면 최종 실패로 집계
                        Interlocked.Add(ref counters.TotalFailure, transientItems.Count);
                        Log.Warning(
                            "[Job {JobId}][Worker {WorkerId}] 일시적 실패 토큰 {Count}건 최대 재시도 초과, 실패 처리",
                            job.JobId, workerId, transientItems.Count);
                    }
                }
                catch (Exception ex)
                {
                    // 네트워크 오류 등 FCM 호출 자체가 실패한 경우 (응답을 받지 못했으므로 전체 재시도)
                    Log.Warning(
                        ex, "[Job {JobId}][Worker {WorkerId}] 배치 전체 발송 오류 (시도 {Attempt}/{MaxRetry})",
                        job.JobId, workerId, attempt, maxRetry);

                    if (attempt == maxRetry)
                    {
                        Interlocked.Add(ref counters.TotalFailure, currentItems.Count);
                    }
                    else
                    {
                        await Task.Delay(1000 * attempt, ct);
                        // currentItems 변경 없이 동일 목록 전체 재시도
                    }
                }
            }
        }
    }
}
