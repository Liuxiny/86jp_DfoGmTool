using System;
using System.IO;
using System.Text;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.SelfTests
{
    internal static class InventoryMigrationSelfTest
    {
        private static int _failures;

        internal static int Run()
        {
            Console.WriteLine("=== INVENTORY_MIGRATION selftest ===");
            var root = Path.Combine(Path.GetTempPath(), "dfo-gm-migration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                TestLegacyToNewAndConflict(root);
                TestLegacyEquippedUsesFirstFreeBagSlots(root);
                TestNewToLegacy(root);
                TestFullBagResidual(root);
                TestTitleBookBothDirections(root);
                TestLegacyConflictCleanup(root);
                TestNewConflictCleanup(root);
                TestLegacyMirrorCleanup(root);
                TestNewMirrorCleanup(root);
                TestStackableMergeBothDirections(root);
                TestStackableLimitAndResidual(root);
                TestAccountCargoStackableBothDirections(root);
                TestTransactionRollback(root);
            }
            catch (Exception ex)
            {
                _failures++;
                Console.Error.WriteLine("UNHANDLED: " + ex);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }

            Console.WriteLine(_failures == 0
                ? "InventoryMigrationSelfTest OK"
                : $"InventoryMigrationSelfTest FAIL: {_failures}");
            return _failures == 0 ? 0 : 1;
        }

        private static void TestLegacyToNewAndConflict(string root)
        {
            var db = CreateDatabase(root, "legacy-new.db");
            using (var connection = Open(db))
            {
                SeedIdentity(connection);
                InsertNew(connection, 9, 900001);
                InsertLegacy(connection, 9, 900002);
            }
            var report = Coordinator(db).MigrateLegacyToNew();
            Check("legacy->new succeeds", report.Success);
            Check("conflict shifts forward", Scalar(db, "SELECT COUNT(*) FROM character_new_items WHERE character_id=1 AND list_type=0 AND slot_index=10;") == 1);
            Check("legacy source cleared", Scalar(db, "SELECT COUNT(*) FROM character_items;") == 0);
            Check("status disables empty legacy source", !report.Status.CanUpgrade && report.Status.CanDowngrade);
        }

        private static void TestLegacyEquippedUsesFirstFreeBagSlots(string root)
        {
            var db = CreateDatabase(root, "legacy-equipped-first-free.db");
            using (var connection = Open(db))
            {
                SeedIdentity(connection);
                InsertLegacyEquipped(connection, 0, 905000);
                InsertLegacyEquipped(connection, 11, 905011);
                InsertLegacyEquipped(connection, 12, 905012);
                InsertLegacyEquipped(connection, 24, 905024);
                InsertLegacyEquipped(connection, 25, 905025);
            }

            var report = Coordinator(db).MigrateLegacyToNew();
            Check("legacy equipped item kinds fill their bags from the first free slots", report.Success
                && Scalar(db, "SELECT COUNT(*) FROM character_new_items WHERE character_id=1 AND list_type=1 AND slot_index=0;") == 1
                && Scalar(db, "SELECT COUNT(*) FROM character_new_items WHERE character_id=1 AND list_type=0 AND slot_index IN(9,10);") == 2
                && Scalar(db, "SELECT COUNT(*) FROM character_new_items WHERE character_id=1 AND list_type=0 AND slot_index IN(11,12);") == 0
                && Scalar(db, "SELECT COUNT(*) FROM character_new_items WHERE character_id=1 AND list_type=7 AND slot_index=0;") == 1
                && Scalar(db, "SELECT COUNT(*) FROM character_new_items WHERE character_id=1 AND list_type=7 AND slot_index=140;") == 1);
            Check("legacy equipped sources are cleared after first-free migration",
                Scalar(db, "SELECT COUNT(*) FROM character_equipped_entries WHERE character_id=1;") == 0);
        }

        private static void TestNewToLegacy(string root)
        {
            var db = CreateDatabase(root, "new-legacy.db");
            using (var connection = Open(db))
            {
                SeedIdentity(connection);
                InsertNew(connection, 9, 910001);
            }
            var report = Coordinator(db).MigrateNewToLegacy();
            Check("new->legacy succeeds", report.Success);
            Check("new source cleared", Scalar(db, "SELECT COUNT(*) FROM character_new_items;") == 0);
            Check("legacy target populated", Scalar(db, "SELECT COUNT(*) FROM character_items WHERE character_id=1 AND list_type=0 AND slot_index=9;") == 1);
            Check("status disables empty new source", !report.Status.CanDowngrade && report.Status.CanUpgrade);
        }

        private static void TestFullBagResidual(string root)
        {
            var db = CreateDatabase(root, "residual.db");
            using (var connection = Open(db))
            {
                SeedIdentity(connection);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE characters SET name=@name WHERE character_id=1;";
                    command.Parameters.AddWithValue("@name", Encoding.UTF8.GetBytes("迁移角色"));
                    command.ExecuteNonQuery();
                }
                for (short slot = 9; slot <= 64; slot++)
                    InsertNew(connection, slot, 920000 + slot);
                InsertLegacy(connection, 9, 929999);
            }
            var report = Coordinator(db).MigrateLegacyToNew();
            Check("full bag run commits non-fatal report", report.Success);
            Check("full bag keeps source residual", Scalar(db, "SELECT COUNT(*) FROM character_items;") == 1);
            Check("full bag reports exact bag and count", report.Residuals.Count == 1
                && report.Residuals[0].BagType == "装备背包"
                && report.Residuals[0].ItemCount == 1
                && report.Residuals[0].RequiredFreeSlots == 1
                && report.Residuals[0].CharacterName == "迁移角色");
        }

        private static void TestTransactionRollback(string root)
        {
            var db = CreateDatabase(root, "rollback.db");
            using (var connection = Open(db))
            {
                SeedIdentity(connection);
                InsertLegacy(connection, 9, 930001);
                InsertLegacy(connection, 10, 930002);
                Exec(connection, @"CREATE TRIGGER force_migration_failure
BEFORE INSERT ON character_new_items WHEN NEW.slot_index=10
BEGIN SELECT RAISE(ABORT,'forced migration failure'); END;");
            }
            var threw = false;
            try { Coordinator(db).MigrateLegacyToNew(); }
            catch (SqliteException) { threw = true; }
            Check("migration error is surfaced", threw);
            Check("error rolls back all target writes", Scalar(db, "SELECT COUNT(*) FROM character_new_items;") == 0);
            Check("error rolls back all source deletes", Scalar(db, "SELECT COUNT(*) FROM character_items;") == 2);
        }

        private static void TestTitleBookBothDirections(string root)
        {
            var db = CreateDatabase(root, "titlebook.db");
            using (var connection = Open(db))
            {
                SeedIdentity(connection);
                var oldCore = ItemCore.Create(ItemCore.KindEquipment, 940001);
                var blob = new byte[80 * LegacyTitleBookCoreCodec.RecordSize];
                var record = LegacyTitleBookCoreCodec.EncodeRecord(0, oldCore);
                Buffer.BlockCopy(record, 0, blob, 0, record.Length);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "INSERT INTO character_titlebook(character_id,general) VALUES(1,@blob);";
                    command.Parameters.AddWithValue("@blob", blob);
                    command.ExecuteNonQuery();
                }
                var existing = ItemCore.Create(ItemCore.KindEquipment, 940002);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "INSERT INTO character_new_titlebook(character_id,category,slot_index,item_core) VALUES(1,0,0,@core);";
                    command.Parameters.AddWithValue("@core", existing.ToBytes());
                    command.ExecuteNonQuery();
                }
            }
            var upgrade = Coordinator(db).MigrateLegacyToNew();
            Check("titlebook conflict shifts forward", upgrade.Success
                && Scalar(db, "SELECT COUNT(*) FROM character_new_titlebook WHERE character_id=1 AND category=0 AND slot_index=1;") == 1);
            Check("legacy titlebook source rows cleared", Scalar(db, "SELECT COUNT(*) FROM character_titlebook;") == 0
                && Scalar(db, "SELECT COUNT(*) FROM character_achievement_chunks;") == 0);
            var downgrade = Coordinator(db).MigrateNewToLegacy();
            Check("titlebook reverse succeeds and clears new", downgrade.Success
                && Scalar(db, "SELECT COUNT(*) FROM character_new_titlebook;") == 0
                && Scalar(db, "SELECT COUNT(*) FROM character_titlebook;") == 1);
        }

        private static void TestLegacyConflictCleanup(string root)
        {
            var db = CreateDatabase(root, "legacy-conflict-cleanup.db");
            using (var connection = Open(db))
            {
                SeedIdentity(connection);
                // 名称装饰卡迁移只读取独立列，不解析 raw_entry；使用最小占位数据，
                // 避免专项测试依赖外部 PVF。
                var raw = new byte[] { 28, 0, 0, 0, 0 };
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"INSERT INTO character_equipped_entries
(character_id,slot,item_id,expire_time,equipment_lock_id,raw_entry)
VALUES(1,28,950001,0,0,@raw);";
                    command.Parameters.AddWithValue("@raw", raw);
                    command.ExecuteNonQuery();
                }
                Exec(connection, "INSERT INTO character_name_tag_state(character_id,item_id,expire_time) VALUES(1,950002,0);");

                for (short slot = 0; slot < 80; slot++)
                {
                    var core = ItemCore.Create(ItemCore.KindEquipment, 951000 + slot);
                    using var command = connection.CreateCommand();
                    command.CommandText = @"INSERT INTO character_new_titlebook
(character_id,category,slot_index,item_core) VALUES(1,0,@slot,@core);";
                    command.Parameters.AddWithValue("@slot", slot);
                    command.Parameters.AddWithValue("@core", core.ToBytes());
                    command.ExecuteNonQuery();
                }

                var legacyCore = ItemCore.Create(ItemCore.KindEquipment, 952001);
                var blob = new byte[80 * LegacyTitleBookCoreCodec.RecordSize];
                var record = LegacyTitleBookCoreCodec.EncodeRecord(0, legacyCore);
                Buffer.BlockCopy(record, 0, blob, 0, record.Length);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "INSERT INTO character_titlebook(character_id,general) VALUES(1,@blob);";
                    command.Parameters.AddWithValue("@blob", blob);
                    command.ExecuteNonQuery();
                }
            }

            var report = Coordinator(db).MigrateLegacyToNew();
            Check("existing new name tag wins and legacy slot is cleared", report.Success
                && Scalar(db, "SELECT item_id FROM character_name_tag_state WHERE character_id=1;") == 950002
                && Scalar(db, "SELECT COUNT(*) FROM character_equipped_entries WHERE character_id=1 AND slot=28;") == 0);
            Check("full new titlebook wins and legacy rows are cleared", report.Residuals.Count == 0
                && Scalar(db, "SELECT COUNT(*) FROM character_new_titlebook WHERE character_id=1 AND category=0;") == 80
                && Scalar(db, "SELECT COUNT(*) FROM character_titlebook;") == 0
                && Scalar(db, "SELECT COUNT(*) FROM character_achievement_chunks;") == 0);
            Check("legacy shortcut status is disabled after conflict cleanup", !report.Status.CanUpgrade);
        }

        private static void TestNewConflictCleanup(string root)
        {
            var db = CreateDatabase(root, "new-conflict-cleanup.db");
            using (var connection = Open(db))
            {
                SeedIdentity(connection);

                var raw = new byte[] { 28, 0, 0, 0, 0 };
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"INSERT INTO character_equipped_entries
(character_id,slot,item_id,expire_time,equipment_lock_id,raw_entry)
VALUES(1,28,953001,0,0,@raw);";
                    command.Parameters.AddWithValue("@raw", raw);
                    command.ExecuteNonQuery();
                }
                Exec(connection, "INSERT INTO character_name_tag_state(character_id,item_id,expire_time) VALUES(1,953002,0);");

                var blob = new byte[80 * LegacyTitleBookCoreCodec.RecordSize];
                for (short slot = 0; slot < 80; slot++)
                {
                    var core = ItemCore.Create(ItemCore.KindEquipment, 954000 + slot);
                    var record = LegacyTitleBookCoreCodec.EncodeRecord(slot, core);
                    Buffer.BlockCopy(record, 0, blob, slot * LegacyTitleBookCoreCodec.RecordSize, record.Length);
                }
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "INSERT INTO character_titlebook(character_id,general) VALUES(1,@blob);";
                    command.Parameters.AddWithValue("@blob", blob);
                    command.ExecuteNonQuery();
                }

                var newCore = ItemCore.Create(ItemCore.KindEquipment, 955001);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"INSERT INTO character_new_titlebook
