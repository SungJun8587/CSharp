using JwtAuthService.Data;
using JwtAuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace JwtAuthService.Repositories
{
    /// <summary>
    /// 리프레시 토큰 저장소 구현
    /// </summary>
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        /// <summary>DB 컨텍스트</summary>
        private readonly AppDbContext _db;

        /// <summary>
        /// 생성자: 의존성 주입
        /// </summary>
        /// <param name="db">애플리케이션 DB 컨텍스트</param>
        public RefreshTokenRepository(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// 리프레시 토큰 추가
        /// </summary>
        /// <param name="refreshToken">추가할 리프레시 토큰 엔티티</param>
        /// <returns>비동기 작업</returns>
        public async Task AddAsync(RefreshToken refreshToken)
        {
            // 1. DB에 토큰 추가
            _db.RefreshTokens.Add(refreshToken);

            // 2. 변경사항 저장
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// 토큰 문자열로 리프레시 토큰 조회
        /// </summary>
        /// <param name="token">조회할 리프레시 토큰 문자열</param>
        /// <returns>리프레시 토큰 엔티티 또는 null</returns>
        public async Task<RefreshToken?> GetByTokenAsync(string token)
        {
            return await _db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == token);
        }

        /// <summary>
        /// 리프레시 토큰 무효화
        /// </summary>
        /// <param name="token">무효화할 리프레시 토큰 엔티티</param>
        /// <param name="replacedByToken">교체된 토큰 문자열 (선택)</param>
        /// <returns>비동기 작업</returns>
        public async Task InvalidateAsync(RefreshToken token, string? replacedByToken = null)
        {
            // 1. 토큰 폐기 시간 기록
            token.RevokedAt = DateTime.UtcNow;

            // 2. 교체 토큰 기록
            token.ReplacedByToken = replacedByToken;

            // 3. DB 변경사항 저장
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// 특정 사용자에 대한 모든 유효한 리프레시 토큰 조회
        /// </summary>
        /// <param name="userId">사용자 ID</param>
        /// <returns>유효한 리프레시 토큰 컬렉션</returns>
        public async Task<IEnumerable<RefreshToken>> GetAllValidTokensForUserAsync(long userId)
        {
            return await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();
        }

        /// <summary>
        /// 특정 사용자 + 특정 기기(deviceId)에 대한 활성 리프레시 토큰 조회
        /// </summary>
        /// <param name="userId">사용자 ID</param>
        /// <param name="deviceId">기기 식별자</param>
        /// <returns>활성 리프레시 토큰 컬렉션</returns>
        public async Task<IEnumerable<RefreshToken>> GetActiveTokensForUserDeviceAsync(long userId, string deviceId)
        {
            return await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.DeviceId == deviceId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();
        }
    }
}
