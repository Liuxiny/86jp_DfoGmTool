using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Linq;

namespace DfoGmTool.SelfTests
{
    internal static class InventoryA21SelfTest
    {
        private const int AccountId = 821001;
        private const int CharacterId = 821011;

        internal static int Run()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "dfo-gm-inventory-a21-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var databasePath = Path.Combine(root, "inventory.db");
            var schemaPath = Path.Combine(
                AppContext.BaseDirectory,
                "ServerCore",
                "Sqlite",
                "item_schema.sql");
            var failures = 0;
            try
            {
                SqliteDatabaseBootstrap.CreateTestDatabase(databasePath, schemaPath);
                SeedOwnerRows(databasePath);
                var store = new NewInventoryStore(databasePath, schemaPath);

                Check(
                    "角色虚拟槽写入 A21 character_inventory_items",
                    store.TrySetVirtualCount(CharacterId, AccountId, 0, 42),
                    ref failures);
                Check(
                    "角色背包读回 99B",
                    store.TryLoadItem(CharacterId, AccountId, InventoryListType.Main, 0, out var wallet)
                    && wallet.Core.ToBytes().Length == ItemCore.Size
                    && wallet.Core.Count == 42,
                    ref failures);
                Check(
                    "金币 slot0 只写 A21 character_inventory_items",
                    Count(databasePath, "SELECT COUNT(*) FROM character_inventory_items WHERE character_id=821011 AND list_type=0 AND slot_index=0") == 1,
                    ref failures);

                var medal = ItemCore.Create(ItemCore.KindGuildMedal, 10001);
                var gem = ItemCore.Create(ItemCore.KindGuardianGem, 10002);
                gem.Count = 4;
                InsertCharacter(databasePath, CharacterId, InventoryListType.GuildMedal, 0, medal);
                InsertCharacter(databasePath, CharacterId, InventoryListType.GuildMedal, 49, gem);

                Check(
                    "A21 list_type=38 使用勋章 0-48 / 守护珠 49-97",
                    NewInventoryStore.TryGetRange(ItemCore.KindGuildMedal, out var medalList, out var medalStart, out var medalEnd)
                    && NewInventoryStore.TryGetRange(ItemCore.KindGuardianGem, out var gemList, out var gemStart, out var gemEnd)
                    && medalList == InventoryListType.GuildMedal
                    && medalStart == 0 && medalEnd == 48
                    && gemList == InventoryListType.GuildMedal
                    && gemStart == 49 && gemEnd == 97,
                    ref failures);
                Check(
                    "勋章/守护珠 99B 写读",
                    store.TryLoadItem(CharacterId, AccountId, InventoryListType.GuildMedal, 0, out var loadedMedal)
                    && store.TryLoadItem(CharacterId, AccountId, InventoryListType.GuildMedal, 49, out var loadedGem)
                    && loadedMedal.Core.ItemKind == ItemCore.KindGuildMedal
                    && loadedGem.Core.ItemKind == ItemCore.KindGuardianGem
                    && loadedGem.Core.Count == 4,
                    ref failures);
                Check(
                    "角色勋章槽删除",
                    store.TryDelete(CharacterId, AccountId, InventoryListType.GuildMedal, 0, 0, out _)
                    && !store.TryLoadItem(CharacterId, AccountId, InventoryListType.GuildMedal, 0, out _),
                    ref failures);

                var accountItem = ItemCore.Create(ItemCore.KindMaterial, 10003);
                accountItem.Count = 7;
                InsertAccount(databasePath, AccountId, 3, accountItem);
                Check(
                    "A21 account_inventory_items 写读",
                    store.LoadAccountCargo(AccountId).Any(item =>
                        item.ListType == InventoryListType.AccountCargo
                        && item.SlotIndex == 3
                        && item.Core.ItemId == 10003
                        && item.Core.Count == 7),
                    ref failures);

                using (var connection = Open(databasePath))
                using (var transaction = connection.BeginTransaction())
                {
                    CurrencyService.AddCubeFragment(connection, transaction, AccountId, 3033, 7);
                    CurrencyService.AddSoulWarehouse(connection, transaction, AccountId, 10100115, 11);
                    transaction.Commit();
                }
                using (var connection = Open(databasePath))
                {
                    Check(
                        "A21 cube 账号字段不回归",
                        CurrencyService.LoadCubeFragments(connection, null, AccountId).Any(item => item.ItemId == 3033 && item.Slot == 354 && item.Count == 7),
                        ref failures);
                    Check(
                        "A21 soul 账号字段 360",
                        CurrencyService.LoadSoulWarehouseCounts(connection, null, AccountId).Any(item => item.ItemId == 10100115 && item.Slot == 360 && item.Count == 11),
                        ref failures);
                }
                Check(
                    "账号仓库 A21 99B 更新",
                    store.UpdateItemCore(
                        0,
                        AccountId,
                        InventoryListType.AccountCargo,
                        3,
                        core => { core.Count = 8; return null; },
                        out _,
                        out _)
                    && store.LoadAccountCargo(AccountId).Any(item => item.SlotIndex == 3 && item.Core.Count == 8),
                    ref failures);
                Check(
                    "账号仓库删除",
                    store.DeleteAccountCargoAt(AccountId, 3) == 1
                    && Count(databasePath, "SELECT COUNT(*) FROM account_inventory_items WHERE account_id=821001 AND slot_index=3") == 0,
                    ref failures);

                var tail = Enumerable.Range(0, ItemCore.Size)
                    .Select(index => (byte)((index * 29 + 7) & 0xFF))
                    .ToArray();
                Check(
                    "A21 ItemCore 99B 全字节 round-trip",
                    ItemCore.FromBytes(tail).ToBytes().SequenceEqual(tail),
                    ref failures);
                Check(
                    "EpicPiece kind14 不进入普通 ItemCore 发放路由",
                    !NewInventoryStore.TryGetRange(ItemCore.KindEpicPiece, out _, out _, out _),
                    ref failures);
                var epicSeed = new byte[12];
                var epicAdded = EpicPieceService.ApplyBlobDelta(epicSeed, 1, 3, 17, out var epicBefore, out var epicAfter);
                var epicSubtracted = EpicPieceService.ApplyBlobDelta(epicAdded, 1, 3, -5, out var epicBefore2, out var epicAfter2);
                Check(
                    "A21 epic blob 小端顺序与加减",
                    epicBefore == 0 && epicAfter == 17
                    && epicBefore2 == 17 && epicAfter2 == 12
                    && BitConverter.ToInt32(epicSubtracted, 4) == 12
                    && BitConverter.ToInt32(epicSubtracted, 0) == 0
                    && BitConverter.ToInt32(epicSubtracted, 8) == 0,
                    ref failures);
                Check(
                    "库存操作写入 A21 inventory_audit_log",
                    Count(databasePath, "SELECT COUNT(*) FROM inventory_audit_log") >= 3,
                    ref failures);
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine("InventoryA21SelfTest EXCEPTION: " + ex);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }

