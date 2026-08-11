using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed partial class NewInventoryStore
    {
        // 异常库存维护必须与正常库存删除使用同一套关联状态、锁和审计
        // 语义；此入口仅供 GmService 在已持有 IMMEDIATE 事务时调用。
        internal void DeleteCharacterAnomalyCore(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long itemUid,
            int characterId,
            int accountId,
            InventoryListType listType,
            short slotIndex,
            byte[] itemCoreBytes)
        {
            var core = TryDecodeCore(itemCoreBytes);
            var record = new NewInventoryItemRecord
            {
                ItemUid = itemUid,
                CharacterId = characterId,
                AccountId = accountId,
                ListType = listType,
                SlotIndex = slotIndex,
                Core = core,
            };

            DeleteCoreRow(connection, transaction, record);
            if (core != null)
                DeleteAssociatedState(connection, transaction, characterId, core);
            WriteAudit(
                connection,
                transaction,
                "gm_inventory_anomaly_cleanup",
                characterId,
                accountId,
                listType,
                slotIndex,
                core,
                null,
                itemUid);
        }

        internal void DeleteAccountCargoAnomalyCore(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long itemUid,
            int accountId,
            int characterId,
            short slotIndex,
            byte[] itemCoreBytes)
        {
            var core = TryDecodeCore(itemCoreBytes);
            var record = new NewInventoryItemRecord
            {
                ItemUid = itemUid,
                CharacterId = characterId,
                AccountId = accountId,
                ListType = InventoryListType.AccountCargo,
                SlotIndex = slotIndex,
                Core = core,
            };

            DeleteCoreRow(connection, transaction, record);
            WriteAudit(
                connection,
                transaction,
                "gm_inventory_anomaly_cleanup",
                characterId,
                accountId,
                InventoryListType.AccountCargo,
                slotIndex,
                core,
                null,
                itemUid);
        }

        private static ItemCore TryDecodeCore(byte[] bytes)
        {
            if (bytes == null || bytes.Length != ItemCore.Size)
                return null;
            try
            {
                return ItemCore.FromBytes(bytes);
            }
            catch
            {
                return null;
            }
        }
    }
}
