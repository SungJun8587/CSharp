using JwtAuthService.Models;

namespace JwtAuthService.Services
{
    public interface IJwtService
    {
        string GenerateAccessToken(User user);
        Task<(string accessToken, string refreshToken)> GenerateTokensAsync(User user, string? deviceId = null);
        Task<(string? accessToken, string? refreshToken, string? error)> RotateRefreshTokenAsync(string oldRefreshToken, string? deviceId = null);
        Task InvalidateAccessTokenAsync(string accessToken);
        DateTime GetExpirationFromToken(string accessToken);
        Task<bool> IsAccessTokenBlacklistedAsync(string accessToken);
    }
}
