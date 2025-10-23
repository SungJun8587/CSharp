namespace JwtAuthService.Models
{
    /// <summary>블랙리스트에 등록된 액세스 토큰 엔티티 모델</summary>
    public class BlacklistedAccessToken
    {
        /// <summary>토큰 ID</summary>
        public long Id { get; set; }

        /// <summary>JWT 고유 식별자 (JTI)</summary>
        public string Jti { get; set; } = null!;

        /// <summary>토큰 만료 일시 (UTC)</summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>토큰 블랙리스트 등록 일시 (UTC)</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
