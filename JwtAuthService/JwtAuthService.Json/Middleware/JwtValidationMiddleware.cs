using JwtAuthCommon.Services;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace JwtAuthService.Json.Middleware
{
    /// <summary>
    /// JWT 액세스 토큰 검증 미들웨어
    /// 요청 헤더의 Authorization 토큰을 검증하고, 블랙리스트 체크 후 HttpContext.User 설정
    /// </summary>
    public class JwtValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _config;
        private readonly ITokenBlacklistService _blacklistService;

        /// <summary>
        /// 생성자: 의존성 주입
        /// </summary>
        /// <param name="next">다음 미들웨어</param>
        /// <param name="config">앱 설정 (JWT 시크릿, 발행자 등)</param>
        /// <param name="blacklistService">토큰 블랙리스트 서비스</param>
        public JwtValidationMiddleware(RequestDelegate next, IConfiguration config, ITokenBlacklistService blacklistService)
        {
            _next = next;
            _config = config;
            _blacklistService = blacklistService;
        }

        /// <summary>
        /// 미들웨어 실행
        /// 요청 헤더에서 토큰 추출 → 블랙리스트 확인 → JWT 유효성 검증 → HttpContext.User 설정
        /// </summary>
        /// <param name="context">HTTP 컨텍스트</param>
        /// <returns>비동기 작업</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            // 1. Authorization 헤더에서 Bearer 토큰 추출
            var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

            // 2. JWT 시크릿 확인
            var secret = _config["Jwt:Secret"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException("JWT Secret is not configured. Please check appsettings.json or environment variables.");
            }

            if (!string.IsNullOrEmpty(token))
            {
                // 3. 블랙리스트 체크
                if (await _blacklistService.IsBlacklistedAsync(token))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Token is blacklisted or revoked.");
                    return;
                }

                try
                {
                    // 4. 토큰 검증
                    var tokenHandler = new JwtSecurityTokenHandler();
                    var key = Encoding.UTF8.GetBytes(secret);

                    tokenHandler.ValidateToken(token, new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(key),
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidIssuer = _config["Jwt:Issuer"],
                        ValidAudience = _config["Jwt:Audience"],
                        ClockSkew = TimeSpan.Zero
                    }, out SecurityToken validatedToken);

                    var jwtToken = (JwtSecurityToken)validatedToken;

                    // 5. 토큰 클레임에서 사용자 ID 추출
                    var userId = jwtToken.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value;

                    if (!string.IsNullOrEmpty(userId))
                    {
                        // 6. HttpContext.User 설정
                        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
                        var identity = new ClaimsIdentity(claims, "jwt");
                        context.User = new ClaimsPrincipal(identity);
                    }
                }
                catch (Exception)
                {
                    // 7. 토큰 검증 실패 시 401 응답
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Invalid or expired token.");
                    return;
                }
            }

            // 8. 다음 미들웨어 호출
            await _next(context);
        }
    }
}
