using DfoGmTool.ServerCore.GameWorld;
using DfoGmTool.ServerCore.Game.ItemUpgrade;
using GmPvfLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed class ItemMetadata
    {
        public string ItemKind { get; set; } = "unknown";

        public string StackableType { get; set; }

        internal StackableItemFile StackableFile { get; set; }

        public string PvfFilePath { get; set; }

        public int BuyGold { get; set; }

        public int BuyCoin { get; set; }

        public int SellGold { get; set; }

        public ushort Durability { get; set; }

        public int StackLimit { get; set; }

        public int NeedMaterialId { get; set; }

        public int NeedMaterialCount { get; set; }

        public int Grade { get; set; }

        public int MinimumLevel { get; set; }

        public int Rarity { get; set; }

        public string EquipmentType { get; set; }

        public string ItemCategory { get; set; }

        public string AttachType { get; set; }

        public bool SupportsPetEquipmentQuality { get; set; }

        public IReadOnlyList<string> ImpossibleContents { get; set; } = Array.Empty<string>();

        public bool IsSealed => string.Equals(AttachType?.Trim('[', ']', ' '), "sealing", StringComparison.OrdinalIgnoreCase);

        public bool IsStackable => string.Equals(ItemKind, "stackable", StringComparison.Ordinal);

        public bool IsMaterialExchange => NeedMaterialId > 0 && NeedMaterialCount > 0;

        public void GetSlotRange(out int slotStart, out int slotEnd)
        {
            if (string.Equals(ItemKind, "equipment", StringComparison.Ordinal))
            {
                slotStart = 9; slotEnd = 64; return;
            }
            if (StackableType == null)
            {
                slotStart = 65; slotEnd = 120; return;
            }
            var st = StackableType.Replace("`", "").Trim().ToLowerInvariant();
            if (st.StartsWith("[material]"))
            {
                if (st.EndsWith("4"))
                { slotStart = 345; slotEnd = 359; }
                else
                { slotStart = 121; slotEnd = 176; }
                return;
            }
            if (st.StartsWith("[quest]"))
            { slotStart = 177; slotEnd = 232; return; }
            if (st.StartsWith("[material expert job]"))
            { slotStart = 233; slotEnd = 288; return; }
            if (st.StartsWith("[avatar emblem]"))
            { slotStart = 289; slotEnd = 344; return; }
            slotStart = 65; slotEnd = 120;
        }
    }

    internal sealed class ItemSellRates
    {
        public int Equipment { get; set; } = 200;  
        public int NonStackable { get; set; } = 150; 
        public int Stackable { get; set; } = 30;    

        public static ItemSellRates Parse(string content)
        {
            var r = new ItemSellRates();
            if (string.IsNullOrEmpty(content)) return r;
            
            var m = System.Text.RegularExpressions.Regex.Match(content, @"\]\s*\r?\n\s*(\d+)\s+(\d+)\s+(\d+)");
            if (m.Success)
            {
                r.Equipment = int.Parse(m.Groups[1].Value);
                r.NonStackable = int.Parse(m.Groups[2].Value);
                r.Stackable = int.Parse(m.Groups[3].Value);
            }
            return r;
        }
    }

    public static class ItemMetadataResolver 
    {
        private static readonly object CacheLock = new object();
        internal static Lazy<LstFile> EquipmentList = CreateEquipmentList();
        private static Lazy<LstFile> StackableList = CreateStackableList();
        private static Lazy<ItemSellRates> SellRates = CreateSellRates();
        private static readonly Regex AvatarSocketRegex = new Regex(@"\[\s*([ABCDSM])\s+socket\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private const string AvatarTypeSelectTag = "[avatar type select]";
        private const string AvatarTypeSelectEndTag = "[/avatar type select]";
        private const string EmblemSocketDefaultTag = "[emblem socket default]";
        private const string EmblemSocketDefaultEndTag = "[/emblem socket default]";
        private const string AvatarEmblemSocketNumTag = "[avatar emblem socket num]";

        internal static void ResetForPvfChange()
        {
            lock (CacheLock)
            {
                EquipmentList = CreateEquipmentList();
                StackableList = CreateStackableList();
                SellRates = CreateSellRates();
            }
        }

        private static Lazy<LstFile> CreateEquipmentList()
        {
            return new Lazy<LstFile>(() => LstFile.Parse(PvfArchiveAccessor.ReadText("equipment/equipment.lst")));
        }

        private static Lazy<LstFile> CreateStackableList()
        {
            return new Lazy<LstFile>(() => LstFile.Parse(PvfArchiveAccessor.ReadText("stackable/stackable.lst")));
        }

        private static Lazy<ItemSellRates> CreateSellRates()
        {
            return new Lazy<ItemSellRates>(() => ItemSellRates.Parse(PvfArchiveAccessor.ReadText("equipment/pricetable.tbl")));
        }

        public static ItemMetadata Resolve(int itemTemplateId)
        {
            var equipmentEntry = EquipmentList.Value.GetById(itemTemplateId);
            if (equipmentEntry != null)
            {
                var equipment = EquipmentFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("equipment", equipmentEntry.FilePath)));
                var buyGold = Math.Max(0, equipment.Price >= 0 ? equipment.Price : equipment.Value);
                
                
                var baseSellPrice = equipment.Value >= 0 ? equipment.Value : buyGold;
                var sellGold = Math.Max(1, baseSellPrice * SellRates.Value.Equipment / 1000);
                // 只有武器和防具有耐久度，其他装备类型（首饰/魔法石/称号/装扮/宠物等）无耐久。
                var eqType = NormalizeEquipmentType(equipment.EquipmentType);
                var hasDurability = equipment.Durability > 0 && HasDurabilityByType(eqType);
                var durability = hasDurability ? equipment.Durability : 0;

                return new ItemMetadata
                {
                    ItemKind = "equipment",
                    PvfFilePath = equipmentEntry.FilePath,
                    BuyGold = buyGold,
                    SellGold = sellGold,
                    Durability = (ushort)durability,
                    StackLimit = 1,
                    Grade = equipment.Grade,
                    MinimumLevel = equipment.MinimumLevel,
                    Rarity = equipment.Rarity,
                    EquipmentType = NormalizeEquipmentType(equipment.EquipmentType),
                    ItemCategory = equipment.ItemCategory,
                    AttachType = equipment.AttachType,
                    SupportsPetEquipmentQuality = HasPetEquipmentQuality(equipment),
                    ImpossibleContents = equipment.ImpossibleContentItems,
                };
            }

            var stackableEntry = StackableList.Value.GetById(itemTemplateId);
            if (stackableEntry != null)
            {
                var stackable = StackableItemFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("stackable", stackableEntry.FilePath)));
                var buyGold = Math.Max(0, stackable.Price >= 0 ? stackable.Price : stackable.Value);
                
                
                
                
                var baseSellPrice = stackable.Value >= 0 ? stackable.Value : (stackable.Price >= 0 ? stackable.Price : 0);
                var sellGold = baseSellPrice > 0 ? Math.Max(1, baseSellPrice * SellRates.Value.Stackable / 1000) : 0;

                int needMatId = 0, needMatCount = 0;
                if (!string.IsNullOrWhiteSpace(stackable.NeedMaterial))
                {
                    var parts = stackable.NeedMaterial.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        int.TryParse(parts[0], out needMatId);
                        int.TryParse(parts[1], out needMatCount);
                    }
                }

                
                
                var hasMaterialCost = needMatId > 0 && needMatCount > 0;
                return new ItemMetadata
                {
                    ItemKind = "stackable",
                    StackableType = stackable.StackableType,
                    StackableFile = stackable,
                    PvfFilePath = stackableEntry.FilePath,
                    BuyGold = buyGold,
                    SellGold = sellGold,
                    Durability = 0,
                    StackLimit = stackable.StackLimit,
                    NeedMaterialId = needMatId,
                    NeedMaterialCount = needMatCount,
                    Grade = stackable.Grade,
                    MinimumLevel = stackable.MinimumLevel,
                    Rarity = stackable.Rarity,
                    ItemCategory = stackable.ItemCategory,
                    ImpossibleContents = stackable.ImpossibleContentItems,
                };
            }

            return new ItemMetadata
            {
                ItemKind = "special",
                BuyGold = 0,
                SellGold = 1,
                Durability = 0,
                StackLimit = 1,
            };
        }

        public static LstEntry GetStackableEntry(int itemTemplateId)
        {
            return StackableList.Value.GetById(itemTemplateId);
        }

        public static LstEntry GetEquipmentEntry(int itemTemplateId)
        {
            return EquipmentList.Value.GetById(itemTemplateId);
        }

        public static bool TryLoadEquipmentFile(int itemTemplateId, out EquipmentFile equipment)
        {
            equipment = null;
            var equipmentEntry = EquipmentList.Value.GetById(itemTemplateId);
            if (equipmentEntry == null)
                return false;

            equipment = EquipmentFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("equipment", equipmentEntry.FilePath)));
            return true;
        }

        public static bool TryLoadStackableFile(int itemTemplateId, out StackableItemFile stackable)
        {
            return TryLoadStackable(itemTemplateId, out stackable);
        }

        public static string ResolveEquipmentType(int itemTemplateId)
        {
            return TryGetEquipmentType(itemTemplateId, out var equipmentType)
                ? equipmentType
                : null;
        }

        public static string ResolvePvfTypeTag(ItemMetadata metadata)
        {
            if (metadata == null)
                return null;
            return FirstPvfTypeTag(
                string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal)
                    ? metadata.EquipmentType
                    : metadata.StackableType);
        }

        public static string FirstPvfTypeTag(string typeString)
        {
            if (string.IsNullOrWhiteSpace(typeString))
                return null;

            var match = Regex.Match(typeString.Replace("`", "").ToLowerInvariant(), @"\[([a-z ]+)\]");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        public static bool IsAvatarMetadata(ItemMetadata metadata)
        {
            if (metadata == null || !string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal))
                return false;

            if (string.Equals(metadata.ItemCategory?.Trim(), "clear avatar", StringComparison.OrdinalIgnoreCase))
                return true;

            var tag = ResolvePvfTypeTag(metadata);
            if (!string.IsNullOrWhiteSpace(tag) && tag.EndsWith(" avatar", StringComparison.OrdinalIgnoreCase))
                return true;

            var path = metadata.PvfFilePath;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalizedPath = "/" + path.Replace('\\', '/').Trim('/');
            return normalizedPath.IndexOf("/avatar/", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedPath.IndexOf("/at_avatar/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsNameTagMetadata(ItemMetadata metadata)
        {
            return string.Equals(ResolvePvfTypeTag(metadata), "name tag", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPetCreatureMetadata(ItemMetadata metadata)
        {
            return string.Equals(ResolvePvfTypeTag(metadata), "creature", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPetArtifactMetadata(ItemMetadata metadata)
        {
            var tag = ResolvePvfTypeTag(metadata);
            return string.Equals(tag, "artifact red", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "artifact blue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "artifact green", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasPetEquipmentQuality(EquipmentFile equipment)
        {
            if (equipment == null)
                return false;

            return equipment.PhysicalAttack != 0
                || equipment.MagicalAttack != 0
                || equipment.PhysicalDefense != 0
                || equipment.MagicalDefense != 0
                || HasAnyValue(equipment.EquipmentPhysicalAttack)
                || HasAnyValue(equipment.EquipmentMagicalAttack)
                || HasAnyValue(equipment.EquipmentPhysicalDefense)
                || HasAnyValue(equipment.EquipmentMagicalDefense);
        }

        private static bool HasAnyValue(int[] values)
        {
            if (values == null)
                return false;
            foreach (var value in values)
            {
                if (value != 0)
                    return true;
            }
            return false;
        }

        public static bool RequiresManualGrantType(ItemMetadata metadata)
        {
            if (metadata == null || string.Equals(metadata.ItemKind, "special", StringComparison.Ordinal))
                return false;

            if (string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal))
            {
                if (IsAvatarMetadata(metadata))
                    return false;
                return !EquipmentTypeInfo.TryParse(metadata.EquipmentType, out _);
            }

            if (string.Equals(metadata.ItemKind, "stackable", StringComparison.Ordinal))
                return string.IsNullOrWhiteSpace(FirstPvfTypeTag(metadata.StackableType));

            return true;
        }

        public static byte ResolveEmblemSocketType(int itemTemplateId)
        {
            var stackableEntry = StackableList.Value.GetById(itemTemplateId);
            if (stackableEntry == null)
                return 0;

            StackableItemFile stackable;
            try
            {
                stackable = StackableItemFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("stackable", stackableEntry.FilePath)));
            }
            catch (Exception ex)
            {
                FileLogger.Log($"  [EmblemAttach] ResolveEmblemSocketType(0x{itemTemplateId:X8}) failed: {ex.Message}");
                return 0;
            }

            var stackableType = stackable.StackableType != null
                ? stackable.StackableType.Replace("`", "").Trim()
                : string.Empty;
            if (!stackableType.StartsWith("[avatar emblem]", StringComparison.OrdinalIgnoreCase))
                return 0;

            var text = string.Join(" ", new[]
            {
                stackableEntry.FilePath,
                stackable.Name,
                stackable.ItemCategory,
                stackable.ItemGroupName,
                stackable.AttachType,
                stackable.StringData,
                stackable.IntData,
                stackable.Equipment,
                string.Join(" ", stackable.StringDataItems ?? new List<string>()),
            }).ToLowerInvariant();

            byte socketType = 0;
            if (text.Contains("red") || text.Contains("[red]"))
                socketType |= 0x01;
            if (text.Contains("yellow") || text.Contains("[yellow]"))
                socketType |= 0x02;
            if (text.Contains("green") || text.Contains("[green]"))
                socketType |= 0x04;
            if (text.Contains("blue") || text.Contains("[blue]"))
                socketType |= 0x08;
            if (text.Contains("platinum") || text.Contains("[platinum]"))
                socketType |= 0x10;
            if (text.Contains("multi") || text.Contains("all color") || text.Contains("rainbow") || text.Contains("colorful"))
                socketType |= 0x0F;

            return socketType;
        }

        public static IReadOnlyList<byte> ResolveAvatarSocketTypes(int itemTemplateId)
        {
            var result = new List<byte>();
            var equipmentEntry = EquipmentList.Value.GetById(itemTemplateId);
            if (equipmentEntry == null)
                return result;

            try
            {
                var text = PvfArchiveAccessor.ReadText(Path.Combine("equipment", equipmentEntry.FilePath));
                var section = ExtractAvatarTypeSelectSection(text);
                AddAvatarSocketMatches(result, section, 5);

                if (result.Count == 0)
                    AddAvatarSocketMatches(result, ExtractEmblemSocketDefaultSection(text), ResolveAvatarEmblemSocketNum(text));
            }
            catch (Exception ex)
            {
                FileLogger.Log($"  [AvatarSocket] ResolveAvatarSocketTypes(0x{itemTemplateId:X8}) failed: {ex.Message}");
            }

            return result;
        }

        private static void AddAvatarSocketMatches(List<byte> result, string section, int maxCount)
        {
            if (result == null || string.IsNullOrEmpty(section) || maxCount <= 0)
                return;

            foreach (Match match in AvatarSocketRegex.Matches(section))
            {
                if (!match.Success || match.Groups.Count < 2)
                    continue;

                if (TryMapAvatarSocketCode(match.Groups[1].Value[0], out var socketType))
                {
                    result.Add(socketType);
                    if (result.Count >= maxCount)
                        break;
                }
            }
        }

        public static byte ResolveAvatarSocketType(int itemTemplateId)
        {
            var socketTypes = ResolveAvatarSocketTypes(itemTemplateId);
            return socketTypes != null && socketTypes.Count > 0 ? socketTypes[0] : (byte)0;
        }

        private static string ExtractAvatarTypeSelectSection(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var start = text.IndexOf(AvatarTypeSelectTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += AvatarTypeSelectTag.Length;
            var end = text.IndexOf(AvatarTypeSelectEndTag, start, StringComparison.OrdinalIgnoreCase);
            return end > start ? text.Substring(start, end - start) : text.Substring(start);
        }

        private static string ExtractEmblemSocketDefaultSection(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var start = text.IndexOf(EmblemSocketDefaultTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += EmblemSocketDefaultTag.Length;
            var end = text.IndexOf(EmblemSocketDefaultEndTag, start, StringComparison.OrdinalIgnoreCase);
            return end > start ? text.Substring(start, end - start) : text.Substring(start);
        }

        private static int ResolveAvatarEmblemSocketNum(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 5;

            var start = text.IndexOf(AvatarEmblemSocketNumTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return 5;

            start += AvatarEmblemSocketNumTag.Length;
            var end = text.IndexOf('[', start);
            var section = end > start ? text.Substring(start, end - start) : text.Substring(start);
            foreach (var token in section.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, out var count))
                    return Math.Max(0, Math.Min(5, count));
            }

            return 5;
        }

        private static bool TryMapAvatarSocketCode(char code, out byte socketType)
        {
            switch (char.ToUpperInvariant(code))
            {
                case 'A':
                    socketType = 0x01;
                    return true;
                case 'B':
                    socketType = 0x02;
                    return true;
                case 'C':
                    socketType = 0x04;
                    return true;
                case 'D':
                    socketType = 0x08;
                    return true;
                case 'S':
                    socketType = 0x10;
                    return true;
                case 'M':
                    socketType = 0xEF;
                    return true;
                default:
                    socketType = 0;
                    return false;
            }
        }

        public static bool TryValidateEnchantByBeadTarget(int beadItemTemplateId, int targetItemTemplateId, byte enchantUpgradeCount, out int enchantCardItemId, out string rejectReason)
        {
            enchantCardItemId = 0;
            rejectReason = null;

            if (!TryLoadStackable(beadItemTemplateId, out var bead))
            {
                rejectReason = "bead is not found in stackable.lst";
                return false;
            }

            if (bead.MonsterCardId > 0)
                enchantCardItemId = bead.MonsterCardId;
            else if (bead.EnchantIndex > 0)
                enchantCardItemId = bead.EnchantIndex;
            else
            {
                rejectReason = "bead has no monster card id/enchant index";
                return false;
            }

            // 宝珠直接声明 target item id 时，只有白名单装备能被附魔。
            if (bead.TargetItemIds != null && bead.TargetItemIds.Count > 0 && !bead.TargetItemIds.Contains(targetItemTemplateId))
            {
                rejectReason = "target item id is not allowed by bead target item id";
                return false;
            }

            if (!TryGetEquipmentType(targetItemTemplateId, out var targetEquipmentType))
            {
                rejectReason = "target is not found in equipment.lst";
                return false;
            }

            StackableItemFile card = null;
            if (bead.MonsterCardId > 0)
            {
                if (!TryLoadStackable(bead.MonsterCardId, out card))
                {
                    rejectReason = "monster card is not found in stackable.lst";
                    return false;
                }
            }
            else
            {
                TryLoadStackable(enchantCardItemId, out card);
            }

            if (card != null)
            {
                // monster card 的 string data: 第一个是图片资源，后续是允许附魔的 equipment type。
                var allowedTypes = ExtractAllowedEquipmentTypes(card.StringDataItems);
                if (allowedTypes.Count > 0 && !allowedTypes.Contains(targetEquipmentType))
                {
                    rejectReason = "target equipment type is not allowed by monster card string data";
                    return false;
                }

                if (card.EnchantTable.Count > 0 && !card.EnchantTable.Contains(enchantUpgradeCount))
                {
                    rejectReason = "enchant upgrade count is not allowed by monster card enchant table";
                    return false;
                }

                if (card.EnchantTable.Count == 0 && enchantUpgradeCount != 0)
                {
                    rejectReason = "monster card has no enchant table for upgraded bead";
                    return false;
                }
            }

            if (card == null && enchantUpgradeCount != 0)
            {
                rejectReason = "upgraded enchant bead requires monster card enchant table";
                return false;
            }

            return true;
        }

        private static bool TryLoadStackable(int itemTemplateId, out StackableItemFile stackable)
        {
            stackable = null;
            var stackableEntry = StackableList.Value.GetById(itemTemplateId);
            if (stackableEntry == null)
                return false;

            stackable = StackableItemFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("stackable", stackableEntry.FilePath)));
            return true;
        }

        private static bool TryGetEquipmentType(int itemTemplateId, out string equipmentType)
        {
            equipmentType = null;
            var equipmentEntry = EquipmentList.Value.GetById(itemTemplateId);
            if (equipmentEntry == null)
                return false;

            var equipment = EquipmentFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("equipment", equipmentEntry.FilePath)));
            equipmentType = NormalizeEquipmentType(equipment.EquipmentType);
            return !string.IsNullOrWhiteSpace(equipmentType);
        }

        private static HashSet<string> ExtractAllowedEquipmentTypes(List<string> stringDataItems)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (stringDataItems == null || stringDataItems.Count <= 1)
                return result;

            for (var i = 1; i < stringDataItems.Count; i++)
            {
                var normalized = NormalizeEquipmentType(stringDataItems[i]);
                if (!string.IsNullOrWhiteSpace(normalized))
                    result.Add(normalized);
            }

            return result;
        }

        private static bool HasDurabilityByType(string normalizedType)
        {
            if (string.IsNullOrEmpty(normalizedType))
                return false;
            // 武器
            if (normalizedType == "[weapon]" || normalizedType == "[support weapon]")
                return true;
            // 防具
            if (normalizedType == "[coat]" || normalizedType == "[pants]"
                || normalizedType == "[shoulder]" || normalizedType == "[shoes]"
                || normalizedType == "[waist]")
                return true;
            return false;
        }

        // 全部修理("一键修理")适用的装备类型 (客户端 handler 过滤一致)。
        // 只修这 13 类穿戴装备; 
        private static readonly HashSet<string> RepairAllEquipmentTypes = new HashSet<string>
        {
            "[weapon]", "[coat]", "[pants]", "[hat]", "[shoulder]", "[waist]",
            "[shoes]", "[amulet]", "[wrist]", "[ring]", "[support]",
            "[aurora avatar]", "[magic stone]",
        };

        // itemTemplateId 是否属于"全部修理"适用的装备类型。
        public static bool IsRepairAllEligible(int itemTemplateId)
        {
            return TryGetEquipmentType(itemTemplateId, out var type)
                && type != null
                && RepairAllEquipmentTypes.Contains(type);
        }

        private static string NormalizeEquipmentType(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var start = raw.IndexOf('[', StringComparison.Ordinal);
            var end = start >= 0 ? raw.IndexOf(']', start + 1) : -1;
            if (start < 0 || end <= start)
                return raw.Trim('`', ' ', '\t', '\r', '\n').ToLowerInvariant();

            return raw.Substring(start, end - start + 1).ToLowerInvariant();
        }

        /// <summary>
        /// 判断物品是否为克隆装扮。克隆装扮的 PVF [item category] 段值为 "clear avatar"。
        /// </summary>
        public static bool IsCloneAvatarItem(int itemTemplateId)
        {
            var equipmentEntry = EquipmentList.Value.GetById(itemTemplateId);
            if (equipmentEntry == null) return false;
            var equipment = EquipmentFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("equipment", equipmentEntry.FilePath)));
            return string.Equals(equipment.ItemCategory, "clear avatar", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNameTagItem(int itemTemplateId)
        {
            var meta = Resolve(itemTemplateId);
            return meta != null
                && string.Equals(meta.ItemKind, "equipment", StringComparison.Ordinal)
                && string.Equals(meta.EquipmentType, "[name tag]", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPetInventoryEquipment(int itemTemplateId)
        {
            return CreatureExtraResolver.IsPetInventoryEquipment(itemTemplateId);
        }

        public static bool IsPetConsumableItem(ItemMetadata metadata)
        {
            if (metadata == null || !metadata.IsStackable || string.IsNullOrWhiteSpace(metadata.StackableType))
                return false;
            var stackableType = metadata.StackableType.Replace("`", "").Trim();
            return stackableType.StartsWith("[creature]", StringComparison.OrdinalIgnoreCase)
                || stackableType.StartsWith("[feed]", StringComparison.OrdinalIgnoreCase);
        }
    }
}
