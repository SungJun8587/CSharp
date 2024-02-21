using Common.Lib;
using Microsoft.AspNetCore.SignalR;
using Protocol;

namespace Server
{
    public partial class GameHub : Hub
    {
        // *************************************************************************
        // 채팅방 참여
        // *************************************************************************
        public async Task<AckEnterChatRoom> ReqEnterChatRoom(ReqEnterChatRoom req)
        {
            var player = Clients.Caller;
            var connectionId = Context.ConnectionId;

            if (SgSession.Instance.GetSessionInfo(connectionId, out SessionInfo session) == false)
            {
                ErrorHelper.Throw(ERROR_CODE_SPEC.NoSession, "");
            }

            var ack = new AckEnterChatRoom
            {
                Infos = new List<PChatInfo>()
            };

            // 요청한 룸이 가용한 범위인지
            if (req.RoomId < 0 || req.RoomId > 10)
            {
                ack.RetCode = ERROR_CODE_SPEC.ChatNotAvailableRoom;
                ack.RetMessage = "Not Available RoomNo, Chat room IDs exist from 1 to 10";
                return ack;
            }

            // 현재 있는 방과 요청한 Room이 같아도 에러
            if (req.RoomId != 0 && req.RoomId == session.chatRoomId)
            {
                ack.RetCode = ERROR_CODE_SPEC.ChatSameRoomId;
                ack.RetMessage = "Not Available RoomNo, This user is already in the chat room";
                return ack;
            }

            int tryRoomNo = -1;
            if (req.RoomId == 0)
            {
                tryRoomNo = SgChatting.I.FindCandidateRoom();
            }
            else
            {
                // req.RoomId 방이 가용한지 체크 (느슨한 체크를 한다)
                bool canEnter = SgChatting.I.CanEnter(req.RoomId);
                if (canEnter == false)
                {
                    ack.RetCode = ERROR_CODE_SPEC.ChatFullRoom;
                    ack.RetMessage = "The chat room is full of people";
                    return ack;
                }
                tryRoomNo = req.RoomId;
            }

            if (tryRoomNo == -1)
            {
                ack.RetCode = ERROR_CODE_SPEC.ChatCanNotEnter;
                ack.RetMessage = "This user is not allowed to enter all chat rooms";
                return ack;
            }

            // 기존에 진입한 방이 있었다면 나오기
            if (session.chatRoomId > 0)
            {
                await SgChatting.I.LeaveChat(Clients, Groups, session);
            }

            // 진입
            await SgChatting.I.EnterChat(Clients, Groups, session, tryRoomNo);

            // 채널내 메세지 히스토리
            await SgChatting.I.GetMessages(session.chatRoomId, ack.Infos, req.LastReadId);

            return ack;
        }

        // *************************************************************************
        // 채팅방 메세지 전송
        // *************************************************************************
        public async Task<AckSendChatRoom> ReqSendChatRoom(ReqSendChatRoom req)
        {
            var player = Clients.Caller;
            var connectionId = Context.ConnectionId;

            if (SgSession.Instance.GetSessionInfo(connectionId, out SessionInfo session) == false)
            {
                ErrorHelper.Throw(ERROR_CODE_SPEC.NoSession, "");
            }

            var ack = new AckSendChatRoom
            {
            };

            if (session.chatRoomId == 0)
            {
                ack.RetCode = ERROR_CODE_SPEC.ChatNeedToEnter;
                ack.RetMessage = "This user is not participating in the chat room";
                return ack;
            }

            var now = SgTime.I.Now;

            var bcChannel = new BCChatRoomRecv
            {
                Infos = new List<PChatInfo>()
            };

            var info = new PChatInfo
            {
                ChatRecvType = EChatRecvType.SendChatRoom,
                PlayerNo = session.playerNo,
                Nickname = session.Nickname,
                Icon = session.Icon,
                Msg = req.Msg,
                Emoticon = req.Emoticon,
                Timestamp = now
            };

            // 큐에 저장하고
            await SgChatting.I.AddMessage(session.chatRoomId, info);

            // 패킷에 넣는다
            bcChannel.Infos.Add(info);

            await _hubContext.Clients.Group("ChannelChat:" + session.chatRoomId).SendAsync("BCRecvChatRoomMessage", bcChannel);

            return ack;
        }

        // *************************************************************************
        // 채팅방 퇴장
        // *************************************************************************
        public async Task<AckLeaveChatRoom> ReqLeaveChatRoom()
        {
            var player = Clients.Caller;
            var connectionId = Context.ConnectionId;

            if (SgSession.Instance.GetSessionInfo(connectionId, out SessionInfo session) == false)
            {
                ErrorHelper.Throw(ERROR_CODE_SPEC.NoSession, "");
            }

            var ack = new AckLeaveChatRoom
            {
            };

            // 기존에 진입한 방이 있었다면 나오기
            if (session.chatRoomId > 0)
            {
                ack.RoomId = session.chatRoomId;
                await SgChatting.I.LeaveChat(Clients, Groups, session);
            }

            return ack;
        }
    }
}