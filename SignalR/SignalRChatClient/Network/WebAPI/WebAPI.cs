using Common.Lib;
using Protocol;

namespace SignalRChat
{
    public partial class WebAPI
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        private static string _requestHost = string.Empty;

        public WebAPI(string requestHost)
        {
            _requestHost = requestHost;
        }

        public WebAPI(EEnvironments environments)
        {
            switch (environments)
            {
                case EEnvironments.DL_Local:
                    _requestHost = "http://127.0.0.1:5000";
                    break;
            }
        }

        public async Task<AckGlobalJoin> GlobalJoin(ReqGlobalJoin reqGlobalJoin)
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                HttpClientHelper<AckGlobalJoin> httpClientHelper = new HttpClientHelper<AckGlobalJoin>(_httpClient);

                string requestUri = _requestHost + "/User/" + EAction.GlobalJoin.ToString();
                Dictionary<string, string> fields = new Dictionary<string, string>();
                fields.Add("Id", reqGlobalJoin.Id);
                fields.Add("DeviceID", reqGlobalJoin.DeviceID);

                AckGlobalJoin ackGlobalJoin = await httpClientHelper.PostRequestJson(requestUri, fields, tokenSource.Token);
                return ackGlobalJoin;
            }
            catch (TaskCanceledException ex)
            {
                // timeout
                Console.WriteLine("TaskCanceledException:" + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception:" + ex.Message);
            }

            tokenSource.Dispose();
            return null;
        }

        public async Task<AckGlobalLogin> GlobalLogin(ReqGlobalLogin reqGlobalLogin)
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                HttpClientHelper<AckGlobalLogin> httpClientHelper = new HttpClientHelper<AckGlobalLogin>(_httpClient);

                string requestUri = _requestHost + "/User/" + EAction.GlobalLogin.ToString();
                Dictionary<string, string> fields = new Dictionary<string, string>();
                fields.Add("FpID", reqGlobalLogin.FpID);
                fields.Add("DeviceID", reqGlobalLogin.DeviceID);

                AckGlobalLogin ackGlobalLogin = await httpClientHelper.PostRequestJson(requestUri, fields, tokenSource.Token);
                return ackGlobalLogin;
            }
            catch (TaskCanceledException ex)
            {
                // timeout
                Console.WriteLine("TaskCanceledException:" + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception:" + ex.Message);
            }

            tokenSource.Dispose();
            return null;
        }

        public async Task<Tuple<int, int>> InitSessionCount()
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                HttpClientHelper<Tuple<int, int>> httpClientHelper = new HttpClientHelper<Tuple<int, int>>(_httpClient);

                string requestUri = _requestHost + "/Op/" + EAction.InitSessionCount.ToString();
                Tuple<int, int> ack = await httpClientHelper.GetSingleItemRequest(requestUri, null);
                return ack;
            }
            catch (TaskCanceledException ex)
            {
                // timeout
                Console.WriteLine("TaskCanceledException:" + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception:" + ex.Message);
            }

            tokenSource.Dispose();
            return null;
        }

        public async Task<int> GetSessionCount()
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                HttpClientHelper<int> httpClientHelper = new HttpClientHelper<int>(_httpClient);

                string requestUri = _requestHost + "/Op/" + EAction.GetSessionCount.ToString();
                int ack = await httpClientHelper.GetSingleItemRequest(requestUri, null);
                return ack;
            }
            catch (TaskCanceledException ex)
            {
                // timeout
                Console.WriteLine("TaskCanceledException:" + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception:" + ex.Message);
            }

            tokenSource.Dispose();
            return -1;
        }

        public async Task<int> GetConnectionCount()
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                HttpClientHelper<int> httpClientHelper = new HttpClientHelper<int>(_httpClient);

                string requestUri = _requestHost + "/Op/" + EAction.GetConnectionCount.ToString();
                int ack = await httpClientHelper.GetSingleItemRequest(requestUri, null);
                return ack;
            }
            catch (TaskCanceledException ex)
            {
                // timeout
                Console.WriteLine("TaskCanceledException:" + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception:" + ex.Message);
            }

            tokenSource.Dispose();
            return -1;
        }

        public async Task<string> GetChattingRoomUserCount(int roomId)
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                HttpClientHelper<string> httpClientHelper = new HttpClientHelper<string>(_httpClient);

                string requestUri = _requestHost + "/Op/" + EAction.GetChattingRoomUserCount.ToString();
                Dictionary<string, string> fields = new Dictionary<string, string>();
                fields.Add("roomId", roomId.ToString());
                string ack = await httpClientHelper.GetSingleItemRequest(requestUri, fields);
                return ack;
            }
            catch (TaskCanceledException ex)
            {
                // timeout
                Console.WriteLine("TaskCanceledException:" + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception:" + ex.Message);
            }

            tokenSource.Dispose();
            return null;
        }
    }
}