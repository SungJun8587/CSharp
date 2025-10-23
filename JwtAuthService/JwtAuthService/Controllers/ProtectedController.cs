using JwtAuthService.Models;
using JwtAuthService.Repositories;
using JwtAuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;

namespace JwtAuthService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProtectedController : ControllerBase
    {
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IJwtService _jwtService;

        public ProtectedController(IRefreshTokenRepository refreshTokenRepository, IJwtService jwtService)
        {
            _refreshTokenRepository = refreshTokenRepository;
            _jwtService = jwtService;
        }

        /// <summary>
        /// 보호된 리소스 조회
        /// </summary>
        /// <returns>사용자 정보와 메시지</returns>
        [HttpGet("userinfo")]
        [Authorize]                 // JWT 인증이 있어야 접근 가능
        public IActionResult GetUserInfo()
        {
            // 1. 사용자 ID 클레임 가져오기
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null) return Unauthorized(new { message = "Invalid token or missing claim." });

            // 2. 사용자 이름 클레임 가져오기
            var usernameClaim = User.Claims.FirstOrDefault(c => c.Type == "username");
            if (usernameClaim == null) return Unauthorized(new { message = "Invalid token or missing username claim." });

            // 3. 사용자 이메일 가져오기
            var useremailClaim = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email);    // 필수가 아니므로, 에러 체크 제외.

            // 4. 사용자 ID와 이름, 이메일 가져오기
            var userId = userIdClaim.Value;
            var userName = usernameClaim.Value;
            var userEmail = useremailClaim?.Value;

            // 5. 예시 응답 (실제 서비스에서는 DB에서 사용자 정보 조회 가능)
            return Ok(new
            {
                message = "This is a protected resource.",
                userid = userId,
                username = userName,
                email = userEmail,      
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// 현재 토큰 정보 조회
        /// </summary>
        [HttpGet("tokeninfo")]
        [Authorize]
        public async Task<ActionResult<TokenExpiryResponse>> GetTokenInfo()
        {
            // 1. Authorization 헤더에서 Bearer 토큰 추출
            var authHeader = HttpContext.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return Unauthorized();

            string accessToken = authHeader.Substring("Bearer ".Length);

            // 2. AccessToken에서 만료 시간 추출
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(accessToken);
            var expUnix = long.Parse(jwtToken.Claims.First(c => c.Type == JwtRegisteredClaimNames.Exp).Value);
            var accessExp = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;

            // 3. 토큰에서 사용자 Id 얻기
            var userId = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
            if (userId == null)
                return Unauthorized(new { message = "Invalid token: username claim missing." });

            // 4. DB에서 최신 RefreshToken 조회
            var refreshTokenInfo = await _refreshTokenRepository.GetAllValidTokensForUserAsync(long.Parse(userId));
            if (refreshTokenInfo == null)
                return NotFound(new { message = "RefreshToken not found." });

            var refreshToken = refreshTokenInfo.OrderByDescending(r => r.CreatedAt).FirstOrDefault();
            if (refreshToken == null)
                return NotFound(new { message = "RefreshToken not found." });

            // 5. RefreshToken 만료 시간 얻기
            var refreshExp = refreshToken.ExpiresAt;

            var response = new TokenExpiryResponse
            {
                AccessToken = accessToken,
                AccessTokenExpiresAt = accessExp,
                RefreshToken = refreshToken.Token,
                RefreshTokenExpiresAt = refreshExp,
                AccessTokenRemainingSeconds = (ulong)Math.Max(0, (accessExp - DateTime.UtcNow).TotalSeconds),
                RefreshTokenRemainingSeconds = (ulong)Math.Max(0, (refreshExp - DateTime.UtcNow).TotalSeconds)
            };

            return Ok(response);
        }

        /// <summary>
        /// 테스트용 보호된 엔드포인트
        /// </summary>
        /// <returns>인증 성공 메시지</returns>
        [HttpGet("test")]
        [Authorize]
        public IActionResult TestProtected()
        {
            return Ok(new { message = "You have accessed a protected endpoint!" });
        }
    }
}
