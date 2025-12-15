using Microsoft.IdentityModel.Tokens;
using JwtAuthCommon.Repositories;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using JwtAuthCommon.Entities;

namespace JwtAuthCommon.Services
{
    /// <summary>
    /// JWT 액세스 토큰 및 리프레시 토큰 생성, 검증, 로테이션 서비스.
    /// Redis 블랙리스트 연동 및 기기 바인딩 기능 포함.
    /// </summary>
    public class JwtService : IJwtService
    {
        /// <summary>앱 설정(Configuration) 주입</summary>
        private readonly IConfiguration _config;

        /// <summary>리프레시 토큰 저장소</summary>
        private readonly IRefreshTokenRepository _refreshRepo;

        /// <summary>사용자 저장소</summary>
        private readonly IUserRepository _userRepo;

        /// <summary>액세스 토큰 블랙리스트 서비스 (선택)</summary>
        private readonly ITokenBlacklistService? _blacklist;

        /// <summary>액세스 토큰 만료 시간 (분)</summary>
        private readonly int _accessMinutes;

        /// <summary>리프레시 토큰 만료 시간 (일)</summary>
        private readonly int _refreshDays;

        /// <summary>생성자: 의존성 주입 및 설정값 초기화</summary>
        /// <param name="config">앱 설정</param>
        /// <param name="refreshRepo">리프레시 토큰 저장소</param>
        /// <param name="userRepo">사용자 저장소</param>
        /// <param name="blacklist">블랙리스트 서비스 (선택)</param>
        public JwtService(IConfiguration config, IRefreshTokenRepository refreshRepo, IUserRepository userRepo, ITokenBlacklistService? blacklist = null)
        {
            // 1. IConfiguration 객체 저장(appsettings.json, 환경 변수 등에서 JWT 설정을 읽기 위해 사용)
            _config = config;

            // 2. IRefreshTokenRepository 객체 저장(리프레시 토큰 DB 작업용)
            _refreshRepo = refreshRepo;

            // 3. IUserRepository 객체 저장(사용자 정보 DB 작업용)
            _userRepo = userRepo;

            // 4. ITokenBlacklistService 객체 저장(액세스 토큰 블랙리스트 관리용, 선택적)
            _blacklist = blacklist;

            // 5. 액세스 토큰 만료 시간을 설정(appsettings.json의 Jwt:AccessTokenExpirationMinutes 값 사용)
            _accessMinutes = _config.GetValue<int>("Jwt:AccessTokenExpirationMinutes");

            // 6. 리프레시 토큰 만료 기간을 설정(appsettings.json의 Jwt:RefreshTokenExpirationDays 값 사용)
            _refreshDays = _config.GetValue<int>("Jwt:RefreshTokenExpirationDays");
        }

