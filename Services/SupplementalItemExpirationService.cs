using System;
using System.Collections.Generic;
using DfoGmTool.ServerCore.Game.SelectCharacter;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    // 租赁期限由选角 0x0357 独立保存；背包实例缺少期限时，GM 读侧以它作为显示回退。
    internal sealed class SupplementalItemExpirationService
    {
        private const int RentalInfoNotificationType = 0x0357;
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

            byte[] body = null;
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT body
FROM character_init_bodies
WHERE character_id = @characterId
  AND noti_type = @notiType
ORDER BY occurrence_index
LIMIT 1;";
                    command.Parameters.AddWithValue("@characterId", characterId);
                    command.Parameters.AddWithValue("@notiType", RentalInfoNotificationType);
                    var value = command.ExecuteScalar();
                    if (value is byte[] bytes)
                        body = bytes;
                }
            }

            if (body == null || body.Length == 0)
                return expireTimes;

            var rental = new RentalInfoSnapshot();
            RentalInfoSnapshot.ParseStorageBody(body, rental);
            foreach (var item in rental.Items)
            {
                if (item == null
                    || item.InventoryTemplateId == 0
                    || item.InventoryTemplateId > int.MaxValue
                    || item.ExpireTime == 0
                    || item.ExpireTime > int.MaxValue)
                    continue;

                var templateId = (int)item.InventoryTemplateId;
                var expireTime = (int)item.ExpireTime;
                if (!expireTimes.TryGetValue(templateId, out var existing) || expireTime < existing)
                    expireTimes[templateId] = expireTime;
            }

            return expireTimes;
        }
    }
}
