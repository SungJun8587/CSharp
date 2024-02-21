namespace SignalRChat
{
    public enum EEnvironments
    {
        DL_Local
    }

    public enum EAction
    {
        GlobalJoin,
        GlobalLogin,
        InitSessionCount,
        GetSessionCount,
        GetConnectionCount,
        GetChattingRoomUserCount
    }

    public enum ECommand
    {
        // PlayerHub
        ReqReconnect,
        ReqLogin,
        ReqSetNickname,
        ReqSetPlayerIcon,

        // ChatHub
        ReqEnterChatRoom,
        ReqSendChatRoom,
        ReqLeaveChatRoom
    }
}