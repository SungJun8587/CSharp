using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Server;

var builder = WebApplication.CreateBuilder(args);

ConfigData.InitConfigure(builder.Configuration);

// Add services to the container.

// WebAPI
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiTaskFilter>();
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Cors정책
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy",
        builder => builder
        .AllowAnyMethod()
        .AllowAnyHeader()
        .WithOrigins("http://localhost:57638")
    );
});

// SignalR
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(20);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(80);

    options.AddFilter<PacketTaskFilter>();
}).AddMessagePackProtocol();

// HubTask(허브에서 호출할 채널)
builder.Services.AddSingleton<SgTask>();

// HashTask(해쉬별 하나의 컨슈머로 시리얼라이즈)
//builder.Services.AddSingleton<SgHashTask>();

// HashTimerTask(해쉬별 하나의 컨슈머로 타이머 포함된 시리얼라이즈)
//builder.Services.AddSingleton<SgHashTimerTask>();

builder.Services.AddDbContextPool<GlobalWriteDBContext>(options => options
    .UseMySql(ConfigData.GlobalWriteDB, ServerVersion.AutoDetect(ConfigData.GlobalWriteDB))
    .EnableThreadSafetyChecks(false)
);

builder.Services.AddDbContextPool<GlobalReadDBContext>(options => options
    .UseMySql(ConfigData.GlobalReadDB, ServerVersion.AutoDetect(ConfigData.GlobalReadDB))
    .EnableThreadSafetyChecks(false)
    //.LogTo(Console.WriteLine)  // 쿼리 로그 남기기(Console에)
);

builder.Services.AddDbContextPool<GameDBContext>(options => options
    .UseMySql(ConfigData.GameDB, ServerVersion.AutoDetect(ConfigData.GameDB))
    .EnableThreadSafetyChecks(false)
    //.LogTo(Console.WriteLine)  // 쿼리 로그 남기기(Console에)
);

// context factory를 정의하는 이유는 한 요청에서 여러번 dbContext를 생성해서 사용할일이 있다(Guild cas로 인해서)
builder.Services.AddDbContextFactory<GameDBContext>(options => options
    .UseMySql(ConfigData.GameDB, ServerVersion.AutoDetect(ConfigData.GameDB))
    //.LogTo(Console.WriteLine) // 쿼리 로그 남기기(Console에)
);

// DB 로그 백그라운드
builder.Services.AddSingleton<ILoggerService, LoggerService>();
builder.Services.AddHostedService(sp => sp.GetService<ILoggerService>() as LoggerService);

SgChatting.I.Init();

var app = builder.Build();

if (builder.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseRouting();

// CORS(UseEndpoints 전에 설정한다)
app.UseCors("CorsPolicy");

// Root
app.MapGet("/", async context =>
{
    await context.Response.WriteAsync("Hello World! Ver1");
});

// SignalR Hub
app.MapHub<Server.GameHub>("/GameHub");

app.MapControllers();

app.Run("http://*:5000");
