using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using UdtSharp;
using p2pcopy;

namespace p2pconn
{
    public partial class Form1 : Form
    {
        #region Tab 控件
        private TableLayoutPanel mainLayout;
        private TabControl tabControl;
        private TabPage tabConnect, tabAbout;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusBarLabel;
        private System.Windows.Forms.Timer statusTimer;
        private DateTime startTime;
        private NotifyIcon notifyIcon;
        private bool allowRealClose = false;
        #endregion

        #region Tab 1 - 连接
        private GroupBox grpWebServer;
        private Panel pnlStatusDot;
        private Label lblStatusText, lblWebUrl, lblClientCount, lblLocalIp;
        private NumericUpDown nudPort;
        private Button btnStartStop, btnCopyUrl;

        private GroupBox grpP2P;
        private Label lblP2PStatus, lblPeerName;
        private TextBox txtMyEndpoint, txtPeerAddress;
        private Button btnP2PConnect, btnP2PDisconnect;
        private System.Threading.Thread p2pThread = null;
        private bool p2pConnected = false;

        private TextBox txtPublicIp, txtPublicUrl;
        private Label lblNatType;
        private Button btnCopyIp, btnOpenUrl;
        #endregion

        #region Tab 2 - 关于
        private GroupBox grpAbout;
        private Label lblVersion, lblUptime;
        private GroupBox grpHardware;
        private TextBox txtHardwareInfo;
        private Button btnRefreshHw;
        #endregion

        public Form1()
        {
            startTime = DateTime.Now;
            BuildForm();
            AttachEvents();

            // 自动启动服务器
            StartServer();
        }

        private void BuildForm()
        {
            // === 窗口基本属性 ===
            this.Text = "P2P Remote Desktop v1.0";
            this.Size = new Size(620, 500);
            this.MinimumSize = new Size(520, 420);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 加载图标
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "p2p.ico");
                if (!File.Exists(iconPath))
                    iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Resources", "p2p.ico");
                if (File.Exists(iconPath))
                    this.Icon = new Icon(iconPath);
            }
            catch { }

