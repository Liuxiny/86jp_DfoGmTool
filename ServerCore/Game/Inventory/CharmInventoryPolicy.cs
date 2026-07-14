using DfoGmTool.ServerCore.Game.ItemUpgrade;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal static class CharmInventoryPolicy
    {
        internal static bool IsCharm(int itemTemplateId)
            => EquipmentTypeInfo.ParseOrUnknown(ItemMetadataResolver.ResolveEquipmentType(itemTemplateId)) == EquipmentType.Charm;

        internal static bool IsCharmItem(int itemTemplateId) => IsCharm(itemTemplateId);

        internal static bool CanEnterMain(SqliteConnection connection, SqliteTransaction transaction,
            int characterId, int itemTemplateId, long excludedItemUid1 = 0, long excludedItemUid2 = 0)
        {
            if (!IsCharm(itemTemplateId)) return true;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT item_uid, item_template_id FROM character_items
WHERE character_id=@characterId AND list_type=@mainList
  AND item_uid<>@excluded1 AND item_uid<>@excluded2;";
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@mainList", (int)InventoryListType.Main);
                command.Parameters.AddWithValue("@excluded1", excludedItemUid1);
                command.Parameters.AddWithValue("@excluded2", excludedItemUid2);
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                        if (IsCharm(reader.GetInt32(1))) return false;
            }
            return true;
        }
    }
}
