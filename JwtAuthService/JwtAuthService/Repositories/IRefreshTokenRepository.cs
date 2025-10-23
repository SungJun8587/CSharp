using JwtAuthService.Models;

namespace JwtAuthService.Repositories
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByTokenAsync(string token);
        Task AddAsync(RefreshToken refreshToken);
        Task InvalidateAsync(RefreshToken token, string? replacedByToken = null);
        Task<IEnumerable<RefreshToken>> GetAllValidTokensForUserAsync(long userId);
        Task<IEnumerable<RefreshToken>> GetActiveTokensForUserDeviceAsync(long userId, string deviceId);
    }
}
