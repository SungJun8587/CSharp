using Microsoft.EntityFrameworkCore;

namespace Server
{
    public class GlobalWriteDBContext : DbContext
    {
        public DbSet<Account> Account { get; set; }
        public GlobalWriteDBContext(DbContextOptions<GlobalWriteDBContext> options) : base(options)
        {
            Account = Set<Account>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>().HasIndex(a => new { a.UserNo, a.IsDeleted }).IsUnique();
        }
    }
}
