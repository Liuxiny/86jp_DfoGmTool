using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.ItemUpgrade;
using DfoGmTool.ServerCore.Game.Skills;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        public object GetInventoryItemConfigOptions(int characterId, int listType, int slot, PvfIndexService pvfIndex)
        {
            if (!TryLoadGrantCharacter(characterId, out var job, out _, out _))
                return Error("角色不存在: " + characterId);

            var list = (InventoryListType)listType;
            if (!TryLoadInventoryItemRecord(characterId, list, slot, out var record))
                return Error("目标槽位没有可配置物品");

            return BuildInventoryItemConfigOptions(record, list, job, pvfIndex, failWhenUnsupported: true);
        }

        public object ConfigureInventoryItem(int characterId, InventoryItemConfigureRequest request, PvfIndexService pvfIndex)
        {
            if (request == null)
                return Error("请求为空");
            if (!TryLoadGrantCharacter(characterId, out var job, out _, out _))
                return Error("角色不存在: " + characterId);

            var list = (InventoryListType)request.ListType;
            using (var connection = new SqliteConnection(_config.ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    if (!TryLoadInventoryItemRecord(connection, transaction, characterId, list, request.Slot, out var record))
                        return Error("目标槽位没有可配置物品");

                    var metadata = ItemMetadataResolver.Resolve(record.ItemTemplateId);
                    if (metadata == null || metadata.ItemKind == "special")
                        return Error("物品模板不存在，无法配置");

                    var options = request.Options ?? new ItemGrantOptions();
                    var wantsExpiration = options.ExpirationDays != null;
                    int? expireTime = null;
                    if (wantsExpiration)
                    {
                        if (!TryResolveInventoryExpirationOverride(record, metadata, options.ExpirationDays.Value, out var resolvedExpireTime, out var expirationError))
                            return Error(expirationError);
                        expireTime = resolvedExpireTime;
                    }

                    if (string.Equals(record.ItemKind, "avatar", StringComparison.Ordinal))
                    {
                        byte? requested = null;
                        if (options.AvatarOptionValue != null)
                        {
                            if (!TryBuildInventoryAvatarOptions(record.ItemTemplateId, job, out var avatarOptions, out var avatarError))
                                return Error(avatarError ?? "该时装没有可配置属性");

                            var requestedRaw = options.AvatarOptionValue.Value;
                            if (requestedRaw < 0 || requestedRaw > byte.MaxValue
                                || !AvatarGrantPolicy.ContainsValue(avatarOptions, requestedRaw))
                            {
                                return Error("装扮属性不属于当前模板、品级和职业的合法选项");
                            }
                            requested = (byte)requestedRaw;
                        }

                        if (requested == null && expireTime == null)
                            return Error("该时装没有可保存的配置项");

                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            if (expireTime != null && requested != null)
                            {
                                command.CommandText = @"
UPDATE character_items
SET option_value = @optionValue,
    expire_time = @expireTime,
    updated_at = CURRENT_TIMESTAMP
WHERE item_uid = @itemUid;";
                                command.Parameters.AddWithValue("@optionValue", requested.Value);
                                command.Parameters.AddWithValue("@expireTime", expireTime.Value);
                            }
                            else if (expireTime != null)
                            {
                                command.CommandText = @"
UPDATE character_items
SET expire_time = @expireTime,
    updated_at = CURRENT_TIMESTAMP
WHERE item_uid = @itemUid;";
                                command.Parameters.AddWithValue("@expireTime", expireTime.Value);
                            }
                            else
                            {
                                command.CommandText = @"
UPDATE character_items
SET option_value = @optionValue,
    updated_at = CURRENT_TIMESTAMP
WHERE item_uid = @itemUid;";
                                command.Parameters.AddWithValue("@optionValue", requested.Value);
                            }
                            command.Parameters.AddWithValue("@itemUid", record.ItemUid);
                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        return new { success = true, characterId, listType = request.ListType, slot = request.Slot, type = "avatar", optionValue = requested, expireTime };
                    }

                    if (!IsInventoryConfigurableEquipment(record.ItemTemplateId, record.ItemKind, list, pvfIndex, out var capability))
                    {
                        if (expireTime == null)
                            return Error("该装备类型没有可配置属性");

                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = @"
UPDATE character_items
SET expire_time = @expireTime,
    updated_at = CURRENT_TIMESTAMP
WHERE item_uid = @itemUid;";
                            command.Parameters.AddWithValue("@expireTime", expireTime.Value);
                            command.Parameters.AddWithValue("@itemUid", record.ItemUid);
                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        return new { success = true, characterId, listType = request.ListType, slot = request.Slot, type = "expiration", expireTime };
                    }

                    var view = ItemExtraView.Parse(record.ExtraJson);
                    var builder = ItemExtraViewBuilder.FromView(view);
                    if (!EquipmentGrantPolicy.TryApplyToBuilder(
                            metadata,
                            options,
                            AmplifyInitialValueResolver.Resolve,
                            builder.Equipment,
                            out var error))
                    {
                        return Error(error);
                    }

                    var seed = (int)ItemQuality.ResolveSeed(options.QualityMode);
                    var extraJson = MergeKnownEquipmentExtraJson(record.ExtraJson, builder.Build().Serialize());
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        if (expireTime != null)
                        {
                            command.CommandText = @"
UPDATE character_items
SET stack_count = @seed,
    instance_value = @seed,
    extra_json = @extraJson,
    expire_time = @expireTime,
    updated_at = CURRENT_TIMESTAMP
WHERE item_uid = @itemUid;";
                            command.Parameters.AddWithValue("@expireTime", expireTime.Value);
                        }
                        else
                        {
                            command.CommandText = @"
UPDATE character_items
SET stack_count = @seed,
    instance_value = @seed,
    extra_json = @extraJson,
    updated_at = CURRENT_TIMESTAMP
WHERE item_uid = @itemUid;";
                        }
                        command.Parameters.AddWithValue("@seed", seed);
                        command.Parameters.AddWithValue("@extraJson", extraJson);
                        command.Parameters.AddWithValue("@itemUid", record.ItemUid);
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    return new
                    {
                        success = true,
                        characterId,
                        listType = request.ListType,
                        slot = request.Slot,
                        type = "equipment",
                        qualitySeed = seed,
                        upgradeLevel = options.UpgradeLevel,
                        amplifyType = options.AmplifyType,
                        forgingLevel = options.ForgingLevel,
                        expireTime,
                        canUpgrade = capability.CanUpgrade,
                        canAmplify = capability.CanAmplify,
                        canForge = capability.CanForge,
                    };
                }
            }
        }

        private object BuildInventoryItemConfigOptions(
            SqliteInventoryStore.ItemRecord record,
            InventoryListType list,
            int job,
            PvfIndexService pvfIndex,
            bool failWhenUnsupported)
        {
            var name = pvfIndex.ResolveItemName(record.ItemTemplateId);
            var expiration = BuildInventoryExpirationConfig(record);
            if (string.Equals(record.ItemKind, "avatar", StringComparison.Ordinal))
            {
                if (!TryBuildInventoryAvatarOptions(record.ItemTemplateId, job, out var options, out var error))
                {
                    if (expiration != null)
                    {
                        return new
                        {
                            success = true,
                            type = "expiration",
                            itemTemplateId = record.ItemTemplateId,
                            name,
                            listType = (int)list,
                            slot = (int)record.SlotIndex,
                            expiration,
                        };
                    }
                    return failWhenUnsupported
                        ? Error(error ?? "该时装没有可配置属性")
                        : null;
                }

                if (!ItemMetadataResolver.TryLoadEquipmentFile(record.ItemTemplateId, out var equipment))
                    return Error("装扮模板无法从 PVF 读取");
                var selected = AvatarGrantPolicy.ContainsValue(options, record.OptionValue)
                    ? (int)record.OptionValue
                    : options[0].Value;

                return new
                {
                    success = true,
                    type = "avatar",
                    itemTemplateId = record.ItemTemplateId,
                    name,
                    listType = (int)list,
                    slot = (int)record.SlotIndex,
                    avatar = new
                    {
                        part = equipment.EquipmentType,
                        grade = equipment.Grade,
                        currentOptionValue = selected,
                        options = options.Select(value => new
                        {
                            value = value.Value,
                            label = value.Label,
                            isSkill = value.IsSkill,
                        }).ToArray(),
                    },
                    expiration,
                };
            }

            if (!IsInventoryConfigurableEquipment(record.ItemTemplateId, record.ItemKind, list, pvfIndex, out var capability))
            {
                if (expiration != null)
                {
                    return new
                    {
                        success = true,
                        type = "expiration",
                        itemTemplateId = record.ItemTemplateId,
                        name,
                        listType = (int)list,
                        slot = (int)record.SlotIndex,
                        expiration,
                    };
                }
                return failWhenUnsupported
                    ? Error("该装备类型没有可配置属性")
                    : null;
            }

            var metadata = ItemMetadataResolver.Resolve(record.ItemTemplateId);
            var extra = ItemExtraView.Parse(record.ExtraJson).Equipment;
            var currentAmplifyType = extra.AmplifyType >= 0 && extra.AmplifyType <= 4
                ? extra.AmplifyType
                : 0;

            return new
            {
                success = true,
                type = "equipment",
                itemTemplateId = record.ItemTemplateId,
                name,
                listType = (int)list,
                slot = (int)record.SlotIndex,
                equipment = new
                {
                    type = metadata.EquipmentType,
                    rarity = metadata.Rarity,
                    minimumLevel = metadata.MinimumLevel,
                    canUpgrade = capability.CanUpgrade,
                    canAmplify = capability.CanAmplify,
                    canForge = capability.CanForge,
                    maxUpgradeLevel = capability.MaxUpgradeLevel,
                    maxForgingLevel = capability.MaxForgingLevel,
                    currentQualityMode = record.InstanceValue == (int)ItemQuality.TopQualitySeed
                        ? (int)ItemQualityMode.Top
                        : (int)ItemQualityMode.Random,
                    currentQualitySeed = record.InstanceValue,
                    currentUpgradeLevel = (int)extra.Upgrade,
                    currentAmplifyType = (int)currentAmplifyType,
                    currentForgingLevel = (int)extra.Forging,
                    qualityOptions = new[]
                    {
                        new { value = (int)ItemQualityMode.Random, label = "随机品级" },
                        new { value = (int)ItemQualityMode.Top, label = "100% 最上级" },
                    },
                    amplifyTypes = new[]
                    {
                        new { value = 0, label = "无红字（强化）" },
                        new { value = 1, label = EquipmentGrantPolicy.GetAmplifyTypeLabel(1) },
                        new { value = 2, label = EquipmentGrantPolicy.GetAmplifyTypeLabel(2) },
                        new { value = 3, label = EquipmentGrantPolicy.GetAmplifyTypeLabel(3) },
                        new { value = 4, label = EquipmentGrantPolicy.GetAmplifyTypeLabel(4) },
                    },
                },
                expiration,
            };
        }

        private static bool CanConfigureInventoryExpiration(int itemTemplateId, string itemKind, int currentExpireTime)
        {
            if (IsDailyDeleteTemplate(itemTemplateId))
                return false;
            if (currentExpireTime > 0)
                return true;

            var metadata = ItemMetadataResolver.Resolve(itemTemplateId);
            if (metadata == null || metadata.ItemKind == "special")
                return false;

            var isAvatar = string.Equals(itemKind, "avatar", StringComparison.Ordinal)
                || ItemMetadataResolver.IsAvatarMetadata(metadata);
            if (isAvatar)
                return currentExpireTime > 0 && AvatarDurationResolver.Resolve(itemTemplateId).Count > 0;

            var capability = BuildGrantExpirationCapability(itemTemplateId, metadata, isAvatar: false, out var error);
            return error == null && capability != null && capability.CanOverride;
        }

        private static bool IsDailyDeleteTemplate(int itemTemplateId)
        {
            var metadata = ItemMetadataResolver.Resolve(itemTemplateId);
            if (metadata?.IsStackable == true
                && StackableExpirationPolicyResolver.TryResolve(metadata.StackableFile, out var policy))
                return policy.DailyDeleteItem;
            return false;
        }

        private static object BuildInventoryExpirationConfig(SqliteInventoryStore.ItemRecord record)
        {
            if (record == null || !CanConfigureInventoryExpiration(record.ItemTemplateId, record.ItemKind, record.ExpireTime))
                return null;

            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            var metadata = ItemMetadataResolver.Resolve(record.ItemTemplateId);
            var isAvatar = string.Equals(record.ItemKind, "avatar", StringComparison.Ordinal)
                || ItemMetadataResolver.IsAvatarMetadata(metadata);
            if (isAvatar && record.ExpireTime <= 0)
                return null;

            var remainingDays = record.ExpireTime > now
                ? (int)Math.Ceiling((record.ExpireTime - now) / 86400.0)
                : 30;
            if (remainingDays < 1)
                remainingDays = 1;

            var durations = isAvatar
                ? AvatarDurationResolver.Resolve(record.ItemTemplateId)
                    .Select(value => new
                    {
                        days = value.DurationDays,
                        label = value.DurationDays == 0 ? "永久" : value.DurationDays + " 天",
                    })
                    .ToArray()
                : null;

            return new
            {
                canOverride = true,
                currentExpireTime = record.ExpireTime,
                currentRemainingDays = remainingDays,
                maxDays = ItemGrantExpirationOverride.MaximumDays,
                durations,
                defaultDays = durations != null && durations.Length > 0
                    ? durations[0].days
                    : Math.Min(remainingDays, ItemGrantExpirationOverride.MaximumDays),
            };
        }

        private static bool TryResolveInventoryExpirationOverride(
            SqliteInventoryStore.ItemRecord record,
            ItemMetadata metadata,
            int days,
            out int expireTime,
            out string error)
        {
            expireTime = 0;
            error = null;

            var isAvatar = string.Equals(record.ItemKind, "avatar", StringComparison.Ordinal)
                || ItemMetadataResolver.IsAvatarMetadata(metadata);
            if (isAvatar)
            {
                var durationOptions = AvatarDurationResolver.Resolve(record.ItemTemplateId);
                if (!AvatarDurationResolver.ContainsDuration(durationOptions, days))
                {
                    error = "装扮期限不属于该模板的 PVF 支持档位";
                    return false;
                }
                if (days == 0)
                    return true;

                var avatarValue = DateTimeOffset.Now.ToUnixTimeSeconds() + days * 86400L;
                if (avatarValue <= 0 || avatarValue > int.MaxValue)
                {
                    error = "装扮期限超出服务端可存储范围";
                    return false;
                }
                expireTime = (int)avatarValue;
                return true;
            }

            var defaultExpireTime = record.ExpireTime;
            string resolveError = null;
            if (defaultExpireTime <= 0
                && ItemGrantExpirationResolver.TryResolve(record.ItemTemplateId, metadata, out var resolvedExpireTime, out resolveError))
            {
                defaultExpireTime = resolvedExpireTime;
            }
            else if (defaultExpireTime <= 0 && resolveError != null)
            {
                error = resolveError;
                return false;
            }

            var capability = new ItemGrantExpirationCapability
            {
                IsLimited = defaultExpireTime > 0,
                CanOverride = defaultExpireTime > 0,
                DefaultExpireTime = defaultExpireTime,
            };
            if (metadata?.IsStackable == true
                && StackableExpirationPolicyResolver.TryResolve(metadata.StackableFile, out var policy))
            {
                capability.IsLimited = policy.RequiresInstanceExpiration
                    || policy.AbsoluteExpirationUnixTime > 0
                    || policy.DailyDeleteItem;
                capability.CanOverride = policy.RequiresInstanceExpiration
                    || policy.AbsoluteExpirationUnixTime > 0
                    || record.ExpireTime > 0;
            }
            if (record.ExpireTime > 0)
            {
                capability.IsLimited = true;
                capability.CanOverride = true;
                capability.DefaultExpireTime = record.ExpireTime;
            }

            return ItemGrantExpirationOverride.TryResolve(
                capability,
                days,
                DateTimeOffset.Now.ToUnixTimeSeconds(),
                out expireTime,
                out error);
        }

        private static string ResolveInventoryConfigKind(
            int itemTemplateId,
            string itemKind,
            InventoryListType listType,
            int job,
            PvfIndexService pvfIndex)
        {
            if (string.Equals(itemKind, "avatar", StringComparison.Ordinal))
            {
                return TryBuildInventoryAvatarOptions(itemTemplateId, job, out var options, out _)
                    && options.Count > 0
                    ? "avatar"
                    : null;
            }

            return IsInventoryConfigurableEquipment(itemTemplateId, itemKind, listType, pvfIndex, out _)
                ? "equipment"
                : null;
        }

        private static bool TryBuildInventoryAvatarOptions(
            int itemTemplateId,
            int job,
            out List<AvatarGrantOption> options,
            out string error)
        {
            options = null;
            error = null;
            if (!ItemMetadataResolver.TryLoadEquipmentFile(itemTemplateId, out var equipment))
            {
                error = "装扮模板无法从 PVF 读取";
                return false;
            }
            if (!AvatarGrantPolicy.IsUsableByJob(equipment.UsableJob, job))
            {
                error = "该装扮不适用于当前角色职业";
                return false;
            }

            var isCoat = string.Equals(
                NormalizeEquipmentToken(equipment.EquipmentType),
                "coat avatar",
                StringComparison.Ordinal);
            if (isCoat && equipment.AbilityCaseIndex < 0)
            {
                error = "该上衣装扮的 .equ 没有 ability case index 配置";
                return false;
            }
            if (!isCoat && (equipment.AvatarSelectAbilities == null || equipment.AvatarSelectAbilities.Count == 0))
            {
                error = "该装扮的 .equ 没有 avatar select ability 配置";
                return false;
            }

            options = AvatarGrantPolicy.ResolveOptions(
                equipment.EquipmentType,
                equipment.Grade,
                equipment.AvatarSelectAbilities,
                job,
                equipment.AbilityCaseIndex);
            if (options == null || options.Count == 0)
            {
                error = "该装扮没有当前职业可选属性";
                return false;
            }
            return true;
        }

        private static string NormalizeEquipmentToken(string value)
        {
            var text = (value ?? string.Empty).Trim().Trim('`').Trim().ToLowerInvariant();
            var start = text.IndexOf('[', StringComparison.Ordinal);
            var end = start >= 0 ? text.IndexOf(']', start + 1) : -1;
            return start >= 0 && end > start
                ? text.Substring(start + 1, end - start - 1).Trim().Replace("_", string.Empty)
                : text.Replace("_", string.Empty);
        }

        private static bool IsInventoryConfigurableEquipment(
            int itemTemplateId,
            string itemKind,
            InventoryListType listType,
            PvfIndexService pvfIndex,
            out EquipmentGrantCapability capability)
        {
            capability = null;
            var metadata = ItemMetadataResolver.Resolve(itemTemplateId);
            if (metadata == null || metadata.ItemKind == "special")
                return false;

            var isPetQualityEquipment = listType == InventoryListType.Pet
                && string.Equals(itemKind, "pet", StringComparison.Ordinal)
                && ItemMetadataResolver.IsPetArtifactMetadata(metadata)
                && metadata.SupportsPetEquipmentQuality;
            if (isPetQualityEquipment)
            {
                capability = EquipmentGrantPolicy.Describe(metadata);
                return true;
            }

            if (!string.Equals(itemKind, "equipment", StringComparison.Ordinal))
                return false;
            if (listType != InventoryListType.Main && listType != InventoryListType.Equipment)
                return false;
            if (ItemMetadataResolver.IsAvatarMetadata(metadata)
                || ItemMetadataResolver.IsPetInventoryEquipment(itemTemplateId)
                || ItemMetadataResolver.RequiresManualGrantType(metadata))
            {
                return false;
            }

            capability = EquipmentGrantPolicy.Describe(metadata);
            return capability.CanUpgrade || capability.CanAmplify || capability.CanForge;
        }

        private bool TryLoadInventoryItemRecord(
            int characterId,
            InventoryListType listType,
            int slot,
            out SqliteInventoryStore.ItemRecord record)
        {
            using (var connection = new SqliteConnection(_config.ConnectionString))
            {
                connection.Open();
                return TryLoadInventoryItemRecord(connection, null, characterId, listType, slot, out record);
            }
        }

        private static bool TryLoadInventoryItemRecord(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            InventoryListType listType,
            int slot,
            out SqliteInventoryStore.ItemRecord record)
        {
            record = null;
            var dbListType = SqliteInventoryStore.MapToDbListType(listType);
            var expectedKind = listType == InventoryListType.Avatar
                ? "avatar"
                : listType == InventoryListType.Equipment
                    ? "equipment"
                    : null;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT item_uid, list_type, slot_index, item_template_id, item_kind, stack_count, instance_value,
       durability, seal_flag, option_value, expire_time, marker_16, pet_serial_or_handle, equipment_lock_id, extra_json
FROM character_items
WHERE character_id = @characterId
  AND list_type = @listType
  AND slot_index = @slotIndex
  AND (@expectedKind IS NULL OR item_kind = @expectedKind)
ORDER BY item_uid DESC
LIMIT 1;";
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@listType", (int)dbListType);
                command.Parameters.AddWithValue("@slotIndex", slot);
                if (expectedKind == null)
                    command.Parameters.AddWithValue("@expectedKind", DBNull.Value);
                else
                    command.Parameters.AddWithValue("@expectedKind", expectedKind);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return false;
                    record = SqliteInventoryStore.ReadItemRecord(reader);
                    return true;
                }
            }
        }

        private static string MergeKnownEquipmentExtraJson(string originalExtraJson, string equipmentExtraJson)
        {
            var target = ParseJsonObject(originalExtraJson);
            var equipment = ParseJsonObject(equipmentExtraJson);
            foreach (var key in new[] { "extData0", "prefixData0E", "middleData1A", "tailData2F", "jewelSocket" })
            {
                if (equipment.TryGetPropertyValue(key, out var value))
                    target[key] = value == null ? null : value.DeepClone();
            }
            return target.ToJsonString();
        }

        private static JsonObject ParseJsonObject(string json)
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
                }
                catch
                {
                    return new JsonObject();
                }
            }
            return new JsonObject();
        }
    }

    public sealed class InventoryItemConfigureRequest
    {
        public int ListType { get; set; }

        public int Slot { get; set; }

        public ItemGrantOptions Options { get; set; }
    }
}
