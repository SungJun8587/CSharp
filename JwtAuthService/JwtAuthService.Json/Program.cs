using JwtAuthCommon.Data;
using JwtAuthCommon.Repositories;
using JwtAuthCommon.Services;
using JwtAuthService.Json.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// 1. 테스트 환경이면 로그 레벨 제한
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddFilter(level => level >= LogLevel.Warning); // Warning 이상만
}

var configuration = builder.Configuration;

// 1. DbContext 설정 (MySQL, Pomelo)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 33))));

// 2. Redis 설정 및 싱글톤 등록
var redisConf = configuration.GetValue<string>("Redis:Configuration") ?? "localhost:6379";
var multiplexer = ConnectionMultiplexer.Connect(redisConf);
builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);
builder.Services.AddSingleton<ITokenBlacklistService, TokenBlacklistService>();

// 3. Repositories 및 서비스 등록
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// 4. JWT 인증 설정
var jwt = configuration.GetSection("Jwt");
var secret = jwt.GetValue<string>("Secret") ?? throw new Exception("Jwt:Secret missing");
var key = Encoding.UTF8.GetBytes(secret);
var validIssuer = jwt.GetValue<string>("Issuer");
var validAudience = jwt.GetValue<string>("Audience");

builder.Services.AddAuthentication(options =>
{
    // 기본 인증 스킴을 JWT Bearer 로 설정
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;                                 // HTTPS 강제 여부(false 하면 개발환경에서 http 허용)
    options.SaveToken = true;                                             // JWT를 AuthenticationProperties에 저장할지 여부

    // JWT 토큰 검증 조건 설정
    options.TokenValidationParameters = new TokenValidationParameters
    {
        // Issuer(발급자) 검증
        ValidateIssuer = true,
        ValidIssuer = validIssuer,

        // Audience(대상자) 검증
        ValidateAudience = true,
        ValidAudience = validAudience,

        // 서명키 검증(HMAC SHA256 등)
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),

        // 유효 기간(exp) 검증
        ValidateLifetime = true
    };

    // 5. JWT 이벤트(수신/검증/실패 등) 처리자 설정
    options.Events = new JwtBearerEvents
    {
        // 5-1. 토큰이 수신되었을 때 가장 먼저 호출되는 이벤트
        OnMessageReceived = async ctx =>
        {
            // 현재 요청 Endpoint 가져오기
            var endpoint = ctx.HttpContext.GetEndpoint();

            // Endpoint가 없거나, [Authorize] 속성이 없으면 JWT 검사 건너뜀
            if (endpoint == null || endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>() == null)
            {
                // 권한 필요 없는 페이지 → 검사 건너뜀
                return;
            }

            // Authorization 헤더 읽기
            var authHeader = ctx.Request.Headers["Authorization"].ToString();

            // Authorization 헤더 자체가 없거나 비어있을 때
            if (string.IsNullOrWhiteSpace(authHeader))
            {
                ctx.NoResult();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";

                var json = JsonSerializer.Serialize(new { message = "AccessToken is missing." });
                await ctx.Response.WriteAsync(json);
                return;
            }

            // Authorization 헤더가 Bearer 방식이 아닌 경우
            if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                ctx.NoResult();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";

                var json = JsonSerializer.Serialize(new { message = "Invalid Authorization header format." });
                await ctx.Response.WriteAsync(json);
                return;
            }

            // Bearer 뒤 토큰이 비어 있을 때
            var token = authHeader.Substring("Bearer ".Length).Trim();
            if (string.IsNullOrEmpty(token))
            {
                ctx.NoResult();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";

                var json = JsonSerializer.Serialize(new { message = "AccessToken is empty." });
                await ctx.Response.WriteAsync(json);
                return;
            }
        },
        // 5-2. 토큰이 유효성 검증(TokenValidationParameters)에 통과한 후 호출
        OnTokenValidated = async ctx =>
        {
            var blacklist = ctx.HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>();               // ITokenBlacklistService DI 가져오기
            var jti = ctx.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;     // 토큰 내부의 jti(JWT ID) 값 가져오기

            // 블랙리스트에 등록된 jti라면 인증 실패 처리
            if (!string.IsNullOrEmpty(jti) && await blacklist.IsBlacklistedAsync(jti))
            {
                ctx.NoResult();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";

                var json = JsonSerializer.Serialize(new { message = "Token_blacklisted." });
                await ctx.Response.WriteAsync(json);
                return;
            }
        }
    };
});

// 6. 컨트롤러 및 Swagger 설정
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 7. DB 마이그레이션 적용 또는 DB 존재 확인
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// 8. 개발 환경일 경우 Swagger UI 활성화
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 9. 라우팅, 인증, 권한 미들웨어 적용
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// 10. JWT 검증 미들웨어 적용
app.UseMiddleware<JwtValidationMiddleware>();

// 11. 컨트롤러 엔드포인트 매핑
app.MapControllers();

// 12. 애플리케이션 실행
app.Run();

// 13. Program 클래스 부분 정의 (테스트 및 통합용)
public partial class Program { }
