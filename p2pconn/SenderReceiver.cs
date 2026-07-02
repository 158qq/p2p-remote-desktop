using p2pconn;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Cryptography;

namespace p2pcopy
{
    public static class SenderReceiver
    {
        #region "declare"
        public static bool isConnected = false;
        public static TcpClient tcpClient = null;
        public static NetworkStream netStream;
        public static BinaryWriter swriter;
        static BinaryReader sreader;
        public static Bitmap _decodeBitmap;
        public static Rectangle[] rect;
        private static int FPS = 0;
        private static Stopwatch sfps = Stopwatch.StartNew();
        private static Stopwatch RenderSW = Stopwatch.StartNew();
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);
        private static string ENCRYPTIONKEY = "SPXGPU3UPSIWSX5NLKFTIVN5RHXZW1F2H8CC2ORE";
        static readonly Aes256 aes = new Aes256(ENCRYPTIONKEY);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        #endregion

        #region "recive data <======"
        static internal void Run(Object conn)
        {
            tcpClient = (TcpClient)conn;
            netStream = tcpClient.GetStream();
            sreader = new BinaryReader(netStream);

            int screenW = GetSystemMetrics(SM_CXSCREEN);
            int screenH = GetSystemMetrics(SM_CYSCREEN);

            SendMessage("peer|" + Environment.UserName + "|" + screenW + "|" + screenH);

            while (isConnected && netStream.CanRead)
            {
                try
                {
                    string message = sreader.ReadString();

                    string decryptedMessage;
                    try
                    {
                        decryptedMessage = aes.Decrypt(message);
                    }
                    catch (FormatException)
                    {
                        Logger.LogError("解密失败，无效的 Base-64 字符串，跳过此消息");
                        continue;
                    }

                    if (decryptedMessage != null && decryptedMessage.Length > 0)
                    {

                        string[] words = decryptedMessage.Split('|');
                        switch (words[0])
                        {
                            case "peer":
                                GlobalVariables.peername = words[1];
                                Logger.LogInfo("已连接 => " + words[1]);
                                RemoteDesktop.RScreenWidth = int.Parse(words[2]);
                                RemoteDesktop.RScreenHeight = int.Parse(words[3]);
                                break;

                            case "c":
                                Logger.LogInfo(words[1]);
                                break;

                            case "openp2pDesktop":
                                RemoteDesktop.StartDesktop();
                                Logger.LogInfo("收到远程桌面启动请求");
                                break;

                            case "ds":
                                if (RemoteDesktop.DesktopRunning == true)
                                {
                                    RemoteDesktop.DesktopSpeed = Int32.Parse(words[1]);
                                    RemoteDesktop.stream.FrameInterval = RemoteDesktop.DesktopSpeed;
                                }
                                break;

                            // desktop_streaming
                            case "b":
                                try
                                {
                                    if (words.Length < 3)
                                        break;

                                    int value;
                                    if (int.TryParse(words[2], out value))
                                    {
                                        int toRecv = Convert.ToInt32(words[2]);
                                        if (toRecv <= 0 || toRecv > 10 * 1024 * 1024)
                                        {
                                            Logger.LogWarning("无效的数据长度: " + toRecv);
                                            break;
                                        }

                                        byte[] tempBytes = sreader.ReadBytes(toRecv);
                                        if (tempBytes == null || tempBytes.Length != toRecv || tempBytes.Length == 0)
                                        {
                                            Logger.LogWarning("数据不完整，期望: " + toRecv + "，实际: " + (tempBytes != null ? tempBytes.Length.ToString() : "null"));
                                            break;
                                        }

                                        try
                                        {
                                            byte[] decompressed = QuickLZ.Decompress(tempBytes);
                                            if (decompressed != null && decompressed.Length > 0)
                                            {
                                                try
                                                {
                                                    Bitmap decoded = RemoteDesktop.UnsafeMotionCodec.DecodeData(new MemoryStream(decompressed));
                                                    // 广播到 Web 客户端
                                                    WebSocketServer.BroadcastFrame(decoded);
                                                    FPS++;
                                                    if (sfps.ElapsedMilliseconds >= 1000)
                                                    {
                                                        Logger.LogInfo("帧率: " + FPS);
                                                        FPS = 0;
                                                        sfps = Stopwatch.StartNew();
                                                    }
                                                }
                                                catch (Exception decodeEx)
                                                {
                                                    Logger.LogError("解码失败，跳过此帧: " + decodeEx.Message);
                                                }
                                            }
                                        }
                                        catch (Exception decompressEx)
                                        {
                                            Logger.LogError("解压失败，跳过此帧: " + decompressEx.Message);
                                        }
                                        Array.Clear(tempBytes, 0, tempBytes.Length);
                                        GC.Collect();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError("处理桌面帧错误: " + ex.ToString());
                                }
                                break;

                            // mouse_control
                            case "m":
                                try
                                {
                                    InputControl obj1 = new InputControl();
                                    obj1.MoveMouse(int.Parse(words[1]), int.Parse(words[2]));
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError("鼠标移动错误: " + ex.Message);
                                }
                                break;

                            case "mw":
                                try
                                {
                                    InputControl obj3 = new InputControl();
                                    obj3.MouseWheel(int.Parse(words[1]));
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError("鼠标滚轮错误: " + ex.Message);
                                }
                                break;

                            // keyboard_control
                            case "ku":
                                try
                                {
                                    InputControl obj4 = new InputControl();
                                    obj4.SendKeystroke(Convert.ToByte(words[1]), Convert.ToByte(MapVirtualKey(Convert.ToUInt32(words[1]), 0)), false, false);
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError("键盘抬起错误: " + ex.Message.ToString());
                                }
                                break;

                            case "kd":
                                try
                                {
                                    InputControl obj5 = new InputControl();
                                    obj5.SendKeystroke(Convert.ToByte(words[1]), Convert.ToByte(MapVirtualKey(Convert.ToUInt32(words[1]), 0)), true, false);
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError("键盘按下错误: " + ex.Message.ToString());
                                }
                                break;

                            case "endp2pDesktop":
                                RemoteDesktop.StopDesktop();
                                Logger.LogInfo("远程桌面已停止");
                                break;

                            case "end":
                                if (RemoteDesktop.DesktopRunning == true)
                                {
                                    RemoteDesktop.StopDesktop();
                                }
                                netStream.Close();
                                isConnected = false;
                                Process.GetCurrentProcess().Kill();
                                break;

                        }

                        if (words[0] == "mu" || words[0] == "md")
                        {
                            try
                            {
                                InputControl obj2 = new InputControl();
                                bool isleft = false;
                                if (int.Parse(words[3]) == 0)
                                    isleft = true;
                            
                                if (words[4] == "MouseUp")
                                {
                                    obj2.PressOrReleaseMouseButton(false, isleft, int.Parse(words[1]), int.Parse(words[2]));
                                }
                                else
                                {
                                    obj2.PressOrReleaseMouseButton(true, isleft, int.Parse(words[1]), int.Parse(words[2]));
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError("鼠标按键错误: " + ex.Message);
                            }
                        } 
                    }

                }
                catch (IOException e)
                {
                    Logger.LogError("获取数据错误: " + e.Message);
                }
            }
        }
    #endregion

        #region "send data =====>"
    static internal void SendMessage(string message)
        {
            try
            {
                if (isConnected && netStream != null && netStream.CanWrite)
                {
                    // aes 256 bit encode
                    message = aes.Encrypt(message);
                    swriter = new BinaryWriter(netStream);
                    swriter.Write(message);
                    swriter.Flush();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("发送消息错误: " + ex.Message);
            }
        }
        #endregion

        /// <summary>
        /// 发送原始字节数据（用于桌面帧传输）
        /// </summary>
        static internal void SendRawBytes(byte[] data)
        {
            try
            {
                if (isConnected && netStream != null && netStream.CanWrite)
                {
                    netStream.Write(data, 0, data.Length);
                    netStream.Flush();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("发送数据错误: " + ex.Message);
            }
        }

    }
}
