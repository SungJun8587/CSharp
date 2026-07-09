using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;

namespace FcmPushAgent
{
    /// <summary>
    /// push_jobs.status 컬럼의 가능한 값들.
    /// Pending(대기) -> Running(처리중) -> Completed(완료) 가 정상 흐름이며,
    /// 복구 불가능한 오류 시 Failed로 전이될 수 있습니다.
    /// </summary>
    public enum JobStatus
    {
        /// <summary>아직 예약시간이 도래하지 않았거나, 도래했지만 아직 아무도 선점하지 않은 상태</summary>
        Pending,
        /// <summary>현재 어떤 워커가 처리 중인 상태 (claimed_by/claimed_at에 lease 정보가 기록됨)</summary>
        Running,
        /// <summary>모든 대상 유저에게 발송을 마친 상태</summary>
        Completed,
        /// <summary>복구 불가능한 오류로 영구 실패 처리된 상태</summary>
        Failed
    }

    /// <summary>
    /// push_jobs 테이블의 한 행(row)을 나타냅니다.
    /// 예약시간/알림내용/진행상태가 모두 DB에 저장되어 있습니다.
    /// 발송 처리량 옵션(배치 크기, 동시성 등)은 전역 설정(PushSchedulerDefaults)을 사용합니다.
    /// </summary>
    public class PushJob
    {
        /// <summary>작업 고유 식별자 (push_jobs.job_id)</summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>발송 예약 시각</summary>
        public DateTime ScheduledTime { get; set; }

        /// <summary>현재 작업 상태</summary>
        public JobStatus Status { get; set; }

        /// <summary>FCM 알림 제목</summary>
        public string NotificationTitle { get; set; } = string.Empty;

        /// <summary>FCM 알림 본문</summary>
        public string NotificationBody { get; set; } = string.Empty;

        /// <summary>재개 시 시작할 users.id 위치 (keyset pagination 커서)</summary>
        public long LastProcessedId { get; set; }

        /// <summary>지금까지 DB에서 읽은 누적 유저 수</summary>
        public long TotalRead { get; set; }

        /// <summary>FCM 발송 성공 누적 건수</summary>
        public long TotalSuccess { get; set; }

        /// <summary>FCM 발송 실패 누적 건수 (무효 토큰 포함)</summary>
        public long TotalFailure { get; set; }
    }

    /// <summary>
    /// Dapper 매핑용 내부 DTO. push_jobs 조회 결과를 받아 PushJob으로 변환합니다.
    /// status 컬럼이 MySQL ENUM(소문자 문자열)으로 내려오므로, string으로 받아
    /// Enum.Parse(ignoreCase: true)로 수동 변환합니다.
    /// </summary>
    internal class PushJobRow
    {
        public string JobId { get; set; } = string.Empty;
        public DateTime ScheduledTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public string NotificationTitle { get; set; } = string.Empty;
        public string NotificationBody { get; set; } = string.Empty;
        public long LastProcessedId { get; set; }
        public long TotalRead { get; set; }
        public long TotalSuccess { get; set; }
        public long TotalFailure { get; set; }

        /// <summary>DTO를 도메인 객체(PushJob)로 변환합니다.</summary>
        public PushJob ToJob() => new PushJob
        {
            JobId = JobId,
            ScheduledTime = ScheduledTime,
            Status = Enum.Parse<JobStatus>(Status, ignoreCase: true),
            NotificationTitle = NotificationTitle,
            NotificationBody = NotificationBody,
            LastProcessedId = LastProcessedId,
            TotalRead = TotalRead,
            TotalSuccess = TotalSuccess,
            TotalFailure = TotalFailure
        };
    }

    /// <summary>
    /// push_jobs 테이블을 조회/갱신합니다.
    /// 예약시간 폴링(선점), 진행상태(체크포인트) 저장, lease 갱신을 모두 이 레포지토리가 담당합니다.
    /// 모든 DB 접근은 Dapper를 통해 이루어집니다.
    /// </summary>
    public class PushJobRepository
    {
        // MySQL 연결 문자열. 메서드마다 새 연결을 열고 닫습니다(짧은 트랜잭션 단위로 사용).
        private readonly string _connectionString;

