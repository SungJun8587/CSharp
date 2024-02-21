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
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Red, "ä�� ���� ������ ������"));
                ChatRoomLeaveState();
                ServerDisconnectState();
            }));
        }

        void OnEvent_RecvChatRoomNoti(BCChatRoomRecv bcPacket)
        {
            if (bcPacket.Infos.Count > 0)
            {
                string msg = string.Format("[�˸�] {0}", bcPacket.Infos[0].Msg);
                object item = null;

                if (bcPacket.Infos[0].ChatRecvType == EChatRecvType.EnterChatRoom)
                {
                    item = new ListBoxItemColorMessageSet(Color.Blue, msg);
                }
                else if (bcPacket.Infos[0].ChatRecvType == EChatRecvType.LeaveChatRoom)
                {
                    item = new ListBoxItemColorMessageSet(Color.OrangeRed, msg);
                }

                // ũ�ν� ������ �۾��� �߸��Ǿ����ϴ�.��Ʈ���� �ڽ��� ������� �����尡 �ƴ� �����忡�� �׼����Ǿ����ϴ�.
                // ��� ������ ����� �� �߻� ���� �ذ��ϱ�
                if (listBoxMsg.InvokeRequired)
                {
                    // ��Ʈ���� ���� â �ڵ��� �ִ� �����忡�� ������ �븮�ڸ� �����ϴ� ���
                    // �� �ش� �����忡�� ���� ��Ʈ���� ������ �����Ű�� ���� �ƴ϶�, �ش� ��Ʈ���� �����忡 �̰� ������� �޶�� ��û�ϴ� ������ ����
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

                // ũ�ν� ������ �۾��� �߸��Ǿ����ϴ�.��Ʈ���� �ڽ��� ������� �����尡 �ƴ� �����忡�� �׼����Ǿ����ϴ�.
                // ��� ������ ����� �� �߻� ���� �ذ��ϱ�
                if (listBoxChat.InvokeRequired)
                {
                    // ��Ʈ���� ���� â �ڵ��� �ִ� �����忡�� ������ �븮�ڸ� �����ϴ� ���
                    // �� �ش� �����忡�� ���� ��Ʈ���� ������ �����Ű�� ���� �ƴ϶�, �ش� ��Ʈ���� �����忡 �̰� ������� �޶�� ��û�ϴ� ������ ����
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