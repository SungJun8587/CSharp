using System.Collections.Concurrent;
using Common.Lib;
using Microsoft.AspNetCore.SignalR;

namespace Server
{
    /// <summary>Connection 정보</summary>
    public class ConnInfo
    {
        /// <summary>허브 호출자 연결에 대한 정보에 액세스하기 위한 컨텍스트</summary>
        public HubCallerContext hubContext;

        /// <summary>플레이어 번호</summary>
        public ulong PlayerNo { get; set; }

        /// <summary>접속 시간 플래그
        ///     - 로그인 요청(ReqLogin)이나 재접속 요청(ReqReconnect)을 통해 0으로 설정
        ///     - SgConnInfo에 RepeatAsync() 함수가 10초에 한번씩 체크하면서 0보다 크고, 현재 시간 값보다 작은 경우 삭제 처리
        /// </summary>
        public long AuthTime { get; set; }
    }

    /// <summary>Connection 정보 관리
    ///     - 클라이언트 Connection 정보를 10초에 한번씩 체크하면서 0보다 크고, 현재 시간 값보다 작은 경우 삭제 처리
    ///     - 클라이언트에 접속이 끊어지면, 바로 삭제
    /// </summary>
    public class SgConnInfo
    {
        private static readonly Lazy<SgConnInfo> instanceHolder =
            new Lazy<SgConnInfo>(() => new SgConnInfo());

#nullable enable
        private Task? _timerTask;
#nullable restore
        private readonly PeriodicTimer _timer;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public SgConnInfo()
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

                        foreach (var pair in _infos)
                        {
                            var info = pair.Value;

                            // 접속 시간 플래그가 0보다 크고, 현재 시간 값보다 작은 경우 삭제 처리
                            if (info.AuthTime > 0 && info.AuthTime < SgTime.I.Now)
                            {
                                _removeCandidates.Add(pair.Key);
                            }
                        }

                        foreach (var connectionId in _removeCandidates)
                        {
                            if (_infos.TryRemove(connectionId, out ConnInfo info) == true)
                            {
                                // 해당커넥션을 Disconnect한다
                                info.hubContext.Abort();
                            }
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

        public static SgConnInfo Instance
        {
            get { return instanceHolder.Value; }
        }

        // Add - 로그인, Remove : 클라이언트 접속이 끊어졌을 때
        // ConnInfo 맵 - Key:connectionId, Value:ConnInfo
        private ConcurrentDictionary<string, ConnInfo> _infos =
            new ConcurrentDictionary<string, ConnInfo>();

        private ConcurrentBag<string> _removeCandidates = new ConcurrentBag<string>();

        public void Add(string connectionId, HubCallerContext context)
        {
            _infos.GetOrAdd(connectionId, new ConnInfo
            {
                // 로그인 요청(ReqLogin)이나 재접속 요청(ReqReconnect)까지 5초의 시간을 준다
                // 이 안에 AuthTime이 0이 되지 않으면, 해당 유저는 Invalid한 유저로 간주하고 disconnect된다
                AuthTime = SgTime.I.Now + (SgTime.T_SECOND * 5),
                hubContext = context
            });
        }

        public void Remove(string connectionId)
        {
            // AuthTime에 의해서 이미 강제로 지워지면서 블럭된 경우는 이미 지워졌을수도 있다
            _infos.TryRemove(connectionId, out ConnInfo info);
        }

        public void RemoveAll()
        {
            foreach (var item in _infos)
            {
                Remove(item.Key);
            }
        }

        public bool GetConnectionInfo(string connectionId, out ConnInfo info)
        {
            if (_infos.TryGetValue(connectionId, out info) == false)
            {
                return false;
            }
            return true;
        }

        public int GetConnectionCount()
        {
            return _infos.Count;
        }

        public List<ConnInfo> GetConnectionList()
        {
            return _infos.Values.ToList();
        }        
    }
}