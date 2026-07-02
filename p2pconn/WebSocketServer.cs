using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace p2pconn
{
    public static class WebSocketServer
    {
        private static HttpListener listener;
        private static List<WebSocket> connectedClients = new List<WebSocket>();
        private static object clientsLock = new object();
        private static Thread serverThread;
        private static Thread screenCaptureThread;
        private static bool isRunning = false;
        private static bool isCapturing = false;
        private static int port = 8080;
        private static ImageCodecInfo jpegEncoder;
        private static EncoderParameters encoderParams;
        private static Bitmap captureBitmap;
        private static Graphics captureGraphics;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private static DxgiCapture dxgiCapture;
        public static bool IsHardwareAccelerated { get { return dxgiCapture != null && dxgiCapture.IsInitialized; } }
        public static bool IsRunning { get { return isRunning; } }
        public static bool IsCapturing { get { return isCapturing; } }
        public static int Port { get { return port; } }
        public static int ConnectedClientsCount
        {
            get { lock (clientsLock) { return connectedClients.Count; } }
        }
        public static string CaptureMode
        {
            get
            {
                if (dxgiCapture != null && dxgiCapture.IsInitialized) return "硬件加速 (DXGI)";
                return "软件模式 (GDI)";
            }
        }

        public static void Start(int listenPort = 8080)
        {
            if (isRunning) return;
            port = listenPort;
            isRunning = true;

            listener = new HttpListener();
            listener.Prefixes.Add($"http://*:{port}/");

            try
            {
                listener.Start();
                Logger.LogInfo("WebSocket 服务器已启动，端口: " + port);

                serverThread = new Thread(RunServer);
                serverThread.IsBackground = true;
                serverThread.Start();

                StartScreenCapture();
            }
            catch (Exception ex)
            {
                Logger.LogError("启动 WebSocket 服务器失败: " + ex.Message);
                isRunning = false;
            }
        }

        private static void StartScreenCapture()
        {
            if (isCapturing) return;
            isCapturing = true;

            dxgiCapture = new DxgiCapture();
            bool useDxgi = dxgiCapture.Initialize();

            if (!useDxgi)
            {
                int w = GetSystemMetrics(SM_CXSCREEN);
                int h = GetSystemMetrics(SM_CYSCREEN);
                captureBitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                captureGraphics = Graphics.FromImage(captureBitmap);
                captureGraphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                captureGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                captureGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
            }

            jpegEncoder = GetEncoder(ImageFormat.Jpeg);
            encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);

            screenCaptureThread = new Thread(ScreenCaptureLoop);
            screenCaptureThread.IsBackground = true;
            screenCaptureThread.Start();

            Logger.LogInfo("Web 端屏幕捕获已启动" + (useDxgi ? " (硬件加速)" : " (软件)"));
        }

        private static void ScreenCaptureLoop()
        {
            while (isCapturing)
            {
                try
                {
                    bool hasClients;
                    lock (clientsLock)
                    {
                        hasClients = connectedClients.Count > 0;
                    }

                    if (hasClients)
                    {
                        if (dxgiCapture != null && dxgiCapture.IsInitialized)
                        {
                            using (Bitmap frame = dxgiCapture.CaptureFrame())
                            {
                                if (frame != null)
                                {
                                    BroadcastFrame(frame);
                                }
                            }
                        }
                        else
                        {
                            captureGraphics.CopyFromScreen(0, 0, 0, 0,
                                new System.Drawing.Size(GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN)));
                            BroadcastFrame(captureBitmap);
                        }
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("Web 端屏幕捕获错误: " + ex.Message);
                    Thread.Sleep(1000);
                }
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        public static void Stop()
        {
            isRunning = false;
            isCapturing = false;
            listener?.Stop();
            dxgiCapture?.Dispose();
            captureGraphics?.Dispose();
            captureBitmap?.Dispose();
            lock (clientsLock)
            {
                foreach (var client in connectedClients)
                {
                    try
                    {
                        client.CloseAsync(WebSocketCloseStatus.NormalClosure, "服务器关闭", CancellationToken.None).Wait();
                    }
                    catch { }
                }
                connectedClients.Clear();
            }
            Logger.LogInfo("WebSocket 服务器已停止");
        }

        public static string GetServerUrl()
        {
            return $"http://localhost:{port}/";
        }

        private static void RunServer()
        {
            while (isRunning)
            {
                try
                {
                    var context = listener.GetContext();
                    ProcessRequest(context);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError("HTTP 请求处理错误: " + ex.Message);
                }
            }
        }

        private static void ProcessRequest(HttpListenerContext context)
        {
            if (context.Request.IsWebSocketRequest)
            {
                HandleWebSocketRequest(context);
            }
            else
            {
                ServeHtmlPage(context);
            }
        }

        private static async void HandleWebSocketRequest(HttpListenerContext context)
        {
            WebSocket webSocket = null;
            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                webSocket = wsContext.WebSocket;
                lock (clientsLock)
                {
                    connectedClients.Add(webSocket);
                }
                Logger.LogInfo("WebSocket 客户端已连接，当前连接数: " + connectedClients.Count);

                byte[] buffer = new byte[1024 * 64];
                while (webSocket.State == WebSocketState.Open && isRunning)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        HandleClientMessage(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("WebSocket 连接错误: " + ex.Message);
            }
            finally
            {
                if (webSocket != null)
                {
                    try
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                    catch { }
                }
                lock (clientsLock)
                {
                    connectedClients.Remove(webSocket);
                }
                Logger.LogInfo("WebSocket 客户端已断开，当前连接数: " + connectedClients.Count);
            }
        }

        private static void ServeHtmlPage(HttpListenerContext context)
        {
            string html = GetHtmlContent();
            byte[] responseBytes = Encoding.UTF8.GetBytes(html);

            context.Response.ContentType = "text/html; charset=UTF-8";
            context.Response.ContentLength64 = responseBytes.Length;
            context.Response.StatusCode = 200;

            using (var stream = context.Response.OutputStream)
            {
                stream.Write(responseBytes, 0, responseBytes.Length);
            }
        }

        private static void HandleClientMessage(string message)
        {
            try
            {
                Newtonsoft.Json.Linq.JObject data = Newtonsoft.Json.Linq.JObject.Parse(message);
                string type = data["type"].ToString();
                InputControl inputControl = new InputControl();

                switch (type)
                {
                    case "mousemove":
                        int x = (int)data["x"];
                        int y = (int)data["y"];
                        inputControl.MoveMouse(x, y);
                        break;

                    case "mousedown":
                        bool isLeft = (int)data["button"] == 0;
                        inputControl.PressOrReleaseMouseButton(true, isLeft, (int)data["x"], (int)data["y"]);
                        break;

                    case "mouseup":
                        bool isLeftUp = (int)data["button"] == 0;
                        inputControl.PressOrReleaseMouseButton(false, isLeftUp, (int)data["x"], (int)data["y"]);
                        break;

                    case "mousedblclick":
                        bool isLeftDbl = (int)data["button"] == 0;
                        inputControl.PressOrReleaseMouseButton(true, isLeftDbl, (int)data["x"], (int)data["y"]);
                        Thread.Sleep(50);
                        inputControl.PressOrReleaseMouseButton(false, isLeftDbl, (int)data["x"], (int)data["y"]);
                        Thread.Sleep(50);
                        inputControl.PressOrReleaseMouseButton(true, isLeftDbl, (int)data["x"], (int)data["y"]);
                        Thread.Sleep(50);
                        inputControl.PressOrReleaseMouseButton(false, isLeftDbl, (int)data["x"], (int)data["y"]);
                        break;

                    case "keydown":
                        byte keyCode = (byte)(int)data["keyCode"];
                        inputControl.SendKeyDown(keyCode);
                        break;

                    case "keyup":
                        byte keyCodeUp = (byte)(int)data["keyCode"];
                        inputControl.SendKeyUp(keyCodeUp);
                        break;

                    case "mousewheel":
                        int delta = (int)data["delta"];
                        inputControl.MouseWheel(delta);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("处理客户端消息错误: " + ex.Message);
            }
        }

        public static void BroadcastFrame(Bitmap frame)
        {
            if (connectedClients.Count == 0) return;

            try
            {
                byte[] jpegData;
                using (MemoryStream ms = new MemoryStream())
                {
                    frame.Save(ms, jpegEncoder, encoderParams);
                    jpegData = ms.ToArray();
                }

                List<WebSocket> clientsCopy;
                lock (clientsLock)
                {
                    clientsCopy = new List<WebSocket>(connectedClients);
                }

                foreach (var client in clientsCopy)
                {
                    if (client.State == WebSocketState.Open)
                    {
                        try
                        {
                            client.SendAsync(new ArraySegment<byte>(jpegData), WebSocketMessageType.Binary, true, CancellationToken.None);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("广播帧错误: " + ex.Message);
            }
        }

        private static string GetHtmlContent()
        {
            return @"
<!DOCTYPE html>
<html lang='zh-CN'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>P2P 远程桌面</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        html, body { width: 100%; height: 100%; overflow: hidden; font-family: 'Segoe UI', 'Microsoft YaHei', sans-serif; }
        body { background: #000; display: flex; justify-content: center; align-items: center; }
        canvas { display: block; width: 100%; height: 100%; object-fit: contain; }
        /* 悬浮球容器 */
        #float-ball {
            position: fixed; z-index: 9999; user-select: none; -webkit-user-select: none;
            touch-action: none;
        }
        #float-ball .ball {
            width: 48px; height: 48px; border-radius: 50%;
            background: rgba(30,30,30,0.85); box-shadow: 0 2px 12px rgba(0,0,0,0.5);
            display: flex; align-items: center; justify-content: center;
            cursor: pointer; transition: transform 0.2s, background 0.2s;
            position: relative;
        }
        #float-ball .ball:hover { background: rgba(60,60,60,0.9); transform: scale(1.08); }
        #float-ball .ball .dot { width: 5px; height: 5px; border-radius: 50%; background: #fff; margin: 2px; }
        /* 状态指示小点 */
        #float-ball .ball .status-dot {
            position: absolute; top: 6px; right: 6px; width: 10px; height: 10px;
            border-radius: 50%; border: 2px solid rgba(30,30,30,0.85);
            transition: background 0.3s;
        }
        .status-dot.connected { background: #00ff00; }
        .status-dot.disconnected { background: #ff4444; }
        /* 展开面板 */
        #float-panel {
            position: absolute; display: none; flex-direction: column;
            background: rgba(30,30,30,0.92); border-radius: 12px;
            padding: 14px; gap: 10px; min-width: 150px;
            box-shadow: 0 4px 20px rgba(0,0,0,0.5);
            backdrop-filter: blur(8px); -webkit-backdrop-filter: blur(8px);
        }
        #float-panel.show { display: flex; }
        #float-panel .row {
            display: flex; align-items: center; justify-content: space-between;
            color: #ccc; font-size: 13px; gap: 12px;
        }
        #float-panel .row .label { opacity: 0.7; }
        #float-panel .row .value { font-weight: 600; font-variant-numeric: tabular-nums; }
        #float-panel .fps-val { color: #ffcc00; }
        .float-btn {
            width: 100%; padding: 8px 0; border: none; border-radius: 6px;
            cursor: pointer; font-size: 13px; color: #fff; text-align: center;
            transition: opacity 0.2s;
        }
        .float-btn:hover { opacity: 0.85; }
        .float-btn.fullscreen { background: #4CAF50; }
        .float-btn.disconnect { background: #f44336; }
    </style>
</head>
<body>
    <canvas id='desktop'></canvas>

    <!-- 悬浮球 -->
    <div id='float-ball'>
        <div class='ball' id='ball-btn'>
            <span class='dot'></span><span class='dot'></span><span class='dot'></span>
            <span class='dot'></span><span class='dot'></span><span class='dot'></span>
            <span class='dot'></span><span class='dot'></span><span class='dot'></span>
            <span class='status-dot' id='status-dot'></span>
        </div>
        <div id='float-panel'>
            <div class='row'><span class='label'>状态</span><span class='value' id='panel-status' style='color:#00ff00'>连接中...</span></div>
            <div class='row'><span class='label'>帧率</span><span class='value fps-val' id='panel-fps'>0 FPS</span></div>
            <div class='row'><span class='label'>分辨率</span><span class='value' id='panel-res'>--</span></div>
            <button class='float-btn fullscreen' onclick='toggleFullscreen()'>全屏</button>
            <button class='float-btn disconnect' onclick='disconnect()'>断开</button>
        </div>
    </div>

    <script>
        const canvas = document.getElementById('desktop');
        const ctx = canvas.getContext('2d');
        const ballEl = document.getElementById('float-ball');
        const ballBtn = document.getElementById('ball-btn');
        const panelEl = document.getElementById('float-panel');
        const statusDot = document.getElementById('status-dot');
        const panelStatus = document.getElementById('panel-status');
        const panelFps = document.getElementById('panel-fps');
        const panelRes = document.getElementById('panel-res');

        let ws;
        let reconnectTimer = null;
        let intentionallyDisconnected = false;
        let fps = 0;
        let lastFpsUpdate = Date.now();
        let screenWidth = 1920;
        let screenHeight = 1080;

        // ===== 悬浮球逻辑 =====
        let panelOpen = false;
        let dragging = false, dragStartX, dragStartY, ballStartX, ballStartY;
        let ballX = window.innerWidth - 70, ballY = window.innerHeight - 160;
        ballEl.style.left = ballX + 'px';
        ballEl.style.top = ballY + 'px';

        function positionPanel() {
            const bRect = ballBtn.getBoundingClientRect();
            const pw = panelEl.offsetWidth || 150;
            const ph = panelEl.offsetHeight || 160;
            let px = bRect.right - pw - 24;
            let py = bRect.top - ph - 12;
            if (px < 8) px = 8;
            if (py < 8) { py = bRect.bottom + 12; }
            panelEl.style.left = (px - bRect.left + ballBtn.offsetLeft) + 'px';
            panelEl.style.top  = (py - bRect.top + ballBtn.offsetTop) + 'px';
        }

        ballBtn.addEventListener('pointerdown', function(e) {
            dragging = true;
            dragStartX = e.clientX; dragStartY = e.clientY;
            ballStartX = ballX; ballStartY = ballY;
            ballBtn.setPointerCapture(e.pointerId);
            e.preventDefault();
        });

        window.addEventListener('pointermove', function(e) {
            if (!dragging) return;
            const dx = e.clientX - dragStartX, dy = e.clientY - dragStartY;
            ballX = Math.max(0, Math.min(window.innerWidth - 48, ballStartX + dx));
            ballY = Math.max(0, Math.min(window.innerHeight - 48, ballStartY + dy));
            ballEl.style.left = ballX + 'px';
            ballEl.style.top = ballY + 'px';
            if (panelOpen) positionPanel();
        });

        window.addEventListener('pointerup', function(e) {
            if (!dragging) return;
            const moved = Math.abs(e.clientX - dragStartX) + Math.abs(e.clientY - dragStartY);
            dragging = false;
            if (moved < 8) {
                // 点击切换面板
                panelOpen = !panelOpen;
                if (panelOpen) { positionPanel(); panelEl.classList.add('show'); }
                else { panelEl.classList.remove('show'); }
            }
        });

        document.addEventListener('click', function(e) {
            if (panelOpen && !ballEl.contains(e.target)) {
                panelOpen = false;
                panelEl.classList.remove('show');
            }
        });

        function updateStatus(connected, text) {
            statusDot.className = 'status-dot ' + (connected ? 'connected' : 'disconnected');
            panelStatus.textContent = text;
            panelStatus.style.color = connected ? '#00ff00' : '#ff4444';
        }

        function scheduleReconnect() {
            if (intentionallyDisconnected) return;
            if (reconnectTimer) clearTimeout(reconnectTimer);
            reconnectTimer = setTimeout(function() {
                reconnectTimer = null;
                connect();
            }, 3000);
        }

        function connect() {
            if (intentionallyDisconnected) return;
            const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            const wsUrl = protocol + '//' + window.location.host + '/';
            ws = new WebSocket(wsUrl);
            ws.binaryType = 'blob';

            ws.onopen = function() {
                updateStatus(true, '已连接');
            };

            ws.onmessage = function(event) {
                if (event.data instanceof Blob) {
                    const reader = new FileReader();
                    reader.onload = function(e) {
                        const img = new Image();
                        img.onload = function() {
                            screenWidth = img.width;
                            screenHeight = img.height;
                            canvas.width = screenWidth;
                            canvas.height = screenHeight;
                            ctx.drawImage(img, 0, 0);
                            panelRes.textContent = screenWidth + 'x' + screenHeight;

                            fps++;
                            if (Date.now() - lastFpsUpdate >= 1000) {
                                panelFps.textContent = fps + ' FPS';
                                fps = 0;
                                lastFpsUpdate = Date.now();
                            }
                        };
                        img.src = e.target.result;
                    };
                    reader.readAsDataURL(event.data);
                }
            };

            ws.onerror = function(error) {
                updateStatus(false, '连接错误');
                // onclose 会随后触发，由 onclose 统一调度重连
            };

            ws.onclose = function() {
                updateStatus(false, '已断开');
                scheduleReconnect();
            };
        }

        function disconnect() {
            intentionallyDisconnected = true;
            if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null; }
            if (ws) {
                ws.close();
                ws = null;
            }
        }

        function toggleFullscreen() {
            if (!document.fullscreenElement) {
                document.documentElement.requestFullscreen();
            } else {
                document.exitFullscreen();
            }
        }

        function sendMessage(msg) {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify(msg));
            }
        }

        canvas.addEventListener('mousemove', function(e) {
            const rect = canvas.getBoundingClientRect();
            const scaleX = screenWidth / rect.width;
            const scaleY = screenHeight / rect.height;
            sendMessage({
                type: 'mousemove',
                x: Math.round((e.clientX - rect.left) * scaleX),
                y: Math.round((e.clientY - rect.top) * scaleY)
            });
        });

        canvas.addEventListener('mousedown', function(e) {
            e.preventDefault();
            const rect = canvas.getBoundingClientRect();
            const scaleX = screenWidth / rect.width;
            const scaleY = screenHeight / rect.height;
            sendMessage({
                type: 'mousedown',
                x: Math.round((e.clientX - rect.left) * scaleX),
                y: Math.round((e.clientY - rect.top) * scaleY),
                button: e.button
            });
        });

        canvas.addEventListener('mouseup', function(e) {
            const rect = canvas.getBoundingClientRect();
            const scaleX = screenWidth / rect.width;
            const scaleY = screenHeight / rect.height;
            sendMessage({
                type: 'mouseup',
                x: Math.round((e.clientX - rect.left) * scaleX),
                y: Math.round((e.clientY - rect.top) * scaleY),
                button: e.button
            });
        });

        canvas.addEventListener('dblclick', function(e) {
            e.preventDefault();
            const rect = canvas.getBoundingClientRect();
            const scaleX = screenWidth / rect.width;
            const scaleY = screenHeight / rect.height;
            sendMessage({
                type: 'mousedblclick',
                x: Math.round((e.clientX - rect.left) * scaleX),
                y: Math.round((e.clientY - rect.top) * scaleY),
                button: e.button
            });
        });

        canvas.addEventListener('wheel', function(e) {
            e.preventDefault();
            sendMessage({
                type: 'mousewheel',
                delta: e.deltaY > 0 ? -120 : 120
            });
        });

        window.addEventListener('keydown', function(e) {
            sendMessage({
                type: 'keydown',
                keyCode: e.keyCode
            });
        });

        window.addEventListener('keyup', function(e) {
            sendMessage({
                type: 'keyup',
                keyCode: e.keyCode
            });
        });

        connect();
    </script>
</body>
</html>";
        }
    }
}
