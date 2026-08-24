using System;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    // A21 slot truth shared by grant, pickup, read validation and UI projections.
    internal static class A21InventorySlotPolicy
    {
        internal const int MainExpandStageNone = 0;
        internal const int MainExpandStage1 = 8;
        internal const int MainExpandStage2 = 16;
        internal const int MainExpandStageFull = 24;

        internal const int MainQuickSlotStart = 3;
        internal const int MainQuickSlotEnd = 8;
        internal const int MainEquipmentSlotStart = 9;
        internal const int MainEquipmentSlotEnd = 64;
        internal const int MainConsumableSlotStart = 65;
        internal const int MainConsumableSlotEnd = 120;
        internal const int MainMaterialSlotStart = 121;
        internal const int MainMaterialSlotEnd = 176;
        internal const int MainQuestSlotStart = 177;
        internal const int MainQuestSlotEnd = 232;
        internal const int MainExpertSlotStart = 233;
        internal const int MainExpertSlotEnd = 288;
        internal const int MainAvatarEmblemSlotStart = 289;
        internal const int MainAvatarEmblemSlotEnd = 351;

        internal const int AvatarSlotStart = 0;
        internal const int AvatarSlotEnd = 209;
        internal const int PetCreatureSlotStart = 0;
        internal const int PetCreatureSlotEnd = 139;
        internal const int PetEquipmentSlotStart = 140;
        internal const int PetEquipmentSlotEnd = 188;
        internal const int PetConsumableSlotStart = 189;
        internal const int PetConsumableSlotEnd = 239;
        internal const int GuildMedalSlotStart = 0;
        internal const int GuildMedalSlotEnd = 48;
        internal const int GuardianGemSlotStart = 49;
        internal const int GuardianGemSlotEnd = 97;

        internal const int PersonalCargoSlotStart = 0;
        internal const int PersonalCargoSlotEnd = 199;
        internal const int PersonalCargoDefaultCapacity = 8;
        internal const int AccountCargoSlotStart = 0;
        internal const int AccountCargoSlotEnd = 119;

        internal static bool TryGetRange(
            byte itemKind,
            out InventoryListType listType,
            out short start,
            out short end)
        {
            listType = InventoryListType.Main;
            start = end = 0;
            switch (itemKind)
            {
                case ItemCore.KindUnknown:
                    start = MainQuickSlotStart;
                    end = MainQuickSlotEnd;
                    return true;
                case ItemCore.KindEquipment:
                    start = MainEquipmentSlotStart;
                    end = MainEquipmentSlotEnd;
                    return true;
                case ItemCore.KindConsumable:
                    start = MainConsumableSlotStart;
                    end = MainConsumableSlotEnd;
                    return true;
                case ItemCore.KindMaterial:
                    start = MainMaterialSlotStart;
                    end = MainMaterialSlotEnd;
                    return true;
                case ItemCore.KindQuest:
                    start = MainQuestSlotStart;
                    end = MainQuestSlotEnd;
                    return true;
                case ItemCore.KindExpertJobMaterial:
                    start = MainExpertSlotStart;
                    end = MainExpertSlotEnd;
                    return true;
                case ItemCore.KindAvatarEmblem:
                    start = MainAvatarEmblemSlotStart;
                    end = MainAvatarEmblemSlotEnd;
                    return true;
                case ItemCore.KindAvatar:
                    listType = InventoryListType.Avatar;
                    start = AvatarSlotStart;
                    end = AvatarSlotEnd;
                    return true;
                case ItemCore.KindCreature:
                    listType = InventoryListType.Pet;
                    start = PetCreatureSlotStart;
                    end = PetCreatureSlotEnd;
                    return true;
                case ItemCore.KindCreatureEquipment:
                    listType = InventoryListType.Pet;
                    start = PetEquipmentSlotStart;
                    end = PetEquipmentSlotEnd;
                    return true;
                case ItemCore.KindCreatureConsumable:
                    listType = InventoryListType.Pet;
                    start = PetConsumableSlotStart;
                    end = PetConsumableSlotEnd;
                    return true;
                case ItemCore.KindGuildMedal:
                    listType = InventoryListType.GuildMedal;
                    start = GuildMedalSlotStart;
                    end = GuildMedalSlotEnd;
                    return true;
                case ItemCore.KindGuardianGem:
                    listType = InventoryListType.GuildMedal;
                    start = GuardianGemSlotStart;
                    end = GuardianGemSlotEnd;
                    return true;
                default:
                    return false;
            }
        }

        internal static bool TryGetMainRange(byte itemKind, int expandStage, out short start, out short end)
        {
            start = end = 0;
            if (!TryGetRange(itemKind, out var listType, out start, out end)
                || listType != InventoryListType.Main)
                return false;
            if (itemKind == ItemCore.KindAvatarEmblem)
                return true;
            if (itemKind == ItemCore.KindUnknown)
                return true;
            if (!TryNormalizeMainExpandStage(expandStage, out var normalizedStage))
                return false;
            if (itemKind != ItemCore.KindEquipment
                && itemKind != ItemCore.KindConsumable
                && itemKind != ItemCore.KindMaterial
                && itemKind != ItemCore.KindQuest
                && itemKind != ItemCore.KindExpertJobMaterial)
                return false;
            end = checked((short)(end - (MainExpandStageFull - normalizedStage)));
            return end >= start;
        }

        internal static bool TryNormalizeMainExpandStage(int value, out int normalized)
        {
            switch (value)
            {
                case MainExpandStageNone:
                case MainExpandStage1:
                case MainExpandStage2:
                case MainExpandStageFull:
                    normalized = value;
                    return true;
                default:
                    normalized = MainExpandStageNone;
                    return false;
            }
        }

        internal static bool TryGetEquipmentBodyKind(int slot, out byte itemKind)
        {
            if (slot >= 0 && slot <= 11)
                itemKind = ItemCore.KindAvatar;
            else if (slot == 25)
                itemKind = ItemCore.KindCreature;
            else if (slot >= 26 && slot <= 28)
                itemKind = ItemCore.KindCreatureEquipment;
            else if (slot == 31)
                itemKind = ItemCore.KindGuildMedal;
            else if (slot >= 12 && slot <= 24 || slot == 30)
                itemKind = ItemCore.KindEquipment;
            else
            {
                itemKind = ItemCore.KindUnknown;
                return false;
            }

            return true;
        }

        internal static bool TryGetPetKind(int slot, out byte itemKind)
        {
            if (slot >= PetCreatureSlotStart && slot <= PetCreatureSlotEnd)
                itemKind = ItemCore.KindCreature;
            else if (slot >= PetEquipmentSlotStart && slot <= PetEquipmentSlotEnd)
                itemKind = ItemCore.KindCreatureEquipment;
            else if (slot >= PetConsumableSlotStart && slot <= PetConsumableSlotEnd)
                itemKind = ItemCore.KindCreatureConsumable;
            else
            {
                itemKind = ItemCore.KindUnknown;
                return false;
            }

            return true;
        }

        internal static bool IsPetConsumableSlot(int slot)
        {
            return slot >= PetConsumableSlotStart && slot <= PetConsumableSlotEnd;
        }

        internal static bool TryGetGuildKind(int slot, out byte itemKind)
        {
            if (slot >= GuildMedalSlotStart && slot <= GuildMedalSlotEnd)
                itemKind = ItemCore.KindGuildMedal;
            else if (slot >= GuardianGemSlotStart && slot <= GuardianGemSlotEnd)
                itemKind = ItemCore.KindGuardianGem;
            else
            {
                itemKind = ItemCore.KindUnknown;
                return false;
            }

            return true;
        }

        internal static bool IsValidSlotForKind(
            byte itemKind,
            InventoryListType listType,
            int slot,
            int mainExpandStage)
        {
            if (listType == InventoryListType.Equipment)
                return TryGetEquipmentBodyKind(slot, out var bodyKind) && bodyKind == itemKind;
            if (listType == InventoryListType.Pet)
                return TryGetPetKind(slot, out var petKind) && petKind == itemKind;
            if (listType == InventoryListType.GuildMedal)
                return TryGetGuildKind(slot, out var guildKind) && guildKind == itemKind;
            if (listType == InventoryListType.Avatar)
                return itemKind == ItemCore.KindAvatar
                    && slot >= AvatarSlotStart && slot <= AvatarSlotEnd;
            return TryGetMainRange(itemKind, mainExpandStage, out var start, out var end)
                && listType == InventoryListType.Main
                && slot >= start && slot <= end;
        }

        internal static int NormalizePersonalCapacity(int value)
        {
            if (value <= 0)
                return PersonalCargoDefaultCapacity;
            return Math.Min(value, PersonalCargoSlotEnd - PersonalCargoSlotStart + 1);
        }

        internal static int NormalizeAccountCapacity(int value)
        {
            return Math.Max(AccountCargoSlotStart, Math.Min(value, AccountCargoSlotEnd - AccountCargoSlotStart + 1));
        }

        internal static string GetEquipmentCategory(int slot)
        {
            if (slot >= 0 && slot <= 11) return "穿戴装扮";
            if (slot >= 12 && slot <= 24) return "穿戴装备";
            if (slot >= 26 && slot <= 28) return "穿戴宠物装备";
            if (slot == 25) return "穿戴宠物";
            if (slot == 29) return "名称装饰状态";
            if (slot == 30) return "穿戴符咒";
            if (slot == 31) return "穿戴勋章";
            return "其他穿戴槽";
        }
    }
}
