using Common.Lib;
using System.Text;
using System.Web;

namespace Server
{
    public class ParamDecryption
    {
        private readonly RequestDelegate _next;

        public ParamDecryption(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task Invoke(HttpContext httpContext)
        {
            string reqData = string.Empty;

            HttpRequest request = httpContext.Request;

            // 요청한 Post 또는 Get 방식 Data
            if (request.Method == "POST")
            {
                request.EnableBuffering();  // 요청 본문(Body)을 여러 번 읽을 수 있도록 메서드를 호출
                request.Body.Position = 0;  // 요청 본문의 위치를 ​​0으로 재설정(파이프라인의 다음 작업에서도 원할하게 실행되야하므로, 읽었던 Body Stream의 위치를 초기화)
                using (var reader = new StreamReader(request.Body, Encoding.UTF8))
                {
                    var requestBodyAsString = await reader.ReadToEndAsync().ConfigureAwait(false);
                    reqData = HttpUtility.UrlDecode(requestBodyAsString);
                }
            }
            else
            {
                reqData = request.QueryString.ToString();
            }

            if (!string.IsNullOrEmpty(reqData))
            {
                if (!ConfigData.IsDebug)
                {
                    if(SecurityUtility.DecryptString(reqData, out string output) == true)
                    {
                        reqData = output;
                    };
                }
                
                if (request.Method == "POST")
                {
                    // Form 데이터 변경
                    byte[] bytes = Encoding.UTF8.GetBytes(reqData);
                    request.Body = new MemoryStream(bytes);
                }
                else
                {
                    // QueryString 데이터 변경
                    QueryString queryString = new QueryString(reqData);
                    request.QueryString = queryString;
                }
            }

            await _next.Invoke(httpContext);
        }
    }
}



