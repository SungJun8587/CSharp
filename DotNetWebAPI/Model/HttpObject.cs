using Common.Lib;

namespace DotNetWebAPI.Model
{
    /// <summary>HTTP 요청 정보</summary>
    public class CHttpRequest
    {
        /// <summary>HTTP 요청 일시</summary>
        public DateTime DateTime { get; set; }
        
        /// <summary>컨트롤러의 특정 액션 요청 일시</summary>
        public DateTime? DateTimeActionLevel { get; set; }
        
        /// <summary>요청의 가상 경로</summary>
        public string Path { get; set; }
        
        /// <summary>쿼리 문자열</summary>
        public string Query { get; set; }
        
        /// <summary>쿼리 문자열 변수의 컬렉션</summary>
        public List<KeyValuePair<string, string>> Queries { get; set; }
        
        /// <summary>HTTP Method(GET, POST, PUT, DELETE)</summary>
        public string Method { get; set; }
        
        /// <summary>URI에서 사용하는 프로토콜</summary>
        public string Scheme { get; set; }
        
        /// <summary>요청하는 호스트에 대한 호스트명 및 포트번호</summary>
        public string Host { get; set; }
        
        /// <summary>요청 헤더 변수의 컬렉션</summary>
        public Dictionary<string, string> Headers { get; set; }
        
        /// <summary>요청 내용</summary>
        public string Body { get; set; }
        
        /// <summary>요청 컨텐츠 타입(html, xml, json 등)</summary>
        public string ContentType { get; set; }
    }

    /// <summary>HTTP 응답 정보</summary>
    public class CHttpResponse
    {
        /// <summary>HTTP 응답 일시</summary>
        public DateTime DateTime { get; set; }
        
        /// <summary>컨트롤러의 특정 액션 응답 일시</summary>
        public DateTime? DateTimeActionLevel { get; set; }

        /// <summary>응답 상태 코드</summary>
        public string Status { get; set; }

        /// <summary>응답 헤더 변수의 컬렉션</summary>
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>응답 내용</summary>
        public string Body { get; set; }

        /// <summary>응답 컨텐츠 타입(html, xml, json 등)</summary>
        public string ContentType { get; set; }        
    }

    /// <summary>HTTP 에러 정보</summary>
    public class CHttpException
    {
        /// <summary>에러 여부(false/true : 무/유)</summary>
        public bool IsActionLevel { get; set; }

        /// <summary>오류를 발생시키는 애플리케이션 또는 개체의 이름</summary>
        public string Source { get; set; }

        /// <summary>현재 예외를 설명하는 메시지</summary>
        public string Message { get; set; }

        /// <summary>호출 스택의 직접 실행 프레임 문자열 표현</summary>
        public string StackTrace { get; set; }        
    }

    /// <summary>HTTP 로그 옵션 정보</summary>
    public class HttpLoggerOption
    {
        /// <summary>로그 활성 여부(false/true : 무/유)</summary>
        public bool IsEnabled { get; set; }
        
        /// <summary>프로젝트 명</summary>
        public string Name { get; set; }
        
        /// <summary>로그 일시 포멧</summary>
        public string DateTimeFormat { get; set; }
    }

    /// <summary>HTTP 로그 정보</summary>
    public class HttpLogModel
    {
        /// <summary>로그 ID(Guid.NewGuid().ToString())</summary>
        public string LogId { get; set; }           
        
        /// <summary>프로젝트 명</summary>
        public string Node { get; set; }

        /// <summary>클라이언트 IP</summary>
        public string ClientIP { get; set; }

        /// <summary>추적 로그에서 이 요청을 나타내는 고유 식별자(HttpContext TraceIdentifier)</summary>
        public string TraceId { get; set; }  

        /// <summary>HTTP 요청 정보</summary>
        public CHttpRequest Request { get; set; }

        /// <summary>HTTP 응답 정보</summary>
        public CHttpResponse Response { get; set; }

        /// <summary>HTTP 에러 정보</summary>
        public CHttpException Exception { get; set; }

        public HttpLogModel()
        {
            LogId = GlobalFunc.GetNewIDToGuid().ToString();

            Request = new CHttpRequest();
            Response = new CHttpResponse();
            Exception = new CHttpException();
        }        
    }  
}