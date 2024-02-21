using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server
{
    /// <summary>유저 가입 로그</summary>
    public class Log_User_Register : GlobalLogDBBase
    {
        /// <summary>가입 일</summary>
        [Key, Column(Order = 0)]
        public uint InsertDate { get; set; }

        /// <summary>유저 계정번호</summary>
        [Key, Column(Order = 1)]
        public ulong UserNo { get; set; }

        /// <summary>일련번호</summary>
        [Key, Column(Order = 2)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] 
        public ulong No { get; set; }

        /// <summary>고유 유저키값</summary>
        public string FpID { get; set; } = string.Empty;
        
        /// <summary>기기 ID</summary>
        public string DeviceID { get; set; } = string.Empty;

        /// <summary>가입 일시</summary>
        public DateTime InsertDateTime { get; set; }
    }
}