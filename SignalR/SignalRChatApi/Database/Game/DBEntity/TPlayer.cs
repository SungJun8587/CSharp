using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server
{
    /// <summary>플레이어 기본 정보</summary>
    public class TPlayer
    {
        /// <summary>플레이어 번호</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public ulong PlayerNo { get; set; }

        /// <summary>유저 계정 번호</summary>
        public ulong UserNo { get; set; }

        /// <summary>닉네임 Default를 Null로 해야한다</summary>
        [MaxLength(50)]
        public string Name { get; set; }

        /// <summary>프로필 아이콘</summary>
        public uint Icon { get; set; }

        /// <summary>등록 일시(플레이어 생성 일시)</summary>
        public DateTime InsertDate { get; set; }
    }
}