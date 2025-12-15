using JwtAuthCommon.Entities;

namespace JwtAuthCommon.Services
{
    public interface IJwtService
    {
        string GenerateAccessToken(UserEntity user);
        Task<(string accessToken, string refreshToken)> GenerateTokensAsync(UserEntity user, string? deviceId = null);
        Task<(string? accessToken, string? refreshToken, string? error)> RotateRefreshTokenAsync(string oldRefreshToken, string? deviceId = null);
        Task InvalidateAccessTokenAsync(string accessToken);
        DateTime GetExpirationFromToken(string accessToken);
        Task<bool> IsAccessTokenBlacklistedAsync(string accessToken);
    }
}
