using System;
using System.Collections.Generic;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.ReviveCoin;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal sealed class GmMailAttachmentDraft
    {
        internal int Ordinal { get; set; }
        internal byte ItemType { get; set; }
        internal int SourceListType { get; set; }
        internal int ItemTemplateId { get; set; }
        internal string ItemKind { get; set; }
        internal int ItemCount { get; set; }
        internal int InstanceValue { get; set; }
        internal ushort Durability { get; set; }
        internal byte SealFlag { get; set; }
        internal int OptionValue { get; set; }
        internal int ExpireTime { get; set; }
        internal int Marker16 { get; set; }
        internal int PetSerialOrHandle { get; set; }
        internal string ExtraJson { get; set; } = "{}";
        internal byte[] ItemCoreData { get; set; }
        internal string DetailJson { get; set; } = string.Empty;
    }

    public sealed partial class NewInventoryStore
    {
        // 系统邮件服务会把附件按每封 10 件分片；总量上限在这里提前检查，
        // 避免恶意请求在生成草稿时分配过大的列表。
        internal const int MaximumMailAttachments = 100;

        internal bool TryCreateMailAttachments(
            int job,
            int itemTemplateId,
            int requestedCount,
            ItemGrantOptions options,
            out IReadOnlyList<GmMailAttachmentDraft> attachments,
            out string error)
        {
            attachments = Array.Empty<GmMailAttachmentDraft>();
            error = null;
            if (itemTemplateId <= 0 || requestedCount <= 0)
            {
                error = "物品编号和数量必须大于 0";
                return false;
            }

            options ??= new ItemGrantOptions();
            var metadata = ItemMetadataResolver.Resolve(itemTemplateId);
            var isServerVirtualReward = CurrencyService.IsCubeFragment(itemTemplateId)
                || ReviveCoinService.IsReviveCoinReward(itemTemplateId);
            if (metadata == null && !isServerVirtualReward)
            {
                error = "物品模板不存在";
                return false;
            }

            byte itemKind;
            InventoryListType listType;
            if (metadata == null || string.Equals(metadata.ItemKind, "special", StringComparison.Ordinal))
            {
                if (!isServerVirtualReward)
                {
                    error = "该特殊资产不支持通过物品邮件发放";
                    return false;
                }
                itemKind = ItemCore.KindConsumable;
                listType = InventoryListType.Main;
            }
            else if (!TryResolveKindAndRange(
                         metadata,
                         options.ManualGrantType,
                         out itemKind,
                         out listType,
                         out _,
                         out _,
                         out error))
            {
                return false;
            }

            var expireTime = 0;
            if (metadata != null
                && !TryResolveExpiration(
                    itemTemplateId,
                    metadata,
                    options,
                    itemKind == ItemCore.KindAvatar,
                    out expireTime,
                    out error))
            {
                return false;
            }

            byte avatarAbility = 0;
            if (itemKind == ItemCore.KindAvatar
                && !TryValidateAvatarOptions(
                    itemTemplateId,
                    job,
                    options,
                    out avatarAbility,
                    out expireTime,
                    out error))
            {
                return false;
            }

            var stackable = isServerVirtualReward || IsStackableKind(itemKind);
            // PVF 的 stack_limit 是客户端/服务端共同遵循的单堆上限。
            // 未配置上限的旧条目按一个无限堆处理，但仍受系统邮件总量上限约束。
            var stackLimit = int.MaxValue;
            if (stackable && metadata != null && metadata.IsStackable && metadata.StackLimit > 0)
                stackLimit = metadata.StackLimit;

            var expectedAttachmentCount = stackable
                ? ((long)requestedCount + stackLimit - 1L) / stackLimit
                : requestedCount;
            if (expectedAttachmentCount > MaximumMailAttachments)
            {
                var stackLimitText = stackLimit == int.MaxValue
                    ? "未设置（按单堆处理）"
                    : stackLimit.ToString();
                var maximumCount = stackable
                    ? (long)stackLimit * MaximumMailAttachments
                    : MaximumMailAttachments;
                error = "发放数量过大：PVF 单堆上限 " + stackLimitText
                    + "，本次最多可发 " + maximumCount + " 个（最多 "
                    + MaximumMailAttachments + " 个附件、10 封邮件）";
                return false;
            }

            var result = new List<GmMailAttachmentDraft>((int)expectedAttachmentCount);
            var remaining = requestedCount;
            var ordinal = 0;
            while (remaining > 0)
            {
                var perAttachmentCount = stackable
                    ? (int)Math.Min((long)remaining, stackLimit)
                    : 1;
                ItemCore core;
                if (metadata == null || string.Equals(metadata.ItemKind, "special", StringComparison.Ordinal))
                {
                    core = ItemCore.Create(itemKind, itemTemplateId);
                    core.Count = perAttachmentCount;
                }
                else
                {
                    core = CreateDefaultCore(
                        metadata,
                        itemKind,
                        itemTemplateId,
                        perAttachmentCount,
                        options,
                        expireTime,
                        out error);
                    if (core == null)
                        return false;
                }

                core.SortLockFlag = 0;
                core.EquipmentLockId = 0;
                if (itemKind == ItemCore.KindAvatar)
                {
                    core.AvatarUid = 0;
                    core.AbilityNo = avatarAbility;
                    core.ExpireTime = expireTime;
                }
                else if (itemKind == ItemCore.KindCreature)
                {
                    core.CreatureUid = 0;
                }

                var itemType = itemKind == ItemCore.KindAvatar
                    ? (byte)1
                    : listType == InventoryListType.Pet
                        ? (byte)3
                        : (byte)0;
                result.Add(new GmMailAttachmentDraft
                {
                    Ordinal = ordinal,
                    ItemType = itemType,
                    SourceListType = (int)listType,
                    ItemTemplateId = itemTemplateId,
                    ItemKind = GetLegacyKindLabel(itemKind),
                    ItemCount = perAttachmentCount,
                    InstanceValue = core.Value,
                    Durability = core.Durability,
                    SealFlag = core.SealFlag,
                    OptionValue = core.AbilityNo,
                    ExpireTime = core.ExpireTime,
                    Marker16 = core.Marker16,
                    PetSerialOrHandle = core.CreatureUid,
                    ItemCoreData = core.ToBytes(),
                });

                remaining -= perAttachmentCount;
                ordinal++;
            }

            attachments = result;
            return true;
        }
    }
}
