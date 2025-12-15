using JwtAuthCommon.Repositories;
using JwtAuthCommon.Services;
using JwtAuthService.Protobuf.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;

namespace JwtAuthService.Protobuf.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IJwtService _jwtService;

        public UserController(IRefreshTokenRepository refreshTokenRepository, IJwtService jwtService)
        {
            _refreshTokenRepository = refreshTokenRepository;
            _jwtService = jwtService;
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("all")]
        [Produces("application/x-protobuf")]
        public ActionResult<List<User>> All()
        {
            // 예시: 간단한 Admin 전용 엔드포인트
            return new List<User> { new User { UserId = "1", Username = "admin", Role = "Admin" } };
        }

        /// <summary>
        /// 보호된 리소스 조회
        /// </summary>
        /// <returns>사용자 정보와 메시지</returns>
        [Authorize]                 // JWT 인증이 있어야 접근 가능
        [HttpGet("userinfo")]
        [Produces("application/x-protobuf")]
        public IActionResult GetUserInfo()
        {
            // 1. 사용자 ID 클레임 가져오기
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null) return Unauthorized(new { message = "Invalid token or missing userId claim." });

            // 2. 사용자 이름 클레임 가져오기
            var userNameClaim = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Name);
            if (userNameClaim == null) return Unauthorized(new { message = "Invalid token or missing userName claim." });

            // 3. 사용자 이메일 가져오기
            var userEmailClaim = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email);    // 필수가 아니므로, 에러 체크 제외.

            // 4. 사용자 역할 가져오기
            var userRoleClaim = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role);
            if (userRoleClaim == null) return Unauthorized(new { message = "Invalid token or missing userRole claim." });

            // 5. 사용자 ID와 이름, 이메일 가져오기
            var userId = userIdClaim.Value;
            var userName = userNameClaim.Value;
            var userEmail = userEmailClaim?.Value;
            var userRole = userRoleClaim.Value;

            // 6. 예시 응답(실제 서비스에서는 DB에서 사용자 정보 조회 가능)
            return Ok(new User
            {
                UserId = userId,
                Username = userName,
                Email = userEmail,
                Role = userRole,
                CreatedAt = DateTime.UtcNow.ToTimestampUtc()
            });
        }

        /// <summary>
        /// 현재 토큰 정보 조회
        /// </summary>
        [Authorize]
        [HttpGet("tokeninfo")]
        [Produces("application/x-protobuf")]
        public async Task<ActionResult<TokenExpiryResponse>> GetTokenInfo()
        {
            // 1. Authorization 헤더에서 Bearer 토큰 추출
            var authHeader = HttpContext.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return Unauthorized(new { message = "Invalid token." });

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
                AccessTokenExpiresAt = accessExp.ToTimestampUtc(),
                RefreshToken = refreshToken.Token,
                RefreshTokenExpiresAt = refreshExp.ToTimestampUtc(),
                AccessTokenRemainingSeconds = (ulong)Math.Max(0, (accessExp - DateTime.UtcNow).TotalSeconds),
                RefreshTokenRemainingSeconds = (ulong)Math.Max(0, (refreshExp - DateTime.UtcNow).TotalSeconds)
            };

            return Ok(response);
        }
    }
}
