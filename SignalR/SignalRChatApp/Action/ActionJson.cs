using Common.Lib;
using Microsoft.AspNetCore.SignalR.Client;
using Protocol;
using SignalRChat;
using System.Diagnostics;

namespace SignalRChatApp
{
    public class ActionJson : IActionBase
    {
        private readonly HubConnection _hub;

        // 코드 수행 시간 측정
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private ulong _agentId = 0;
        private long _lastActionTime = 0;
        private long _lastNowTime = 0;
        private int _maxCount = 0;
        private int _index = 0;
        private int _repeatCount = 0;

        public ActionJson(HubConnection hub, ulong agentId)
        {
            _hub = hub;

            _maxCount = TestPacketManager.Instance.GetActionSize();
            _index = 0;
            _lastActionTime = 0;
            _lastNowTime = 0;
            _repeatCount = 0;
            _agentId = agentId;
        }

        public override bool CanAction()
        {
            // 반복횟수가 하나라도 있으면 더이상 액션을 할 수 없는것으로 판단한다
            return _repeatCount <= 0;
        }

        public override async Task DoActions(Agent agent)
        {
            if (_maxCount == 0)
            {
                return;
            }

            while (true)
            {
                var action = TestPacketManager.Instance.GetActionData(_index);
                if (action == null)
                {
                    return;
                }

                if (_lastNowTime == 0)
                {
                    // 최초 패킷 실행시
                    _lastNowTime = SgTime.I.Now;
                }

                var realTimeDelta = SgTime.I.Now - _lastNowTime;
                var actionDelta = action.Time - _lastActionTime;
                if (actionDelta > realTimeDelta)
                {
                    // 아직 시간 안되었으므로 다음 시간으로 넘긴다
                    return;
                }

                _lastActionTime = action.Time;
                _lastNowTime = SgTime.I.Now;

                _stopwatch.Reset();
                _stopwatch.Start();

                if (action.Deserialized != null)
                {
                    // 2023.12.06 추가 : 채팅방 입장일 경우 방 번호 분배
                    if (action.PacketName.Equals("ReqEnterChatRoom"))
                        action.Deserialized = new ReqEnterChatRoom() { RoomId = ((int)_agentId % 10) };

                    if (_hub.State == HubConnectionState.Connected)
                        await _hub.InvokeAsync(action.PacketName, action.Deserialized);
                }
                else
                {
                    await _hub.InvokeAsync(action.PacketName);
                }
                _stopwatch.Stop();

                //_startTime에, stopwatch 밀린만큼 보정은 필요하다
                if (_agentId == 1)
                {
                    Console.WriteLine(_index + ": " + action.PacketName + ":" + _stopwatch.ElapsedMilliseconds);
                }

                _index++;
                if (_index >= _maxCount)
                {
                    Console.Write("C");

                    // 반복했었던 횟수 증가
                    _repeatCount++;

                    // 인덱스 초기화
                    _index = 0;
                    _lastActionTime = 0;
                    _lastNowTime = 0;
                    //break;
                }
            }
        }
    }
}
