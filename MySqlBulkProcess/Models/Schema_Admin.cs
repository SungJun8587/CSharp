using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AdminTool.Models
{
    [Table("Tool_AdminActionLog")]
    public class Schema_AdminLog
    {
       /// <summary>일련번호</summary>
        public uint No { get; set; }

        /// <summary>관리자 일련번호</summary>
        public uint AdminIdx { get; set; }

        /// <summary>접속 IP</summary>
        public string IP { get; set; } = string.Empty;

        /// <summary>열람 메뉴</summary>
        public string URL { get; set; } = string.Empty;

        /// <summary>메뉴 일련번호</summary>
        public uint MenuNo { get; set; }

        /// <summary>메뉴 위치</summary>
        public string MenuSortFullNo { get; set; } = string.Empty;

        /// <summary>요청 사항(로그인, CRUD 등)</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>수정전 데이터</summary>
        public string PrevData { get; set; } = string.Empty;

        /// <summary>요청 내역(수정 후 데이터)</summary>
        public string ReqData { get; set; } = string.Empty;

        /// <summary>운영자 코멘트</summary>
        public string Note { get; set; } = string.Empty;

        /// <summary>확인한 관리자 일련번호</summary>
        public uint ConfirmAdminIdx { get; set; }

        /// <summary>등록 일시</summary>
        public DateTime InsertDate { get; set; }

        /// <summary>수정 일시</summary>
        public DateTime UpdateDate { get; set; }
    }
}