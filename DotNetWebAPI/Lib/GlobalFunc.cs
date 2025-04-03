using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Primitives;

namespace Common.Lib
{
    public class GlobalFunc
    {
        // ******************************************************************************************
        //
        // Date : 
        // Description : 서버 IP 얻기
        // Parameters
        // Return Type : string
        // Reference :
        //
        // ******************************************************************************************	        
        public static string GetServerIP()
        {
            try
            {
                IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());

                foreach (IPAddress address in ipHostInfo.AddressList)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                        return address.ToString();
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        // ******************************************************************************************
        //
        // Date : 
        // Description : 접속한 유저 IP 얻기
        // Parameters
        // Return Type : string
        // Reference :
        //
        // ******************************************************************************************		
        public static string GetUserIP()
        {
            string UserIP = string.Empty;
            StringValues values;

            try
            {
                HttpContext httpContext = new HttpContextAccessor().HttpContext;
                if (httpContext == null) return UserIP;

                if (httpContext.Request?.Headers?.TryGetValue("X-Forwarded-For", out values) ?? false)
                {
                    string rawValues = values.ToString();
                    if (!string.IsNullOrWhiteSpace(rawValues))
                    {
                        string[] pRawValues = rawValues.Split(',');
                        if (pRawValues != null && pRawValues.Length > 0)
                        {
                            UserIP = pRawValues[0];
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(UserIP) && httpContext.Connection != null && httpContext.Connection.RemoteIpAddress != null)
                {
                    var remoteIpAddress = httpContext.Connection.RemoteIpAddress;
                    if (remoteIpAddress != null)
                    {
                        if (remoteIpAddress.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            remoteIpAddress = Dns.GetHostEntry(remoteIpAddress).AddressList.First(x => x.AddressFamily == AddressFamily.InterNetwork);
                        }
                        UserIP = remoteIpAddress.ToString();
                    }
                }

                if (string.IsNullOrWhiteSpace(UserIP))
                {
                    if (httpContext.Request?.Headers?.TryGetValue("REMOTE_ADDR", out values) ?? false)
                    {
                        string rawValues = values.ToString();
                        if (!string.IsNullOrWhiteSpace(rawValues))
                            UserIP = values.ToString();
                    }
                }

                return UserIP;
            }
            catch
            {
                return string.Empty;
            }
        }

        // ******************************************************************************************
        //
        // Date : 
        // Description : 19자리 고유숫자 ID 생성(0 ~ 18446744073709551615)
        // Parameters
        // Return Type : ulong
        // Reference :
        //
        // ******************************************************************************************		
        public static ulong GetNewIDToGuid()
        {

            byte[] gb = Guid.NewGuid().ToByteArray();
            return BitConverter.ToUInt64(gb, 0);
        }

        // ******************************************************************************************
        //
        // Date : 
        // Description : HTTP 헤더 Key, Value로 저장
        // Parameters
        // Return Type : Dictionary<string, string>
        // Reference :
        //
        // ******************************************************************************************		
        public static Dictionary<string, string> FormatHeaders(IHeaderDictionary headers)
        {
            Dictionary<string, string> pairs = new Dictionary<string, string>();
            foreach (var header in headers)
            {
                pairs.Add(header.Key, header.Value);
            }
            return pairs;
        }

        // ******************************************************************************************
        //
        // Date : 
        // Description : HTTP 쿼리스트링 Key, Value 기반으로 List 저장
        // Parameters
        // Return Type : List<KeyValuePair<string, string>>
        // Reference :
        //
        // ******************************************************************************************	
        public static List<KeyValuePair<string, string>> FormatQueries(string queryString)
        {
            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>();
            string key, value;
            foreach (var query in queryString.TrimStart('?').Split("&"))
            {
                var items = query.Split("=");
                key = items.Count() >= 1 ? items[0] : string.Empty;
                value = items.Count() >= 2 ? items[1] : string.Empty;
                if (!String.IsNullOrEmpty(key))
                {
                    pairs.Add(new KeyValuePair<string, string>(key, value));
                }
            }
            return pairs;
        }        
    }
}