using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace DfoGmTool
{
    // GM 工具可从服务端目录自动发现数据源，也可在运行后直接选择数据库和 PVF。
    public sealed class GmConfig
    {
        public string ServerBinDir { get; }
        public string DatabasePath { get; }
        public string PvfPath { get; }

        // schema 优先用服务端目录里的；自选数据源则使用工具自带的当前 schema。
        public string SchemaPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ServerBinDir))
                {
                    var serverSchema = Path.Combine(ServerBinDir, "Sqlite", "item_schema.sql");
                    if (File.Exists(serverSchema))
                        return serverSchema;
                }
                return Path.Combine(AppContext.BaseDirectory, "ServerCore", "Sqlite", "item_schema.sql");
            }
        }
        public string ConnectionString => new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true
        }.ToString();

        private GmConfig(string databasePath, string pvfPath, string serverBinDir)
        {
            DatabasePath = databasePath;
            PvfPath = pvfPath;
            ServerBinDir = serverBinDir;
        }

        public static GmConfig TryResolve(string[] args)
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
                if (TryCreateFromServerBin(candidate, out var config, out _))
                    return config;
            }

            return null;
        }

        public static bool TryCreate(
            string databasePath,
            string pvfPath,
            out GmConfig config,
            out string error)
        {
            config = null;
            error = null;

            var errors = new List<string>();
            var hasDatabase = TryGetExistingFilePath(databasePath, "数据库", out var fullDatabasePath, out var databaseError);
            if (!hasDatabase)
                errors.Add(databaseError);
            var hasPvf = TryGetExistingFilePath(pvfPath, "PVF", out var fullPvfPath, out var pvfError);
            if (!hasPvf)
                errors.Add(pvfError);
            if (errors.Count > 0)
            {
                error = string.Join(Environment.NewLine, errors);
                return false;
            }

            var serverBinDir = InferServerBinDir(fullDatabasePath);
            var candidate = new GmConfig(fullDatabasePath, fullPvfPath, serverBinDir);
            if (!File.Exists(candidate.SchemaPath))
            {
                error = "找不到数据库 schema 文件: " + candidate.SchemaPath;
                return false;
            }

            config = candidate;
            return true;
        }

        private static bool TryCreateFromServerBin(string serverBinDir, out GmConfig config, out string error)
        {
            config = null;
            error = null;
            if (string.IsNullOrWhiteSpace(serverBinDir))
                return false;

            string fullServerBinDir;
            try
            {
                fullServerBinDir = Path.GetFullPath(serverBinDir);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            var databasePath = Path.Combine(fullServerBinDir, "Data", "inventory.db");
            var pvfPath = Path.Combine(fullServerBinDir, "Data", "Pvf", "Script.pvf");
            return TryCreate(databasePath, pvfPath, out config, out error);
        }

        internal static bool TryGetExistingFilePath(string path, string label, out string fullPath, out string error)
        {
            fullPath = null;
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = label + "路径不能为空。";
                return false;
            }

            try
            {
                fullPath = Path.GetFullPath(path.Trim());
            }
            catch (Exception ex)
            {
                error = label + "路径无效: " + ex.Message;
                return false;
            }

            if (!File.Exists(fullPath))
            {
                error = label + "文件不存在: " + fullPath;
                return false;
            }

            return true;
        }

        private static string InferServerBinDir(string databasePath)
        {
            var databaseDirectory = Path.GetDirectoryName(databasePath);
            if (string.IsNullOrWhiteSpace(databaseDirectory)
                || !string.Equals(Path.GetFileName(databaseDirectory), "Data", StringComparison.OrdinalIgnoreCase))
                return null;

            return Directory.GetParent(databaseDirectory)?.FullName;
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
