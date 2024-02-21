using Microsoft.AspNetCore.SignalR;
using Protocol;

namespace Server
{
    public class ErrorHelper
    {
        public static void Throw(ERROR_CODE_SPEC code, string debugComment)
        {
            string message = string.Format(
                "${0}$ ^{1}^", code, debugComment);

            //DebugParse(message, out ERROR_CODE_SPEC resErr, out USER_ERR userErr, out string comment);
            throw new HubException(message);
        }
    }
}

