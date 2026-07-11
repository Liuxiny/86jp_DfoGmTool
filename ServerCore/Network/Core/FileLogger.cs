using System;
using System.IO;
using System.Text;

namespace DfoGmTool.ServerCore
{
    public static class FileLogger
    {
        private static readonly object _lock = new object();
        private static readonly string _logPath;
        private static readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        static FileLogger()
        {
            var dir = AppContext.BaseDirectory;
            _logPath = Path.Combine(dir, "server.log");
            // server.log 包含中文诊断字段，显式写入 BOM 方便 Windows 查看器识别 UTF-8。
            File.WriteAllText(_logPath, $"=== DfoServer started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\r\n", _encoding);
        }

        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n";
            lock (_lock)
            {
                File.AppendAllText(_logPath, line, _encoding);
            }
        }

        public static void Log(string format, params object[] args)
        {
            Log(string.Format(format, args));
        }
    }
}
