using JwtAuthCommon.Entities;

namespace JwtAuthCommon.Repositories
{
    /// <summary>
    /// 사용자 저장소 인터페이스
    /// </summary>
    public interface IUserRepository
    {
        /// <summary>모든 사용자 목록을 조회</summary>
        Task<List<UserEntity>> GetAllAsync();

        /// <summary>관리자(Admin) 권한을 가진 사용자 목록을 조회</summary>
        Task<List<UserEntity>> GetAdminsAsync();

        /// <summary>일반 사용자(User) 권한을 가진 사용자 목록을 조회</summary>
        Task<List<UserEntity>> GetUsersAsync();

        /// <summary>사용자 이름으로 조회</summary>
        Task<UserEntity?> GetByUsernameAsync(string username);

        /// <summary>사용자 ID로 조회</summary>
        Task<UserEntity?> GetByIdAsync(long id);

        /// <summary>사용자 추가</summary>
        Task AddAsync(UserEntity user);

        /// <summary>사용자 삭제 처리</summary>
        Task DeleteUserAsync(long id);

        /// <summary>사용자 활성화 처리</summary>
        Task ActiveUserAsync(long id);
    }
}
