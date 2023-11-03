using DotNetDIDemo.Service;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// IOC(Inversion of Control) : DI(Dependency Injection) Container라고도 하며 components의 자동 Dependency Injection을 제공하는 프로그래밍 Framework
//  - Singleton : 서비스의 인스턴스 오직 하나 생성(응용 프로그램이 살아있는 동안 서비스가 생성 후 유지)
//  - Transient : 서비스가 요청될 때마다 생성(의존성이 요청될 때마다 매번 서비스가 생성)
//  - Scoped : 각 클라이언트 요청에 대한 서비스 인스턴스를 생성
//builder.Services.AddTransient<IBlogRepository, BlogRepository>();
builder.Services.AddTransient<IBlogRepository, NewBlogRepository>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // http://localhost:5000/swagger/index.html
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run("http://*:5000");

