using JwtAuthCommon.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JwtAuthCommon.HostedServices
{
    /// <summary>
    /// 주기적으로 실행되어 만료된 BlacklistedAccessTokens DB 레코드를 삭제하는 백그라운드 서비스.
    /// Redis는 TTL로 자동 만료되지만, DB는 별도 삭제 배치가 없으면 테이블이 계속 커지므로
    /// 이 서비스가 그 역할을 대신한다.
    /// </summary>
    public class BlacklistCleanupHostedService : BackgroundService
    {
        /// <summary>정리 주기 (기본 6시간)</summary>
        private readonly TimeSpan _interval;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BlacklistCleanupHostedService> _logger;

        public BlacklistCleanupHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<BlacklistCleanupHostedService> logger,
            TimeSpan? interval = null)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _interval = interval ?? TimeSpan.FromHours(6);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 서버 기동 직후 바로 한 번 실행한 뒤, 이후 _interval 주기로 반복
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IBlacklistedAccessTokenRepository>();

                    var deleted = await repo.DeleteExpiredAsync();

                    if (deleted > 0)
                    {
                        _logger.LogInformation(
                            "만료된 블랙리스트 DB 레코드 {Deleted}건을 삭제했습니다.", deleted);
                    }
                }
                catch (Exception ex)
                {
                    // 정리 작업 실패가 서버 전체에 영향을 주지 않도록 예외를 삼키고 다음 주기에 재시도한다.
                    _logger.LogError(ex, "블랙리스트 만료 레코드 정리 중 오류가 발생했습니다.");
                }

                try
                {
                    await Task.Delay(_interval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // 앱 종료 시 정상적으로 루프 탈출
                }
            }
        }
    }
}
