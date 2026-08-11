using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DfoGmTool.ServerCore.Game.Inventory;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        private static int _inventoryAnomalyRunning;

        public object GetInventoryAnomalyStatus(PvfIndexService pvfIndex)
        {
            if (pvfIndex == null || !pvfIndex.IsReady)
                return InventoryAnomalyError("PVF 索引尚未就绪，无法判断合法物品 ID");

            if (Volatile.Read(ref _inventoryAnomalyRunning) != 0)
                return InventoryAnomalyResponse(
                    new InventoryAnomalySnapshot(),
                    running: true,
                    deletedCount: 0);

            try
            {
                var legalIds = pvfIndex.CopyValidItemIds();
                using var connection = new SqliteConnection(_config.ConnectionString);
                connection.Open();
                var snapshot = ScanInventoryAnomalies(connection, null, legalIds);
                return InventoryAnomalyResponse(snapshot, running: false, deletedCount: 0);
            }
            catch (Exception ex) when (ex is SqliteException || ex is InvalidOperationException)
            {
                return InventoryAnomalyError("扫描异常库存失败: " + ex.Message);
            }
        }

        public object CleanInventoryAnomalies(PvfIndexService pvfIndex)
        {
            if (pvfIndex == null || !pvfIndex.IsReady)
                return InventoryAnomalyError("PVF 索引尚未就绪，无法判断合法物品 ID");

            if (Interlocked.CompareExchange(ref _inventoryAnomalyRunning, 1, 0) != 0)
                return InventoryAnomalyError("异常库存清理正在运行，请稍后重试", running: true);

            var commitAttempted = false;
            var commitSucceeded = false;
            var deletedCount = 0;
            InventoryAnomalySnapshot remaining = null;
            try
            {
                var legalIds = pvfIndex.CopyValidItemIds();
                InventoryAnomalySnapshot before;
                using (var connection = new SqliteConnection(_config.ConnectionString))
                {
                    connection.Open();
                    using var transaction = connection.BeginTransaction(deferred: false);
                    before = ScanInventoryAnomalies(connection, transaction, legalIds);
                    foreach (var anomaly in before.Records)
                    {
                        if (anomaly.Source == InventoryAnomalySource.Character)
                        {
                            _inventory.DeleteCharacterAnomalyCore(
                                connection,
                                transaction,
                                anomaly.ItemUid,
                                anomaly.CharacterId,
                                anomaly.AccountId,
                                (InventoryListType)anomaly.ListType,
                                checked((short)anomaly.SlotIndex),
                                anomaly.ItemCoreBytes);
                        }
                        else
                        {
                            _inventory.DeleteAccountCargoAnomalyCore(
                                connection,
                                transaction,
                                anomaly.ItemUid,
                                anomaly.AccountId,
                                anomaly.CharacterId,
                                checked((short)anomaly.SlotIndex),
                                anomaly.ItemCoreBytes);
                        }
                    }

                    deletedCount = before.Records.Count;
                    // 在同一个 IMMEDIATE 事务的写视图中刷新剩余状态，再提交。
                    // 因而刷新失败仍属于提交前错误，事务会整体回滚；提交后不
                    // 再另开连接做可能误报“已回滚”的刷新。
                    remaining = ScanInventoryAnomalies(connection, transaction, legalIds);
                    commitAttempted = true;
                    transaction.Commit();
                    commitSucceeded = true;
                }
            }
            catch (Exception ex) when (ex is SqliteException
                                       || ex is InvalidOperationException
                                       || ex is OverflowException
                                       || ex is ArgumentException)
            {
                if (!commitAttempted)
                    return InventoryAnomalyError("清理异常库存失败，事务已回滚: " + ex.Message);
                if (!commitSucceeded)
                {
                    // Commit 抛错时 SQLite 的最终状态无法由异常本身证明，不能
                    // 把结果描述成“已回滚”。让调用方明确需要核查提交结果。
                    return InventoryAnomalyError("清理异常库存失败，事务提交结果不确定，请核查: " + ex.Message);
                }

                // 这里只可能是提交成功后的资源释放/响应刷新阶段异常。清理
                // 已提交，保留成功语义并把异常放到独立字段，禁止假报回滚。
                return InventoryAnomalyResponse(
                    remaining ?? new InventoryAnomalySnapshot(),
                    running: false,
                    deletedCount,
                    statusRefreshError: "清理已提交，但状态刷新阶段失败: " + ex.Message);
            }
            finally
            {
                Volatile.Write(ref _inventoryAnomalyRunning, 0);
            }

            return InventoryAnomalyResponse(
                remaining ?? new InventoryAnomalySnapshot(),
                running: false,
                deletedCount);
        }

        private static object InventoryAnomalyError(string error, bool running = false)
        {
            return new
            {
                success = false,
                running,
                hasAnomalies = false,
                totalCount = 0,
                characterCount = 0,
                accountCargoCount = 0,
                details = Array.Empty<object>(),
                deletedCount = 0,
                error,
            };
        }

        private static object InventoryAnomalyResponse(
            InventoryAnomalySnapshot snapshot,
            bool running,
            int deletedCount,
            string statusRefreshError = null)
        {
            snapshot ??= new InventoryAnomalySnapshot();
            var details = snapshot.Records
                .OrderBy(value => value.Source)
                .ThenBy(value => value.AccountId)
                .ThenBy(value => value.CharacterId)
                .ThenBy(value => value.ListType)
                .ThenBy(value => value.SlotIndex)
                .ThenBy(value => value.ItemUid)
                .Select(value => (object)new
                {
                    source = value.Source == InventoryAnomalySource.Character ? "character" : "accountCargo",
                    accountId = value.AccountId,
                    characterId = value.CharacterId,
                    characterName = value.CharacterName,
                    listType = value.ListType,
                    container = value.Container,
                    slot = value.SlotIndex,
                    itemId = value.ItemId,
                    itemUid = value.ItemUid,
                    reason = value.Reason,
                })
                .ToArray();
            return new
            {
                success = true,
                running,
                hasAnomalies = details.Length > 0,
                totalCount = details.Length,
                characterCount = snapshot.Records.Count(value => value.Source == InventoryAnomalySource.Character),
                accountCargoCount = snapshot.Records.Count(value => value.Source == InventoryAnomalySource.AccountCargo),
                details,
                deletedCount,
                statusRefreshError,
            };
        }

        private static InventoryAnomalySnapshot ScanInventoryAnomalies(
            SqliteConnection connection,
            SqliteTransaction transaction,
            ISet<int> legalIds)
        {
            var result = new InventoryAnomalySnapshot();
            ScanCharacterAnomalies(connection, transaction, legalIds, result.Records);
            ScanAccountCargoAnomalies(connection, transaction, legalIds, result.Records);
            return result;
        }

        private static void ScanCharacterAnomalies(
            SqliteConnection connection,
            SqliteTransaction transaction,
            ISet<int> legalIds,
            List<InventoryAnomalyRecord> records)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT n.item_uid, n.owner_id, n.character_id, n.list_type, n.slot_index,
       n.item_core, c.account_id, CAST(c.name AS BLOB)
