using JwtAuthService.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit.Abstractions;

namespace JwtAuthService.Tests;

// dotnet test --logger "console;verbosity=detailed"
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
        // 1. 회원가입
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = "testuser1",
            Password = "Password123!",
            Email = "test1@google.com",
        });
        registerResponse.EnsureSuccessStatusCode();

        // 2. 로그인
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "testuser1",
            Password = "Password123!"
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginData = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        if (loginData == null) return;

        string accessToken = loginData.AccessToken;
        string refreshToken = loginData.RefreshToken;

        Assert.False(string.IsNullOrEmpty(accessToken));
        Assert.False(string.IsNullOrEmpty(refreshToken));

        _output.WriteLine("");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");
        _output.WriteLine($"accessToken: {accessToken}");
        _output.WriteLine($"refreshToken: {refreshToken}");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");

        // 3. 유저 정보(보호된 API 호출 - 예: /api/protected/userinfo)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var protectedResponse = await _client.GetAsync("/api/protected/userinfo");
        protectedResponse.EnsureSuccessStatusCode(); // 200 OK 나와야 함

        var userInfo = await protectedResponse.Content.ReadFromJsonAsync<User>();
        Assert.NotNull(userInfo);
        Assert.Equal("testuser1", userInfo.Username);

        _output.WriteLine("");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");
        _output.WriteLine($"Id: {userInfo.Id}");
        _output.WriteLine($"Username: {userInfo.Username}");
        _output.WriteLine($"Email: {userInfo.Email}");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");

        // 4. 토큰 정보(보호된 API 호출 - 예: /api/protected/tokeninfo)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var expirationResponse = await _client.GetAsync("/api/protected/tokeninfo");
        expirationResponse.EnsureSuccessStatusCode();

        var expData = await expirationResponse.Content.ReadFromJsonAsync<TokenExpiryResponse>();

        _output.WriteLine("");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");
        _output.WriteLine($"accessToken 만료 시간: {expData?.AccessTokenExpiresAt:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine($"refreshToken 만료 시간: {expData?.RefreshTokenExpiresAt:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine($"accessToken 만료되기까지 남은 시간(초): {expData?.AccessTokenRemainingSeconds}");
        _output.WriteLine($"refreshToken 만료되기까지 남은 시간(초): {expData?.RefreshTokenRemainingSeconds}");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");

        // 5. 토큰 갱신
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest
        {
            RefreshToken = refreshToken
        });
        refreshResponse.EnsureSuccessStatusCode();

        var refreshData = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>();
        if (refreshData == null) return;

        string newAccessToken = refreshData.AccessToken;
        string newRefreshToken = refreshData.RefreshToken;

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
