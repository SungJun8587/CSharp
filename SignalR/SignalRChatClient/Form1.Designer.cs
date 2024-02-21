using SignalRChat;
using System.Windows.Forms;

namespace SignalRChatClient
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            txtURL = new TextBox();
            label2 = new Label();
            txtFpID = new TextBox();
            btnUserJoin = new Button();
            btnServerConnect = new Button();
            btnServerDisconnect = new Button();
            label3 = new Label();
            txtNickname = new TextBox();
            btnSetNickname = new Button();
            label4 = new Label();
            txtUserCount = new TextBox();
            groupBox1 = new GroupBox();
            groupBox2 = new GroupBox();
            cbChatRoomId = new ComboBox();
            btnRoomEnter = new Button();
            btnRoomLeave = new Button();
            label5 = new Label();
            txtRoomUserCount = new TextBox();
            txtReqChatMsg = new TextBox();
            btnRoomChat = new Button();
            listBoxChat = new ListBox();
            listBoxMsg = new ListBox();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(11, 25);
            label1.Name = "label1";
            label1.Size = new Size(39, 15);
            label1.TabIndex = 1;
            label1.Text = "URL : ";
            // 
            // txtURL
            // 
            txtURL.Location = new Point(55, 20);
            txtURL.Margin = new Padding(3, 2, 3, 2);
            txtURL.Name = "txtURL";
            txtURL.ReadOnly = true;
            txtURL.Size = new Size(330, 23);
            txtURL.TabIndex = 2;
            txtURL.Text = "http://localhost:5000";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(11, 50);
            label2.Name = "label2";
            label2.Size = new Size(43, 15);
            label2.TabIndex = 3;
            label2.Text = "FpID : ";
            // 
            // txtFpID
            // 
            txtFpID.Location = new Point(55, 46);
            txtFpID.Margin = new Padding(3, 2, 3, 2);
            txtFpID.Name = "txtFpID";
            txtFpID.Size = new Size(330, 23);
            txtFpID.TabIndex = 4;
            // 
            // btnUserJoin
            // 
            btnUserJoin.Location = new Point(390, 19);
            btnUserJoin.Margin = new Padding(3, 2, 3, 2);
            btnUserJoin.Name = "btnUserJoin";
            btnUserJoin.Size = new Size(52, 52);
            btnUserJoin.TabIndex = 5;
            btnUserJoin.Text = "유저\n가입";
            btnUserJoin.UseVisualStyleBackColor = true;
            btnUserJoin.Click += btnUserJoin_Click;
            // 
            // btnServerConnect
            // 
            btnServerConnect.Location = new Point(445, 19);
            btnServerConnect.Margin = new Padding(3, 2, 3, 2);
            btnServerConnect.Name = "btnServerConnect";
            btnServerConnect.Size = new Size(52, 52);
            btnServerConnect.TabIndex = 6;
            btnServerConnect.Text = "연결";
            btnServerConnect.UseVisualStyleBackColor = true;
            btnServerConnect.Click += btnConnect_Click;
            // 
            // btnServerDisconnect
            // 
            btnServerDisconnect.Enabled = false;
            btnServerDisconnect.Location = new Point(500, 19);
            btnServerDisconnect.Margin = new Padding(3, 2, 3, 2);
            btnServerDisconnect.Name = "btnServerDisconnect";
            btnServerDisconnect.Size = new Size(52, 52);
            btnServerDisconnect.TabIndex = 7;
            btnServerDisconnect.Text = "끊기";
            btnServerDisconnect.UseVisualStyleBackColor = true;
            btnServerDisconnect.Click += btnDisconnect_Click;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(11, 78);
            label3.Name = "label3";
            label3.Size = new Size(54, 15);
            label3.TabIndex = 11;
            label3.Text = "닉네임 : ";
            // 
            // txtNickname
            // 
            txtNickname.Location = new Point(65, 73);
            txtNickname.Margin = new Padding(3, 2, 3, 2);
            txtNickname.Name = "txtNickname";
            txtNickname.ReadOnly = true;
            txtNickname.Size = new Size(150, 23);
            txtNickname.TabIndex = 9;
            // 
            // btnSetNickname
            // 
            btnSetNickname.Enabled = false;
            btnSetNickname.Location = new Point(220, 73);
            btnSetNickname.Margin = new Padding(3, 2, 3, 2);
            btnSetNickname.Name = "btnSetNickname";
            btnSetNickname.Size = new Size(100, 25);
            btnSetNickname.TabIndex = 10;
            btnSetNickname.Text = "닉네임 변경";
            btnSetNickname.UseVisualStyleBackColor = true;
            btnSetNickname.Click += btnSetNickname_Click;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(326, 77);
            label4.Name = "label4";
            label4.Size = new Size(66, 15);
            label4.TabIndex = 11;
            label4.Text = "동접자수 : ";
            // 
            // txtUserCount
            // 
            txtUserCount.Location = new Point(392, 73);
            txtUserCount.Margin = new Padding(3, 2, 3, 2);
            txtUserCount.Name = "txtUserCount";
            txtUserCount.ReadOnly = true;
            txtUserCount.Size = new Size(50, 23);
            txtUserCount.TabIndex = 12;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(label1);
            groupBox1.Controls.Add(txtURL);
            groupBox1.Controls.Add(label2);
            groupBox1.Controls.Add(txtFpID);
            groupBox1.Controls.Add(btnUserJoin);
            groupBox1.Controls.Add(btnServerConnect);
            groupBox1.Controls.Add(btnServerDisconnect);
            groupBox1.Controls.Add(label3);
            groupBox1.Controls.Add(txtNickname);
            groupBox1.Controls.Add(btnSetNickname);
            groupBox1.Controls.Add(label4);
            groupBox1.Controls.Add(txtUserCount);
            groupBox1.Location = new Point(10, 9);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(560, 110);
            groupBox1.TabIndex = 13;
            groupBox1.TabStop = false;
            groupBox1.Text = "서버 접속";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(cbChatRoomId);
            groupBox2.Controls.Add(btnRoomEnter);
            groupBox2.Controls.Add(btnRoomLeave);
            groupBox2.Controls.Add(label5);
            groupBox2.Controls.Add(txtRoomUserCount);
            groupBox2.Controls.Add(txtReqChatMsg);
            groupBox2.Controls.Add(btnRoomChat);
            groupBox2.Controls.Add(listBoxChat);
            groupBox2.Location = new Point(10, 125);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(560, 300);
            groupBox2.TabIndex = 12;
            groupBox2.TabStop = false;
            groupBox2.Text = "채팅방";
            // 
            // cbChatRoomId
            // 
            cbChatRoomId.DropDownStyle = ComboBoxStyle.DropDownList;
            cbChatRoomId.FormattingEnabled = true;
            cbChatRoomId.Location = new Point(11, 23);
            cbChatRoomId.Name = "cbChatRoomId";
            cbChatRoomId.Size = new Size(71, 23);
            cbChatRoomId.TabIndex = 13;
            cbChatRoomId.SelectedIndexChanged += cbChatRoomId_Select;
            // 
            // btnRoomEnter
            // 
            btnRoomEnter.Enabled = false;
            btnRoomEnter.Location = new Point(88, 23);
            btnRoomEnter.Margin = new Padding(3, 2, 3, 2);
            btnRoomEnter.Name = "btnRoomEnter";
            btnRoomEnter.Size = new Size(70, 25);
            btnRoomEnter.TabIndex = 14;
            btnRoomEnter.Text = "방 입장";
            btnRoomEnter.UseVisualStyleBackColor = true;
            btnRoomEnter.Click += btnRoomEnter_Click;
            // 
            // btnRoomLeave
            // 
            btnRoomLeave.Enabled = false;
            btnRoomLeave.Location = new Point(163, 23);
            btnRoomLeave.Margin = new Padding(3, 2, 3, 2);
            btnRoomLeave.Name = "btnRoomLeave";
            btnRoomLeave.Size = new Size(70, 25);
            btnRoomLeave.TabIndex = 15;
            btnRoomLeave.Text = "방 나가기";
            btnRoomLeave.UseVisualStyleBackColor = true;
            btnRoomLeave.Click += btnRoomLeave_Click;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(242, 28);
            label5.Name = "label5";
            label5.Size = new Size(70, 15);
            label5.TabIndex = 19;
            label5.Text = "방 유저수 : ";
            // 
            // txtRoomUserCount
            // 
            txtRoomUserCount.Location = new Point(312, 23);
            txtRoomUserCount.Name = "txtRoomUserCount";
            txtRoomUserCount.ReadOnly = true;
            txtRoomUserCount.Size = new Size(50, 23);
            txtRoomUserCount.TabIndex = 20;
            // 
            // txtReqChatMsg
            // 
            txtReqChatMsg.Location = new Point(10, 56);
            txtReqChatMsg.Margin = new Padding(3, 2, 3, 2);
            txtReqChatMsg.Name = "txtReqChatMsg";
            txtReqChatMsg.ReadOnly = true;
            txtReqChatMsg.Size = new Size(465, 23);
            txtReqChatMsg.TabIndex = 16;
            // 
            // btnRoomChat
            // 
            btnRoomChat.Enabled = false;
            btnRoomChat.Location = new Point(480, 55);
            btnRoomChat.Margin = new Padding(3, 2, 3, 2);
            btnRoomChat.Name = "btnRoomChat";
            btnRoomChat.Size = new Size(67, 26);
            btnRoomChat.TabIndex = 17;
            btnRoomChat.Text = "채팅";
            btnRoomChat.UseVisualStyleBackColor = true;
            btnRoomChat.Click += btnRoomChat_Click;
            // 
            // listBoxChat
            // 
            listBoxChat.DrawMode = DrawMode.OwnerDrawFixed;
            listBoxChat.Enabled = false;
            listBoxChat.FormattingEnabled = true;
            listBoxChat.ItemHeight = 15;
            listBoxChat.Location = new Point(10, 90);
            listBoxChat.Name = "listBoxChat";
            listBoxChat.ScrollAlwaysVisible = true;
            listBoxChat.Size = new Size(540, 199);
            listBoxChat.TabIndex = 18;
            listBoxChat.DrawItem += listBoxChat_DrawItem;
            // 
            // listBoxMsg
            // 
            listBoxMsg.DrawMode = DrawMode.OwnerDrawFixed;
            listBoxMsg.FormattingEnabled = true;
            listBoxMsg.ItemHeight = 15;
            listBoxMsg.Location = new Point(10, 430);
            listBoxMsg.Margin = new Padding(3, 4, 3, 4);
            listBoxMsg.Name = "listBoxMsg";
            listBoxMsg.Size = new Size(560, 139);
            listBoxMsg.TabIndex = 19;
            listBoxMsg.DrawItem += listBoxMsg_DrawItem;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(577, 583);
            Controls.Add(groupBox1);
            Controls.Add(groupBox2);
            Controls.Add(listBoxMsg);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Margin = new Padding(3, 2, 3, 2);
            Name = "Form1";
            Text = "SignalRChatClient";
            Load += Form1_Load;
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ResumeLayout(false);
        }

        #endregion
        private GroupBox groupBox1;
        private Label label1;
        private TextBox txtURL;
        private Label label2;
        private TextBox txtFpID;
        private Button btnUserJoin;
        private Button btnServerConnect;
        private Button btnServerDisconnect;
        private Label label3;
        private TextBox txtNickname;
        private Button btnSetNickname;
        private Label label4;
        private TextBox txtUserCount;
        private GroupBox groupBox2;
        private ComboBox cbChatRoomId;
        private Button btnRoomEnter;
        private Button btnRoomLeave;
        private Label label5;
        private TextBox txtRoomUserCount;
        private TextBox txtReqChatMsg;
        private Button btnRoomChat;
        private ListBox listBoxChat;
        private ListBox listBoxMsg;
    }
}