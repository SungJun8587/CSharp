using System.Text.Json.Serialization;

namespace JwtAuthService.Models
{
    /// <summary>회원가입 요청 모델</summary>
    public class RegisterRequest
    {
        /// <summary>사용자 이름</summary>
        public string Username { get; set; } = null!;
        
        /// <summary>비밀번호</summary>
        public string Password { get; set; } = null!;

        /// <summary>이메일 (선택)</summary>
        public string? Email { get; set; }
    }

    /// <summary>로그인 요청 모델</summary>
    public class LoginRequest
    {
        /// <summary>사용자 이름</summary>
        public string Username { get; set; } = null!;
        
        /// <summary>비밀번호</summary>
        public string Password { get; set; } = null!;
        
        /// <summary>기기 식별자</summary>
        public string? DeviceId { get; set; }
    }

    /// <summary>로그인 요청 후 서버가 반환하는 응답 모델</summary>
    public class TokenResponse
    {
        /// <summary>
        /// JWT 액세스 토큰 (API 요청 시 Authorization 헤더에 사용)
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// 새로운 액세스 토큰을 발급받기 위한 리프레시 토큰
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;
    }

    /// <summary>토큰 만료 일시 반환하는 응답 모델</summary>
    public class TokenExpiryResponse
    {
        /// <summary>
        /// JWT 액세스 토큰(API 요청 시 Authorization 헤더에 사용)
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>액세스 토큰 만료 일시(UTC)</summary>
        public DateTime AccessTokenExpiresAt { get; set; }

        /// <summary>
        /// 새로운 액세스 토큰을 발급받기 위한 리프레시 토큰
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>리프레시 토큰 만료 일시(UTC)</summary>
        public DateTime RefreshTokenExpiresAt { get; set; }

        /// <summary>액세스 토큰 남은 유효 시간(초)</summary>
        public ulong AccessTokenRemainingSeconds { get; set; }

        /// <summary>리프레시 토큰 남은 유효 시간(초)</summary>
        public ulong RefreshTokenRemainingSeconds { get; set; }
    }

    /// <summary>리프레시 토큰 요청 모델</summary>
    public class RefreshRequest
    {
        /// <summary>리프레시 토큰</summary>
        public string RefreshToken { get; set; } = null!;
        
        /// <summary>기기 식별자</summary>
        public string? DeviceId { get; set; }
    }

    /// <summary>사용자 엔티티</summary>
    public class User
    {
        /// <summary>사용자 고유 ID</summary>
        [JsonPropertyName("userid")]
        public long Id { get; set; }

        /// <summary>사용자 이름</summary>
        [JsonPropertyName("username")]
        public string Username { get; set; } = null!;

        /// <summary>BCrypt 해시된 비밀번호</summary>
        public string Password_Hash { get; set; } = null!;

        /// <summary>이메일 (선택)</summary>
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        /// <summary>생성 시간</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
