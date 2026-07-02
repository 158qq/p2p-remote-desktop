using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace p2pconn
{
    public partial class Form1 : Form
    {
        #region 控件声明
        private TableLayoutPanel mainLayout;
        private TabControl tabControl;
        private TabPage tabConnect, tabAbout;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusBarLabel;
        private Timer statusTimer;
        private DateTime startTime;
        private NotifyIcon notifyIcon;
        private bool allowRealClose = false;

        // Tab 1 - 连接
        private GroupBox grpWebServer;
        private Panel pnlStatusDot;
        private Label lblStatusText, lblWebUrl, lblClientCount, lblLocalIp;
        private NumericUpDown nudPort;
        private Button btnStartStop, btnCopyUrl;

        // Tab 2 - 关于
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
        }

        private void BuildForm()
        {
            // === 窗口基本属性 ===
            this.Text = "P2P Remote Desktop - 局域网版";
            this.Size = new Size(550, 450);
            this.MinimumSize = new Size(500, 400);
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
                RowCount = 1,
                ColumnCount = 1,
                Padding = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // --- Web 服务器分组 ---
            grpWebServer = new GroupBox
            {
                Text = "Web 服务器 (局域网)",
                Dock = DockStyle.Top,
                Height = 280,
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

            layout.Controls.Add(grpWebServer, 0, 0);
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
                Text = "P2P Remote Desktop Server v1.0 (局域网版)",
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
            statusTimer = new Timer { Interval = 1000 };
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

            // --- 操作系统 ---
            sb.AppendLine("【操作系统】");
            sb.AppendLine("  名称       : " + GetOSName());
            sb.AppendLine("  版本       : " + Environment.OSVersion.VersionString);
            sb.AppendLine("  架构       : " + (Environment.Is64BitOperatingSystem ? "64 位" : "32 位"));
            sb.AppendLine("  进程架构   : " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("  主机名     : " + Environment.MachineName);
            sb.AppendLine("  用户名     : " + Environment.UserName);
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
                        sb.AppendLine("  总内存     : " + totalKB / 1024 + " MB (" + totalKB / 1024 / 1024 + " GB)");
                        sb.AppendLine("  已用       : " + (totalKB - freeKB) / 1024 + " MB (" + (totalKB - freeKB) / 1024 / 1024 + " GB)");
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
    }
}
