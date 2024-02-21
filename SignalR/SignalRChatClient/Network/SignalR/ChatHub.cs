using Microsoft.AspNetCore.SignalR.Client;
using Protocol;

namespace SignalRChat
{
    public partial class GameHub
    {
        // *************************************************************************
        // 채팅방 참여
        // *************************************************************************
        public async Task<AckEnterChatRoom> ReqEnterChatRoom(int roomId)
        {
            AckEnterChatRoom ack = new AckEnterChatRoom();

            ReqEnterChatRoom req = new ReqEnterChatRoom
            {
                RoomId = roomId
            };

            try
            {
                ack = await _hubConnection.InvokeAsync<AckEnterChatRoom>(ECommand.ReqEnterChatRoom.ToString(), req);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return ack;
        }

        // *************************************************************************
        // 채팅방 메세지 전송
        // *************************************************************************
        public async Task<AckSendChatRoom> ReqSendChatRoom(string msg, uint emoticon)
        {
            AckSendChatRoom ack = new AckSendChatRoom();

            ReqSendChatRoom req = new ReqSendChatRoom
            {
                Msg = msg
            };

            try
            {
                ack = await _hubConnection.InvokeAsync<AckSendChatRoom>(ECommand.ReqSendChatRoom.ToString(), req);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return ack;
        }

        // *************************************************************************
        // 채팅방 퇴장
        // *************************************************************************
        public async Task<AckLeaveChatRoom> ReqLeaveChatRoom()
        {
            AckLeaveChatRoom ack = new AckLeaveChatRoom();

            try
            {
                ack = await _hubConnection.InvokeAsync<AckLeaveChatRoom>(ECommand.ReqLeaveChatRoom.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return ack;
        }        
    }
}