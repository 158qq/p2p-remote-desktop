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
        private Button btnP2PListen, btnP2PConnect, btnP2PDisconnect;
        private System.Threading.Thread p2pThread = null;
        private bool p2pConnected = false;
        private TcpListener tcpListener = null;
        private bool isListening = false;
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
            StartServer();
        }

        private void BuildForm()
        {
            this.Text = "P2P Remote Desktop v1.0";
            this.Size = new Size(620, 500);
            this.MinimumSize = new Size(520, 420);
            this.StartPosition = FormStartPosition.CenterScreen;

            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "p2p.ico");
                if (!File.Exists(iconPath))
                    iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Resources", "p2p.ico");
                if (File.Exists(iconPath))
                    this.Icon = new Icon(iconPath);
            }
            catch { }

            notifyIcon = new NotifyIcon();
            notifyIcon.Text = "P2P Remote Desktop";
            try { if (this.Icon != null) notifyIcon.Icon = this.Icon; } catch { }
            notifyIcon.Visible = false;
            notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            };

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
                DisconnectP2P();
                Application.Exit();
            };
            notifyIcon.ContextMenuStrip = trayMenu;

            mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(8)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

            tabControl = new TabControl { Dock = DockStyle.Fill };
            tabControl.Appearance = TabAppearance.Normal;

            BuildConnectTab();
            BuildAboutTab();

            tabControl.TabPages.AddRange(new TabPage[] { tabConnect, tabAbout });
            mainLayout.Controls.Add(tabControl, 0, 0);

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

            // === Web 服务器分组 ===
            grpWebServer = new GroupBox
            {
                Text = "Web 服务器",
                Dock = DockStyle.Top,
                Height = 145,
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

            // 客户端数 + 按钮
            lblClientCount = new Label
            {
                Location = new Point(leftCol, y + 3),
                Size = new Size(200, 23),
                Text = "0 个已连接"
            };
            grpWebServer.Controls.Add(lblClientCount);

            btnStartStop = new Button
            {
                Location = new Point(leftCol, y + 28),
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
                Location = new Point(leftCol + 120, y + 28),
                Size = new Size(80, 32),
                Text = "复制链接",
                FlatStyle = FlatStyle.System
            };
            grpWebServer.Controls.Add(btnCopyUrl);

            lblLocalIp = new Label
            {
                Location = new Point(leftCol + 210, y + 28),
                Size = new Size(360, 32),
                Text = GetLocalIPString(),
                ForeColor = Color.Gray
            };
            grpWebServer.Controls.Add(lblLocalIp);

            layout.Controls.Add(grpWebServer, 0, 0);

            // === P2P 连接分组 ===
            grpP2P = new GroupBox
            {
                Text = "P2P 局域网直连",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold),
                Margin = new Padding(0, 10, 0, 0)
            };

            int py = 28;
            int pLeft = 16, pRight = 100;

            // 本机地址
            var lblMyEp = new Label
            {
                Location = new Point(pLeft, py + 3),
                Size = new Size(80, 20),
                Text = "本机地址:",
                Font = new Font("Microsoft YaHei", 9F)
            };
            grpP2P.Controls.Add(lblMyEp);

            string localAddr = GetLocalIPString().Split(',')[0].Trim() + ":9000";
            txtMyEndpoint = new TextBox
            {
                Location = new Point(pRight, py),
                Size = new Size(200, 20),
                Text = localAddr,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 150, 243)
            };
            grpP2P.Controls.Add(txtMyEndpoint);

            var btnCopyEp = new Button
            {
                Location = new Point(pRight + 210, py - 2),
                Size = new Size(50, 23),
                Text = "复制",
                FlatStyle = FlatStyle.System
            };
            btnCopyEp.Click += (s, e) => Clipboard.SetText(txtMyEndpoint.Text);
            grpP2P.Controls.Add(btnCopyEp);

            py += 30;

            // 对端地址
            var lblPeerAddr = new Label
            {
                Location = new Point(pLeft, py + 3),
                Size = new Size(80, 20),
                Text = "对端地址:",
                Font = new Font("Microsoft YaHei", 9F)
            };
            grpP2P.Controls.Add(lblPeerAddr);

            txtPeerAddress = new TextBox
            {
                Location = new Point(pRight, py),
                Size = new Size(220, 20),
                Text = "例: 192.168.1.x:9000",
                ForeColor = Color.Gray,
                Font = new Font("Microsoft YaHei", 9F)
            };
            txtPeerAddress.Enter += (s, e) =>
            {
                if (txtPeerAddress.Text == "例: 192.168.1.x:9000")
                {
                    txtPeerAddress.Text = "";
                    txtPeerAddress.ForeColor = Color.Black;
                }
            };
            txtPeerAddress.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtPeerAddress.Text))
                {
                    txtPeerAddress.Text = "例: 192.168.1.x:9000";
                    txtPeerAddress.ForeColor = Color.Gray;
                }
            };
            grpP2P.Controls.Add(txtPeerAddress);

            py += 32;

            // 监听 / 连接按钮
            btnP2PListen = new Button
            {
                Location = new Point(pLeft, py),
                Size = new Size(100, 30),
                Text = "开始监听",
                BackColor = Color.FromArgb(156, 39, 176),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold)
            };
            btnP2PListen.FlatAppearance.BorderSize = 0;
            btnP2PListen.Click += BtnP2PListen_Click;
            grpP2P.Controls.Add(btnP2PListen);

            btnP2PConnect = new Button
            {
                Location = new Point(pLeft + 110, py),
                Size = new Size(100, 30),
                Text = "主动连接",
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
                Location = new Point(pLeft + 220, py),
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
                StopServer();
            else
                StartServer();
        }

        private void BtnCopyUrl_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(WebSocketServer.GetServerUrl());
                statusBarLabel.Text = "链接已复制到剪贴板";
            }
            catch { statusBarLabel.Text = "复制失败"; }
        }

        private void NudPort_ValueChanged(object sender, EventArgs e)
        {
            statusBarLabel.Text = WebSocketServer.IsRunning
                ? "端口已更改，需要重启服务器才能生效"
                : "端口已设置为 " + nudPort.Value;
        }

        private void BtnRefreshHw_Click(object sender, EventArgs e)
        {
            txtHardwareInfo.Text = GetHardwareInfo();
            statusBarLabel.Text = "硬件信息已刷新";
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            if (WebSocketServer.IsRunning)
            {
                pnlStatusDot.BackColor = Color.FromArgb(76, 175, 80);
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

            TimeSpan uptime = DateTime.Now - startTime;
            lblUptime.Text = string.Format("运行时间: {0:D2}:{1:D2}:{2:D2}",
                (int)uptime.TotalHours, uptime.Minutes, uptime.Seconds);

            if (p2pConnected)
            {
                lblP2PStatus.Text = "已连接 ✓";
                lblP2PStatus.ForeColor = Color.FromArgb(76, 175, 80);
                if (!string.IsNullOrEmpty(GlobalVariables.peername) && GlobalVariables.peername != "对方")
                    lblPeerName.Text = GlobalVariables.peername;
            }
            else if (isListening)
            {
                lblP2PStatus.Text = "监听中...等待连接";
                lblP2PStatus.ForeColor = Color.Orange;
            }
            else
            {
                lblP2PStatus.Text = "未连接";
                lblP2PStatus.ForeColor = Color.Gray;
                lblPeerName.Text = "--";
            }

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
        #endregion

        #region 硬件信息
        private string GetHardwareInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("══════════════ 设备硬件信息 ══════════════");
            sb.AppendLine();
            sb.AppendLine("【操作系统】");
            sb.AppendLine("  名称       : " + GetOSName());
            sb.AppendLine("  版本       : " + Environment.OSVersion.VersionString);
            sb.AppendLine("  架构       : " + (Environment.Is64BitOperatingSystem ? "64 位" : "32 位"));
            sb.AppendLine("  进程架构   : " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("  主机名     : " + Environment.MachineName);
            sb.AppendLine("  用户名     : " + Environment.UserName);
            sb.AppendLine("  系统目录   : " + Environment.SystemDirectory);
            sb.AppendLine();

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
                            sb.AppendLine("    驱动版本 : " + obj["DriverVersion"]);
                        if (obj["VideoProcessor"] != null)
                            sb.AppendLine("    视频处理器: " + obj["VideoProcessor"]);
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

            sb.AppendLine("【显示器】");
            foreach (var screen in Screen.AllScreens)
            {
                string primary = screen.Primary ? " (主显示器)" : "";
                sb.AppendLine(string.Format("  {0}: {1}x{2} @ {3} bit{4}",
                    screen.DeviceName, screen.Bounds.Width, screen.Bounds.Height, screen.BitsPerPixel, primary));
            }
            sb.AppendLine();

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
            catch (Exception ex) { sb.AppendLine("  (查询失败: " + ex.Message + ")"); }
            sb.AppendLine();

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
                                sb.AppendLine("    IPv4: " + ip.Address);
                        }
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("  (查询失败: " + ex.Message + ")"); }
            sb.AppendLine();

            sb.AppendLine("【.NET 运行时】");
            sb.AppendLine("  CLR 版本   : " + Environment.Version);
            sb.AppendLine("  工作目录   : " + AppDomain.CurrentDomain.BaseDirectory);
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
                        return obj["Caption"]?.ToString()?.Trim() ?? "Windows";
                }
            }
            catch { }
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
                                ips.Add(ip.Address.ToString());
                        }
                    }
                }
            }
            catch { }
            return ips.Count > 0 ? string.Join(", ", ips) : "未检测到";
        }
        #endregion

        #region P2P 局域网直连（基于 TCP）
        /// <summary>
        /// 监听模式：绑定本地 TCP 端口，等待对端连接
        /// </summary>
        private void BtnP2PListen_Click(object sender, EventArgs e)
        {
            if (isListening)
            {
                StopListening();
                return;
            }
            if (p2pConnected)
            {
                MessageBox.Show("已连接，请先断开", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int localPort = 9000;
            GlobalVariables.IsP2PServer = true; // 监听方 = 服务端，自动共享桌面
            btnP2PListen.Enabled = false;
            btnP2PConnect.Enabled = false;
            lblP2PStatus.Text = "正在启动监听...";
            lblP2PStatus.ForeColor = Color.Orange;

            p2pThread = new System.Threading.Thread(() =>
            {
                try
                {
                    tcpListener = new TcpListener(IPAddress.Any, localPort);
                    tcpListener.Start();
                    isListening = true;

                    this.BeginInvoke(new Action(() =>
                    {
                        btnP2PListen.Text = "停止监听";
                        btnP2PListen.BackColor = Color.FromArgb(244, 67, 54);
                        btnP2PListen.Enabled = true;
                        btnP2PConnect.Enabled = true;
                        lblP2PStatus.Text = "监听中...等待连接";
                        lblP2PStatus.ForeColor = Color.Orange;
                        statusBarLabel.Text = "P2P: 监听端口 " + localPort;
                    }));

                    // 等待对端连接
                    TcpClient client = tcpListener.AcceptTcpClient();
                    if (client == null || !isListening)
                    {
                        if (client != null) client.Close();
                        return;
                    }

                    OnP2PConnected(client);
                }
                catch (Exception ex)
                {
                    isListening = false;
                    try { tcpListener?.Stop(); } catch { }
                    tcpListener = null;

                    this.BeginInvoke(new Action(() =>
                    {
                        lblP2PStatus.Text = "监听失败: " + ex.Message;
                        lblP2PStatus.ForeColor = Color.Red;
                        btnP2PListen.Enabled = true;
                        btnP2PConnect.Enabled = true;
                        btnP2PListen.Text = "开始监听";
                        btnP2PListen.BackColor = Color.FromArgb(156, 39, 176);
                        statusBarLabel.Text = "P2P: 监听失败";
                    }));
                }
            });
            p2pThread.IsBackground = true;
            p2pThread.Start();
        }

        /// <summary>
        /// 客户端模式：直接 TCP 连接对端
        /// </summary>
        private void BtnP2PConnect_Click(object sender, EventArgs e)
        {
            if (p2pConnected)
            {
                MessageBox.Show("已连接，请先断开", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string peerAddr = txtPeerAddress.Text.Trim();
            if (string.IsNullOrEmpty(peerAddr) || peerAddr.StartsWith("例:"))
            {
                MessageBox.Show("请输入对端地址（格式：IP:端口）", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string[] parts = peerAddr.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int peerPort) || peerPort < 1 || peerPort > 65535)
            {
                MessageBox.Show("地址格式错误，请使用 IP:端口 格式（例：192.168.1.100:9000）", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!IPAddress.TryParse(parts[0], out IPAddress peerIp))
            {
                MessageBox.Show("IP 地址格式错误", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            GlobalVariables.IsP2PServer = false; // 连接方 = 客户端，查看对端桌面
            btnP2PListen.Enabled = false;
            btnP2PConnect.Enabled = false;
            lblP2PStatus.Text = "正在连接...";
            lblP2PStatus.ForeColor = Color.Orange;
            statusBarLabel.Text = "P2P: 正在连接 " + peerAddr;

            // 如果正在监听，先停止
            if (isListening)
                StopListening();

            p2pThread = new System.Threading.Thread(() =>
            {
                try
                {
                    var client = new TcpClient();
                    client.Connect(peerIp, peerPort);
                    OnP2PConnected(client);
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        lblP2PStatus.Text = "连接失败: " + ex.Message;
                        lblP2PStatus.ForeColor = Color.Red;
                        btnP2PListen.Enabled = true;
                        btnP2PConnect.Enabled = true;
                        statusBarLabel.Text = "P2P: 连接失败";
                    }));
                }
            });
            p2pThread.IsBackground = true;
            p2pThread.Start();
        }

        private void OnP2PConnected(TcpClient client)
        {
            p2pConnected = true;
            SenderReceiver.isConnected = true;
            SenderReceiver.tcpClient = client;
            isListening = false;
            try { tcpListener?.Stop(); } catch { }
            tcpListener = null;

            this.BeginInvoke(new Action(() =>
            {
                lblP2PStatus.Text = "已连接 ✓";
                lblP2PStatus.ForeColor = Color.LimeGreen;
                btnP2PListen.Enabled = false;
                btnP2PConnect.Enabled = false;
                btnP2PDisconnect.Enabled = true;
                btnP2PListen.Text = "开始监听";
                btnP2PListen.BackColor = Color.FromArgb(156, 39, 176);
                statusBarLabel.Text = "P2P: 已连接到 " + txtPeerAddress.Text;
            }));

            SenderReceiver.Run(client);
        }

        private void StopListening()
        {
            isListening = false;
            try { tcpListener?.Stop(); } catch { }
            tcpListener = null;
            this.BeginInvoke(new Action(() =>
            {
                btnP2PListen.Text = "开始监听";
                btnP2PListen.BackColor = Color.FromArgb(156, 39, 176);
                lblP2PStatus.Text = "未连接";
                lblP2PStatus.ForeColor = Color.Gray;
                statusBarLabel.Text = "P2P: 监听已停止";
            }));
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
                if (SenderReceiver.tcpClient != null)
                {
                    try { SenderReceiver.tcpClient.Close(); } catch { }
                    SenderReceiver.tcpClient = null;
                }
                try { tcpListener?.Stop(); } catch { }
                tcpListener = null;
                p2pConnected = false;
                isListening = false;

                this.BeginInvoke(new Action(() =>
                {
                    lblP2PStatus.Text = "已断开";
                    lblP2PStatus.ForeColor = Color.Gray;
                    lblPeerName.Text = "--";
                    btnP2PListen.Enabled = true;
                    btnP2PConnect.Enabled = true;
                    btnP2PDisconnect.Enabled = false;
                    btnP2PListen.Text = "开始监听";
                    btnP2PListen.BackColor = Color.FromArgb(156, 39, 176);
                    statusBarLabel.Text = "P2P: 已断开";
                }));
            }
            catch (Exception ex)
            {
                Logger.LogError("断开 P2P 连接错误: " + ex.Message);
            }
        }
        #endregion
    }
}