            Console.WriteLine(
                failures == 0
                    ? "InventoryA21SelfTest OK"
                    : $"InventoryA21SelfTest FAIL: {failures}");
            return failures == 0 ? 0 : 1;
        }

        private static void SeedOwnerRows(string path)
        {
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO accounts(account_id,m_id) VALUES(821001,'inventory-a21');
INSERT INTO characters(character_id,account_id,name) VALUES(821011,821001,'inventory-a21-character');
INSERT INTO account_cargo_state(account_id,selection_key) VALUES(821001,64);
INSERT INTO character_container_state(character_id,list_type,list_param16) VALUES(821011,0,24);";
            command.ExecuteNonQuery();
        }

        private static void InsertCharacter(
            string path,
            int characterId,
            InventoryListType listType,
            int slot,
            ItemCore core)
        {
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO character_inventory_items(character_id,list_type,slot_index,item_core)
VALUES(@characterId,@listType,@slot,@core);";
            command.Parameters.AddWithValue("@characterId", characterId);
            command.Parameters.AddWithValue("@listType", (int)listType);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.Add("@core", SqliteType.Blob).Value = core.ToBytes();
            command.ExecuteNonQuery();
        }

        private static void InsertAccount(string path, int accountId, int slot, ItemCore core)
        {
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO account_inventory_items(account_id,slot_index,item_core)
VALUES(@accountId,@slot,@core);";
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.Add("@core", SqliteType.Blob).Value = core.ToBytes();
            command.ExecuteNonQuery();
        }

        private static int Count(string path, string sql)
        {
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                ForeignKeys = true,
                Pooling = false,
            }.ConnectionString);
            connection.Open();
            return connection;
        }

        private static void Check(string name, bool condition, ref int failures)
        {
            if (condition)
                Console.WriteLine("[PASS] " + name);
            else
            {
                failures++;
                Console.WriteLine("[FAIL] " + name);
            }
        }
    }
}
