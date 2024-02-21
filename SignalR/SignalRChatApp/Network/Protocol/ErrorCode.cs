namespace Protocol
{
    /// <summary>
    /// 응답 에러(클라내부 처리용)
    /// 10000 번 이후에는 유저
    /// </summary>
    public enum ERROR_CODE_SPEC
    {
        //-------------------------------------------------------
        // System
        //-------------------------------------------------------
        UnDefine = -1,          // 기타 오류(파라미터 등)
        Success = 0,            // 성공
        DB_Error = 1,           // DB 처리 오류

        NoConnection,           // 커넥션이 연결되어있지 않다
        NoSession,              // 세션이 만료되거나 로그인이 안되어있다
        NotComplete,            // 완료되지 않음
        SystemFrequentlyLogin,  // 너무 빈번한 로그인 요청
 
         //-------------------------------------------------------
        // Account
        //-------------------------------------------------------
        NoAccountDB,            // Account DB 없음
        NonExistsUser,          // 존재하지 않는 유저
        NotAvailableGuid,       // 유효하지 않은 guid

        //-------------------------------------------------------
        // Player
        //-------------------------------------------------------
        NoPlayerDB,             // Player DB 없음
        FailedToSetNickname,    // PlayerSetNickname 실패
        DuplicateNickName,      // 닉네임 중복
        FailedToSetIcon,        // PlayerSetIcon 실패

        //-------------------------------------------------------
        // Chat
        //-------------------------------------------------------
        ChatNotAvailableRoom,   // 잘못된 방 ID 요청
        ChatSameRoomId,         // 같은 채팅방 ID
        ChatFullRoom,           // 채팅방 정원 초과
        ChatCanNotEnter,        // 채팅방 참가 불가     
        ChatNeedToEnter,        // 채팅방 참가 필요
    }

    public class AckResult
    {
        public ERROR_CODE_SPEC RetCode { get; set; }
        public string RetMessage { get; set; } = string.Empty;
    }    
}
