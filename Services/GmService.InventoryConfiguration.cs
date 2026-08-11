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

            if (!TryGetAccountId(characterId, out var accountId))
                return Error("角色不存在: " + characterId);
            var list = (InventoryListType)request.ListType;
            if (!_inventory.TryLoadItem(characterId, accountId, list, (short)request.Slot, out var record))
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
                ushort? requested = null;
                if (options.AvatarOptionValue != null)
                {
                    if (!TryBuildInventoryAvatarOptions(record.ItemTemplateId, job, out var avatarOptions, out var avatarError))
                        return Error(avatarError ?? "该时装没有可配置属性");
                    var raw = options.AvatarOptionValue.Value;
                    if (raw < 0 || raw > byte.MaxValue || !AvatarGrantPolicy.ContainsValue(avatarOptions, raw))
                        return Error("装扮属性不属于当前模板、品级和职业的合法选项");
                    requested = (ushort)raw;
                }
                if (requested == null && expireTime == null)
                    return Error("该时装没有可保存的配置项");
                if (!_inventory.UpdateAvatarDetail(characterId, accountId, list, (short)request.Slot, requested, expireTime, out var avatarUpdateError))
                    return Error(avatarUpdateError);
                return new { success = true, characterId, listType = request.ListType, slot = request.Slot, type = "avatar", optionValue = requested, expireTime };
            }

            if (!IsInventoryConfigurableEquipment(record.ItemTemplateId, record.ItemKind, list, pvfIndex, out var capability))
            {
                if (expireTime == null)
                    return Error("该装备类型没有可配置属性");
                if (!_inventory.UpdateItemCore(characterId, accountId, list, (short)request.Slot, core => { core.ExpireTime = expireTime.Value; return null; }, out _, out var expirationUpdateError))
                    return Error(expirationUpdateError);
                return new { success = true, characterId, listType = request.ListType, slot = request.Slot, type = "expiration", expireTime };
            }

            var seed = (int)ItemQuality.ResolveSeed(options.QualityMode);
            if (!_inventory.UpdateItemCore(characterId, accountId, list, (short)request.Slot, core =>
            {
                if (!Enum.IsDefined(typeof(ItemQualityMode), options.QualityMode)) return "装备品级选项无效";
                if (options.UpgradeLevel < 0 || options.UpgradeLevel > EquipmentGrantPolicy.MaximumUpgradeLevel) return "强化/增幅等级必须在 0-31 之间";
                if (options.AmplifyType < 0 || options.AmplifyType > 4) return "红字属性类型无效";
                if (options.AmplifyType > 0 && !capability.CanAmplify) return "该装备不支持增幅";
                if (options.UpgradeLevel > 0 && options.AmplifyType == 0 && !capability.CanUpgrade) return "该装备不支持强化";
                if (options.ForgingLevel < 0 || options.ForgingLevel > EquipmentGrantPolicy.MaximumForgingLevel || (options.ForgingLevel > 0 && !capability.CanForge)) return "锻造等级无效或该装备不是武器";
                core.InstanceValue = seed;
                core.Upgrade = (byte)options.UpgradeLevel;
                core.AmplifyType = (byte)options.AmplifyType;
                core.AmplifyValue = options.AmplifyType > 0 ? AmplifyInitialValueResolver.Resolve(metadata.Rarity) : (ushort)0;
                if (options.AmplifyType > 0 && core.AmplifyValue == 0) return "无法从 PVF 计算红字初始值";
                core.GenuineUpgrade = (byte)options.ForgingLevel;
                if (expireTime != null) core.ExpireTime = expireTime.Value;
                return null;
            }, out _, out var updateError))
                return Error(updateError);

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

        private object BuildInventoryItemConfigOptions(
            NewInventoryItemRecord record,
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
                var selected = AvatarGrantPolicy.ContainsValue(options, record.Core.AbilityNo)
                    ? (int)record.Core.AbilityNo
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
            var currentAmplifyType = record.Core.AmplifyType <= 4
                ? record.Core.AmplifyType
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
                    currentUpgradeLevel = (int)record.Core.Upgrade,
                    currentAmplifyType = (int)currentAmplifyType,
                    currentForgingLevel = (int)record.Core.GenuineUpgrade,
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

        private static object BuildInventoryExpirationConfig(NewInventoryItemRecord record)
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
            NewInventoryItemRecord record,
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
            var avatarMetadata = AvatarEquipmentMetadataReader.Read(equipment);
            if (isCoat && avatarMetadata.AbilityCaseIndex < 0)
            {
                error = "该上衣装扮的 .equ 没有 ability case index 配置";
                return false;
            }
            if (!isCoat && avatarMetadata.SelectAbilities.Count == 0)
            {
                error = "该装扮的 .equ 没有 avatar select ability 配置";
                return false;
            }

            options = AvatarGrantPolicy.ResolveOptions(
                equipment.EquipmentType,
                equipment.Grade,
                avatarMetadata.SelectAbilities,
                job,
                avatarMetadata.AbilityCaseIndex);
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
            out NewInventoryItemRecord record)
        {
            record = null;
            return TryGetAccountId(characterId, out var accountId)
                && _inventory.TryLoadItem(characterId, accountId, listType, (short)slot, out record);
        }
    }

    public sealed class InventoryItemConfigureRequest
    {
        public int ListType { get; set; }

        public int Slot { get; set; }

        public ItemGrantOptions Options { get; set; }
    }
}
