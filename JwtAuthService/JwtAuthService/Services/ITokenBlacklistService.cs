namespace JwtAuthService.Services
{
    public interface ITokenBlacklistService
    {
        Task AddToBlacklistAsync(string jti, TimeSpan expiry);
        Task<bool> IsBlacklistedAsync(string jti);
    }
}
