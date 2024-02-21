using System.ComponentModel;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Server
{
    /// <summary>
    /// Channel에 들어갈 GlobalLogDBBase
    /// </summary>
    public interface GlobalLogDBBase
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
        
    public class GlobalLogDBContext : DbContext
    {
        public GlobalLogDBContext(DbContextOptions<GlobalLogDBContext> options) : base(options)
        {
            Log_GameHubError = Set<Log_GameHubError>();
            Log_User_Login = Set<Log_User_Login>();
            Log_User_Register = Set<Log_User_Register>();
        }

        public DbSet<Log_GameHubError> Log_GameHubError { get; set; }
        public DbSet<Log_User_Login> Log_User_Login { get; set; }
        public DbSet<Log_User_Register> Log_User_Register { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Log_GameHubError>().HasIndex(c => new { c.PlayerNo });

            modelBuilder.Entity<Log_User_Login>().HasKey(c => new { c.InsertDate, c.UserNo, c.No });
            modelBuilder.Entity<Log_User_Login>().HasIndex(c => new { c.No });
            modelBuilder.Entity<Log_User_Login>().HasIndex(c => new { c.UserNo });

            modelBuilder.Entity<Log_User_Register>().HasKey(c => new { c.InsertDate, c.UserNo, c.No });
            modelBuilder.Entity<Log_User_Register>().HasIndex(c => new { c.No });
            modelBuilder.Entity<Log_User_Register>().HasIndex(c => new { c.UserNo });
        }
    }
}
