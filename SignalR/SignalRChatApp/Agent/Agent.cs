using Common.Lib;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Protocol;
using SignalRChat;
using SignalRChatApp;

namespace SignalRChatApp
{
    public enum EAgentState
    {
        Init,
        GlobalJoin,
        GlobalLogin,
        ConnectHub,
        Action,
        TestConnect,
        TestAction,
    }

    public class Agent : StateKitAsync<EAgentState>
    {
        // 비동기 방식으로 타이머 틱을 처리하는 최신 타이머 API 
        private readonly PeriodicTimer _timer;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly IHost _host;
        private WebAPI _webAPI;
        private GameHub _gameHub;
        private IActionBase _action;

        public ulong _id { get; private set; }
        public ulong _playerNo { get; private set; }
        public string _fpId { get; private set; } = string.Empty;
        public string _deviceId { get; private set; } = string.Empty;

        public Agent(ulong id, IHost host, CancellationToken token)
        {
            _id = id;
            _host = host;

            // 1초당
            _timer = new PeriodicTimer(new TimeSpan(0, 0, 0, 0, 700));
        }

        public async Task StartTimer()
        {
            try
            {
                await InitStateAsync(EAgentState.Init);
            }
            catch (OperationCanceledException ex)
            {
                Console.WriteLine(ex.Message);
                _cts.Cancel();
            }
        }

        async Task Init_Enter()
        {
            if (_webAPI == null)
            {
                _webAPI = new WebAPI(ConfigData.GameServerHost);
            }

            // 초당 100명 기준으로 10틱 간격
            await Task.Delay((int)_id * ConfigData.DelayPerUser);

            await ChangeStateAsync(EAgentState.GlobalJoin);
        }

        async Task GlobalJoin_Enter()
        {
            try
            {
                _deviceId = Guid.NewGuid().ToString();

                ReqGlobalJoin reqGlobalJoin = new ReqGlobalJoin()
                {
                    Id = "",
                    DeviceID = _deviceId
                };

                AckGlobalJoin ack = await _webAPI.GlobalJoin(reqGlobalJoin);
                if (ack == null || ack.RetCode != ERROR_CODE_SPEC.Success)
                {
                    await Task.Delay(5000);
                    await ChangeStateAsync(EAgentState.Init);
                    return;
                }
                _fpId = ack.FpID;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                await Task.Delay(5000);
                await ChangeStateAsync(EAgentState.Init);
                return;
            }

            await ChangeStateAsync(EAgentState.GlobalLogin);
        }

        async Task GlobalLogin_Enter()
        {
            try
            {
                ReqGlobalLogin reqGlobalLogin = new ReqGlobalLogin()
                {
                    FpID = _fpId,
                    DeviceID = _deviceId
                };

                AckGlobalLogin ack = await _webAPI.GlobalLogin(reqGlobalLogin);
                if (ack == null || ack.RetCode != ERROR_CODE_SPEC.Success)
                {
                    await Task.Delay(5000);
                    await ChangeStateAsync(EAgentState.Init);
                    return;
                }
                _fpId = ack.Account.FpID;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                await Task.Delay(5000);
                await ChangeStateAsync(EAgentState.Init);
                return;
            }

            await ChangeStateAsync(EAgentState.ConnectHub);
        }

        async Task ConnectHub_Enter()
        {
            if (_gameHub == null)
            {
                _gameHub = new GameHub(ConfigData.GameServerHost);
            }

            await _gameHub.StartAsync();

            _action = new ActionJson(_gameHub.GetHubConnection(), _id);
            AckLogin ackLogin = await _gameHub.GameReqLogin(_fpId);

            _playerNo = ackLogin.PlayerNo;

            await ChangeStateAsync(EAgentState.Action);
        }

        async Task Action_Enter()
        {
            // 만약 액션이 유효하지 않다면 다시 ConnectHub로 간다
            if (_action == null)
            {
                _ = ChangeStateAsync(EAgentState.ConnectHub);
                return;
            }

            // 타이머마다 반복하면서 액션들을 처리하자
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                try
                {
                    if (_action.CanAction() == false)
                    {
                        // 모든 액션을 했으면, connection을 끊고 다시 처음으로 간다
                        await _gameHub.StopAsync();
                        await Task.Delay(3000);
                        await ChangeStateAsync(EAgentState.GlobalJoin);
                        return;
                    }

                    await _action.DoActions(this);
                }
                catch (Exception ex)
                {
                    // 일단 exception 있어도 타이머 반복시킨다
                    Console.WriteLine(ex.Message);
                }
            }
        }
    }
}