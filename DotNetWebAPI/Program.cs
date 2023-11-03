using Common.Filter;
using Common.Middleware;
using DotNetWebAPI.Model;
using DotNetWebAPI.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

IConfiguration configuration = builder.Configuration;

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Options
builder.Services.AddOptions<HttpLoggerOption>().Bind(configuration.GetSection("HttpLogger")).ValidateDataAnnotations();

// 콘솔 로그는 Information 로그 레벨만 출력
// HTTP 로그는 Critical 로그 레벨로 제한하여 로그 파일에 HTTP 로그만 기록되게 제한
var logger = new LoggerConfiguration()
    //.ReadFrom.Configuration(configuration)
    //.Enrich.FromLogContext()
    .WriteTo.Logger(
        p => p.Filter.ByIncludingOnly(e => e.Level == Serilog.Events.LogEventLevel.Information)
        .WriteTo.Console(
            restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message}{NewLine}{Exception}"
        )
    )
    .WriteTo.Logger(
        p => p.Filter.ByIncludingOnly(e => e.Level == Serilog.Events.LogEventLevel.Fatal)
        .WriteTo.File("Logs/webapi-.log",
            restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Fatal,
            outputTemplate: "{Message:lj}{NewLine}",
            rollingInterval: RollingInterval.Day)
    )
    .CreateLogger();


// https://www.c-sharpcorner.com/article/how-to-implement-serilog-in-asp-net-core-web-api/
// 로그 레벨
//  - Verbose/Trace
//  - Debug
//  - Information
//  - Warning
//  - Error
//  - Fatal/Critical

// ReadFrom.Configuration(builder.Configuration) 
//  - 이 메소드는 애플리케이션의 구성을 기반으로 로거를 구성합니다. 
//  - 이 메소드는 builder.Configuration 객체를 사용하여 구성 설정(appsettings.json)을 읽고 이를 로거에 적용합니다.
// 또는
// ReadFrom.Configuration(new ConfigurationBuilder().AddJsonFile("seri-log.config.json").Build())    
//  - "seri-log.config.json"이라는 JSON 파일에서 읽은 다음 해당 구성 개체를 ReadFrom.Configuration에 전달

// Enrich.FromLogContext() 
//  - 이 메서드는 로그 이벤트에 상황별 정보를 추가합니다. 
//  - 이를 통해 현재 메소드의 이름이나 이벤트를 시작한 사용자와 같은 추가 정보로 로그 이벤트를 강화할 수 있습니다.

// CreateLogger() 
//  - 이 메소드는 Serilog 로거를 생성하는 데 사용됩니다.

builder.Logging.ClearProviders();       // builder에서 모든 logger provider들을 제거
builder.Logging.AddSerilog(logger);     // 이 메소드는 .NET 애플리케이션의 로깅 파이프라인에 Serilog 로거를 추가

// IOC(Inversion of Control) : DI(Dependency Injection) Container라고도 하며 components의 자동 Dependency Injection을 제공하는 프로그래밍 Framework
//  - Singleton : 서비스의 인스턴스 오직 하나 생성(응용 프로그램이 살아있는 동안 서비스가 생성 후 유지)
//  - Transient : 서비스가 요청될 때마다 생성(의존성이 요청될 때마다 매번 서비스가 생성)
//  - Scoped : 각 클라이언트 요청에 대한 서비스 인스턴스를 생성
builder.Services.AddSingleton<IHttpLogger, HttpLogger>();
builder.Services.AddScoped<IHttpLogModelCreator, HttpLogModelCreator>();

// 로그 필터(Filter) 등록
builder.Services.AddMvc(options =>
{
    options.Filters.Add(new HttpLoggerActionFilter());
    options.Filters.Add(new HttpLoggerErrorFilter());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // http://localhost:5000/swagger/index.html
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 로그 미들웨어(Middleware) 등록
app.UseMiddleware<LogMiddleware>();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run("http://*:5000");
