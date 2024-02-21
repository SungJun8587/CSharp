using Microsoft.AspNetCore.SignalR.Client;
using Protocol;

namespace SignalRChat
{
    public partial class GameHub
    {
        // *************************************************************************
        // 재접속 처리
        // *************************************************************************
        public async Task<AckReconnect> ReqReconnect(ulong playerNo)
        {
            AckReconnect ack = new AckReconnect();

            ReqReconnect req = new ReqReconnect
            {
                PlayerNo = playerNo
            };

            try
            {
                ack = await _hubConnection.InvokeAsync<AckReconnect>(ECommand.ReqReconnect.ToString(), req);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return ack;
        }
        
        // *************************************************************************
        // 게임 가입 및 로그인
        // *************************************************************************
        public async Task<AckLogin> GameReqLogin(string fpID)
        {
            AckLogin ack = new AckLogin();

            try
            {
                ack = await _hubConnection.InvokeAsync<AckLogin>(ECommand.ReqLogin.ToString(), fpID);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return ack;
        }

        // *************************************************************************
        // 게임 닉네임 설정
        // *************************************************************************
        public async Task<AckSetNickname> ReqSetNickname(string name)
        {
            AckSetNickname ack = new AckSetNickname();

            try
            {
                ack = await _hubConnection.InvokeAsync<AckSetNickname>(ECommand.ReqSetNickname.ToString(), name);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return ack;
        }

        // *************************************************************************
        // 플레이어 아이콘 설정
        // *************************************************************************
        public async Task<AckSetPlayerIcon> ReqSetPlayerIcon(string name)
        {
            AckSetPlayerIcon ack = new AckSetPlayerIcon();

            try
            {
                ack = await _hubConnection.InvokeAsync<AckSetPlayerIcon>(ECommand.ReqSetPlayerIcon.ToString(), name);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return ack;
        }
    }
}