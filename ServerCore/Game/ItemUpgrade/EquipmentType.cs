using System;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.ItemUpgrade
{
    public enum EquipmentType
    {
        HatAvatar = 0,
        HairAvatar = 1,
        FaceAvatar = 2,
        CoatAvatar = 3,
        PantsAvatar = 4,
        ShoesAvatar = 5,
        BreastAvatar = 6,
        WaistAvatar = 7,
        SkinAvatar = 8,
        AuroraAvatar = 9,
        WeaponAvatar = 10,
        AuraSkinAvatar = 11,
        AuroraIllusionAvatar = AuraSkinAvatar,
        Weapon = 12,
        TitleName = 13,
        Coat = 14,
        Shoulder = 15,
        Pants = 16,
        Shoes = 17,
        Waist = 18,
        Amulet = 19,
        Wrist = 20,
        Ring = 21,
        Support = 22,
        MagicStone = 23,
        SupportWeapon = 24,
        Creature = 25,
        ArtifactRed = 26,
        ArtifactBlue = 27,
        ArtifactGreen = 28,
        NameTag = 29,
        Charm = 30,
        GuildMedal = 31,
        Flag = GuildMedal,
        Unknown = -1,
    }

    public static class EquipmentTypeInfo
    {
        private static readonly Dictionary<string, EquipmentType> TextToType = new Dictionary<string, EquipmentType>(StringComparer.OrdinalIgnoreCase)
        {
            ["[hat avatar]"] = EquipmentType.HatAvatar,
            ["[hair avatar]"] = EquipmentType.HairAvatar,
            ["[face avatar]"] = EquipmentType.FaceAvatar,
            ["[coat avatar]"] = EquipmentType.CoatAvatar,
            ["[pants avatar]"] = EquipmentType.PantsAvatar,
            ["[shoes avatar]"] = EquipmentType.ShoesAvatar,
            ["[breast avatar]"] = EquipmentType.BreastAvatar,
            ["[waist avatar]"] = EquipmentType.WaistAvatar,
            ["[skin avatar]"] = EquipmentType.SkinAvatar,
            ["[aurora avatar]"] = EquipmentType.AuroraAvatar,
            ["[weapon avatar]"] = EquipmentType.WeaponAvatar,
            ["[aura skin avatar]"] = EquipmentType.AuraSkinAvatar,
            ["[aurora illusion avatar]"] = EquipmentType.AuraSkinAvatar,
            ["[aurora skin avatar]"] = EquipmentType.AuraSkinAvatar,
            ["[aurora change avatar]"] = EquipmentType.AuraSkinAvatar,
            ["[weapon]"] = EquipmentType.Weapon,
            ["[title name]"] = EquipmentType.TitleName,
            ["[coat]"] = EquipmentType.Coat,
            ["[shoulder]"] = EquipmentType.Shoulder,
            ["[pants]"] = EquipmentType.Pants,
            ["[shoes]"] = EquipmentType.Shoes,
            ["[waist]"] = EquipmentType.Waist,
            ["[amulet]"] = EquipmentType.Amulet,
            ["[wrist]"] = EquipmentType.Wrist,
            ["[ring]"] = EquipmentType.Ring,
            ["[support]"] = EquipmentType.Support,
            ["[magic stone]"] = EquipmentType.MagicStone,
            ["[support weapon]"] = EquipmentType.SupportWeapon,
            ["[creature]"] = EquipmentType.Creature,
            ["[artifact red]"] = EquipmentType.ArtifactRed,
            ["[artifact blue]"] = EquipmentType.ArtifactBlue,
            ["[artifact green]"] = EquipmentType.ArtifactGreen,
            ["[name tag]"] = EquipmentType.NameTag,
            ["[charm]"] = EquipmentType.Charm,
            ["[flag]"] = EquipmentType.GuildMedal,
        };

        private static readonly Dictionary<EquipmentType, string> TypeToText = BuildReverseMap();

        public static bool TryParse(string raw, out EquipmentType type)
        {
            type = EquipmentType.Unknown;
            var token = NormalizeToken(raw);
            return !string.IsNullOrEmpty(token) && TextToType.TryGetValue(token, out type);
        }

        public static EquipmentType ParseOrUnknown(string raw)
        {
            return TryParse(raw, out var type) ? type : EquipmentType.Unknown;
        }

        public static string ToPvfToken(EquipmentType type)
        {
            return TypeToText.TryGetValue(type, out var token) ? token : null;
        }

        // PVF/旧外观编码中的 200-230 使用插槽前的编号；A21 在槽 11 插入光环皮肤。
        public static int ToA21AppearanceSlot(int slot)
        {
            if (slot >= 200 && slot <= 230)
            {
                var encodedSlot = slot - 200;
                return encodedSlot >= 11 ? encodedSlot + 1 : encodedSlot;
            }

            return slot;
        }

        public static bool IsA21RosterAppearanceSlot(int slot)
        {
            return slot >= (short)EquipmentType.HatAvatar
                && slot <= (short)EquipmentType.TitleName;
        }

        public static bool IsA21Noti2EquippedSlot(int slot)
        {
            return (slot >= (short)EquipmentType.HatAvatar
                    && slot <= (short)EquipmentType.ArtifactGreen)
                || slot == (short)EquipmentType.GuildMedal;
        }

        public static bool IsCostumeBarSlot(short slot)
        {
            return slot >= (short)EquipmentType.HatAvatar
                && slot <= (short)EquipmentType.AuraSkinAvatar;
        }

        public static bool IsAvatarPart(EquipmentType type)
        {
            return type >= EquipmentType.HatAvatar
                && type <= EquipmentType.AuraSkinAvatar;
        }

        public static bool IsWeapon(EquipmentType type)
        {
            return type == EquipmentType.Weapon;
        }

        public static bool IsArmor(EquipmentType type)
        {
            return type == EquipmentType.Coat
                || type == EquipmentType.Shoulder
                || type == EquipmentType.Pants
                || type == EquipmentType.Shoes
                || type == EquipmentType.Waist;
        }

        public static bool IsAccessory(EquipmentType type)
        {
            return type == EquipmentType.Amulet
                || type == EquipmentType.Wrist
                || type == EquipmentType.Ring;
        }

        public static bool IsSpecialEquipment(EquipmentType type)
        {
            return type == EquipmentType.Support || type == EquipmentType.MagicStone;
        }

        public static bool IsUpgradeTargetType(EquipmentType type)
        {
            return IsWeapon(type) || IsArmor(type) || IsAccessory(type) || IsSpecialEquipment(type);
        }

        public static bool MatchesSlotRestriction(EquipmentType type, int slotRestriction)
        {
            switch (slotRestriction)
            {
                case 0:
                    return true;
                case 1:
                    return IsWeapon(type);
                case 2:
                    return IsArmor(type);
                case 3:
                    return IsAccessory(type);
                default:
                    return false;
            }
        }

        private static string NormalizeToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var text = raw.Trim().Trim('`').Trim().ToLowerInvariant();
            var start = text.IndexOf('[', StringComparison.Ordinal);
            var end = start >= 0 ? text.IndexOf(']', start + 1) : -1;
            if (start >= 0 && end > start)
                return text.Substring(start, end - start + 1);

            return text;
        }

        private static Dictionary<EquipmentType, string> BuildReverseMap()
        {
            var map = new Dictionary<EquipmentType, string>();
            foreach (var pair in TextToType)
            {
                if (!map.ContainsKey(pair.Value))
                    map[pair.Value] = pair.Key;
            }

            return map;
        }
    }
}
