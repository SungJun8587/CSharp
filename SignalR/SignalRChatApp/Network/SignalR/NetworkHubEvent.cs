using Microsoft.AspNetCore.SignalR.Client;
using Protocol;

namespace SignalRChat
{
    public class NetworkHubEvent
    {
        private HubConnection _hub;

        public NetworkHubEvent(HubConnection hub)
        {
            _hub = hub;
        }

        public Action<BCChatRoomRecv> Event_RecvChatRoomNoti;
        public Action<BCChatRoomRecv> Event_RecvChatRoomMessage;

        public void RegisterEvent()
        {
            _hub.On("BCRecvChatRoomNoti", Event_RecvChatRoomNoti);
            _hub.On("BCRecvChatRoomMessage", Event_RecvChatRoomMessage);
        }
    }
}