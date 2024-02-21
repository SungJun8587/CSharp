
using Common.Lib;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Protocol;

namespace Server
{
    public partial class GameHub : Hub
    {
        // *************************************************************************
        // 재접속 처리
        // *************************************************************************
        public async Task<AckReconnect> ReqReconnect(ReqReconnect req)
        {
            var ack = new AckReconnect();

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                if (SgSession.Instance.GetSessionInfo(req.PlayerNo, out SessionInfo info) == false)
                {
                    // 세션이 날라갔는데 리커넥트 요청한 경우는 에러로 처리한다. 클라이언트에서도 로그인 과정을 거쳐야한다
                    throw new HubException("No PlayerNo, Need to Login again, PlayerNo :" + req.PlayerNo);
                }

                // 행여나 리커넥트를 SessionOffset을 다르게 요청한 케이스는 다른 기기에서 로그인을 시도했는데 불구하고 리커넥트를 시도한 경우이다
                // 이런 경우는 다시 로그인을 시도하도록 한다
                if (info.SessionOffset != req.SessionOffset)
                {
                    throw new HubException("SessionOffset is different, Need to Login again, PlayerNo :" + req.PlayerNo);
                }

                var player = Clients.Caller;
                var connectionId = Context.ConnectionId;
                if (SgConnInfo.Instance.GetConnectionInfo(connectionId, out ConnInfo connInfo) == true)
                {
                    connInfo.PlayerNo = info.playerNo;
                    // 커넥션 안지워지게 0으로 만든다
                    connInfo.AuthTime = 0;
                }

                var dbContext = scope.ServiceProvider.GetService<GameDBContext>();

                var playerDB = await dbContext.Players
                    .Where(b => b.PlayerNo == req.PlayerNo)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync();
                if (playerDB == null)
                {
                    ack.RetCode = ERROR_CODE_SPEC.NoPlayerDB;
                    ack.RetMessage = "PlayerNo:" + req.PlayerNo;
                    return ack;
                }

                // 커넥션 교체
                SgSession.Instance.ChangeConnection(ref info, Context, Context.ConnectionId, SgTime.I.Now);

                var pInfo = new PAllPlayerInfo();
                pInfo.PlayerNo = req.PlayerNo;
                pInfo.Name = playerDB.Name;

                ack.PlayerInfo = pInfo;
                ack.SessionOffset = info.SessionOffset;
            }

            return ack;
        }

        // *************************************************************************
        // 게임 가입 및 로그인
        // *************************************************************************
        public async Task<AckLogin> ReqLogin(string fpID)
        {
            uint serverGroupNo = 1;
            var ackLogin = new AckLogin();

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                bool isJoin = false;
                bool isNeedUpdate = false;

                // 1. fpID 유효성 체크
                if (string.IsNullOrEmpty(fpID) == true)
                {
                    ackLogin.RetCode = ERROR_CODE_SPEC.NoAccountDB;
                    ackLogin.RetMessage = ERROR_CODE_SPEC.NoAccountDB.ToString();
                    return ackLogin;
                }

                // 2. fpID로 유저 계정 번호(UserNo) 얻기
                if (SecurityUtility.GetUserNo(fpID, out ulong userNo) == false)
                {
                    ackLogin.RetCode = ERROR_CODE_SPEC.NoAccountDB;
                    ackLogin.RetMessage = ERROR_CODE_SPEC.NoAccountDB.ToString();
                    return ackLogin;
                }

                // 3. 규칙에 의해 playerNo 생성
                ulong playerNo = (1000 + ((ulong)serverGroupNo % 1000)) * 1000000000 + userNo;

                var now = SgTime.I.Now;
                var nowDateTime = SgTime.I.NowDateTime;

                // 4. 세션 선체크(다른 로직 하기전에, 쿨타임체크)
                if (SgSession.Instance.GetSessionInfo(playerNo, out SessionInfo sessionInfo) == true)
                {
                    // 5초 이내에 또 로그인이 오는경우
                    if (sessionInfo.LastLoginTime + SgTime.T_SECOND * 5 > now)
                    {
                        ackLogin.RetCode = ERROR_CODE_SPEC.SystemFrequentlyLogin;
                        ackLogin.RetMessage = ERROR_CODE_SPEC.SystemFrequentlyLogin.ToString();
                        return ackLogin;
                    }
                }

                // 5. 해당 유저가 플레이어를 생성했는지 체크
                var dbContext = scope.ServiceProvider.GetService<GameDBContext>();

                var playerDB = await dbContext.Players.Where(b => b.PlayerNo == playerNo).SingleOrDefaultAsync();
                if (playerDB == null)
                {
                    isJoin = true;
                    isNeedUpdate = true;

                    playerDB = new TPlayer
                    {
                        PlayerNo = playerNo,
                        UserNo = userNo,
                        Name = "닉네임_" + playerNo,
                        Icon = 1,
                        InsertDate = nowDateTime
                    };
                    dbContext.Players.Add(playerDB);
                }

                if (isNeedUpdate)
                {
                    await dbContext.SaveChangesAsync();
                }

                // 6. 해당 그룹에 가입 또는 로그인 로그 기록
                LogContainer logContainer = new LogContainer();

                if (isJoin)     // 가입
                {
                    logContainer.gameLogs.Add(new Log_Player_Register
                    {
                        InsertDate = Convert.ToUInt32(SgTime.I.NowDateTime.ToString("yyyyMMdd")),
                        PlayerNo = playerDB.PlayerNo,
                        UserNo = userNo,
                        InsertDateTime = nowDateTime
                    });

                    logContainer.gameLogs.Add(new Log_Player_Login
                    {
                        InsertDate = Convert.ToUInt32(SgTime.I.NowDateTime.ToString("yyyyMMdd")),
                        PlayerNo = playerDB.PlayerNo,
                        UserNo = userNo,
                        Name = playerDB.Name,
                        InsertDateTime = nowDateTime
                    });
                }
                else
                {
                    logContainer.gameLogs.Add(new Log_Player_Login
                    {
                        InsertDate = Convert.ToUInt32(SgTime.I.NowDateTime.ToString("yyyyMMdd")),
                        PlayerNo = playerDB.PlayerNo,
                        UserNo = userNo,
                        Name = playerDB.Name,
                        InsertDateTime = nowDateTime
                    });
                }

                _logger.Add(logContainer);

                ackLogin.PlayerNo = playerDB.PlayerNo;
                ackLogin.Name = playerDB.Name;
                ackLogin.Icon = playerDB.Icon;

                var player = Clients.Caller;
                var connectionId = Context.ConnectionId;
                if (sessionInfo != null)
                {
                    // 커넥션 교체
                    SgSession.Instance.ChangeConnection(
                        ref sessionInfo, Context, Context.ConnectionId, now);
                    sessionInfo.LastLoginTime = now;
                }
                else
                {
                    sessionInfo = new SessionInfo
                    {
                        hubContext = this.Context,
                        connectionId = connectionId,
                        userNo = userNo,
                        playerNo = playerNo,
                        Nickname = playerDB.Name,
                        Icon = playerDB.Icon,
                        UpdatedTime = now,
                        LastLoginTime = now
                    };
                    SgSession.Instance.Add(connectionId, sessionInfo);
                }

                if (SgConnInfo.Instance.GetConnectionInfo(connectionId, out ConnInfo connInfo) == true)
                {
                    connInfo.PlayerNo = playerNo;
                    // 커넥션 안지워지게 0으로 만든다
                    connInfo.AuthTime = 0;
                }

                ackLogin.RetCode = ERROR_CODE_SPEC.Success;
                ackLogin.RetMessage = ERROR_CODE_SPEC.Success.ToString();
            }

