using Newtonsoft.Json;
using Protocol;
using System.ComponentModel;
using System.Windows.Forms;

namespace SignalRChatClient
{
    public partial class Form1 : Form
    {
        async Task HubClosed(Exception ex)
        {
            await Task.Delay(2);

            Invoke((Action)(() =>
            {
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Red, "채팅 서버 연결이 끊어짐"));
                ChatRoomLeaveState();
                ServerDisconnectState();
            }));
        }

        void OnEvent_RecvChatRoomNoti(BCChatRoomRecv bcPacket)
        {
            if (bcPacket.Infos.Count > 0)
            {
                string msg = string.Format("[알림] {0}", bcPacket.Infos[0].Msg);
                object item = null;

                if (bcPacket.Infos[0].ChatRecvType == EChatRecvType.EnterChatRoom)
                {
                    item = new ListBoxItemColorMessageSet(Color.Blue, msg);
                }
                else if (bcPacket.Infos[0].ChatRecvType == EChatRecvType.LeaveChatRoom)
                {
                    item = new ListBoxItemColorMessageSet(Color.OrangeRed, msg);
                }

                // 크로스 스레드 작업이 잘못되었습니다.컨트롤이 자신이 만들어진 스레드가 아닌 스레드에서 액세스되었습니다.
                // 라는 에러가 디버깅 중 발생 문제 해결하기
                if (listBoxMsg.InvokeRequired)
                {
                    // 컨트롤의 내부 창 핸들이 있는 스레드에서 지정된 대리자를 실행하는 기능
                    // 즉 해당 스레드에서 직접 컨트롤의 내용을 변경시키는 것이 아니라, 해당 컨트롤의 스레드에 이걸 변경시켜 달라고 요청하는 역할을 수행
                    listBoxMsg.Invoke(new MethodInvoker(delegate {
                        listBoxMsg.Items.Add(item);
                    }));
                }
                else
                {
                    listBoxMsg.Items.Add(item);
                }
            }
        }

        void OnEvent_RecvChatRoomMessage(BCChatRoomRecv bcPacket)
        {
            if (bcPacket.Infos.Count > 0)
            {
                string msg = string.Format("{0} : {1}", bcPacket.Infos[0].Nickname, bcPacket.Infos[0].Msg);
                object item = null;

                if (GlobalValues.PlayerNo == bcPacket.Infos[0].PlayerNo)
                {
                    item = new ListBoxItemColorMessageSet(Color.Black, msg);
                }
                else
                {
                    item = new ListBoxItemColorMessageSet(Color.OrangeRed, msg);
                }

                // 크로스 스레드 작업이 잘못되었습니다.컨트롤이 자신이 만들어진 스레드가 아닌 스레드에서 액세스되었습니다.
                // 라는 에러가 디버깅 중 발생 문제 해결하기
                if (listBoxChat.InvokeRequired)
                {
                    // 컨트롤의 내부 창 핸들이 있는 스레드에서 지정된 대리자를 실행하는 기능
                    // 즉 해당 스레드에서 직접 컨트롤의 내용을 변경시키는 것이 아니라, 해당 컨트롤의 스레드에 이걸 변경시켜 달라고 요청하는 역할을 수행
                    listBoxChat.Invoke(new MethodInvoker(delegate {
                        listBoxChat.Items.Add(item);
                    }));
                }
                else
                {
                    listBoxChat.Items.Add(item);
                }
            }
        }

        private async void worker_DoUserCount(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            while (true)
            {
                if (worker.CancellationPending == true)
                {
                    e.Cancel = true;
                    break;
                }
                else
                {
                    int connectionCount = await webAPI.GetConnectionCount();

                    if (txtUserCount.InvokeRequired)
                        txtUserCount.Invoke(new MethodInvoker(delegate { txtUserCount.Text = connectionCount.ToString(); }));
                    else txtUserCount.Text = connectionCount.ToString();

                    if (GlobalValues.RoomId > 0)
                    {
                        int roomUserCount = 0;
                        string roomUserCountJson = await webAPI.GetChattingRoomUserCount(GlobalValues.RoomId);
                        if (!string.IsNullOrEmpty(roomUserCountJson))
                        {
                            Dictionary<int, int> roomUserCountInfos = JsonConvert.DeserializeObject<Dictionary<int, int>>(roomUserCountJson);
                            if (roomUserCountInfos != null && roomUserCountInfos.Count > 0)
                            {
                                if (!roomUserCountInfos.TryGetValue(GlobalValues.RoomId, out roomUserCount))
                                    roomUserCount = 0;
                            }

                            if (txtRoomUserCount.InvokeRequired)
                                txtRoomUserCount.Invoke(new MethodInvoker(delegate { txtRoomUserCount.Text = roomUserCount.ToString(); }));
                            else txtRoomUserCount.Text = roomUserCount.ToString();
                        }
                    }

                    System.Threading.Thread.Sleep((int)e.Argument);
                }
            }
        }
    }
}