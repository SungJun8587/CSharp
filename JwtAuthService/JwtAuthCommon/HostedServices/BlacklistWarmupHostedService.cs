using JwtAuthCommon.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace JwtAuthCommon.HostedServices
{
    /// <summary>
    /// 서버 기동 시 1회 실행되는 웜업(warm-up) 서비스.
    ///
    /// Redis가 재시작되거나 장애로 데이터가 유실된 경우, 이미 로그아웃/폐기 처리된
    /// Access 토큰이 다시 유효한 것처럼 통과되는 것을 막기 위해,
    /// DB(BlacklistedAccessTokens)에 남아있는 "아직 만료되지 않은" 블랙리스트 항목을
    /// 애플리케이션 시작 시점에 Redis로 다시 채워 넣는다.
    ///
    /// IHostedService.StartAsync는 앱이 요청을 받기 시작하기 전에 실행되므로,
    /// 웜업이 끝나기 전에 (이미 폐기됐어야 할) 토큰이 통과할 여지를 최소화한다.
    /// </summary>
    public class BlacklistWarmupHostedService : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConnectionMultiplexer _mux;
        private readonly ILogger<BlacklistWarmupHostedService> _logger;

        public BlacklistWarmupHostedService(
            IServiceScopeFactory scopeFactory,
            IConnectionMultiplexer mux,
            ILogger<BlacklistWarmupHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _mux = mux;
            _logger = logger;
        }

        /// <summary>
        /// 앱 시작 시 호출: DB에 남아있는 유효한 블랙리스트 항목을 Redis로 복원한다.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IBlacklistedAccessTokenRepository>();

                // 1. DB에서 아직 만료되지 않은 블랙리스트 항목 전체 조회
                var validEntries = await repo.GetAllValidAsync();

                var redis = _mux.GetDatabase();
                var now = DateTime.UtcNow;
                var restored = 0;

                // 2. 각 항목을 Redis에 남은 TTL만큼 재등록
                foreach (var entry in validEntries)
                {
                    var remaining = entry.ExpiresAt - now;
                    if (remaining <= TimeSpan.Zero) continue; // 조회 이후 시점에 만료된 경우 스킵

                    await redis.StringSetAsync($"blacklist:{entry.Jti}", "1", remaining);
                    restored++;
                }

                _logger.LogInformation(
                    "블랙리스트 웜업 완료: DB에서 {Restored}건을 Redis로 복원했습니다.", restored);
            }
            catch (Exception ex)
            {
                // 웜업 실패가 앱 기동 자체를 막아서는 안 되므로 예외를 삼키고 에러 로그만 남긴다.
                // (다만 이 경우 Redis 장애 발생 이력이 있다면 운영자가 즉시 인지할 수 있도록 알림 연동을 권장)
                _logger.LogError(ex, "블랙리스트 웜업 중 오류가 발생했습니다.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
