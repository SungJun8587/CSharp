using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SignalRChat;

namespace SignalRChatApp
{
    public class AgentManager
    {
        private IHost _host;
        private CancellationTokenSource _tokenSource;

        public AgentManager()
        {
            var builder = new HostBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHttpClient();
                }).UseConsoleLifetime();

            _host = builder.Build();

            // Action Json 로드
            TestPacketManager.Instance.LoadActionsFromJson("/ActionData/actions.json");

        }

        public async Task CreateAgentTasks(int count)
        {
            _tokenSource = new CancellationTokenSource();

            List<Agent> agents = new List<Agent>();

            Console.WriteLine("회원가입 및 커넥트");
            Console.WriteLine("시나리오테스트 시작");

            var tasks = new List<Task>();
            for (var i = 1; i < count + 1; i++)
            {
                var agent = new Agent((ulong)i, _host, _tokenSource.Token);
                agents.Add(agent);
                var task = agent.StartTimer();
                tasks.Add(task);
            }

            Task.WaitAll(tasks.ToArray());

            await Task.CompletedTask;
        }

        public void CancelAgents()
        {
            _tokenSource.Cancel();
        }
    }
}