FROM character_new_items n
LEFT JOIN characters c ON c.character_id=COALESCE(n.character_id, n.owner_id)
WHERE n.owner_scope='character'
ORDER BY n.owner_id, n.list_type, n.slot_index, n.item_uid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var characterId = reader.IsDBNull(2) ? reader.GetInt32(1) : reader.GetInt32(2);
                var listType = reader.GetInt32(3);
                var slotIndex = reader.GetInt32(4);
                // Main slots 0..2 are the virtual wallet. Even an empty/zero
                // ItemCore there is a valid representation and must be ignored.
                if (listType == (int)InventoryListType.Main && slotIndex >= 0 && slotIndex <= 2)
                    continue;

                var bytes = ReadBlob(reader, 5);
                var record = BuildAnomalyRecord(
                    InventoryAnomalySource.Character,
                    reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    characterId,
                    ReadCharacterName(reader, 7),
                    listType,
                    slotIndex,
                    reader.GetInt64(0),
                    bytes,
                    legalIds);
                if (record != null)
                    records.Add(record);
            }
        }

        private static void ScanAccountCargoAnomalies(
            SqliteConnection connection,
            SqliteTransaction transaction,
            ISet<int> legalIds,
            List<InventoryAnomalyRecord> records)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT n.item_uid, n.account_id, n.character_id, n.list_type, n.slot_index,
       n.item_core, CAST(c.name AS BLOB)
