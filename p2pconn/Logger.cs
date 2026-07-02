using System;
using System.IO;

namespace p2pconn
{
    public static class Logger
    {
        private static string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static object lockObj = new object();

        public static void Log(string level, string message)
        {
            try
            {
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string fileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
                string filePath = Path.Combine(logDirectory, fileName);
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";

                lock (lockObj)
                {
                    File.AppendAllText(filePath, logLine);
                }
            }
            catch
            {
            }
        }

        public static void LogInfo(string message)
        {
            Log("INFO", message);
        }

        public static void LogError(string message)
        {
            Log("ERROR", message);
        }

        public static void LogWarning(string message)
        {
            Log("WARN", message);
        }

        public static void LogDebug(string message)
        {
            Log("DEBUG", message);
        }
    }
}
