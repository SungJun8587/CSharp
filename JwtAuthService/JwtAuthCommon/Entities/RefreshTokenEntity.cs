namespace JwtAuthCommon.Entities
{
    /// <summary>리프레시 토큰 엔티티</summary>
    public class RefreshTokenEntity
    {
        /// <summary>토큰 ID</summary>
        public long Id { get; set; }

        /// <summary>연결된 사용자 ID</summary>
        public long UserId { get; set; }

        /// <summary>리프레시 토큰 값</summary>
        public string Token { get; set; } = null!;

        /// <summary>연결된 기기 식별자</summary>
        public string? DeviceId { get; set; }

        /// <summary>토큰 만료 일시 (UTC)</summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>토큰 생성 일시 (UTC)</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>토큰 폐기 일시 (UTC, null이면 아직 활성)</summary>
        public DateTime? RevokedAt { get; set; }

        /// <summary>이 토큰을 대체한 토큰 값 (null이면 없음)</summary>
        public string? ReplacedByToken { get; set; }

        /// <summary>토큰 활성 여부 (폐기되지 않았고 만료되지 않은 경우)</summary>
        public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;
    }
}
