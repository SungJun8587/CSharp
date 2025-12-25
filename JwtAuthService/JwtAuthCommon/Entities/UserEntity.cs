namespace JwtAuthCommon.Entities
{
    /// <summary>사용자 엔티티</summary>
    public class UserEntity
    {
        /// <summary>
        /// 사용자 고유 ID
        ///     - 1 ~ 100 : 관리자
        ///     - 101 부터 : 유저
        /// </summary>
        public long Id { get; set; }

        /// <summary>사용자 이름</summary>
        public string Username { get; set; } = null!;

        /// <summary>BCrypt 해시된 비밀번호</summary>
        public string Password_Hash { get; set; } = null!;

        /// <summary>이메일(선택)</summary>
        public string? Email { get; set; }

        /// <summary>사용자 역할</summary>
        public string Role { get; set; } = null!;

        /// <summary>
        /// 계정 활성화 여부(true/false : 유/무)
        //      - false 인 경우 로그인 차단(정지 계정 처리 용도)
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>계정 생성 일시</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>마지막 로그인 일시</summary>
        public DateTime? LastLoginAt { get; set; }

        /// <summary>계정 활성화 상태가 마지막으로 변경된 일시</summary>
        public DateTime? IsActiveChangedAt { get; set; }
    }
}
