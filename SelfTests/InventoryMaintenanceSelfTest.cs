using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.GameWorld;
using DfoGmTool.ServerCore.Infrastructure;
using DfoGmTool.Services;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.SelfTests
{
    internal static class InventoryMaintenanceSelfTest
    {
        private const int AccountOne = 810001;
        private const int AccountTwo = 810002;
        private const int CharacterOne = 810011;
        private const int CharacterTwo = 810012;
        private static int _failures;

        internal static int Run()
        {
            _failures = 0;
            Console.WriteLine("=== INVENTORY_MAINTENANCE selftest ===");
            var dbPath = Path.Combine(
                Path.GetTempPath(),
                "dfogm-inventory-maintenance-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var schema = Path.Combine(AppContext.BaseDirectory, "ServerCore", "Sqlite", "item_schema.sql");
                var pvf = ResolveLatestServerPvf();
                Check("latest server PVF exists", pvf != null);
                if (pvf == null)
                    return 1;

                SqliteDatabaseBootstrap.CreateTestDatabase(dbPath, schema);
                SeedDatabase(dbPath);
                Check("GM config loads inventory maintenance database",
                    GmConfig.TryCreate(dbPath, pvf, out var config, out var configError), configError);
                if (config == null)
                    return 1;

                PvfArchiveAccessor.Configure(pvf);
                PvfRuntimeCache.ResetForPvfChange();
                GmService.ResetPvfStaticData();
                var index = new PvfIndexService(pvf);
                index.WarmInBackground();
                WaitForIndex(index);
                if (!index.IsReady)
                    return 1;

                var legalId = index.AllItems.FirstOrDefault()?.Id ?? 0;
                Check("PVF exposes a legal parsed item ID", legalId > 0 && index.ContainsItemId(legalId));
                Check("obviously fake item ID is not legal", !index.ContainsItemId(987654321));
                if (legalId <= 0)
                    return 1;

                SeedInventoryRows(dbPath, legalId);
                var gm = new GmService(config, index);
                var status = gm.GetInventoryAnomalyStatus(index);
                Check("status scans every role and account cargo row", IsSuccess(status));
                Check("status reports source totals and stable details",
                    GetIntProperty(status, "totalCount") == 5
                    && GetIntProperty(status, "characterCount") == 4
                    && GetIntProperty(status, "accountCargoCount") == 1);
                var detailsOk = DetailCount(status) == 5
                    && DetailHas(status, "character", CharacterOne, 3, 987654321, "item_id_not_in_pvf")
                    && DetailHas(status, "character", CharacterTwo, 0, 0, "item_core_null_or_invalid_length")
                    && DetailHas(status, "accountCargo", 0, 12, 987654322, "item_id_not_in_pvf");
                Check("status details include all anomaly reasons and containers", detailsOk);
                Check("status decodes UTF-8 BLOB character names",
                    DetailHasName(status, "character", CharacterOne, "角色一"));
                Check("virtual currency slots are never reported",
                    DetailHasNo(status, "character", CharacterOne, 0, 0));

                SeedMaintenanceFailureTrigger(dbPath);
                var rollback = gm.CleanInventoryAnomalies(index);
                Check("trigger failure returns error rather than fake success",
                    !IsSuccess(rollback));
                Check("trigger failure rolls back every anomaly row",
                    LoadInt(dbPath, "SELECT COUNT(*) FROM character_new_items WHERE item_uid IN (9001,9002,9003,9004);") == 4
                    && LoadInt(dbPath, "SELECT COUNT(*) FROM account_cargo_new_items WHERE item_uid=9005;") == 1);
                DropMaintenanceFailureTrigger(dbPath);

                var cleaned = gm.CleanInventoryAnomalies(index);
                Check("clean removes all then-current anomalies", IsSuccess(cleaned)
                    && GetIntProperty(cleaned, "deletedCount") == 5
                    && GetIntProperty(cleaned, "totalCount") == 0
                    && !GetBoolProperty(cleaned, "hasAnomalies"));
                Check("legal and virtual rows remain",
                    LoadInt(dbPath, "SELECT COUNT(*) FROM character_new_items WHERE item_uid IN (9000,9006);") == 2);
                Check("avatar/creature details and locks tied to deleted cores are removed",
                    LoadInt(dbPath, "SELECT COUNT(*) FROM character_avatar_detail WHERE item_uid=7001;") == 0
                    && LoadInt(dbPath, "SELECT COUNT(*) FROM character_creatures WHERE character_id=810011 AND creature_key=8001;") == 0
                    && LoadInt(dbPath, "SELECT COUNT(*) FROM character_item_locks WHERE character_id=810011 AND equipment_lock_id IN (9,10);") == 0);
                Check("unrelated detail and lock rows are preserved",
                    LoadInt(dbPath, "SELECT COUNT(*) FROM character_avatar_detail WHERE item_uid=7002;") == 1
                    && LoadInt(dbPath, "SELECT COUNT(*) FROM character_creatures WHERE character_id=810011 AND creature_key=8002;") == 1
                    && LoadInt(dbPath, "SELECT COUNT(*) FROM character_item_locks WHERE character_id=810011 AND equipment_lock_id=99;") == 1);
                Check("cleanup writes character and account audit rows",
                    LoadInt(dbPath, "SELECT COUNT(*) FROM inventory_audit_log_v2 WHERE action_name='gm_inventory_anomaly_cleanup';") == 5);

                var second = gm.CleanInventoryAnomalies(index);
                Check("second cleanup is idempotent", IsSuccess(second)
                    && GetIntProperty(second, "deletedCount") == 0
                    && GetIntProperty(second, "totalCount") == 0);

                Console.WriteLine(_failures == 0
                    ? "InventoryMaintenanceSelfTest OK"
                    : "InventoryMaintenanceSelfTest FAIL: " + _failures);
                return _failures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("InventoryMaintenanceSelfTest EXCEPTION: " + ex);
                return 1;
            }
            finally
            {
                try
                {
                    if (File.Exists(dbPath))
                        File.Delete(dbPath);
                }
                catch
                {
                    // Best-effort cleanup only; test result is already recorded.
                }
            }
        }

        private static void SeedDatabase(string dbPath)
        {
            using var connection = Open(dbPath);
            using var transaction = connection.BeginTransaction();
            Exec(connection, transaction, $@"
INSERT INTO accounts(account_id,m_id,password_hash) VALUES
({AccountOne},'inventory-maintenance-one',''),
({AccountTwo},'inventory-maintenance-two','');");
            Exec(connection, transaction, $@"
INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,slot_index) VALUES
({CharacterOne},{AccountOne},CAST(X'E8A792E889B2E4B880' AS BLOB),0,0,1,0,0),
({CharacterTwo},{AccountTwo},'inventory-maintenance-two',0,0,1,0,0);");
            transaction.Commit();
        }

        private static void SeedInventoryRows(string dbPath, int legalId)
        {
            using var connection = Open(dbPath);
            using var transaction = connection.BeginTransaction();
            var valid = ItemCore.Create(ItemCore.KindConsumable, legalId);
            valid.Count = 3;
            InsertCharacter(connection, transaction, 9000, CharacterOne, 0, 65, valid.ToBytes());

            // Wallet slot: item id zero is valid virtual state and must not be flagged.
            var wallet = ItemCore.Create(ItemCore.KindSpecialMaterial, 0);
            wallet.Count = 42;
            InsertCharacter(connection, transaction, 9006, CharacterOne, 0, 0, wallet.ToBytes());

            var avatar = ItemCore.Create(ItemCore.KindAvatar, 987654321);
            avatar.AvatarUid = 7001;
            avatar.EquipmentLockId = 9;
            InsertCharacter(connection, transaction, 9001, CharacterOne, 3, 0, avatar.ToBytes());
            Exec(connection, transaction, @"
INSERT INTO character_avatar_detail(item_uid,owner_id,character_id,item_id,jewel_socket)
VALUES(7001,810001,810011,987654321,zeroblob(30)),
      (7002,810001,810011,123456,zeroblob(30));");

            var creature = ItemCore.Create(ItemCore.KindCreature, 987654320);
            creature.CreatureUid = 8001;
            creature.EquipmentLockId = 10;
            InsertCharacter(connection, transaction, 9003, CharacterOne, 7, 0, creature.ToBytes());
            Exec(connection, transaction, @"
INSERT INTO character_creatures(character_id,sort_order,creature_key,field04)
VALUES(810011,0,8001,100),(810011,1,8002,100);");
            Exec(connection, transaction, @"
INSERT INTO character_item_locks(character_id,equipment_lock_id,inventory_list_type,slot,state)
VALUES(810011,9,3,0,1),(810011,10,7,0,1),(810011,99,0,65,1);");

            var invalidId = ItemCore.Create(ItemCore.KindMaterial, 987654319);
            InsertCharacter(connection, transaction, 9004, CharacterTwo, 2, 0, invalidId.ToBytes());
            // A short BLOB exercises the malformed-core branch. The schema
            // check is temporarily disabled only for this selftest fixture.
            Exec(connection, transaction, "PRAGMA ignore_check_constraints=ON;");
            InsertCharacter(connection, transaction, 9002, CharacterTwo, 0, 65, new byte[] { 1 });
            Exec(connection, transaction, "PRAGMA ignore_check_constraints=OFF;");

            var accountValid = ItemCore.Create(ItemCore.KindMaterial, legalId);
            InsertAccountCargo(connection, transaction, 9006, AccountOne, 12, 0, accountValid.ToBytes());
            var accountInvalid = ItemCore.Create(ItemCore.KindMaterial, 987654322);
            InsertAccountCargo(connection, transaction, 9005, AccountTwo, 12, 0, accountInvalid.ToBytes());
            transaction.Commit();
        }

        private static void InsertCharacter(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long itemUid,
            int characterId,
            int listType,
            int slot,
            byte[] itemCore)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO character_new_items(item_uid,owner_scope,owner_id,character_id,list_type,slot_index,item_core)
VALUES(@uid,'character',@cid,@cid,@list,@slot,@core);";
            command.Parameters.AddWithValue("@uid", itemUid);
            command.Parameters.AddWithValue("@cid", characterId);
            command.Parameters.AddWithValue("@list", listType);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.Add("@core", SqliteType.Blob).Value = itemCore;
            command.ExecuteNonQuery();
        }

        private static void InsertAccountCargo(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long itemUid,
            int accountId,
            int listType,
            int slot,
            byte[] itemCore)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO account_cargo_new_items(item_uid,account_id,character_id,list_type,slot_index,item_core)
VALUES(@uid,@aid,NULL,@list,@slot,@core);";
            command.Parameters.AddWithValue("@uid", itemUid);
            command.Parameters.AddWithValue("@aid", accountId);
            command.Parameters.AddWithValue("@list", listType);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.Add("@core", SqliteType.Blob).Value = itemCore;
            command.ExecuteNonQuery();
        }

        private static void SeedMaintenanceFailureTrigger(string dbPath)
        {
            using var connection = Open(dbPath);
            using var transaction = connection.BeginTransaction();
            Exec(connection, transaction, @"
CREATE TRIGGER inventory_maintenance_abort_audit
BEFORE INSERT ON inventory_audit_log_v2
WHEN NEW.action_name='gm_inventory_anomaly_cleanup'
BEGIN SELECT RAISE(ABORT,'inventory maintenance selftest failure'); END;");
            transaction.Commit();
        }

        private static void DropMaintenanceFailureTrigger(string dbPath)
        {
            using var connection = Open(dbPath);
            using var transaction = connection.BeginTransaction();
            Exec(connection, transaction, "DROP TRIGGER inventory_maintenance_abort_audit;");
            transaction.Commit();
        }

        private static int DetailCount(object result)
        {
            var property = result?.GetType().GetProperty("details", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return property?.GetValue(result) is System.Collections.ICollection collection ? collection.Count : 0;
        }

        private static bool DetailHas(object result, string source, int characterId, int listType, int itemId, string reason)
        {
            var property = result?.GetType().GetProperty("details", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (!(property?.GetValue(result) is System.Collections.IEnumerable details))
                return false;
            foreach (var detail in details)
            {
                if (string.Equals(GetStringProperty(detail, "source"), source, StringComparison.Ordinal)
                    && GetIntProperty(detail, "characterId") == characterId
                    && GetIntProperty(detail, "listType") == listType
                    && GetIntProperty(detail, "itemId") == itemId
                    && string.Equals(GetStringProperty(detail, "reason"), reason, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool DetailHasNo(object result, string source, int characterId, int listType, int itemId)
            => !DetailHas(result, source, characterId, listType, itemId, "item_id_non_positive")
                && !DetailHas(result, source, characterId, listType, itemId, "item_id_not_in_pvf")
                && !DetailHas(result, source, characterId, listType, itemId, "item_core_null_or_invalid_length")
                && !DetailHas(result, source, characterId, listType, itemId, "item_core_decode_failed");

        private static bool DetailHasName(object result, string source, int characterId, string expectedName)
        {
            var property = result?.GetType().GetProperty("details", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (!(property?.GetValue(result) is System.Collections.IEnumerable details))
                return false;
            foreach (var detail in details)
            {
                if (string.Equals(GetStringProperty(detail, "source"), source, StringComparison.Ordinal)
                    && GetIntProperty(detail, "characterId") == characterId
                    && string.Equals(GetStringProperty(detail, "characterName"), expectedName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static int GetIntProperty(object value, string name)
        {
            var property = value?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return property == null ? 0 : Convert.ToInt32(property.GetValue(value));
        }

        private static string GetStringProperty(object value, string name)
        {
            var property = value?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return property?.GetValue(value)?.ToString();
        }

        private static bool GetBoolProperty(object value, string name)
        {
            var property = value?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return property != null && Convert.ToBoolean(property.GetValue(value));
        }

        private static bool IsSuccess(object value) => GetBoolProperty(value, "success");

        private static int LoadInt(string dbPath, string sql)
        {
            using var connection = Open(dbPath);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static void Exec(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static SqliteConnection Open(string dbPath)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
            connection.Open();
            return connection;
        }

        private static void WaitForIndex(PvfIndexService index)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!index.IsReady && string.IsNullOrWhiteSpace(index.BuildError) && DateTime.UtcNow < deadline)
                Thread.Sleep(100);
            Check("PVF index ready", index.IsReady && string.IsNullOrWhiteSpace(index.BuildError), index.BuildError);
        }

        private static string ResolveLatestServerPvf()
        {
            foreach (var root in EnumerateSearchRoots())
            {
                var codesRoot = Path.Combine(root, "Codes");
                if (!Directory.Exists(codesRoot))
                    continue;
                foreach (var serverDir in Directory.GetDirectories(codesRoot, "ServerS4A12_*").OrderByDescending(value => value))
                {
                    foreach (var path in new[]
                    {
                        Path.Combine(serverDir, "dist", "linux-x64", "Data", "Pvf", "Script.pvf"),
                        Path.Combine(serverDir, "Server", "DfoServer", "bin", "Release", "win-x64", "Data", "Pvf", "Script.pvf"),
                        Path.Combine(serverDir, "Server", "DfoServer", "bin", "Debug", "Data", "Pvf", "Script.pvf"),
                        Path.Combine(serverDir, "Server", "DfoServer", "Data", "Pvf", "Script.pvf"),
                    })
                    {
                        if (File.Exists(path))
                            return path;
                    }
                }
            }
            return null;
        }

        private static IEnumerable<string> EnumerateSearchRoots()
        {
            var roots = new List<string>();
            AddRoot(roots, Directory.GetCurrentDirectory());
            AddRoot(roots, AppContext.BaseDirectory);
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && directory != null; i++, directory = directory.Parent)
                AddRoot(roots, directory.FullName);
            return roots;
        }

        private static void AddRoot(List<string> roots, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }
            if (!roots.Contains(path, StringComparer.OrdinalIgnoreCase))
                roots.Add(path);
        }

        private static void Check(string name, bool condition, string error = null)
        {
            if (condition)
            {
                Console.WriteLine("PASS " + name);
                return;
            }
            _failures++;
            Console.Error.WriteLine("FAIL " + name + (string.IsNullOrWhiteSpace(error) ? string.Empty : ": " + error));
        }
    }
}
