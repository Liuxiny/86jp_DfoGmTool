using System;
using System.Collections.Generic;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.Skills;
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
            int count,
            ItemGrantOptions options = null)
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
            {
                if (options?.ExpirationDays == null || !IsExpiredExpirationError(expirationError))
                    return Fail(result, expirationError);
                expireTime = 0;
            }

            var manualGrantType = NormalizeManualGrantType(options?.ManualGrantType);
            var requiresManualGrantType = ItemMetadataResolver.RequiresManualGrantType(metadata);
            if (!requiresManualGrantType && manualGrantType != null)
                return Fail(result, "PVF 绫诲瀷宸插彲纭锛屼笉允许运用手动发放分类");
            if (requiresManualGrantType)
            {
                if (manualGrantType == null)
                    return Fail(result, "PVF 鐗╁搧绫诲瀷涓嶆槑纭紝璇峰湪鍙戞斁鍗＄墖涓墜鍔ㄦ寚瀹氬垎绫?");
                if (!IsManualGrantTypeAllowed(metadata, manualGrantType))
                    return Fail(result, "鎵嬪姩鍙戞斁鍒嗙被涓嶉€傜敤浜庤 PVF 鐗╁搧");
            }

            var isAvatar = ItemMetadataResolver.IsAvatarMetadata(metadata)
                || manualGrantType == "avatar";
            var isPetConsumable = ItemMetadataResolver.IsPetConsumableItem(metadata)
                || manualGrantType == "pet-consumable";
            var isPetEquipment = string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal)
                && (ItemMetadataResolver.IsPetInventoryEquipment(itemTemplateId)
                    || manualGrantType == "pet"
                    || manualGrantType == "pet-equipment");
            var isCreature = (isPetEquipment && SqliteInventoryStore.IsCreatureItem(itemTemplateId))
                || manualGrantType == "pet";
            var isPetArtifactEquipment = (isPetEquipment && !isCreature)
                || manualGrantType == "pet-equipment";
            var hasPetArtifactQuality = isPetArtifactEquipment && metadata.SupportsPetEquipmentQuality;

            var listType = InventoryListType.Main;
            var itemKind = metadata.ItemKind;
            var marker16 = metadata.IsStackable ? 0 : -1;
            var durability = metadata.Durability;
            var extraJson = "{}";
            byte optionValue = 0;
            var qualityMode = ItemQualityMode.Top;
            int slotStart;
            int slotEnd;

            if (isAvatar)
            {
                if (!TryResolveAvatarGrant(
                        connection,
                        transaction,
                        characterId,
                        itemTemplateId,
                        options,
                        out optionValue,
                        out expireTime,
                        out var avatarError))
                {
                    return Fail(result, avatarError);
                }

                listType = InventoryListType.Avatar;
                itemKind = "avatar";
                slotStart = 0;
                slotEnd = 500;
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
                if (options?.ExpirationDays != null
                    && !TryResolveExpirationOverride(itemTemplateId, metadata, expireTime, options.ExpirationDays.Value, out expireTime, out var overrideError))
                {
                    return Fail(result, overrideError);
                }

                if (requiresManualGrantType)
                {
                    ResolveManualMainSlotRange(manualGrantType, out slotStart, out slotEnd);
                }
                else
                {
                    metadata.GetSlotRange(out slotStart, out slotEnd);
                }
                if (metadata.IsStackable && expireTime > 0)
                    itemKind = "special";
            }

            if (!isAvatar && string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal))
            {
                var equipmentCapability = EquipmentGrantPolicy.Describe(metadata);
                if (options != null)
                {
                    qualityMode = options.QualityMode;
                    if (!Enum.IsDefined(typeof(ItemQualityMode), qualityMode))
                        return Fail(result, "装备品级选项无效");
                    if (equipmentCapability.CanUpgrade || equipmentCapability.CanAmplify || equipmentCapability.CanForge)
                    {
                        if (!EquipmentGrantPolicy.TryBuildExtraJson(
                                metadata,
                                options,
                                AmplifyInitialValueResolver.Resolve,
                                out extraJson,
                                out var equipmentError))
                            return Fail(result, equipmentError);
                    }
                    else if (options.UpgradeLevel != 0 || options.AmplifyType != 0 || options.ForgingLevel != 0)
                    {
                        return Fail(result, "该物品不支持强化、增幅或锻造属性");
                    }
                }
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
                var carriesQuality = listType != InventoryListType.Avatar
                    && (listType != InventoryListType.Pet || hasPetArtifactQuality);
                var qualitySeed = carriesQuality ? (int)ItemQuality.ResolveSeed(qualityMode) : 0;
                var storedStackCount = carriesQuality ? qualitySeed : 0;
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
                    optionValue,
                    expireTime,
                    marker16,
                    petSerialOrHandle,
                    extraJson);
                AddGrantedSlot(result, listType, (short)targetSlot, 1);
            }

            return CompleteGrant(connection, transaction, characterId, result);
        }

        private static bool TryResolveAvatarGrant(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int itemTemplateId,
            ItemGrantOptions options,
            out byte optionValue,
            out int expireTime,
            out string error)
        {
            optionValue = 0;
            expireTime = 0;
            error = null;
            if (!ItemMetadataResolver.TryLoadEquipmentFile(itemTemplateId, out var equipment))
            {
                error = "装扮模板无法从 PVF 读取";
                return false;
            }
            if (!TryLoadCharacterGrantContext(connection, transaction, characterId, out var job, out var growType, out var level))
            {
                error = "角色不存在";
                return false;
            }
            if (!AvatarGrantPolicy.IsUsableByJob(equipment.UsableJob, job))
            {
                error = "该装扮不适用于当前角色职业";
                return false;
            }

            var avatarMetadata = AvatarEquipmentMetadataReader.Read(equipment);
            var legalOptions = AvatarGrantPolicy.ResolveOptions(
                equipment.EquipmentType,
                equipment.Grade,
                avatarMetadata.SelectAbilities,
                job,
                avatarMetadata.AbilityCaseIndex);
            var requestedOption = options?.AvatarOptionValue ?? 0;
            if (requestedOption < 0 || requestedOption > byte.MaxValue
                || !AvatarGrantPolicy.ContainsValue(legalOptions, requestedOption))
            {
                error = "装扮属性不属于当前模板、品级和职业的合法选项";
                return false;
            }
            optionValue = (byte)requestedOption;

            if (options?.ExpirationDays != null)
            {
                var requestedDays = options.ExpirationDays.Value;
                var durationOptions = AvatarDurationResolver.Resolve(itemTemplateId);
                if (!AvatarDurationResolver.ContainsDuration(durationOptions, requestedDays))
                {
                    error = "装扮期限不属于该模板的 PVF 支持档位";
                    return false;
                }
                if (requestedDays > 0)
                {
                    var value = DateTimeOffset.Now.ToUnixTimeSeconds() + requestedDays * 86400L;
                    if (value > int.MaxValue)
                    {
                        error = "装扮期限超出服务端可存储范围";
                        return false;
                    }
                    expireTime = (int)value;
                }
            }
            return true;
        }

        private static bool TryResolveExpirationOverride(
            int itemTemplateId,
            ItemMetadata metadata,
            int defaultExpireTime,
            int days,
            out int expireTime,
            out string error)
        {
            var expiredFixedExpiration = false;
            if (defaultExpireTime <= 0
                && !metadata.IsStackable
                && string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal)
                && !ItemGrantExpirationResolver.TryResolve(itemTemplateId, metadata, out _, out var resolveError)
                && IsExpiredExpirationError(resolveError))
            {
                expiredFixedExpiration = true;
            }

            var capability = new ItemGrantExpirationCapability
            {
                IsLimited = defaultExpireTime > 0 || expiredFixedExpiration,
                CanOverride = defaultExpireTime > 0 || expiredFixedExpiration,
                DefaultExpireTime = defaultExpireTime,
                IsExpired = expiredFixedExpiration,
            };
            if (metadata?.IsStackable == true
                && StackableExpirationPolicyResolver.TryResolve(metadata.StackableFile, out var policy))
            {
                capability.IsLimited = policy.RequiresInstanceExpiration
                    || policy.AbsoluteExpirationUnixTime > 0
                    || policy.DailyDeleteItem;
                capability.CanOverride = policy.RequiresInstanceExpiration
                    || policy.AbsoluteExpirationUnixTime > 0;
            }
            return ItemGrantExpirationOverride.TryResolve(
                capability,
                days,
                DateTimeOffset.Now.ToUnixTimeSeconds(),
                out expireTime,
                out error);
        }

        private static bool IsExpiredExpirationError(string error)
            => !string.IsNullOrWhiteSpace(error) && error.Contains("已过期");

        private static string NormalizeManualGrantType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return value.Trim().ToLowerInvariant();
        }

        private static bool IsManualGrantTypeAllowed(ItemMetadata metadata, string manualGrantType)
        {
            if (metadata == null || string.IsNullOrWhiteSpace(manualGrantType))
                return false;

            if (string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal))
            {
                return manualGrantType == "equipment"
                    || manualGrantType == "avatar"
                    || manualGrantType == "pet"
                    || manualGrantType == "pet-equipment";
            }

            if (metadata.IsStackable)
            {
                return manualGrantType == "consumable"
                    || manualGrantType == "material"
                    || manualGrantType == "quest"
                    || manualGrantType == "expert-material"
                    || manualGrantType == "avatar-emblem"
                    || manualGrantType == "special-material"
                    || manualGrantType == "pet-consumable";
            }

            return false;
        }

        private static void ResolveManualMainSlotRange(string manualGrantType, out int slotStart, out int slotEnd)
        {
            switch (manualGrantType)
            {
                case "equipment":
                    slotStart = 9;
                    slotEnd = 64;
                    return;
                case "material":
                    slotStart = 121;
                    slotEnd = 176;
                    return;
                case "quest":
                    slotStart = 177;
                    slotEnd = 232;
                    return;
                case "expert-material":
                    slotStart = 233;
                    slotEnd = 288;
                    return;
                case "avatar-emblem":
                    slotStart = 289;
                    slotEnd = 344;
                    return;
                case "special-material":
                    slotStart = 345;
                    slotEnd = 359;
                    return;
                default:
                    slotStart = 65;
                    slotEnd = 120;
                    return;
            }
        }

        private static bool TryLoadCharacterGrantContext(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            out int job,
            out int growType,
            out int level)
        {
            job = 0;
            growType = 0;
            level = 0;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT job, grow_type, level FROM characters WHERE character_id = @cid;";
                command.Parameters.AddWithValue("@cid", characterId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return false;
                    job = reader.GetInt32(0);
                    growType = reader.GetInt32(1);
                    level = reader.GetInt32(2);
                    return true;
                }
            }
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
