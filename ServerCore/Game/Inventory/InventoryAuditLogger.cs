// GM瘦身拷贝: 相对服务端原版删除了 WriteOpenSelectablePackageAuditLog(依赖未拷贝的 PackageRewardEntry);
// 保留成员与原版逐字一致
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal sealed class InventoryAuditLogger
    {

        internal void WriteAuditLog(SqliteConnection connection, SqliteTransaction transaction, int characterId, string actionName, SqliteInventoryStore.ItemRecord source, InventoryListType destinationListType, short destinationSlotIndex, int moveCount)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO item_audit_log (
    owner_scope, owner_id, character_id, action_name, list_type, slot_index, item_uid,
    item_template_id, delta_stack_count, payload_json)
VALUES (
    'character', @ownerId, @characterId, @actionName, @listType, @slotIndex, @itemUid,
    @itemTemplateId, @deltaStackCount, @payloadJson);";
                command.Parameters.AddWithValue("@ownerId", characterId);
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@actionName", actionName);
                command.Parameters.AddWithValue("@listType", (int)destinationListType);
                command.Parameters.AddWithValue("@slotIndex", destinationSlotIndex);
                command.Parameters.AddWithValue("@itemUid", source.ItemUid);
                command.Parameters.AddWithValue("@itemTemplateId", source.ItemTemplateId);
                command.Parameters.AddWithValue("@deltaStackCount", moveCount);
                command.Parameters.AddWithValue("@payloadJson", "{\"srcListType\":" + (int)source.ListType + ",\"srcSlotIndex\":" + source.SlotIndex + ",\"dstListType\":" + (int)destinationListType + ",\"dstSlotIndex\":" + destinationSlotIndex + "}");
                command.ExecuteNonQuery();
            }
        }

        internal void WriteDeleteAuditLog(SqliteConnection connection, SqliteTransaction transaction, int characterId, SqliteInventoryStore.ItemRecord source, int deleteCount)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO item_audit_log (
    owner_scope, owner_id, character_id, action_name, list_type, slot_index, item_uid,
    item_template_id, delta_stack_count, payload_json)
VALUES (
    'character', @ownerId, @characterId, 'delete_item', @listType, @slotIndex, @itemUid,
    @itemTemplateId, @deltaStackCount, @payloadJson);";
                command.Parameters.AddWithValue("@ownerId", characterId);
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@listType", (int)source.ListType);
                command.Parameters.AddWithValue("@slotIndex", source.SlotIndex);
                command.Parameters.AddWithValue("@itemUid", source.ItemUid);
                command.Parameters.AddWithValue("@itemTemplateId", source.ItemTemplateId);
                command.Parameters.AddWithValue("@deltaStackCount", -deleteCount);
                command.Parameters.AddWithValue("@payloadJson", "{\"deleteCount\":" + deleteCount + "}");
                command.ExecuteNonQuery();
            }
        }

        internal void WriteGmGrantAuditLog(SqliteConnection connection, SqliteTransaction transaction, int characterId, ItemGrantResult grant)
        {
            if (grant == null || !grant.Success)
                return;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO item_audit_log (
    owner_scope, owner_id, character_id, action_name, list_type, slot_index,
    item_template_id, delta_stack_count, payload_json)
VALUES (
    'character', @ownerId, @characterId, 'gm_grant', @listType, @slotIndex,
    @itemTemplateId, @deltaStackCount, @payloadJson);";
                command.Parameters.AddWithValue("@ownerId", characterId);
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@listType", (int)grant.ListType);
                command.Parameters.AddWithValue("@slotIndex", grant.AssignedSlot);
                command.Parameters.AddWithValue("@itemTemplateId", grant.ItemTemplateId);
                command.Parameters.AddWithValue("@deltaStackCount", grant.GrantedCount);
                command.Parameters.AddWithValue("@payloadJson",
                    "{\"source\":\"gm_tool\",\"requestedCount\":" + grant.RequestedCount.ToString(CultureInfo.InvariantCulture)
                    + ",\"grantedCount\":" + grant.GrantedCount.ToString(CultureInfo.InvariantCulture)
                    + ",\"expireTime\":" + grant.ExpireTime.ToString(CultureInfo.InvariantCulture)
                    + ",\"slots\":[" + string.Join(",", grant.AffectedSlots) + "]}");
                command.ExecuteNonQuery();
            }
        }

        internal void WriteEnchantAuditLog(SqliteConnection connection, SqliteTransaction transaction, int characterId, SqliteInventoryStore.ItemRecord bead, SqliteInventoryStore.ItemRecord target, int enchantCardItemId, byte enchantUpgradeCount)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO item_audit_log (
    owner_scope, owner_id, character_id, action_name, list_type, slot_index, item_uid,
    item_template_id, delta_stack_count, payload_json)
