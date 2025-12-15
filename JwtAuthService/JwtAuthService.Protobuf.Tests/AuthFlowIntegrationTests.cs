using JwtAuthService.Protobuf.Helper;
using JwtAuthService.Protobuf.Models;
using System.Net.Http.Headers;
using Xunit.Abstractions;

namespace JwtAuthService.Protobuf.Tests;

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
        string userName = "user04";
        string passWord = "Pass123!";
        string email = "user04@example.com";

        // 1. 회원가입
        var registerRequest = new RegisterRequest
        {
            Username = userName,
            Password = passWord,
            Email = email,
        };
        // Protobuf Content 생성 및 요청
        using var registerContent = ProtobufHelper.CreateProtobufContent(registerRequest);
        var registerResponse = await _client.PostAsync("/api/auth/register", registerContent);
        registerResponse.EnsureSuccessStatusCode();

        // 2. 로그인
        var loginRequest = new LoginRequest
        {
            Username = userName,
            Password = passWord
        };
        using var loginContent = ProtobufHelper.CreateProtobufContent(loginRequest);
        var loginResponse = await _client.PostAsync("/api/auth/login", loginContent);
        loginResponse.EnsureSuccessStatusCode();

        // Protobuf 응답 역직렬화
        var loginData = await ProtobufHelper.ReadProtobufAsync<LoginResponse>(loginResponse);
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

        // Protobuf 응답 역직렬화
        var userInfo = await ProtobufHelper.ReadProtobufAsync<User>(userInfoResponse);
        Assert.NotNull(userInfo);
        Assert.Equal(userName, userInfo!.Username); // null-forgiving 연산자 사용

        _output.WriteLine("");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");
        _output.WriteLine($"Id: {userInfo.UserId}");
        _output.WriteLine($"Username: {userInfo.Username}");
        _output.WriteLine($"Email: {userInfo.Email}");
        _output.WriteLine($"Role: {userInfo.Role}");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");

        // 4. 토큰 정보(보호된 API 호출 - 예: /api/user/tokeninfo)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var tokeninfoResponse = await _client.GetAsync("/api/user/tokeninfo");
        tokeninfoResponse.EnsureSuccessStatusCode();

        // Protobuf 응답 역직렬화
        var tokeninfoData = await ProtobufHelper.ReadProtobufAsync<TokenExpiryResponse>(tokeninfoResponse);

        _output.WriteLine("");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");
        // Protobuf는 DateTime이 아닌 Unix Timestamp(long/int)를 사용할 가능성이 높으므로, 필요에 따라 변환이 필요할 수 있다.
        // 현재는 예시를 위해 DateTime 속성이 있다고 가정하고 포맷을 사용.
        _output.WriteLine($"accessToken 만료 시간: {tokeninfoData?.AccessTokenExpiresAt:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine($"refreshToken 만료 시간: {tokeninfoData?.RefreshTokenExpiresAt:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine($"accessToken 만료되기까지 남은 시간(초): {tokeninfoData?.AccessTokenRemainingSeconds}");
        _output.WriteLine($"refreshToken 만료되기까지 남은 시간(초): {tokeninfoData?.RefreshTokenRemainingSeconds}");
        _output.WriteLine("-----------------------------------------------------------------------------------------------------------");

        // 5. 토큰 갱신
        var refreshRequest = new RefreshRequest
        {
            RefreshToken = refreshToken
        };
        using var refreshContent = ProtobufHelper.CreateProtobufContent(refreshRequest);
        var refreshResponse = await _client.PostAsync("/api/auth/refresh", refreshContent);
        refreshResponse.EnsureSuccessStatusCode();

        // Protobuf 응답 역직렬화
        var refreshData = await ProtobufHelper.ReadProtobufAsync<RefreshResponse>(refreshResponse);
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
        var logoutRequest = new RefreshRequest
        {
            RefreshToken = newRefreshToken
        };
        using var logoutContent = ProtobufHelper.CreateProtobufContent(logoutRequest);
        var logoutResponse = await _client.PostAsync("/api/auth/logout", logoutContent);
        logoutResponse.EnsureSuccessStatusCode();
    }
}
