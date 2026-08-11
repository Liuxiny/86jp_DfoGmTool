using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;
using System;
using System.IO;

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
                SqliteDatabaseBootstrap.CreateTestDatabase(
                    currentPath,
                    schema);
                var current =
                    DatabaseCompatibilityGuard.Validate(currentPath);
                Check(
                    "v52 final schema is accepted",
                    current.SchemaVersion == 52,
                    ref failures);

                var oldPath = Path.Combine(root, "old.db");
                CreateVersionedDatabase(oldPath, schema, 51);
                var oldRejected = Rejects(
                    oldPath,
                    "过旧");
                var oldVersionBefore = ReadVersion(oldPath);
                try
                {
                    SqliteDatabaseBootstrap.Initialize(oldPath, schema);
                }
                catch (InvalidOperationException)
                {
                }
                var oldVersionAfter = ReadVersion(oldPath);
                Check(
                    "v51 is rejected without migration or version mutation",
                    oldRejected
                    && oldVersionBefore == 51
                    && oldVersionAfter == 51,
                    ref failures);

                var futurePath = Path.Combine(root, "future.db");
                CreateVersionedDatabase(futurePath, schema, 53);
                Check(
                    "future v53 fails closed",
                    Rejects(futurePath, "高于"),
                    ref failures);

                var emptyPath = Path.Combine(root, "empty.db");
                File.WriteAllBytes(emptyPath, Array.Empty<byte>());
                Check(
                    "empty database is not initialized by GM",
                    Rejects(emptyPath, "为空或不存在"),
                    ref failures);

                var malformedPath = Path.Combine(root, "malformed.db");
                using (var connection = Open(malformedPath))
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
CREATE TABLE accounts(account_id INTEGER PRIMARY KEY);
CREATE TABLE characters(character_id INTEGER PRIMARY KEY);
PRAGMA user_version = 52;";
                    command.ExecuteNonQuery();
                }
                Check(
                    "v52 marker with missing tables/columns is rejected",
                    Rejects(malformedPath, "缺少最新版契约"),
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
                command.CommandText = $"PRAGMA user_version = {version};";
                command.ExecuteNonQuery();
            }
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    ForeignKeys = true
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
