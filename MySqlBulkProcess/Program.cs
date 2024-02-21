using AdminTool.Models;
using Common.Lib.Bulk;
using MySqlConnector;

namespace MySqlBulkProcess
{
    class Program
    {
        public static void Main(string[] args)
        {
            QueryConfig.SetDialect(Dialect.MySql);

            TestBulkAllDelete();
            TestBulkInsert();
            TestBulkUpdate();
            TestBulkDelete();
        }

        public static void TestBulkAllDelete()
        {
            using (var db = new MySqlConnection(ConfigData.LocalDB))
            {
                db.BulkDelete("tool_adminactionlog2");
            }
        }

        public static void TestBulkInsert()
        {
            using (var db = new MySqlConnection(ConfigData.LocalDB))
            {
                /*
                var list = db.GetListByBulk<Schema_AdminLog>(new
                {
                    No = new List<uint>()
                    {
                        1,
                        2,
                        3,
                        4,
                        5,
                        6,
                        7,
                        8,
                        9,
                        10,
                        11,
                        13,
                        15
                    }
                }).ToList();
                */

                var list = db.GetListByBulk<Schema_AdminLog>(null).ToList();

                db.BulkInsert("tool_adminactionlog2", list);
            }
        }

        public static void TestBulkUpdate()
        {
            using (var db = new MySqlConnection(ConfigData.LocalDB))
            {
                var list = db.GetListByBulk<Schema_AdminLog>(new
                {
                    No = new List<uint>()
                    {
                        1,
                        3,
                        5
                    }
                }).ToList();

                list.ForEach(p =>
                {
                    p.ConfirmAdminIdx = 100;
                    p.UpdateDate = DateTime.Now;
                });

                db.BulkUpdate("tool_adminactionlog2", list, p => new { p.ConfirmAdminIdx, p.UpdateDate }, p => new { p.No });
            }
        }

        public static void TestBulkDelete()
        {
            using (var db = new MySqlConnection(ConfigData.LocalDB))
            {
                var list = db.GetListByBulk<Schema_AdminLog>(new
                {
                    No = new List<uint>()
                    {
                        1,
                        6,
                        11
                    }
                }).ToList();

                db.BulkDelete("tool_adminactionlog2", list, p => new { p.No });
            }
        }          

        /*
        public static async Task Main(string[] args)
        {
            QueryConfig.SetDialect(Dialect.MySql);

            await TestBulkAllDeleteAsync();
            await TestBulkInsertAsync();
            await TestBulkUpdateAsync();
            await TestBulkDeleteAsync();
        }
        */

        public static async Task TestBulkAllDeleteAsync()
        {
            using (var db = new MySqlConnection(ConfigData.LocalDB))
            {
                await db.BulkDeleteAsync("tool_adminactionlog2");
            }
        }

        public static async Task TestBulkInsertAsync()
        {
            using (var db = new MySqlConnection(ConfigData.LocalDB))
            {
                /*
                var list = db.GetListByBulk<Schema_AdminLog>(new
                {
                    No = new List<uint>()
                    {
                        1,
                        2,
                        3,
                        4,
                        5,
                        6,
                        7,
                        8,
                        9,
                        10,
                        11,
                        13,
                        15
                    }
                }).ToList();
                */

                var list = await db.GetListByBulk<Schema_AdminLog>(null).ToListAsync();

                await db.BulkInsertAsync("tool_adminactionlog2", list);
            }
        }

        public static async Task TestBulkUpdateAsync()
        {
            using (var db = new MySqlConnection(ConfigData.LocalDB))
            {
                var list = await db.GetListByBulk<Schema_AdminLog>(new
                {
                    No = new List<uint>()
                    {
                        1,
                        3,
                        5
                    }
                }).ToListAsync();

                list.ForEach(p =>
                {
                    p.ConfirmAdminIdx = 100;
                    p.UpdateDate = DateTime.Now;
                });

                await db.BulkUpdateAsync("tool_adminactionlog2", list, p => new { p.ConfirmAdminIdx, p.UpdateDate }, p => new { p.No });
            }
        }

        public static async Task TestBulkDeleteAsync()
        {
            using (var db = new MySqlConnection(ConfigData.LocalDB))
            {
                var list = await db.GetListByBulk<Schema_AdminLog>(new
                {
                    No = new List<uint>()
                    {
                        1,
                        6,
                        11
                    }
                }).ToListAsync();

                await db.BulkDeleteAsync("tool_adminactionlog2", list, p => new { p.No });
            }
        }   
    }
}