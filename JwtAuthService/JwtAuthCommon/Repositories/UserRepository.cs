using JwtAuthCommon.Data;
using JwtAuthCommon.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace JwtAuthCommon.Repositories
{
    /// <summary>
    /// 사용자(User) 엔티티에 대한 데이터 접근을 담당하는 저장소 클래스
    /// </summary>
    public class UserRepository : IUserRepository
    {
        private readonly AppDbContext _db;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="db">사용자 테이블에 접근하기 위한 EF Core 데이터베이스 컨텍스트</param>
        public UserRepository(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// 모든 사용자 목록을 조회.
        /// </summary>
        /// <returns>모든 UserEntity 목록</returns>
        public async Task<List<UserEntity>> GetAllAsync()
        {
            return await _db.Users.ToListAsync();
        }

        /// <summary>
        /// 관리자(Admin) 권한을 가진 사용자 목록을 조회.
        /// </summary>
        /// <returns>관리자 권한을 가진 UserEntity 목록</returns>
        public async Task<List<UserEntity>> GetAdminsAsync()
        {
            return await _db.Users
                .Where(p => p.Role == "Admin" && p.IsActive)
                .Select(p => new UserEntity
                {
                    Id = p.Id,
                    Username = p.Username,
                    Email = p.Email,
                    IsActive = p.IsActive,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();
        }

        /// <summary>
        /// 일반 사용자(User) 권한을 가진 사용자 목록을 조회.
        /// </summary>
        /// <returns>일반 사용자 권한을 가진 UserEntity 목록</returns>
        public async Task<List<UserEntity>> GetUsersAsync()
        {
            return await _db.Users
                .Where(p => p.Role == "User" && p.IsActive)
                .Select(p => new UserEntity
                {
                    Id = p.Id,
                    Username = p.Username,
                    Email = p.Email,
                    IsActive = p.IsActive,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();
        }

        /// <summary>
        /// 사용자 이름을 기준으로 사용자를 조회.
        /// </summary>
        /// <param name="username">조회할 사용자 이름</param>
        /// <returns>해당 사용자 정보가 존재하면 UserEntity 객체, 존재하지 않으면 null</returns>
        public async Task<UserEntity?> GetByUsernameAsync(string username)
        {
            return await _db.Users.Where(p => p.IsActive).FirstOrDefaultAsync(u => u.Username == username);
        }

        /// <summary>
        /// 사용자 ID를 기준으로 사용자를 조회.
        /// </summary>
        /// <param name="id">사용자 고유 식별자(ID)</param>
        /// <returns>해당 사용자 정보가 존재하면 UserEntity 객체, 존재하지 않으면 null</returns>
        public async Task<UserEntity?> GetByIdAsync(long id)
        {
            return await _db.Users.FindAsync(id);
        }

        /// <summary>
        /// 새로운 사용자를 데이터베이스에 추가.
        /// </summary>
        /// <param name="user">추가할 사용자 엔티티</param>
        public async Task AddAsync(UserEntity user)
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// 사용자를 삭제 처리.
        /// </summary>
        /// <param name="id">사용자 고유 식별자(ID)</param>
        public async Task DeleteUserAsync(long id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
                throw new Exception("User not found");

            user.IsActive = false;
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// 사용자 활성화 처리.
        /// </summary>
        /// <param name="id">사용자 고유 식별자(ID)</param>
        public async Task ActiveUserAsync(long id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
                throw new Exception("User not found");

            user.IsActive = true;
            await _db.SaveChangesAsync();
        }
    }
}
