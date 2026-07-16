using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Infrastructure;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed class SqliteAssetService : IAssetService
    {
        private readonly string _connectionString;
        private readonly SqliteInventoryStore _store;

        public SqliteAssetService(string databasePath, string schemaFilePath, SqliteInventoryStore store = null)
        {
            if (databasePath == null) throw new ArgumentNullException(nameof(databasePath));
            if (schemaFilePath == null) throw new ArgumentNullException(nameof(schemaFilePath));

            _connectionString = SqliteDatabaseBootstrap.BuildConnectionString(databasePath);
            _store = store ?? new SqliteInventoryStore(databasePath, schemaFilePath);
        }

        public DbScope OpenScope(int characterId, int accountId)
        {
            return new DbScope(_connectionString, characterId, accountId);
        }

        public bool TryAddItem(DbScope scope, int itemTemplateId, int count, out short assignedSlot)
        {
            assignedSlot = -1;
            var storage = AssetRouter.Classify(itemTemplateId);

            switch (storage)
            {
                case AssetStorage.AccountCubeFragment:
                    CurrencyService.AddCubeFragment(scope.Connection, scope.Transaction, scope.AccountId, itemTemplateId, count);
                    assignedSlot = (short)CurrencyService.GetCubeFragmentSlot(itemTemplateId);
                    return true;

                case AssetStorage.CharacterItem:
                    return _store.TryPickupItemCore(
                        scope.Connection, scope.Transaction,
                        scope.CharacterId, scope.AccountId,
                        itemTemplateId, count, out assignedSlot);

                default:
                    return false;
            }
        }

        public ItemGrantResult TryGrantCharacterItem(DbScope scope, int itemTemplateId, int count, ItemGrantOptions options = null)
        {
            return _store.CharacterItemGrants.TryGrant(
                scope.Connection,
                scope.Transaction,
                scope.CharacterId,
                itemTemplateId,
                count,
                options);
        }

        public bool TryRemoveItem(DbScope scope, int itemTemplateId, int count, out short slot, out int remaining)
        {
            slot = -1;
            remaining = 0;

            var result = InventoryDbPrimitives.RemoveItemByTemplateId(
                scope.Connection, scope.Transaction,
                scope.CharacterId, itemTemplateId, count);

            if (result == null)
                return false;

            slot = result.Value.SlotIndex;
            remaining = result.Value.RemainingCount;
            return true;
        }

        public int CountItem(DbScope scope, int itemTemplateId)
        {
            if (CurrencyService.IsCubeFragment(itemTemplateId))
            {
                var cubes = CurrencyService.LoadCubeFragments(scope.Connection, scope.Transaction, scope.AccountId);
                foreach (var cube in cubes)
                {
                    if (cube.ItemId == itemTemplateId)
                        return cube.Count;
                }
                return 0;
            }

            using (var cmd = scope.Connection.CreateCommand())
            {
                cmd.Transaction = scope.Transaction;
                cmd.CommandText = "SELECT stack_count FROM character_items WHERE character_id = @cid AND list_type = 0 AND item_template_id = @tid LIMIT 1;";
                cmd.Parameters.AddWithValue("@cid", scope.CharacterId);
                cmd.Parameters.AddWithValue("@tid", itemTemplateId);
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    return Convert.ToInt32(result);
                return 0;
            }
        }

        public WalletSnapshot LoadWallet(DbScope scope)
        {
            return CurrencyService.LoadWallet(scope.Connection, scope.Transaction, scope.CharacterId);
        }

        public void GrantGold(DbScope scope, int amount)
        {
            CurrencyService.GrantGold(scope.Connection, scope.Transaction, scope.CharacterId, amount);
        }

        public bool TrySpendGold(DbScope scope, int amount)
        {
            return CurrencyService.TrySpendGold(scope.Connection, scope.Transaction, scope.CharacterId, amount);
        }

        public void GrantLuckyStar(DbScope scope, int amount)
        {
            CurrencyService.GrantLuckyStar(scope.Connection, scope.Transaction, scope.AccountId, amount);
        }

        public bool TrySpendLuckyStar(DbScope scope, int amount)
        {
            return CurrencyService.TrySpendLuckyStar(scope.Connection, scope.Transaction, scope.AccountId, amount);
        }

        public CharacterItemListSnapshot LoadSnapshot(DbScope scope)
        {
            return _store.LoadCharacterItemListSnapshot(scope.CharacterId, scope.AccountId);
        }
    }
}
