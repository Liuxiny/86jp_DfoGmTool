using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    // 租赁期限由服务端 character_rental_items 表独立保存(迁移22后的存储,
    // 旧 character_init_bodies 0x0357 blob 已退役)；背包实例缺少期限时，GM 读侧以它作为显示回退。
    internal sealed class SupplementalItemExpirationService
    {
        private readonly string _connectionString;

        internal SupplementalItemExpirationService(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        internal IReadOnlyDictionary<int, int> LoadRentalExpireTimes(int characterId)
        {
            var expireTimes = new Dictionary<int, int>();
            if (characterId <= 0)
                return expireTimes;

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT inventory_template_id, expire_time
FROM character_rental_items
WHERE character_id = @characterId;";
                    command.Parameters.AddWithValue("@characterId", characterId);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var templateId = reader.GetInt64(0);
                            var expireTimeRaw = reader.GetInt64(1);
                            if (templateId <= 0
                                || templateId > int.MaxValue
                                || expireTimeRaw <= 0
                                || expireTimeRaw > int.MaxValue)
                                continue;

                            var id = (int)templateId;
                            var expireTime = (int)expireTimeRaw;
                            if (!expireTimes.TryGetValue(id, out var existing) || expireTime < existing)
                                expireTimes[id] = expireTime;
                        }
                    }
                }
            }

            return expireTimes;
        }
    }
}
