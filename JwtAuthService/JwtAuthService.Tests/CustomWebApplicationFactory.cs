using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // 로그 설정 재정의
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();                         // 기존 로거 제거
            logging.AddConsole();                             // 콘솔 로거 추가 (선택)
            logging.AddFilter(level => level >= LogLevel.Warning); // Warning 이상만 출력
        });
    }
}
