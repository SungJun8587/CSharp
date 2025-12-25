using JwtAuthCommon.Entities;
using Microsoft.EntityFrameworkCore;

namespace JwtAuthCommon.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        /// <summary>사용자 테이블</summary>
        public DbSet<UserEntity> Users { get; set; } = null!;

        /// <summary>Refresh Token 테이블</summary>
        public DbSet<RefreshTokenEntity> RefreshTokens { get; set; } = null!;

        /// <summary>블랙리스트 Access Token 테이블</summary>
        public DbSet<BlacklistedAccessTokenEntity> BlacklistedAccessTokens { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureUsers(modelBuilder);
            ConfigureRefreshTokens(modelBuilder);
            ConfigureBlacklistedAccessTokens(modelBuilder);
        }

        private static void ConfigureUsers(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserEntity>(entity =>
            {
                entity.ToTable("Users");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id)
                      .ValueGeneratedOnAdd();

                entity.Property(e => e.Username)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.HasIndex(e => e.Username)
                      .IsUnique()
                      .HasDatabaseName("uq_Users_UserName");

                entity.Property(e => e.Password_Hash)
                      .HasMaxLength(255)
                      .IsRequired();

                entity.Property(e => e.Email)
                      .HasMaxLength(255);

                entity.Property(e => e.Role)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(e => e.IsActive)
                      .HasColumnType("bit(1)")
                      .IsRequired();

                entity.Property(e => e.CreatedAt)
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.LastLoginAt);

                entity.Property(e => e.IsActiveChangedAt);
            });
        }

        private static void ConfigureRefreshTokens(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RefreshTokenEntity>(entity =>
            {
                entity.ToTable("RefreshTokens");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Token)
                      .HasMaxLength(512)
                      .IsRequired();

                entity.Property(e => e.DeviceId)
                      .HasMaxLength(256);

                entity.Property(e => e.ReplacedByToken)
                      .HasMaxLength(512);

                entity.Property(e => e.CreatedAt)
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasIndex(e => e.UserId)
                      .HasDatabaseName("IX_RefreshTokens_UserId");

                entity.HasIndex(e => new { e.UserId, e.DeviceId })
                      .HasDatabaseName("IX_RefreshTokens_UserId_DeviceId");
            });
        }

        private static void ConfigureBlacklistedAccessTokens(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BlacklistedAccessTokenEntity>(entity =>
            {
                entity.ToTable("BlacklistedAccessTokens");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Jti)
                      .HasMaxLength(255)
                      .IsRequired();

                entity.HasIndex(e => e.Jti)
                      .IsUnique()
                      .HasDatabaseName("UQ_BlacklistedAccessTokens_Jti");

                entity.Property(e => e.CreatedAt)
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");
            });
        }
    }
}