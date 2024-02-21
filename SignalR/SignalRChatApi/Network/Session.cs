using System.Collections.Concurrent;
using Common.Lib;
using Microsoft.AspNetCore.SignalR;

namespace Server
{
    /// <summary>세션 정보</summary>
    public class SessionInfo
    {
        /// <summary>허브 호출자 연결에 대한 정보에 액세스하기 위한 컨텍스트</summary>
        public HubCallerContext hubContext;

        /// <summary>연결 ID</summary>
        public string connectionId;

        /// <summary>참여하고 있는 채팅방 Id</summary>
        public int chatRoomId;

        /// <summary>유저 계정 번호</summary>
        public ulong userNo;

        /// <summary>플레이어 번호</summary>
        public ulong playerNo;

        /// <summary>닉네임</summary>
        public string Nickname { get; set; }

        /// <summary>아이콘</summary>
        public uint Icon { get; set; }

        /// <summary>세션 수정 일시</summary>
        public long UpdatedTime { get; set; }

        /// <summary>최종 로그인 일시</summary>
        public long LastLoginTime { get; set; }

        /// <summary>삭제를 위한 예약 플래그
        ///     - 접속이 끊어졌을 경우 해당 플래그를 설정(true)
        ///     - SgSession에 RepeatAsync() 함수가 10초에 한번씩 체크하면서 해당 플래그가 설정(true)된 세션을 삭제 처리
        /// </summary>
        public bool IsReserveForDelete { get; set; }

        /// <summary>세션 갱신 혹은 생성시에 증가</summary>
        public int SessionOffset { get; set; }

        public void SetInfo(SessionInfo newInfo)
        {
            hubContext = newInfo.hubContext;
            connectionId = newInfo.connectionId;
            chatRoomId = newInfo.chatRoomId;
            userNo = newInfo.userNo;
            playerNo = newInfo.playerNo;
            Nickname = newInfo.Nickname;
            Icon = newInfo.Icon;
            UpdatedTime = newInfo.UpdatedTime;
            LastLoginTime = newInfo.LastLoginTime;
            IsReserveForDelete = newInfo.IsReserveForDelete;
            SessionOffset++;
        }
    }

    /// <summary>세션 정보 관리
    ///     - 세션 정보를 10초에 한번씩 체크하면서 삭제를 위한 예약 플래그(IsReserveForDelete)가 설정(true)된 세션을 삭제 처리
    ///     - 클라이언트에 접속이 끊어져도, 재접속 처리를 위해 세션은 바로 삭제하지 않고, 15분 정도에 유예 시간(세션 수정 일시가 15분이 지난 경우)을 부여한 후에 삭제
    /// </summary>
    public class SgSession
    {
        private static readonly Lazy<SgSession> instanceHolder =
            new Lazy<SgSession>(() => new SgSession());

#nullable enable
        private Task? _timerTask;
#nullable restore
        private readonly PeriodicTimer _timer;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public SgSession()
        {
            _timer = new PeriodicTimer(new TimeSpan(0, 0, 10));
            _timerTask = RepeatAsync();
        }

        public async Task RepeatAsync()
        {
            try
            {
                while (await _timer.WaitForNextTickAsync(_cts.Token))
                {
                    try
                    {
                        _removeCandidates.Clear();

                        foreach (var pair in _playerNoInfos)
                        {
                            var info = pair.Value;

                            // 세션 수정 일시가 15분이 지났거나, 삭제 플래그가 설정(true)된 세션 삭제 처리 
                            if ((info.UpdatedTime + (SgTime.T_MINUTE * 15) < SgTime.I.Now) 
                                || info.IsReserveForDelete == true)
                            {
                                _removeCandidates.Add(pair.Key);
                            }
                        }

                        foreach (var playerNo in _removeCandidates)
                        {
                            Remove(playerNo);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 내부에서 실패하더라도 RepeatAsync는 계속되어야한다
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public static SgSession Instance
        {
            get { return instanceHolder.Value; }
        }

        // PlayerString(ConnectionId) - PlayerNo 맵
        private ConcurrentDictionary<string, ulong> _infos =
            new ConcurrentDictionary<string, ulong>();

        // Add - 로그인, Remove : 클라이언트 접속이 끊어졌을 때 바로 삭제하지 않고, 15분 정도에 유예 시간(세션 수정 일시가 15분이 지난 경우)을 부여한 후에 삭제
        // Session 맵 - Key:PlayerNo, Value:SessionInfo
        private ConcurrentDictionary<ulong, SessionInfo> _playerNoInfos =
            new ConcurrentDictionary<ulong, SessionInfo>();

        private ConcurrentBag<ulong> _removeCandidates = new ConcurrentBag<ulong>();

        public void Add(string connection, SessionInfo info)
        {
            _infos.GetOrAdd(connection, info.playerNo);
            var oldInfo = _playerNoInfos.GetOrAdd(info.playerNo, info);
            oldInfo.SetInfo(info);
        }

        public void ReserveRemove(SessionInfo sessionInfo)
        {
            sessionInfo.IsReserveForDelete = true;
        }

        // 지우는 것은 타이머에서만 지운다
        private void Remove(ulong playerNo)
        {
            _playerNoInfos.TryRemove(playerNo, out SessionInfo info);
            _infos.TryRemove(info.connectionId, out ulong removed);
        }

        public void RemoveAll()
        {
            foreach (var item in _playerNoInfos)
            {
                Remove(item.Key);
            }
        }

        public bool GetSessionInfo(string connection, out SessionInfo info)
        {
            if (_infos.TryGetValue(connection, out ulong playerNo) == false)
            {
                info = null;
                return false;
            }
            if (_playerNoInfos.TryGetValue(playerNo, out info) == false)
            {
                return false;
            }
            info.UpdatedTime = SgTime.I.Now;
            return true;
        }

        public bool GetSessionInfo(ulong playerNo, out SessionInfo info)
        {
            if (_playerNoInfos.TryGetValue(playerNo, out info) == false)
            {
                return false;
            }
            info.UpdatedTime = SgTime.I.Now;
            return true;
        }

        public List<SessionInfo> GetSessionList()
        {
            return _playerNoInfos.Values.ToList();
        }

        public int GetSessionCount()
        {
            return _playerNoInfos.Count;
        }

        public void ChangeConnection(ref SessionInfo info, HubCallerContext hubContext, string connectionId, long now)
        {
            // 기존 connectionId로 들어있던 부분은 삭제
            var oldConnectionId = info.connectionId;
            _infos.TryRemove(oldConnectionId, out ulong removedPlayerNo);

            // 내부 갱신후
            info.connectionId = connectionId;
            info.hubContext = hubContext;
            info.UpdatedTime = now;
            info.SessionOffset++;

            // 새로 추가
            _infos.TryAdd(connectionId, info.playerNo);
        }
    }
}