            // 系统托盘
            notifyIcon = new NotifyIcon();
            notifyIcon.Text = "P2P Remote Desktop";
            try
            {
                if (this.Icon != null) notifyIcon.Icon = this.Icon;
            }
            catch { }
            notifyIcon.Visible = false;
            notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            };

            // 托盘右键菜单
            var trayMenu = new ContextMenuStrip();
            var showItem = trayMenu.Items.Add("显示主窗口");
            showItem.Click += (s, e) =>
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            };
            trayMenu.Items.Add("-");
            var exitItem = trayMenu.Items.Add("退出");
            exitItem.Click += (s, e) =>
            {
                allowRealClose = true;
                notifyIcon.Visible = false;
                WebSocketServer.Stop();
                Application.Exit();
            };
            notifyIcon.ContextMenuStrip = trayMenu;

            // === 主布局 ===
            mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(8)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

            // === TabControl ===
            tabControl = new TabControl { Dock = DockStyle.Fill };
            tabControl.Appearance = TabAppearance.Normal;

            BuildConnectTab();
            BuildAboutTab();

            tabControl.TabPages.AddRange(new TabPage[] { tabConnect, tabAbout });
            mainLayout.Controls.Add(tabControl, 0, 0);

            // === 状态栏 ===
            statusStrip = new StatusStrip { SizingGrip = false };
            statusBarLabel = new ToolStripStatusLabel("就绪");
            statusStrip.Items.Add(statusBarLabel);
            mainLayout.Controls.Add(statusStrip, 0, 1);

            this.Controls.Add(mainLayout);
        }

        #region Tab 1 - 连接管理
        private void BuildConnectTab()
        {
            tabConnect = new TabPage("连接");
            tabConnect.BackColor = SystemColors.Control;
            tabConnect.Padding = new Padding(12);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // --- Web 服务器分组 ---
            grpWebServer = new GroupBox
            {
                Text = "Web 服务器",
                Dock = DockStyle.Top,
                Height = 320,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold)
            };

            int y = 28;
            int leftCol = 16;
            int rightCol = 140;

            // 状态指示灯
            pnlStatusDot = new Panel
            {
                Location = new Point(leftCol, y),
                Size = new Size(14, 14),
                BackColor = Color.Red
            };
            pnlStatusDot.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(pnlStatusDot.BackColor))
                    g.FillEllipse(brush, 1, 1, 12, 12);
            };
            grpWebServer.Controls.Add(pnlStatusDot);

            lblStatusText = new Label
            {
                Location = new Point(leftCol + 20, y - 2),
                Size = new Size(260, 20),
                Text = "服务器已停止",
                Font = new Font("Microsoft YaHei", 9F)
            };
            grpWebServer.Controls.Add(lblStatusText);

            y += 30;

            // 端口
            var lblPort = new Label
            {
                Location = new Point(leftCol, y + 3),
                Text = "监听端口:",
                Size = new Size(70, 20)
            };
            grpWebServer.Controls.Add(lblPort);

            nudPort = new NumericUpDown
            {
                Location = new Point(rightCol, y),
                Size = new Size(70, 22),
                Minimum = 80,
                Maximum = 65535,
                Value = 8080
            };
            grpWebServer.Controls.Add(nudPort);

            y += 35;

            // Web 访问地址
            var lblUrlTitle = new Label
            {
                Location = new Point(leftCol, y + 3),
                Text = "Web 访问:",
                Size = new Size(70, 20)
            };
            grpWebServer.Controls.Add(lblUrlTitle);

            lblWebUrl = new Label
            {
                Location = new Point(rightCol, y + 3),
                Size = new Size(360, 20),
                Text = "http://0.0.0.0:8080/",
                ForeColor = Color.Blue,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Underline),
                Cursor = Cursors.Hand
            };
            lblWebUrl.Click += (s, e) => System.Diagnostics.Process.Start(lblWebUrl.Text);
            grpWebServer.Controls.Add(lblWebUrl);

            y += 28;

            // 客户端数
            var lblClientTitle = new Label
            {
                Location = new Point(leftCol, y + 3),
                Text = "Web 客户端:",
                Size = new Size(80, 20)
            };
            grpWebServer.Controls.Add(lblClientTitle);

            lblClientCount = new Label
            {
                Location = new Point(rightCol, y + 3),
                Size = new Size(200, 20),
                Text = "0 个已连接"
            };
            grpWebServer.Controls.Add(lblClientCount);

            y += 35;

            // 启动/停止按钮
            btnStartStop = new Button
            {
                Location = new Point(leftCol, y),
                Size = new Size(110, 32),
                Text = "启动服务器",
                BackColor = Color.FromArgb(76, 175, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold)
            };
            btnStartStop.FlatAppearance.BorderSize = 0;
            grpWebServer.Controls.Add(btnStartStop);

            btnCopyUrl = new Button
            {
                Location = new Point(leftCol + 120, y),
                Size = new Size(80, 32),
                Text = "复制链接",
                FlatStyle = FlatStyle.System
            };
            grpWebServer.Controls.Add(btnCopyUrl);

            // 本地IP
            lblLocalIp = new Label
            {
                Location = new Point(leftCol, y + 40),
                Size = new Size(500, 20),
                Text = "本地IP: " + GetLocalIPString(),
                ForeColor = Color.Gray
            };
            grpWebServer.Controls.Add(lblLocalIp);

            y += 70;

            // --- 公网信息 (STUN) ---
            var lblPubIpTitle = new Label
            {
                Location = new Point(leftCol, y),
                Size = new Size(70, 20),
                Text = "公网 IP:",
                Font = new Font("Microsoft YaHei", 9F)
            };
            grpWebServer.Controls.Add(lblPubIpTitle);

            txtPublicIp = new TextBox
            {
                Location = new Point(rightCol, y),
                Size = new Size(260, 20),
                Text = "检测中...",
                ForeColor = Color.Gray,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control
            };
            grpWebServer.Controls.Add(txtPublicIp);

            y += 22;

            var lblNatTitle = new Label
            {
                Location = new Point(leftCol, y),
                Size = new Size(70, 20),
                Text = "NAT 类型:",
                Font = new Font("Microsoft YaHei", 9F)
            };
            grpWebServer.Controls.Add(lblNatTitle);

            lblNatType = new Label
            {
                Location = new Point(rightCol, y),
                Size = new Size(300, 20),
                Text = "检测中...",
                ForeColor = Color.Gray
            };
            grpWebServer.Controls.Add(lblNatType);

            y += 22;

            var lblPubUrlTitle = new Label
            {
                Location = new Point(leftCol, y),
                Size = new Size(70, 20),
                Text = "公网访问:",
                Font = new Font("Microsoft YaHei", 9F)
            };
            grpWebServer.Controls.Add(lblPubUrlTitle);

            txtPublicUrl = new TextBox
            {
                Location = new Point(rightCol, y),
                Size = new Size(300, 20),
                Text = "等待检测...",
                ForeColor = Color.Gray,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control
            };
            grpWebServer.Controls.Add(txtPublicUrl);

            // "打开" 按钮
            btnOpenUrl = new Button
            {
                Location = new Point(rightCol + 310, y - 2),
                Size = new Size(50, 23),
                Text = "打开",
                Enabled = false,
                FlatStyle = FlatStyle.System
            };
            btnOpenUrl.Click += (s, e) =>
            {
                try
                {
                    if (txtPublicUrl.Text.StartsWith("http"))
                        System.Diagnostics.Process.Start(txtPublicUrl.Text);
                }
                catch { }
            };
            grpWebServer.Controls.Add(btnOpenUrl);

            // "复制IP" 按钮
            btnCopyIp = new Button
            {
                Location = new Point(rightCol + 270, txtPublicIp.Top - 2),
                Size = new Size(50, 23),
                Text = "复制",
                Enabled = false,
                FlatStyle = FlatStyle.System
            };
            btnCopyIp.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(txtPublicIp.Text) && txtPublicIp.Text != "检测中..." && txtPublicIp.Text != "检测失败" && txtPublicIp.Text != "检测异常")
                    Clipboard.SetText(txtPublicIp.Text);
            };
            grpWebServer.Controls.Add(btnCopyIp);

            layout.Controls.Add(grpWebServer, 0, 0);

            // --- P2P 连接分组 ---
            grpP2P = new GroupBox
            {
                Text = "P2P 连接",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold),
                Margin = new Padding(0, 10, 0, 0)
            };

            int py = 28;
            int pLeft = 16, pRight = 140;

            // 本机端点
            var lblMyEp = new Label
            {
                Location = new Point(pLeft, py + 3),
                Size = new Size(70, 20),
                Text = "本机端点:",
                Font = new Font("Microsoft YaHei", 9F)
            };
            grpP2P.Controls.Add(lblMyEp);

            txtMyEndpoint = new TextBox
            {
                Location = new Point(pRight, py),
                Size = new Size(200, 20),
                Text = "等待检测...",
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold)
            };
            grpP2P.Controls.Add(txtMyEndpoint);

            var btnCopyEp = new Button
            {
                Location = new Point(pRight + 210, py - 2),
                Size = new Size(50, 23),
                Text = "复制",
                FlatStyle = FlatStyle.System
            };
            btnCopyEp.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(txtMyEndpoint.Text) && txtMyEndpoint.Text != "等待检测...")
                    Clipboard.SetText(txtMyEndpoint.Text);
            };
            grpP2P.Controls.Add(btnCopyEp);

            py += 30;

            // 对端地址
            var lblPeerAddr = new Label
            {
                Location = new Point(pLeft, py + 3),
                Size = new Size(70, 20),
                Text = "对端地址:",
                Font = new Font("Microsoft YaHei", 9F)
            };
            grpP2P.Controls.Add(lblPeerAddr);

            txtPeerAddress = new TextBox
            {
                Location = new Point(pRight, py),
                Size = new Size(220, 20),
                Text = "例: 113.108.x.x:9000",
                ForeColor = Color.Gray,
                Font = new Font("Microsoft YaHei", 9F)
            };
            txtPeerAddress.Enter += (s, e) =>
            {
                if (txtPeerAddress.Text == "例: 113.108.x.x:9000")
                {
                    txtPeerAddress.Text = "";
                    txtPeerAddress.ForeColor = Color.Black;
                }
            };
            txtPeerAddress.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtPeerAddress.Text))
                {
                    txtPeerAddress.Text = "例: 113.108.x.x:9000";
                    txtPeerAddress.ForeColor = Color.Gray;
                }
            };
            grpP2P.Controls.Add(txtPeerAddress);

            py += 32;

            // 连接/断开按钮
            btnP2PConnect = new Button
            {
                Location = new Point(pLeft, py),
                Size = new Size(100, 30),
                Text = "发起连接",
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold)
            };
            btnP2PConnect.FlatAppearance.BorderSize = 0;
            btnP2PConnect.Click += BtnP2PConnect_Click;
            grpP2P.Controls.Add(btnP2PConnect);

            btnP2PDisconnect = new Button
            {
                Location = new Point(pLeft + 110, py),
                Size = new Size(100, 30),
                Text = "断开连接",
                Enabled = false,
                FlatStyle = FlatStyle.System,
                Font = new Font("Microsoft YaHei", 9F)
            };
            btnP2PDisconnect.Click += BtnP2PDisconnect_Click;
            grpP2P.Controls.Add(btnP2PDisconnect);

            py += 40;

            // 连接状态
            var lblStTitle = new Label
            {
                Location = new Point(pLeft, py + 3),
                Size = new Size(70, 20),
                Text = "连接状态:",
                Font = new Font("Microsoft YaHei", 9F)
            };
            grpP2P.Controls.Add(lblStTitle);

            lblP2PStatus = new Label
            {
                Location = new Point(pRight, py + 3),
                Size = new Size(300, 20),
                Text = "未连接",
                ForeColor = Color.Gray
            };
            grpP2P.Controls.Add(lblP2PStatus);

            py += 22;

            // 对端名称
            var lblPnTitle = new Label
            {
                Location = new Point(pLeft, py + 3),
                Size = new Size(70, 20),
                Text = "对端名称:",
                Font = new Font("Microsoft YaHei", 9F)
            };
            grpP2P.Controls.Add(lblPnTitle);

            lblPeerName = new Label
            {
                Location = new Point(pRight, py + 3),
                Size = new Size(300, 20),
                Text = "--",
                ForeColor = Color.Gray
            };
            grpP2P.Controls.Add(lblPeerName);

            layout.Controls.Add(grpWebServer, 0, 0);
            layout.Controls.Add(grpP2P, 0, 1);

            tabConnect.Controls.Add(layout);
        }
        #endregion

        #region Tab 2 - 关于
        private void BuildAboutTab()
        {
            tabAbout = new TabPage("关于");
            tabAbout.BackColor = SystemColors.Control;
            tabAbout.Padding = new Padding(12);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // --- 软件信息分组 ---
            grpAbout = new GroupBox
            {
                Text = "软件信息",
                Dock = DockStyle.Top,
                Height = 95,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold)
            };

            var appName = new Label
            {
                Location = new Point(16, 24),
                Size = new Size(300, 22),
                Text = "P2P Remote Desktop Server v1.0",
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold)
            };
            grpAbout.Controls.Add(appName);

            lblVersion = new Label
            {
                Location = new Point(16, 48),
                Size = new Size(400, 20),
                Text = ".NET Framework: " + Environment.Version.ToString() + "   |   架构: " +
                       (Environment.Is64BitProcess ? "x64" : "x86")
            };
            grpAbout.Controls.Add(lblVersion);

            lblUptime = new Label
            {
                Location = new Point(16, 68),
                Size = new Size(250, 20),
                Text = "运行时间: 00:00:00"
            };
            grpAbout.Controls.Add(lblUptime);

            layout.Controls.Add(grpAbout, 0, 0);

            // --- 硬件信息分组 ---
            grpHardware = new GroupBox
            {
                Text = "设备硬件信息",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold),
                Margin = new Padding(0, 10, 0, 0)
            };

            var hwLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(8)
            };
            hwLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            hwLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            txtHardwareInfo = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font("Consolas", 9F),
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle
            };
            hwLayout.Controls.Add(txtHardwareInfo, 0, 0);

            btnRefreshHw = new Button
            {
                Text = "刷新硬件信息",
                Size = new Size(120, 28),
                Anchor = AnchorStyles.Left
            };
            hwLayout.Controls.Add(btnRefreshHw, 0, 1);

            grpHardware.Controls.Add(hwLayout);
            layout.Controls.Add(grpHardware, 0, 1);

            tabAbout.Controls.Add(layout);
        }
        #endregion

        #region 事件绑定
        private void AttachEvents()
        {
            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
            this.Resize += Form1_Resize;

            btnStartStop.Click += BtnStartStop_Click;
            btnCopyUrl.Click += BtnCopyUrl_Click;
            nudPort.ValueChanged += NudPort_ValueChanged;

            btnRefreshHw.Click += BtnRefreshHw_Click;

            // 状态定时器
            statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            statusTimer.Tick += StatusTimer_Tick;
            statusTimer.Start();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            txtHardwareInfo.Text = GetHardwareInfo();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!allowRealClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                notifyIcon.Visible = true;
                //notifyIcon.ShowBalloonTip(2000, "P2P Remote Desktop", "程序已最小化到系统托盘", ToolTipIcon.Info);
            }
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                notifyIcon.Visible = true;
            }
        }

        private void BtnStartStop_Click(object sender, EventArgs e)
        {
            if (WebSocketServer.IsRunning)
            {
                StopServer();
            }
            else
            {
                StartServer();
            }
        }

        private void BtnCopyUrl_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(WebSocketServer.GetServerUrl());
                statusBarLabel.Text = "链接已复制到剪贴板";
            }
            catch
            {
                statusBarLabel.Text = "复制失败";
            }
        }

        private void NudPort_ValueChanged(object sender, EventArgs e)
        {
            if (WebSocketServer.IsRunning)
            {
                statusBarLabel.Text = "端口已更改，需要重启服务器才能生效";
            }
            else
            {
                statusBarLabel.Text = "端口已设置为 " + nudPort.Value;
            }
        }

        private void BtnRefreshHw_Click(object sender, EventArgs e)
        {
            txtHardwareInfo.Text = GetHardwareInfo();
            statusBarLabel.Text = "硬件信息已刷新";
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            // 更新服务器状态
            if (WebSocketServer.IsRunning)
            {
                pnlStatusDot.BackColor = Color.FromArgb(76, 175, 80); // 绿色
                lblStatusText.Text = "服务器运行中";
                lblStatusText.ForeColor = Color.FromArgb(76, 175, 80);
                btnStartStop.Text = "停止服务器";
                btnStartStop.BackColor = Color.FromArgb(244, 67, 54);

                lblWebUrl.Text = WebSocketServer.GetServerUrl();
                lblClientCount.Text = WebSocketServer.ConnectedClientsCount + " 个已连接";
            }
            else
            {
                pnlStatusDot.BackColor = Color.Red;
                lblStatusText.Text = "服务器已停止";
                lblStatusText.ForeColor = Color.Red;
                btnStartStop.Text = "启动服务器";
                btnStartStop.BackColor = Color.FromArgb(76, 175, 80);

                lblWebUrl.Text = "http://0.0.0.0:" + nudPort.Value + "/";
                lblClientCount.Text = "--";
            }

            // 更新运行时间
            TimeSpan uptime = DateTime.Now - startTime;
            lblUptime.Text = string.Format("运行时间: {0:D2}:{1:D2}:{2:D2}",
                (int)uptime.TotalHours, uptime.Minutes, uptime.Seconds);

            // 更新 P2P 状态
            if (!string.IsNullOrEmpty(GlobalVariables.peername) && GlobalVariables.peername != "对方")
            {
                lblP2PStatus.Text = "已连接";
                lblP2PStatus.ForeColor = Color.FromArgb(76, 175, 80);
                lblPeerName.Text = GlobalVariables.peername;
            }
            else
            {
                lblP2PStatus.Text = "等待连接...";
                lblP2PStatus.ForeColor = Color.Gray;
                lblPeerName.Text = "--";
            }

            // 状态栏
            if (WebSocketServer.IsRunning)
            {
                statusBarLabel.Text = string.Format("运行中 | 端口: {0} | Web客户端: {1} | 捕获: {2}",
                    WebSocketServer.Port, WebSocketServer.ConnectedClientsCount, WebSocketServer.CaptureMode);
            }
            else
            {
                statusBarLabel.Text = "已停止";
            }
        }
        #endregion

        #region 服务器控制
        private void StartServer()
        {
            int port = (int)nudPort.Value;
            try
            {
            WebSocketServer.Start(port);
            statusBarLabel.Text = "服务器已启动，端口: " + port;

            // 启动 STUN 公网检测
            QueryStunInfo();
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动服务器失败:\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopServer()
        {
            WebSocketServer.Stop();
            statusBarLabel.Text = "服务器已停止";
        }

        private void QueryStunInfo()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // 加载 STUN 服务器列表
                    string stunFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StunServers.json");
                    if (!File.Exists(stunFile))
                        stunFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "StunServers.json");

                    p2pcopy.StunResult result = null;
                    string usedServer = "";
                    string lastError = "";
                    int dnsFailCount = 0;
                    int timeoutCount = 0;

                    // 尝试 STUN（如果配置文件存在）
                    if (File.Exists(stunFile))
                    {
                        var servers = p2p.StunServer.StunServer.GetStunServersFromFile(stunFile);
                        if (servers != null && servers.Length > 0)
                        {
                            foreach (var server in servers)
                            {
                                try
                                {
                                    using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                                    {
                                        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                                        socket.ReceiveTimeout = 3000;
                                        result = p2pcopy.StunClient.Query(server.Server, server.Port, socket);
                                        usedServer = server.Server;
                                        break;
                                    }
                                }
                                catch (System.Net.Sockets.SocketException sex)
                                {
                                    lastError = sex.SocketErrorCode.ToString();
                                    if (sex.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound ||
                                        sex.SocketErrorCode == System.Net.Sockets.SocketError.TryAgain)
                                        dnsFailCount++;
                                    else
                                        timeoutCount++;
                                    continue;
                                }
                                catch (Exception ex)
                                {
                                    lastError = ex.Message;
                                    timeoutCount++;
                                    continue;
                                }
                            }
                        }
                    }

                    // STUN 成功 → 显示完整信息
                    if (result != null && result.PublicEndPoint != null)
                    {
                        string publicIp = result.PublicEndPoint.Address.ToString();
                        string natTypeName = GetNatTypeName(result.NetType);
                        string publicUrl = "http://" + publicIp + ":" + nudPort.Value + "/";

                        // 如果 STUN 返回 IPv6，尝试 HTTP 获取 IPv4
                        bool isIpv6 = result.PublicEndPoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
                        if (isIpv6)
                        {
                            string httpIp4 = TryGetPublicIpv4();
                            if (!string.IsNullOrEmpty(httpIp4))
                            {
                                publicIp = httpIp4;
                                publicUrl = "http://" + publicIp + ":" + nudPort.Value + "/";
                                natTypeName += "（IPv6 穿透困难，已获取 IPv4）";
                            }
                        }

                        this.BeginInvoke(new Action(() =>
                        {
                            txtPublicIp.Text = publicIp;
                            txtPublicIp.ForeColor = Color.FromArgb(33, 150, 243);

                            lblNatType.Text = natTypeName;
                            lblNatType.ForeColor = GetNatTypeColor(result.NetType);

                            txtPublicUrl.Text = publicUrl;
                            txtPublicUrl.ForeColor = Color.Blue;
                            txtPublicUrl.Font = new Font("Microsoft YaHei", 9F, FontStyle.Underline);

                            btnCopyIp.Enabled = true;
                            btnOpenUrl.Enabled = true;

                            // 更新本机 P2P 端点（STUN 检测到的公网 IP:端口）
                            int publicPort = result.PublicEndPoint.Port;
                            txtMyEndpoint.Text = publicIp + ":" + publicPort;
                            txtMyEndpoint.ForeColor = Color.FromArgb(33, 150, 243);

                            statusBarLabel.Text = "公网检测完成: " + publicIp + "  (" + natTypeName + ")";
                        }));
                        return;
                    }

                    // === STUN 全部失败 → HTTP 备用检测 ===
                    string httpIp = null;
                    string httpIpv6 = null;   // 备用：找不到 IPv4 时暂存 IPv6
                    string httpError = null;

                    // 备选 API 列表（按优先级）
                    string[] apis = {
                        "https://api.ipify.org",
                        "https://ifconfig.me/ip",
                        "https://icanhazip.com",
                        "http://ipinfo.io/ip"
                    };

                    foreach (string apiUrl in apis)
                    {
                        try
                        {
                            using (var wc = new System.Net.WebClient())
                            {
                                wc.Encoding = System.Text.Encoding.UTF8;
                                wc.Headers.Add("User-Agent", "P2PRemoteDesktop/1.0");
                                // 超时 5 秒
                                string ipText = wc.DownloadString(apiUrl).Trim();
                                // 验证是否是有效 IP 地址
                                if (System.Net.IPAddress.TryParse(ipText, out System.Net.IPAddress parsedIp))
                                {
                                    // 跳过 IPv6，只接受 IPv4
                                    if (parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                                    {
                                        httpIpv6 = ipText;  // 暂存，继续找 IPv4
                                        continue;
                                    }
                                    httpIp = ipText;
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            httpError = ex.Message;
                            continue;
                        }
                    }

                    // 如果没找到 IPv4，但有 IPv6，就用 IPv6（带警告）
                    if (string.IsNullOrEmpty(httpIp) && !string.IsNullOrEmpty(httpIpv6))
                    {
                        httpIp = httpIpv6;
                    }

                    this.BeginInvoke(new Action(() =>
                    {
                        if (!string.IsNullOrEmpty(httpIp))
                        {
                            // 检查是否是 IPv6
                            bool isIpv6 = false;
                            if (System.Net.IPAddress.TryParse(httpIp, out var parsed))
                                isIpv6 = parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;

                            // HTTP 成功获取公网 IP（但无 NAT 类型）
                            txtPublicIp.Text = httpIp;
                            txtPublicIp.ForeColor = isIpv6 ? Color.DarkOrange : Color.FromArgb(76, 175, 80);

                            lblNatType.Text = isIpv6
                                ? "仅 IPv6 可用，P2P 穿透可能失败"
                                : "UDP 可能被阻挡 (HTTP 检测)";
                            lblNatType.ForeColor = isIpv6 ? Color.Red : Color.DarkOrange;

                            string publicUrl = "http://" + httpIp + ":" + nudPort.Value + "/";
                            txtPublicUrl.Text = publicUrl;
                            txtPublicUrl.ForeColor = Color.Blue;
                            txtPublicUrl.Font = new Font("Microsoft YaHei", 9F, FontStyle.Underline);

                            btnCopyIp.Enabled = true;
                            btnOpenUrl.Enabled = true;

                            // HTTP 检测无法获取公网端口，使用默认 P2P 端口 9000
                            txtMyEndpoint.Text = httpIp + ":9000";
                            txtMyEndpoint.ForeColor = isIpv6 ? Color.DarkOrange : Color.FromArgb(76, 175, 80);

                            statusBarLabel.Text = isIpv6
                                ? "公网 IP (IPv6): " + httpIp + " — 建议在有 IPv4 的网络使用 P2P"
                                : "公网 IP (HTTP): " + httpIp + "  —  STUN/UDP 未响应，需端口转发或内网穿透";
                        }
                        else
                        {
                            // 完全失败
                            string reason = "所有检测均失败";
                            if (dnsFailCount > 0) reason = "DNS 解析失败 (" + dnsFailCount + "个服务器)";
                            else if (timeoutCount > 0) reason = "UDP 无响应 (" + timeoutCount + "次超时)，可能被防火墙拦截";
                            else if (!string.IsNullOrEmpty(lastError)) reason = "STUN 错误: " + lastError;
                            else if (!string.IsNullOrEmpty(httpError)) reason = "HTTP 也不可达: " + httpError;

                            txtPublicIp.Text = "检测失败";
                            txtPublicIp.ForeColor = Color.Red;
                            lblNatType.Text = reason;
                            lblNatType.ForeColor = Color.Gray;
                            txtPublicUrl.Text = "当前仅支持局域网访问";

                            statusBarLabel.Text = "无法获取公网信息 — " + reason;
                        }
                    }));
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        txtPublicIp.Text = "检测异常";
                        txtPublicIp.ForeColor = Color.Red;
                        lblNatType.Text = ex.Message;
                        lblNatType.ForeColor = Color.Gray;
                        txtPublicUrl.Text = "--";
                        statusBarLabel.Text = "公网检测异常: " + ex.Message;
                    }));
                }
            });
        }

        private string GetNatTypeName(p2pcopy.StunNetType netType)
        {
            switch (netType)
            {
                case p2pcopy.StunNetType.OpenInternet: return "公网直连 (无 NAT)";
                case p2pcopy.StunNetType.FullCone: return "完全锥形 NAT";
                case p2pcopy.StunNetType.RestrictedCone: return "受限锥形 NAT";
                case p2pcopy.StunNetType.PortRestrictedCone: return "端口受限锥形 NAT";
                case p2pcopy.StunNetType.Symmetric: return "对称 NAT (需中继)";
                case p2pcopy.StunNetType.SymmetricUdpFirewall: return "对称 UDP 防火墙";
                case p2pcopy.StunNetType.UdpBlocked: return "UDP 被阻断";
                default: return "未知";
            }
        }

        private Color GetNatTypeColor(p2pcopy.StunNetType netType)
        {
            switch (netType)
            {
                case p2pcopy.StunNetType.OpenInternet:
                case p2pcopy.StunNetType.FullCone:
                    return Color.FromArgb(76, 175, 80); // 绿色 — 穿透友好
                case p2pcopy.StunNetType.RestrictedCone:
                case p2pcopy.StunNetType.PortRestrictedCone:
                    return Color.FromArgb(255, 152, 0); // 橙色 — 需要 hole punching
                case p2pcopy.StunNetType.Symmetric:
                case p2pcopy.StunNetType.UdpBlocked:
                    return Color.Red; // 红色 — 需中继
                default:
                    return Color.Gray;
            }
        }
        #endregion

        #region 硬件信息
        private string GetHardwareInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("══════════════ 设备硬件信息 ══════════════");
            sb.AppendLine();

            // --- 操作系统 ---
            sb.AppendLine("【操作系统】");
            sb.AppendLine("  名称       : " + GetOSName());
            sb.AppendLine("  版本       : " + Environment.OSVersion.VersionString);
            sb.AppendLine("  架构       : " + (Environment.Is64BitOperatingSystem ? "64 位" : "32 位"));
            sb.AppendLine("  进程架构   : " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("  主机名     : " + Environment.MachineName);
            sb.AppendLine("  用户名     : " + Environment.UserName);
            sb.AppendLine("  系统目录   : " + Environment.SystemDirectory);
            sb.AppendLine();

            // --- CPU ---
            sb.AppendLine("【处理器 (CPU)】");
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString()?.Trim() ?? "未知";
                        sb.AppendLine("  型号       : " + name);
                        sb.AppendLine("  物理核心   : " + (obj["NumberOfCores"]?.ToString() ?? "N/A"));
                        sb.AppendLine("  逻辑核心   : " + (obj["NumberOfLogicalProcessors"]?.ToString() ?? Environment.ProcessorCount.ToString()));
                        if (obj["MaxClockSpeed"] != null)
                            sb.AppendLine("  最大频率   : " + obj["MaxClockSpeed"] + " MHz");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  核心数     : " + Environment.ProcessorCount);
                sb.AppendLine("  (WMI 查询失败: " + ex.Message + ")");
            }
            sb.AppendLine();

            // --- 内存 ---
            sb.AppendLine("【内存 (RAM)】");
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        ulong totalKB = Convert.ToUInt64(obj["TotalVisibleMemorySize"]);
                        ulong freeKB = Convert.ToUInt64(obj["FreePhysicalMemory"]);
                        ulong totalGB = totalKB / 1024 / 1024;
                        ulong usedGB = (totalKB - freeKB) / 1024 / 1024;
                        sb.AppendLine("  总内存     : " + totalKB / 1024 + " MB (" + totalGB + " GB)");
                        sb.AppendLine("  已用       : " + (totalKB - freeKB) / 1024 + " MB (" + usedGB + " GB)");
                        sb.AppendLine("  可用       : " + freeKB / 1024 + " MB (" + (freeKB / 1024 / 1024) + " GB)");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  (WMI 查询失败: " + ex.Message + ")");
            }
            sb.AppendLine();

            // --- 显卡 ---
            sb.AppendLine("【显卡 (GPU)】");
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion, VideoProcessor FROM Win32_VideoController"))
                {
                    int idx = 0;
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        idx++;
                        string name = obj["Name"]?.ToString()?.Trim() ?? "未知显卡";
                        sb.AppendLine("  GPU #" + idx);
                        sb.AppendLine("    名称     : " + name);
                        if (obj["AdapterRAM"] != null)
                        {
                            ulong ramBytes = Convert.ToUInt64(obj["AdapterRAM"]);
                            sb.AppendLine("    显存     : " + (ramBytes / 1024 / 1024 / 1024) + " GB (" + (ramBytes / 1024 / 1024) + " MB)");
                        }
                        if (obj["DriverVersion"] != null)
                        {
                            sb.AppendLine("    驱动版本 : " + obj["DriverVersion"]);
                        }
                        if (obj["VideoProcessor"] != null)
                        {
                            sb.AppendLine("    视频处理器: " + obj["VideoProcessor"]);
                        }
                        sb.AppendLine();
                    }
                    if (idx == 0) sb.AppendLine("  (未检测到独立显卡)");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  (WMI 查询失败: " + ex.Message + ")");
            }
            sb.AppendLine();

            // --- 显示器 ---
            sb.AppendLine("【显示器】");
            foreach (var screen in Screen.AllScreens)
            {
                string primary = screen.Primary ? " (主显示器)" : "";
                sb.AppendLine(string.Format("  {0}: {1}x{2} @ {3} bit{4}",
                    screen.DeviceName,
                    screen.Bounds.Width, screen.Bounds.Height,
                    screen.BitsPerPixel,
                    primary));
            }
            sb.AppendLine();

            // --- 磁盘 ---
            sb.AppendLine("【磁盘驱动器】");
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        long totalGB = drive.TotalSize / 1024 / 1024 / 1024;
                        long freeGB = drive.TotalFreeSpace / 1024 / 1024 / 1024;
                        long usedGB = totalGB - freeGB;
                        sb.AppendLine(string.Format("  {0} ({1})", drive.Name, drive.VolumeLabel));
                        sb.AppendLine(string.Format("    总容量   : {0} GB", totalGB));
                        sb.AppendLine(string.Format("    已用     : {0} GB ({1:P1})", usedGB, (double)usedGB / totalGB));
                        sb.AppendLine(string.Format("    可用     : {0} GB ({1:P1})", freeGB, (double)freeGB / totalGB));
                        sb.AppendLine(string.Format("    文件系统 : {0}", drive.DriveFormat));
                        sb.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  (查询失败: " + ex.Message + ")");
            }
            sb.AppendLine();

            // --- 网络适配器 ---
            sb.AppendLine("【网络】");
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                         ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    {
                        sb.AppendLine("  " + ni.Name + " (" + ni.NetworkInterfaceType + ")");
                        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                sb.AppendLine("    IPv4: " + ip.Address);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  (查询失败: " + ex.Message + ")");
            }
            sb.AppendLine();

            // --- .NET 运行时 ---
            sb.AppendLine("【.NET 运行时】");
            sb.AppendLine("  CLR 版本   : " + Environment.Version);
            sb.AppendLine("  工作目录   : " + AppDomain.CurrentDomain.BaseDirectory);
            sb.AppendLine();

            sb.AppendLine("══════════════════════════════════════════");

            return sb.ToString();
        }

        private string GetOSName()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return obj["Caption"]?.ToString()?.Trim() ?? "Windows";
                    }
                }
            }
            catch { }

            // 备用方案：通过注册表
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        string productName = key.GetValue("ProductName")?.ToString() ?? "";
                        string build = key.GetValue("CurrentBuild")?.ToString() ?? "";
                        string ubr = key.GetValue("UBR")?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(productName))
                            return $"{productName} (Build {build}.{ubr})";
                    }
                }
            }
            catch { }

            return "Windows " + Environment.OSVersion.VersionString;
        }

        private string GetLocalIPString()
        {
            var ips = new List<string>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                ips.Add(ip.Address.ToString());
                            }
                        }
                    }
                }
            }
            catch { }

            return ips.Count > 0 ? string.Join(", ", ips) : "未检测到";
        }
        #endregion

        #region P2P 连接
        private void BtnP2PConnect_Click(object sender, EventArgs e)
        {
            if (p2pConnected)
            {
                MessageBox.Show("已连接，请先断开", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string peerAddr = txtPeerAddress.Text.Trim();
            if (string.IsNullOrEmpty(peerAddr))
            {
                MessageBox.Show("请输入对端地址（格式：IP:端口）", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 解析对端地址
            string[] parts = peerAddr.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int peerPort) || peerPort < 1 || peerPort > 65535)
            {
                MessageBox.Show("地址格式错误，请使用 IP:端口 格式（例：113.108.20.30:9000）", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!System.Net.IPAddress.TryParse(parts[0], out System.Net.IPAddress peerIp))
            {
                MessageBox.Show("IP 地址格式错误", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int localP2PPort = 9000; // P2P 本地端口（可配置）

            btnP2PConnect.Enabled = false;
            lblP2PStatus.Text = "正在连接...";
            lblP2PStatus.ForeColor = Color.Orange;
            statusBarLabel.Text = "P2P: 正在连接 " + peerAddr;

            // 后台线程发起 UDT Rendezvous 连接
            p2pThread = new System.Threading.Thread(() =>
            {
                try
                {
                    // 创建 UDT socket
                    var socket = new UdtSharp.UdtSocket(AddressFamily.InterNetwork, SocketType.Dgram);

                    // 绑定本地 UDP 端口
                    var localEp = new IPEndPoint(System.Net.IPAddress.Any, localP2PPort);
                    socket.Bind(localEp);

                    // 开启 Rendezvous 模式（NAT 穿透）
                    socket.SetSocketOption(UdtSharp.UDTOpt.UDT_RENDEZVOUS, true);

                    // 连接到对端
                    var peerEp = new IPEndPoint(peerIp, peerPort);
                    socket.Connect(peerEp);

                    // 等待连接建立（UDT Connect 是同步的，会阻塞直到连接或超时）
                    // 检查连接状态
                    bool connected = false;
                    for (int i = 0; i < 50; i++) // 最多等 5 秒
                    {
                        if (socket.IsConnected())
                        {
                            connected = true;
                            break;
                        }
                        System.Threading.Thread.Sleep(100);
                    }

                    if (!connected)
                    {
                        socket.Close();
                        this.BeginInvoke(new Action(() =>
                        {
                            lblP2PStatus.Text = "连接超时";
                            lblP2PStatus.ForeColor = Color.Red;
                            btnP2PConnect.Enabled = true;
                            btnP2PDisconnect.Enabled = false;
                            statusBarLabel.Text = "P2P: 连接超时";
                        }));
                        return;
                    }

                    // 连接成功
                    p2pConnected = true;
                    SenderReceiver.isConnected = true;
                    SenderReceiver.client = socket;

                    this.BeginInvoke(new Action(() =>
                    {
                        lblP2PStatus.Text = "已连接 ✓";
                        lblP2PStatus.ForeColor = Color.LimeGreen;
                        btnP2PConnect.Enabled = false;
                        btnP2PDisconnect.Enabled = true;
                        statusBarLabel.Text = "P2P: 已连接到 " + peerAddr;
                    }));

                    // 启动数据接收循环（在后台线程）
                    SenderReceiver.Run(socket);
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        lblP2PStatus.Text = "连接失败: " + ex.Message;
                        lblP2PStatus.ForeColor = Color.Red;
                        btnP2PConnect.Enabled = true;
                        btnP2PDisconnect.Enabled = false;
                        statusBarLabel.Text = "P2P: 连接失败";
                    }));
                }
            });
            p2pThread.IsBackground = true;
            p2pThread.Start();
        }

        private void BtnP2PDisconnect_Click(object sender, EventArgs e)
        {
            DisconnectP2P();
        }

        private void DisconnectP2P()
        {
            try
            {
                SenderReceiver.isConnected = false;
                if (SenderReceiver.client != null)
                {
                    try { SenderReceiver.client.Close(); } catch { }
                    SenderReceiver.client = null;
                }
                p2pConnected = false;

                this.BeginInvoke(new Action(() =>
                {
                    lblP2PStatus.Text = "已断开";
                    lblP2PStatus.ForeColor = Color.Gray;
                    lblPeerName.Text = "--";
                    btnP2PConnect.Enabled = true;
                    btnP2PDisconnect.Enabled = false;
                    statusBarLabel.Text = "P2P: 已断开";
                }));
            }
            catch (Exception ex)
            {
                Logger.LogError("断开 P2P 连接错误: " + ex.Message);
            }
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 通过 HTTP API 获取公网 IPv4 地址（跳过 IPv6）
        /// </summary>
        private string TryGetPublicIpv4()
        {
            string[] apis = {
                "https://api.ipify.org",
                "https://ifconfig.me/ip",
                "https://icanhazip.com",
                "http://ipinfo.io/ip"
            };

            foreach (string apiUrl in apis)
            {
                try
                {
                    using (var wc = new System.Net.WebClient())
                    {
                        wc.Encoding = System.Text.Encoding.UTF8;
                        wc.Headers.Add("User-Agent", "P2PRemoteDesktop/1.0");
                        string ipText = wc.DownloadString(apiUrl).Trim();
                        if (System.Net.IPAddress.TryParse(ipText, out System.Net.IPAddress parsedIp))
                        {
                            // 只接受 IPv4
                            if (parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                return ipText;
                        }
                    }
                }
                catch { continue; }
            }
            return null;
        }
        #endregion
    }
}
