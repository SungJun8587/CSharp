using Microsoft.Extensions.Configuration;

namespace MySqlBulkProcess
{
    public static class ConfigData
    {
        public static string LocalDB { get; set; }

        static ConfigData()
        {
            var builder = new ConfigurationBuilder()
                      .SetBasePath(Directory.GetCurrentDirectory())
                      .AddJsonFile("appsettings.json", optional: false);

            IConfiguration config = builder.Build();

            LocalDB = config.GetConnectionString("LocalDB");
        }
    }
}