(character_id,category,slot_index,item_core) VALUES(1,0,0,@core);";
                    command.Parameters.AddWithValue("@core", newCore.ToBytes());
                    command.ExecuteNonQuery();
                }
            }

            var report = Coordinator(db).MigrateNewToLegacy();
            Check("existing legacy name tag wins and new state is cleared", report.Success
                && Scalar(db, "SELECT item_id FROM character_equipped_entries WHERE character_id=1 AND slot=28;") == 953001
                && Scalar(db, "SELECT COUNT(*) FROM character_name_tag_state WHERE character_id=1;") == 0);
            Check("full legacy titlebook wins and new rows are cleared", report.Residuals.Count == 0
                && Scalar(db, "SELECT COUNT(*) FROM character_titlebook WHERE character_id=1;") == 1
                && Scalar(db, "SELECT COUNT(*) FROM character_new_titlebook;") == 0);
            Check("new shortcut status is disabled after conflict cleanup", !report.Status.CanDowngrade);
        }

        private static void TestLegacyMirrorCleanup(string root)
        {
            var db = CreateDatabase(root, "legacy-mirror-cleanup.db");
            using (var connection = Open(db))
            {
                SeedIdentity(connection);

                var gold = new ItemCore
                {
                    ItemKind = ItemCore.KindSpecialMaterial,
                    ItemId = 0,
                    Count = 100,
                };
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"INSERT INTO character_new_items
(owner_scope,owner_id,character_id,list_type,slot_index,item_core)
VALUES('character',1,1,0,0,@core);";
                    command.Parameters.AddWithValue("@core", gold.ToBytes());
                    command.ExecuteNonQuery();
                }
                Exec(connection, @"INSERT INTO character_items
(owner_scope,owner_id,character_id,list_type,slot_index,item_template_id,item_kind,stack_count,instance_value,extra_json)
VALUES('character',1,1,0,0,0,'special',100,100,'{}');");

                var cargo = ItemCore.Create(ItemCore.KindEquipment, 960001);
                cargo.InstanceValue = 10000;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"INSERT INTO account_cargo_new_items
(account_id,character_id,list_type,slot_index,item_core) VALUES(1,1,12,0,@core);";
                    command.Parameters.AddWithValue("@core", cargo.ToBytes());
                    command.ExecuteNonQuery();
                }
                Exec(connection, @"INSERT INTO account_cargo_items
(account_id,slot_index,item_template_id,item_kind,stack_count,instance_value,marker_16,extra_json)
VALUES(1,0,960001,'equipment',1,10000,-1,'{}');");

                Exec(connection, "UPDATE accounts SET cube_black=100 WHERE account_id=1;");
                Exec(connection, @"INSERT INTO character_items
(owner_scope,owner_id,character_id,list_type,slot_index,item_template_id,item_kind,stack_count,instance_value,extra_json)
VALUES('character',1,1,0,354,3033,'stackable',100,100,'{}');");
            }

            var report = Coordinator(db).MigrateLegacyToNew();
            Check("mirrored legacy gold does not add to new gold", report.Success
                && ReadNewCount(db, "character_new_items", "owner_id=1 AND list_type=0 AND slot_index=0") == 100);
            Check("mirrored legacy cargo does not duplicate new cargo", Scalar(db,
                "SELECT COUNT(*) FROM account_cargo_new_items WHERE account_id=1;") == 1);
            Check("mirrored legacy cube does not add to account cube", Scalar(db,
                "SELECT cube_black FROM accounts WHERE account_id=1;") == 100);
            Check("all consumed legacy mirror rows are cleared", Scalar(db, "SELECT COUNT(*) FROM character_items;") == 0
                && Scalar(db, "SELECT COUNT(*) FROM account_cargo_items;") == 0
                && !report.Status.CanUpgrade);
        }

        private static void TestNewMirrorCleanup(string root)
        {
            var db = CreateDatabase(root, "new-mirror-cleanup.db");
            using (var connection = Open(db))
            {
                SeedIdentity(connection);
                Exec(connection, @"INSERT INTO character_items
(owner_scope,owner_id,character_id,list_type,slot_index,item_template_id,item_kind,stack_count,instance_value,extra_json)
VALUES('character',1,1,0,0,0,'special',100,100,'{}');");
                var gold = new ItemCore { ItemKind = ItemCore.KindSpecialMaterial, ItemId = 0, Count = 100 };
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"INSERT INTO character_new_items
(owner_scope,owner_id,character_id,list_type,slot_index,item_core)
VALUES('character',1,1,0,0,@core);";
                    command.Parameters.AddWithValue("@core", gold.ToBytes());
                    command.ExecuteNonQuery();
                }

                Exec(connection, @"INSERT INTO account_cargo_items
(account_id,slot_index,item_template_id,item_kind,stack_count,instance_value,marker_16,extra_json)
VALUES(1,0,970001,'equipment',1,10000,-1,'{}');");
                var cargo = ItemCore.Create(ItemCore.KindEquipment, 970001);
                cargo.InstanceValue = 10000;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"INSERT INTO account_cargo_new_items
(account_id,character_id,list_type,slot_index,item_core) VALUES(1,1,12,0,@core);";
                    command.Parameters.AddWithValue("@core", cargo.ToBytes());
                    command.ExecuteNonQuery();
                }
            }

            var report = Coordinator(db).MigrateNewToLegacy();
            Check("mirrored new gold does not add to legacy gold", report.Success
                && Scalar(db, "SELECT stack_count FROM character_items WHERE character_id=1 AND list_type=0 AND slot_index=0;") == 100);
            Check("mirrored new cargo does not duplicate legacy cargo", Scalar(db,
                "SELECT COUNT(*) FROM account_cargo_items WHERE account_id=1;") == 1);
            Check("all consumed new mirror rows are cleared", Scalar(db, "SELECT COUNT(*) FROM character_new_items;") == 0
                && Scalar(db, "SELECT COUNT(*) FROM account_cargo_new_items;") == 0
                && !report.Status.CanDowngrade);
        }

        private static void TestStackableMergeBothDirections(string root)
        {
            var upgradeDb = CreateDatabase(root, "stackable-upgrade.db");
            using (var connection = Open(upgradeDb))
            {
                SeedIdentity(connection);
                InsertNewStack(connection, 65, 980001, 40);
                InsertLegacyStack(connection, 66, 980001, 30);
            }

            var upgrade = Coordinator(upgradeDb, stackLimit: 99).MigrateLegacyToNew();
            Check("legacy stackable merges into matching new stack", upgrade.Success
                && Scalar(upgradeDb, "SELECT COUNT(*) FROM character_new_items WHERE character_id=1 AND list_type=0 AND slot_index BETWEEN 65 AND 120;") == 1
                && ReadNewCount(upgradeDb, "character_new_items", "owner_id=1 AND list_type=0 AND slot_index=65") == 70
                && Scalar(upgradeDb, "SELECT COUNT(*) FROM character_items;") == 0);

            var downgradeDb = CreateDatabase(root, "stackable-downgrade.db");
            using (var connection = Open(downgradeDb))
            {
                SeedIdentity(connection);
                InsertLegacyStack(connection, 65, 980002, 45);
                InsertNewStack(connection, 66, 980002, 25);
            }

            var downgrade = Coordinator(downgradeDb, stackLimit: 99).MigrateNewToLegacy();
            Check("new stackable merges into matching legacy stack", downgrade.Success
                && Scalar(downgradeDb, "SELECT COUNT(*) FROM character_items WHERE character_id=1 AND list_type=0 AND slot_index BETWEEN 65 AND 120;") == 1
                && Scalar(downgradeDb, "SELECT stack_count FROM character_items WHERE character_id=1 AND list_type=0 AND slot_index=65;") == 70
                && Scalar(downgradeDb, "SELECT COUNT(*) FROM character_new_items;") == 0);
        }

        private static void TestStackableLimitAndResidual(string root)
        {
            var splitDb = CreateDatabase(root, "stackable-split.db");
            using (var connection = Open(splitDb))
            {
                SeedIdentity(connection);
                InsertNewStack(connection, 65, 981001, 40);
                InsertLegacyStack(connection, 66, 981001, 70);
            }

            var split = Coordinator(splitDb, stackLimit: 50).MigrateLegacyToNew();
            Check("stackable fills target then splits remainder by PVF limit", split.Success
                && Scalar(splitDb, "SELECT COUNT(*) FROM character_new_items WHERE character_id=1 AND list_type=0 AND slot_index BETWEEN 65 AND 120;") == 3
                && SumNewCount(splitDb, "character_new_items", "owner_id=1 AND list_type=0 AND slot_index BETWEEN 65 AND 120") == 110
                && MaxNewCount(splitDb, "character_new_items", "owner_id=1 AND list_type=0 AND slot_index BETWEEN 65 AND 120") == 50
                && Scalar(splitDb, "SELECT COUNT(*) FROM character_items;") == 0);

            var fullDb = CreateDatabase(root, "stackable-full.db");
            using (var connection = Open(fullDb))
            {
                SeedIdentity(connection);
                for (short slot = 65; slot <= 120; slot++)
                    InsertNewStack(connection, slot, slot == 65 ? 981002 : 982000 + slot, 50);
                InsertLegacyStack(connection, 66, 981002, 30);
            }

            var full = Coordinator(fullDb, stackLimit: 50).MigrateLegacyToNew();
            Check("full bag keeps whole source stack and reports one required slot", full.Success
                && Scalar(fullDb, "SELECT COUNT(*) FROM character_items;") == 1
                && SumNewCount(fullDb, "character_new_items", "owner_id=1 AND item_core IS NOT NULL") == 56 * 50
                && full.Residuals.Count == 1
                && full.Residuals[0].ItemCount == 30
                && full.Residuals[0].RequiredFreeSlots == 1);
        }

        private static void TestAccountCargoStackableBothDirections(string root)
        {
            var upgradeDb = CreateDatabase(root, "cargo-stack-upgrade.db");
            using (var connection = Open(upgradeDb))
            {
                SeedIdentity(connection);
                InsertNewCargoStack(connection, 0, 983001, 40);
                InsertLegacyCargoStack(connection, 1, 983001, 30);
            }

            var upgrade = Coordinator(upgradeDb, stackLimit: 50).MigrateLegacyToNew();
            Check("legacy account cargo stack merges and splits into new cargo", upgrade.Success
                && Scalar(upgradeDb, "SELECT COUNT(*) FROM account_cargo_new_items WHERE account_id=1;") == 2
                && SumNewCount(upgradeDb, "account_cargo_new_items", "account_id=1") == 70
                && MaxNewCount(upgradeDb, "account_cargo_new_items", "account_id=1") == 50
                && Scalar(upgradeDb, "SELECT COUNT(*) FROM account_cargo_items;") == 0);

            var downgradeDb = CreateDatabase(root, "cargo-stack-downgrade.db");
            using (var connection = Open(downgradeDb))
            {
                SeedIdentity(connection);
                InsertLegacyCargoStack(connection, 0, 983002, 45);
                InsertNewCargoStack(connection, 1, 983002, 25);
            }

            var downgrade = Coordinator(downgradeDb, stackLimit: 50).MigrateNewToLegacy();
            Check("new account cargo stack merges and splits into legacy cargo", downgrade.Success
                && Scalar(downgradeDb, "SELECT COUNT(*) FROM account_cargo_items WHERE account_id=1;") == 2
                && Scalar(downgradeDb, "SELECT SUM(stack_count) FROM account_cargo_items WHERE account_id=1;") == 70
                && Scalar(downgradeDb, "SELECT MAX(stack_count) FROM account_cargo_items WHERE account_id=1;") == 50
                && Scalar(downgradeDb, "SELECT COUNT(*) FROM account_cargo_new_items;") == 0);

            var mirrorDb = CreateDatabase(root, "cargo-stack-mirror.db");
            using (var connection = Open(mirrorDb))
            {
                SeedIdentity(connection);
                InsertNewCargoStack(connection, 0, 983003, 35);
                InsertLegacyCargoStack(connection, 0, 983003, 35);
            }

            var mirror = Coordinator(mirrorDb, stackLimit: 50).MigrateLegacyToNew();
            var mirrorLegacyRows = Scalar(mirrorDb, "SELECT COUNT(*) FROM account_cargo_items;");
            var mirrorNewRows = Scalar(mirrorDb, "SELECT COUNT(*) FROM account_cargo_new_items;");
            var mirrorCount = SumNewCount(mirrorDb, "account_cargo_new_items", "account_id=1");
            if (mirrorLegacyRows != 0 || mirrorNewRows != 1 || mirrorCount != 35)
                Console.WriteLine($"mirror diagnostics: legacy={mirrorLegacyRows}, new={mirrorNewRows}, count={mirrorCount}, residual={mirror.Residuals.Count}");
            Check("identical account cargo mirror clears source without doubling stack", mirror.Success
                && mirrorLegacyRows == 0
                && mirrorNewRows == 1
                && mirrorCount == 35);
        }

        private static string CreateDatabase(string root, string name)
        {
            var db = Path.Combine(root, name);
            var schema = Path.Combine(AppContext.BaseDirectory, "ServerCore", "Sqlite", "item_schema.sql");
            SqliteDatabaseBootstrap.CreateTestDatabase(db, schema);
            _ = new NewInventoryStore(db, schema);
            return db;
        }

        private static InventoryDataMigrationCoordinator Coordinator(string db)
            => new InventoryDataMigrationCoordinator(new SqliteConnectionStringBuilder { DataSource = db }.ToString());

        private static InventoryDataMigrationCoordinator Coordinator(string db, int stackLimit)
            => new InventoryDataMigrationCoordinator(
                new SqliteConnectionStringBuilder { DataSource = db }.ToString(),
                _ => stackLimit);

        private static SqliteConnection Open(string db)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db }.ToString());
            connection.Open();
            return connection;
        }

        private static void SeedIdentity(SqliteConnection connection)
        {
            Exec(connection, "UPDATE accounts SET m_id='migration-test',password_hash='' WHERE account_id=1;");
            Exec(connection, "INSERT INTO characters(character_id,account_id,name) VALUES(1,1,'migration-role');");
        }

        private static void InsertLegacy(SqliteConnection connection, short slot, int itemId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO character_items
(owner_scope,owner_id,character_id,list_type,slot_index,item_template_id,item_kind,stack_count,instance_value,extra_json)
VALUES('character',1,1,0,@slot,@item,'equipment',1,10000,'{}');";
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@item", itemId);
            command.ExecuteNonQuery();
        }

        private static void InsertLegacyEquipped(SqliteConnection connection, short slot, int itemId)
        {
            var raw = new byte[40];
            raw[0] = checked((byte)slot);
            BitConverter.GetBytes(itemId).CopyTo(raw, 1);
            BitConverter.GetBytes(10000u).CopyTo(raw, 5);
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO character_equipped_entries
(character_id,slot,item_id,expire_time,equipment_lock_id,raw_entry)
VALUES(1,@slot,@item,0,0,@raw);";
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@item", itemId);
            command.Parameters.AddWithValue("@raw", raw);
            command.ExecuteNonQuery();
        }

        private static void InsertNew(SqliteConnection connection, short slot, int itemId)
        {
            var core = ItemCore.Create(ItemCore.KindEquipment, itemId);
            core.InstanceValue = 10000;
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO character_new_items
(owner_scope,owner_id,character_id,list_type,slot_index,item_core)
VALUES('character',1,1,0,@slot,@core);";
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core.ToBytes());
            command.ExecuteNonQuery();
        }

        private static void InsertLegacyStack(SqliteConnection connection, short slot, int itemId, int count)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO character_items
