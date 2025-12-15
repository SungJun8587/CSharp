using StackExchange.Redis;

namespace JwtAuthCommon.Services
{
    /// <summary>
    /// 액세스 토큰 블랙리스트 서비스
    /// Redis를 사용하여 토큰 식별자(JTI)를 블랙리스트로 관리
    /// </summary>
    public class TokenBlacklistService : ITokenBlacklistService
    {
        /// <summary>Redis DB 인스턴스</summary>
        private readonly IDatabase _redis;

        /// <summary>
        /// 생성자: Redis 연결 멀티플렉서 주입
        /// </summary>
        /// <param name="mux">Redis 연결 멀티플렉서</param>
        public TokenBlacklistService(IConnectionMultiplexer mux)
        {
            _redis = mux.GetDatabase();
        }

        /// <summary>
        /// JTI를 블랙리스트에 추가
        /// </summary>
        /// <param name="jti">JWT 고유 식별자</param>
        /// <param name="expiry">블랙리스트 만료 시간</param>
        /// <returns>비동기 작업</returns>
        public async Task AddToBlacklistAsync(string jti, TimeSpan expiry)
        {
            // Redis에 "blacklist:{JTI}" 키로 저장, 값은 1, 만료 시간 적용
            await _redis.StringSetAsync($"blacklist:{jti}", "1", expiry);
        }

        /// <summary>
        /// JTI가 블랙리스트에 있는지 확인
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
