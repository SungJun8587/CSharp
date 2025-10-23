using JwtAuthService.Repositories;

namespace JwtAuthService.Services
{
    /// <summary>인증 관련 비즈니스 로직 서비스</summary>
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepo;
        private readonly IJwtService _jwtService;
        private readonly IRefreshTokenRepository _refreshRepo;

        /// <summary>생성자: 의존성 주입</summary>
        public AuthService(IUserRepository userRepo, IJwtService jwtService, IRefreshTokenRepository refreshRepo)
        {
            _userRepo = userRepo;
            _jwtService = jwtService;
            _refreshRepo = refreshRepo;
        }

        /// <summary>사용자 로그인 처리 및 액세스/리프레시 토큰 발급</summary>
        /// <param name="username">사용자 이름</param>
        /// <param name="password">비밀번호</param>
        /// <param name="deviceId">기기 식별자 (선택)</param>
        /// <returns>액세스 토큰, 리프레시 토큰, 만료 시간 또는 null</returns>
        public async Task<object?> LoginAsync(string username, string password, string? deviceId = null)
        {
            var user = await _userRepo.GetByUsernameAsync(username);
            if (user == null) return null;
            if (!BCrypt.Net.BCrypt.Verify(password, user.Password_Hash)) return null;

            var (access, refresh) = await _jwtService.GenerateTokensAsync(user, deviceId);
            return new { accessToken = access, refreshToken = refresh, expiresIn = 60 * 15 };
        }

        /// <summary>리프레시 토큰으로 액세스 토큰 재발급 (토큰 로테이션)</summary>
        /// <param name="refreshToken">리프레시 토큰 값</param>
        /// <param name="deviceId">기기 식별자 (선택)</param>
        /// <returns>새 액세스/리프레시 토큰 또는 null</returns>
        public async Task<object?> RefreshAsync(string refreshToken, string? deviceId = null)
        {
            var (access, refresh, error) = await _jwtService.RotateRefreshTokenAsync(refreshToken, deviceId);
            if (error != null) return null;
            return new { accessToken = access, refreshToken = refresh };
        }

        /// <summary>로그아웃 처리 및 리프레시 토큰 폐기</summary>
        /// <param name="refreshToken">리프레시 토큰 값</param>
        /// <param name="deviceId">기기 식별자 (선택)</param>
        public async Task LogoutAsync(string? refreshToken, string? deviceId = null)
        {
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var existing = await _refreshRepo.GetByTokenAsync(refreshToken);
                if (existing != null) await _refreshRepo.InvalidateAsync(existing, null);
            }
        }
    }
}
