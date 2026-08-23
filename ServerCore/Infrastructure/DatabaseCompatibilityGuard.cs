using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DfoGmTool.ServerCore.Infrastructure
{
    public sealed class DatabaseCompatibilityReport
    {
        public DatabaseCompatibilityReport(
            long schemaVersion,
            string baselineId,
            long? metadataSchemaVersion)
        {
            SchemaVersion = schemaVersion;
            BaselineId = baselineId;
            MetadataSchemaVersion = metadataSchemaVersion;
        }

        public long SchemaVersion { get; }
        public string BaselineId { get; }
        public long? MetadataSchemaVersion { get; }
        public bool StructureCompatible => true;
    }

    public static class DatabaseCompatibilityGuard
    {
        private static readonly string[] RequiredTables =
        {
            "accounts",
            "characters",
            "character_inventory_items",
            "account_inventory_items",
            "character_titlebook_items",
            "mailbox_messages",
            "mailbox_recipients",
            "mailbox_attachments",
            "inventory_audit_log",
            "character_expert_job",
            "character_active_quests",
            "quest_progress_event_inbox",
            "character_pvp_skill_state",
            "character_pvp_skills",
            "account_increase_chance_lottery_progress"
        };

        private static readonly (string Table, string Column)[] RequiredColumns =
        {
            ("accounts", "account_id"),
            ("accounts", "m_id"),
            ("characters", "character_id"),
            ("characters", "account_id"),
            ("character_inventory_items", "character_id"),
            ("character_inventory_items", "list_type"),
            ("character_inventory_items", "slot_index"),
            ("character_inventory_items", "item_core"),
            ("account_inventory_items", "account_id"),
            ("account_inventory_items", "slot_index"),
            ("account_inventory_items", "item_core"),
            ("character_titlebook_items", "character_id"),
            ("character_titlebook_items", "category"),
            ("character_titlebook_items", "slot_index"),
            ("character_titlebook_items", "item_core"),
            ("mailbox_messages", "message_id"),
            ("mailbox_messages", "receiver_character_id"),
            ("mailbox_recipients", "message_id"),
            ("mailbox_recipients", "character_id"),
            ("mailbox_attachments", "message_id"),
            ("mailbox_attachments", "item_core"),
            ("inventory_audit_log", "action_name"),
            ("character_expert_job", "enchanter_endurance"),
            ("character_active_quests", "activation_id"),
            ("quest_progress_event_inbox", "activation_id"),
            ("character_pvp_skill_state", "character_id"),
            ("character_pvp_skills", "character_id"),
            ("account_increase_chance_lottery_progress", "account_id")
        };

        private static readonly string[] ItemCoreTables =
        {
            "character_inventory_items",
            "account_inventory_items",
            "character_titlebook_items",
            "mailbox_attachments"
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
                ForeignKeys = true,
                // This is a short-lived probe.  Returning it to the provider
                // pool can keep SQLite's SHM handle alive after validation.
                Pooling = false
            }.ConnectionString;
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var version = ReadUserVersion(connection);
                var tableCount = ReadTableCount(connection);
                var metadata = ReadMetadata(connection);
                if (tableCount == 0)
                {
                    throw new InvalidOperationException(
                        $"数据库结构不兼容（user_version={version}）：数据库为空，缺少服务端结构。");
                }

                var missing = new List<string>();
                foreach (var table in RequiredTables)
                {
                    if (!TableExists(connection, table))
                        missing.Add("表 " + table);
                }
                foreach (var requirement in RequiredColumns)
                {
                    if (!ColumnExists(connection, requirement.Table, requirement.Column))
                        missing.Add(requirement.Table + "." + requirement.Column);
                }
                foreach (var table in ItemCoreTables)
                    AddItemCoreProblems(connection, table, missing);

                if (missing.Count > 0)
                {
                    var baseline = string.IsNullOrWhiteSpace(metadata.BaselineId)
                        ? "未知"
                        : metadata.BaselineId;
                    throw new InvalidOperationException(
                        $"数据库结构不兼容（user_version={version}, baseline_id={baseline}）：" +
                        string.Join(", ", missing));
                }

                return new DatabaseCompatibilityReport(
                    version,
                    metadata.BaselineId,
                    metadata.SchemaVersion);
            }
        }

        private static void AddItemCoreProblems(
            SqliteConnection connection,
            string tableName,
            List<string> problems)
        {
            if (!TableExists(connection, tableName)
                || !ColumnExists(connection, tableName, "item_core"))
            {
                return;
            }

            var coreType = ReadColumnType(connection, tableName, "item_core");
            if (!string.Equals(coreType, "BLOB", StringComparison.OrdinalIgnoreCase))
                problems.Add($"{tableName}.item_core 类型为 {coreType ?? "未知"}，需要BLOB");

            var ddl = ReadTableDefinition(connection, tableName);
            var declaredLength = Regex.Match(
                ddl ?? string.Empty,
                @"length\s*\(\s*item_core\s*\)\s*=\s*(\d+)",
                RegexOptions.IgnoreCase);
            if (!declaredLength.Success)
            {
                problems.Add($"{tableName}.item_core 缺少99B CHECK约束");
            }
            else if (declaredLength.Groups[1].Value != "99")
            {
                problems.Add(
                    $"{tableName}.item_core 声明长度为 {declaredLength.Groups[1].Value}，需要99B");
            }
            else if (!Regex.IsMatch(
                         ddl ?? string.Empty,
                         @"check\s*\([^;]*length\s*\(\s*item_core\s*\)\s*=\s*99",
                         RegexOptions.IgnoreCase))
            {
                problems.Add($"{tableName}.item_core 缺少99B CHECK约束");
            }

            using (var command = connection.CreateCommand())
            {
                var nullIsInvalid = tableName != "mailbox_attachments";
                command.CommandText = nullIsInvalid
                    ? $"SELECT rowid, length(item_core) FROM {tableName} WHERE item_core IS NULL OR length(item_core) <> 99 LIMIT 20;"
                    : $"SELECT rowid, length(item_core) FROM {tableName} WHERE item_core IS NOT NULL AND length(item_core) <> 99 LIMIT 20;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var rowId = reader.IsDBNull(0) ? "?" : reader.GetInt64(0).ToString();
                        var length = reader.IsDBNull(1) ? "NULL" : reader.GetInt32(1).ToString();
                        problems.Add($"{tableName}.item_core 第{rowId}行长度为{length}，需要99B");
                    }
                }
            }
        }

        private static (string BaselineId, long? SchemaVersion) ReadMetadata(
            SqliteConnection connection)
        {
            if (!TableExists(connection, "schema_metadata")
                || !ColumnExists(connection, "schema_metadata", "singleton_id")
                || !ColumnExists(connection, "schema_metadata", "baseline_id")
                || !ColumnExists(connection, "schema_metadata", "schema_version"))
            {
                return (null, null);
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT baseline_id, schema_version FROM schema_metadata WHERE singleton_id=1 LIMIT 1;";
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return (null, null);
                    var baseline = reader.IsDBNull(0) ? null : reader.GetString(0);
                    var version = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                    return (baseline, version);
                }
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

        private static bool TableExists(SqliteConnection connection, string tableName)
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
                        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }

        private static string ReadColumnType(
            SqliteConnection connection,
            string tableName,
            string columnName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info({tableName});";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                            return reader.IsDBNull(2) ? null : reader.GetString(2);
                    }
                }
            }
            return null;
        }

        private static string ReadTableDefinition(
            SqliteConnection connection,
            string tableName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT sql FROM sqlite_master WHERE type='table' AND name=@name;";
                command.Parameters.AddWithValue("@name", tableName);
                return command.ExecuteScalar() as string;
            }
        }
    }
}
