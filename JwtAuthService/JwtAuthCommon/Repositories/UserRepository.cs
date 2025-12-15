using JwtAuthCommon.Data;
using JwtAuthCommon.Entities;
using Microsoft.EntityFrameworkCore;

namespace JwtAuthCommon.Repositories
{
    /// <summary>
    /// 사용자 저장소 구현
    /// </summary>
    public class UserRepository : IUserRepository
    {
        private readonly AppDbContext _db;

        public UserRepository(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// 사용자 추가 (비동기)
        /// </summary>
        public async Task AddAsync(UserEntity user)
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync(); // DB에 비동기 저장
        }

        /// <summary>
        /// 사용자 이름으로 조회
        /// </summary>
        public async Task<UserEntity?> GetByUsernameAsync(string username)
        {
            return await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        }

        /// <summary>
        /// ID로 사용자 조회
        /// </summary>
        public async Task<UserEntity?> GetByIdAsync(long id)
        {
            return await _db.Users.FindAsync(id);
        }
    }
}
