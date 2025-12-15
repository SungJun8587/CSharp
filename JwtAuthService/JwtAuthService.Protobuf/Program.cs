using Google.Protobuf;
using JwtAuthCommon.Data;
using JwtAuthCommon.Repositories;
using JwtAuthCommon.Services;
using JwtAuthService.Protobuf.Middleware;
using JwtAuthService.Protobuf.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;


var wReq = new LoginRequest
{
    Username = "user01",
    Password = "Pass123!",
    DeviceId = "mobile"
};

using var wStream = File.Create("login_request.bin");
wReq.WriteTo(wStream);
wStream.Close();

using var rStream = File.OpenRead("login_request.bin");
var rReq = LoginRequest.Parser.ParseFrom(rStream);
rStream.Close();

Console.WriteLine($"Username: {rReq.Username}");
Console.WriteLine($"Password: {rReq.Password}");
Console.WriteLine($"DeviceId: {rReq.DeviceId}");

var builder = WebApplication.CreateBuilder(args);

// 1. 테스트 환경이면 로그 레벨 제한
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddFilter(level => level >= LogLevel.Warning); // Warning 이상만
}

var configuration = builder.Configuration;

// --- 서비스 등록 ---

// Kestrel 서버 옵션 설정: 동기 I/O 작업을 허용하도록 설정합니다.
builder.Services.Configure<KestrelServerOptions>(options =>
{
    // WARNING: 프로덕션 환경에서는 사용하지 않는 것이 좋습니다.
    options.AllowSynchronousIO = true;
});

// IIS In-Process 호스팅을 사용하는 경우에도 동기 I/O를 허용합니다.
builder.Services.Configure<IISServerOptions>(options =>
{
    options.AllowSynchronousIO = true;
});

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

            var authHeader = ctx.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                ctx.NoResult();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/x-protobuf";

                // ErrorResponse 객체 생성
                var error = new ErrorResponse { Message = "AccessToken is missing or invalid." };

                // 1. MemoryStream에 직렬화
                // Google.Protobuf 메시지 클래스의 WriteTo(Stream) 메서드 사용
                using var ms = new MemoryStream();
                error.WriteTo(ms); // ErrorResponse 객체를 MemoryStream에 직렬화

                // 2. 스트림 위치를 처음으로 이동
                ms.Position = 0;

                // 3. 비동기 복사
                // (ms의 내용을 HTTP 응답 Body로 비동기 복사)
                await ms.CopyToAsync(ctx.Response.Body);
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
                ctx.Response.ContentType = "application/x-protobuf";

                // ErrorResponse 객체 생성
                var error = new ErrorResponse { Message = "Token_blacklisted." };

                // 1. MemoryStream에 직렬화
                // Google.Protobuf 메시지 클래스의 WriteTo(Stream) 메서드 사용
                using var ms = new MemoryStream();
                error.WriteTo(ms); // ErrorResponse 객체를 MemoryStream에 직렬화

                // 2. 스트림 위치를 처음으로 이동
                ms.Position = 0;

                // 3. 비동기 복사
                // (ms의 내용을 HTTP 응답 Body로 비동기 복사)
                await ms.CopyToAsync(ctx.Response.Body);
                return;
            }
        }
    };
});

// 6. 컨트롤러 및 Protobuf 포맷터 추가, Swagger 설정
builder.Services.AddControllers(options =>
{
    // Protobuf Input/Output 포맷터를 등록하여 Protobuf 통신을 활성화합니다.
    // JSON 포맷터보다 우선 순위를 높게 설정합니다.
    options.InputFormatters.Insert(0, new ProtobufInputFormatter());
    options.OutputFormatters.Insert(0, new ProtobufOutputFormatter());
});

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
