using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DfoGmTool.ServerCore.Game.Inventory;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        private const int DefaultCharacterSlotLimit = 17;
        private static readonly SemaphoreSlim CharacterCloneMutationGate = new SemaphoreSlim(1, 1);

        private static readonly CharacterCloneOption[] CharacterCloneOptions =
        {
            new CharacterCloneOption("basic", "角色基础信息", true),
            new CharacterCloneOption("skills", "技能/技能点/技能栏", true),
            new CharacterCloneOption("quests", "任务与已完成记录", true),
            new CharacterCloneOption("titlebook", "称号簿/成就", true),
            new CharacterCloneOption("dungeon", "地图难度/副本状态", true),
            new CharacterCloneOption("daily", "每日/周常状态", true),
            new CharacterCloneOption("wallet", "角色金币/复活币/技能点货币行", true),
            new CharacterCloneOption("quickSlots", "快捷栏", true),
            new CharacterCloneOption("mainEquipment", "装备背包", true),
            new CharacterCloneOption("consumables", "消耗品背包", true),
            new CharacterCloneOption("materials", "材料背包", true),
            new CharacterCloneOption("questItems", "任务品背包", true),
            new CharacterCloneOption("expertMaterials", "副职业材料背包", true),
            new CharacterCloneOption("emblems", "徽章背包", true),
            new CharacterCloneOption("personalCargo", "个人仓库", true),
            new CharacterCloneOption("equipped", "身上装备/称号/穿戴记录", true),
            new CharacterCloneOption("avatars", "装扮栏", true),
            new CharacterCloneOption("pets", "宠物", true),
            new CharacterCloneOption("petEquipment", "宠物装备", true),
            new CharacterCloneOption("petConsumables", "宠物用品", true),
            new CharacterCloneOption("locks", "锁定/排序状态", true),
            new CharacterCloneOption("misc", "其他角色状态表", true),
            new CharacterCloneOption("audit", "物品审计日志", false),
        };

        private static readonly Dictionary<string, string[]> CharacterCloneTableGroups =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["basic"] = new[] { "character_subtype0_fields", "character_subtype1_fields", "character_init_flags" },
                ["skills"] = new[] { "character_skills", "character_pvp_skill_state", "character_pvp_skills", "character_dark_knight_combo_skill_pages", "character_hotkey_slots" },
                ["quests"] = new[] { "character_active_quests", "character_quest_notify_selections", "character_invisible_falgs" },
                ["titlebook"] = new[] { "character_achievement_complete", "character_new_titlebook" },
                ["dungeon"] = new[] { "character_dungeon_permissions", "character_dimensions", "character_dimension_flags", "character_growth_weapon_stages", "character_pvp_missions", "character_tower_of_despair_progress" },
                ["daily"] = new[] { "character_daily_reset", "character_daily_counters", "character_daily_challenge_groups", "character_daily_challenge_entries", "character_daily_challenge_claims", "character_daily_challenge_tail_ids", "character_daily_schedule_states", "character_buy_restrict_items", "character_crystal_contract" },
                ["wallet"] = new[] { "character_gold_limits" },
                ["equipped"] = new[] { "character_rental_items", "character_knight_shield_deck", "character_name_tag_state" },
                ["pets"] = new[] { "character_creatures", "character_pet_welcome_cache" },
                ["locks"] = new[] { "character_item_locks", "character_sort_item_locks" },
                ["misc"] = new[] { "character_item_values", "character_collectbox_slots", "character_mercenary_support", "character_expert_job", "character_expert_job_recipes" },
                ["audit"] = new[] { "item_audit_log" },
            };

        public object GetCharacterClonePlan(int sourceCharacterId)
        {
            int sourceAccountId;
            if (!TryGetAccountId(sourceCharacterId, out sourceAccountId))
                return Error("角色不存在: " + sourceCharacterId);

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                var slotLimit = ResolveCharacterSlotLimit(conn, null);
                var accounts = new List<object>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT a.account_id, a.m_id, COUNT(c.character_id) AS character_count
FROM accounts a
LEFT JOIN characters c ON c.account_id = a.account_id AND c.delete_flag = 0
GROUP BY a.account_id, a.m_id
ORDER BY a.account_id;";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var accountId = reader.GetInt32(0);
                            var count = reader.GetInt32(2);
                            accounts.Add(new
                            {
                                accountId,
                                name = reader.GetString(1),
                                characterCount = count,
                                slotLimit,
                                canAcceptCharacter = count < slotLimit,
                                isCurrent = accountId == sourceAccountId,
                            });
                        }
                    }
                }

                return new
                {
                    sourceCharacterId,
                    sourceAccountId,
                    slotLimit,
                    accounts,
                    options = CharacterCloneOptions.Select(o => new { key = o.Key, label = o.Label, defaultChecked = o.DefaultChecked }).ToList(),
                };
            }
        }

        public object CheckCharacterNameAvailable(string name)
        {
            var normalized = (name ?? string.Empty).Trim();
            var invalid = ValidateCharacterName(normalized);
            if (invalid != null)
                return new { success = true, available = false, reason = invalid };

            using (var conn = new SqliteConnection(_config.ConnectionString))
            using (var cmd = conn.CreateCommand())
            {
                conn.Open();
                cmd.CommandText = @"
SELECT COUNT(1)
FROM characters
WHERE delete_flag = 0
  AND (name = @name OR name = @nameBytes OR name_bytes = @nameBytes);";
                cmd.Parameters.AddWithValue("@name", normalized);
                cmd.Parameters.AddWithValue("@nameBytes", Encoding.UTF8.GetBytes(normalized));
                var exists = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
                return new { success = true, available = !exists, reason = exists ? "角色名已存在" : "" };
            }
        }

        public object CreateAccountForClone(string accountName, string password, string confirmPassword)
        {
            var name = (accountName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Error("账号名不能为空");
            if (string.IsNullOrEmpty(password))
                return Error("密码不能为空");
            if (password != confirmPassword)
                return Error("两次输入的密码不一致");

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    if (AccountNameExists(conn, tx, name))
                        return Error("账号名已存在: " + name);

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
INSERT INTO accounts (m_id, password_hash) VALUES (@mid, @pwd);
SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("@mid", name);
                        cmd.Parameters.AddWithValue("@pwd", ComputeMd5Hex(password));
                        var accountId = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                        tx.Commit();
                        return new { success = true, accountId, name };
                    }
                }
            }
        }

        public object CloneCharacter(int sourceCharacterId, CharacterCloneRequest request)
        {
            if (request == null)
                return Error("复制参数为空");
            if (request.TargetAccountId <= 0)
                return Error("请选择目标账号");

            var newName = (request.NewName ?? string.Empty).Trim();
            var invalidName = ValidateCharacterName(newName);
            if (invalidName != null)
                return Error(invalidName);

            var selected = NormalizeCloneOptions(request.Options);
            CharacterCloneMutationGate.Wait();
            try
            {
                using (var conn = new SqliteConnection(_config.ConnectionString))
                {
                    conn.Open();
                    ExecutePragma(conn, "PRAGMA foreign_keys = ON;");
                    ExecutePragma(conn, "PRAGMA busy_timeout = 5000;");
                    using (var tx = conn.BeginTransaction(deferred: false))
                    {
                        if (!CharacterExists(conn, tx, sourceCharacterId, out var sourceAccountId))
                            return Error("源角色不存在: " + sourceCharacterId);
                        if (!AccountExists(conn, tx, request.TargetAccountId))
                            return Error("目标账号不存在: " + request.TargetAccountId);
                        if (CharacterNameExists(conn, tx, newName))
                            return Error("角色名已存在: " + newName);

                        var slotLimit = ResolveCharacterSlotLimit(conn, tx);
                        var targetCount = CountCharactersByAccount(conn, tx, request.TargetAccountId);
                        if (targetCount >= slotLimit)
                            return Error($"目标账号角色数量已达上限 {slotLimit}");

                        var targetSlotIndex = ResolveFreeCharacterSlotIndex(conn, tx, request.TargetAccountId, slotLimit);
                        if (targetSlotIndex < 0)
                            return Error("目标账号没有可用角色槽位");

                        var cloneTables = ResolveSelectedCloneTables(conn, tx, selected);
                        ValidateCloneTableSafety(conn, tx, cloneTables);

                        var newCharacterId = CloneCharacterRow(conn, tx, sourceCharacterId, request.TargetAccountId, newName, targetSlotIndex);
                        DeleteStaleCloneInventory(conn, tx, newCharacterId);

                        foreach (var tableName in cloneTables)
                        {
                            if (tableName.Equals("character_creatures", StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (tableName.Equals("item_audit_log", StringComparison.OrdinalIgnoreCase))
                            {
                                CloneGenericTable(conn, tx, tableName, sourceCharacterId, newCharacterId, request.TargetAccountId, null, cloneAudit: true);
                                continue;
                            }

                            CloneGenericTable(conn, tx, tableName, sourceCharacterId, newCharacterId, request.TargetAccountId, null, cloneAudit: false);
                        }

                        var creatureUidMap = CloneSelectedCreatureDetails(conn, tx, sourceCharacterId, newCharacterId, selected);
                        CloneSelectedItems(conn, tx, sourceCharacterId, newCharacterId, selected, creatureUidMap);
                        CloneSelectedContainerStates(conn, tx, sourceCharacterId, newCharacterId, selected);
                        var strippedEquipmentCount = StripJobRestrictedEquippedItems(conn, tx, newCharacterId, selected);
                        ValidateClonedInventoryLayout(conn, tx, newCharacterId);
                        ValidateClonedCharacter(conn, tx, newCharacterId, request.TargetAccountId, targetSlotIndex);

                        tx.Commit();
                        return new
                        {
                            success = true,
                            sourceCharacterId,
                            characterId = newCharacterId,
                            targetAccountId = request.TargetAccountId,
                            sourceAccountId,
                            name = newName,
                            slotIndex = targetSlotIndex,
                            strippedEquipmentCount,
                            copiedOptions = selected.OrderBy(v => v).ToList(),
                        };
                    }
                }
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6)
            {
                return Error("数据库正忙，复制未执行；请稍后重试（未写入半成品）");
            }
            catch (Exception ex)
            {
                return Error("复制失败，事务已回滚: " + ex.Message);
            }
            finally
            {
                CharacterCloneMutationGate.Release();
            }
        }

        private static HashSet<string> NormalizeCloneOptions(IEnumerable<string> options)
        {
            var known = new HashSet<string>(CharacterCloneOptions.Select(o => o.Key), StringComparer.OrdinalIgnoreCase);
            var selected = options == null
                ? new HashSet<string>(CharacterCloneOptions.Where(o => o.DefaultChecked).Select(o => o.Key), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(options.Where(o => known.Contains(o)), StringComparer.OrdinalIgnoreCase);

            selected.Add("basic");
            return selected;
        }

        private static IReadOnlyList<string> ResolveSelectedCloneTables(SqliteConnection conn, SqliteTransaction tx, HashSet<string> selected)
        {
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in selected)
            {
                if (!CharacterCloneTableGroups.TryGetValue(key, out var groupTables))
                    continue;
                foreach (var table in groupTables)
                    tables.Add(table);
            }
            foreach (var table in DiscoverDynamicCharacterCloneTables(conn, tx))
                tables.Add(table);
            tables.Remove("characters");
            tables.Remove("character_items");
            tables.Remove("character_new_items");
            tables.Remove("character_avatar_detail");
            tables.Remove("character_container_state");
            return tables.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static int CloneCharacterRow(SqliteConnection conn, SqliteTransaction tx, int sourceCharacterId, int targetAccountId, string newName, int targetSlotIndex)
        {
            var columns = LoadAccountBackupColumns(conn, tx, "characters").Values.Select(c => c.Name).ToList();
            var source = LoadSingleRow(conn, tx, "characters", "character_id = @cid", ("@cid", sourceCharacterId));
            if (source == null)
                throw new InvalidOperationException("源角色不存在");

            var insertColumns = columns.Where(c => !c.Equals("character_id", StringComparison.OrdinalIgnoreCase)).ToList();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO characters (" + string.Join(", ", insertColumns.Select(QuoteAccountBackupIdentifier)) + ") VALUES ("
                    + string.Join(", ", insertColumns.Select((_, i) => "@p" + i.ToString(CultureInfo.InvariantCulture))) + "); SELECT last_insert_rowid();";
                for (var i = 0; i < insertColumns.Count; i++)
                {
                    var column = insertColumns[i];
                    object value = source[column];
                    if (column.Equals("account_id", StringComparison.OrdinalIgnoreCase))
                        value = targetAccountId;
                    else if (column.Equals("name", StringComparison.OrdinalIgnoreCase))
                        value = Encoding.UTF8.GetBytes(newName);
                    else if (column.Equals("name_bytes", StringComparison.OrdinalIgnoreCase))
                        value = Encoding.UTF8.GetBytes(newName);
                    else if (column.Equals("delete_flag", StringComparison.OrdinalIgnoreCase))
                        value = 0;
                    else if (column.Equals("slot_index", StringComparison.OrdinalIgnoreCase))
                        value = targetSlotIndex;
                    else if (column.Equals("created_at", StringComparison.OrdinalIgnoreCase) || column.Equals("updated_at", StringComparison.OrdinalIgnoreCase))
                        value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    cmd.Parameters.AddWithValue("@p" + i.ToString(CultureInfo.InvariantCulture), value ?? DBNull.Value);
                }
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private static void CloneGenericTable(
            SqliteConnection conn,
            SqliteTransaction tx,
            string tableName,
            int sourceCharacterId,
            int newCharacterId,
            int targetAccountId,
            Func<Dictionary<string, object>, bool> rowFilter,
            bool cloneAudit)
        {
            var columnInfos = LoadCloneColumns(conn, tx, tableName);
            var columns = columnInfos.Select(c => c.Name).ToList();
            var where = BuildCharacterTableWhere(columns, tableName);
            if (where == null)
                return;

            foreach (var row in LoadRows(conn, tx, tableName, where, ("@cid", sourceCharacterId)))
            {
                if (rowFilter != null && !rowFilter(row))
                    continue;

                var insertColumns = columns
                    .Where(c => !ShouldSkipCloneColumn(tableName, c, cloneAudit)
                        && !IsGeneratedIntegerPrimaryKey(columnInfos, c))
                    .ToList();
                InsertClonedRow(conn, tx, tableName, insertColumns, row, sourceCharacterId, newCharacterId, targetAccountId, cloneAudit);
            }
        }

        private static void CloneSelectedItems(
            SqliteConnection conn,
            SqliteTransaction tx,
            int sourceCharacterId,
            int newCharacterId,
            HashSet<string> selected,
            IReadOnlyDictionary<int, int> creatureUidMap)
        {
            if (!TableExists(conn, tx, "character_new_items"))
                return;

            var ranges = ResolveSelectedItemRanges(conn, tx, sourceCharacterId, selected);
            if (ranges.Count == 0)
                return;

            foreach (var row in LoadRows(conn, tx, "character_new_items", "character_id = @cid OR (owner_scope = 'character' AND owner_id = @cid)", ("@cid", sourceCharacterId)))
            {
                var listType = ToInt(row, "list_type");
                var slot = ToInt(row, "slot_index");
                if (!ranges.Any(r => r.Contains(listType, slot)))
                    continue;

                var bytes = row["item_core"] as byte[];
                if (bytes == null || bytes.Length != ItemCore.Size)
                    throw new InvalidOperationException($"源角色新版物品损坏: list={listType} slot={slot}");
                var core = ItemCore.FromBytes(bytes);
                if (core.ItemKind == ItemCore.KindAvatar)
                {
                    var oldAvatarUid = core.Value;
                    var newAvatarUid = AllocateClonedAvatarUid(conn, tx);
                    CloneAvatarDetail(conn, tx, oldAvatarUid, newAvatarUid, newCharacterId);
                    core.Value = newAvatarUid;
                }
                else if (core.ItemKind == ItemCore.KindCreature && core.Value > 0)
                {
                    if (creatureUidMap == null || !creatureUidMap.TryGetValue(core.Value, out var newCreatureUid))
                        throw new InvalidOperationException($"复制宠物缺少 UID 映射: source={sourceCharacterId} uid={core.Value}");
                    core.Value = newCreatureUid;
                }

                using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = @"INSERT INTO character_new_items
(owner_scope,owner_id,character_id,list_type,slot_index,item_core,created_at,updated_at)
VALUES('character',@cid,@cid,@list,@slot,@core,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);";
                insert.Parameters.AddWithValue("@cid", newCharacterId);
                insert.Parameters.AddWithValue("@list", listType);
                insert.Parameters.AddWithValue("@slot", slot);
                insert.Parameters.AddWithValue("@core", core.ToBytes());
                try { insert.ExecuteNonQuery(); }
                catch (SqliteException ex)
                {
                    throw new InvalidOperationException($"复制新版物品冲突: source={sourceCharacterId} target={newCharacterId} list={listType} slot={slot}", ex);
                }
            }
        }

        private static Dictionary<int, int> CloneSelectedCreatureDetails(
            SqliteConnection conn,
            SqliteTransaction tx,
            int sourceCharacterId,
            int newCharacterId,
            HashSet<string> selected)
        {
            var result = new Dictionary<int, int>();
            if (!TableExists(conn, tx, "character_new_items") || !TableExists(conn, tx, "character_creatures"))
                return result;

            var ranges = ResolveSelectedItemRanges(conn, tx, sourceCharacterId, selected);
            var referencedUids = new HashSet<int>();
            foreach (var row in LoadRows(conn, tx, "character_new_items", "character_id = @cid", ("@cid", sourceCharacterId)))
            {
                var listType = ToInt(row, "list_type");
                var slot = ToInt(row, "slot_index");
                if (!ranges.Any(range => range.Contains(listType, slot)))
                    continue;
                if (!(row["item_core"] is byte[] bytes) || bytes.Length != ItemCore.Size)
                    continue;
                var core = ItemCore.FromBytes(bytes);
                if (core.ItemKind == ItemCore.KindCreature && core.Value > 0)
                    referencedUids.Add(core.Value);
            }

            var copiedSortOrders = new HashSet<int>();
            var sourceDetails = LoadRows(conn, tx, "character_creatures", "character_id = @cid", ("@cid", sourceCharacterId));
            foreach (var row in sourceDetails)
            {
                var oldUid = ToInt(row, "creature_key");
                if (oldUid <= 0 || (!selected.Contains("pets") && !referencedUids.Contains(oldUid)))
                    continue;
                var newUid = GetOrAllocateClonedCreatureUid(conn, tx, oldUid, result);
                var sortOrder = ToInt(row, "sort_order");
                copiedSortOrders.Add(sortOrder);
                using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = @"INSERT INTO character_creatures
(character_id,sort_order,creature_key,field04,mode_flag,progress_value,mode1_field0a,mode1_field0b,field_after_value,creature_text,tail_flag,extra_json)
VALUES(@cid,@sort,@uid,@field04,@mode,@progress,@field0a,@field0b,@after,@text,@tail,@extra);";
                insert.Parameters.AddWithValue("@cid", newCharacterId);
                insert.Parameters.AddWithValue("@sort", sortOrder);
                insert.Parameters.AddWithValue("@uid", newUid);
                insert.Parameters.AddWithValue("@field04", row["field04"] ?? 0);
                insert.Parameters.AddWithValue("@mode", row["mode_flag"] ?? 0);
                insert.Parameters.AddWithValue("@progress", row["progress_value"] ?? 0);
                insert.Parameters.AddWithValue("@field0a", row["mode1_field0a"] ?? 0);
                insert.Parameters.AddWithValue("@field0b", row["mode1_field0b"] ?? 0);
                insert.Parameters.AddWithValue("@after", row["field_after_value"] ?? 0);
                insert.Parameters.AddWithValue("@text", row["creature_text"] ?? DBNull.Value);
                insert.Parameters.AddWithValue("@tail", row["tail_flag"] ?? 0);
                insert.Parameters.AddWithValue("@extra", row["extra_json"] ?? "{}");
                insert.ExecuteNonQuery();
            }

            var nextSortOrder = copiedSortOrders.Count == 0 ? 0 : copiedSortOrders.Max() + 1;
            foreach (var oldUid in referencedUids.OrderBy(value => value))
            {
                if (result.ContainsKey(oldUid))
                    continue;
                var newUid = GetOrAllocateClonedCreatureUid(conn, tx, oldUid, result);
                while (copiedSortOrders.Contains(nextSortOrder))
                    nextSortOrder++;
                using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = @"INSERT INTO character_creatures
(character_id,sort_order,creature_key,field04,mode_flag,progress_value,mode1_field0a,mode1_field0b,field_after_value,creature_text,tail_flag,extra_json)
VALUES(@cid,@sort,@uid,100,0,0,0,0,1,NULL,0,'{}');";
                insert.Parameters.AddWithValue("@cid", newCharacterId);
                insert.Parameters.AddWithValue("@sort", nextSortOrder++);
                insert.Parameters.AddWithValue("@uid", newUid);
                insert.ExecuteNonQuery();
            }
            return result;
        }

        private static int GetOrAllocateClonedCreatureUid(
            SqliteConnection conn,
            SqliteTransaction tx,
            int oldUid,
            IDictionary<int, int> mapping)
        {
            if (mapping.TryGetValue(oldUid, out var existing))
                return existing;
            using (var ensure = conn.CreateCommand())
            {
                ensure.Transaction = tx;
                ensure.CommandText = @"CREATE TABLE IF NOT EXISTS character_creature_uid_sequence (
creature_uid INTEGER PRIMARY KEY AUTOINCREMENT);
INSERT OR IGNORE INTO character_creature_uid_sequence(creature_uid)
SELECT COALESCE(MAX(creature_key),0) FROM character_creatures WHERE creature_key > 0;";
                ensure.ExecuteNonQuery();
            }
            using var allocate = conn.CreateCommand();
            allocate.Transaction = tx;
            allocate.CommandText = "INSERT INTO character_creature_uid_sequence DEFAULT VALUES; SELECT last_insert_rowid();";
            var newUid = checked((int)Convert.ToInt64(allocate.ExecuteScalar(), CultureInfo.InvariantCulture));
            mapping[oldUid] = newUid;
            return newUid;
        }

        private static int AllocateClonedAvatarUid(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO character_avatar_uid_sequence DEFAULT VALUES; SELECT last_insert_rowid();";
            return checked((int)Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture));
        }

        private static void DeleteStaleCloneInventory(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            var avatarUids = new List<int>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT item_core FROM character_new_items
WHERE owner_scope='character' AND owner_id=@cid AND character_id IS NULL;";
                command.Parameters.AddWithValue("@cid", characterId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var bytes = reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0);
                    if (bytes == null || bytes.Length != ItemCore.Size) continue;
                    var core = ItemCore.FromBytes(bytes);
                    if (core.ItemKind == ItemCore.KindAvatar && core.Value > 0) avatarUids.Add(core.Value);
                }
            }
            foreach (var avatarUid in avatarUids)
            {
                using var detail = connection.CreateCommand();
                detail.Transaction = transaction;
                detail.CommandText = "DELETE FROM character_avatar_detail WHERE item_uid=@uid;";
                detail.Parameters.AddWithValue("@uid", avatarUid);
                detail.ExecuteNonQuery();
            }
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = @"DELETE FROM character_new_items
WHERE owner_scope='character' AND owner_id=@cid AND character_id IS NULL;";
            delete.Parameters.AddWithValue("@cid", characterId);
            delete.ExecuteNonQuery();
        }

        private static void CloneAvatarDetail(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int oldAvatarUid,
            int newAvatarUid,
            int newCharacterId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO character_avatar_detail
(item_uid,owner_id,character_id,item_id,expire_date,clear_avatar_id,jewel_socket,color1,color2,delete_date)
SELECT @newUid,@cid,@cid,item_id,expire_date,clear_avatar_id,jewel_socket,color1,color2,delete_date
FROM character_avatar_detail WHERE item_uid=@oldUid;";
            command.Parameters.AddWithValue("@newUid", newAvatarUid);
            command.Parameters.AddWithValue("@oldUid", oldAvatarUid);
            command.Parameters.AddWithValue("@cid", newCharacterId);
            if (command.ExecuteNonQuery() != 0)
                return;

            command.CommandText = @"INSERT INTO character_avatar_detail
(item_uid,owner_id,character_id,item_id,expire_date,clear_avatar_id,jewel_socket,color1,color2,delete_date)
VALUES(@newUid,@cid,@cid,0,0,0,zeroblob(30),0,0,0);";
            command.ExecuteNonQuery();
        }

        private static void CloneSelectedContainerStates(SqliteConnection conn, SqliteTransaction tx, int sourceCharacterId, int newCharacterId, HashSet<string> selected)
        {
            if (!TableExists(conn, tx, "character_container_state"))
                return;

            var listTypes = new HashSet<int>();
            if (selected.Overlaps(new[] { "wallet", "quickSlots", "mainEquipment", "consumables", "materials", "questItems", "expertMaterials", "emblems", "equipped" }))
                listTypes.Add(0);
            if (selected.Contains("avatars") || selected.Contains("equipped"))
                listTypes.Add(1);
            if (selected.Contains("personalCargo"))
                listTypes.Add(2);
            if (selected.Overlaps(new[] { "pets", "petEquipment", "petConsumables", "equipped" }))
                listTypes.Add(7);

            if (listTypes.Count == 0)
                return;

            var columns = LoadAccountBackupColumns(conn, tx, "character_container_state").Values.Select(c => c.Name).ToList();
            foreach (var row in LoadRows(conn, tx, "character_container_state", "character_id = @cid", ("@cid", sourceCharacterId)))
            {
                if (!listTypes.Contains(ToInt(row, "list_type")))
                    continue;

                InsertClonedRow(conn, tx, "character_container_state", columns, row, sourceCharacterId, newCharacterId, 0, cloneAudit: false);
            }
        }

        private static List<ItemCloneRange> ResolveSelectedItemRanges(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            HashSet<string> selected)
        {
            var ranges = new List<ItemCloneRange>();
            var mainExpandStage = LoadCloneContainerListParam(connection, transaction, characterId, 0, 24);
            if (mainExpandStage != 0 && mainExpandStage != 8 && mainExpandStage != 16 && mainExpandStage != 24)
                throw new InvalidOperationException($"源角色主背包扩展状态无效: {mainExpandStage}");
            var personalCargoCapacity = LoadCloneContainerListParam(connection, transaction, characterId, 2, 8);
            personalCargoCapacity = personalCargoCapacity <= 0 ? 8 : Math.Min(personalCargoCapacity, 152);
            var exEquipSlotStat = LoadCloneExtraEquipmentSlotStat(connection, transaction, characterId);

            if (selected.Contains("wallet")) ranges.Add(new ItemCloneRange(0, 0, 2));
            if (selected.Contains("quickSlots")) ranges.Add(new ItemCloneRange(0, 3, 8));
            if (selected.Contains("mainEquipment")) ranges.Add(new ItemCloneRange(0, 9, GetExpandedCloneMainEnd(64, mainExpandStage)));
            if (selected.Contains("consumables")) ranges.Add(new ItemCloneRange(0, 65, GetExpandedCloneMainEnd(120, mainExpandStage)));
            if (selected.Contains("materials")) ranges.Add(new ItemCloneRange(0, 121, GetExpandedCloneMainEnd(176, mainExpandStage)));
            if (selected.Contains("questItems")) ranges.Add(new ItemCloneRange(0, 177, GetExpandedCloneMainEnd(232, mainExpandStage)));
            if (selected.Contains("expertMaterials")) ranges.Add(new ItemCloneRange(0, 233, GetExpandedCloneMainEnd(288, mainExpandStage)));
            if (selected.Contains("emblems")) ranges.Add(new ItemCloneRange(0, 289, 351));
            if (selected.Contains("personalCargo")) ranges.Add(new ItemCloneRange(2, 0, personalCargoCapacity - 1));
            if (selected.Contains("avatars")) ranges.Add(new ItemCloneRange(1, 0, 209));
            if (selected.Contains("equipped"))
            {
                ranges.Add(new ItemCloneRange(3, 0, 20));
                for (var slot = 21; slot <= 23; slot++)
                    if ((exEquipSlotStat & (1 << (slot - 21))) != 0)
                        ranges.Add(new ItemCloneRange(3, slot, slot));
                ranges.Add(new ItemCloneRange(3, 24, 27));
                ranges.Add(new ItemCloneRange(3, 29, 29));
            }
            if (selected.Contains("pets")) ranges.Add(new ItemCloneRange(7, 0, 139));
            if (selected.Contains("petEquipment")) ranges.Add(new ItemCloneRange(7, 140, 188));
            if (selected.Contains("petConsumables")) ranges.Add(new ItemCloneRange(7, 189, 239));
            return ranges;
        }

        private static int LoadCloneContainerListParam(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int listType,
            int defaultValue)
        {
            if (!TableExists(connection, transaction, "character_container_state"))
                return defaultValue;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"SELECT list_param16
FROM character_container_state
WHERE character_id=@cid AND list_type=@listType;";
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@listType", listType);
            var value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? defaultValue
                : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static int LoadCloneExtraEquipmentSlotStat(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT ex_equip_slot_stat FROM characters WHERE character_id=@cid;";
            command.Parameters.AddWithValue("@cid", characterId);
            var value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? 0
                : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static int GetExpandedCloneMainEnd(int fullEnd, int mainExpandStage)
        {
            return fullEnd - (24 - mainExpandStage);
        }

        private static string BuildCharacterTableWhere(List<string> columns, string tableName)
        {
            if (tableName.Equals("character_mercenary_support", StringComparison.OrdinalIgnoreCase)
                && columns.Contains("owner_character_id", StringComparer.OrdinalIgnoreCase))
                return "owner_character_id = @cid";
            if (columns.Contains("character_id", StringComparer.OrdinalIgnoreCase))
                return "character_id = @cid";
            if (columns.Contains("owner_scope", StringComparer.OrdinalIgnoreCase) && columns.Contains("owner_id", StringComparer.OrdinalIgnoreCase))
                return "owner_scope = 'character' AND owner_id = @cid";
            return null;
        }

        private static bool ShouldSkipCloneColumn(string tableName, string column, bool cloneAudit)
        {
            if ((tableName.Equals("character_items", StringComparison.OrdinalIgnoreCase)
                    || tableName.Equals("character_new_items", StringComparison.OrdinalIgnoreCase))
                && column.Equals("item_uid", StringComparison.OrdinalIgnoreCase))
                return true;
            if (tableName.Equals("item_audit_log", StringComparison.OrdinalIgnoreCase)
                && (column.Equals("audit_id", StringComparison.OrdinalIgnoreCase) || column.Equals("log_id", StringComparison.OrdinalIgnoreCase) || column.Equals("item_uid", StringComparison.OrdinalIgnoreCase)))
                return true;
            return false;
        }

        private static void InsertClonedRow(
            SqliteConnection conn,
            SqliteTransaction tx,
            string tableName,
            List<string> insertColumns,
            Dictionary<string, object> row,
            int sourceCharacterId,
            int newCharacterId,
            int targetAccountId,
            bool cloneAudit)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO " + QuoteAccountBackupIdentifier(tableName)
                    + " (" + string.Join(", ", insertColumns.Select(QuoteAccountBackupIdentifier)) + ") VALUES ("
                    + string.Join(", ", insertColumns.Select((_, i) => "@p" + i.ToString(CultureInfo.InvariantCulture))) + ");";

                for (var i = 0; i < insertColumns.Count; i++)
                {
                    var column = insertColumns[i];
                    object value = row[column];
                    if (column.Equals("character_id", StringComparison.OrdinalIgnoreCase))
                        value = newCharacterId;
                    else if (column.Equals("account_id", StringComparison.OrdinalIgnoreCase) && targetAccountId > 0)
                        value = targetAccountId;
                    else if (column.Equals("owner_id", StringComparison.OrdinalIgnoreCase)
                        && row.TryGetValue("owner_scope", out var scope)
                        && string.Equals(Convert.ToString(scope, CultureInfo.InvariantCulture), "character", StringComparison.OrdinalIgnoreCase))
                        value = newCharacterId;
                    else if (column.Equals("owner_character_id", StringComparison.OrdinalIgnoreCase))
                        value = newCharacterId;
                    else if (column.Equals("support_character_id", StringComparison.OrdinalIgnoreCase) && ToInt(row, column) == sourceCharacterId)
                        value = newCharacterId;
                    else if (cloneAudit && column.Equals("payload_json", StringComparison.OrdinalIgnoreCase))
                        value = "{}";
                    else if (column.Equals("created_at", StringComparison.OrdinalIgnoreCase) || column.Equals("updated_at", StringComparison.OrdinalIgnoreCase))
                        value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                    cmd.Parameters.AddWithValue("@p" + i.ToString(CultureInfo.InvariantCulture), value ?? DBNull.Value);
                }
                cmd.ExecuteNonQuery();
            }
        }

        private static Dictionary<string, object> LoadSingleRow(SqliteConnection conn, SqliteTransaction tx, string tableName, string whereSql, params (string Name, object Value)[] parameters)
        {
            return LoadRows(conn, tx, tableName, whereSql, parameters).FirstOrDefault();
        }

        private static List<Dictionary<string, object>> LoadRows(SqliteConnection conn, SqliteTransaction tx, string tableName, string whereSql, params (string Name, object Value)[] parameters)
        {
            var rows = new List<Dictionary<string, object>>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT * FROM " + QuoteAccountBackupIdentifier(tableName) + " WHERE " + whereSql + ";";
                foreach (var parameter in parameters)
                    cmd.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        for (var i = 0; i < reader.FieldCount; i++)
                            row[reader.GetName(i)] = reader.GetValue(i) == DBNull.Value ? null : reader.GetValue(i);
                        rows.Add(row);
                    }
                }
            }
            return rows;
        }

        private static bool CharacterExists(SqliteConnection conn, SqliteTransaction tx, int characterId, out int accountId)
        {
            accountId = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT account_id FROM characters WHERE character_id = @cid AND delete_flag = 0;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                var value = cmd.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                    return false;
                accountId = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
        }

        private static bool AccountExists(SqliteConnection conn, SqliteTransaction tx, int accountId)
        {
            return CountRows(conn, tx, "accounts", "account_id = @aid", ("@aid", accountId)) > 0;
        }

        private static bool AccountNameExists(SqliteConnection conn, SqliteTransaction tx, string accountName)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(1) FROM accounts WHERE m_id = @mid;";
                cmd.Parameters.AddWithValue("@mid", accountName);
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
        }

        private static bool CharacterNameExists(SqliteConnection conn, SqliteTransaction tx, string name)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT COUNT(1)
