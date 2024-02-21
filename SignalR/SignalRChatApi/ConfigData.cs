namespace Server
{
    public static class ConfigData
    {
        public static bool IsDebug { get; set; }                            // 디버그 여부
        public static uint ServerGroupNo { get; set; }                      // 게임서버 그룹 번호
        public static uint ServerChannelNo { get; set; }                    // 게임서버 채널 번호
        public static int HashMultiple { get; set; }                        // 코어당 Hash 갯수
        public static string GlobalWriteDB { get; set; } = string.Empty;    // Global DB Master 연결 정보
        public static string GlobalReadDB { get; set; } = string.Empty;     // Global DB Slave 연결 정보
        public static string GlobalLogDB { get; set; } = string.Empty;      // GlobalLog DB 연결 정보
        public static string GameDB { get; set; } = string.Empty;           // Game DB 연결 정보
        public static string GameLogDB { get; set; } = string.Empty;        // GameLog DB 연결 정보

        public static void InitConfigure(IConfiguration configuration)
        {
            IsDebug = Convert.ToBoolean(configuration.GetSection("GlobalValues")["IsDebug"]);
            ServerGroupNo = Convert.ToUInt32(configuration.GetSection("GlobalValues")["ServerGroupNo"]);
            ServerChannelNo = Convert.ToUInt32(configuration.GetSection("GlobalValues")["ServerChannelNo"]);
            HashMultiple = Convert.ToInt32(configuration.GetSection("GlobalValues")["HashMultiple"]);
            
            GlobalWriteDB = configuration.GetConnectionString("GlobalWriteDB");
            GlobalReadDB = configuration.GetConnectionString("GlobalReadDB");
            GlobalLogDB = configuration.GetConnectionString("GlobalLogDB");
            GameDB = configuration.GetConnectionString("GameDB");
            GameLogDB = configuration.GetConnectionString("GameLogDB");
        }
    }
}
