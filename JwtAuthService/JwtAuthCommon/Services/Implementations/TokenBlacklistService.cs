using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace JwtAuthCommon.Services
{
    /// <summary>
    /// 액세스 토큰 블랙리스트 서비스.
    ///
    /// - 실시간 검증(IsBlacklistedAsync)은 항상 Redis만 조회한다. (지연시간 최소화, stateless 검증 유지)
    /// - 블랙리스트 등록(AddToBlacklistAsync)은 Redis에 TTL과 함께 저장하는 동시에,
    ///   DB(BlacklistedAccessTokens 테이블) 기록이 필요한 항목을 BlacklistWriteQueue에 적재한다.
    ///     · 이 서비스는 큐에 넣기만 할 뿐, 이 경로에서 직접 DbContext/스코프를 생성하지 않는다.
    ///     · 실제 DB 기록은 BlacklistDbWriterHostedService가 백그라운드에서 여러 건을 모아
    ///       배치로 처리하므로, 트래픽이 많아져도 요청당 스코프 생성 비용이 발생하지 않는다.
    ///     · 큐 적재 자체는 인메모리 연산이라 실패하지 않으므로 별도의 try/catch가 필요 없다.
    /// </summary>
    public class TokenBlacklistService : ITokenBlacklistService
    {
        /// <summary>Redis DB 인스턴스</summary>
        private readonly IDatabase _redis;

        /// <summary>DB 기록 요청을 적재하는 인메모리 큐 (백그라운드 배치 처리용)</summary>
        private readonly BlacklistWriteQueue _writeQueue;

        /// <summary>로깅용 로거</summary>
        private readonly ILogger<TokenBlacklistService> _logger;

        /// <summary>
        /// 생성자: Redis 연결 멀티플렉서, DB 기록용 큐, 로거 주입
        /// </summary>
        /// <param name="mux">Redis 연결 멀티플렉서</param>
        /// <param name="writeQueue">블랙리스트 DB 기록 요청을 적재하는 큐</param>
        /// <param name="logger">로거</param>
        public TokenBlacklistService(
            IConnectionMultiplexer mux,
            BlacklistWriteQueue writeQueue,
            ILogger<TokenBlacklistService> logger)
        {
            _redis = mux.GetDatabase();
            _writeQueue = writeQueue;
            _logger = logger;
        }

        /// <summary>
        /// JTI를 블랙리스트에 추가한다.
        /// 1) Redis에 TTL과 함께 저장 (실시간 검증 경로, 반드시 성공해야 함)
        /// 2) DB 기록 요청은 큐에 적재만 하고 즉시 반환 (실제 저장은 백그라운드에서 배치 처리)
        /// </summary>
        /// <param name="jti">JWT 고유 식별자</param>
        /// <param name="expiry">블랙리스트 만료까지 남은 시간</param>
        /// <returns>비동기 작업</returns>
        public async Task AddToBlacklistAsync(string jti, TimeSpan expiry)
        {
            if (string.IsNullOrEmpty(jti)) return;

            // 1. Redis에 "blacklist:{JTI}" 키로 저장, 값은 1, 만료 시간 적용 (실시간 검증의 핵심 경로)
            await _redis.StringSetAsync($"blacklist:{jti}", "1", expiry);

            // 2. DB 기록 요청을 큐에 적재 (스코프 생성 없음, 논블로킹, 이 호출은 즉시 반환됨)
            //    실제 DB 저장은 BlacklistDbWriterHostedService가 여러 건을 모아 배치로 처리한다.
            var expiresAt = DateTime.UtcNow.Add(expiry);
            _writeQueue.Enqueue(jti, expiresAt);

            _logger.LogDebug("블랙리스트 DB 기록을 큐에 적재했습니다. Jti={Jti}", jti);
        }

        /// <summary>
        /// JTI가 블랙리스트에 있는지 확인한다.
        /// 실시간 검증 경로이므로 항상 Redis만 조회한다. (DB 조회는 하지 않음 - 지연시간 최소화)
        /// </summary>
        /// <param name="jti">확인할 JWT 고유 식별자</param>
        /// <returns>블랙리스트 여부</returns>
        public async Task<bool> IsBlacklistedAsync(string jti)
        {
            // JTI가 비어있으면 false 반환
            if (string.IsNullOrEmpty(jti)) return false;

            // Redis에 키 존재 여부 확인
            return await _redis.KeyExistsAsync($"blacklist:{jti}");
        }
    }
}
