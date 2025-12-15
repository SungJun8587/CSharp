using JwtAuthService.Json.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit.Abstractions;

namespace JwtAuthService.Json.Tests;

// 서버 실행 : dotnet run --launch-profile http
// 클라이언트 실행 : dotnet test --logger "console;verbosity=detailed"
//      dotnet test                       : .NET 프로젝트 내의 단위 테스트를 빌드하고 실행하는 기본 명령어.
//      --logger 또는 -l                  : 테스트 결과를 기록(로깅)하는 데 사용할 테스트 결과 로거를 지정.
//      "console;verbosity=detailed"	  : logger 옵션의 인수.
//          - console                     : 사용할 로거가 콘솔 로거임을 지정. 즉, 테스트 결과를 파일 대신 명령 프롬프트/터미널에 출력.
//          - ;verbosity=detailed         : 콘솔 로거에 전달되는 매개변수. 출력의 상세 정보 수준을 detailed로 설정.
// 테스트 디버깅
//      - 상단 메뉴 → 테스트(S) → 모든 테스트 디버깅(D) → 테스트 탐색기 열기
//
public class AuthFlowIntegrationTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output; // xUnit 출력용  

    public AuthFlowIntegrationTests(CustomWebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _client = factory.CreateClient();
        _output = output;
    }

    [Fact(DisplayName = "회원가입 / 로그인 / 유저 정보 조회 / 토큰 정보 조회 / 토큰 갱신 / 로그아웃")]
    public async Task AuthWorkflowWithProtectedApiTest()
    {
        string userName = "user03";
        string passWord = "Pass123!";
        string email = "user03@example.com";

        // 1. 회원가입
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = userName,
            Password = passWord,
            Email = email,
        });
        registerResponse.EnsureSuccessStatusCode();

        // 2. 로그인
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            UserName = userName,
            Password = passWord
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginData = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginData == null || !loginData.Success) return;

        string accessToken = loginData.Token.AccessToken;
        string refreshToken = loginData.Token.RefreshToken;

        Assert.False(string.IsNullOrEmpty(accessToken));
        Assert.False(string.IsNullOrEmpty(refreshToken));

        _output.WriteLine("");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");
        _output.WriteLine($"accessToken: {accessToken}");
        _output.WriteLine($"refreshToken: {refreshToken}");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");

        // 3. 유저 정보(보호된 API 호출 - 예: /api/user/userinfo)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var userInfoResponse = await _client.GetAsync("/api/user/userinfo");
        userInfoResponse.EnsureSuccessStatusCode(); // 200 OK 나와야 함

        var userInfo = await userInfoResponse.Content.ReadFromJsonAsync<User>();
        Assert.NotNull(userInfo);
        Assert.Equal(userName, userInfo.UserName);

        _output.WriteLine("");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");
        _output.WriteLine($"Id: {userInfo.UserId}");
        _output.WriteLine($"Username: {userInfo.UserName}");
        _output.WriteLine($"Email: {userInfo.Email}");
        _output.WriteLine($"Role: {userInfo.Role}");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");

        // 4. 토큰 정보(보호된 API 호출 - 예: /api/user/tokeninfo)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var tokeninfoResponse = await _client.GetAsync("/api/user/tokeninfo");
        tokeninfoResponse.EnsureSuccessStatusCode();

        var tokeninfoData = await tokeninfoResponse.Content.ReadFromJsonAsync<TokenExpiryResponse>();

        _output.WriteLine("");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");
        _output.WriteLine($"accessToken 만료 시간: {tokeninfoData?.AccessTokenExpiresAt:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine($"refreshToken 만료 시간: {tokeninfoData?.RefreshTokenExpiresAt:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine($"accessToken 만료되기까지 남은 시간(초): {tokeninfoData?.AccessTokenRemainingSeconds}");
        _output.WriteLine($"refreshToken 만료되기까지 남은 시간(초): {tokeninfoData?.RefreshTokenRemainingSeconds}");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");

        // 5. 토큰 갱신
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest
        {
            RefreshToken = refreshToken
        });
        refreshResponse.EnsureSuccessStatusCode();

        var refreshData = await refreshResponse.Content.ReadFromJsonAsync<RefreshResponse>();
        if (refreshData == null || !refreshData.Success) return;

        string newAccessToken = refreshData.Token.AccessToken;
        string newRefreshToken = refreshData.Token.RefreshToken;

        Assert.False(string.IsNullOrEmpty(newAccessToken));
        Assert.False(string.IsNullOrEmpty(newRefreshToken));

        _output.WriteLine("");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");
        _output.WriteLine($"newAccessToken: {newAccessToken}");
        _output.WriteLine($"newRefreshToken: {newRefreshToken}");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");

        // 6. 로그아웃
        var logoutResponse = await _client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest
        {
            RefreshToken = newRefreshToken
        });
        logoutResponse.EnsureSuccessStatusCode();
    }
}
