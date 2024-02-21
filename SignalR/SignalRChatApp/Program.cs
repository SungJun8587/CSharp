using Protocol;
using SignalRChat;

namespace SignalRChatApp
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("TaskCount : " + ConfigData.TaskCount);
            Console.WriteLine("GameServerHost : " + ConfigData.GameServerHost);

            AgentManager agents = new AgentManager();
            await agents.CreateAgentTasks(ConfigData.TaskCount);
            //Task.WaitAll(tasks.ToArray());
            await Task.CompletedTask;
            

            /*
            WebAPI webAPI = new WebAPI(ConfigData.WebServerHost);
            string deviceID = Guid.NewGuid().ToString();

            AckGlobalJoin ackGlobalJoin = await WebAPIJoin(webAPI, deviceID);
            AckGlobalLogin ackGlobalLogin = await WebAPILogin(webAPI, ackGlobalJoin.FpID, deviceID);

            GameHub gameHub = new GameHub(ConfigData.GameServerHost);

            await gameHub.StartAsync();

            AckLogin ackLogin = await GameLogin(gameHub, ackGlobalJoin.FpID);

            await gameHub.StopAsync();
            */
        }

        public async static Task<AckGlobalJoin> WebAPIJoin(WebAPI webAPI, string deviceID)
        {
            ReqGlobalJoin reqGlobalJoin = new ReqGlobalJoin() {
                Id = "",
                DeviceID = Guid.NewGuid().ToString()
            };
            AckGlobalJoin ackGlobalJoin = await webAPI.GlobalJoin(reqGlobalJoin);

            return ackGlobalJoin;
        }

        public async static Task<AckGlobalLogin> WebAPILogin(WebAPI webAPI, string fpID, string DeviceID)
        {
            ReqGlobalLogin reqGlobalJoin = new ReqGlobalLogin() {
                FpID = fpID,
                DeviceID = DeviceID
            };
            AckGlobalLogin ackGlobalLogin = await webAPI.GlobalLogin(reqGlobalJoin);

            return ackGlobalLogin;
        }

        public async static Task<AckLogin> GameLogin(GameHub gameHub, string fpID)
        {
            AckLogin ackLogin = await gameHub.GameReqLogin(fpID);
            return ackLogin;
        }        
    }
}