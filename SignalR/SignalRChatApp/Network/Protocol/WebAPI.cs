namespace Protocol
{
    public class ReqGlobalJoin
    {
        /// <summary>ID(AccountType == 0 or 1이면 FpID, AccountType == 2이면 SnsID)</summary>
        public string Id { get; set; } = string.Empty;

        public string DeviceID { get; set; } = string.Empty;
    }

    public class AckGlobalJoin : AckResult
    {
        /// <summary>유저 UUID</summary>
        public string FpID { get; set; } = string.Empty;

        /// <summary>최초 가입 여부(0/1 : 무/유)
        ///  - FpID에 해당하는 UserNo가 존재할 경우 IsFirstJoin = false
        ///  - FpID에 해당하는 UserNo가 존재하지 않아 새로 생성한 경우 IsFirstJoin = true
        /// </summary>        
        public bool IsFirstJoin { get; set; }

        /// <summary>가입 일시</summary>        
        public DateTime JoinDateTime { get; set; }
    }

    public class ReqGlobalLogin
    {
        public string FpID { get; set; } = string.Empty;
        public string DeviceID { get; set; } = string.Empty;
    }

    public class PAccount
    {
        /// <summary>유저 계정 번호</summary>
        public ulong UserNo { get; set; }

        /// <summary>유저 UUID. MySQL UUID() 함수로 생성한 값.</summary>
        public string FpID { get; set; }

        /// <summary>기기 ID</summary>
        public string DeviceID { get; set; } = string.Empty;

        /// <summary>삭제 유무(true/false:삭제/정상)</summary>
        public bool IsDeleted { get; set; }
    }

    public class AckGlobalLogin : AckResult
    {
        // 상태(0/1/2/3 : 진입가능/점검/제재유저/탈퇴유저)
        public byte State { get; set; }

        public PAccount Account { get; set; }
    }
}