using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Protocol;

namespace SignalRChat
{
    public partial class GameHub
    {
        private readonly NetworkHubManager _networkHubManager;

        private HubConnection _hubConnection { get { return _networkHubManager.GetHubConnection(); } }
        private NetworkHubEvent _hubEvent { get { return _networkHubManager.GetHubEvent(); } }

        public HubConnection GetHubConnection()
        {
            return _hubConnection;
        }

        public NetworkHubEvent GetNetworkHubEvent()
        {
            return _hubEvent;
        }

        public GameHub(EEnvironments environments)
        {
            string host = string.Empty;
            string requestUrl = string.Empty;

            switch (environments)
            {
                case EEnvironments.DL_Local:
                    host = "http://127.0.0.1:5000";
                    break;
            }

            requestUrl = host + "/GameHub";

            _networkHubManager = new NetworkHubManager(requestUrl);
        }

        public GameHub(string host)
        {
            string requestUrl = string.Empty;

            requestUrl = host + "/GameHub";

            _networkHubManager = new NetworkHubManager(requestUrl);
        }

        public async Task StartAsync()
        {
            await _networkHubManager.StartAsync();
        }

        public async Task StopAsync()
        {
            await _networkHubManager.StopAsync();
        }

        public async Task<T> InvokeAsyncJson<T>(string jsonProtocal)
        {
            JObject jsonObject = JObject.Parse(jsonProtocal);
            JProperty property = (JProperty)jsonObject.First;

            string command = property.Name;
            object packet = null;

            if (jsonObject[command].Type == JTokenType.String)
            {
                packet = jsonObject[command].ToString();
            }
            else
            {
                packet = JsonConvert.DeserializeObject(jsonObject[command].ToString());
            }

            return await _hubConnection.InvokeAsync<T>(command, packet);
        }
    }
}