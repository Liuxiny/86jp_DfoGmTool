// GM瘦身拷贝: 相对服务端原版删除了 CountItem, EnsureDatabase, EnsureContainerState, EnsureReviveCoinSlot,
// DeleteExpiredRentalEquipment, RebuildRentalInfoFromInventory, BuildRentalShopIndex,
// BuildRentalShopExpireIndex, TryResolveRentalShopId, HasSeedData, SeedInitialSnapshot,
// InsertSnapshotCommonItem, InsertSnapshotAvatarItem, InsertSnapshotPetItem, InsertSnapshotAccountCargoItem,
// SeedNewCharacterEquipment, 以及构造器中未拷贝子 store(_enchantStore/_itemUpgradeStore/_packageStore/
// _shopStore)的字段与初始化和 ExpertJob/ItemUpgrade 两个失效 using; 保留成员与原版逐字一致
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.SelectCharacter;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed partial class SqliteInventoryStore : IInventoryStore
    {
        internal const int DefaultAvatarUnknownFixed30 = 0x00001E00;
        internal const ushort DefaultAvatarUnknownFixed4 = 0x0400;
        internal const ushort DefaultPersonalCargoCapacity = 8;
        internal static readonly object StackableItemCacheLock = new object();
        internal static readonly Dictionary<int, GmPvfLib.StackableItemFile> StackableItemCache = new Dictionary<int, GmPvfLib.StackableItemFile>();

        internal static void ResetForPvfChange()
        {
            lock (StackableItemCacheLock)
                StackableItemCache.Clear();
        }

        private readonly string _connectionString;
        internal string ConnectionString => _connectionString;
        private readonly InventoryAuditLogger _auditLogger;
        internal readonly InventoryDbPrimitives _db;
        internal readonly InventoryEquipmentStore _equipStore;
        internal CharacterItemGrantService CharacterItemGrants { get; }
        private readonly IRentalTimeProvider _rentalTimeProvider;

        public SqliteInventoryStore(string databasePath, string schemaFilePath, IRentalTimeProvider rentalTimeProvider = null)
        {
            if (databasePath == null) throw new ArgumentNullException(nameof(databasePath));
            if (schemaFilePath == null) throw new ArgumentNullException(nameof(schemaFilePath));

            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            _connectionString = Infrastructure.SqliteDatabaseBootstrap.Initialize(databasePath, schemaFilePath);
            _rentalTimeProvider = rentalTimeProvider ?? SystemRentalTimeProvider.Instance;
            _auditLogger = new InventoryAuditLogger();
            _db = new InventoryDbPrimitives();
            _equipStore = new InventoryEquipmentStore(_db, _auditLogger);
            CharacterItemGrants = new CharacterItemGrantService(_db, _auditLogger);
        }

        public CharacterItemListSnapshot LoadCharacterItemListSnapshot(int characterId, int accountId)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                NormalizeRentalInventoryRows(connection, characterId, _rentalTimeProvider.UtcNowUnixSeconds());
                using (var repairTransaction = connection.BeginTransaction())
                {
                    RepairPetCreatureItemListSlotConflict(connection, repairTransaction, characterId);
                    repairTransaction.Commit();
                }

                var snapshot = new CharacterItemListSnapshot();
                var listParams = _equipStore.LoadContainerState(connection, null, characterId, accountId);
                snapshot.MainListParam16 = GetListParam(listParams, InventoryListType.Main);
                snapshot.AvatarListParam16 = GetListParam(listParams, InventoryListType.Avatar);
                snapshot.PersonalCargoListParam16 = NormalizePersonalCargoListParam(GetListParam(listParams, InventoryListType.PersonalCargo));
                snapshot.AccountCargoState = _equipStore.LoadAccountCargoState(connection, null, characterId, accountId);
                var petCreatureExtraBySerial = LoadPetCreatureExtraJsonMap(connection, null, characterId);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT list_type, slot_index, item_template_id, item_kind, stack_count, instance_value,
       durability, seal_flag, option_value, expire_time, marker_16, pet_serial_or_handle, equipment_lock_id, extra_json
FROM character_items
WHERE character_id = @characterId
ORDER BY list_type, slot_index;";
                    command.Parameters.AddWithValue("@characterId", characterId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var listType = (InventoryListType)reader.GetInt32(0);
                            var extraJson = reader.IsDBNull(13) ? "{}" : reader.GetString(13);

                            switch (listType)
                            {
                                case InventoryListType.Main:
                                    snapshot.MainItems.Add(InventoryItemCodec.ReadCommonItem(reader, extraJson));
                                    break;
                                case InventoryListType.Avatar:
                                    var avKind = reader.IsDBNull(3) ? "" : reader.GetString(3);
                                    snapshot.AvatarItems.Add(avKind == "avatar"
                                        ? InventoryItemCodec.ReadAvatarItem(reader, extraJson)
                                        : InventoryItemCodec.ReadEquipmentAsAvatarItem(reader, extraJson));
                                    break;
                                case InventoryListType.PersonalCargo:
                                    snapshot.PersonalCargoItems.Add(InventoryItemCodec.ReadCommonItem(reader, extraJson));
                                    break;
                                case InventoryListType.Pet:
                                    if (IsCreatureItem(reader.GetInt32(2)))
                                    {
                                        var petSerial = reader.GetInt32(11);
                                        petCreatureExtraBySerial.TryGetValue(petSerial, out var storedExtraJson);
                                        extraJson = MergePetCreatureInstanceExtraJsonForRead(storedExtraJson, extraJson);
                                    }
                                    snapshot.PetItems.Add(InventoryItemCodec.ReadPetItem(reader, extraJson));
                                    break;
                            }
                        }
                    }
                }

                using (var acCmd = connection.CreateCommand())
                {
                    acCmd.CommandText = @"
SELECT 12 AS list_type, slot_index, item_template_id, item_kind, stack_count, instance_value,
       durability, seal_flag, option_value, expire_time, marker_16, 0 AS pet_serial_or_handle, 0 AS equipment_lock_id, extra_json
FROM account_cargo_items
WHERE account_id = @accountId
ORDER BY slot_index;";
                    acCmd.Parameters.AddWithValue("@accountId", accountId);
                    using (var reader = acCmd.ExecuteReader())
                    {
                        while (reader.Read())
                            snapshot.AccountCargoItems.Add(InventoryItemCodec.ReadCommonItem(reader, reader.IsDBNull(13) ? "{}" : reader.GetString(13)));
                    }
                }

                // 读取账号级晶块, 合成虚拟 slot 条目添加到 MainItems
                var cubeFragments = CurrencyService.LoadCubeFragments(connection, null, accountId);
                foreach (var (itemId, slot, count) in cubeFragments)
                {
                    if (count > 0)
                    {
                        snapshot.MainItems.Add(new CommonInventoryItem
                        {
                            SlotIndex = (short)slot,
                            ItemTemplateId = itemId,
                            CountOrInstanceValue = count,
                        });
                    }
                }

                return snapshot;
            }
        }

        private static void NormalizeRentalInventoryRows(SqliteConnection connection, int characterId, uint now)
        {
            // 历史数据可能把租赁装备写成普通装备或 instance_value 非零；读取前统一成客户端可显示形态。
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
SELECT item_uid, item_template_id
FROM character_items
WHERE character_id = @characterId
  AND list_type = @listType
  AND expire_time > @now;";
                cmd.Parameters.AddWithValue("@characterId", characterId);
                cmd.Parameters.AddWithValue("@listType", (int)InventoryListType.Main);
                cmd.Parameters.AddWithValue("@now", now);
                var rows = new List<(long itemUid, int itemTemplateId)>();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var itemTemplateId = reader.GetInt32(1);
                        if (!RentalWeaponInventoryMapper.IsValidInventoryTemplate(itemTemplateId))
                            continue;

                        rows.Add((reader.GetInt64(0), itemTemplateId));
                    }
                }

                foreach (var row in rows)
                {
                    using (var update = connection.CreateCommand())
                    {
                        update.CommandText = @"
UPDATE character_items
SET item_kind = 'special',
    stack_count = @qualitySeed,
    instance_value = 0,
    durability = @durability,
    marker_16 = -1,
    extra_json = CASE WHEN extra_json IS NULL OR extra_json = '{}' THEN @extraJson ELSE extra_json END,
    updated_at = CURRENT_TIMESTAMP
WHERE item_uid = @itemUid;";
                        update.Parameters.AddWithValue("@qualitySeed", RentalWeaponRequestCodec.RentalWeaponQualitySeed);
                        update.Parameters.AddWithValue("@durability", RentalWeaponRequestCodec.RentalWeaponDurability);
                        update.Parameters.AddWithValue("@extraJson", "{\"extData0\":0,\"prefixData0E\":\"0000000000000000\",\"middleData1A\":\"0000000000000000000000000000000000\",\"tailData2F\":\"00000000000000000000000000000000000000000000000000000000000000000000000000\"}");
                        update.Parameters.AddWithValue("@itemUid", row.itemUid);
                        update.ExecuteNonQuery();
                    }
                }
            }
        }

        public bool TryDeleteItem(int characterId, int accountId, InventoryListType listType, short slotIndex, short deleteCount, out InventoryMutationResult result)
        {
            result = null;
            if (!IsSupportedDeleteOrSellListType(listType))
                return false;

            var dbListType = MapToDbListType(listType);

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    if (CurrencyService.IsCubeFragmentSlot(slotIndex))
                    {
                        var itemId = CurrencyService.GetCubeFragmentItemIdFromSlot(slotIndex);
                        if (itemId <= 0)
                            return false;

                        var cubes = CurrencyService.LoadCubeFragments(connection, transaction, accountId);
                    var currentCount = 0;
                    foreach (var (id, slot, count) in cubes)
                        if (id == itemId) { currentCount = count; break; }
                    if (currentCount < deleteCount)
                        return false;

                    CurrencyService.AddCubeFragment(connection, transaction, accountId, itemId, -deleteCount);
                    var remainingCount = currentCount - deleteCount;

                    var cubeWallet = _db.LoadWallet(connection, transaction, characterId);
                    transaction.Commit();

                    result = new InventoryMutationResult
                    {
                        ListType = listType,
                        SlotIndex = slotIndex,
                        ItemTemplateId = itemId,
                        RemainingStackCount = remainingCount,
                        InstanceValue = remainingCount,
                        Durability = 0,
                        UpdatedGold = cubeWallet.Gold,
                        UpdatedSp = cubeWallet.Sp,
                        UpdatedCoin = cubeWallet.Cera,
                        RequestedCount = deleteCount,
                        AppliedCount = deleteCount,
                    };
                    return true;
                }

                    // 金币槽(主背包 slot=0, item_template_id=0): 客户端用 DELETE_ITEM 同步"消耗金币"
                    // (例如消耗金币的技能), 走通用 TryDeleteItemCore 会把整行物理删除导致余额归零。
                    // 这里像 cube fragment 一样按数量增减, 而非删行。
                    // 只对主背包(Main)生效; avatar/equipment/pet 的 slot=0 不是金币, 不能误判。
                    if (slotIndex == 0 && listType == InventoryListType.Main)
                    {
                        if (deleteCount <= 0)
                            return false;

                        if (!CurrencyService.TrySpendGold(connection, transaction, characterId, deleteCount))
                            return false;

                        var goldWallet = _db.LoadWallet(connection, transaction, characterId);
                        transaction.Commit();

                        result = new InventoryMutationResult
                        {
                            ListType = listType,
                            SlotIndex = slotIndex,
                            ItemTemplateId = 0,
                            RemainingStackCount = goldWallet.Gold,
                            InstanceValue = goldWallet.Gold,
                            Durability = 0,
                            UpdatedGold = goldWallet.Gold,
                            UpdatedSp = goldWallet.Sp,
                            UpdatedCoin = goldWallet.Cera,
                            RequestedCount = deleteCount,
                            AppliedCount = deleteCount,
                        };
                        return true;
                    }


                var ok = TryDeleteItemCore(connection, transaction, characterId, listType, dbListType, slotIndex, deleteCount, out result);
                if (ok)
                    transaction.Commit();
                return ok;
                }
            }
        }

        // 非晶块删除内核: 调用方持有连接与事务并负责提交(失败不提交=回滚)。
        // 有期限的 PVF stackable 会以 special 形态持久化；已验证其 PVF 类型的调用方
        // 可显式保留逐颗堆叠语义，不改变其他 special 的通用删除行为。
        internal bool TryDeleteItemCore(
            SqliteConnection connection, SqliteTransaction transaction,
            int characterId, InventoryListType listType, InventoryListType dbListType,
            short slotIndex, short deleteCount, out InventoryMutationResult result,
            bool treatSourceAsStackable = false)
        {
            result = null;

            var item = _db.LoadItemRecord(connection, transaction, characterId, dbListType, slotIndex);
            if (item == null)
                return false;

            if (IsEquipmentItemLocked(connection, transaction, characterId, item))
            {
                FileLogger.Log($"  [DeleteItem] REJECT: locked item listType={dbListType} slot={slotIndex} lockId={item.EquipmentLockId}");
                return false;
            }

            var isStackCountedRecord = treatSourceAsStackable || IsStackCountedRecord(item);
            var stackedCount = treatSourceAsStackable
                ? Math.Max(0, item.StackCount)
                : GetStackedRecordCount(item);
            var appliedCount = isStackCountedRecord
                ? deleteCount <= 0 || deleteCount >= stackedCount
                    ? stackedCount
                    : deleteCount
                : 1;
            var itemRemainingCount = Math.Max(0, stackedCount - appliedCount);
            var satietyMutation = default(PetSatietyMutation);
            if (isStackCountedRecord && appliedCount < stackedCount)
            {
                if (IsPetConsumableRecord(item))
                    _db.UpdatePetStackCount(connection, transaction, item.ItemUid, itemRemainingCount);
                else
                    _db.UpdateStackCount(connection, transaction, item.ItemUid, itemRemainingCount);
            }
            else
            {
                _db.DeleteItem(connection, transaction, item.ItemUid);
            }

            if (IsPetConsumableRecord(item))
                satietyMutation = ApplyPetFoodSatiety(connection, transaction, characterId, item.ItemTemplateId);

            _auditLogger.WriteDeleteAuditLog(connection, transaction, characterId, item, appliedCount);
            var wallet = _db.LoadWallet(connection, transaction, characterId);

            result = new InventoryMutationResult
            {
                ListType = listType,
                SlotIndex = slotIndex,
                ItemTemplateId = item.ItemTemplateId,
                RemainingStackCount = itemRemainingCount,
                InstanceValue = isStackCountedRecord ? itemRemainingCount : item.InstanceValue,
                Durability = item.Durability,
                UpdatedGold = wallet.Gold,
                UpdatedSp = wallet.Sp,
                UpdatedCoin = wallet.Cera,
                RequestedCount = deleteCount,
                AppliedCount = (short)appliedCount,
                PetCreatureKey = satietyMutation.CreatureKey,
                PetSatietyBefore = satietyMutation.Before,
                PetSatietyAfter = satietyMutation.After,
                PetSatietyChanged = satietyMutation.Changed,
            };
            return true;
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static ushort GetListParam(Dictionary<InventoryListType, ushort> states, InventoryListType listType)
        {
            return states.TryGetValue(listType, out var value) ? value : (ushort)0;
        }

        internal static string CreateDefaultAvatarExtraJson()
        {
            var builder = ItemExtraViewBuilder.FromAvatarView(null);
            builder.Avatar.UnknownFixed4 = DefaultAvatarUnknownFixed4;
            return builder.Build().Serialize();
        }

        internal static string CreateDefaultPetExtraJson()
        {
            return "{\"tailData0A\":\"" + ItemExtraView.ToHex(new byte[74]) + "\"}";
        }

            internal static bool IsSupportedDeleteOrSellListType(InventoryListType listType)
            {
                return listType == InventoryListType.Main
                || listType == InventoryListType.PersonalCargo
                || listType == InventoryListType.Avatar
                || listType == InventoryListType.Equipment
                || listType == InventoryListType.Pet;
            }

        internal static int NormalizeRemovalCount(ItemRecord source, short requestedCount)
        {
            if (!IsStackCountedRecord(source))
                return 1;

            var currentCount = GetStackedRecordCount(source);
            if (requestedCount <= 0 || requestedCount >= currentCount)
                return currentCount;

            return requestedCount;
        }

        internal static bool IsStackCountedRecord(ItemRecord source)
        {
            if (source == null)
                return false;

            return source.ItemKind == "stackable" || IsPetConsumableRecord(source);
        }

        // 宠物判定: 物品在 equipment.lst 且 .equ 的 [equipment type] 为 [creature]。
        // CreatureExtraResolver 对不在 equipment.lst 的物品会抛异常, 这里吞掉返回 false。
        internal static bool IsCreatureItem(int itemTemplateId)
        {
            try
            {
                return CreatureExtraResolver.HasCreatureExtra(itemTemplateId);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"  [CeraShopBuy] IsCreatureItem(0x{itemTemplateId:X8}) 判定失败, 视为非宠物: {ex.Message}");
                return false;
            }
        }


        internal static ItemRecord ReadItemRecord(SqliteDataReader reader)
        {
            return new ItemRecord
            {
                ItemUid = reader.GetInt64(0),
                ListType = (InventoryListType)reader.GetInt32(1),
                SlotIndex = Convert.ToInt16(reader.GetInt32(2), CultureInfo.InvariantCulture),
                ItemTemplateId = reader.GetInt32(3),
                ItemKind = reader.GetString(4),
                StackCount = reader.GetInt32(5),
                InstanceValue = reader.GetInt32(6),
                Durability = Convert.ToUInt16(reader.GetInt32(7), CultureInfo.InvariantCulture),
                SealFlag = Convert.ToByte(reader.GetInt32(8), CultureInfo.InvariantCulture),
                OptionValue = Convert.ToByte(reader.GetInt32(9), CultureInfo.InvariantCulture),
                ExpireTime = reader.GetInt32(10),
                Marker16 = reader.GetInt32(11),
                PetSerialOrHandle = reader.GetInt32(12),
                EquipmentLockId = reader.FieldCount > 14
                    ? Convert.ToByte(reader.GetInt32(13), CultureInfo.InvariantCulture)
                    : (byte)0,
                ExtraJson = reader.IsDBNull(reader.FieldCount > 14 ? 14 : 13)
                    ? "{}"
                    : reader.GetString(reader.FieldCount > 14 ? 14 : 13),
            };
        }

        internal sealed class ItemRecord
        {
            public long ItemUid { get; set; }

            public InventoryListType ListType { get; set; }

            public short SlotIndex { get; set; }

            public int ItemTemplateId { get; set; }

            public string ItemKind { get; set; } = "unknown";

            public int StackCount { get; set; }

            public int InstanceValue { get; set; }

            public ushort Durability { get; set; }

            public byte SealFlag { get; set; }

            public byte OptionValue { get; set; }

            public int ExpireTime { get; set; }

            public int Marker16 { get; set; }

            public int PetSerialOrHandle { get; set; }

            public byte EquipmentLockId { get; set; }

            public string ExtraJson { get; set; } = "{}";
        }
    }
}
