using JwtAuthCommon.Repositories;
using JwtAuthCommon.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JwtAuthCommon.HostedServices
{
    /// <summary>
    /// BlacklistWriteQueue에 쌓인 블랙리스트 항목을 배치로 모아 DB에 기록하는 백그라운드 서비스.
    ///
    /// TokenBlacklistService.AddToBlacklistAsync는 이 큐에 항목을 적재하기만 하고 즉시 반환하므로,
    /// 요청 경로에서는 스코프 생성/DB 접근 비용이 전혀 발생하지 않는다. 이 서비스가 유일한
    /// 컨슈머로서, 짧은 시간(flush interval) 동안 또는 일정 개수(batch size)만큼 항목을 모은 뒤
    /// 스코프를 한 번만 생성해 IBlacklistedAccessTokenRepository.AddRangeAsync로 일괄 저장한다.
    ///
    /// 예) 로그아웃 요청이 초당 수백~수천 건 몰려도, DB 쪽은 최대 (1000ms / flush interval)회만
    /// 스코프를 생성하고 SaveChangesAsync를 호출하게 되어 오버헤드가 트래픽에 비례해 커지지 않는다.
    /// </summary>
    public class BlacklistDbWriterHostedService : BackgroundService
    {
        /// <summary>한 번에 모아서 저장할 최대 배치 크기</summary>
        private const int MaxBatchSize = 200;

        /// <summary>배치가 가득 차지 않아도 강제로 flush할 주기</summary>
        private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

        private readonly BlacklistWriteQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BlacklistDbWriterHostedService> _logger;

        public BlacklistDbWriterHostedService(
            BlacklistWriteQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<BlacklistDbWriterHostedService> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var reader = _queue.Reader;
            var batch = new List<BlacklistWriteQueue.Entry>(MaxBatchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                batch.Clear();

                try
                {
                    // 1. 최소 1건은 도착할 때까지 대기 (큐가 비어있으면 여기서 블로킹 없이 대기)
                    if (!await reader.WaitToReadAsync(stoppingToken))
                        break; // 채널이 완료(Complete)된 경우

                    // 2. FlushInterval 동안, 또는 MaxBatchSize에 도달할 때까지 최대한 모아서 배치 구성
                    using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    flushCts.CancelAfter(FlushInterval);

                    while (batch.Count < MaxBatchSize && reader.TryRead(out var entry))
                    {
                        batch.Add(entry);
                    }

                    // TryRead로 즉시 가져올 수 있는 만큼 다 가져온 뒤, 아직 배치가 다 안 찼으면
                    // 짧게 추가로 기다려 뒤이어 들어오는 항목까지 같은 배치에 포함시킨다.
                    while (batch.Count < MaxBatchSize && !flushCts.IsCancellationRequested)
                    {
                        if (!await reader.WaitToReadAsync(flushCts.Token).ConfigureAwait(false))
                            break;

                        while (batch.Count < MaxBatchSize && reader.TryRead(out var entry))
                        {
                            batch.Add(entry);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // 앱 종료 시 정상 취소 - 아래에서 지금까지 모은 배치는 마저 저장 시도
                }
                catch (OperationCanceledException)
                {
                    // FlushInterval 타임아웃으로 인한 취소 - 지금까지 모은 배치를 저장하러 진행
                }

                if (batch.Count == 0) continue;

                await FlushBatchAsync(batch);
            }

            // 종료 직전 큐에 남아있는 항목을 최대한 마저 비운다 (best-effort)
            var remaining = new List<BlacklistWriteQueue.Entry>();
            while (reader.TryRead(out var entry))
            {
                remaining.Add(entry);
            }
            if (remaining.Count > 0)
            {
                await FlushBatchAsync(remaining);
            }
        }

        /// <summary>
        /// 모아둔 배치를 스코프 하나를 열어 한 번에 DB에 저장한다.
        /// 실패해도 프로세스를 중단시키지 않고 경고 로그만 남긴다(best-effort).
        /// </summary>
        private async Task FlushBatchAsync(List<BlacklistWriteQueue.Entry> batch)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IBlacklistedAccessTokenRepository>();

                await repo.AddRangeAsync(batch);

                _logger.LogDebug("블랙리스트 배치 {Count}건을 DB에 기록했습니다.", batch.Count);
            }
            catch (Exception ex)
            {
                // DB 기록 실패가 서버 전체에 영향을 주지 않도록 예외를 삼키고 경고 로그만 남긴다.
                // (Redis에는 이미 반영되어 있으므로 실시간 검증에는 영향이 없다.)
                _logger.LogWarning(ex,
                    "블랙리스트 배치({Count}건) DB 기록에 실패했습니다. (Redis 등록은 정상 처리됨)", batch.Count);
            }
        }
    }
}