            return ackLogin;
        }

        // *************************************************************************
        // 게임 닉네임 설정
        // *************************************************************************
        public async Task<AckSetNickname> ReqSetNickname(string name)
        {
            var player = Clients.Caller;
            var connectionId = Context.ConnectionId;
            if (SgSession.Instance.GetSessionInfo(connectionId, out SessionInfo session) == false)
            {
                ErrorHelper.Throw(ERROR_CODE_SPEC.NoSession, "");
            }

            AckSetNickname ack = new AckSetNickname { Name = name };

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetService<GameDBContext>();

                var anotherPlayerDB = await dbContext.Players.Where(p => p.Name.Equals(name)).SingleOrDefaultAsync();
                if (anotherPlayerDB != null)
                {
                    ack.RetCode = ERROR_CODE_SPEC.DuplicateNickName;
                    ack.RetMessage = ERROR_CODE_SPEC.DuplicateNickName.ToString();
                    return ack;
                }

                var playerDB = await dbContext.Players
                    .Where(b => b.PlayerNo == session.playerNo)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync();
                if (playerDB == null)
                {
                    ack.RetCode = ERROR_CODE_SPEC.NoPlayerDB;
                    ack.RetMessage = "PlayerNo:" + session.playerNo;
                    return ack;
                }

                string prevNickname = session.Nickname;

                playerDB.Name = name;
                session.Nickname = name;

                try
                {
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception)
                {
                    session.Nickname = prevNickname;
                    ErrorHelper.Throw(ERROR_CODE_SPEC.FailedToSetNickname, "Failed to set PlayerNickname");
                }

                ack.RetCode = ERROR_CODE_SPEC.Success;
                ack.RetMessage = ERROR_CODE_SPEC.Success.ToString();
            }

            return ack;
        }

        // *************************************************************************
        // 플레이어 아이콘 설정
        // *************************************************************************
        public async Task<AckSetPlayerIcon> ReqSetPlayerIcon(uint iconTid)
        {
            var player = Clients.Caller;
            var connectionId = Context.ConnectionId;
            if (SgSession.Instance.GetSessionInfo(connectionId, out SessionInfo session) == false)
            {
                ErrorHelper.Throw(ERROR_CODE_SPEC.NoSession, "");
            }

            AckSetPlayerIcon ack = new AckSetPlayerIcon { Tid = iconTid };

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetService<GameDBContext>();

                var playerDB = await dbContext.Players
                    .Where(b => b.PlayerNo == session.playerNo)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync();
                if (playerDB == null)
                {
                    ack.RetCode = ERROR_CODE_SPEC.NoPlayerDB;
                    ack.RetMessage = "PlayerNo:" + session.playerNo;
                    return ack;
                }

                uint prevIcon = session.Icon;

                playerDB.Icon = iconTid;
                session.Icon = iconTid;

                try
                {
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception)
                {
                    session.Icon = prevIcon;
                    ErrorHelper.Throw(ERROR_CODE_SPEC.FailedToSetIcon, "Failed to set PlayerIcon");
                }

                ack.RetCode = ERROR_CODE_SPEC.Success;
                ack.RetMessage = ERROR_CODE_SPEC.Success.ToString();
            }

            return ack;
        }
    }
}