        /// <param name="connectionString">MySQL 연결 문자열</param>
        public PushJobRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// 지금 실행 가능한 작업들을 최대 <paramref name="maxJobs"/>개까지 "선점(claim)"합니다.
        ///
        /// 동시성 처리:
        ///  - FOR UPDATE SKIP LOCKED 를 사용해, 다른 프로세스/스레드가 이미 잡고 있는 행은 건너뜁니다.
        ///    -> 여러 워커 인스턴스를 띄워도 같은 job을 중복으로 가져가지 않습니다.
        ///  - 선점과 동시에 status를 'running'으로, claimed_by/claimed_at을 기록해
        ///    "이 워커가 지금 이 job을 처리 중"이라는 lease를 남깁니다.
        ///  - 우선순위: 1) 중단되어 재개해야 하는 'running' 작업(단, lease가 일정 시간 지나 죽은 것으로 판단되는 것),
        ///            2) 예약시간이 도달한 'pending' 작업 (가장 이른 예약시간 우선)
        /// </summary>
        /// <param name="maxJobs">한 번에 선점을 시도할 최대 작업 개수 (보통 남은 동시 실행 슬롯 수)</param>
        /// <param name="workerInstanceId">이 프로세스를 식별하는 고유 문자열 (claimed_by에 기록됨)</param>
        /// <param name="staleLeaseThreshold">
        /// 이 시간보다 claimed_at이 오래된 'running' 작업은 죽은 워커가 처리하던 것으로 간주하고 재선점 대상에 포함
        /// </param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>선점에 성공한 PushJob 목록 (실행 가능한 작업이 없으면 빈 리스트)</returns>
        public async Task<List<PushJob>> ClaimRunnableJobsAsync(
            int maxJobs, string workerInstanceId, TimeSpan staleLeaseThreshold, CancellationToken ct)
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // SELECT ... FOR UPDATE와 이어지는 UPDATE를 하나의 트랜잭션으로 묶어
            // "조회 후 갱신" 사이에 다른 워커가 끼어들 여지를 없앱니다.
            using var tx = await conn.BeginTransactionAsync(ct);

            // 1) 후보 선점 (SKIP LOCKED로 다른 워커와 경쟁하지 않음).
            //    running이지만 stale lease인 작업을 pending(신규)보다 우선 처리합니다.
            const string selectSql = @"
                SELECT job_id AS JobId
                FROM push_jobs
                WHERE
                    (status = 'pending' AND scheduled_time <= UTC_TIMESTAMP())
                    OR (status = 'running' AND (claimed_at IS NULL OR claimed_at <= @StaleBefore))
                ORDER BY
                    (status = 'running') DESC,
                    scheduled_time ASC
                LIMIT @MaxJobs
                FOR UPDATE SKIP LOCKED";

            var jobIds = (await conn.QueryAsync<string>(
                new CommandDefinition(
                    selectSql,
                    new { StaleBefore = DateTime.UtcNow - staleLeaseThreshold, MaxJobs = maxJobs },
                    transaction: tx,
                    cancellationToken: ct))).ToList();

            // 선점할 작업이 하나도 없으면 트랜잭션만 커밋하고 빈 결과 반환
            if (jobIds.Count == 0)
            {
                await tx.CommitAsync(ct);
                return new List<PushJob>();
            }

            // 2) 선점한 행들을 running + claimed_by/claimed_at으로 갱신 (lease 기록).
            //    Dapper의 IN 절 자동 전개: List<string>을 그대로 넘기면 @JobIds를 IN (?,?,...)으로 펼쳐줍니다.
            const string updateSql = @"
                UPDATE push_jobs
                SET status = 'running',
                    claimed_by = @WorkerId,
                    claimed_at = UTC_TIMESTAMP()
                WHERE job_id IN @JobIds";

            await conn.ExecuteAsync(
                new CommandDefinition(
                    updateSql,
                    new { WorkerId = workerInstanceId, JobIds = jobIds },
                    transaction: tx,
                    cancellationToken: ct));

            // 여기서 커밋해야 행 잠금이 해제되고 다른 워커가 다음 선점을 시도할 수 있습니다.
            await tx.CommitAsync(ct);

            // 3) 선점에 성공한 job들의 전체 정보를 조회 (트랜잭션 밖, 잠금 불필요한 단순 조회)
            return await GetJobsByIdsAsync(jobIds, conn, ct);
        }

