# P2P Remote Desktop - 局域网版

一个基于C#开发的局域网远程桌面控制工具，支持WebSocket服务器模式，无需公网IP即可在局域网内实现远程桌面访问。

## 🌟 功能特性

### 核心功能
- **WebSocket服务器**：在局域网内提供远程桌面访问服务
- **Web客户端**：通过浏览器访问，无需安装额外软件
- **实时屏幕流式传输**：支持DXGI桌面复制API和传统GDI捕获
- **输入控制**：支持远程鼠标和键盘控制
- **加密传输**：AES-256加密保护数据传输安全

### 技术特性
- **多种捕获方式**：支持DXGI桌面复制API和传统GDI捕获
- **硬件编码**：支持GPU硬件加速编码（如果可用）
- **压缩算法**：支持多种压缩算法（QuickLZ、LZW、JPEG）
- **系统托盘**：最小化到系统托盘，右键菜单快速操作

### 用户界面
- **连接管理**：WebSocket服务器启停、端口配置
- **客户端监控**：显示当前连接的Web客户端数量
- **系统信息**：显示CPU、内存、GPU、磁盘等硬件信息

## 📋 系统要求

- **操作系统**：Windows 7/8/10/11
- **.NET Framework**：4.8或更高版本
- **内存**：建议4GB以上
- **网络**：局域网连接

## 🚀 快速开始

### 编译运行

1. 使用Visual Studio 2015或更高版本打开 `p2pconn.sln`
2. 还原NuGet包（Newtonsoft.Json）
3. 编译项目（Debug或Release模式）
4. 运行生成的 `p2p.exe`

### 使用说明

#### WebSocket模式（局域网）
1. 在主界面选择端口（默认8080）
2. 点击"启动服务器"按钮
3. 在浏览器中访问显示的URL（如 `http://192.168.1.100:8080`）
4. 输入密码（默认1234）连接

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
│   └── ...
├── ico/                          # 图标资源
├── packages/                     # NuGet包
├── p2pconn.sln                   # Visual Studio解决方案
└── README.md                     # 本文件
```

## 🔧 技术架构

### 连接模式

#### WebSocket模式（局域网）
- 适用于局域网远程桌面访问
- 基于WebSocket协议传输视频流和控制信号
- HTML5客户端，无需安装额外软件
- 支持密码认证

### 屏幕捕获流程
```
桌面 → DXGI捕获/GDI捕获 → 压缩编码 → 加密 → 网络传输 → 解密 → 解码显示
```

## 📦 依赖项

- **Newtonsoft.Json** 13.0.1 - JSON序列化
- **System.Windows.Forms** - WinForms界面
- **System.Management** - 硬件信息读取（WMI）

## ⚙️ 配置说明

### 默认端口
- WebSocket服务器：8080

### 默认密码
- 1234（可在源代码中修改）

### 配置文件
- `app.config` - 应用程序配置
- `app.manifest` - 应用程序清单（管理员权限）

## 🔒 安全说明

- 所有数据传输使用AES-256加密
- WebSocket连接需要密码认证
- 建议仅在受信任的局域网环境中使用

## 📝 开发日志

### v1.0.2 (2026-07-02)
- ✅ 精简代码，移除公网功能和P2P连接功能
- ✅ 仅保留WebSocket服务器模式（局域网）
- ✅ 简化UI，移除P2P分组和公网IP检测
- ✅ 移除STUN客户端和UDT库

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

- **Newtonsoft.Json**：JSON序列化库
- **Miroslav Pejic**：原始项目作者

## 📧 联系方式

如有问题或建议，欢迎提交Issue或Pull Request。

---

**⚠️ 免责声明**：本软件仅供学习和合法用途，请勿用于非法目的。使用本软件产生的任何后果由使用者自行承担。

**📌 注意**：此为精简版本，仅支持局域网WebSocket模式。如需公网访问或P2P连接功能，请使用完整版本。
