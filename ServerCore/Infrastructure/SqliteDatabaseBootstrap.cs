using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace DfoGmTool.ServerCore.Infrastructure
{
    public static class SqliteDatabaseBootstrap
    {
        private static readonly object InitLock = new object();
        private static readonly HashSet<string> InitializedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 每个数据库文件每进程只执行一次 schema + 版本化迁移(SqliteMigrations, user_version 门控)。
        // 全库 40+ 处调用点(repo 构造函数/部分请求路径)无需改动, 后续调用降为一次哈希查询。
        public static string Initialize(string databasePath, string schemaFilePath)
        {
            var connectionString = BuildConnectionString(databasePath);
            var key = Path.GetFullPath(databasePath);

            lock (InitLock)
            {
                if (InitializedPaths.Contains(key))
                    return connectionString;

                EnsureDatabaseFile(databasePath);
                using (var conn = new SqliteConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        // item_schema.sql = 新库的完整最终形态(CREATE ... IF NOT EXISTS 幂等)
                        cmd.CommandText = File.ReadAllText(schemaFilePath);
                        cmd.ExecuteNonQuery();
                    }

                    // WAL 持久生效: 读写不互锁, 消除快速切角色时 database is locked
                    using (var walCmd = conn.CreateCommand())
                    {
                        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
                        walCmd.ExecuteScalar();
                    }

                    // 旧库升级: 编号迁移每库只跑一次(见 SqliteMigrations 头注释)
                    DfoGmTool.ServerCore.Sqlite.SqliteMigrations.Apply(conn);
                }

                InitializedPaths.Add(key);
            }

            return connectionString;
        }

        public static string BuildConnectionString(string databasePath)
        {
            return new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true
            }.ConnectionString;
        }

        private static void EnsureDatabaseFile(string databasePath)
        {
            if (File.Exists(databasePath))
                return;

            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }
    }
}