        /// <summary>
        /// 주어진 job_id 목록에 해당하는 PushJob 전체 정보를 조회합니다.
        /// ClaimRunnableJobsAsync에서 선점 직후 상세 정보를 가져오기 위한 내부 헬퍼입니다.
        /// </summary>
        /// <param name="jobIds">조회할 job_id 목록</param>
        /// <param name="conn">재사용할 열린 연결 (새 연결을 열지 않기 위해 전달받음)</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>조회된 PushJob 목록</returns>
        private async Task<List<PushJob>> GetJobsByIdsAsync(
            List<string> jobIds, MySqlConnection conn, CancellationToken ct)
        {
            const string sql = @"
                SELECT job_id         AS JobId,
                       scheduled_time AS ScheduledTime,
                       status         AS Status,
                       notification_title AS NotificationTitle,
                       notification_body  AS NotificationBody,
                       last_processed_id  AS LastProcessedId,
                       total_read         AS TotalRead,
                       total_success      AS TotalSuccess,
                       total_failure      AS TotalFailure
                FROM push_jobs
                WHERE job_id IN @JobIds";

            // Dapper IN 절 자동 전개: List<string>을 @JobIds에 넘기면 자동으로 펼쳐집니다.
            var rows = await conn.QueryAsync<PushJobRow>(
                new CommandDefinition(sql, new { JobIds = jobIds }, cancellationToken: ct));

            return rows.Select(r => r.ToJob()).ToList();
        }

        /// <summary>
        /// 처리 도중 살아있음을 알리기 위해 lease(claimed_at)를 갱신합니다.
        /// 오래 걸리는 job이 stale로 잘못 판정되어 다른 워커에게 빼앗기지 않도록 주기적으로 호출합니다.
        /// </summary>
        /// <param name="jobId">lease를 갱신할 작업 ID</param>
        /// <param name="ct">취소 토큰</param>
        public async Task RenewLeaseAsync(string jobId, CancellationToken ct)
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE push_jobs SET claimed_at = UTC_TIMESTAMP() WHERE job_id = @JobId",
                    new { JobId = jobId },
                    cancellationToken: ct));
        }

        /// <summary>
        /// 작업 상태를 갱신합니다. Completed/Failed로 전이할 때는 lease(claimed_by/claimed_at)를 해제하여
        /// 해당 행이 더 이상 "처리 중"으로 보이지 않게 합니다.
        /// </summary>
        /// <param name="jobId">상태를 변경할 작업 ID</param>
        /// <param name="status">새로 설정할 상태</param>
        /// <param name="ct">취소 토큰</param>
        public async Task UpdateStatusAsync(string jobId, JobStatus status, CancellationToken ct)
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // 종료 상태(Completed/Failed)로 전이할 때만 lease 컬럼을 NULL로 비워 명시적으로 해제.
            // 그 외(Running 등)에는 lease를 건드리지 않습니다.
            string sql = status is JobStatus.Completed or JobStatus.Failed
                ? "UPDATE push_jobs SET status = @Status, claimed_by = NULL, claimed_at = NULL WHERE job_id = @JobId"
                : "UPDATE push_jobs SET status = @Status WHERE job_id = @JobId";

            await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { JobId = jobId, Status = status.ToString().ToLowerInvariant() },
                    cancellationToken: ct));
        }

        /// <summary>
        /// 진행 상황(재개 지점, 누적 카운터)을 갱신합니다.
        /// DB 배치 단위로 호출하여 크래시 발생 시 재개 지점을 최신으로 유지합니다.
        /// </summary>
        /// <param name="jobId">진행 상황을 갱신할 작업 ID</param>
        /// <param name="lastProcessedId">다음 재개 시 시작할 users.id 커서</param>
        /// <param name="totalRead">지금까지 DB에서 읽은 누적 유저 수</param>
        /// <param name="totalSuccess">FCM 발송 성공 누적 건수</param>
        /// <param name="totalFailure">FCM 발송 실패 누적 건수</param>
        /// <param name="ct">취소 토큰</param>
        public async Task UpdateProgressAsync(
            string jobId, long lastProcessedId, long totalRead, long totalSuccess, long totalFailure, CancellationToken ct)
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await conn.ExecuteAsync(
                new CommandDefinition(
                    @"UPDATE push_jobs
                      SET last_processed_id = @LastProcessedId,
                          total_read        = @TotalRead,
                          total_success     = @TotalSuccess,
                          total_failure     = @TotalFailure
                      WHERE job_id = @JobId",
                    new { JobId = jobId, LastProcessedId = lastProcessedId, TotalRead = totalRead, TotalSuccess = totalSuccess, TotalFailure = totalFailure },
                    cancellationToken: ct));
        }
    }
}
