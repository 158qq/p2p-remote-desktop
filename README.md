# P2P Remote Desktop

一个基于C#开发的P2P远程桌面控制工具，支持WebSocket和P2P两种连接模式，无需公网IP即可实现远程桌面访问。

## 🌟 功能特性

### 核心功能
- **双模式连接**：支持WebSocket服务器模式和P2P直连模式
- **P2P穿透**：基于UDT协议的NAT穿透，无需公网IP即可连接
- **公网IP检测**：自动检测公网IP地址和NAT类型（STUN协议 + HTTP fallback）
- **远程桌面流式传输**：实时屏幕捕获和传输
- **输入控制**：支持远程鼠标和键盘控制

### 技术特性
- **多种捕获方式**：支持DXGI桌面复制API和传统GDI捕获
- **硬件编码**：支持GPU硬件加速编码（如果可用）
- **加密传输**：AES-256加密保护数据传输安全
- **压缩算法**：支持多种压缩算法（QuickLZ、LZW、JPEG）
- **系统托盘**：最小化到系统托盘，右键菜单快速操作

### 用户界面
- **连接管理**：WebSocket服务器启停、端口配置
- **公网信息**：显示公网IP、NAT类型、公网访问URL
- **P2P连接**：本机端点、对端地址配置，一键连接/断开
- **系统信息**：显示CPU、内存、GPU、磁盘等硬件信息

## 📋 系统要求

- **操作系统**：Windows 7/8/10/11
- **.NET Framework**：4.8或更高版本
- **内存**：建议4GB以上
- **网络**：支持IPv4/IPv6，UDP端口（用于P2P穿透）

## 🚀 快速开始

### 编译运行

1. 使用Visual Studio 2015或更高版本打开 `p2pconn.sln`
2. 还原NuGet包（Newtonsoft.Json）
3. 编译项目（Debug或Release模式）
4. 运行生成的 `p2p.exe`

### 使用说明

#### WebSocket模式（局域网）
1. 在主界面选择端口（默认8888）
2. 点击"启动服务器"按钮
3. 在浏览器中访问显示的URL（如 `http://localhost:8888`）
4. 输入密码（默认1234）连接

#### P2P模式（公网）
1. 查看"本机端点"信息（IP:端口）
2. 将本机端点告知对端
3. 输入对端的IP:端口
4. 双方同时点击"连接"按钮
5. 等待NAT穿透完成（约5-30秒）

## 🏗️ 项目结构

```
p2p-main/
├── p2pconn/                      # 主项目
│   ├── Form1.cs                  # 主界面（WinForms）
│   ├── Program.cs                # 程序入口
│   ├── WebSocketServer.cs        # WebSocket服务器
│   ├── DxgiCapture.cs            # DXGI屏幕捕获
│   ├── SenderReceiver.cs         # 数据发送接收
│   ├── HardwareEncoder.cs        # 硬件编码器
│   ├── Cryptography/             # 加密模块
│   │   ├── Aes256.cs
│   │   ├── Sha256.cs
│   │   └── SafeComparison.cs
│   ├── StreamingDesktop/         # 桌面流式传输
│   │   ├── RemoteDesktop.cs
│   │   ├── InputControl.cs
│   │   └── ScreenCaptureStream.cs
│   ├── StreamingLibrary/         # 编解码库
│   │   ├── JpgCompression.cs
│   │   ├── LzwCompression.cs
│   │   └── UnsafeStreamCodec.cs
│   ├── StunClientEndPoint/       # STUN客户端
│   │   └── StunClient.cs
│   ├── StunServer/               # STUN服务器（可选）
│   │   └── StunServer.cs
│   └── UdtSharpLib/              # UDT协议实现
│       ├── UdtSocket.cs
│       ├── Core.cs
│       ├── Packet.cs
│       └── ...
├── ico/                          # 图标资源
├── packages/                     # NuGet包
├── p2pconn.sln                   # Visual Studio解决方案
└── README.md                     # 本文件
```

## 🔧 技术架构

### 连接模式

#### 1. WebSocket模式
- 适用于局域网或公网服务器场景
- 基于WebSocket协议传输视频流和控制信号
- HTML5客户端，无需安装额外软件
- 支持密码认证

#### 2. P2P模式（UDT）
- 适用于点对点直连场景
- 基于UDT（UDP-based Data Transfer Protocol）
- 支持NAT穿透（需要双方同时发起连接）
- 无需中间服务器转发

### 屏幕捕获流程
```
桌面 → DXGI捕获/GDI捕获 → 压缩编码 → 加密 → 网络传输 → 解密 → 解码显示
```

### P2P连接流程
```
1. 获取本机公网IP（STUN/HTTP）
2. 交换端点信息（IP:端口）
3. 双方同时发起Connect（Rendezvous模式）
4. NAT穿透（UDP打洞）
5. 建立UDT连接
6. 开始数据传输
```

## 📦 依赖项

- **Newtonsoft.Json** 13.0.1 - JSON序列化
- **System.Windows.Forms** - WinForms界面
- **System.Management** - 硬件信息读取（WMI）

## ⚙️ 配置说明

### 默认端口
- WebSocket服务器：8888
- P2P连接：随机UDP端口

### 默认密码
- 1234（可在源代码中修改）

### 配置文件
- `app.config` - 应用程序配置
- `app.manifest` - 应用程序清单（管理员权限）

## 🔒 安全说明

- 所有数据传输使用AES-256加密
- WebSocket连接需要密码认证
- P2P连接建议设置强密码
- 公网使用建议使用VPN或防火墙保护

## 🐛 已知问题

1. **STUN检测失败**：企业防火墙可能阻止UDP出站流量，程序会自动切换到HTTP fallback
2. **IPv6显示问题**：部分网络环境返回IPv6地址，程序会自动过滤只显示IPv4
3. **HTML重连bug**：已修复（v1.0.1+），使用单一重连定时器防止客户端数量递增
4. **编译环境**：需要在Visual Studio或Developer Command Prompt中编译，不支持某些在线编译环境

## 📝 开发日志

### v1.0.2 (2026-07-02)
- ✅ 修复IPv6地址显示问题（强制显示IPv4）
- ✅ 优化STUN检测流程（UDP→HTTP fallback）
- ✅ 公网IP字段改为可复制的TextBox
- ✅ 添加复制和打开按钮

### v1.0.1 (2026-07-01)
- ✅ 修复HTML自动重连导致客户端数量递增的bug
- ✅ 添加P2P连接功能（UDT Rendezvous模式）
- ✅ 添加系统托盘右键菜单（显示主窗口/退出）
- ✅ 移除设置Tab，简化为2-Tab界面

### v1.0.0 (2024-06-11)
- ✅ 初始版本
- ✅ WebSocket远程桌面功能
- ✅ STUN公网IP检测
- ✅ WinForms UI界面

## 📄 许可证

本项目采用MIT许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 🙏 致谢

- **UDT协议**：基于UDT（UDP-based Data Transfer Protocol）实现P2P穿透
- **STUN协议**：使用STUN协议检测NAT类型和公网IP
- **Newtonsoft.Json**：JSON序列化库
- **Miroslav Pejic**：原始项目作者

## 📧 联系方式

如有问题或建议，欢迎提交Issue或Pull Request。

---

**⚠️ 免责声明**：本软件仅供学习和合法用途，请勿用于非法目的。使用本软件产生的任何后果由使用者自行承担。
