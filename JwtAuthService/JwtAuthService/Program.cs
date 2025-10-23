using JwtAuthService.Data;
using JwtAuthService.Repositories;
using JwtAuthService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using JwtAuthService.Middleware;
using StackExchange.Redis;
using System.Text;

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
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = validIssuer,
        ValidateAudience = true,
        ValidAudience = validAudience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateLifetime = true
    };

    // 5. 토큰 검증 후 블랙리스트 체크
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            var blacklist = ctx.HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>();
            var jti = ctx.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;

            // 5.1. 블랙리스트에 존재하면 인증 실패 처리
            if (!string.IsNullOrEmpty(jti) && await blacklist.IsBlacklistedAsync(jti))
            {
                ctx.Fail("token_blacklisted");
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
