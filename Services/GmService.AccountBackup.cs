using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        private const int AccountBackupVersion = 1;

        private static readonly Regex AccountBackupIdentifier = new Regex(
            @"\A[A-Za-z_][A-Za-z0-9_]*\z",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly string[] AccountBackupPreferredOrder =
        {
            "accounts",
            "account_settings",
            "account_premiums",
            "account_cargo_state",
            "account_cargo_items",
            "characters",
            "character_subtype1_fields",
            "character_subtype0_fields",
            "character_skills",
            "character_dark_knight_combo_skill_pages",
            "character_init_bodies",
            "character_init_flags",
            "character_invisible_falgs",
            "character_dungeon_permissions",
            "character_container_state",
            "character_creatures",
            "character_equipped_entries",
            "equipped_items",
            "character_item_values",
            "character_item_locks",
            "character_sort_item_locks",
            "character_hotkey_slots",
            "character_active_quests",
            "character_achievement_complete",
            "character_titlebook",
            "character_achievement_chunks",
            "character_daily_reset",
            "character_daily_counters",
            "character_daily_challenge_groups",
            "character_daily_challenge_entries",
            "character_daily_challenge_tail_ids",
            "character_daily_schedule_states",
            "character_buy_restrict_items",
            "character_pet_welcome_cache",
            "character_rental_items",
            "character_crystal_contract",
            "character_growth_weapon_stages",
            "character_pvp_missions",
            "character_dimensions",
            "character_dimension_flags",
            "character_collectbox_slots",
            "character_mercenary_support",
            "character_items",
            "item_audit_log",
            "account_character_entries",
        };

        private static readonly Dictionary<string, HashSet<string>> AccountBackupRestoreExcludedColumns =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["character_items"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "item_uid" },
                ["account_cargo_items"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "item_uid" },
                ["item_audit_log"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audit_id", "log_id" },
            };

        private static readonly HashSet<string> AccountBackupDeprecatedOptionalTables =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "account_character_entries",
            };

        public object ExportAccountBackup(int accountId)
        {
            if (accountId <= 0)
                return Error("账号 ID 无效");

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    EnsureAccountBackupSupported(conn, tx);

                    var accountExists = CountRows(conn, tx, "accounts", "account_id = @aid", ("@aid", accountId)) > 0;
                    if (!accountExists)
                        return Error("账号不存在: " + accountId);

                    var characterIds = LoadAccountCharacterIds(conn, tx, accountId, includeDeleted: false);
                    var characterNames = LoadAccountCharacterNameValues(conn, tx, accountId, includeDeleted: false);
                    var tableInfos = LoadAccountBackupTableInfos(conn, tx);
                    var dumps = new List<AccountBackupTableDump>();

                    foreach (var table in SortAccountBackupTables(tableInfos))
                    {
                        var predicate = BuildAccountBackupPredicate(table, accountId, characterIds, characterNames);
                        if (predicate == null)
                            continue;

                        var dump = DumpAccountBackupTable(conn, tx, table, predicate);
                        if (dump.Rows.Count > 0 || table.Name.Equals("accounts", StringComparison.OrdinalIgnoreCase))
                            dumps.Add(dump);
                    }

                    tx.Commit();

                    return new AccountBackupFile
                    {
                        Version = AccountBackupVersion,
                        ExportedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                        AccountID = accountId,
                        CharacterIDs = characterIds,
                        Tables = dumps,
                    };
                }
            }
        }

        public object RestoreAccountBackup(AccountBackupFile file)
        {
            var validation = ValidateAccountBackupFile(file);
            if (validation != null)
                return Error(validation);

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                ExecutePragma(conn, "PRAGMA foreign_keys = ON;");
                using (var tx = conn.BeginTransaction())
                {
                    EnsureAccountBackupSupported(conn, tx);

                    var tableMap = file.Tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
                    if (!tableMap.ContainsKey("accounts"))
                        return Error("备份文件缺少 accounts 表数据");
                    if (!tableMap.ContainsKey("characters") && file.CharacterIDs.Count > 0)
                        return Error("备份文件缺少 characters 表数据");

                    if (tableMap.TryGetValue("characters", out var characterDump))
                    {
                        var dumpCharacterIds = ExtractIntColumnValues(characterDump, "character_id");
                        if (!EqualIntSets(file.CharacterIDs, dumpCharacterIds))
                            return Error("备份文件中的 characterIDs 与 characters 表不一致");
                    }

                    var schemaTables = LoadAccountBackupTableInfos(conn, tx)
                        .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
                    var restorableDumps = new List<AccountBackupTableDump>();
                    foreach (var dump in file.Tables)
                    {
                        if (!schemaTables.TryGetValue(dump.Name, out var targetTable))
                        {
                            if (AccountBackupDeprecatedOptionalTables.Contains(dump.Name))
                                continue;
                            return Error("目标数据库缺少表: " + dump.Name);
                        }

                        NormalizeAccountBackupDumpForTargetSchema(dump, targetTable);

                        foreach (var column in dump.Columns)
                        {
                            if (!targetTable.Columns.ContainsKey(column))
                                return Error("目标数据库表 " + dump.Name + " 缺少列: " + column);
                        }
                        restorableDumps.Add(dump);
                    }

                    var existingCharacterIds = LoadAccountCharacterIds(conn, tx, file.AccountID, includeDeleted: true);
                    var overwroteExistingAccount = AccountHasAnyBackupData(conn, tx, file.AccountID);
                    var deletedExistingCharacterCount = existingCharacterIds.Count;

                    ClearAccountBackupData(conn, tx, file.AccountID, existingCharacterIds);

                    var conflicts = FindConflictingBackupCharacterIds(conn, tx, file.AccountID, file.CharacterIDs);
                    if (conflicts.Count > 0)
                        return Error("备份中的角色 ID 已被其他账号占用: " + string.Join(", ", conflicts));

                    var petConflicts = FindBackupPetHandleConflicts(conn, tx, tableMap, file.CharacterIDs);
                    if (petConflicts.Count > 0)
                        return Error("备份中的宠物句柄已被当前数据库占用: " + string.Join(", ", petConflicts));

                    foreach (var dump in SortAccountBackupDumps(restorableDumps))
                    {
                        RestoreAccountBackupTable(conn, tx, dump);
                    }

                    tx.Commit();

                    return new RestoreAccountBackupResult
                    {
                        Success = true,
                        AccountID = file.AccountID,
                        OverwroteExistingAccount = overwroteExistingAccount,
                        DeletedExistingCharacterCount = deletedExistingCharacterCount,
                        RestoredCharacterCount = file.CharacterIDs.Count,
                        CharacterIDs = file.CharacterIDs,
                    };
                }
            }
        }

        private static string ValidateAccountBackupFile(AccountBackupFile file)
        {
            if (file == null)
                return "备份文件为空";
            if (file.Version != AccountBackupVersion)
                return "不支持的备份文件版本: " + file.Version;
            if (file.AccountID <= 0)
                return "备份文件中的账号 ID 无效";
            if (file.CharacterIDs == null)
                return "备份文件缺少角色 ID 列表";
            if (file.Tables == null || file.Tables.Count == 0)
                return "备份文件没有可恢复的数据表";
            if (file.Tables.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                return "备份文件存在重复表";
            return null;
        }

        private static void NormalizeAccountBackupDumpForTargetSchema(AccountBackupTableDump dump, AccountBackupTableInfo targetTable)
        {
            if (dump == null || targetTable == null)
                return;
            if (!dump.Name.Equals("characters", StringComparison.OrdinalIgnoreCase))
                return;
            if (!targetTable.Columns.ContainsKey("slot_index"))
                return;

            var slotIndex = dump.Columns.FindIndex(c => c.Equals("slot_index", StringComparison.OrdinalIgnoreCase));
            if (slotIndex < 0)
            {
                dump.Columns.Add("slot_index");
                foreach (var row in dump.Rows)
                    row.Add(new AccountBackupValue { Type = "integer", Integer = 0 });
                slotIndex = dump.Columns.Count - 1;
            }

            NormalizeCharacterBackupSlotIndexes(dump, slotIndex);
        }

        private static void NormalizeCharacterBackupSlotIndexes(AccountBackupTableDump dump, int slotIndex)
        {
            var activeRows = new List<(List<AccountBackupValue> Row, long CharacterId, long Slot)>();
            var characterIdIndex = dump.Columns.FindIndex(c => c.Equals("character_id", StringComparison.OrdinalIgnoreCase));
            var deleteFlagIndex = dump.Columns.FindIndex(c => c.Equals("delete_flag", StringComparison.OrdinalIgnoreCase));
            if (characterIdIndex < 0 || slotIndex < 0)
                return;

            foreach (var row in dump.Rows)
            {
                if (row.Count != dump.Columns.Count)
                    continue;
                var deleted = deleteFlagIndex >= 0 && row[deleteFlagIndex].ToInt64() != 0;
                if (deleted)
                    continue;

                activeRows.Add((row, row[characterIdIndex].ToInt64(), row[slotIndex].ToInt64()));
            }

            var seen = new HashSet<long>();
            var needsRebuild = activeRows.Any(entry => entry.Slot < 0 || !seen.Add(entry.Slot));
            if (!needsRebuild)
                return;

            var nextSlot = 0L;
            foreach (var entry in activeRows.OrderBy(e => e.CharacterId))
            {
                entry.Row[slotIndex] = new AccountBackupValue { Type = "integer", Integer = nextSlot };
                nextSlot++;
            }
        }

        private static void EnsureAccountBackupSupported(SqliteConnection conn, SqliteTransaction tx)
        {
            if (!TableExists(conn, tx, "accounts") || !TableExists(conn, tx, "characters"))
                throw new InvalidOperationException("当前数据库缺少 accounts 或 characters 表，无法进行账号备份");
            if (!ColumnExists(conn, tx, "characters", "account_id"))
                throw new InvalidOperationException("当前数据库 characters 表缺少 account_id，无法进行账号备份");
        }

        private static List<int> LoadAccountCharacterIds(SqliteConnection conn, SqliteTransaction tx, int accountId, bool includeDeleted)
        {
            var ids = new List<int>();
            var where = "account_id = @aid";
            if (!includeDeleted && ColumnExists(conn, tx, "characters", "delete_flag"))
                where += " AND delete_flag = 0";

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT character_id FROM characters WHERE " + where + " ORDER BY character_id;";
                cmd.Parameters.AddWithValue("@aid", accountId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        ids.Add(reader.GetInt32(0));
                }
            }
            return ids;
        }

        private static List<object> LoadAccountCharacterNameValues(SqliteConnection conn, SqliteTransaction tx, int accountId, bool includeDeleted)
        {
            var names = new List<object>();
            if (!ColumnExists(conn, tx, "characters", "name"))
                return names;

            var where = "account_id = @aid";
            if (!includeDeleted && ColumnExists(conn, tx, "characters", "delete_flag"))
                where += " AND delete_flag = 0";

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT name FROM characters WHERE " + where + " ORDER BY character_id;";
                cmd.Parameters.AddWithValue("@aid", accountId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        names.Add(reader.GetValue(0));
                }
            }
            return names;
        }

        private static List<AccountBackupTableInfo> LoadAccountBackupTableInfos(SqliteConnection conn, SqliteTransaction tx)
        {
            var names = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        names.Add(reader.GetString(0));
                }
            }

            var tables = new List<AccountBackupTableInfo>();
            foreach (var name in names)
            {
                if (!IsAccountBackupIdentifier(name))
                    continue;

                var columns = LoadAccountBackupColumns(conn, tx, name);
                if (columns.Count == 0)
                    continue;

                tables.Add(new AccountBackupTableInfo(name, columns));
            }
            return tables;
        }

        private static Dictionary<string, AccountBackupColumnInfo> LoadAccountBackupColumns(SqliteConnection conn, SqliteTransaction tx, string tableName)
        {
            var columns = new Dictionary<string, AccountBackupColumnInfo>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "PRAGMA table_info(" + QuoteAccountBackupIdentifier(tableName) + ");";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var name = reader.GetString(1);
                        if (IsAccountBackupIdentifier(name))
                            columns[name] = new AccountBackupColumnInfo(name, reader.GetInt32(5));
                    }
                }
            }
            return columns;
        }

        private static List<AccountBackupTableInfo> SortAccountBackupTables(List<AccountBackupTableInfo> tables)
        {
            return tables
                .OrderBy(t => AccountBackupOrderIndex(t.Name))
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<AccountBackupTableDump> SortAccountBackupDumps(IEnumerable<AccountBackupTableDump> dumps)
        {
            return dumps
                .OrderBy(t => AccountBackupOrderIndex(t.Name))
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
        }

        private static int AccountBackupOrderIndex(string tableName)
        {
            for (var i = 0; i < AccountBackupPreferredOrder.Length; i++)
            {
                if (AccountBackupPreferredOrder[i].Equals(tableName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return AccountBackupPreferredOrder.Length + 100;
        }

        private static AccountBackupPredicate BuildAccountBackupPredicate(
            AccountBackupTableInfo table,
            int accountId,
            List<int> characterIds,
            List<object> characterNames)
        {
            var clauses = new List<string>();
            var parameters = new List<(string Name, object Value)>();

            if (table.Columns.ContainsKey("account_id"))
            {
                clauses.Add(QuoteAccountBackupIdentifier("account_id") + " = @aid");
                parameters.Add(("@aid", accountId));
            }

            if (table.Name.Equals("characters", StringComparison.OrdinalIgnoreCase)
                && table.Columns.ContainsKey("delete_flag"))
            {
                clauses.Clear();
                parameters.Clear();
                clauses.Add("(" + QuoteAccountBackupIdentifier("account_id") + " = @aid AND " + QuoteAccountBackupIdentifier("delete_flag") + " = 0)");
                parameters.Add(("@aid", accountId));
            }
            else if (table.Columns.ContainsKey("character_id") && characterIds.Count > 0)
            {
                clauses.Add(BuildInClause("character_id", characterIds.Cast<object>().ToList(), parameters, "@cid"));
            }

            if (table.Columns.ContainsKey("owner_scope") && table.Columns.ContainsKey("owner_id"))
            {
                var ownerParts = new List<string>();
                ownerParts.Add("(" + QuoteAccountBackupIdentifier("owner_scope") + " = 'account' AND " + QuoteAccountBackupIdentifier("owner_id") + " = @ownerAccountId)");
                parameters.Add(("@ownerAccountId", accountId));
                if (characterIds.Count > 0)
                    ownerParts.Add("(" + QuoteAccountBackupIdentifier("owner_scope") + " = 'character' AND " + BuildInClause("owner_id", characterIds.Cast<object>().ToList(), parameters, "@ownerCid") + ")");
                clauses.Add("(" + string.Join(" OR ", ownerParts) + ")");
            }

            if (table.Columns.ContainsKey("owner_character_id") && characterIds.Count > 0)
                clauses.Add(BuildInClause("owner_character_id", characterIds.Cast<object>().ToList(), parameters, "@ownerCharacter"));
            if (table.Columns.ContainsKey("support_character_id") && characterIds.Count > 0)
                clauses.Add(BuildInClause("support_character_id", characterIds.Cast<object>().ToList(), parameters, "@supportCharacter"));

            if (table.Name.Equals("account_character_entries", StringComparison.OrdinalIgnoreCase)
                && table.Columns.ContainsKey("name")
                && characterNames.Count > 0)
            {
                clauses.Add(BuildInClause("name", characterNames, parameters, "@characterName"));
            }

            if (clauses.Count == 0)
                return null;

            return new AccountBackupPredicate("(" + string.Join(" OR ", clauses) + ")", parameters);
        }

        private static AccountBackupTableDump DumpAccountBackupTable(
            SqliteConnection conn,
            SqliteTransaction tx,
            AccountBackupTableInfo table,
            AccountBackupPredicate predicate)
        {
            var columns = table.Columns.Values.Select(c => c.Name).ToList();
            var query = "SELECT " + string.Join(", ", columns.Select(QuoteAccountBackupIdentifier))
                + " FROM " + QuoteAccountBackupIdentifier(table.Name)
                + " WHERE " + predicate.Sql;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = query;
                foreach (var parameter in predicate.Parameters)
                    cmd.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);

                var dump = new AccountBackupTableDump
                {
                    Name = table.Name,
                    Columns = columns,
                    Rows = new List<List<AccountBackupValue>>(),
                };

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var row = new List<AccountBackupValue>(columns.Count);
                        for (var i = 0; i < columns.Count; i++)
                            row.Add(AccountBackupValue.FromDbValue(reader.GetValue(i)));
                        dump.Rows.Add(row);
                    }
                }
                return dump;
            }
        }

        private static bool AccountHasAnyBackupData(SqliteConnection conn, SqliteTransaction tx, int accountId)
        {
            if (TableExists(conn, tx, "accounts")
                && CountRows(conn, tx, "accounts", "account_id = @aid", ("@aid", accountId)) > 0)
                return true;
            if (TableExists(conn, tx, "characters")
                && CountRows(conn, tx, "characters", "account_id = @aid", ("@aid", accountId)) > 0)
                return true;
            return false;
        }

        private static void ClearAccountBackupData(SqliteConnection conn, SqliteTransaction tx, int accountId, List<int> existingCharacterIds)
        {
            var tables = LoadAccountBackupTableInfos(conn, tx)
                .Where(t => !t.Name.Equals("accounts", StringComparison.OrdinalIgnoreCase)
                    && !t.Name.Equals("characters", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => AccountBackupOrderIndex(t.Name))
                .ThenByDescending(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var table in tables)
            {
                var predicate = BuildAccountBackupDeletePredicate(table, accountId, existingCharacterIds);
                if (predicate == null)
                    continue;

                ExecuteBackupDelete(conn, tx, table.Name, predicate);
            }

            if (TableExists(conn, tx, "characters"))
                ExecuteNonQuery(conn, tx, "DELETE FROM characters WHERE account_id = @aid;", ("@aid", accountId));
            if (TableExists(conn, tx, "accounts"))
                ExecuteNonQuery(conn, tx, "DELETE FROM accounts WHERE account_id = @aid;", ("@aid", accountId));
        }

        private static AccountBackupPredicate BuildAccountBackupDeletePredicate(AccountBackupTableInfo table, int accountId, List<int> characterIds)
        {
            var clauses = new List<string>();
            var parameters = new List<(string Name, object Value)>();

            if (table.Columns.ContainsKey("account_id"))
            {
                clauses.Add(QuoteAccountBackupIdentifier("account_id") + " = @aid");
                parameters.Add(("@aid", accountId));
            }
            if (table.Columns.ContainsKey("character_id") && characterIds.Count > 0)
                clauses.Add(BuildInClause("character_id", characterIds.Cast<object>().ToList(), parameters, "@deleteCid"));
            if (table.Columns.ContainsKey("owner_scope") && table.Columns.ContainsKey("owner_id"))
            {
                var ownerParts = new List<string>();
                ownerParts.Add("(" + QuoteAccountBackupIdentifier("owner_scope") + " = 'account' AND " + QuoteAccountBackupIdentifier("owner_id") + " = @deleteOwnerAccountId)");
                parameters.Add(("@deleteOwnerAccountId", accountId));
                if (characterIds.Count > 0)
                    ownerParts.Add("(" + QuoteAccountBackupIdentifier("owner_scope") + " = 'character' AND " + BuildInClause("owner_id", characterIds.Cast<object>().ToList(), parameters, "@deleteOwnerCid") + ")");
                clauses.Add("(" + string.Join(" OR ", ownerParts) + ")");
            }
            if (table.Columns.ContainsKey("owner_character_id") && characterIds.Count > 0)
                clauses.Add(BuildInClause("owner_character_id", characterIds.Cast<object>().ToList(), parameters, "@deleteOwnerCharacter"));
            if (table.Columns.ContainsKey("support_character_id") && characterIds.Count > 0)
                clauses.Add(BuildInClause("support_character_id", characterIds.Cast<object>().ToList(), parameters, "@deleteSupportCharacter"));

            if (clauses.Count == 0)
                return null;

            return new AccountBackupPredicate("(" + string.Join(" OR ", clauses) + ")", parameters);
        }

        private static void ExecuteBackupDelete(SqliteConnection conn, SqliteTransaction tx, string tableName, AccountBackupPredicate predicate)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM " + QuoteAccountBackupIdentifier(tableName) + " WHERE " + predicate.Sql + ";";
                foreach (var parameter in predicate.Parameters)
                    cmd.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        private static List<int> FindConflictingBackupCharacterIds(SqliteConnection conn, SqliteTransaction tx, int accountId, List<int> characterIds)
        {
            var result = new List<int>();
            if (characterIds.Count == 0)
                return result;

            var parameters = new List<(string Name, object Value)> { ("@aid", accountId) };
            var inClause = BuildInClause("character_id", characterIds.Cast<object>().ToList(), parameters, "@conflictCid");
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT character_id FROM characters WHERE account_id <> @aid AND " + inClause + " ORDER BY character_id;";
                foreach (var parameter in parameters)
                    cmd.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        result.Add(reader.GetInt32(0));
                }
            }
            return result;
        }

        private static List<long> FindBackupPetHandleConflicts(
            SqliteConnection conn,
            SqliteTransaction tx,
            Dictionary<string, AccountBackupTableDump> tableMap,
            List<int> restoringCharacterIds)
        {
            var handles = ExtractBackupPetHandles(tableMap);
            if (handles.Count == 0 || !TableExists(conn, tx, "character_items"))
                return new List<long>();

            var columns = LoadAccountBackupColumns(conn, tx, "character_items");
            if (!columns.ContainsKey("pet_serial_or_handle"))
                return new List<long>();

            var parameters = new List<(string Name, object Value)>();
            var handleClause = BuildInClause("pet_serial_or_handle", handles.Cast<object>().ToList(), parameters, "@petHandle");
            var excludeClause = restoringCharacterIds.Count > 0
                ? " AND NOT (" + BuildInClause("character_id", restoringCharacterIds.Cast<object>().ToList(), parameters, "@petCid") + ")"
                : "";

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT DISTINCT pet_serial_or_handle FROM character_items WHERE " + handleClause + excludeClause + " ORDER BY pet_serial_or_handle;";
                foreach (var parameter in parameters)
                    cmd.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);

                var conflicts = new List<long>();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                            conflicts.Add(Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture));
                    }
                }
                return conflicts;
            }
        }

        private static List<long> ExtractBackupPetHandles(Dictionary<string, AccountBackupTableDump> tableMap)
        {
            var handles = new HashSet<long>();
            if (tableMap.TryGetValue("character_items", out var itemDump))
            {
                var kindIndex = itemDump.Columns.FindIndex(c => c.Equals("item_kind", StringComparison.OrdinalIgnoreCase));
                var handleIndex = itemDump.Columns.FindIndex(c => c.Equals("pet_serial_or_handle", StringComparison.OrdinalIgnoreCase));
                if (kindIndex >= 0 && handleIndex >= 0)
                {
                    foreach (var row in itemDump.Rows)
                    {
                        if (row.Count != itemDump.Columns.Count)
                            continue;
                        var kind = row[kindIndex].ToDbValue() as string;
                        if (!string.Equals(kind, "pet", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var handle = row[handleIndex].ToInt64();
                        if (handle > 0)
                            handles.Add(handle);
                    }
                }
            }

            if (tableMap.TryGetValue("character_creatures", out var creatureDump))
            {
                var handleIndex = creatureDump.Columns.FindIndex(c => c.Equals("creature_key", StringComparison.OrdinalIgnoreCase));
                if (handleIndex >= 0)
                {
                    foreach (var row in creatureDump.Rows)
                    {
                        if (row.Count != creatureDump.Columns.Count)
                            continue;
                        var handle = row[handleIndex].ToInt64();
                        if (handle > 0)
                            handles.Add(handle);
                    }
                }
            }

            return handles.OrderBy(v => v).ToList();
        }

        private static List<int> ExtractIntColumnValues(AccountBackupTableDump dump, string columnName)
        {
            var index = dump.Columns.FindIndex(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return new List<int>();
            return dump.Rows
                .Where(r => r.Count == dump.Columns.Count)
                .Select(r => (int)r[index].ToInt64())
                .OrderBy(v => v)
                .ToList();
        }

        private static void RestoreAccountBackupTable(SqliteConnection conn, SqliteTransaction tx, AccountBackupTableDump dump)
        {
            var restoreColumns = new List<string>();
            var restoreIndexes = new List<int>();
            AccountBackupRestoreExcludedColumns.TryGetValue(dump.Name, out var excluded);

            for (var i = 0; i < dump.Columns.Count; i++)
            {
                if (excluded != null && excluded.Contains(dump.Columns[i]))
                    continue;
                restoreColumns.Add(dump.Columns[i]);
                restoreIndexes.Add(i);
            }

            if (restoreColumns.Count == 0 || dump.Rows.Count == 0)
                return;

            var placeholders = string.Join(", ", restoreColumns.Select((_, i) => "@p" + i.ToString(CultureInfo.InvariantCulture)));
            var sql = "INSERT INTO " + QuoteAccountBackupIdentifier(dump.Name)
                + " (" + string.Join(", ", restoreColumns.Select(QuoteAccountBackupIdentifier)) + ")"
                + " VALUES (" + placeholders + ");";

            for (var rowIndex = 0; rowIndex < dump.Rows.Count; rowIndex++)
            {
                var row = dump.Rows[rowIndex];
                if (row.Count != dump.Columns.Count)
                    throw new InvalidOperationException("备份表 " + dump.Name + " 第 " + (rowIndex + 1) + " 行列数不一致");

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    for (var i = 0; i < restoreIndexes.Count; i++)
                        cmd.Parameters.AddWithValue("@p" + i.ToString(CultureInfo.InvariantCulture), row[restoreIndexes[i]].ToDbValue() ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static int CountRows(SqliteConnection conn, SqliteTransaction tx, string tableName, string whereSql, params (string Name, object Value)[] parameters)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(1) FROM " + QuoteAccountBackupIdentifier(tableName) + " WHERE " + whereSql + ";";
                foreach (var parameter in parameters)
                    cmd.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private static string BuildInClause(
            string columnName,
            List<object> values,
            List<(string Name, object Value)> parameters,
            string prefix)
        {
            var names = new List<string>();
            for (var i = 0; i < values.Count; i++)
            {
                var name = prefix + "_" + parameters.Count.ToString(CultureInfo.InvariantCulture);
                names.Add(name);
                parameters.Add((name, values[i]));
            }
            return QuoteAccountBackupIdentifier(columnName) + " IN (" + string.Join(", ", names) + ")";
        }

        private static bool EqualIntSets(List<int> left, List<int> right)
        {
            return left.OrderBy(v => v).SequenceEqual(right.OrderBy(v => v));
        }

        private static bool IsAccountBackupIdentifier(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && AccountBackupIdentifier.IsMatch(name);
        }

        private static string QuoteAccountBackupIdentifier(string name)
        {
            if (!IsAccountBackupIdentifier(name))
                throw new InvalidOperationException("无效的数据库标识符: " + name);
            return "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        private static void ExecutePragma(SqliteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        private sealed class AccountBackupColumnInfo
        {
            public AccountBackupColumnInfo(string name, int primaryKeyIndex)
            {
                Name = name;
                PrimaryKeyIndex = primaryKeyIndex;
            }

            public string Name { get; }
            public int PrimaryKeyIndex { get; }
        }

        private sealed class AccountBackupTableInfo
        {
            public AccountBackupTableInfo(string name, Dictionary<string, AccountBackupColumnInfo> columns)
            {
                Name = name;
                Columns = columns;
            }

            public string Name { get; }
            public Dictionary<string, AccountBackupColumnInfo> Columns { get; }
        }

        private sealed class AccountBackupPredicate
        {
            public AccountBackupPredicate(string sql, List<(string Name, object Value)> parameters)
            {
                Sql = sql;
                Parameters = parameters;
            }

            public string Sql { get; }
            public List<(string Name, object Value)> Parameters { get; }
        }
    }

    public sealed class AccountBackupValue
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("integer")]
        public long? Integer { get; set; }

        [JsonPropertyName("real")]
        public double? Real { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("blob")]
        public string Blob { get; set; }

        public static AccountBackupValue FromDbValue(object value)
        {
            if (value == null || value == DBNull.Value)
                return new AccountBackupValue { Type = "null" };
            if (value is byte[] bytes)
                return new AccountBackupValue { Type = "blob", Blob = Convert.ToBase64String(bytes) };
            if (value is string text)
                return new AccountBackupValue { Type = "text", Text = text };
            if (value is float || value is double || value is decimal)
                return new AccountBackupValue { Type = "real", Real = Convert.ToDouble(value, CultureInfo.InvariantCulture) };
            return new AccountBackupValue { Type = "integer", Integer = Convert.ToInt64(value, CultureInfo.InvariantCulture) };
        }

        public object ToDbValue()
        {
            switch (Type)
            {
                case "null":
                    return null;
                case "integer":
                    if (!Integer.HasValue)
                        throw new InvalidOperationException("整数备份值缺少 integer 字段");
                    return Integer.Value;
                case "real":
                    if (!Real.HasValue)
                        throw new InvalidOperationException("浮点备份值缺少 real 字段");
                    return Real.Value;
                case "text":
                    return Text ?? string.Empty;
                case "blob":
                    return Convert.FromBase64String(Blob ?? string.Empty);
                default:
                    throw new InvalidOperationException("不支持的备份值类型: " + Type);
            }
        }

        public long ToInt64()
        {
            var value = ToDbValue();
            return value == null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
    }

    public sealed class AccountBackupTableDump
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("columns")]
        public List<string> Columns { get; set; } = new List<string>();

        [JsonPropertyName("rows")]
        public List<List<AccountBackupValue>> Rows { get; set; } = new List<List<AccountBackupValue>>();
    }

    public sealed class AccountBackupFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("exportedAt")]
        public string ExportedAt { get; set; }

        [JsonPropertyName("accountID")]
        public int AccountID { get; set; }

        [JsonPropertyName("characterIDs")]
        public List<int> CharacterIDs { get; set; } = new List<int>();

        [JsonPropertyName("tables")]
        public List<AccountBackupTableDump> Tables { get; set; } = new List<AccountBackupTableDump>();
    }

    public sealed class RestoreAccountBackupResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("accountID")]
        public int AccountID { get; set; }

        [JsonPropertyName("overwroteExistingAccount")]
        public bool OverwroteExistingAccount { get; set; }

        [JsonPropertyName("deletedExistingCharacterCount")]
        public int DeletedExistingCharacterCount { get; set; }

        [JsonPropertyName("restoredCharacterCount")]
        public int RestoredCharacterCount { get; set; }

        [JsonPropertyName("characterIDs")]
        public List<int> CharacterIDs { get; set; } = new List<int>();
    }
}
