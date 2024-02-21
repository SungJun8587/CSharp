using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Server
{
    //---------------------------------------------------------------
    // GameHub
    // 다른 파일의 허브도 모두 GameHub로 통일해서 사용한다
    //---------------------------------------------------------------
    public partial class GameHub : Hub
    {
        // 다른 쓰레드에서 dbContext 접근을 위해 필요하다
        private readonly IServiceScopeFactory _serviceScopeFactory;
        
        // https://blog.hildenco.com/2018/12/accessing-entity-framework-context-on.html
        // 위에 따르면, hub를 직접 이용하는것은 위험하므로 hubcontext를 따로 저장해서 간다
        private readonly IHubContext<GameHub> _hubContext;

        private readonly IDbContextFactory<GameDBContext> _contextFactory;

        private readonly ILoggerService _logger;

        public GameHub(
            IServiceScopeFactory serviceScopeFactory,
            IHubContext<GameHub> hubContext,
            IDbContextFactory<GameDBContext> contextFactory,
            ILoggerService logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _hubContext = hubContext;
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            //string userName = Context.User.Identity.Name;
            //await Task.Delay(10);
            var connectionId = Context.ConnectionId;

            SgConnInfo.Instance.Add(connectionId, Context);

            await Task.CompletedTask;
        }

        public override async Task OnDisconnectedAsync(Exception ex)
        {
            var connectionId = Context.ConnectionId;
            //await Clients.Others.SendAsync("CSend", $"{connectionId} left");

            SgConnInfo.Instance.Remove(connectionId);

            if (SgSession.Instance.GetSessionInfo(connectionId, out SessionInfo info) == false)
            {
                // 없으면 끝낸다
                return;
            }

            // 채팅룸에 있는것으로 추정된다면
            if (info.chatRoomId > 0)
            {
                await SgChatting.I.LeaveChat(Clients, Groups, info);
            }
        }

        // 연결된 모든 클라이언트에 메세지 보내기
        // return Clients.All.SendAsync("Send", $"{Context.ConnectionId}: {message}");

        // 메서드를 호출한 클라이언트를 제외하고, 연결된 모든 클라이언트에 메세지 보내기
        // return Clients.Others.SendAsync("Send", $"{Context.ConnectionId}: {message}");

        // 지정된 클라이언트를 제외하고, 연결된 모든 클라이언트에 메세지 보내기
        // AllExcept(IReadOnlyList<string> excludedConnectionIds);

        // 연결된 특정 클라이언트(connectionId)에 메세지 보내기
        // return Clients.Client(connectionId).SendAsync("Send", $"Private message from {Context.ConnectionId}: {message}");

        // 지정된 그룹(groupName)에 속한 모든 클라이언트에 메세지 보내기
        // return Clients.Group(groupName).SendAsync("Send", $"{Context.ConnectionId}@{groupName}: {message}");

        // 호출한 클라이언트가 속한 그룹에서 호출한 클라이언트를 제외하고 모든 그룹원에게 메시지 보내기
        // return Clients.OthersInGroup(groupName).SendAsync("Send", $"{Context.ConnectionId}@{groupName}: {message}");

        // 호출한 클라이언트에 메세지 보내기
        // return Clients.Caller.SendAsync("Send", $"{Context.ConnectionId}: {message}");       
    }
}

