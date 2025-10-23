using JwtAuthService.Models;

namespace JwtAuthService.Services
{
    public interface IAuthService
    {
        Task<object?> LoginAsync(string username, string password, string? deviceId = null);
        Task<object?> RefreshAsync(string refreshToken, string? deviceId = null);
        Task LogoutAsync(string? refreshToken, string? deviceId = null);
    }
}
