using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;
using Protocol;
using Common.Lib;

namespace Server
{
    /// <summary>채팅방별 인원 제한 처리</summary>
    public class SgChatting
    {
        private static readonly Lazy<SgChatting> instanceHolder =
            new Lazy<SgChatting>(() => new SgChatting());

        /// <summary>채팅에 참여할 수 있는 최대 유저수</summary>
        private readonly int maxUserCount = 600;

        /// <summary>최대 방수</summary>
        private readonly int maxRoomCount = 10;

        /// <summary>채팅방에 참여할 수 있는 최대 유저수</summary>
        private readonly int maxUserPerRoom;

        /// <summary>채팅방별 유저 카운트 관리<summary>
        private readonly ConcurrentDictionary<int, int> counts = new ConcurrentDictionary<int, int>();

        /// <summary>채팅방별 유저 메세지 저장소</summary>
        private readonly Dictionary<int, ConcurrentQueue<PChatInfo>> _messages = new Dictionary<int, ConcurrentQueue<PChatInfo>>();

        /// <summary>채팅방별 유저 메세지 카운트</summary>
        private readonly Dictionary<int, ulong> _messageCount = new Dictionary<int, ulong>();

        private SgChatting()
        {
            maxUserPerRoom = maxUserCount / maxRoomCount;
            for (var i = 1; i <= maxRoomCount; i++)
            {
                counts.TryAdd(i, 0);
                _messages.TryAdd(i, new ConcurrentQueue<PChatInfo>());
                _messageCount.TryAdd(i, 0);
            }

            // 하나의 채널이고, 여러유저(writer)가 쓰고, 여러 Task에서 읽어갈(reader) 것이다
            _channel = Channel.CreateUnbounded<Func<Task>>(
                // 기본값이다(여러 Reader, 여러 Writer)
                new UnboundedChannelOptions() { SingleReader = false, SingleWriter = false }
                );
        }

        public static SgChatting I
        {
            get { return instanceHolder.Value; }
        }

        // *************************************************************************
        // 채팅방 진입시, 적정한 방 찾아서 할당
        // *************************************************************************
        public int FindCandidateRoom()
        {
            var start = 1;
            var end = maxRoomCount;
            int curIndex = -1;
            for (var i = start; i <= end; i++)
            {
                curIndex = i;

                // 채팅방에 참여할 수 있는 최대 유저수를 초과했는지 체크
                if (counts[i] < maxUserPerRoom)
                {
                    break;
                }
            }
            return curIndex;
        }

        public int GetMaxRoomCount()
        {
            return maxRoomCount;
        }

        public int GetRoomUserCount(int roomId)
        {
            if (counts.TryGetValue(roomId, out int count) == false)
            {
                return 0;
            }

            return count;
        }

        // *************************************************************************
        // 해당 채팅방 진입할 수 있는지 체크
        // *************************************************************************
        public bool CanEnter(int roomId)
        {
            if (counts.TryGetValue(roomId, out int count) == false)
            {
                return true;
            }
            return count < maxUserPerRoom;  // 채팅방에 참여할 수 있는 최대 유저수를 초과했는지 체크
        }

        // *************************************************************************
        // 채팅방 진입
        // *************************************************************************
        public async Task EnterChat(IHubCallerClients hubCallerClients, IGroupManager groupManager, SessionInfo session, int roomId)
        {
            // 1. 진입 카운트(선카운트 증가)
            SgChatting.I.AddRoomCount(roomId, 1);

            // 2. 지정된 그룹(ChannelChat:방번호)에 연결을 추가
            await groupManager.AddToGroupAsync(session.connectionId, "ChannelChat:" + roomId);

            // 3. 세션에 기록
            session.chatRoomId = roomId;

            // 4. 해당 채팅방에 있는 유저들에게 진입 메세지 전파
            var bcChannel = new BCChatRoomRecv
            {
                Infos = new List<PChatInfo>()
            };
            SgChatting.I.AddMessagePacket(bcChannel.Infos, session, EChatRecvType.EnterChatRoom, "'" + session.Nickname + "'님이 방 " + roomId + "에 입장했습니다");

            // 허브 메서드를 호출한 클라이언트가 속한 그룹에서 호출한 클라이언트를 제외하고 모든 그룹원에게 메시지 보내기
            await hubCallerClients.OthersInGroup("ChannelChat:" + session.chatRoomId).SendAsync("BCRecvChatRoomNoti", bcChannel);
        }