VALUES (
    'character', @ownerId, @characterId, 'enchant_by_bead', @listType, @slotIndex, @itemUid,
    @itemTemplateId, 0, @payloadJson);";
                command.Parameters.AddWithValue("@ownerId", characterId);
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@listType", (int)target.ListType);
                command.Parameters.AddWithValue("@slotIndex", target.SlotIndex);
                command.Parameters.AddWithValue("@itemUid", target.ItemUid);
                command.Parameters.AddWithValue("@itemTemplateId", target.ItemTemplateId);
                command.Parameters.AddWithValue("@payloadJson",
                    "{\"beadItemUid\":" + bead.ItemUid
                    + ",\"beadItemTemplateId\":" + bead.ItemTemplateId
                    + ",\"beadSlotIndex\":" + bead.SlotIndex
                    + ",\"enchantCardItemId\":" + enchantCardItemId
                    + ",\"enchantUpgradeCount\":" + enchantUpgradeCount
                    + "}");
                command.ExecuteNonQuery();
            }
        }

        internal void WriteBuyAuditLog(SqliteConnection connection, SqliteTransaction transaction, int characterId, int itemTemplateId, short slotIndex, int buyGold, int buyCoin)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO item_audit_log (
    owner_scope, owner_id, character_id, action_name, list_type, slot_index,
    item_template_id, delta_stack_count, payload_json)
VALUES (
    'character', @ownerId, @characterId, 'buy_item', @listType, @slotIndex,
    @itemTemplateId, 1, @payloadJson);";
                command.Parameters.AddWithValue("@ownerId", characterId);
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@listType", (int)InventoryListType.Main);
                command.Parameters.AddWithValue("@slotIndex", slotIndex);
                command.Parameters.AddWithValue("@itemTemplateId", itemTemplateId);
                command.Parameters.AddWithValue("@payloadJson", "{\"buyGold\":" + buyGold + ",\"buyCoin\":" + buyCoin + "}");
                command.ExecuteNonQuery();
            }
        }

        internal void WriteOpenPackageAuditLog(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            SqliteInventoryStore.ItemRecord packageItem,
            int addedAvatarCount,
            int addedMainItemCount,
            int addedPetCount)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO item_audit_log (
    owner_scope, owner_id, character_id, action_name, list_type, slot_index, item_uid,
    item_template_id, delta_stack_count, payload_json)
VALUES (
    'character', @ownerId, @characterId, 'open_avatar_package', @listType, @slotIndex, @itemUid,
    @itemTemplateId, -1, @payloadJson);";
                command.Parameters.AddWithValue("@ownerId", characterId);
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@listType", (int)packageItem.ListType);
                command.Parameters.AddWithValue("@slotIndex", packageItem.SlotIndex);
                command.Parameters.AddWithValue("@itemUid", packageItem.ItemUid);
                command.Parameters.AddWithValue("@itemTemplateId", packageItem.ItemTemplateId);
                command.Parameters.AddWithValue("@payloadJson",
                    "{\"addedAvatarCount\":" + addedAvatarCount
                    + ",\"addedMainItemCount\":" + addedMainItemCount
                    + ",\"addedPetCount\":" + addedPetCount + "}");
                command.ExecuteNonQuery();
            }
        }

        // GM瘦身拷贝: 此处删除了 WriteOpenSelectablePackageAuditLog(依赖未拷贝的 PackageRewardEntry)
        internal void WriteSellAuditLog(SqliteConnection connection, SqliteTransaction transaction, int characterId, SqliteInventoryStore.ItemRecord source, int sellCount, int goldDelta)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO item_audit_log (
    owner_scope, owner_id, character_id, action_name, list_type, slot_index, item_uid,
    item_template_id, delta_stack_count, payload_json)
VALUES (
    'character', @ownerId, @characterId, 'sell_item', @listType, @slotIndex, @itemUid,
    @itemTemplateId, @deltaStackCount, @payloadJson);";
                command.Parameters.AddWithValue("@ownerId", characterId);
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@listType", (int)source.ListType);
                command.Parameters.AddWithValue("@slotIndex", source.SlotIndex);
                command.Parameters.AddWithValue("@itemUid", source.ItemUid);
                command.Parameters.AddWithValue("@itemTemplateId", source.ItemTemplateId);
                command.Parameters.AddWithValue("@deltaStackCount", -sellCount);
                command.Parameters.AddWithValue("@payloadJson", "{\"sellCount\":" + sellCount + ",\"goldDelta\":" + goldDelta + "}");
                command.ExecuteNonQuery();
            }
        }

        internal void WriteSortAuditLog(SqliteConnection connection, SqliteTransaction transaction, int characterId, InventoryListType listType, int affectedCount)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO item_audit_log (
    owner_scope, owner_id, character_id, action_name, list_type, delta_stack_count, payload_json)
VALUES (
    'character', @ownerId, @characterId, 'sort_item', @listType, 0, @payloadJson);";
                command.Parameters.AddWithValue("@ownerId", characterId);
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@listType", (int)listType);
                command.Parameters.AddWithValue("@payloadJson", "{\"affectedCount\":" + affectedCount + "}");
                command.ExecuteNonQuery();
            }
        }
    }
}
