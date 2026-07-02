namespace p2pconn
{
    public class GlobalVariables
    {
        /// <summary>远程对端名称，由 P2P peer 消息设置</summary>
        public static string peername = "对方";

        /// <summary>当前是否为 P2P 服务端（监听方），用于决定谁启动桌面共享</summary>
        public static bool IsP2PServer = false;
    }
}
