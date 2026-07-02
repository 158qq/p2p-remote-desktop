using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace p2pconn
{
    public class HardwareEncoder : IDisposable
    {
        private bool useHardware = false;
        private object encoderLock = new object();

        public bool IsHardwareAccelerated { get { return useHardware; } }

        public HardwareEncoder()
        {
            try
            {
                useHardware = TryInitializeHardwareEncoder();
                if (useHardware)
                {
                    Logger.LogInfo("硬件加速编码器已初始化");
                }
                else
                {
                    Logger.LogInfo("回退到软件编码器");
                }
            }
            catch
            {
                useHardware = false;
                Logger.LogInfo("回退到软件编码器");
            }
        }

        private bool TryInitializeHardwareEncoder()
        {
            try
            {
                Type type = Type.GetTypeFromProgID("MFTranscode.ProfileManager");
                if (type != null)
                {
                    object obj = Activator.CreateInstance(type);
                    Marshal.ReleaseComObject(obj);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public byte[] EncodeToJpeg(Bitmap bitmap, int quality)
        {
            lock (encoderLock)
            {
                if (useHardware)
                {
                    try
                    {
                        return EncodeWithHardware(bitmap, quality);
                    }
                    catch
                    {
                        useHardware = false;
                        return EncodeWithSoftware(bitmap, quality);
                    }
                }
                return EncodeWithSoftware(bitmap, quality);
            }
        }

        private byte[] EncodeWithSoftware(Bitmap bitmap, int quality)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                ImageCodecInfo codec = GetJpegCodec();
                System.Drawing.Imaging.EncoderParameters parameters = new System.Drawing.Imaging.EncoderParameters(1);
                parameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                bitmap.Save(ms, codec, parameters);
                return ms.ToArray();
            }
        }

        private byte[] EncodeWithHardware(Bitmap bitmap, int quality)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                BITMAP bmpInfo = new BITMAP();
                GetObject(hBitmap, Marshal.SizeOf(bmpInfo), out bmpInfo);

                int width = bmpInfo.bmWidth;
                int height = bmpInfo.bmHeight;
                int stride = width * 4;

                IntPtr pBuffer = Marshal.AllocHGlobal(stride * height);
                try
                {
                    BitBlt(pBuffer, 0, 0, width, height, hBitmap, 0, 0, 0x00CC0020);

                    return EncodeNV12ToJpeg(pBuffer, width, height, quality);
                }
                finally
                {
                    Marshal.FreeHGlobal(pBuffer);
                }
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        private byte[] EncodeNV12ToJpeg(IntPtr nv12Data, int width, int height, int quality)
        {
            byte[] jpegData = null;
            int dataSize = 0;

            IntPtr pCodec = IntPtr.Zero;
            IntPtr pBuffer = IntPtr.Zero;

            try
            {
                int result = CoInitializeEx(IntPtr.Zero, 0);
                if (result != 0 && result != 0x80010106)
                    throw new System.ComponentModel.Win32Exception(result);

                result = CreateJPEGCodec(out pCodec);
                if (result != 0)
                    throw new System.ComponentModel.Win32Exception(result);

                result = SetJPEGQuality(pCodec, quality);
                if (result != 0)
                    throw new System.ComponentModel.Win32Exception(result);

                result = EncodeFrame(pCodec, nv12Data, width, height, width, out pBuffer, out dataSize);
                if (result != 0)
                    throw new System.ComponentModel.Win32Exception(result);

                jpegData = new byte[dataSize];
                Marshal.Copy(pBuffer, jpegData, 0, dataSize);
            }
            finally
            {
                if (pBuffer != IntPtr.Zero)
                    CoTaskMemFree(pBuffer);
                if (pCodec != IntPtr.Zero)
                    ReleaseCodec(pCodec);
                CoUninitialize();
            }

            return jpegData;
        }

        private ImageCodecInfo GetJpegCodec()
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == ImageFormat.Jpeg.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public short bmPlanes;
            public short bmBitsPixel;
            public IntPtr bmBits;
        }

        [DllImport("gdi32.dll")]
        private static extern int GetObject(IntPtr hObject, int nCount, out BITMAP lpObject);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        [DllImport("HardwareCodec.dll", EntryPoint = "CreateJPEGCodec", SetLastError = true)]
        private static extern int CreateJPEGCodec(out IntPtr pCodec);

        [DllImport("HardwareCodec.dll", EntryPoint = "SetJPEGQuality", SetLastError = true)]
        private static extern int SetJPEGQuality(IntPtr pCodec, int quality);

        [DllImport("HardwareCodec.dll", EntryPoint = "EncodeFrame", SetLastError = true)]
        private static extern int EncodeFrame(IntPtr pCodec, IntPtr pData, int width, int height, int stride, out IntPtr pOutput, out int outputSize);

        [DllImport("HardwareCodec.dll", EntryPoint = "ReleaseCodec", SetLastError = true)]
        private static extern int ReleaseCodec(IntPtr pCodec);

        public void Dispose()
        {
        }
    }
}
