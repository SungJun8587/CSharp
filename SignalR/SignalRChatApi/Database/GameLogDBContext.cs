using System.ComponentModel;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Server
{
    /// <summary>
    /// Channel에 들어갈 GameLogDBBase
    /// </summary>
    public interface GameLogDBBase
    {
        /// <summary>
        /// GameLogDBBase의 자식클래스에서 프로퍼티를 돌면서 DataTable에 row를 추가한다
        /// </summary>
        /// <param name="table"></param>
        public void AddToDatatable(ref DataTable table)
        {
            PropertyDescriptorCollection Properties = TypeDescriptor.GetProperties(this);

            if (table.Columns.Count == 0)
            {
                table.TableName = this.GetType().Name;
                foreach (PropertyDescriptor oProp in Properties)
                    table.Columns.Add(oProp.Name, Nullable.GetUnderlyingType(oProp.PropertyType) ?? oProp.PropertyType);
            }
            DataRow oRow = table.NewRow();
            foreach (PropertyDescriptor oProp in Properties)
                oRow[oProp.Name] = oProp.GetValue(this) ?? DBNull.Value;

            table.Rows.Add(oRow);
        }
    }

    public class GameLogDBContext : DbContext
    {
        public GameLogDBContext(DbContextOptions<GameLogDBContext> options) : base(options)
        {
            Log_Player_Login = Set<Log_Player_Login>();
            Log_Player_Register = Set<Log_Player_Register>();
        }

        public DbSet<Log_Player_Login> Log_Player_Login { get; set; }
        public DbSet<Log_Player_Register> Log_Player_Register { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Log_Player_Login>().HasKey(c => new { c.InsertDate, c.PlayerNo, c.No });
            modelBuilder.Entity<Log_Player_Login>().HasIndex(c => new { c.No });
            modelBuilder.Entity<Log_Player_Login>().HasIndex(c => new { c.PlayerNo });

            modelBuilder.Entity<Log_Player_Register>().HasKey(c => new { c.InsertDate, c.PlayerNo, c.No });
            modelBuilder.Entity<Log_Player_Register>().HasIndex(c => new { c.No });
            modelBuilder.Entity<Log_Player_Register>().HasIndex(c => new { c.PlayerNo });
        }
    }
}