        /// <summary>
        /// 사용자 정보로 액세스 토큰 생성
        /// </summary>
        /// <param name="user">사용자 엔티티</param>
        /// <returns>JWT 액세스 토큰 문자열</returns>
        public string GenerateAccessToken(UserEntity user)
        {
            // JWT 설정 섹션 가져오기
            var jwt = _config.GetSection("Jwt");
            var secret = jwt.GetValue<string>("Secret")!;
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // 고유 식별자(JTI) 생성
            // 1. 블랙리스트 처리
            //      - 토큰을 강제로 만료시키거나 로그아웃 시 블랙리스트에 추가할 수 있다.
            //      - 블랙리스트에서는 JTI를 기준으로 특정 토큰을 식별한다.
            // 2. 중복 사용 방지
            //      - 같은 토큰이 여러 번 사용되는 것을 방지할 수 있다.
            //      - 예를 들어, 결제 API 같은 민감한 곳에서는 같은 JTI를 두 번 사용하면 거부할 수 있다.
            // 3. 토큰 추적
            //      - 서버 로그나 감사 기록에서 어떤 토큰으로 요청이 들어왔는지 추적 가능.
            var jti = Guid.NewGuid().ToString();

            // 클레임 설정
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),                 // 사용자 ID 추가
                new Claim(ClaimTypes.Name, user.Username ?? string.Empty),                  // 사용자 이름 추가
                new Claim(ClaimTypes.Role, user.Role),                                      // 사용자 역할 추가
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),       // 이메일 추가
                new Claim(JwtRegisteredClaimNames.Jti, jti)                                 // 고유 식별자(JTI) 추가
            };

            // 토큰 만료 시간 계산
            var expires = DateTime.UtcNow.AddMinutes(_accessMinutes);

            // JWT 토큰 생성
            var token = new JwtSecurityToken(
                issuer: jwt.GetValue<string>("Issuer"),          // 토큰 발급자(issuer) 설정 : 토큰을 발급한 서버/서비스 식별
                audience: jwt.GetValue<string>("Audience"),      // 토큰 대상(audience) 설정 : 토큰을 사용할 클라이언트/서비스 식별
                claims: claims,                                  // 토큰에 담길 클레임(Claims) 목록 : 사용자 정보, 권한, JTI 등
                expires: expires,                                // 토큰 만료 시간(expiration) : UTC 기준, 이 시간이 지나면 토큰 만료
                signingCredentials: creds                        // 서명 정보(SigningCredentials) : 토큰 위변조 방지용 비밀키 및 알고리즘
            );

            // 토큰 문자열 반환
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// 액세스 토큰과 리프레시 토큰 생성
        /// 기기 ID가 지정된 경우 기존 토큰은 무효화
        /// </summary>
        /// <param name="user">사용자 엔티티</param>
        /// <param name="deviceId">기기 식별자 (선택)</param>
        /// <returns>액세스 토큰과 리프레시 토큰</returns>
        public async Task<(string accessToken, string refreshToken)> GenerateTokensAsync(UserEntity user, string? deviceId = null)
        {
            // 기기 ID가 존재하면 해당 기기용 기존 활성 리프레시 토큰을 모두 무효화
            if (!string.IsNullOrEmpty(deviceId))
            {
                var existing = await _refreshRepo.GetActiveTokensForUserDeviceAsync(user.Id, deviceId);
                foreach (var t in existing)
                {
                    // 기존 토큰 폐기
                    await _refreshRepo.InvalidateAsync(t, null);
                }
            }

            // 새 액세스 토큰 생성
            var access = GenerateAccessToken(user);

            // 새 리프레시 토큰 생성
            var refresh = new RefreshTokenEntity
            {
                UserId = user.Id,
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),     // 안전한 랜덤 토큰
                DeviceId = deviceId,                                                    // 기기 바인딩
                ExpiresAt = DateTime.UtcNow.AddDays(_refreshDays)                       // 만료일 설정
            };

            // DB에 새 리프레시 토큰 저장
            await _refreshRepo.AddAsync(refresh);

            // 액세스 토큰과 리프레시 토큰 반환
            return (access, refresh.Token);
        }

        /// <summary>
        /// 액세스 토큰을 블랙리스트에 등록하여 폐기 처리
        /// </summary>
        /// <param name="accessToken">폐기할 액세스 토큰</param>
        public async Task InvalidateAccessTokenAsync(string accessToken)
        {
            if (_blacklist == null) return;

            // JWT 토큰 읽기
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(accessToken);
            var jti = jwt.Id;
            var exp = jwt.ValidTo - DateTime.UtcNow;

            // 아직 유효 기간이 남아 있으면 블랙리스트에 추가
            if (exp.TotalSeconds > 0)
                await _blacklist.AddToBlacklistAsync(jti, exp);
        }

        /// <summary>
        /// 리프레시 토큰 로테이션
        /// 기존 토큰 무효화 후 새 액세스/리프레시 토큰 발급
        /// </summary>
        /// <param name="oldRefreshToken">기존 리프레시 토큰</param>
        /// <param name="deviceId">기기 식별자 (선택)</param>
        /// <returns>새 액세스 토큰, 새 리프레시 토큰, 오류 메시지 또는 null</returns>
        public async Task<(string? accessToken, string? refreshToken, string? error)> RotateRefreshTokenAsync(string oldRefreshToken, string? deviceId = null)
        {
            // 기존 리프레시 토큰 조회
            var existing = await _refreshRepo.GetByTokenAsync(oldRefreshToken);
            if (existing == null)
                return (null, null, "invalid_refresh_token");

            // 이미 폐기된 토큰 확인
            if (existing.RevokedAt != null)
                return (null, null, "token_revoked");

            // 토큰 만료 확인
            if (existing.ExpiresAt <= DateTime.UtcNow)
                return (null, null, "token_expired");

            // 기기 ID가 불일치하면 무효화 후 에러 반환
            if (!string.IsNullOrEmpty(existing.DeviceId) && existing.DeviceId != deviceId)
            {
                await _refreshRepo.InvalidateAsync(existing, null);
                return (null, null, "device_mismatch");
            }

            // 새 리프레시 토큰 생성
            var newRefresh = new RefreshTokenEntity
            {
                UserId = existing.UserId,
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                DeviceId = existing.DeviceId ?? deviceId,
                ExpiresAt = DateTime.UtcNow.AddDays(_refreshDays)
            };

            // 기존 토큰 무효화 및 DB 저장
            await _refreshRepo.InvalidateAsync(existing, newRefresh.Token);
            await _refreshRepo.AddAsync(newRefresh);

            // 사용자 조회
            var user = await _userRepo.GetByIdAsync(existing.UserId);
            if (user == null) return (null, null, "user_not_found");

            // 새 액세스 토큰 생성
            var newAccess = GenerateAccessToken(user);

            // 새 액세스/리프레시 토큰 반환
            return (newAccess, newRefresh.Token, null);
        }

        /// <summary>
        /// 주어진 JWT 액세스 토큰에서 만료 시간(UTC)을 반환.
        /// </summary>
        /// <param name="accessToken">JWT 액세스 토큰 문자열</param>
        /// <returns>토큰 만료 시간 (UTC)</returns>
        public DateTime GetExpirationFromToken(string accessToken)
        {
            // JWT 토큰 핸들러 생성
            var handler = new JwtSecurityTokenHandler();

            // 토큰 파싱: 문자열 -> JwtSecurityToken 객체
            var jwtToken = handler.ReadJwtToken(accessToken);

            // 'exp' 클레임(만료 시간)을 가져오기 (Unix epoch 초 단위)
            var expUnix = long.Parse(jwtToken.Claims.First(c => c.Type == JwtRegisteredClaimNames.Exp).Value);

            // Unix epoch 초를 DateTime(UTC)로 변환
            var expDateTime = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;

            return expDateTime;
        }

        /// <summary>
        /// 액세스 토큰이 블랙리스트에 등록되었는지 확인
        /// </summary>
        /// <param name="accessToken">확인할 액세스 토큰</param>
        /// <returns>블랙리스트 여부</returns>
        public async Task<bool> IsAccessTokenBlacklistedAsync(string accessToken)
        {
            if (_blacklist == null) return false;

            // JWT 토큰 읽기
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(accessToken);

            // 블랙리스트 여부 반환
            return await _blacklist.IsBlacklistedAsync(jwt.Id);
        }
    }
}
