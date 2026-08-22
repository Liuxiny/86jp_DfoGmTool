using System;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.SelfTests
{
    internal static class MySqlTranslationSelfTest
    {
        public static int Run()
        {
            var failures = 0;
            Check("UPDATE accounts SET cera=MAX(0,cera+@v);", "GREATEST(0, cera+@v)", ref failures);
            Check("UPDATE accounts SET cera=MIN(999,cera+@v);", "LEAST(999, cera+@v)", ref failures);
            Check("INSERT INTO seq DEFAULT VALUES; SELECT last_insert_rowid();", "INSERT INTO seq VALUES (); SELECT LAST_INSERT_ID();", ref failures);
            Check("INSERT OR REPLACE INTO t(id,value) VALUES(@id,@value);", "ON DUPLICATE KEY UPDATE", ref failures);
            var upsert = MySqlTranslator.Translate(@"INSERT INTO t(id,value) VALUES(@id,@value)
ON CONFLICT(id) DO UPDATE SET value=MAX(t.value,excluded.value) WHERE excluded.value>t.value;");
            if (!upsert.Contains("ON DUPLICATE KEY UPDATE", StringComparison.OrdinalIgnoreCase)
                || !upsert.Contains("GREATEST(t.value, VALUES(value))", StringComparison.OrdinalIgnoreCase)
                || upsert.Contains(" WHERE ", StringComparison.OrdinalIgnoreCase))
                failures++;
            Console.WriteLine(failures == 0 ? "MySqlTranslationSelfTest OK" : $"MySqlTranslationSelfTest FAIL: {failures}");
            return failures == 0 ? 0 : 1;
        }

        private static void Check(string sql, string expected, ref int failures)
        {
            if (!MySqlTranslator.Translate(sql).Contains(expected, StringComparison.OrdinalIgnoreCase)) failures++;
        }
    }
}