(owner_scope,owner_id,character_id,list_type,slot_index,item_template_id,item_kind,stack_count,instance_value,extra_json)
VALUES('character',1,1,0,@slot,@item,'stackable',@count,@count,'{}');";
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@item", itemId);
            command.Parameters.AddWithValue("@count", count);
            command.ExecuteNonQuery();
        }

        private static void InsertNewStack(SqliteConnection connection, short slot, int itemId, int count)
        {
            var core = ItemCore.Create(ItemCore.KindConsumable, itemId);
            core.Count = count;
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO character_new_items
(owner_scope,owner_id,character_id,list_type,slot_index,item_core)
VALUES('character',1,1,0,@slot,@core);";
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core.ToBytes());
            command.ExecuteNonQuery();
        }

        private static void InsertLegacyCargoStack(SqliteConnection connection, short slot, int itemId, int count)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO account_cargo_items
(account_id,slot_index,item_template_id,item_kind,stack_count,instance_value,extra_json)
VALUES(1,@slot,@item,'stackable',@count,@count,'{}');";
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@item", itemId);
            command.Parameters.AddWithValue("@count", count);
            command.ExecuteNonQuery();
        }

        private static void InsertNewCargoStack(SqliteConnection connection, short slot, int itemId, int count)
        {
            var core = ItemCore.Create(ItemCore.KindConsumable, itemId);
            core.Count = count;
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO account_cargo_new_items
(account_id,character_id,list_type,slot_index,item_core)
VALUES(1,1,12,@slot,@core);";
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core.ToBytes());
            command.ExecuteNonQuery();
        }

        private static void Exec(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static int Scalar(string db, string sql)
        {
            using var connection = Open(db);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static int ReadNewCount(string db, string table, string where)
        {
            using var connection = Open(db);
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT item_core FROM {table} WHERE {where} LIMIT 1;";
            var bytes = (byte[])command.ExecuteScalar();
            return ItemCore.FromBytes(bytes).Count;
        }

        private static int SumNewCount(string db, string table, string where)
        {
            using var connection = Open(db);
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT item_core FROM {table} WHERE {where};";
            using var reader = command.ExecuteReader();
            var total = 0;
            while (reader.Read())
                total += ItemCore.FromBytes((byte[])reader[0]).Count;
            return total;
        }

        private static int MaxNewCount(string db, string table, string where)
        {
            using var connection = Open(db);
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT item_core FROM {table} WHERE {where};";
            using var reader = command.ExecuteReader();
            var maximum = 0;
            while (reader.Read())
                maximum = Math.Max(maximum, ItemCore.FromBytes((byte[])reader[0]).Count);
            return maximum;
        }

        private static void Check(string name, bool condition)
        {
            Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
            if (!condition)
                _failures++;
        }
    }
}
