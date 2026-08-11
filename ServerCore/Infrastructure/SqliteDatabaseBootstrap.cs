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

        // GM 只连接由服务端完成迁移的数据库，绝不在生产路径执行 schema 或迁移。
        // 同一文件每进程只验证一次，后续仓储构造复用已验证的数据源。
        public static string Initialize(string databasePath, string schemaFilePath)
        {
            var connectionString = BuildConnectionString(databasePath);
            var key = Path.GetFullPath(databasePath);

            lock (InitLock)
            {
                if (InitializedPaths.Contains(key))
                    return connectionString;

                DatabaseCompatibilityGuard.Validate(databasePath);

                InitializedPaths.Add(key);
            }

            return connectionString;
        }

        public static string BuildConnectionString(string databasePath)
        {
            return new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite,
                ForeignKeys = true
            }.ConnectionString;
        }

        // Only deterministic self-tests may create a database from the bundled
        // final schema. Production code must always call Initialize instead.
        internal static string CreateTestDatabase(
            string databasePath,
            string schemaFilePath)
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true
            }.ConnectionString;
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = File.ReadAllText(schemaFilePath);
                    command.ExecuteNonQuery();
                    command.CommandText =
                        $"PRAGMA user_version = {DatabaseCompatibilityGuard.MaximumSupportedVersion};";
                    command.ExecuteNonQuery();
                }
            }
            DatabaseCompatibilityGuard.Validate(databasePath);
            lock (InitLock)
            {
                InitializedPaths.Add(Path.GetFullPath(databasePath));
            }
            return connectionString;
        }
    }
}
