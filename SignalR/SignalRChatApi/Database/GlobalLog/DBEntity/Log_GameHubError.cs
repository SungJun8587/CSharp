using System.ComponentModel.DataAnnotations;

namespace Server
{
    /// <summary>게임허브 에러 로그</summary>
    public class Log_GameHubError : GlobalLogDBBase
    {
        /// <summary>일련번호</summary>
        [Key]
        public ulong No { get; set; }

        /// <summary>플레이어 번호</summary>
        public ulong PlayerNo { get; set; }

        /// <summary>오류를 발생시키는 응용 프로그램 또는 개체의 이름</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>예외를 설명하는 메시지</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>호출 스택의 직접 실행 프레임 문자열 표현</summary>
        public string StackTrace { get; set; } = string.Empty;

        /// <summary>서버 그룹 번호</summary>
        public uint ServerGroupNo { get; set; }

        /// <summary>서버 채널 번호</summary>
        public uint ServerChannelNo { get; set; }

        /// <summary>확인한 관리자 일련번호</summary>
        public uint ConfirmAdminIdx { get; set; }

        /// <summary>등록 일시</summary>
        public DateTime InsertDateTime { get; set; }
    }
}