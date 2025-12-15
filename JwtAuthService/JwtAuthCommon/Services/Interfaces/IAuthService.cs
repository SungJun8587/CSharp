namespace JwtAuthCommon.Services
{
    public interface IAuthService
    {
        Task<(string? accessToken, string? refreshToken, int expiresIn)> LoginAsync(string username, string password, string? deviceId = null);
        Task<(string? accessToken, string? refreshToken)> RefreshAsync(string refreshToken, string? deviceId = null);
        Task LogoutAsync(string? refreshToken, string? deviceId = null);
    }
}
