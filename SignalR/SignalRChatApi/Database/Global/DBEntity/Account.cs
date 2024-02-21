using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Server
{
    /// <summary>유저 기본 정보</summary>
    public class Account
    {
        /// <summary>유저 계정 번호</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public ulong UserNo { get; set; }

        /// <summary>기기 ID</summary>
        [Column(TypeName = "varchar(100)")]
        public string DeviceID { get; set; } = string.Empty;

        /// <summary>삭제 유무(true/false:삭제/정상)</summary>
        public bool IsDeleted { get; set; }

        /// <summary>등록 일시</summary>
        [JsonIgnore]
        public DateTime InsertDate { get; set; }

        /// <summary>삭제 일시</summary>
        [JsonIgnore]
        public DateTime DeleteDate { get; set; }
    }
}