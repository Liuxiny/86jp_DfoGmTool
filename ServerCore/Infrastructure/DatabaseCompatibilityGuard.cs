using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace DfoGmTool.ServerCore.Infrastructure
{
    public sealed class DatabaseCompatibilityReport
    {
        public DatabaseCompatibilityReport(long schemaVersion)
        {
            SchemaVersion = schemaVersion;
        }

        public long SchemaVersion { get; }
        public int MinimumSupportedVersion =>
            DatabaseCompatibilityGuard.MinimumSupportedVersion;
        public int MaximumSupportedVersion =>
            DatabaseCompatibilityGuard.MaximumSupportedVersion;
    }

    public static class DatabaseCompatibilityGuard
    {
        public const int MinimumSupportedVersion = 52;
        public const int MaximumSupportedVersion = 52;

        private static readonly (string Table, string Column)[] RequiredColumns =
        {
            ("character_expert_job", "enchanter_endurance"),
            ("character_active_quests", "activation_id"),
            ("quest_progress_event_inbox", "activation_id")
        };

        private static readonly string[] RequiredTables =
        {
            "accounts",
            "characters",
            "account_increase_chance_lottery_progress",
            "character_pvp_skill_state",
            "character_pvp_skills"
        };

        public static DatabaseCompatibilityReport Validate(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new InvalidOperationException("数据库路径不能为空。");

            var fullPath = Path.GetFullPath(databasePath);
            if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
            {
                throw new InvalidOperationException(
                    "所选数据库为空或不存在；GM 不会创建服务端数据库。请先启动最新版服务端完成初始化。");
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadOnly,
                ForeignKeys = true
            }.ConnectionString;
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var version = ReadUserVersion(connection);
                var tableCount = ReadTableCount(connection);
                if (version == 0 || tableCount == 0)
                {
                    throw new InvalidOperationException(
                        "所选数据库尚未由服务端初始化（schema version 为 0）。");
                }
                if (version < MinimumSupportedVersion)
                {
                    throw new InvalidOperationException(
                        $"数据库 schema v{version} 过旧；当前 GM 仅支持 v{MinimumSupportedVersion}。请先用最新版服务端升级数据库。");
                }
                if (version > MaximumSupportedVersion)
                {
                    throw new InvalidOperationException(
                        $"数据库 schema v{version} 高于当前 GM 支持的 v{MaximumSupportedVersion}；请先升级 GM，禁止继续写入未知结构。");
                }

                var missing = new List<string>();
                foreach (var table in RequiredTables)
                {
                    if (!TableExists(connection, table))
                        missing.Add(table);
                }
                foreach (var requirement in RequiredColumns)
                {
                    if (!ColumnExists(
                            connection,
                            requirement.Table,
                            requirement.Column))
                    {
                        missing.Add(
                            requirement.Table + "." + requirement.Column);
                    }
                }
                if (missing.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"数据库标记为 schema v{version}，但缺少最新版契约: " +
                        string.Join(", ", missing) + "。");
                }

                return new DatabaseCompatibilityReport(version);
            }
        }

        private static long ReadUserVersion(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version;";
                return Convert.ToInt64(command.ExecuteScalar());
            }
        }

        private static long ReadTableCount(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
                return Convert.ToInt64(command.ExecuteScalar());
            }
        }

        private static bool TableExists(
            SqliteConnection connection,
            string tableName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
                command.Parameters.AddWithValue("@name", tableName);
                return Convert.ToInt64(command.ExecuteScalar()) > 0;
            }
        }

        private static bool ColumnExists(
            SqliteConnection connection,
            string tableName,
            string columnName)
        {
            if (!TableExists(connection, tableName))
                return false;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info({tableName});";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(
                                reader.GetString(1),
                                columnName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
