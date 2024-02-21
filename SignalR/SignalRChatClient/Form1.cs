using Microsoft.AspNetCore.SignalR.Client;
using Protocol;
using SignalRChat;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms;

namespace SignalRChatClient
{
    public partial class Form1 : Form
    {
        private BackgroundWorker workerUserCount;
        private WebAPI webAPI = null;
        private GameHub gameHub = null;
        private bool isChatRoomIdSel = false;
        private int cbChatRoomIdIndex = -1;

        public Form1()
        {
            InitializeComponent();
        }

        // 화면 깜빡임 방지
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            webAPI = new WebAPI(txtURL.Text);

            for (int i = 1; i <= 10; i++)
            {
                cbChatRoomId.Items.Add(i.ToString());
            }
            cbChatRoomId.SelectedIndex = 0;

            txtFpID.Text = GlobalValues.FpID;
            ActiveControl = txtFpID;

            workerUserCount = new BackgroundWorker();
            workerUserCount.DoWork += new DoWorkEventHandler(worker_DoUserCount);
            workerUserCount.WorkerSupportsCancellation = true;
        }

        private void ServerConnectState()
        {
            txtFpID.ReadOnly = true;
            btnUserJoin.Enabled = false;
            btnServerConnect.Enabled = false;
            btnServerDisconnect.Enabled = true;
            txtNickname.ReadOnly = false;
            btnSetNickname.Enabled = true;
            txtUserCount.Text = "";

            if (!string.IsNullOrEmpty(txtNickname.Text))
            {
                isChatRoomIdSel = true;
                cbChatRoomId.DropDownStyle = ComboBoxStyle.DropDown;
                cbChatRoomId.SelectedIndex = -1;
                cbChatRoomIdIndex = -1;
                btnRoomEnter.Enabled = true;
                btnRoomLeave.Enabled = false;
                txtReqChatMsg.ReadOnly = false;
                btnRoomChat.Enabled = true;
            }

            ActiveControl = txtNickname;

            workerUserCount.RunWorkerAsync(10000);
        }

        private void ServerDisconnectState()
        {
            txtFpID.ReadOnly = false;
            btnUserJoin.Enabled = true;
            btnServerConnect.Enabled = true;
            btnServerDisconnect.Enabled = false;

            txtNickname.Text = "";
            txtNickname.ReadOnly = true;
            btnSetNickname.Enabled = false;
            txtUserCount.Text = "";

            isChatRoomIdSel = false;
            cbChatRoomId.DropDownStyle = ComboBoxStyle.DropDownList;
            cbChatRoomId.SelectedIndex = -1;
            cbChatRoomIdIndex = -1;
            btnRoomEnter.Enabled = false;
            btnRoomLeave.Enabled = true;
            txtRoomUserCount.Text = "";
            txtRoomUserCount.ReadOnly = true;
            txtReqChatMsg.ReadOnly = true;
            btnRoomChat.Enabled = false;

            ActiveControl = txtFpID;

            workerUserCount.CancelAsync();
        }

        private void ChatRoomEnterState()
        {
            isChatRoomIdSel = false;
            cbChatRoomId.DropDownStyle = ComboBoxStyle.DropDownList;
            cbChatRoomIdIndex = cbChatRoomId.SelectedIndex;
            GlobalValues.RoomId = Convert.ToInt32(cbChatRoomId.SelectedItem);
            btnRoomEnter.Enabled = false;
            btnRoomLeave.Enabled = true;
            txtRoomUserCount.Text = "";
            txtReqChatMsg.Text = "";
            txtReqChatMsg.ReadOnly = false;
            btnRoomChat.Enabled = true;
            listBoxChat.Enabled = true;
        }

        private void ChatRoomLeaveState()
        {
            isChatRoomIdSel = true;
            cbChatRoomId.DropDownStyle = ComboBoxStyle.DropDown;
            cbChatRoomId.SelectedIndex = -1;
            cbChatRoomIdIndex = -1;
            GlobalValues.RoomId = 0;
            btnRoomEnter.Enabled = true;
            btnRoomLeave.Enabled = false;
            txtRoomUserCount.Text = "";
            txtReqChatMsg.Text = "";
            txtReqChatMsg.ReadOnly = true;
            btnRoomChat.Enabled = false;
            listBoxChat.Items.Clear();
            listBoxChat.Enabled = false;
        }