        // *************************************************************************
        // 채팅방 퇴장
        // *************************************************************************
        public async Task LeaveChat(IHubCallerClients hubCallerClients, IGroupManager groupManager, SessionInfo session)
        {
            var oldRoomId = session.chatRoomId;

            // 1. 세션에 기록
            session.chatRoomId = 0;

            // 2. 지정된 그룹(ChannelChat:방번호)에서 연결을 제거
            await groupManager.RemoveFromGroupAsync(session.connectionId, "ChannelChat:" + oldRoomId);

            // 3. 진입 카운트 감소
            SgChatting.I.AddRoomCount(oldRoomId, -1);

            // 4. 해당 채팅방에 있는 유저들에게 퇴장 메세지 전파
            var bcChannel = new BCChatRoomRecv
            {
                Infos = new List<PChatInfo>()
            };
            SgChatting.I.AddMessagePacket(bcChannel.Infos, session, EChatRecvType.LeaveChatRoom, "'" + session.Nickname + "'님이 방 " + oldRoomId + "에서 퇴장했습니다");

            // 허브 메서드를 호출한 클라이언트가 속한 그룹에서 호출한 클라이언트를 제외하고 모든 그룹원에게 메시지 보내기
            await hubCallerClients.OthersInGroup("ChannelChat:" + oldRoomId).SendAsync("BCRecvChatRoomNoti", bcChannel);
        }

        private void AddRoomCount(int roomId, int addCount)
        {
            counts.AddOrUpdate(roomId, addCount, (index, existingValue) =>
            {
                // 이미 있는 경우에 호출
                return existingValue + addCount;
            });
        }

        private void AddMessagePacket(List<PChatInfo> list, SessionInfo info, EChatRecvType chatRecvType, string message)
        {
            list.Add(new PChatInfo
            {
                ChatRecvType = chatRecvType,
                PlayerNo = info.playerNo,
                Nickname = info.Nickname,
                Icon = info.Icon,
                Msg = message,
                Timestamp = SgTime.I.Now
            });
        }

        #region Task관련
        private readonly Channel<Func<Task>> _channel;
        private List<Task> tasks = new List<Task>();

        public void Init()
        {
            tasks.Clear();

            // 채널 수 만큼
            tasks.Add(Run(maxRoomCount, async () =>
            {
                // 일단 임시로 consumer 1개
                while (await _channel.Reader.WaitToReadAsync())
                {
                    while (_channel.Reader.TryRead(out var cb))
                    {
                        await cb();
                    }
                }
            }));
        }

        private async Task Run(int concurrency, Func<Task> action)
        {
            var tasks = new List<Task>();
            for (var i = 0; i < concurrency; i++)
            {
                tasks.Add(action.Invoke());
            }
            await Task.WhenAll(tasks);
        }

        public Task<string> InvokeTask(Func<Task> invoke)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = tcs.Task;
            var box = async () =>
            {
                string result = "";
                try
                {
                    await invoke();
                }
                catch (HubException ex)
                {
                    result = ex.ToString();
                }
                catch (Exception ex)
                {
                    result = ex.ToString();
                }
                finally
                {
                    tcs.SetResult(result);
                }
            };

            var sw = new SpinWait();
            while (!_channel.Writer.TryWrite(box)) sw.SpinOnce();
            return task;
        }

        public async Task AddMessage(int roomId, PChatInfo info)
        {
            await InvokeTask(async () =>
            {
                if (_messageCount.TryGetValue(roomId, out ulong count) == false)
                {
                    return;
                }
                var id = count + 1;
                _messageCount[roomId] = id;
                // Id 갱신
                info.Id = id;

                if (_messages.TryGetValue(roomId, out ConcurrentQueue<PChatInfo> queue) == false)
                {
                    return;
                }

                queue.Enqueue(info);

                int tryCount = 10;
                // 최대카운트(50) 넘어가면 지워준다
                while (tryCount > 0 && queue.Count > 50)
                {
                    tryCount--;
                    queue.TryDequeue(out PChatInfo chat);
                }

                await Task.CompletedTask;
            });
        }

        public async Task GetMessages(int roomId, List<PChatInfo> infos, ulong lastReadId)
        {
            await InvokeTask(async () =>
            {
                if (_messages.TryGetValue(roomId, out ConcurrentQueue<PChatInfo> queue) == false)
                {
                    return;
                }
                foreach (var info in queue)
                {
                    if (info.Id > lastReadId)
                    {
                        infos.Add(info);
                    }
                }
                await Task.CompletedTask;
            });
        }
        #endregion
    }
}
