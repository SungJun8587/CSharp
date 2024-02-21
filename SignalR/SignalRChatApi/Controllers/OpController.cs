using Microsoft.AspNetCore.Mvc;

namespace Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class OpController : Controller
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public OpController(
            IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        [Route("InitSessionCount")]
        public Tuple<int, int> InitSessionCount()
        {
            int prevSessionCount = SgSession.Instance.GetSessionCount();
            SgSession.Instance.RemoveAll();
            int sessionCount = SgSession.Instance.GetSessionCount();

            return new Tuple<int, int>(prevSessionCount, sessionCount);
        }

        [Route("GetSessionCount")]
        public int GetSessionCount()
        {
            int sessionCount = SgSession.Instance.GetSessionCount();

            return sessionCount;
        }

        [Route("GetConnectionCount")]
        public int GetConnectionCount()
        {
            int connectionCount = SgConnInfo.Instance.GetConnectionCount();

            return connectionCount;
        }

        [Route("GetChattingRoomUserCount")]
        public JsonResult GetChattingRoomUserCount(int roomId)
        {
            Dictionary<int, int> dicRoomPerUser = new Dictionary<int, int>();

            if (roomId > 0)
            {
                int count = SgChatting.I.GetRoomUserCount(roomId);
                dicRoomPerUser.Add(roomId, count);
            }
            else
            {
                for (var i = 1; i <= SgChatting.I.GetMaxRoomCount(); i++)
                {   
                    int count = SgChatting.I.GetRoomUserCount(i);
                    dicRoomPerUser.Add(i, count);
                }
            }

            return new JsonResult(dicRoomPerUser) { SerializerSettings = new System.Text.Json.JsonSerializerOptions() { WriteIndented = true } };
        } 
    }
}
