using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace SignalRChat
{
    public class NetworkHubManager
    {
        private HubConnection _hubConnection;
        private NetworkHubEvent _hubEvent;

        public NetworkHubManager(string requestUrl)
        {
            _hubConnection = new HubConnectionBuilder()
                //.WithUrl(requestUrl)
                
                .WithUrl(requestUrl, Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets,
                options =>
                {
                    options.SkipNegotiation = true;
                    options.ApplicationMaxBufferSize = 1_000_000;
                    options.TransportMaxBufferSize = 1_000_000;
                })
                
                .AddMessagePackProtocol()
                //.AddNewtonsoftJsonProtocol()
                .Build();   

            _hubConnection.Closed += (Exception exception) => {
                if (exception == null)
                {
                    Console.WriteLine("Connection closed without error.");
                }
                else
                {
                    Console.WriteLine($"Connection closed due to an error: {exception}");
                }

                return Task.CompletedTask;
            };

            _hubEvent = new NetworkHubEvent(_hubConnection); 
        }

        public HubConnection GetHubConnection()
        {
            return _hubConnection;
        }

        public NetworkHubEvent GetHubEvent()
        {
            return _hubEvent;
        }

        public async Task StartAsync()
        {
            await _hubConnection.StartAsync();
        }

        public async Task StopAsync()
        {
            await _hubConnection.StopAsync();
        }
    }
}