FROM characters
WHERE delete_flag = 0
  AND (name = @name OR name = @nameBytes OR name_bytes = @nameBytes);";
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@nameBytes", Encoding.UTF8.GetBytes(name));
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
        }

        private static int ResolveFreeCharacterSlotIndex(SqliteConnection conn, SqliteTransaction tx, int accountId, int slotLimit)
        {
            if (slotLimit <= 0)
                return -1;
            if (!ColumnExists(conn, tx, "characters", "slot_index"))
                return 0;

            var used = new HashSet<int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT slot_index
FROM characters
WHERE account_id = @aid
  AND delete_flag = 0;";
                cmd.Parameters.AddWithValue("@aid", accountId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                            used.Add(reader.GetInt32(0));
                    }
                }
            }

            for (var slot = 0; slot < slotLimit; slot++)
            {
                if (!used.Contains(slot))
                    return slot;
            }
            return -1;
        }

        private static int CountCharactersByAccount(SqliteConnection conn, SqliteTransaction tx, int accountId)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(1) FROM characters WHERE account_id = @aid AND delete_flag = 0;";
                cmd.Parameters.AddWithValue("@aid", accountId);
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private static int ResolveCharacterSlotLimit(SqliteConnection conn, SqliteTransaction tx)
        {
            if (!TableExists(conn, tx, "get_userinfo_template"))
                return DefaultCharacterSlotLimit;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT gate_or_count1, gate_or_count2 FROM get_userinfo_template WHERE id = 1 LIMIT 1;";
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                        return DefaultCharacterSlotLimit;
                    var first = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    var second = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    if (first > 0) return first;
                    if (second > 0) return second;
                    return DefaultCharacterSlotLimit;
                }
            }
        }

        private static string ValidateCharacterName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "角色名不能为空";
            var bytes = Encoding.UTF8.GetByteCount(name);
            if (bytes < 2 || bytes > 18)
                return "角色名长度需要为 2-18 字节";
            return null;
        }

        private static string ComputeMd5Hex(string text)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static int ToInt(Dictionary<string, object> row, string column)
        {
            return row.TryGetValue(column, out var value) && value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : 0;
        }

        private static long ToLong(object value)
        {
            return value == null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private readonly struct CharacterCloneOption
        {
            public CharacterCloneOption(string key, string label, bool defaultChecked)
            {
                Key = key;
                Label = label;
                DefaultChecked = defaultChecked;
            }

            public string Key { get; }
            public string Label { get; }
            public bool DefaultChecked { get; }
        }

        private readonly struct ItemCloneRange
        {
            public ItemCloneRange(int listType, int start, int end)
            {
                ListType = listType;
                Start = start;
                End = end;
            }

            public int ListType { get; }
            public int Start { get; }
            public int End { get; }

            public bool Contains(int listType, int slot)
            {
                return ListType == listType && slot >= Start && slot <= End;
            }
        }
    }

    public sealed class CharacterCloneRequest
    {
        public int TargetAccountId { get; set; }
        public string NewName { get; set; }
        public List<string> Options { get; set; }
    }
}