FROM account_cargo_new_items n
LEFT JOIN characters c ON c.character_id=n.character_id
ORDER BY n.account_id, n.list_type, n.slot_index, n.item_uid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var bytes = ReadBlob(reader, 5);
                var record = BuildAnomalyRecord(
                    InventoryAnomalySource.AccountCargo,
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    ReadCharacterName(reader, 6),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt64(0),
                    bytes,
                    legalIds);
                if (record != null)
                    records.Add(record);
            }
        }

        private static byte[] ReadBlob(SqliteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                return null;
            return reader.GetValue(ordinal) as byte[];
        }

        private static InventoryAnomalyRecord BuildAnomalyRecord(
            InventoryAnomalySource source,
            int accountId,
            int characterId,
            string characterName,
            int listType,
            int slotIndex,
            long itemUid,
            byte[] itemCoreBytes,
            ISet<int> legalIds)
        {
            var container = ResolveAnomalyContainer(source, listType);
            if (itemCoreBytes == null || itemCoreBytes.Length != ItemCore.Size)
            {
                return NewAnomaly(
                    source,
                    accountId,
                    characterId,
                    characterName,
                    listType,
                    container,
                    slotIndex,
                    itemUid,
                    0,
                    itemCoreBytes,
                    "item_core_null_or_invalid_length");
            }

            ItemCore core;
            try
            {
                core = ItemCore.FromBytes(itemCoreBytes);
            }
            catch
            {
                return NewAnomaly(
                    source,
                    accountId,
                    characterId,
                    characterName,
                    listType,
                    container,
                    slotIndex,
                    itemUid,
                    0,
                    itemCoreBytes,
                    "item_core_decode_failed");
            }

            if (core.ItemId <= 0)
            {
                return NewAnomaly(
                    source,
                    accountId,
                    characterId,
                    characterName,
                    listType,
                    container,
                    slotIndex,
                    itemUid,
                    core.ItemId,
                    itemCoreBytes,
                    "item_id_non_positive");
            }
            if (legalIds == null || !legalIds.Contains(core.ItemId))
            {
                return NewAnomaly(
                    source,
                    accountId,
                    characterId,
                    characterName,
                    listType,
                    container,
                    slotIndex,
                    itemUid,
                    core.ItemId,
                    itemCoreBytes,
                    "item_id_not_in_pvf");
            }
            return null;
        }

        private static InventoryAnomalyRecord NewAnomaly(
            InventoryAnomalySource source,
            int accountId,
            int characterId,
            string characterName,
            int listType,
            string container,
            int slotIndex,
            long itemUid,
            int itemId,
            byte[] itemCoreBytes,
            string reason)
        {
            return new InventoryAnomalyRecord
            {
                Source = source,
                AccountId = accountId,
                CharacterId = characterId,
                CharacterName = characterName,
                ListType = listType,
                Container = container,
                SlotIndex = slotIndex,
                ItemUid = itemUid,
                ItemId = itemId,
                ItemCoreBytes = itemCoreBytes,
                Reason = reason,
            };
        }

        private static string ResolveAnomalyContainer(InventoryAnomalySource source, int listType)
        {
            if (source == InventoryAnomalySource.AccountCargo)
                return "账号金库";
            return (InventoryListType)listType switch
            {
                InventoryListType.Main => "主背包",
                InventoryListType.Equipment => "穿戴装备",
                InventoryListType.Avatar => "时装",
                InventoryListType.PersonalCargo => "个人仓库",
                InventoryListType.Pet => "宠物",
                _ => "角色列表" + listType,
            };
        }
    }

    internal enum InventoryAnomalySource
    {
        Character,
        AccountCargo,
    }

    internal sealed class InventoryAnomalyRecord
    {
        internal InventoryAnomalySource Source { get; set; }
        internal int AccountId { get; set; }
        internal int CharacterId { get; set; }
        internal string CharacterName { get; set; }
        internal int ListType { get; set; }
        internal string Container { get; set; }
        internal int SlotIndex { get; set; }
        internal int ItemId { get; set; }
        internal long ItemUid { get; set; }
        internal byte[] ItemCoreBytes { get; set; }
        internal string Reason { get; set; }
    }

    internal sealed class InventoryAnomalySnapshot
    {
        internal List<InventoryAnomalyRecord> Records { get; } = new List<InventoryAnomalyRecord>();
    }
}
