using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Sqlite
{
    
    
    
    internal static class SqliteSchemaMigrator
    {
        public static void EnsureColumns(SqliteConnection connection, string tableName, IEnumerable<(string Name, string Definition)> requiredColumns)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentException("tableName is empty", nameof(tableName));

            var existing = ReadColumnNames(connection, tableName);
            if (existing.Count == 0)
                return; 

            foreach (var (name, definition) in requiredColumns)
            {
                if (existing.Contains(name))
                    continue;

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {name} {definition};";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void MigrateCharacterItemsUniqueConstraint(SqliteConnection connection)
        {
            if (connection == null) return;

            EnsureColumns(connection, "character_items", new[]
            {
                ("equipment_lock_id", "INTEGER NOT NULL DEFAULT 0"),
            });

            var createSql = ReadTableCreateSql(connection, "character_items");
            if (createSql == null) return;
            if (createSql.Contains("slot_index, item_kind")) return;

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS character_items_new (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')),
    owner_id INTEGER NOT NULL,
    character_id INTEGER,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_template_id INTEGER NOT NULL,
    item_kind TEXT NOT NULL DEFAULT 'unknown' CHECK (item_kind IN ('unknown', 'stackable', 'equipment', 'avatar', 'pet', 'special')),
    stack_count INTEGER NOT NULL DEFAULT 0,
    instance_value INTEGER NOT NULL DEFAULT 0,
    durability INTEGER NOT NULL DEFAULT 0,
    seal_flag INTEGER NOT NULL DEFAULT 0,
    option_value INTEGER NOT NULL DEFAULT 0,
    expire_time INTEGER NOT NULL DEFAULT 0,
    marker_16 INTEGER NOT NULL DEFAULT 0,
    pet_serial_or_handle INTEGER NOT NULL DEFAULT 0,
    equipment_lock_id INTEGER NOT NULL DEFAULT 0,
    extra_json TEXT NOT NULL DEFAULT '{}',
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(owner_scope, owner_id, list_type, slot_index, item_kind),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE SET NULL
);
INSERT INTO character_items_new (
    item_uid, owner_scope, owner_id, character_id, list_type, slot_index, item_template_id, item_kind,
    stack_count, instance_value, durability, seal_flag, option_value, expire_time, marker_16,
    pet_serial_or_handle, equipment_lock_id, extra_json, created_at, updated_at)
SELECT
    item_uid, owner_scope, owner_id, character_id, list_type, slot_index, item_template_id, item_kind,
    stack_count, instance_value, durability, seal_flag, option_value, expire_time, marker_16,
    pet_serial_or_handle, equipment_lock_id, extra_json, created_at, updated_at
FROM character_items;
DROP TABLE character_items;
ALTER TABLE character_items_new RENAME TO character_items;
CREATE INDEX IF NOT EXISTS idx_character_items_owner_container
    ON character_items(owner_scope, owner_id, list_type, slot_index);
CREATE INDEX IF NOT EXISTS idx_character_items_template
    ON character_items(item_template_id);
CREATE INDEX IF NOT EXISTS idx_character_items_character
    ON character_items(character_id, list_type, slot_index);";
                cmd.ExecuteNonQuery();
            }
        }

        public static void MigrateCharacterItemLocks(SqliteConnection connection)
        {
            if (connection == null) return;

            var existing = ReadColumnNames(connection, "character_item_locks");
            if (existing.Count == 0)
                return;

            if (existing.Contains("equipment_lock_id")
                && existing.Contains("inventory_list_type")
                && existing.Contains("slot")
                && existing.Contains("remaining_seconds"))
                return;

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS character_item_locks_new (
    character_id INTEGER NOT NULL,
    equipment_lock_id INTEGER NOT NULL,
    inventory_list_type INTEGER NOT NULL,
    slot INTEGER NOT NULL,
    state INTEGER NOT NULL,
    remaining_seconds INTEGER,
    PRIMARY KEY (character_id, equipment_lock_id),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
INSERT OR IGNORE INTO character_item_locks_new (
    character_id, equipment_lock_id, inventory_list_type, slot, state, remaining_seconds)
SELECT
    character_id,
    sort_order + 1,
    type_or_list,
    item_key_or_slot,
    state,
    extra_value
FROM character_item_locks
WHERE sort_order >= 0 AND sort_order < 255;
DROP TABLE character_item_locks;
ALTER TABLE character_item_locks_new RENAME TO character_item_locks;";
                cmd.ExecuteNonQuery();
            }
        }

        // 幂等删列: 列不存在则跳过。需要 SQLite ≥3.35 (随包 e_sqlite3 满足);
        // 列被索引/约束引用时 DROP 会失败, 属于迁移编写错误, 应当在启动时炸出来。
        public static void DropColumnsIfExist(SqliteConnection connection, string tableName, params string[] columns)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var existing = ReadColumnNames(connection, tableName);
            if (existing.Count == 0)
                return;

            foreach (var column in columns)
            {
                if (!existing.Contains(column))
                    continue;

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"ALTER TABLE {tableName} DROP COLUMN {column};";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void MigrateCharactersNameUniqueIndex(SqliteConnection connection)
        {
            if (connection == null) return;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_characters_name_unique ON characters(name);";
                cmd.ExecuteNonQuery();
            }
        }

        private static string ReadTableCreateSql(SqliteConnection connection, string tableName)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@name;";
                cmd.Parameters.AddWithValue("@name", tableName);
                var result = cmd.ExecuteScalar();
                return result as string;
            }
        }

        private static HashSet<string> ReadColumnNames(SqliteConnection connection, string tableName)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({tableName});";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        set.Add(reader.GetString(1));
                }
            }
            return set;
        }
    }
}
