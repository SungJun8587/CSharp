using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server
{
    /// <summary>플레이어 로그인 로그</summary>
    public class Log_Player_Login : GameLogDBBase
    {
        /// <summary>로그인 일</summary>
        [Key, Column(Order = 0)]
        public uint InsertDate { get; set; }

        /// <summary>플레이어 번호</summary>
        [Key, Column(Order = 1)]
        public ulong PlayerNo { get; set; }

        /// <summary>일련번호</summary>
        [Key, Column(Order = 2)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public ulong No { get; set; }

        /// <summary>유저 계정번호</summary>
        public ulong UserNo { get; set; }

        /// <summary>닉네임</summary>
        public string Name { get; set; }

        /// <summary>로그인 일시</summary>
        public DateTime InsertDateTime { get; set; }
    }
}