        private async void btnUserJoin_Click(object sender, EventArgs e)
        {
            ReqGlobalJoin reqGlobalJoin = new ReqGlobalJoin()
            {
                Id = "",
                DeviceID = Guid.NewGuid().ToString()
            };

            string deviceID = Guid.NewGuid().ToString();

            AckGlobalJoin ackGlobalJoin = await webAPI.GlobalJoin(reqGlobalJoin);
            if (ackGlobalJoin.RetCode == ERROR_CODE_SPEC.Success)
            {
                txtFpID.Text = ackGlobalJoin.FpID;
            }
            else
            {
                MessageBox.Show("유저 가입 실패.", "알림");
                ActiveControl = txtFpID;
                return;
            }
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtFpID.Text))
            {
                MessageBox.Show("FpID를 입력하세요.", "알림");
                ActiveControl = txtFpID;
                return;
            }

            string deviceID = Guid.NewGuid().ToString();

            ReqGlobalLogin reqGlobalJoin = new ReqGlobalLogin()
            {
                FpID = txtFpID.Text,
                DeviceID = deviceID
            };

            AckGlobalLogin ackGlobalLogin = await webAPI.GlobalLogin(reqGlobalJoin);
            if (ackGlobalLogin.RetCode != ERROR_CODE_SPEC.Success)
            {
                MessageBox.Show("유저 로그인 실패.", "알림");
                ActiveControl = txtFpID;
                return;
            }
            listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Black, "[알림] 유저 로그인 성공"));

            gameHub = new GameHub(txtURL.Text);
            gameHub.GetHubConnection().Closed += HubClosed;
            gameHub.GetNetworkHubEvent().Event_RecvChatRoomNoti += OnEvent_RecvChatRoomNoti;
            gameHub.GetNetworkHubEvent().Event_RecvChatRoomMessage += OnEvent_RecvChatRoomMessage;
            gameHub.GetNetworkHubEvent().RegisterEvent();

            try
            {
                await gameHub.StartAsync();
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Black, "[알림] 채팅 서버 접속"));

                AckLogin ackLogin = await gameHub.GameReqLogin(txtFpID.Text);
                if (ackLogin.PlayerNo < 1)
                {
                    MessageBox.Show("채팅 서버 로그인 실패.", "알림");
                    ActiveControl = txtFpID;
                    return;
                }
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Black, "[알림] 채팅 서버 로그인 성공"));

                GlobalValues.PlayerNo = ackLogin.PlayerNo;
                GlobalValues.PlayerName = ackLogin.Name;

                if (!string.IsNullOrEmpty(ackLogin.Name))
                {
                    txtNickname.Text = ackLogin.Name;
                }
                else
                {
                    txtNickname.Text = "닉네임_" + ackLogin.PlayerNo;
                    AckSetNickname ackSetNickname = await gameHub.ReqSetNickname(txtNickname.Text);
                    if (ackSetNickname.RetCode == ERROR_CODE_SPEC.Success)
                    {
                        listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Black, "[알림] 닉네임 생성 성공"));
                        ChatRoomLeaveState();
                    }
                }

                ServerConnectState();
            }
            catch (Exception ex)
            {
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Red, ex.Message));
            }
        }

        private async void btnDisconnect_Click(object sender, EventArgs e)
        {
            await gameHub.StopAsync();
            listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Black, "[알림] 채팅 서버 접속 종료"));
            listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Black, "-----------------------------------------------------------------"));

            ServerDisconnectState();
        }

        private async void btnSetNickname_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtNickname.Text))
            {
                MessageBox.Show("닉네임을 입력하세요.", "알림");
                ActiveControl = txtNickname;
                return;
            }

            try
            {
                AckSetNickname ackSetNickname = await gameHub.ReqSetNickname(txtNickname.Text);
                if (ackSetNickname.RetCode != ERROR_CODE_SPEC.Success)
                {
                    if (ackSetNickname.RetCode == ERROR_CODE_SPEC.DuplicateNickName)
                    {
                        MessageBox.Show("중복 닉네임이 존재합니다.", "알림");
                    }
                    listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Black, "[알림] 닉네임 변경 실패"));
                    ActiveControl = txtNickname;
                    return;
                }
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Black, "[알림] 닉네임 변경 성공"));

                ChatRoomLeaveState();
            }
            catch (Exception ex)
            {
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Red, ex.Message));
            }
        }

        private void cbChatRoomId_Select(object sender, EventArgs e)
        {
            var cb = sender as ComboBox;
            if (!isChatRoomIdSel)
            {
                cb.SelectedIndex = cbChatRoomIdIndex;
            }
        }

        private async void btnRoomEnter_Click(object sender, EventArgs e)
        {
            if (cbChatRoomId.SelectedItem == null)
            {
                MessageBox.Show("참여할 채팅방을 선택하세요.", "알림");
                return;
            }

            int roomId = Convert.ToInt32(cbChatRoomId.SelectedItem);

            try
            {
                AckEnterChatRoom ackEnterChatRoom = await gameHub.ReqEnterChatRoom(roomId);
                if (ackEnterChatRoom.RetCode != ERROR_CODE_SPEC.Success)
                {
                    MessageBox.Show(ackEnterChatRoom.RetMessage, "알림");
                    return;
                }
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Black, string.Format("[알림] 채팅방{0} 참여 성공", roomId)));

                ChatRoomEnterState();
            }
            catch (Exception ex)
            {
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Red, ex.Message));
            }
        }

        private async void btnRoomLeave_Click(object sender, EventArgs e)
        {
            try
            {
                AckLeaveChatRoom ackLeaveChatRoom = await gameHub.ReqLeaveChatRoom();
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Black, string.Format("[알림] 채팅방{0} 퇴장 성공", ackLeaveChatRoom.RoomId)));
                GlobalValues.RoomId = 0;
                ChatRoomLeaveState();
            }
            catch (Exception ex)
            {
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Red, ex.Message));
            }
        }

        private async void btnRoomChat_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtReqChatMsg.Text))
            {
                MessageBox.Show("보낼 채팅 메세지를 입력하세요.", "알림");
                ActiveControl = txtReqChatMsg;
                return;
            }

            try
            {
                AckSendChatRoom ackSendChatRoom = await gameHub.ReqSendChatRoom(txtReqChatMsg.Text, 1);
            }
            catch (Exception ex)
            {
                listBoxMsg.Items.Add(new ListBoxItemColorMessageSet(Color.Red, ex.Message));
            }
        }
    }
}