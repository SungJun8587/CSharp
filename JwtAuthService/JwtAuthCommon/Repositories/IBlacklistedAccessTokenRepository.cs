using JwtAuthCommon.Entities;
using JwtAuthCommon.Services;

namespace JwtAuthCommon.Repositories
{
    /// <summary>
    /// 블랙리스트 Access Token(BlacklistedAccessTokens 테이블)에 대한 저장소 인터페이스.
    /// Redis는 실시간 조회용 캐시 역할을 하고, 이 저장소는 영속 기록(감사 로그) 및
    /// Redis 장애 시 복구용 소스 역할을 담당한다.
    /// </summary>
    public interface IBlacklistedAccessTokenRepository
    {
        /// <summary>
        /// 블랙리스트 항목을 DB에 추가한다. 이미 동일 Jti가 존재하면 아무 작업도 하지 않는다.
        /// 단건 처리용 - 웜업/테스트 등 배치가 필요 없는 경우에 사용한다.
        /// </summary>
        /// <param name="jti">JWT 고유 식별자</param>
        /// <param name="expiresAt">원본 Access 토큰의 만료 시각(UTC)</param>
        Task AddAsync(string jti, DateTime expiresAt);

        /// <summary>
        /// 여러 블랙리스트 항목을 한 번의 DB 왕복으로 일괄 추가한다.
        /// BlacklistDbWriterHostedService가 큐에서 모은 배치를 저장할 때 사용하며,
        /// 이미 존재하는 Jti는 건너뛰어 중복 삽입(유니크 제약 위반)을 방지한다.
        /// </summary>
        /// <param name="entries">추가할 (Jti, ExpiresAt) 목록</param>
        Task AddRangeAsync(IReadOnlyCollection<BlacklistWriteQueue.Entry> entries);

        /// <summary>
        /// 주어진 Jti가 DB 블랙리스트에 존재하는지 확인한다.
        /// (Redis 장애 등 예외 상황의 폴백 조회용. 평상시 실시간 검증에는 사용하지 않는다.)
        /// </summary>
        /// <param name="jti">확인할 JWT 고유 식별자</param>
        Task<bool> ExistsAsync(string jti);

        /// <summary>
        /// 아직 만료되지 않은(ExpiresAt > now) 블랙리스트 항목을 전부 조회한다.
        /// 서버 기동 시 Redis 웜업(재구성)에 사용한다.
        /// </summary>
        Task<IEnumerable<BlacklistedAccessTokenEntity>> GetAllValidAsync();

        /// <summary>
        /// 이미 만료된(ExpiresAt &lt;= now) 블랙리스트 항목을 일괄 삭제한다.
        /// 주기적인 정리 배치(Cleanup HostedService)에서 사용한다.
        /// </summary>
        /// <returns>삭제된 행(row) 개수</returns>
        Task<int> DeleteExpiredAsync();
    }
}
