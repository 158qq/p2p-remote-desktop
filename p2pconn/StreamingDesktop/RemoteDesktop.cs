using StreamLibrary;
using StreamLibrary.UnsafeCodecs;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using AForge.Video;
using static p2pconn.Win32Stuff;
using System.Diagnostics;

namespace p2pconn
{
    public static class RemoteDesktop
    {
        #region " declare"
        public static int MonitorIndex = 0; // 1 2.. more monitors
        public static IUnsafeCodec UnsafeMotionCodec;
        public static ScreenCaptureStream stream;
        public static BitmapData bmpData = null;
        public static Bitmap DesktopImage = null;
        public static int RScreenWidth = 0;
        public static int RScreenHeight = 0;
        public static int DesktopQuality = 80;
        public static int DesktopSpeed = 16;
        public static bool DesktopRunning = false;
        public static bool AutoSpeed = true;
        public static bool ShowCursor = false;
        public static bool CursorToString = true;
        private static string mode = "[Cursor: Default]";
        private static Stopwatch time = Stopwatch.StartNew(); // test time elapsed
        #endregion

        #region " Screen Bounds (Win32, no Windows.Forms)"
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        private static Rectangle GetScreenBounds()
        {
            int x      = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int y      = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width  = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            return new Rectangle(x, y, width, height);
        }
        #endregion

        #region " Start Stop Remote Desktop"
        public static void StartDesktop()
        {
            try
            {
                if (DesktopRunning == true)
                    return;
                if (UnsafeMotionCodec == null)
                    UnsafeMotionCodec = new UnsafeStreamCodec(DesktopQuality, true);
                DesktopImage = null;
                startAForgeVideo();
                DesktopRunning = true;
                Logger.LogInfo("开始共享桌面");
            }
            catch (Exception ex)
            {
                Logger.LogError("启动桌面错误: " + ex.Message);
            }
        }

        private static void startAForgeVideo()
        {
            // create screen capture video source
            stream = new ScreenCaptureStream(GetScreenBounds());

            //  set interval capture default 100ms
            stream.FrameInterval = DesktopSpeed; 

            // set NewFrame event handler
            stream.NewFrame += new NewFrameEventHandler(video_NewFrame);

            // sleep 1 sec
            Thread.Sleep(1000);

            // start the video source
            stream.Start();
        }
        public static void StopDesktop()
        {
            try
            {
                stopAForgeVideo();
                DesktopImage = null;
                DesktopRunning = false;
                Logger.LogInfo("停止共享桌面");
            }
            catch (Exception ex)
            {
                Logger.LogError("停止桌面错误: " + ex.Message);
            }
        }
        private static void stopAForgeVideo()
        {
            try
            {
                stream.NewFrame -= new NewFrameEventHandler(video_NewFrame);
                stream.SignalToStop();
            }
            catch (Exception ex)
            {
                Logger.LogError("停止视频错误: " + ex.Message);
            }
        }
        #endregion
        #region " Streaming Desktop"
        private static void video_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            ScreenCap = (Bitmap)eventArgs.Frame.Clone();
            try
            {
                WebSocketServer.BroadcastFrame(ScreenCap);
                GetCursorState();
                ScreenCap.Dispose();
                GC.Collect();
            }
            catch (Exception ex)
            {
                Logger.LogError("共享桌面停止错误: " + ex.Message);
                StopDesktop();
                GC.Collect();
            }
        }

    private static  Bitmap  ScreenCap
        {
            get { return DesktopImage; }
            set { DesktopImage = value; }
        }
        #endregion
        #region " Cursor To String"
        private static string Tipo
        {
            get {  return mode;  }
            set {  mode = value;  }
        }
        private static void  GetCursorState()
        {
            try
            {
                CURSORINFO pci;
                pci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
                if (GetCursorInfo(out pci))
                {
                    // 直接使用 hCursor 句柄描述代替 Cursor.ToString()
                    Tipo = "[Cursor: " + pci.hCursor.ToString("X") + "]";
                }
            }
            catch
            {
                Tipo = "";
            }
        }
        #endregion
    }
}
