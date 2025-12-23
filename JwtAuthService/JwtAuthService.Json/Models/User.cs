using System.Text.Json.Serialization;

namespace JwtAuthService.Json.Models
{
    /// <summary>처리 또는 에러 응답</summary>
    public class ResponseData
    {
        /// <summary>성공 여부(true/false : 성공/실패)</summary>
        public bool Success { get; set; }

        /// <summary>처리 응답 또는 에러 메세지</summary>
        public string Message { get; set; } = null!;
    }

    /// <summary>액세스 토큰, 리프레시 토큰 응답</summary>
    public class TokenResponse
    {
        /// <summary>JWT 액세스 토큰(API 요청 시 Authorization 헤더에 사용)</summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>새로운 액세스 토큰을 발급받기 위한 리프레시 토큰</summary>
        public string RefreshToken { get; set; } = string.Empty;
    }

    /// <summary>회원가입 요청 후 서버가 반환하는 응답</summary>
    public class RegisterRequest
    {
        /// <summary>사용자 이름</summary>
        public string UserName { get; set; } = null!;
        
        /// <summary>비밀번호</summary>
        public string Password { get; set; } = null!;

        /// <summary>이메일 (선택)</summary>
        public string? Email { get; set; }

        /// <summary>사용자 역할(기본 : user, 관리자 : admin)</summary>
        public string? Role { get; set; }
    }

    /// <summary>로그인 요청</summary>
    public class LoginRequest
    {
        /// <summary>사용자 이름</summary>
        public string UserName { get; set; } = null!;
        
        /// <summary>비밀번호</summary>
        public string Password { get; set; } = null!;
        
        /// <summary>기기 식별자</summary>
        public string? DeviceId { get; set; }
    }

    /// <summary>로그인 요청 후 서버가 반환하는 응답</summary>
    public class LoginResponse : ResponseData
    {
        /// <summary>액세스 토큰, 리프레시 토큰 응답</summary>
        public TokenResponse Token { get; set; } = null!;
    }

    /// <summary>토큰 만료 일시 반환하는 응답</summary>
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

    /// <summary>리프레시 토큰 요청</summary>
    public class RefreshRequest
    {
        /// <summary>리프레시 토큰</summary>
        public string RefreshToken { get; set; } = null!;
        
        /// <summary>기기 식별자</summary>
        public string? DeviceId { get; set; }
    }

    /// <summary>리프레시 토큰 요청 후 서버가 반환하는 응답</summary>
    public class RefreshResponse : ResponseData
    {
        /// <summary>액세스 토큰, 리프레시 토큰 응답</summary>
        public TokenResponse Token { get; set; } = null!;
    }

    /// <summary>사용자 엔티티</summary>
    public class User
    {
        /// <summary>사용자 고유 ID</summary>
        [JsonPropertyName("userid")]
        public string UserId { get; set; } = null!;

        /// <summary>사용자 이름</summary>
        [JsonPropertyName("username")]
        public string UserName { get; set; } = null!;

        /// <summary>BCrypt 해시된 비밀번호</summary>
        public string Password_Hash { get; set; } = null!;

        /// <summary>이메일 (선택)</summary>
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        /// <summary>사용자 역할</summary>
        [JsonPropertyName("role")]
        public string Role { get; set; } = null!;

        /// <summary>생성 시간</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
