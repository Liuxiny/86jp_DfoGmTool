using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace DfoGmTool.SelfTests
{
    internal static class DatabaseCompatibilitySelfTest
    {
        public static int Run()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "dfo-gm-schema-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var schema = Path.Combine(
                AppContext.BaseDirectory,
                "ServerCore",
                "Sqlite",
                "item_schema.sql");
            var failures = 0;
            try
            {
                var currentPath = Path.Combine(root, "current.db");
                SqliteDatabaseBootstrap.CreateTestDatabase(currentPath, schema);
                var currentHash = Hash(currentPath);
                var current = DatabaseCompatibilityGuard.Validate(currentPath);
                Check(
                    "真实 A21 schema v8 接受并报告 baseline",
                    current.SchemaVersion == A12ToA21MigrationService.TargetSchemaVersion
                    && current.MetadataSchemaVersion == A12ToA21MigrationService.TargetSchemaVersion
                    && current.BaselineId == "86jp-database-v1"
                    && current.StructureCompatible,
                    ref failures);
                Check("guard 不修改真实 A21 数据库", currentHash.SequenceEqual(Hash(currentPath)), ref failures);

                foreach (var version in new[] { 0, 52, 999 })
                {
                    var path = Path.Combine(root, "version-" + version + ".db");
                    CreateVersionedDatabase(path, schema, version);
                    var beforeHash = Hash(path);
                    var beforeVersion = ReadVersion(path);
                    var report = DatabaseCompatibilityGuard.Validate(path);
                    Check(
                        $"同结构 user_version={version} 接受",
                        report.SchemaVersion == version
                        && beforeVersion == version
                        && beforeHash.SequenceEqual(Hash(path)),
                        ref failures);
                }

                var missingTablePath = Path.Combine(root, "missing-table.db");
                CreateVersionedDatabase(missingTablePath, schema, 5);
                Execute(missingTablePath, "DROP TABLE character_inventory_items;");
                Check(
                    "缺少 A21 背包表时报告具体表名",
                    Rejects(missingTablePath, "character_inventory_items"),
                    ref failures);

                var missingColumnPath = Path.Combine(root, "missing-column.db");
                CreateVersionedDatabase(missingColumnPath, schema, 5);
                Execute(
                    missingColumnPath,
                    "ALTER TABLE account_inventory_items RENAME COLUMN item_core TO item_core_legacy;");
                Check(
                    "缺少 item_core 列时报告具体列名",
                    Rejects(missingColumnPath, "account_inventory_items.item_core"),
                    ref failures);

                var missingCheckPath = Path.Combine(root, "missing-check.db");
                CreateVersionedDatabase(missingCheckPath, schema, 5);
                Execute(
                    missingCheckPath,
                    @"
PRAGMA foreign_keys = OFF;
DROP TABLE character_inventory_items;
CREATE TABLE character_inventory_items (
    item_uid INTEGER PRIMARY KEY,
    character_id INTEGER NOT NULL,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_core BLOB
);");
                Check(
                    "空的 item_core 表缺少99B CHECK时拒绝",
                    Rejects(missingCheckPath, "缺少99B CHECK"),
                    ref failures);

                var badCorePath = Path.Combine(root, "bad-core.db");
                CreateVersionedDatabase(badCorePath, schema, 5);
                Execute(
                    badCorePath,
                    @"
PRAGMA foreign_keys = OFF;
DROP TABLE character_inventory_items;
CREATE TABLE character_inventory_items (
    item_uid INTEGER PRIMARY KEY,
    character_id INTEGER NOT NULL,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_core BLOB
);
INSERT INTO character_inventory_items(item_uid,character_id,list_type,slot_index,item_core)
VALUES(1,1,0,9,zeroblob(82));");
                Check(
                    "82B item_core 被拒绝并报告长度",
                    Rejects(badCorePath, "82"),
                    ref failures);

                var emptyPath = Path.Combine(root, "empty.db");
                File.WriteAllBytes(emptyPath, Array.Empty<byte>());
                Check(
                    "空数据库不由 GM 初始化",
                    Rejects(emptyPath, "为空或不存在"),
                    ref failures);

                var codecData = Enumerable.Range(0, ItemCore.Size)
                    .Select(index => (byte)((index * 37 + 11) & 0xFF))
                    .ToArray();
                var codec = ItemCore.FromBytes(codecData);
                Check(
                    "A21 ItemCore 99B 全字节 round-trip",
                    codec.ToBytes().SequenceEqual(codecData)
                    && ItemCore.Size == 99
                    && ItemCore.KindGuildMedal == 12
                    && ItemCore.KindGuardianGem == 13
                    && ItemCore.KindEpicPiece == 14,
                    ref failures);
                Check(
                    "GuildMedal 使用 A21 list_type=38",
                    (int)InventoryListType.GuildMedal == 38,
                    ref failures);
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("[FAIL] unexpected exception: " + ex);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }

            Console.WriteLine(
                failures == 0
                    ? "DatabaseCompatibilitySelfTest OK"
                    : $"DatabaseCompatibilitySelfTest FAIL: {failures}");
            return failures == 0 ? 0 : 1;
        }

        private static void CreateVersionedDatabase(
            string path,
            string schema,
            int version)
        {
            using (var connection = Open(path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = File.ReadAllText(schema);
                command.ExecuteNonQuery();
                command.CommandText = "PRAGMA user_version = " + version + ";";
                command.ExecuteNonQuery();
                command.CommandText = @"
INSERT OR REPLACE INTO schema_metadata
    (singleton_id, baseline_id, schema_version, created_at, updated_at)
VALUES
    (1, '86jp-database-v1', " + A12ToA21MigrationService.TargetSchemaVersion + @", CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);";
                command.ExecuteNonQuery();
            }
        }

        private static void Execute(string path, string sql)
        {
            using (var connection = Open(path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    ForeignKeys = true,
                    Pooling = false
                }.ConnectionString);
            connection.Open();
            return connection;
        }

        private static long ReadVersion(string path)
        {
            using (var connection = Open(path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version;";
                return Convert.ToInt64(command.ExecuteScalar());
            }
        }

        private static byte[] Hash(string path)
        {
            return SHA256.HashData(File.ReadAllBytes(path));
        }

        private static bool Rejects(string path, string messageFragment)
        {
            try
            {
                DatabaseCompatibilityGuard.Validate(path);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message.IndexOf(
                    messageFragment,
                    StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static void Check(
            string name,
            bool condition,
            ref int failures)
        {
            Console.WriteLine((condition ? "[PASS] " : "[FAIL] ") + name);
            if (!condition)
                failures++;
        }
    }
}
