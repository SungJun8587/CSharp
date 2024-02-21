namespace Protocol
{
    /// <summary>클라이언트가 받을 메세지 타입</summary>
    public enum EChatRecvType
    {
        EnterChatRoom,      // 채팅방 입장
        SendChatRoom,       // 채팅방 메세지 보내기
        LeaveChatRoom       // 채팅방 퇴장
    }

    public class PAllPlayerInfo
    {
        /// <summary>플레이어 번호</summary>
        public ulong PlayerNo { get; set; }

        /// <summary>닉네임</summary>
        public string Name { get; set; }

        /// <summary>아이콘 번호</summary>
        public uint Icon { get; set; }
    }

    public class ReqReconnect
    {
        /// <summary>플레이어 번호</summary>
        public ulong PlayerNo { get; set; }

        /// <summary>클라이언트에 저장된 세션 오프셋</summary>
        public int SessionOffset { get; set; }
    }

    public class AckReconnect : AckResult
    {
        /// <summary>플레이어 정보</summary>
        public PAllPlayerInfo PlayerInfo { get; set; }

        /// <summary>세션 생성/갱신때(로그인 혹은 Reconnect)마다 증가되는 Offset - Reconnect시 유효성 검증으로 쓰인다(다른데서 로그인한적 있는지)</summary>
        public int SessionOffset { get; set; }
    }

    public class AckLogin : AckResult
    {
        /// <summary>플레이어 번호</summary>
        public ulong PlayerNo { get; set; }

        /// <summary>닉네임</summary>
        public string Name { get; set; }

        /// <summary>아이콘 번호</summary>
        public uint Icon { get; set; }

        // 세션 생성/갱신때(로그인 혹은 Reconnect)마다 증가되는 Offset - Reconnect시 유효성 검증으로 쓰인다(다른데서 로그인한적 있는지)
        public int SessionOffset { get; set; }
    }

    public class AckSetNickname : AckResult
    {
        /// <summary>닉네임</summary>
        public string Name { get; set; }
    }

    public class AckSetPlayerIcon : AckResult
    {
        /// <summary>아이콘 번호</summary>
        public uint Tid { get; set; }
    }

    //--------------------------------------------------------------------------
    // 채팅
    //--------------------------------------------------------------------------
    public class PChatInfo
    {
        /// <summary>클라이언트가 받을 메세지 타입</summary>
        public EChatRecvType ChatRecvType;

        /// <summary>메세지 ID(채팅방 별로 유니크하게 증가한다)</summary>
        public ulong Id;

        /// <summary>플레이어 번호</summary>
        public ulong PlayerNo;

        /// <summary>닉네임</summary>
        public string Nickname;

        /// <summary>아이콘 번호</summary>
        public uint Icon;

        /// <summary>보낼 메세지</summary>
        public string Msg;

        /// <summary>보낸 일시</summary>
        public long Timestamp;
    }

    public class BCChatRoomRecv
    {
        public List<PChatInfo> Infos { get; set; }
    }

    public class ReqEnterChatRoom
    {
        /// <summary>참여할 방 ID(1 ~ 10)</summary>
        public int RoomId { get; set; }
    }

    public class AckEnterChatRoom : AckResult
    {
        /// <summary>해당 채팅방에서 보낼 메세지(최대 50개이며, 이미 출력한 채팅방에 메세지 Id는 출력 안함)</summary>
        public List<PChatInfo> Infos { get; set; }
    }

    public class ReqSendChatRoom
    {
        /// <summary>보낼 메세지</summary>
        public string Msg { get; set; }
    }

    public class AckSendChatRoom : AckResult
    {
    }

    public class AckLeaveChatRoom : AckResult
    {
        /// <summary>퇴장한 방 ID</summary>
        public int RoomId { get; set; }
    }
}