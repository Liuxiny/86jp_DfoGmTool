// GM瘦身拷贝: 相对服务端原版仅保留 LoadCharacterItemListSnapshot 与 TryDeleteItem 两个成员
// (与瘦身后的 SqliteInventoryStore 实现面一致); 其余接口成员全部删除; 保留成员与原版逐字一致
using DfoGmTool.ServerCore.Game.SelectCharacter;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public interface IInventoryStore
    {
        CharacterItemListSnapshot LoadCharacterItemListSnapshot(int characterId, int accountId);

        bool TryDeleteItem(int characterId, int accountId, InventoryListType listType, short slotIndex, short deleteCount, out InventoryMutationResult result);
    }
}
