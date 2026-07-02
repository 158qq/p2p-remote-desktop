using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace p2pconn
{
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    public class DxgiCapture : IDisposable
    {
        // ---------- COM 对象指针（纯裸指针，不使用 COM 接口包装）----------
        private IntPtr m_duplication   = IntPtr.Zero;
        private IntPtr m_d3dDevice     = IntPtr.Zero;
        private IntPtr m_d3dContext    = IntPtr.Zero;
        private IntPtr m_stagingTexture = IntPtr.Zero;

        private bool m_isInitialized = false;
        private int  m_width  = 0;
        private int  m_height = 0;

        // vtable 委托缓存（避免每帧重复读 vtable）
        private IDXGIOutputDuplication_AcquireNextFrame m_acquireNextFrame;
        private IDXGIOutputDuplication_ReleaseFrame     m_releaseFrame;
        private ID3D11DeviceContext_CopyResource        m_copyResource;
        private ID3D11DeviceContext_Map                 m_map;
        private ID3D11DeviceContext_Unmap               m_unmap;

        private bool m_accessLost = false;
        private int  m_frameCount = 0;

        public bool IsInitialized { get { return m_isInitialized; } }
        public int  Width  { get { return m_width;  } }
        public int  Height { get { return m_height; } }

        // =====================================================================
        //  Initialize
        // =====================================================================
        public bool Initialize()
        {
            try
            {
                Logger.LogInfo("DXGI: 开始初始化硬件加速...");

                IntPtr factory    = IntPtr.Zero;
                IntPtr adapter    = IntPtr.Zero;
                IntPtr output     = IntPtr.Zero;
                IntPtr duplication = IntPtr.Zero;

                try
                {
                    // 1. CreateDXGIFactory1
                    Guid factoryGuid = new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387"); // IDXGIFactory1
                    int hr = CreateDXGIFactory1(ref factoryGuid, out factory);
                    if (hr != 0 || factory == IntPtr.Zero)
                    {
                        Logger.LogInfo("DXGI: CreateDXGIFactory1 失败, HR=" + hr.ToString("X8"));
                        return false;
                    }
                    Logger.LogInfo("DXGI: CreateDXGIFactory1 成功");

                    // 2. EnumAdapters（vtable index 7 on IDXGIFactory）
                    for (uint i = 0; i < 10; i++)
                    {
                        hr = VtableCall_EnumAdapters(factory, i, out adapter);
                        if (hr == 0) { Logger.LogInfo("DXGI: EnumAdapters 索引 " + i + " 成功"); break; }
                        if (hr == unchecked((int)0x887A0002)) { adapter = IntPtr.Zero; break; }
                    }
                    if (adapter == IntPtr.Zero) { Logger.LogInfo("DXGI: 未找到可用的显卡适配器"); return false; }

                    // 3. GetDesc（可选，仅用于日志）
                    DXGI_ADAPTER_DESC adapterDesc = new DXGI_ADAPTER_DESC();
                    if (VtableCall_AdapterGetDesc(adapter, ref adapterDesc) == 0)
                        Logger.LogInfo("DXGI: 显卡名称: " + adapterDesc.Description);

                    // 4. EnumOutputs（vtable index 7 on IDXGIAdapter）
                    for (uint i = 0; i < 10; i++)
                    {
                        hr = VtableCall_EnumOutputs(adapter, i, out output);
                        if (hr == 0) { Logger.LogInfo("DXGI: EnumOutputs 索引 " + i + " 成功"); break; }
                        if (hr == unchecked((int)0x887A0002)) { output = IntPtr.Zero; break; }
                    }
                    if (output == IntPtr.Zero) { Logger.LogInfo("DXGI: 未找到显示输出"); return false; }

                    // 5. GetDesc（分辨率）
                    DXGI_OUTPUT_DESC outputDesc = new DXGI_OUTPUT_DESC();
                    hr = VtableCall_OutputGetDesc(output, ref outputDesc);
                    if (hr != 0) { Logger.LogInfo("DXGI: GetDesc 失败, HR=" + hr.ToString("X8")); return false; }
                    m_width  = outputDesc.DesktopCoordinates.Right  - outputDesc.DesktopCoordinates.Left;
                    m_height = outputDesc.DesktopCoordinates.Bottom - outputDesc.DesktopCoordinates.Top;
                    Logger.LogInfo("DXGI: 屏幕分辨率 " + m_width + "x" + m_height);

                    // 6. D3D11CreateDevice — 完全通过原始 out 指针持有，不做任何 COM QI
                    IntPtr device  = IntPtr.Zero;
                    IntPtr context = IntPtr.Zero;
                    hr = D3D11CreateDevice(
                        IntPtr.Zero, 1 /*D3D_DRIVER_TYPE_HARDWARE*/, IntPtr.Zero,
                        0, IntPtr.Zero, 0, 7 /*D3D11_SDK_VERSION*/,
                        out device, IntPtr.Zero, out context);
                    if (hr != 0 || device == IntPtr.Zero || context == IntPtr.Zero)
                    {
                        Logger.LogInfo("DXGI: D3D11CreateDevice 失败, HR=" + hr.ToString("X8"));
                        if (device  != IntPtr.Zero) Marshal.Release(device);
                        if (context != IntPtr.Zero) Marshal.Release(context);
                        return false;
                    }
                    m_d3dDevice  = device;
                    m_d3dContext = context;
                    Logger.LogInfo("DXGI: D3D11CreateDevice 成功");

                    // 7. DuplicateOutput（vtable index 22 on IDXGIOutput1）
                    hr = VtableCall_DuplicateOutput(output, m_d3dDevice, out duplication);
                    if (hr != 0 || duplication == IntPtr.Zero)
                    {
                        Logger.LogInfo("DXGI: DuplicateOutput 失败, HR=" + hr.ToString("X8"));
                        return false;
                    }
                    m_duplication = duplication;
                    Logger.LogInfo("DXGI: DuplicateOutput 成功");

                    // 8. 缓存 vtable 委托
                    // IDXGIOutputDuplication vtable（IUnknown=0~2, IDXGIObject=3~6, IDXGIDeviceSubObject=7, 自有方法从8起）：
                    //   8  AcquireNextFrame
                    //   14 ReleaseFrame
                    m_acquireNextFrame = GetVtableDelegate<IDXGIOutputDuplication_AcquireNextFrame>(m_duplication, 8);
                    m_releaseFrame     = GetVtableDelegate<IDXGIOutputDuplication_ReleaseFrame>(m_duplication, 14);
                    // ID3D11DeviceContext vtable（IUnknown=0~2, ID3D11DeviceChild=3~6, 自有方法从7起）：
                    //   14 Map,  15 Unmap,  47 CopyResource
                    m_map          = GetVtableDelegate<ID3D11DeviceContext_Map>(m_d3dContext, 14);
                    m_unmap        = GetVtableDelegate<ID3D11DeviceContext_Unmap>(m_d3dContext, 15);
                    m_copyResource = GetVtableDelegate<ID3D11DeviceContext_CopyResource>(m_d3dContext, 47);

                    // 9. 创建 Staging Texture
                    m_stagingTexture = CreateStagingTexture();
                    if (m_stagingTexture == IntPtr.Zero)
                    {
                        Logger.LogInfo("DXGI: 无法创建 staging 纹理");
                        return false;
                    }
                    Logger.LogInfo("DXGI: staging 纹理创建成功");

                    m_isInitialized = true;
                    Logger.LogInfo("DXGI 硬件加速捕获已初始化: " + m_width + "x" + m_height);
                    return true;
                }
                finally
                {
                    // output / adapter / factory 用完即释放，device/context/duplication 由成员变量持有
                    if (output  != IntPtr.Zero) Marshal.Release(output);
                    if (adapter != IntPtr.Zero) Marshal.Release(adapter);
                    if (factory != IntPtr.Zero) Marshal.Release(factory);
                }
            }
            catch (Exception ex)
            {
                Logger.LogInfo("DXGI 初始化失败，回退到软件捕获: " + ex.Message);
                return false;
            }
        }

        // =====================================================================
        //  CaptureFrame
        // =====================================================================
        public Bitmap CaptureFrame()
        {
            if (!m_isInitialized || m_duplication == IntPtr.Zero || m_stagingTexture == IntPtr.Zero)
                return null;

            try
            {
                DXGI_OUTDUPL_FRAME_INFO frameInfo = new DXGI_OUTDUPL_FRAME_INFO();
                IntPtr resource = IntPtr.Zero;

                int hr = m_acquireNextFrame(m_duplication, 100, out frameInfo, out resource);
                if (hr != 0)
                {
                    if (hr == unchecked((int)0x887A0026)) // DXGI_ERROR_ACCESS_LOST
                    {
                        Logger.LogInfo("DXGI: ACCESS_LOST，需要重新初始化");
                        m_accessLost = true;
                    }
                    else if (hr != unchecked((int)0x887A0027)) // DXGI_ERROR_WAIT_TIMEOUT — 正常静默
                    {
                        Logger.LogInfo("DXGI: AcquireNextFrame 失败, HR=" + hr.ToString("X8"));
                    }
                    return null;
                }

                if (resource == IntPtr.Zero)
                {
                    m_releaseFrame(m_duplication);
                    return null;
                }

                bool logThisFrame = (m_frameCount < 3);
                m_frameCount++;

                // resource 和 duplication frame 必须在所有分支都释放
                // 重要：ReleaseFrame 必须在 Marshal.Release(resource) 之前
                try
                {
                    // QueryInterface：拿 ID3D11Texture2D 指针
                    Guid texture2dGuid = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
                    IntPtr srcTexture = IntPtr.Zero;
                    hr = Marshal.QueryInterface(resource, ref texture2dGuid, out srcTexture);
                    if (hr != 0 || srcTexture == IntPtr.Zero)
                    {
                        Logger.LogInfo("DXGI: QueryInterface ID3D11Texture2D 失败, HR=" + hr.ToString("X8"));
                        return null;
                    }

                    try
                    {
                        // CopyResource: 把 GPU texture 拷贝到可 CPU 读取的 staging texture
                        m_copyResource(m_d3dContext, m_stagingTexture, srcTexture);
                    }
                    finally
                    {
                        Marshal.Release(srcTexture);
                    }
                }
                finally
                {
                    // 先 ReleaseFrame，再 Release resource（DXGI 规范要求）
                    m_releaseFrame(m_duplication);
                    Marshal.Release(resource);
                }

                // resource 已释放，现在安全地 Map staging texture 读像素
                {
                    D3D11_MAPPED_SUBRESOURCE mapped = new D3D11_MAPPED_SUBRESOURCE();
                    hr = m_map(m_d3dContext, m_stagingTexture, 0, 1 /*D3D11_MAP_READ*/, 0, out mapped);
                    if (hr != 0)
                    {
                        Logger.LogInfo("DXGI: Map 失败, HR=" + hr.ToString("X8"));
                        return null;
                    }

                    try
                    {
                        if (mapped.pData == IntPtr.Zero)
                        {
                            Logger.LogInfo("DXGI: mapped.pData 为空");
                            return null;
                        }

                        if (logThisFrame)
                        {
                            int firstPixel  = Marshal.ReadInt32(mapped.pData);
                            int centerOff   = (m_height / 2) * (int)mapped.RowPitch + (m_width / 2) * 4;
                            int centerPixel = Marshal.ReadInt32(IntPtr.Add(mapped.pData, centerOff));
                            Logger.LogInfo("DXGI: 帧 " + m_frameCount
                                + " Map 成功, RowPitch=" + mapped.RowPitch
                                + ", 首像素=0x"   + firstPixel.ToString("X8")
                                + ", 中心像素=0x" + centerPixel.ToString("X8"));
                        }

                        // 把 BGRA staging 数据拷贝到 GDI Bitmap
                        Bitmap bitmap = new Bitmap(m_width, m_height, PixelFormat.Format32bppRgb);
                        BitmapData bitmapData = bitmap.LockBits(
                            new Rectangle(0, 0, m_width, m_height),
                            ImageLockMode.WriteOnly,
                            PixelFormat.Format32bppRgb);
                        try
                        {
                            int srcStride  = (int)mapped.RowPitch;
                            int dstStride  = bitmapData.Stride;
                            int copyBytes  = Math.Min(Math.Abs(srcStride), Math.Abs(dstStride));

                            for (int y = 0; y < m_height; y++)
                            {
                                IntPtr srcRow = IntPtr.Add(mapped.pData,    y * srcStride);
                                IntPtr dstRow = IntPtr.Add(bitmapData.Scan0, y * dstStride);
                                CopyMemory(dstRow, srcRow, (uint)copyBytes);
                            }
                        }
                        finally
                        {
                            bitmap.UnlockBits(bitmapData);
                        }

                        return bitmap;
                    }
                    finally
                    {
                        m_unmap(m_d3dContext, m_stagingTexture, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogInfo("DXGI: CaptureFrame 异常: " + ex.Message);
                return null;
            }
        }

        // =====================================================================
        //  TryReinitialize / Dispose
        // =====================================================================
        public bool TryReinitialize()
        {
            if (!m_accessLost) return false;
            Logger.LogInfo("DXGI: 尝试重新初始化桌面复制...");
            Dispose();
            m_accessLost = false;
            return Initialize();
        }

        public void Dispose()
        {
            if (m_stagingTexture != IntPtr.Zero) { Marshal.Release(m_stagingTexture); m_stagingTexture = IntPtr.Zero; }
            if (m_duplication    != IntPtr.Zero) { Marshal.Release(m_duplication);    m_duplication    = IntPtr.Zero; }
            if (m_d3dContext     != IntPtr.Zero) { Marshal.Release(m_d3dContext);     m_d3dContext     = IntPtr.Zero; }
            if (m_d3dDevice      != IntPtr.Zero) { Marshal.Release(m_d3dDevice);      m_d3dDevice      = IntPtr.Zero; }
            m_isInitialized = false;
            m_frameCount    = 0;
        }

        // =====================================================================
        //  Staging Texture（通过 vtable 裸调用 CreateTexture2D）
        // =====================================================================
        private IntPtr CreateStagingTexture()
        {
            try
            {
                D3D11_TEXTURE2D_DESC desc = new D3D11_TEXTURE2D_DESC
                {
                    Width            = (uint)m_width,
                    Height           = (uint)m_height,
                    MipLevels        = 1,
                    ArraySize        = 1,
                    Format           = 87, // DXGI_FORMAT_B8G8R8A8_UNORM = 87
                    SampleDescCount  = 1,
                    SampleDescQuality = 0,
                    Usage            = 3,       // D3D11_USAGE_STAGING
                    BindFlags        = 0,
                    CPUAccessFlags   = 0x20000, // D3D11_CPU_ACCESS_READ
                    MiscFlags        = 0,
                };

                // ID3D11Device vtable 索引（从 IUnknown index=0 起算）：
                // ID3D11Device 直接继承 IUnknown，中间没有 ID3D11DeviceChild！
                //   0  QueryInterface
                //   1  AddRef
                //   2  Release
                //   3  CreateBuffer
                //   4  CreateTexture1D
                //   5  CreateTexture2D   ← 正确 index
                //   6  CreateTexture3D
                var createTex2D = GetVtableDelegate<ID3D11Device_CreateTexture2D>(m_d3dDevice, 5);

                IntPtr descPtr = Marshal.AllocHGlobal(Marshal.SizeOf(desc));
                try
                {
                    Marshal.StructureToPtr(desc, descPtr, false);
                    IntPtr texture = IntPtr.Zero;
                    int hr = createTex2D(m_d3dDevice, descPtr, IntPtr.Zero, out texture);
                    if (hr != 0)
                    {
                        Logger.LogInfo("DXGI: CreateTexture2D 失败, HR=" + hr.ToString("X8"));
                        return IntPtr.Zero;
                    }
                    return texture;
                }
                finally
                {
                    Marshal.FreeHGlobal(descPtr);
                }
            }
            catch (Exception ex)
            {
                Logger.LogInfo("DXGI: CreateStagingTexture 异常: " + ex.Message);
                return IntPtr.Zero;
            }
        }

        // =====================================================================
        //  vtable helper
        // =====================================================================
        private static T GetVtableDelegate<T>(IntPtr comPtr, int index) where T : Delegate
        {
            IntPtr vtbl    = Marshal.ReadIntPtr(comPtr);
            IntPtr funcPtr = Marshal.ReadIntPtr(vtbl, index * IntPtr.Size);
            return (T)Marshal.GetDelegateForFunctionPointer(funcPtr, typeof(T));
        }

        // =====================================================================
        //  Structs
        // =====================================================================
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_OUTPUT_DESC
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            public RECT   DesktopCoordinates;
            public bool   AttachedToDesktop;
            public int    Rotation;
            public IntPtr Monitor;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_ADAPTER_DESC
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint   VendorId;
            public uint   DeviceId;
            public uint   SubSysId;
            public uint   Revision;
            public ulong  DedicatedVideoMemory;
            public ulong  DedicatedSystemMemory;
            public ulong  SharedSystemMemory;
            public long   AdapterLuid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DXGI_OUTDUPL_POINTER_POSITION
        {
            public POINT Position; // 8 bytes
            public int   Visible;  // BOOL = 4 bytes
        }

        // 精确对应 SDK dxgi1_2.h 中的 DXGI_OUTDUPL_FRAME_INFO
        [StructLayout(LayoutKind.Sequential)]
        private struct DXGI_OUTDUPL_FRAME_INFO
        {
            public long LastPresentTime;              // LARGE_INTEGER  8
            public long LastMouseUpdateTime;          // LARGE_INTEGER  8
            public uint AccumulatedFrames;            // UINT           4
            public int  RecyclePresentBanner;         // BOOL           4
            public int  ProtectedContentMaskedOut;    // BOOL           4
            public DXGI_OUTDUPL_POINTER_POSITION PointerPosition; // 12
            public uint TotalMetadataBufferSize;      // UINT           4
            public uint PointerShapeBufferSize;       // UINT           4
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11_TEXTURE2D_DESC
        {
            public uint Width, Height, MipLevels, ArraySize, Format;
            public uint SampleDescCount, SampleDescQuality;
            public uint Usage, BindFlags, CPUAccessFlags, MiscFlags;
        }

        // =====================================================================
        //  Delegates
        // =====================================================================
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIFactory_EnumAdapters(IntPtr factory, uint Adapter, out IntPtr ppAdapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIAdapter_GetDesc(IntPtr adapter, ref DXGI_ADAPTER_DESC pDesc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIAdapter_EnumOutputs(IntPtr adapter, uint Output, out IntPtr ppOutput);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIOutput_GetDesc(IntPtr output, ref DXGI_OUTPUT_DESC pDesc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIOutput1_DuplicateOutput(IntPtr output, IntPtr Device, out IntPtr ppOutputDuplication);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIOutputDuplication_AcquireNextFrame(IntPtr duplication, uint TimeoutInMilliseconds, out DXGI_OUTDUPL_FRAME_INFO pFrameInfo, out IntPtr ppDesktopResource);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IDXGIOutputDuplication_ReleaseFrame(IntPtr duplication);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ID3D11Device_CreateTexture2D(IntPtr device, IntPtr pDesc, IntPtr pInitialData, out IntPtr ppTexture2D);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void ID3D11DeviceContext_CopyResource(IntPtr context, IntPtr pDstResource, IntPtr pSrcResource);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ID3D11DeviceContext_Map(IntPtr context, IntPtr pResource, uint Subresource, uint MapType, uint MapFlags, out D3D11_MAPPED_SUBRESOURCE pMappedResource);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void ID3D11DeviceContext_Unmap(IntPtr context, IntPtr pResource, uint Subresource);

        // =====================================================================
        //  Vtable helpers（静态，用于 Initialize 阶段一次性调用）
        // =====================================================================
        private static int VtableCall_EnumAdapters(IntPtr factory, uint index, out IntPtr adapter)
            => GetVtableDelegate<IDXGIFactory_EnumAdapters>(factory, 7)(factory, index, out adapter);

        private static int VtableCall_AdapterGetDesc(IntPtr adapter, ref DXGI_ADAPTER_DESC desc)
            => GetVtableDelegate<IDXGIAdapter_GetDesc>(adapter, 8)(adapter, ref desc);

        private static int VtableCall_EnumOutputs(IntPtr adapter, uint index, out IntPtr output)
            => GetVtableDelegate<IDXGIAdapter_EnumOutputs>(adapter, 7)(adapter, index, out output);

        private static int VtableCall_OutputGetDesc(IntPtr output, ref DXGI_OUTPUT_DESC desc)
            => GetVtableDelegate<IDXGIOutput_GetDesc>(output, 7)(output, ref desc);

        private static int VtableCall_DuplicateOutput(IntPtr output, IntPtr device, out IntPtr duplication)
            => GetVtableDelegate<IDXGIOutput1_DuplicateOutput>(output, 22)(output, device, out duplication);

        // =====================================================================
        //  P/Invoke
        // =====================================================================
        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(
            IntPtr pAdapter, uint DriverType, IntPtr Software, uint Flags,
            IntPtr pFeatureLevels, uint FeatureLevels, uint SDKVersion,
            out IntPtr ppDevice, IntPtr pFeatureLevel, out IntPtr ppImmediateContext);

        [DllImport("dxgi.dll")]
        private static extern int CreateDXGIFactory1([In] ref Guid riid, out IntPtr ppFactory);

        [DllImport("kernel32.dll")]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);
    }
}
