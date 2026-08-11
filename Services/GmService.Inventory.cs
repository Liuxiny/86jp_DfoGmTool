using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DfoGmTool.ServerCore.Game.TitleBook;
using DfoGmTool.ServerCore.Game.Characters;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.Dungeon;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.Premium;
using DfoGmTool.ServerCore.Game.Quests;
using DfoGmTool.ServerCore.Game.ReviveCoin;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        private const short NameTagEquippedSlot = 28;
        private const int DefaultNameTagGrantDays = 30;
        private const long SecondsPerDay = 86400L;

        // 读侧从新版 ItemCore 投影页面模型，不读取旧 character_items。
        public object ListItems(int characterId, PvfIndexService pvfIndex)
        {
            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            var snapshot = _inventory.LoadCharacterItems(characterId, accountId);
            var rentalExpireTimes = _supplementalItemExpiration.LoadRentalExpireTimes(characterId);
            TryLoadGrantCharacter(characterId, out var job, out _, out _);

            var items = new List<object>();
            foreach (var item in snapshot)
            {
                var kind = item.ItemKind;
                var configKind = ResolveInventoryConfigKind(item.ItemTemplateId, kind, item.ListType, job, pvfIndex);
                var expirationConfigurable = CanConfigureInventoryExpiration(item.ItemTemplateId, kind, item.ExpireTime);
                var container = item.ListType switch
                {
                    InventoryListType.PersonalCargo => "个人仓库",
                    InventoryListType.AccountCargo => "账号金库",
                    InventoryListType.Pet => "宠物",
                    _ => "主背包",
                };
                var category = item.ListType switch
                {
                    InventoryListType.Main => ResolveMainSegment(item.SlotIndex),
                    InventoryListType.Equipment => "穿戴装备",
                    InventoryListType.Avatar => "时装",
                    InventoryListType.Pet => ResolvePetSegment(item.SlotIndex),
                    _ => container,
                };
                items.Add(new
                {
                    container,
                    category,
                    listType = (int)item.ListType,
                    slot = (int)item.SlotIndex,
                    templateId = item.ItemTemplateId,
                    name = pvfIndex.ResolveItemName(item.ItemTemplateId),
                    kind,
                    rarity = pvfIndex.ResolveItemRarity(item.ItemTemplateId),
                    count = item.Count,
                    instanceValue = item.InstanceValue,
                    durability = (int)item.Core.Durability,
                    serial = item.Core.ItemKind == ItemCore.KindCreature ? item.Core.CreatureUid : 0,
                    expireTime = item.ExpireTime,
                    supplementalExpiration = CreateSupplementalExpiration(rentalExpireTimes, item.ItemTemplateId, item.ExpireTime),
                    templateExpiration = CreateTemplateExpiration(pvfIndex, item.ItemTemplateId),
                    seal = (int)item.Core.SealFlag,
                    deletable = IsDeletable(item.ListType, item.SlotIndex),
                    configurable = configKind != null || expirationConfigurable,
                    expirationConfigurable,
                    configKind,
                });
            }

            return new { characterId, count = items.Count, items };
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
                dailyDeleteItem = expiration.DailyDeleteItem,
                invalid = expiration.HasInvalidDefinition,
            };
        }

        private static object CreateSupplementalExpiration(
            IReadOnlyDictionary<int, int> rentalExpireTimes,
            int itemTemplateId,
            int instanceExpireTime)
        {
            if (instanceExpireTime <= 0
                && rentalExpireTimes != null
                && rentalExpireTimes.TryGetValue(itemTemplateId, out var expireTime)
                && expireTime > 0)
            {
                return new
                {
                    expireTime,
                    source = "rental",
                };
            }

            return null;
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

            if (!_inventory.TryDelete(characterId, accountId, list, (short)slot, count, out var remaining))
                return Error("删除失败(槽位为空或该列表不支持删除)");

            return new
            {
                success = true,
                characterId,
                listType,
                slot,
                remaining,
                sorted = false,
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

                if (_inventory.TryDelete(characterId, accountId, list, (short)entry.Slot, 0, out _))
                {
                    deleted++;
                }
                else
                    failed.Add(new { entry.ListType, entry.Slot, reason = "删除失败" });
            }

            return new { success = true, characterId, deleted, sortedSegments = 0, failedCount = failed.Count, failed };
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
            if (slot <= 351) return "徽章";      // 289-351
            if (slot <= 353) return "保留槽";     // 352-353 不存放普通物品
            if (slot <= 359) return "账号晶块";   // 354-359 账号共享(accounts表列), 在账号面板调整
            return "其他";
        }

        private static string ResolvePetSegment(int slot)
        {
            if (slot <= 139) return "宠物";       // 0-139
            if (slot <= 188) return "宠物装备";    // 140-188
            return "宠物用品";                    // 189-237
        }

        public object GiveItem(
            int characterId,
            int itemTemplateId,
            int count,
            ItemGrantOptions options,
            PvfIndexService pvfIndex,
            string requestId = null,
            string deliveryMode = null)
        {
            if (itemTemplateId <= 0)
                return Error("itemTemplateId 无效");
            if (count <= 0)
                return Error("数量必须大于 0");
            if (string.IsNullOrWhiteSpace(requestId)
                || requestId.Length < 8
                || requestId.Length > 128)
                return Error("发放请求编号无效，请刷新页面后重试");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            // 名字解析不到通常意味着 ID 不存在, 直接发下去客户端会异常, 先拦住
            var name = pvfIndex.ResolveItemName(itemTemplateId);
            if (name == null
                && pvfIndex.IsReady
                && !CurrencyService.IsCubeFragment(itemTemplateId)
                && !ReviveCoinService.IsReviveCoinReward(itemTemplateId))
                return Error("物品 ID " + itemTemplateId + " 在 PVF 中不存在(装备/堆叠表都没有)");

            var metadata = ItemMetadataResolver.Resolve(itemTemplateId);
            // 名称装饰卡不是普通背包物品：服务端把它写入角色专用状态，
            // 不能创建一封玩家可领取的邮件，否则在线角色仍看不到已装备的卡。
            if (ItemMetadataResolver.IsNameTagMetadata(metadata))
            {
                var days = options?.ExpirationDays ?? DefaultNameTagGrantDays;
                if (days <= 0 || days > ItemGrantExpirationOverride.MaximumDays)
                    return Error("名称装饰卡期限必须在 1-3650 天之间");

                var previous = _inventory.LoadNameTag(characterId);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var baseTime = previous.ItemId == itemTemplateId && previous.ExpireTime > now
                    ? previous.ExpireTime
                    : now;
                var expire = Math.Min(
                    now + ItemGrantExpirationOverride.MaximumDays * SecondsPerDay,
                    baseTime + (long)days * count * SecondsPerDay);
                if (expire <= now || expire > int.MaxValue)
                    return Error("名称装饰卡期限超出服务端可存储范围");

                _inventory.UpsertNameTag(characterId, itemTemplateId, (int)expire);
                return new
                {
                    success = true,
                    characterId,
                    itemTemplateId,
                    name,
                    count,
                    delivery = "direct_name_tag",
                    slot = (int)NameTagEquippedSlot,
                    slots = new[] { (int)NameTagEquippedSlot },
                    expireTime = (int)expire,
                    nameTagEquipped = true,
                    requiresReselect = true,
                    deliveryHint = "名称装饰卡已直接写入角色状态；请返回角色选择界面后重新进入以刷新显示。",
                };
            }

            // premiumlist_new.etc 中的账号契约同样是服务端专用状态，
            // 复用 GrantAccountPremium 的原子写入与审计，不走邮件。
            if (PremiumCatalog.Load().TryGetValue(itemTemplateId, out var premiumType, out var durationDays)
                && premiumType > 0
                && durationDays > 0)
            {
                using (var connection = new SqliteConnection(_config.ConnectionString))
                {
                    connection.Open();
                    using var transaction = connection.BeginTransaction();
                    var premiumGrant = GrantAccountPremium(
                        connection,
                        transaction,
                        accountId,
                        characterId,
                        itemTemplateId,
                        count,
                        premiumType,
                        durationDays);
                    if (!premiumGrant.Success)
                        return Error(premiumGrant.Error ?? "账号契约发放失败");

                    transaction.Commit();
                    return new
                    {
                        success = true,
                        characterId,
                        accountId,
                        itemTemplateId,
                        name,
                        count = premiumGrant.GrantedCount,
                        delivery = "direct_premium",
                        premiumActivated = true,
                        premiumType,
                        durationDays,
                        expireTime = premiumGrant.ExpireTime,
                        requiresReselect = true,
                        deliveryHint = "账号契约已直接写入账号状态；请返回角色选择界面后重新进入以刷新显示。",
                    };
                }
            }

            // 只有明确的 inventory 才允许直写新版背包。缺失、空白、mail
            // 或任何未知值都保持历史邮件语义，避免旧客户端误触发直写。
            if (NormalizeDeliveryMode(deliveryMode) == "inventory")
            {
                options ??= new ItemGrantOptions();

                if (CurrencyService.IsCubeFragment(itemTemplateId))
                {
                    var slot = CurrencyService.GetCubeFragmentSlot(itemTemplateId);
                    try
                    {
                        using var connection = new SqliteConnection(_config.ConnectionString);
                        connection.Open();
                        using var transaction = connection.BeginTransaction(deferred: false);
                        CurrencyService.AddCubeFragment(
                            connection,
                            transaction,
                            accountId,
                            itemTemplateId,
                            count);
                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        return Error("晶块直充失败: " + ex.Message);
                    }

                    return new
                    {
                        success = true,
                        characterId,
                        accountId,
                        itemTemplateId,
                        name,
                        count,
                        grantedCount = count,
                        delivery = "direct_cube",
                        slot,
                        slots = new[] { slot },
                        requiresReselect = true,
                        replayed = false,
                        deliveryHint = "晶块已直接充入账号共享晶块；请返回角色选择界面后重新进入以刷新显示。",
                    };
                }

                if (ReviveCoinService.IsReviveCoinReward(itemTemplateId))
                {
                    if (!_inventory.TryAdjustVirtualCount(
                            characterId,
                            accountId,
                            ReviveCoinService.WalletSlot,
                            count,
                            int.MaxValue,
                            out var walletValue))
                    {
                        return Error("复活币直充失败");
                    }

                    return new
                    {
                        success = true,
                        characterId,
                        accountId,
                        itemTemplateId,
                        name,
                        count,
                        grantedCount = count,
                        walletValue,
                        delivery = "direct_revive",
                        slot = (int)ReviveCoinService.WalletSlot,
                        slots = new[] { (int)ReviveCoinService.WalletSlot },
                        requiresReselect = true,
                        replayed = false,
                        deliveryHint = "复活币已直接充入角色虚拟钱包；请返回角色选择界面后重新进入以刷新显示。",
                    };
                }

                if (!TryLoadGrantCharacter(characterId, out var job, out _, out _))
                    return Error("角色不存在: " + characterId);

                var inventoryGrant = _inventory.TryGrant(
                    characterId,
                    accountId,
                    job,
                    itemTemplateId,
                    count,
                    options);
                if (!inventoryGrant.Success)
                    return Error(inventoryGrant.Error ?? "新版背包发放失败");

                return new
                {
                    success = true,
                    characterId,
                    accountId,
                    itemTemplateId,
                    name,
                    count = inventoryGrant.GrantedCount,
                    requestedCount = inventoryGrant.RequestedCount,
                    grantedCount = inventoryGrant.GrantedCount,
                    delivery = "inventory",
                    listType = (int)inventoryGrant.ListType,
                    assignedSlot = (int)inventoryGrant.AssignedSlot,
                    slot = (int)inventoryGrant.AssignedSlot,
                    slots = inventoryGrant.AffectedSlots.Select(value => (int)value).ToArray(),
                    affectedSlots = inventoryGrant.AffectedSlots.Select(value => (int)value).ToArray(),
                    expireTime = inventoryGrant.ExpireTime,
                    requiresReselect = true,
                    replayed = false,
                    deliveryHint = "物品已直接写入新版角色背包；请返回角色选择界面后重新进入以刷新显示。",
                };
            }

            options ??= new ItemGrantOptions();
            var grant = _systemMail.SendItemGrant(
                characterId,
                accountId,
                itemTemplateId,
                count,
                options,
                requestId,
                name);
            if (!grant.Success)
                return Error(grant.Error ?? "邮件发放失败");
            return new
            {
                success = true,
                characterId,
                itemTemplateId,
                name,
                count,
                delivery = "mail",
                messageId = grant.MessageId,
                messageIds = grant.MessageIds,
                messageCount = grant.MessageCount,
                attachmentCount = grant.AttachmentCount,
                replayed = grant.Replayed,
                // The live server reloads mailbox rows whenever the mailbox is
                // opened. This out-of-process GM tool cannot push the online
                // 0x0063 alarm or refresh an already-open mailbox, so reopening
                // the mailbox is sufficient; character re-selection is not.
                notification = "mailbox_reopen_required",
                requiresReselect = false,
                deliveryHint = "在线角色请打开邮箱；如果邮箱已经打开，请关闭后重新打开，无需重新选择角色。",
            };
        }

        private static string NormalizeDeliveryMode(string deliveryMode)
        {
            return string.Equals(
                    (deliveryMode ?? string.Empty).Trim(),
                    "inventory",
                    StringComparison.OrdinalIgnoreCase)
                ? "inventory"
                : "mail";
        }

        private sealed class AccountPremiumGrantResult
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            public int ItemTemplateId { get; set; }
            public int RequestedCount { get; set; }
            public int GrantedCount { get; set; }
            public long ExpireTime { get; set; }
            public long PreviousExpireTime { get; set; }
        }

        private static AccountPremiumGrantResult GrantAccountPremium(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int accountId,
            int characterId,
            int itemTemplateId,
            int count,
            int premiumType,
            int durationDays)
        {
            var result = new AccountPremiumGrantResult
            {
                Success = false,
                ItemTemplateId = itemTemplateId,
                RequestedCount = count,
            };

            if (accountId <= 0)
                return FailAccountPremiumGrant(result, "账号不存在");
            if (count <= 0)
                return FailAccountPremiumGrant(result, "数量必须大于 0");
            if (premiumType <= 0 || durationDays <= 0)
                return FailAccountPremiumGrant(result, "账号契约配置无效");

            var effectiveCount = Math.Max(1, count);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var duration = (long)durationDays * SecondsPerDay * effectiveCount;
            var oldExpire = LoadAccountPremiumExpire(connection, transaction, accountId, premiumType);
            var newExpire = Math.Max(now, oldExpire) + duration;
            if (newExpire <= now)
                return FailAccountPremiumGrant(result, "账号契约期限超出服务端可存储范围");

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO account_premiums (account_id, premium_type, end_time, updated_at)
VALUES (@aid, @type, @expire, CURRENT_TIMESTAMP)
ON CONFLICT(account_id, premium_type)
DO UPDATE SET end_time = @expire, updated_at = CURRENT_TIMESTAMP;";
                command.Parameters.AddWithValue("@aid", accountId);
                command.Parameters.AddWithValue("@type", premiumType);
                command.Parameters.AddWithValue("@expire", newExpire);
                command.ExecuteNonQuery();
            }

            result.Success = true;
            result.GrantedCount = effectiveCount;
            result.ExpireTime = newExpire;
            result.PreviousExpireTime = oldExpire;
            WriteAccountPremiumGrantAudit(
                connection,
                transaction,
                accountId,
                characterId,
                result,
                premiumType,
                durationDays);
            return result;
        }

        private static long LoadAccountPremiumExpire(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int accountId,
            int premiumType)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT end_time FROM account_premiums WHERE account_id=@aid AND premium_type=@type;";
                command.Parameters.AddWithValue("@aid", accountId);
                command.Parameters.AddWithValue("@type", premiumType);
                var value = command.ExecuteScalar();
                return value != null && value != DBNull.Value ? Convert.ToInt64(value) : 0L;
            }
        }

        private static void WriteAccountPremiumGrantAudit(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int accountId,
            int characterId,
            AccountPremiumGrantResult grant,
            int premiumType,
            int durationDays)
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
    'account', @ownerId, @characterId, 'gm_grant', NULL, NULL,
    @itemTemplateId, @deltaStackCount, @payloadJson);";
                command.Parameters.AddWithValue("@ownerId", accountId);
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@itemTemplateId", grant.ItemTemplateId);
                command.Parameters.AddWithValue("@deltaStackCount", grant.GrantedCount);
                command.Parameters.AddWithValue("@payloadJson",
                    "{\"source\":\"gm_tool\",\"premiumActivated\":true"
                    + ",\"premiumType\":" + premiumType.ToString(CultureInfo.InvariantCulture)
                    + ",\"requestedCount\":" + grant.RequestedCount.ToString(CultureInfo.InvariantCulture)
                    + ",\"grantedCount\":" + grant.GrantedCount.ToString(CultureInfo.InvariantCulture)
                    + ",\"durationDays\":" + durationDays.ToString(CultureInfo.InvariantCulture)
                    + ",\"expireTime\":" + grant.ExpireTime.ToString(CultureInfo.InvariantCulture)
                    + ",\"previousExpireTime\":" + grant.PreviousExpireTime.ToString(CultureInfo.InvariantCulture)
                    + "}");
                command.ExecuteNonQuery();
            }
        }

        private static AccountPremiumGrantResult FailAccountPremiumGrant(AccountPremiumGrantResult result, string error)
        {
            result.Error = error;
            return result;
        }

        public object GetItemGrantOptions(int characterId, int itemTemplateId, PvfIndexService pvfIndex)
        {
            if (itemTemplateId <= 0)
                return Error("itemTemplateId 无效");
            if (!TryLoadGrantCharacter(characterId, out var job, out var growType, out var level))
                return Error("角色不存在: " + characterId);

            var metadata = ItemMetadataResolver.Resolve(itemTemplateId);
            if (metadata == null || metadata.ItemKind == "special")
                return Error("物品 ID " + itemTemplateId + " 在当前 PVF 中不存在");

            var name = pvfIndex.ResolveItemName(itemTemplateId);
            var equipmentCapability = EquipmentGrantPolicy.Describe(metadata);
            var isAvatar = ItemMetadataResolver.IsAvatarMetadata(metadata);
            var requiresManualGrantType = ItemMetadataResolver.RequiresManualGrantType(metadata);
            var expiration = BuildGrantExpirationCapability(itemTemplateId, metadata, isAvatar, out var expirationError);
            if (expirationError != null)
                return Error(expirationError);

            object avatar = null;
            List<AvatarGrantOption> avatarOptionValues = null;
            IReadOnlyList<AvatarDurationOption> avatarDurationValues = Array.Empty<AvatarDurationOption>();
            if (isAvatar)
            {
                if (!ItemMetadataResolver.TryLoadEquipmentFile(itemTemplateId, out var equipment))
                    return Error("装扮模板无法从 PVF 读取");

                var avatarMetadata = AvatarEquipmentMetadataReader.Read(equipment);
                var compatible = AvatarGrantPolicy.IsUsableByJob(equipment.UsableJob, job);
                avatarOptionValues = compatible
                    ? AvatarGrantPolicy.ResolveOptions(
                        equipment.EquipmentType,
                        equipment.Grade,
                        avatarMetadata.SelectAbilities,
                        job,
                        avatarMetadata.AbilityCaseIndex)
                    : new List<AvatarGrantOption>();
                avatarDurationValues = AvatarDurationResolver.Resolve(itemTemplateId);
                avatar = new
                {
                    compatible,
                    part = metadata.EquipmentType,
                    grade = metadata.Grade,
                    usableJob = equipment.UsableJob,
                    options = avatarOptionValues.Select(value => new
                    {
                        value = value.Value,
                        label = value.Label,
                        isSkill = value.IsSkill,
                    }).ToArray(),
                    durations = avatarDurationValues.Select(value => new
                    {
                        days = value.DurationDays,
                        label = value.DurationDays == 0 ? "永久" : value.DurationDays + " 天",
                    }).ToArray(),
                };
            }

            var isPetArtifact = ItemMetadataResolver.IsPetArtifactMetadata(metadata);
            var isPetCreature = ItemMetadataResolver.IsPetCreatureMetadata(metadata);
            var isConfigurablePetEquipment = isPetArtifact && metadata.SupportsPetEquipmentQuality;
            var isConfigurableEquipment = string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal)
                && !requiresManualGrantType
                && !isAvatar
                && !ItemMetadataResolver.IsPetInventoryEquipment(itemTemplateId)
                && (equipmentCapability.CanUpgrade || equipmentCapability.CanAmplify || equipmentCapability.CanForge);
            var requiresAvatarConfiguration = isAvatar
                && ((avatarOptionValues?.Count ?? 0) > 1 || avatarDurationValues.Count > 0);
            var requiresConfiguration = !isPetCreature
                && (isConfigurableEquipment
                    || isConfigurablePetEquipment
                    || requiresAvatarConfiguration
                    || (!isPetArtifact && expiration.CanOverride)
                    || requiresManualGrantType);
            return new
            {
                success = true,
                characterId,
                itemTemplateId,
                name,
                kind = metadata.ItemKind,
                requiresConfiguration,
                pvfTypeTag = ItemMetadataResolver.ResolvePvfTypeTag(metadata),
                equipment = isConfigurableEquipment || isConfigurablePetEquipment ? new
                {
                    type = metadata.EquipmentType,
                    rarity = metadata.Rarity,
                    minimumLevel = metadata.MinimumLevel,
                    canUpgrade = equipmentCapability.CanUpgrade,
                    canAmplify = equipmentCapability.CanAmplify,
                    canForge = equipmentCapability.CanForge,
                    supportsQuality = isConfigurableEquipment || isConfigurablePetEquipment,
                    maxUpgradeLevel = equipmentCapability.MaxUpgradeLevel,
                    maxForgingLevel = equipmentCapability.MaxForgingLevel,
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
                } : null,
                avatar,
                manual = requiresManualGrantType ? new
                {
                    required = true,
                    choices = BuildManualGrantTypeChoices(metadata),
                } : null,
                expiration = new
                {
                    limited = expiration.IsLimited,
                    canOverride = expiration.CanOverride,
                    expired = expiration.IsExpired,
                    defaultExpireTime = expiration.DefaultExpireTime,
                    maxDays = ItemGrantExpirationOverride.MaximumDays,
                },
            };
        }

        private static object[] BuildManualGrantTypeChoices(ItemMetadata metadata)
        {
            if (metadata == null)
                return Array.Empty<object>();

            if (string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal))
            {
                return new object[]
                {
                    new { value = "equipment", label = "普通装备栏" },
                    new { value = "avatar", label = "装扮栏" },
                    new { value = "pet", label = "宠物栏" },
                    new { value = "pet-equipment", label = "宠物装备栏" },
                };
            }

            if (metadata.IsStackable)
            {
                return new object[]
                {
                    new { value = "consumable", label = "消耗品" },
                    new { value = "material", label = "材料" },
                    new { value = "quest", label = "任务品" },
                    new { value = "expert-material", label = "副职业材料" },
                    new { value = "avatar-emblem", label = "徽章" },
                    new { value = "pet-consumable", label = "宠物消耗品" },
                };
            }

            return Array.Empty<object>();
        }

        private bool TryLoadGrantCharacter(int characterId, out int job, out int growType, out int level)
        {
            job = 0;
            growType = 0;
            level = 0;
            using (var connection = new SqliteConnection(_config.ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
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
        }

        private static ItemGrantExpirationCapability BuildGrantExpirationCapability(
            int itemTemplateId,
            ItemMetadata metadata,
            bool isAvatar,
            out string error)
        {
            error = null;
            if (isAvatar)
            {
                var durations = AvatarDurationResolver.Resolve(itemTemplateId);
                return new ItemGrantExpirationCapability
                {
                    IsLimited = durations.Any(value => value.DurationDays > 0),
                    CanOverride = durations.Count > 0,
                    DefaultExpireTime = 0,
                };
            }

            if (!ItemGrantExpirationResolver.TryResolve(itemTemplateId, metadata, out var expireTime, out error))
            {
                if (!IsExpiredGrantExpirationError(error))
                    return new ItemGrantExpirationCapability();
                error = null;
                return new ItemGrantExpirationCapability
                {
                    IsLimited = true,
                    CanOverride = true,
                    DefaultExpireTime = 0,
                    IsExpired = true,
                };
            }
            var capability = new ItemGrantExpirationCapability
            {
                IsLimited = expireTime > 0,
                CanOverride = expireTime > 0,
                DefaultExpireTime = expireTime,
            };
            if (metadata.IsStackable
                && StackableExpirationPolicyResolver.TryResolve(metadata.StackableFile, out var policy))
            {
                capability.IsLimited = policy.RequiresInstanceExpiration
                    || policy.AbsoluteExpirationUnixTime > 0
                    || policy.DailyDeleteItem;
                capability.CanOverride = policy.RequiresInstanceExpiration
                    || policy.AbsoluteExpirationUnixTime > 0;
            }
            return capability;
        }

        private static bool IsExpiredGrantExpirationError(string error)
            => !string.IsNullOrWhiteSpace(error) && error.Contains("已过期");

        public object RemoveItem(int characterId, int itemTemplateId, int count)
        {
            if (count <= 0)
                count = 1;

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            if (!_inventory.TryRemoveByTemplateId(characterId, accountId, itemTemplateId, count, out var slot, out var remaining))
                return Error("移除失败(角色没有该物品或数量不足)");
            return new { success = true, characterId, itemTemplateId, count, slot = (int)slot, remaining };
        }

        public object AdjustGold(int characterId, int amount)
        {
            if (amount == 0)
                return Error("amount 不能为 0");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            GoldLimitSnapshot goldLimit;
            try
            {
                goldLimit = LoadGoldLimitSnapshot(characterId);
            }
            catch (InvalidOperationException ex)
            {
                return Error(ex.Message);
            }

            var requestedAmount = amount;
            var wallet = _inventory.LoadWallet(characterId);
            if (amount > 0)
                amount = Math.Min(amount, Math.Max(0, goldLimit.GoldCarryLimit - wallet.Gold));
            if (!_inventory.TryAdjustVirtualCount(characterId, accountId, 0, amount, goldLimit.GoldCarryLimit, out var gold))
                return Error("扣款失败(金币不足)");
            return new { success = true, characterId, requestedAmount, amount, gold, goldCarryLimit = goldLimit.GoldCarryLimit };
        }

        // 三种角色货币都写入新版 ItemCore 虚拟钱包槽：金币 slot0、复活币 slot1、技能点 slot2。
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
                GoldLimitSnapshot goldLimit;
                try
                {
                    goldLimit = LoadGoldLimitSnapshot(characterId);
                }
                catch (InvalidOperationException ex)
                {
                    return Error(ex.Message);
                }
                if (value > goldLimit.GoldCarryLimit)
                    return Error("金币不能超过当前上限 " + goldLimit.GoldCarryLimit.ToString("N0"));

                if (!_inventory.TrySetVirtualCount(characterId, accountId, 0, value))
                    return Error("设置失败");
                return new { success = true, characterId, type, value, goldCarryLimit = goldLimit.GoldCarryLimit };
            }

            int slot;
            switch (type)
            {
                case "revive": slot = 1; break;
                case "sp": slot = 2; break;
                default: return Error("不支持的类型: " + type + " (可用: gold/revive/sp)");
            }

            if (!_inventory.TrySetVirtualCount(characterId, accountId, (short)slot, value))
                return Error("货币设置失败(slot " + slot + ")");
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
            using (var connection = new SqliteConnection(_config.ConnectionString))
            {
                connection.Open();
                using var transaction = connection.BeginTransaction();
                if (amount > 0)
                {
                    if (useToken)
                        CurrencyService.GrantTokenCera(connection, transaction, characterId, amount);
                    else
                        CurrencyService.GrantCera(connection, transaction, characterId, amount);
                }
                else
                {
                    var ok = useToken
                        ? CurrencyService.TrySpendTokenCera(connection, transaction, characterId, -amount)
                        : CurrencyService.TrySpendCera(connection, transaction, characterId, -amount);
                    if (!ok)
                        return Error("扣减失败(余额不足)");
                }
                transaction.Commit();
            }

            using (var connection = new SqliteConnection(_config.ConnectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT cera, token_cera FROM accounts WHERE account_id=@aid;";
                command.Parameters.AddWithValue("@aid", accountId);
                using var reader = command.ExecuteReader();
                reader.Read();
                return new { success = true, characterId, accountId, amount, cera = reader.GetInt32(0), tokenCera = reader.GetInt32(1) };
            }
        }
    }

    public sealed class BatchDeleteEntry
    {
        public int ListType { get; set; }
        public int Slot { get; set; }
    }
}
