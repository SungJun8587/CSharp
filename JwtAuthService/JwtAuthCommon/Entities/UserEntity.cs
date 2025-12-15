namespace JwtAuthCommon.Entities
{
    /// <summary>사용자 엔티티</summary>
    public class UserEntity
    {
        /// <summary>사용자 고유 ID</summary>
        public long Id { get; set; }

        /// <summary>사용자 이름</summary>
        public string Username { get; set; } = null!;

        /// <summary>BCrypt 해시된 비밀번호</summary>
        public string Password_Hash { get; set; } = null!;

        /// <summary>이메일(선택)</summary>
        public string? Email { get; set; }

        /// <summary>사용자 역할</summary>
        public string Role { get; set; } = null!;

        /// <summary>생성 시간</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
