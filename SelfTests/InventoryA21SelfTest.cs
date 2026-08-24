using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Infrastructure;
using DfoGmTool.ServerCore.GameWorld;
using DfoGmTool.Services;
using GmPvfLib;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Linq;
using System.Threading;

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
                CheckSlotPolicyMatrix(ref failures);
                CheckRealPvfGrantRoutes(databasePath, schemaPath, ref failures);
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

        private static void CheckSlotPolicyMatrix(ref int failures)
        {
            var expected = new[]
            {
                new { Kind = ItemCore.KindEquipment, List = InventoryListType.Main, Start = 9, End = 64 },
                new { Kind = ItemCore.KindConsumable, List = InventoryListType.Main, Start = 65, End = 120 },
                new { Kind = ItemCore.KindMaterial, List = InventoryListType.Main, Start = 121, End = 176 },
                new { Kind = ItemCore.KindQuest, List = InventoryListType.Main, Start = 177, End = 232 },
                new { Kind = ItemCore.KindExpertJobMaterial, List = InventoryListType.Main, Start = 233, End = 288 },
                new { Kind = ItemCore.KindAvatarEmblem, List = InventoryListType.Main, Start = 289, End = 351 },
                new { Kind = ItemCore.KindAvatar, List = InventoryListType.Avatar, Start = 0, End = 209 },
                new { Kind = ItemCore.KindCreature, List = InventoryListType.Pet, Start = 0, End = 139 },
                new { Kind = ItemCore.KindCreatureEquipment, List = InventoryListType.Pet, Start = 140, End = 188 },
                new { Kind = ItemCore.KindCreatureConsumable, List = InventoryListType.Pet, Start = 189, End = 239 },
                new { Kind = ItemCore.KindGuildMedal, List = InventoryListType.GuildMedal, Start = 0, End = 48 },
                new { Kind = ItemCore.KindGuardianGem, List = InventoryListType.GuildMedal, Start = 49, End = 97 },
            };
            Check(
                "A21 policy kind/list/range matrix",
                expected.All(value => A21InventorySlotPolicy.TryGetRange(
                    value.Kind,
                    out var list,
                    out var start,
                    out var end)
                    && list == value.List
                    && start == value.Start
                    && end == value.End),
                ref failures);
            Check(
                "主背包扩展仍保留 0/8/16/24 语义",
                A21InventorySlotPolicy.TryGetMainRange(ItemCore.KindEquipment, 0, out _, out var noneEnd)
                && noneEnd == 40
                && A21InventorySlotPolicy.TryGetMainRange(ItemCore.KindEquipment, 8, out _, out var stage1End)
                && stage1End == 48
                && A21InventorySlotPolicy.TryGetMainRange(ItemCore.KindEquipment, 16, out _, out var stage2End)
                && stage2End == 56
                && A21InventorySlotPolicy.TryGetMainRange(ItemCore.KindEquipment, 24, out _, out var fullEnd)
                && fullEnd == 64,
                ref failures);
            Check(
                "徽章不随主背包扩展收缩",
                A21InventorySlotPolicy.TryGetMainRange(ItemCore.KindAvatarEmblem, 0, out _, out var emblemEnd)
                && emblemEnd == 351,
                ref failures);
            Check(
                "Avatar/list1 末格 209 有效且跨界无效",
                A21InventorySlotPolicy.IsValidSlotForKind(ItemCore.KindAvatar, InventoryListType.Avatar, 209, 24)
                && !A21InventorySlotPolicy.IsValidSlotForKind(ItemCore.KindAvatar, InventoryListType.Avatar, 210, 24),
                ref failures);
            Check(
                "宠物消耗品末格 239 有效且 240 越界",
                A21InventorySlotPolicy.IsValidSlotForKind(ItemCore.KindCreatureConsumable, InventoryListType.Pet, 239, 24)
                && !A21InventorySlotPolicy.IsValidSlotForKind(ItemCore.KindCreatureConsumable, InventoryListType.Pet, 240, 24),
                ref failures);
            Check(
                "徽章末格 351 有效且主背包跨段无效",
                A21InventorySlotPolicy.IsValidSlotForKind(ItemCore.KindAvatarEmblem, InventoryListType.Main, 351, 0)
                && !A21InventorySlotPolicy.IsValidSlotForKind(ItemCore.KindEquipment, InventoryListType.Pet, 239, 24),
                ref failures);
            Check(
                "穿戴 29 是名称装饰状态而非 ItemCore",
                !A21InventorySlotPolicy.TryGetEquipmentBodyKind(29, out _)
                && A21InventorySlotPolicy.GetEquipmentCategory(29) == "名称装饰状态",
                ref failures);
        }

        private static void CheckRealPvfGrantRoutes(string databasePath, string schemaPath, ref int failures)
        {
            var pvf = ResolveLatestServerPvf();
            Check("当前 A21 PVF 可用于槽位路由回归", pvf != null, ref failures);
            if (pvf == null)
                return;

            PvfArchiveAccessor.Configure(pvf);
            ItemMetadataResolver.ResetForPvfChange();
            var index = new PvfIndexService(pvf);
            index.WarmInBackground();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!index.IsReady && string.IsNullOrWhiteSpace(index.BuildError) && DateTime.UtcNow < deadline)
                Thread.Sleep(50);

            var petEntry = index.AllItems.FirstOrDefault(item => item.Segment == "宠物消耗品");
            var normalEntry = index.AllItems.FirstOrDefault(item => item.Segment == "消耗品");
            Check("当前 PVF 搜索段识别宠物消耗品", petEntry != null, ref failures);
            Check("当前 PVF 仍有普通消耗品段", normalEntry != null, ref failures);
            if (petEntry == null || normalEntry == null)
                return;

            var petMetadata = ItemMetadataResolver.Resolve(petEntry.Id);
            Check(
                "真实 PVF [creature]/[feed] metadata 分类为宠物消耗品",
                ItemMetadataResolver.IsPetConsumableItem(petMetadata)
                && NewInventoryStore.TryResolveKindAndRange(
                    petMetadata,
                    null,
                    out var petKind,
                    out var petList,
                    out var petStart,
                    out var petEnd,
                    out _)
                && petKind == ItemCore.KindCreatureConsumable
                && petList == InventoryListType.Pet
                && petStart == 189
                && petEnd == 239,
                ref failures);

            var petGrant = new NewInventoryStore(databasePath, schemaPath)
                .TryGrant(CharacterId, AccountId, 0, petEntry.Id, 1, null);
            Check(
                "真实 PVF 宠物消耗品直发落 list7/189-239",
                petGrant.Success
                && petGrant.ListType == InventoryListType.Pet
                && petGrant.AssignedSlot >= 189
                && petGrant.AssignedSlot <= 239,
                ref failures);
            if (petGrant.Success)
            {
                var petStore = new NewInventoryStore(databasePath, schemaPath);
                Check(
                    "宠物消耗品 ItemCore kind=7",
                    petStore.TryLoadItem(CharacterId, AccountId, InventoryListType.Pet, petGrant.AssignedSlot, out var petRecord)
                    && petRecord.Core.ItemKind == ItemCore.KindCreatureConsumable
                    && petRecord.Core.Count > 0,
                    ref failures);
            }

            var normalMetadata = ItemMetadataResolver.Resolve(normalEntry.Id);
            var normalGrant = new NewInventoryStore(databasePath, schemaPath)
                .TryGrant(CharacterId, AccountId, 0, normalEntry.Id, 1, null);
            Check(
                "普通消耗品仍直发主背包消耗品段",
                !ItemMetadataResolver.IsPetConsumableItem(normalMetadata)
                && normalGrant.Success
                && normalGrant.ListType == InventoryListType.Main
                && normalGrant.AssignedSlot >= 65
                && normalGrant.AssignedSlot <= 120,
                ref failures);

            EnsureLegacyPickupTable(databasePath);
            var assets = new SqliteAssetService(databasePath, schemaPath);
            using (var scope = assets.OpenScope(CharacterId, AccountId))
            {
                var pickupOk = assets.TryAddItem(scope, petEntry.Id, 1, out var pickupSlot);
                scope.Commit();
                var pickupRoute = LoadLegacyPickupRoute(databasePath, petEntry.Id);
                Check(
                    "真实 PVF 拾取宠物消耗品写入 list7/189-239 且 item_kind=pet",
                    pickupOk
                    && pickupSlot >= 189
                    && pickupSlot <= 239
                    && pickupRoute.ListType == InventoryListType.Pet
                    && pickupRoute.Slot >= 189
                    && pickupRoute.Slot <= 239
                    && pickupRoute.ItemKind == "pet",
                    ref failures);
            }
            using (var scope = assets.OpenScope(CharacterId, AccountId))
            {
                var grant = assets.TryGrantCharacterItem(scope, petEntry.Id, 1);
                scope.Commit();
                var grantRoute = LoadLegacyPickupRoute(databasePath, petEntry.Id);
                Check(
                    "旧角色发放入口同样写入 list7/189-239 且 item_kind=pet",
                    grant.Success
                    && grant.ListType == InventoryListType.Pet
                    && grant.AssignedSlot >= 189
                    && grant.AssignedSlot <= 239
                    && grantRoute.ListType == InventoryListType.Pet
                    && grantRoute.Slot >= 189
                    && grantRoute.Slot <= 239
                    && grantRoute.ItemKind == "pet",
                    ref failures);
            }
        }

        private static (InventoryListType ListType, int Slot, string ItemKind) LoadLegacyPickupRoute(
            string path,
            int itemTemplateId)
        {
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT list_type, slot_index, item_kind
FROM character_items
WHERE character_id=@cid AND item_template_id=@item
ORDER BY item_uid DESC LIMIT 1;";
            command.Parameters.AddWithValue("@cid", CharacterId);
            command.Parameters.AddWithValue("@item", itemTemplateId);
            using var reader = command.ExecuteReader();
            return reader.Read()
                ? ((InventoryListType)reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2))
                : ((InventoryListType)255, -1, null);
        }

        private static void EnsureLegacyPickupTable(string path)
        {
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS character_items (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    owner_scope TEXT NOT NULL DEFAULT 'character',
    owner_id INTEGER NOT NULL DEFAULT 0,
    character_id INTEGER NOT NULL,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_template_id INTEGER NOT NULL,
    item_kind TEXT NOT NULL,
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
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS item_audit_log (
    audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
    owner_scope TEXT NOT NULL DEFAULT 'character',
    owner_id INTEGER NOT NULL DEFAULT 0,
    character_id INTEGER NOT NULL DEFAULT 0,
    action_name TEXT NOT NULL,
    list_type INTEGER,
    slot_index INTEGER,
    item_uid INTEGER,
    item_template_id INTEGER NOT NULL DEFAULT 0,
    delta_stack_count INTEGER NOT NULL DEFAULT 0,
    payload_json TEXT NOT NULL DEFAULT '{}'
);";
            command.ExecuteNonQuery();
        }

        private static string ResolveLatestServerPvf()
        {
            var roots = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
            foreach (var root in roots)
            {
                var current = new DirectoryInfo(root);
                for (var depth = 0; current != null && depth < 8; depth++, current = current.Parent)
                {
                    var codes = Path.Combine(current.FullName, "Codes");
                    var exact = Path.Combine(codes, "ServerS4A21_git", "Server", "DfoServer", "Data", "Pvf", "Script.pvf");
                    if (File.Exists(exact))
                        return exact;
                }
            }
            return null;
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
