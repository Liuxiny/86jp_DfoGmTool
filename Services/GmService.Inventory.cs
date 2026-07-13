using System;
using System.Collections.Generic;
using System.Linq;
using DfoGmTool.ServerCore.Game.TitleBook;
using DfoGmTool.ServerCore.Game.Characters;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.Dungeon;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.Quests;
using DfoGmTool.ServerCore.Game.ReviveCoin;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        // 读侧同样走服务端快照(覆盖全部容器和多态字段语义), 不裸读 character_items
        public object ListItems(int characterId, PvfIndexService pvfIndex)
        {
            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            // 快照加载内部自己开连接, 不能包在 scope 事务里(同库两连接会锁死)
            var snapshot = _store.LoadCharacterItemListSnapshot(characterId, accountId);

            var items = new List<object>();
            AppendCommonItems(items, "主背包", InventoryListType.Main, snapshot.MainItems, pvfIndex);
            AppendCommonItems(items, "个人仓库", InventoryListType.PersonalCargo, snapshot.PersonalCargoItems, pvfIndex);
            AppendCommonItems(items, "账号金库", InventoryListType.AccountCargo, snapshot.AccountCargoItems, pvfIndex);

            foreach (var item in snapshot.EquipmentItems)
            {
                items.Add(new
                {
                    container = "主背包",
                    category = "穿戴装备",
                    listType = (int)InventoryListType.Equipment,
                    slot = (int)item.SlotIndex,
                    templateId = item.AvatarItemId,
                    name = pvfIndex.ResolveItemName(item.AvatarItemId),
                    kind = "equipment",
                    rarity = pvfIndex.ResolveItemRarity(item.AvatarItemId),
                    count = 1,
                    durability = 0,
                    expireTime = item.ExpireTime,
                    templateExpiration = CreateTemplateExpiration(pvfIndex, item.AvatarItemId),
                    deletable = true,
                });
            }

            foreach (var item in snapshot.AvatarItems)
            {
                items.Add(new
                {
                    container = "主背包",
                    category = "时装",
                    listType = (int)InventoryListType.Avatar,
                    slot = (int)item.SlotIndex,
                    templateId = item.AvatarItemId,
                    name = pvfIndex.ResolveItemName(item.AvatarItemId),
                    kind = "avatar",
                    rarity = pvfIndex.ResolveItemRarity(item.AvatarItemId),
                    count = 1,
                    durability = 0,
                    expireTime = item.ExpireTime,
                    templateExpiration = CreateTemplateExpiration(pvfIndex, item.AvatarItemId),
                    deletable = true,
                });
            }

            foreach (var item in snapshot.PetItems)
            {
                items.Add(new
                {
                    container = "宠物",
                    category = ResolvePetSegment(item.SlotIndex),
                    listType = (int)InventoryListType.Pet,
                    slot = (int)item.SlotIndex,
                    templateId = item.CreatureItemId,
                    name = pvfIndex.ResolveItemName(item.CreatureItemId),
                    kind = "pet",
                    rarity = pvfIndex.ResolveItemRarity(item.CreatureItemId),
                    count = 1,
                    durability = 0,
                    serial = item.CreatureSerialOrHandle,
                    expireTime = item.ExpireTime,
                    templateExpiration = CreateTemplateExpiration(pvfIndex, item.CreatureItemId),
                    deletable = true,
                });
            }

            return new { characterId, count = items.Count, items };
        }

        private static void AppendCommonItems(List<object> items, string container, InventoryListType listType,
            List<CommonInventoryItem> source, PvfIndexService pvfIndex)
        {
            var isMainList = listType == InventoryListType.Main;
            foreach (var item in source)
            {
                var kind = pvfIndex.ResolveItemKind(item.ItemTemplateId);
                items.Add(new
                {
                    container,
                    category = isMainList ? ResolveMainSegment(item.SlotIndex) : container,
                    listType = (int)listType,
                    slot = (int)item.SlotIndex,
                    templateId = item.ItemTemplateId,
                    name = pvfIndex.ResolveItemName(item.ItemTemplateId),
                    kind,
                    rarity = pvfIndex.ResolveItemRarity(item.ItemTemplateId),
                    // CountOrInstanceValue 对装备是实例值(品质种子), 对堆叠物是数量
                    count = kind == "equipment" ? 1 : item.CountOrInstanceValue,
                    instanceValue = item.CountOrInstanceValue,
                    durability = (int)item.Durability,
                    expireTime = item.ExpireTime,
                    templateExpiration = CreateTemplateExpiration(pvfIndex, item.ItemTemplateId),
                    seal = (int)item.SealFlag,
                    deletable = IsDeletable(listType, item.SlotIndex),
                });
            }
        }

        // 货币行(主背包 slot 0-2)删行会打坏钱包; 晶块(354-359)和账号金库是账号共享, 在账号面板管理
        private static object CreateTemplateExpiration(PvfIndexService pvfIndex, int itemTemplateId)
        {
            var expiration = pvfIndex.ResolveItemExpiration(itemTemplateId);
            return new
            {
                known = expiration.IsKnown,
                absoluteExpireTime = expiration.AbsoluteExpirationUnixTime,
                usablePeriodDays = expiration.UsablePeriodDays,
                invalid = expiration.HasInvalidDefinition,
            };
        }

        private static bool IsDeletable(InventoryListType listType, int slot)
        {
            if (listType == InventoryListType.AccountCargo)
                return false;
            if (listType == InventoryListType.Main && slot <= 2)
                return false;
            if (listType == InventoryListType.Main && CurrencyService.IsCubeFragmentSlot(slot))
                return false;
            return true;
        }

        // 走服务端 DELETE_ITEM 同款入口(TryDeleteItem): 按列表+槽位精确删除,
        // 排列锁清理/魔方碎片/整删部分删的语义都由服务端代码处理
        public object DeleteItemAt(int characterId, int listType, int slot, int count)
        {
            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            var list = (InventoryListType)listType;
            if (!IsDeletable(list, slot))
                return Error("该槽位不允许删除(货币行或账号金库)");

            InventoryMutationResult result;
            if (!_store.TryDeleteItem(characterId, accountId, list, (short)slot, (short)count, out result))
                return Error("删除失败(槽位为空或该列表不支持删除)");

            return new
            {
                success = true,
                characterId,
                listType,
                slot,
                remaining = result != null ? result.RemainingStackCount : 0,
            };
        }

        public object BatchDeleteItems(int characterId, List<BatchDeleteEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return Error("没有要删除的条目");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            var deleted = 0;
            var failed = new List<object>();
            foreach (var entry in entries)
            {
                var list = (InventoryListType)entry.ListType;
                if (!IsDeletable(list, entry.Slot))
                {
                    failed.Add(new { entry.ListType, entry.Slot, reason = "受保护槽位" });
                    continue;
                }

                InventoryMutationResult result;
                if (_store.TryDeleteItem(characterId, accountId, list, (short)entry.Slot, 0, out result))
                    deleted++;
                else
                    failed.Add(new { entry.ListType, entry.Slot, reason = "删除失败" });
            }

            return new { success = true, characterId, deleted, failedCount = failed.Count, failed };
        }

        // 主背包 slot 分段, 与服务端 ItemMetadataResolver.GetSlotRange / 各 Slot 常量一致
        private static string ResolveMainSegment(int slot)
        {
            if (slot <= 2) return "货币";        // 0金币 1复活币 2技能点
            if (slot <= 8) return "快捷栏";      // QuickSlot 3-8
            if (slot <= 64) return "装备";       // 9-64 (含租赁)
            if (slot <= 120) return "消耗品";    // 65-120
            if (slot <= 176) return "材料";      // 121-176
            if (slot <= 232) return "任务品";    // 177-232
            if (slot <= 288) return "副职业材料"; // 233-288
            if (slot <= 344) return "徽章";      // 289-344
            if (slot <= 353) return "特殊材料";   // 345-353
            if (slot <= 359) return "账号晶块";   // 354-359 账号共享(accounts表列), 在账号面板调整
            return "其他";
        }

        private static string ResolvePetSegment(int slot)
        {
            if (slot <= 139) return "宠物";       // 0-139
            if (slot <= 188) return "宠物装备";    // 140-188
            return "宠物用品";                    // 189-237
        }

        public object GiveItem(int characterId, int itemTemplateId, int count, PvfIndexService pvfIndex)
        {
            if (itemTemplateId <= 0)
                return Error("itemTemplateId 无效");
            if (count <= 0)
                return Error("数量必须大于 0");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            // 名字解析不到通常意味着 ID 不存在, 直接发下去客户端会异常, 先拦住
            var name = pvfIndex.ResolveItemName(itemTemplateId);
            if (name == null && pvfIndex.IsReady)
                return Error("物品 ID " + itemTemplateId + " 在 PVF 中不存在(装备/堆叠表都没有)");

            using (var scope = _assetService.OpenScope(characterId, accountId))
            {
                // 账号/钱包类特殊资产沿用既有入口，不进入角色实例期限发放。
                if (CurrencyService.IsCubeFragment(itemTemplateId)
                    || ReviveCoinService.IsReviveCoinReward(itemTemplateId))
                {
                    if (!_assetService.TryAddItem(scope, itemTemplateId, count, out var legacySlot))
                        return Error("发放失败(背包可能已满)");

                    scope.Commit();
                    return new { success = true, characterId, itemTemplateId, name, count, slot = (int)legacySlot };
                }

                var grant = _assetService.TryGrantCharacterItem(scope, itemTemplateId, count);
                if (!grant.Success)
                    return Error(grant.Error ?? "发放失败(背包可能已满)");

                scope.Commit();
                return new
                {
                    success = true,
                    characterId,
                    itemTemplateId,
                    name,
                    count = grant.GrantedCount,
                    slot = (int)grant.AssignedSlot,
                    expireTime = grant.ExpireTime,
                    slots = grant.AffectedSlots,
                };
            }
        }

        public object RemoveItem(int characterId, int itemTemplateId, int count)
        {
            if (count <= 0)
                count = 1;

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            using (var scope = _assetService.OpenScope(characterId, accountId))
            {
                short slot;
                int remaining;
                if (!_assetService.TryRemoveItem(scope, itemTemplateId, count, out slot, out remaining))
                    return Error("移除失败(角色没有该物品或数量不足)");

                scope.Commit();
                return new { success = true, characterId, itemTemplateId, count, slot = (int)slot, remaining };
            }
        }

        public object AdjustGold(int characterId, int amount)
        {
            if (amount == 0)
                return Error("amount 不能为 0");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            using (var scope = _assetService.OpenScope(characterId, accountId))
            {
                if (amount > 0)
                {
                    _assetService.GrantGold(scope, amount);
                }
                else if (!_assetService.TrySpendGold(scope, -amount))
                {
                    return Error("扣款失败(金币不足)");
                }

                scope.Commit();
            }

            using (var scope = _assetService.OpenScope(characterId, accountId))
            {
                var wallet = _assetService.LoadWallet(scope);
                return new { success = true, characterId, amount, gold = wallet.Gold };
            }
        }

        // 三种角色货币覆写: 金币走 CurrencyService 按差额加扣;
        // 复活币(slot1)/技能点(slot2)是普通计数行, 按服务端 UpdateStackCount 同语义直写
        public object SetWalletValue(int characterId, string type, int value)
        {
            if (value < 0)
                return Error("数值不能为负");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            type = (type ?? string.Empty).Trim().ToLowerInvariant();

            if (type == "gold")
            {
                using (var scope = _assetService.OpenScope(characterId, accountId))
                {
                    var wallet = _assetService.LoadWallet(scope);
                    var delta = value - wallet.Gold;
                    if (delta > 0)
                        _assetService.GrantGold(scope, delta);
                    else if (delta < 0 && !_assetService.TrySpendGold(scope, -delta))
                        return Error("扣减失败");
                    scope.Commit();
                }
                return new { success = true, characterId, type, value };
            }

            int slot;
            switch (type)
            {
                case "revive": slot = 1; break;
                case "sp": slot = 2; break;
                default: return Error("不支持的类型: " + type + " (可用: gold/revive/sp)");
            }

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
UPDATE character_items
SET stack_count = @v, instance_value = @v, updated_at = CURRENT_TIMESTAMP
WHERE character_id = @cid AND list_type = 0 AND slot_index = @slot;";
                    cmd.Parameters.AddWithValue("@v", value);
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    cmd.Parameters.AddWithValue("@slot", slot);
                    if (cmd.ExecuteNonQuery() == 0)
                        return Error("货币行不存在(slot " + slot + ")");
                }
            }
            return new { success = true, characterId, type, value };
        }

        // 点券是账号级余额, 服务端接口按角色定位账号
        public object AdjustCera(int characterId, int amount, string type)
        {
            if (amount == 0)
                return Error("amount 不能为 0");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            var useToken = string.Equals(type, "token", StringComparison.OrdinalIgnoreCase);
            using (var scope = _assetService.OpenScope(characterId, accountId))
            {
                if (amount > 0)
                {
                    if (useToken)
                        CurrencyService.GrantTokenCera(scope.Connection, scope.Transaction, characterId, amount);
                    else
                        CurrencyService.GrantCera(scope.Connection, scope.Transaction, characterId, amount);
                }
                else
                {
                    var ok = useToken
                        ? CurrencyService.TrySpendTokenCera(scope.Connection, scope.Transaction, characterId, -amount)
                        : CurrencyService.TrySpendCera(scope.Connection, scope.Transaction, characterId, -amount);
                    if (!ok)
                        return Error("扣减失败(余额不足)");
                }

                scope.Commit();
            }

            using (var scope = _assetService.OpenScope(characterId, accountId))
            {
                var wallet = _assetService.LoadWallet(scope);
                return new { success = true, characterId, accountId, amount, cera = wallet.Cera, tokenCera = wallet.TokenCera };
            }
        }
    }

    public sealed class BatchDeleteEntry
    {
        public int ListType { get; set; }
        public int Slot { get; set; }
    }
}
