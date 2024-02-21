using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Server
{
    public class GameDBContext : DbContext
    {
        public GameDBContext(DbContextOptions<GameDBContext> options) : base(options)
        {
            Players = Set<TPlayer>();
        }

        public DbSet<TPlayer> Players { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TPlayer>().HasIndex(a => new { a.UserNo }).IsUnique();
            modelBuilder.Entity<TPlayer>().HasIndex(a => a.Name).IsUnique();
        }
    }
}
