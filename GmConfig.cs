using System;
using System.Collections.Generic;
using System.IO;

namespace DfoGmTool
{
    // 定位服务端的运行目录(bin/Debug), GM 工具直接操作那里的 inventory.db 和 Script.pvf。
    public sealed class GmConfig
    {
        public string ServerBinDir { get; }
        public string DatabasePath => Path.Combine(ServerBinDir, "Data", "inventory.db");
        // schema 优先用服务端目录里的(与目标库同源); 缺失时回退到工具自带拷贝(ServerCore\Sqlite)
        public string SchemaPath
        {
            get
            {
                var serverSchema = Path.Combine(ServerBinDir, "Sqlite", "item_schema.sql");
                if (File.Exists(serverSchema))
                    return serverSchema;
                return Path.Combine(AppContext.BaseDirectory, "ServerCore", "Sqlite", "item_schema.sql");
            }
        }
        public string PvfPath => Path.Combine(ServerBinDir, "Data", "Pvf", "Script.pvf");
        public string ConnectionString => "Data Source=" + DatabasePath;

        private GmConfig(string serverBinDir)
        {
            ServerBinDir = serverBinDir;
        }

        public static GmConfig Resolve(string[] args)
        {
            var candidates = new List<string>();

            for (var i = 0; args != null && i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--server-bin", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(args[i + 1]);
            }

            var env = Environment.GetEnvironmentVariable("DFO_GM_SERVER_BIN");
            if (!string.IsNullOrWhiteSpace(env))
                candidates.Add(env);

            // 从工作目录和程序目录向上找同级的服务端仓库
            foreach (var root in EnumerateSearchRoots())
            {
                candidates.Add(Path.Combine(root, "ServerS4A12", "Server", "DfoServer", "bin", "Debug"));
            }

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                var full = Path.GetFullPath(candidate);
                var config = new GmConfig(full);
                if (File.Exists(config.DatabasePath) && File.Exists(config.PvfPath) && File.Exists(config.SchemaPath))
                    return config;
            }

            throw new FileNotFoundException(
                "找不到服务端运行目录(需要 Data/inventory.db + Data/Pvf/Script.pvf; schema 缺失时用工具自带拷贝)。" +
                "请先构建并运行过一次服务端, 或用 --server-bin <路径> / 环境变量 DFO_GM_SERVER_BIN 指定。");
        }

        private static IEnumerable<string> EnumerateSearchRoots()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = start;
                for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(dir); depth++)
                {
                    if (seen.Add(dir))
                        yield return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
        }
    }
}
