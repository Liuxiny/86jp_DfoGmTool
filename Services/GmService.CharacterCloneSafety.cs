using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        private static IEnumerable<string> DiscoverDynamicCharacterCloneTables(SqliteConnection conn, SqliteTransaction tx)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "characters", "character_items", "character_equipped_entries", "equipped_items",
                "character_titlebook", "character_achievement_chunks", "character_new_items",
                "character_avatar_detail", "character_container_state",
                "character_avatar_uid_sequence", "character_creature_uid_sequence",
            };
            foreach (var group in CharacterCloneTableGroups.Values)
            {
                foreach (var table in group)
                    known.Add(table);
            }

            var candidateTables = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT name
FROM sqlite_master
WHERE type = 'table'
  AND name NOT LIKE 'sqlite_%'
ORDER BY name;";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        candidateTables.Add(reader.GetString(0));
                }
            }

            var unknownCharacterTables = new List<string>();
            foreach (var table in candidateTables)
            {
                if (known.Contains(table))
                    continue;
                if (!table.StartsWith("character_", StringComparison.OrdinalIgnoreCase))
                    continue;
                var columns = LoadCloneColumns(conn, tx, table);
                if (columns.Any(c => c.Name.Equals("character_id", StringComparison.OrdinalIgnoreCase))
                    || columns.Any(c => c.Name.Equals("owner_character_id", StringComparison.OrdinalIgnoreCase)))
                    unknownCharacterTables.Add(table);
            }

            if (unknownCharacterTables.Count > 0)
            {
                throw new InvalidOperationException(
                    "发现未登记的角色表，已拒绝复制以避免重放运行时账本: "
                    + string.Join(", ", unknownCharacterTables));
            }

            return Array.Empty<string>();
        }

        private static List<CloneColumnInfo> LoadCloneColumns(SqliteConnection conn, SqliteTransaction tx, string tableName)
        {
            var columns = new List<CloneColumnInfo>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "PRAGMA table_info(" + QuoteAccountBackupIdentifier(tableName) + ");";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var name = reader.GetString(1);
                        if (!IsAccountBackupIdentifier(name))
                            continue;
                        columns.Add(new CloneColumnInfo(
                            name,
                            reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            reader.GetInt32(5)));
                    }
                }
            }
            return columns;
        }

        private static bool IsGeneratedIntegerPrimaryKey(IReadOnlyList<CloneColumnInfo> columns, string columnName)
        {
            var primaryKeys = columns.Where(c => c.PrimaryKeyIndex > 0).ToList();
            if (primaryKeys.Count != 1)
                return false;
            var column = primaryKeys[0];
            return column.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase)
                && column.DeclaredType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)
                && !IsCloneOwnershipColumn(column.Name);
        }

        private static void ValidateCloneTableSafety(SqliteConnection conn, SqliteTransaction tx, IReadOnlyList<string> tableNames)
        {
            foreach (var tableName in tableNames)
            {
                var columns = LoadCloneColumns(conn, tx, tableName);
                if (!columns.Any(c => IsCloneOwnershipColumn(c.Name)))
                    throw new InvalidOperationException($"表 {tableName} 缺少可安全改写的角色归属列");

                using (var indexList = conn.CreateCommand())
                {
                    indexList.Transaction = tx;
                    indexList.CommandText = "PRAGMA index_list(" + QuoteAccountBackupIdentifier(tableName) + ");";
                    var uniqueIndexes = new List<string>();
                    using (var reader = indexList.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader.GetInt32(2) != 0)
                                uniqueIndexes.Add(reader.GetString(1));
                        }
                    }
                    foreach (var indexName in uniqueIndexes)
                    {
                        var indexedColumns = LoadIndexColumns(conn, tx, indexName);
                        if (indexedColumns.Count > 0 && !indexedColumns.Any(IsCloneOwnershipColumn))
                            throw new InvalidOperationException($"表 {tableName} 的唯一索引 {indexName} 不含角色归属列，无法安全动态复制");
                    }
                }
            }
        }

        private static List<string> LoadIndexColumns(SqliteConnection conn, SqliteTransaction tx, string indexName)
        {
            var columns = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "PRAGMA index_info(" + QuoteAccountBackupIdentifier(indexName) + ");";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(2))
                            columns.Add(reader.GetString(2));
                    }
                }
            }
            return columns;
        }

        private static bool IsCloneOwnershipColumn(string column)
        {
            return column.Equals("character_id", StringComparison.OrdinalIgnoreCase)
                || column.Equals("owner_character_id", StringComparison.OrdinalIgnoreCase)
                || column.Equals("owner_id", StringComparison.OrdinalIgnoreCase)
                || column.Equals("account_id", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateClonedCharacter(SqliteConnection conn, SqliteTransaction tx, int characterId, int accountId, int slotIndex)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT COUNT(1)
FROM characters
WHERE character_id = @cid AND account_id = @aid AND slot_index = @slot AND delete_flag = 0;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@aid", accountId);
                cmd.Parameters.AddWithValue("@slot", slotIndex);
                if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException("复制后的角色基础行校验失败");
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT COUNT(1)
FROM characters
WHERE account_id = @aid AND slot_index = @slot AND delete_flag = 0;";
                cmd.Parameters.AddWithValue("@aid", accountId);
                cmd.Parameters.AddWithValue("@slot", slotIndex);
                if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException("目标账号角色槽位发生唯一性冲突");
            }

            if (TableExists(conn, tx, "character_new_items"))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
SELECT COUNT(1)
FROM character_new_items
WHERE character_id = @cid
  AND (owner_scope <> 'character' OR owner_id <> @cid);";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                        throw new InvalidOperationException("复制后的物品归属校验失败");
                }
            }
        }

        private readonly struct CloneColumnInfo
        {
            public CloneColumnInfo(string name, string declaredType, int primaryKeyIndex)
            {
                Name = name;
                DeclaredType = declaredType ?? string.Empty;
                PrimaryKeyIndex = primaryKeyIndex;
            }

            public string Name { get; }
            public string DeclaredType { get; }
            public int PrimaryKeyIndex { get; }
        }
    }
}
