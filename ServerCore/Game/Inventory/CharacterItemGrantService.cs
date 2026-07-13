using System;
using DfoGmTool.ServerCore.Game.Currency;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal sealed class CharacterItemGrantService
    {
        private readonly InventoryDbPrimitives _db;
        private readonly InventoryAuditLogger _auditLogger;

        internal CharacterItemGrantService(InventoryDbPrimitives db, InventoryAuditLogger auditLogger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        }

        internal ItemGrantResult TryGrant(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int itemTemplateId,
            int count)
        {
            var result = new ItemGrantResult
            {
                ItemTemplateId = itemTemplateId,
                RequestedCount = count,
                ListType = InventoryListType.Main,
            };

            if (count <= 0)
                return Fail(result, "数量必须大于 0");

            if (CurrencyService.IsCubeFragment(itemTemplateId)
                || Game.ReviveCoin.ReviveCoinService.IsReviveCoinReward(itemTemplateId))
            {
                return Fail(result, "该特殊资产不属于角色物品发放");
            }

            var metadata = ItemMetadataResolver.Resolve(itemTemplateId);
            if (metadata.ItemKind == "special")
                return Fail(result, "该物品不支持直接发放");
            if (IsAccountContract(metadata))
                return Fail(result, "账号契约不支持通过角色物品发放");

            var isCharm = IsCharmItem(itemTemplateId);
            if (isCharm)
            {
                if (count != 1)
                    return Fail(result, "护石一次只能发放 1 个");
                if (!CanAddCharmToMain(connection, transaction, characterId))
                    return Fail(result, "背包中已有护石，不能再次发放");
            }

            if (!ItemGrantExpirationResolver.TryResolve(itemTemplateId, metadata, out var expireTime, out var expirationError))
                return Fail(result, expirationError);

            var isAvatar = IsAvatarReward(metadata);
            var isPetConsumable = ItemMetadataResolver.IsPetConsumableItem(metadata);
            var isPetEquipment = string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal)
                && ItemMetadataResolver.IsPetInventoryEquipment(itemTemplateId);
            var isCreature = isPetEquipment && SqliteInventoryStore.IsCreatureItem(itemTemplateId);
            var isPetArtifactEquipment = isPetEquipment && !isCreature;

            var listType = InventoryListType.Main;
            var itemKind = metadata.ItemKind;
            var marker16 = metadata.IsStackable ? 0 : -1;
            var durability = metadata.Durability;
            var extraJson = "{}";
            int slotStart;
            int slotEnd;

            if (isAvatar)
            {
                listType = InventoryListType.Avatar;
                itemKind = "avatar";
                slotStart = 0;
                slotEnd = 500;
                expireTime = 0;
                marker16 = SqliteInventoryStore.DefaultAvatarUnknownFixed30;
                durability = 0;
                extraJson = SqliteInventoryStore.CreateDefaultAvatarExtraJson();
            }
            else if (isCreature)
            {
                listType = InventoryListType.Pet;
                itemKind = "pet";
                slotStart = SqliteInventoryStore.PetInventorySlotStart;
                slotEnd = SqliteInventoryStore.PetInventorySlotEnd;
                expireTime = 0;
                marker16 = 0;
                durability = 0;
            }
            else if (isPetArtifactEquipment)
            {
                listType = InventoryListType.Pet;
                itemKind = "pet";
                slotStart = SqliteInventoryStore.PetEquipmentSlotStart;
                slotEnd = SqliteInventoryStore.PetEquipmentSlotEnd;
                expireTime = 0;
                marker16 = 0;
                durability = 0;
            }
            else if (isPetConsumable)
            {
                listType = InventoryListType.Pet;
                itemKind = "pet";
                slotStart = SqliteInventoryStore.PetConsumableSlotStart;
                slotEnd = SqliteInventoryStore.PetConsumableSlotEnd;
                expireTime = 0;
                marker16 = 0;
                durability = 0;
            }
            else
            {
                metadata.GetSlotRange(out slotStart, out slotEnd);
                if (metadata.IsStackable && expireTime > 0)
                    itemKind = "special";
            }

            result.ExpireTime = expireTime;
            if (metadata.IsStackable && !isAvatar)
            {
                if (!TryGrantStackable(
                        connection,
                        transaction,
                        characterId,
                        itemTemplateId,
                        count,
                        listType,
                        itemKind,
                        slotStart,
                        slotEnd,
                        metadata.StackLimit,
                        expireTime,
                        isPetConsumable,
                        durability,
                        result,
                        out var stackError))
                {
                    return Fail(result, stackError);
                }

                return CompleteGrant(connection, transaction, characterId, result);
            }

            for (var rowIndex = 0; rowIndex < count; rowIndex++)
            {
                var targetSlot = _db.FindEmptySlot(connection, transaction, characterId, listType, slotStart, slotEnd);
                if (targetSlot < 0)
                    return Fail(result, "目标背包空间不足");

                var petSerialOrHandle = isCreature
                    ? _db.NextPetSerialOrHandle(connection, transaction, characterId)
                    : 0;
                var qualitySeed = listType == InventoryListType.Pet || listType == InventoryListType.Avatar
                    ? 0
                    : InventoryDbPrimitives.GenerateInstanceValue(itemTemplateId, targetSlot);
                var storedStackCount = listType == InventoryListType.Pet || listType == InventoryListType.Avatar
                    ? 0
                    : qualitySeed;
                var sealFlag = metadata.IsSealed ? (byte)1 : (byte)0;

                _db.InsertCharacterItem(
                    connection,
                    transaction,
                    characterId,
                    listType,
                    (short)targetSlot,
                    itemTemplateId,
                    itemKind,
                    storedStackCount,
                    qualitySeed,
                    durability,
                    sealFlag,
                    0,
                    expireTime,
                    marker16,
                    petSerialOrHandle,
                    extraJson);
                AddGrantedSlot(result, listType, (short)targetSlot, 1);
            }

            return CompleteGrant(connection, transaction, characterId, result);
        }

        private bool TryGrantStackable(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int itemTemplateId,
            int count,
            InventoryListType listType,
            string itemKind,
            int slotStart,
            int slotEnd,
            int stackLimit,
            int expireTime,
            bool isPetConsumable,
            ushort durability,
            ItemGrantResult result,
            out string error)
        {
            error = null;
            var remaining = count;
            if (listType == InventoryListType.Main)
            {
                remaining = FillExistingStacks(
                    connection,
                    transaction,
                    characterId,
                    itemTemplateId,
                    listType,
                    SqliteInventoryStore.QuickSlotStart,
                    SqliteInventoryStore.QuickSlotEnd,
                    stackLimit,
                    expireTime,
                    false,
                    remaining,
                    result);
            }

            remaining = FillExistingStacks(
                connection,
                transaction,
                characterId,
                itemTemplateId,
                listType,
                slotStart,
                slotEnd,
                stackLimit,
                expireTime,
                isPetConsumable,
                remaining,
                result);

            var maxPerStack = stackLimit > 0 ? stackLimit : int.MaxValue;
            while (remaining > 0)
            {
                var targetSlot = _db.FindEmptySlot(connection, transaction, characterId, listType, slotStart, slotEnd);
                if (targetSlot < 0)
                {
                    error = "目标背包空间不足";
                    return false;
                }

                var insertCount = Math.Min(maxPerStack, remaining);
                _db.InsertCharacterItem(
                    connection,
                    transaction,
                    characterId,
                    listType,
                    (short)targetSlot,
                    itemTemplateId,
                    itemKind,
                    insertCount,
                    insertCount,
                    isPetConsumable ? (ushort)0 : durability,
                    0,
                    0,
                    expireTime,
                    0,
                    isPetConsumable ? insertCount : 0,
                    "{}");
                AddGrantedSlot(result, listType, (short)targetSlot, insertCount);
                remaining -= insertCount;
            }

            return true;
        }

        private int FillExistingStacks(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int itemTemplateId,
            InventoryListType listType,
            int slotStart,
            int slotEnd,
            int stackLimit,
            int expireTime,
            bool isPetConsumable,
            int remaining,
            ItemGrantResult result)
        {
            var maxPerStack = stackLimit > 0 ? stackLimit : int.MaxValue;
            while (remaining > 0)
            {
                var existing = _db.FindStackableItemByTemplateIdAndExpireTime(
                    connection,
                    transaction,
                    characterId,
                    listType,
                    itemTemplateId,
                    expireTime,
                    stackLimit,
                    slotStart,
                    slotEnd);
                if (existing == null || existing.StackCount < 0 || existing.StackCount >= maxPerStack)
                    break;

                var capacity = maxPerStack - existing.StackCount;
                var addCount = Math.Min(remaining, capacity);
                if (addCount <= 0)
                    break;

                var newStackCount = existing.StackCount + addCount;
                if (isPetConsumable)
                    _db.UpdatePetStackCount(connection, transaction, existing.ItemUid, newStackCount);
                else
                    _db.UpdateStackCount(connection, transaction, existing.ItemUid, newStackCount);

                AddGrantedSlot(result, listType, existing.SlotIndex, addCount);
                remaining -= addCount;
            }

            return remaining;
        }

        private static bool IsAvatarReward(ItemMetadata metadata)
        {
            var path = metadata?.PvfFilePath;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalizedPath = "/" + path.Replace('\\', '/').Trim('/');
            return normalizedPath.IndexOf("/avatar/", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedPath.IndexOf("/at_avatar/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsAccountContract(ItemMetadata metadata)
        {
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.StackableType))
                return false;

            var stackableType = metadata.StackableType.Replace("`", "").Trim();
            return stackableType.StartsWith("[contract]", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCharmItem(int itemTemplateId)
        {
            return string.Equals(
                ItemMetadataResolver.ResolveEquipmentType(itemTemplateId),
                "[charm]",
                StringComparison.OrdinalIgnoreCase);
        }

        private bool CanAddCharmToMain(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            foreach (var itemTemplateId in _db.LoadCharacterItemTemplateIds(
                         connection,
                         transaction,
                         characterId,
                         InventoryListType.Main))
            {
                if (IsCharmItem(itemTemplateId))
                    return false;
            }

            return true;
        }

        private ItemGrantResult CompleteGrant(SqliteConnection connection, SqliteTransaction transaction, int characterId, ItemGrantResult result)
        {
            if (result.GrantedCount <= 0 || result.AssignedSlot < 0)
                return Fail(result, "未生成有效物品实例");

            result.Success = true;
            _auditLogger.WriteGmGrantAuditLog(connection, transaction, characterId, result);
            return result;
        }

        private static void AddGrantedSlot(ItemGrantResult result, InventoryListType listType, short slotIndex, int grantedCount)
        {
            if (result.AssignedSlot < 0)
            {
                result.ListType = listType;
                result.AssignedSlot = slotIndex;
            }

            if (!result.AffectedSlots.Contains(slotIndex))
                result.AffectedSlots.Add(slotIndex);
            result.GrantedCount += grantedCount;
        }

        private static ItemGrantResult Fail(ItemGrantResult result, string error)
        {
            result.Success = false;
            result.Error = error;
            result.GrantedCount = 0;
            result.AssignedSlot = -1;
            result.AffectedSlots.Clear();
            return result;
        }
    }
}
