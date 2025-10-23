using JwtAuthService.Models;

namespace JwtAuthService.Repositories
{
    /// <summary>
    /// 사용자 저장소 인터페이스
    /// </summary>
    public interface IUserRepository
    {
        /// <summary>사용자 추가</summary>
        Task AddAsync(User user);

        /// <summary>사용자 이름으로 조회</summary>
        Task<User?> GetByUsernameAsync(string username);

        /// <summary>ID로 사용자 조회</summary>
        Task<User?> GetByIdAsync(long id);
    }
}
