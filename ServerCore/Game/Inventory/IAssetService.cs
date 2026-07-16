using DfoGmTool.ServerCore.Game.Currency;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public interface IAssetService
    {
        DbScope OpenScope(int characterId, int accountId);

        bool TryAddItem(DbScope scope, int itemTemplateId, int count, out short assignedSlot);
        ItemGrantResult TryGrantCharacterItem(DbScope scope, int itemTemplateId, int count, ItemGrantOptions options = null);
        bool TryRemoveItem(DbScope scope, int itemTemplateId, int count, out short slot, out int remaining);
        int CountItem(DbScope scope, int itemTemplateId);

        WalletSnapshot LoadWallet(DbScope scope);

        // 发放: SQL原子增量。扣费: 条件扣减, 余额不足返回false且余额不变(旧Add*负数会静默clamp到0, 已废除)。
        void GrantGold(DbScope scope, int amount);
        bool TrySpendGold(DbScope scope, int amount);
        void GrantLuckyStar(DbScope scope, int amount);
        bool TrySpendLuckyStar(DbScope scope, int amount);

        CharacterItemListSnapshot LoadSnapshot(DbScope scope);
    }
}
