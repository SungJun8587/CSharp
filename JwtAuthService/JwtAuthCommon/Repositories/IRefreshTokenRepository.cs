using JwtAuthCommon.Entities;

namespace JwtAuthCommon.Repositories
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshTokenEntity?> GetByTokenAsync(string token);
        Task AddAsync(RefreshTokenEntity refreshToken);
        Task InvalidateAsync(RefreshTokenEntity token, string? replacedByToken = null);
        Task<IEnumerable<RefreshTokenEntity>> GetAllValidTokensForUserAsync(long userId);
        Task<IEnumerable<RefreshTokenEntity>> GetActiveTokensForUserDeviceAsync(long userId, string deviceId);
    }
}
