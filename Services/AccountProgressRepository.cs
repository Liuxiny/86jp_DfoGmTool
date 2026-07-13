using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    // 只承担账户列的读取和写入，范围校验与 PVF 计算留在服务层。
    internal sealed class AccountProgressRepository
    {
        private readonly string _connectionString;

        public AccountProgressRepository(string databasePath, string schemaPath)
        {
            _connectionString = SqliteDatabaseBootstrap.Initialize(databasePath, schemaPath);
        }

        public bool TryLoad(int accountId, out AccountProgressRecord record)
        {
            record = default;
            if (accountId <= 0)
                return false;

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT honor_exp, growth_capsule_exp
FROM accounts
WHERE account_id = @accountId;";
                    command.Parameters.AddWithValue("@accountId", accountId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                            return false;

                        record = new AccountProgressRecord(reader.GetInt64(0), reader.GetInt64(1));
                        return true;
                    }
                }
            }
        }

        public bool TrySetHonorExp(int accountId, long totalExp)
        {
            return TrySetValue(accountId, "honor_exp", totalExp);
        }

        public bool TrySetGrowthCapsuleExp(int accountId, long totalExp)
        {
            return TrySetValue(accountId, "growth_capsule_exp", totalExp);
        }

        private bool TrySetValue(int accountId, string column, long value)
        {
            if (accountId <= 0)
                return false;

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE accounts SET " + column + " = @value WHERE account_id = @accountId;";
                    command.Parameters.AddWithValue("@accountId", accountId);
                    command.Parameters.AddWithValue("@value", value);
                    return command.ExecuteNonQuery() == 1;
                }
            }
        }
    }

    internal readonly struct AccountProgressRecord
    {
        public AccountProgressRecord(long honorExp, long growthCapsuleExp)
        {
            HonorExp = honorExp;
            GrowthCapsuleExp = growthCapsuleExp;
        }

        public long HonorExp { get; }
        public long GrowthCapsuleExp { get; }
    }
}
