using Microsoft.Extensions.Configuration;

namespace SignalRChatApp
{
    public static class ConfigData
    {
        public static int TaskCount { get; set; }
        public static string WebServerHost { get; set; }
        public static string GameServerHost { get; set; }

        public static int DelayPerUser { get; set; }

        static ConfigData()
        {
            var builder = new ConfigurationBuilder()
                      .SetBasePath(Directory.GetCurrentDirectory())
                      .AddJsonFile("appsettings.json", optional: false);

            IConfiguration config = builder.Build();

            TaskCount = Convert.ToInt32(config.GetSection("GlobalValues")["TaskCount"]);
            DelayPerUser = Convert.ToInt32(config.GetSection("GlobalValues")["DelayPerUser"]);
            WebServerHost = config.GetSection("GlobalValues")["WebServerHost"].ToString();
            GameServerHost = config.GetSection("GlobalValues")["GameServerHost"].ToString();
        }
    }
}
