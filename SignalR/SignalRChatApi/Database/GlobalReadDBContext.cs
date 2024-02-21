using Microsoft.EntityFrameworkCore;

namespace Server
{
    public class GlobalReadDBContext : DbContext
    {
        public DbSet<Account> Account { get; set; }
        public GlobalReadDBContext(DbContextOptions<GlobalReadDBContext> options) : base(options)
        {
            Account = Set<Account>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>().HasIndex(a => new { a.UserNo, a.IsDeleted }).IsUnique();
        }
    }
}
