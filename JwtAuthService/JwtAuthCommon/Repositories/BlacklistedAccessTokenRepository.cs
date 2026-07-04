using JwtAuthCommon.Data;
using JwtAuthCommon.Entities;
using JwtAuthCommon.Services;
using Microsoft.EntityFrameworkCore;

namespace JwtAuthCommon.Repositories
{
    /// <summary>
    /// 블랙리스트 Access Token DB 저장소 구현.
    /// EF Core(AppDbContext.BlacklistedAccessTokens)를 통해 MySQL과 연동한다.
    /// </summary>
    public class BlacklistedAccessTokenRepository : IBlacklistedAccessTokenRepository
    {
        /// <summary>DB 컨텍스트</summary>
        private readonly AppDbContext _db;

        /// <summary>
        /// 생성자: 의존성 주입
        /// </summary>
        /// <param name="db">애플리케이션 DB 컨텍스트</param>
        public BlacklistedAccessTokenRepository(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// 블랙리스트 항목을 DB에 추가한다. Jti는 유니크 제약이 걸려 있으므로,
        /// 이미 존재하는 경우(동시 요청 등으로 인한 중복 등록 시도) 조용히 무시한다.
        /// </summary>
        /// <param name="jti">JWT 고유 식별자</param>
        /// <param name="expiresAt">원본 Access 토큰의 만료 시각(UTC)</param>
        public async Task AddAsync(string jti, DateTime expiresAt)
        {
            // 1. 이미 등록된 Jti인지 먼저 확인 (중복 INSERT로 인한 유니크 제약 위반 방지)
            var already = await _db.BlacklistedAccessTokens
                .AnyAsync(b => b.Jti == jti);

            if (already) return;

            // 2. 신규 블랙리스트 엔티티 생성
            var entity = new BlacklistedAccessTokenEntity
            {
                Jti = jti,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow
            };

            // 3. DB에 추가 및 저장
            _db.BlacklistedAccessTokens.Add(entity);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// 여러 블랙리스트 항목을 한 번의 DB 왕복으로 일괄 추가한다.
        /// BlacklistDbWriterHostedService가 큐에서 모은 배치를 저장할 때 사용한다.
        /// </summary>
        /// <param name="entries">추가할 (Jti, ExpiresAt) 목록</param>
        public async Task AddRangeAsync(IReadOnlyCollection<BlacklistWriteQueue.Entry> entries)
        {
            if (entries == null || entries.Count == 0) return;

            // 1. 배치 내에서 동일 Jti가 중복으로 들어온 경우 마지막 값만 남기고 정리
            var distinctByJti = entries
                .GroupBy(e => e.Jti)
                .Select(g => g.Last())
                .ToList();

            // 2. 배치의 Jti 목록으로 DB에 이미 존재하는 항목을 한 번에 조회 (건별 AnyAsync 대신 IN 절 1회 쿼리)
            var jtiList = distinctByJti.Select(e => e.Jti).ToList();
            var existingJtis = await _db.BlacklistedAccessTokens
                .Where(b => jtiList.Contains(b.Jti))
                .Select(b => b.Jti)
                .ToListAsync();
            var existingSet = existingJtis.ToHashSet();

            // 3. 아직 DB에 없는 항목만 신규 엔티티로 변환
            var now = DateTime.UtcNow;
            var toInsert = distinctByJti
                .Where(e => !existingSet.Contains(e.Jti))
                .Select(e => new BlacklistedAccessTokenEntity
                {
                    Jti = e.Jti,
                    ExpiresAt = e.ExpiresAt,
                    CreatedAt = now
                })
                .ToList();

            if (toInsert.Count == 0) return;

            // 4. 한 번의 SaveChangesAsync 호출로 배치 전체를 INSERT (DB 왕복 횟수 최소화)
            _db.BlacklistedAccessTokens.AddRange(toInsert);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// 주어진 Jti가 DB 블랙리스트에 존재하는지 확인한다.
        /// </summary>
        /// <param name="jti">확인할 JWT 고유 식별자</param>
        public async Task<bool> ExistsAsync(string jti)
        {
            if (string.IsNullOrEmpty(jti)) return false;

            return await _db.BlacklistedAccessTokens
                .AnyAsync(b => b.Jti == jti);
        }

        /// <summary>
        /// 아직 만료되지 않은 블랙리스트 항목을 전부 조회한다. (Redis 웜업용)
        /// </summary>
        public async Task<IEnumerable<BlacklistedAccessTokenEntity>> GetAllValidAsync()
        {
            var now = DateTime.UtcNow;

            return await _db.BlacklistedAccessTokens
                .Where(b => b.ExpiresAt > now)
                .ToListAsync();
        }

        /// <summary>
        /// 이미 만료된 블랙리스트 항목을 일괄 삭제한다. (주기적 정리 배치용)
        /// </summary>
        /// <returns>삭제된 행(row) 개수</returns>
        public async Task<int> DeleteExpiredAsync()
        {
            var now = DateTime.UtcNow;

            // EF Core 7+ ExecuteDeleteAsync: 엔티티를 메모리로 로드하지 않고 DB에서 직접 삭제(성능 우수)
            return await _db.BlacklistedAccessTokens
                .Where(b => b.ExpiresAt <= now)
                .ExecuteDeleteAsync();
        }
    }
}
