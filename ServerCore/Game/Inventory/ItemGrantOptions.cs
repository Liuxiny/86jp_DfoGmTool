using System;
using System.Collections.Generic;
using DfoGmTool.ServerCore.Game.Skills;
using DfoGmTool.ServerCore.Game.ItemUpgrade;
using GmPvfLib;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed class ItemGrantOptions
    {
        public ItemQualityMode QualityMode { get; set; } = ItemQualityMode.Top;

        public int UpgradeLevel { get; set; }

        public int AmplifyType { get; set; }

        public int ForgingLevel { get; set; }

        public int? AvatarOptionValue { get; set; }

        public int? ExpirationDays { get; set; }

        public string ManualGrantType { get; set; }
    }

    public sealed class EquipmentGrantCapability
    {
        public bool IsEquipment { get; set; }

        public bool CanUpgrade { get; set; }

        public bool CanAmplify { get; set; }

        public bool CanForge { get; set; }

        public int MaxUpgradeLevel { get; set; }

        public int MaxForgingLevel { get; set; }
    }

    internal static class EquipmentGrantPolicy
    {
        internal const int MaximumUpgradeLevel = 31;
        internal const int MaximumForgingLevel = 8;

        internal static EquipmentGrantCapability Describe(ItemMetadata metadata)
        {
            var type = EquipmentTypeInfo.ParseOrUnknown(metadata?.EquipmentType);
            var isEquipment = metadata != null
                && string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal);
            var upgradeType = isEquipment && EquipmentTypeInfo.IsUpgradeTargetType(type);
            var impossible = metadata?.ImpossibleContents ?? Array.Empty<string>();
            var canUpgrade = upgradeType && !ContainsImpossible(impossible, "upgrade");
            var canAmplify = upgradeType
                && metadata.MinimumLevel >= 55
                && metadata.Rarity >= 2
                && !ContainsImpossible(impossible, "amplify upgrade");

            return new EquipmentGrantCapability
            {
                IsEquipment = isEquipment,
                CanUpgrade = canUpgrade,
                CanAmplify = canAmplify,
                CanForge = upgradeType && EquipmentTypeInfo.IsWeapon(type),
                MaxUpgradeLevel = MaximumUpgradeLevel,
                MaxForgingLevel = MaximumForgingLevel,
            };
        }

        internal static bool TryBuildExtraJson(
            ItemMetadata metadata,
            ItemGrantOptions options,
            Func<int, ushort> resolveInitialAmplifyValue,
            out string extraJson,
            out string error)
        {
            options ??= new ItemGrantOptions();
            extraJson = null;
            error = null;
            var builder = new ItemExtraViewBuilder();
            if (!TryApplyToBuilder(metadata, options, resolveInitialAmplifyValue, builder.Equipment, out error))
                return false;

            extraJson = builder.Build().Serialize();
            return true;
        }

        internal static bool TryApplyToBuilder(
            ItemMetadata metadata,
            ItemGrantOptions options,
            Func<int, ushort> resolveInitialAmplifyValue,
            EquipmentExtraViewBuilder builder,
            out string error)
        {
            options ??= new ItemGrantOptions();
            error = null;
            var capability = Describe(metadata);
            if (!capability.IsEquipment)
            {
                error = "目标不是装备";
                return false;
            }

            if (!Enum.IsDefined(typeof(ItemQualityMode), options.QualityMode))
            {
                error = "装备品级选项无效";
                return false;
            }

            if (options.UpgradeLevel < 0 || options.UpgradeLevel > MaximumUpgradeLevel)
            {
                error = "强化/增幅等级必须在 0-31 之间";
                return false;
            }
            if (options.UpgradeLevel > 0 && !capability.CanUpgrade && options.AmplifyType == 0)
            {
                error = "该装备不支持强化";
                return false;
            }

            if (options.AmplifyType < 0 || options.AmplifyType > 4)
            {
                error = "红字属性类型无效";
                return false;
            }
            if (options.AmplifyType > 0 && !capability.CanAmplify)
            {
                error = "只有 55 级及以上紫色及以上的可升级装备可以添加红字";
                return false;
            }

            if (options.ForgingLevel < 0 || options.ForgingLevel > MaximumForgingLevel)
            {
                error = "锻造等级必须在 0-8 之间";
                return false;
            }
            if (options.ForgingLevel > 0 && !capability.CanForge)
            {
                error = "只有武器可以锻造";
                return false;
            }

            if (builder == null)
            {
                error = "装备属性写入器无效";
                return false;
            }

            builder.Upgrade = (byte)options.UpgradeLevel;
            builder.Forging = (byte)options.ForgingLevel;
            builder.AmplifyType = (byte)options.AmplifyType;
            builder.AmplifyValue = 0;
            if (options.AmplifyType > 0)
            {
                var initial = resolveInitialAmplifyValue?.Invoke(metadata.Rarity) ?? (ushort)0;
                if (initial == 0)
                {
                    error = "无法从 PVF 计算红字初始值";
                    return false;
                }
                builder.AmplifyValue = initial;
            }
            return true;
        }

        internal static string GetAmplifyTypeLabel(int type)
        {
            return type switch
            {
                1 => "体力",
                2 => "精神",
                3 => "力量",
                4 => "智力",
                _ => "无",
            };
        }

        private static bool ContainsImpossible(IReadOnlyList<string> values, string expected)
        {
            if (values == null)
                return false;

            foreach (var value in values)
            {
                var normalized = (value ?? string.Empty).Trim().Trim('`', '[', ']').Trim();
                if (string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    public sealed class AvatarSkillOption
    {
        public AvatarSkillOption(int value, string label)
        {
            Value = value;
            Label = label;
        }

        public int Value { get; }

        public string Label { get; }
    }

    public sealed class AvatarGrantOption
    {
        public AvatarGrantOption(int value, string label, bool isSkill = false)
        {
            Value = value;
            Label = label;
            IsSkill = isSkill;
        }

        public int Value { get; }

        public string Label { get; }

        public bool IsSkill { get; }
    }

    internal static class AvatarGrantPolicy
    {
        private static readonly IReadOnlyDictionary<int, string[]> JobTokens =
            new Dictionary<int, string[]>
            {
                [0] = new[] { "swordman" },
                [1] = new[] { "fighter" },
                [2] = new[] { "gunner" },
                [3] = new[] { "mage" },
                [4] = new[] { "priest" },
                [5] = new[] { "atgunner" },
                [6] = new[] { "thief" },
                [7] = new[] { "atfighter" },
                [8] = new[] { "atmage" },
                [9] = new[] { "demonicswordman" },
                [10] = new[] { "creatormage" },
                [11] = new[] { "atswordman" },
                [12] = new[] { "knight" },
            };

        internal static bool IsUsableByJob(string usableJob, int job)
        {
            var normalized = (usableJob ?? string.Empty)
                .Trim()
                .Trim('`')
                .ToLowerInvariant()
                .Replace("`", string.Empty)
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty)
                .Replace("\t", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);
            if (normalized.Contains("[all]", StringComparison.Ordinal))
                return true;
            if (!JobTokens.TryGetValue(job, out var expected))
                return false;
            foreach (var token in expected)
            {
                if (normalized.Contains("[" + token + "]", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal static List<AvatarGrantOption> ResolveOptions(
            string equipmentType,
            int grade,
            IReadOnlyList<AvatarSelectAbilityEntry> selectAbilities,
            int job,
            int abilityCaseIndex)
        {
            var part = NormalizeToken(equipmentType);
            if (grade <= 0)
                return DefaultOnly();

            if (part == "coat avatar")
            {
                var coatOptions = AvatarAbilityDataProvider.ResolveCoatOptions(abilityCaseIndex, job);
                return coatOptions.Count > 0 ? coatOptions : DefaultOnly();
            }

            if (selectAbilities != null && selectAbilities.Count > 0)
            {
                var exact = ResolveSelectAbilityOptions(selectAbilities, job);
                if (exact.Count > 0)
                    return exact;
            }

            return DefaultOnly();
        }

        internal static bool ContainsValue(IReadOnlyList<AvatarGrantOption> options, int value)
        {
            if (options == null)
                return false;
            foreach (var option in options)
            {
                if (option.Value == value)
                    return true;
            }
            return false;
        }

        private static List<AvatarGrantOption> ResolveSelectAbilityOptions(
            IReadOnlyList<AvatarSelectAbilityEntry> selectAbilities,
            int job)
        {
            var result = new List<AvatarGrantOption>();
            foreach (var entry in selectAbilities)
            {
                if (entry == null || entry.OptionValue < 0 || entry.OptionValue > byte.MaxValue)
                    continue;

                var label = AvatarAbilityDataProvider.BuildSelectAbilityLabel(entry, job, out var isSkill);
                if (!string.IsNullOrWhiteSpace(label))
                    result.Add(new AvatarGrantOption(entry.OptionValue, label, isSkill));
            }
            result.Sort((left, right) => left.Value.CompareTo(right.Value));
            return result;
        }

        private static List<AvatarGrantOption> DefaultOnly()
        {
            return new List<AvatarGrantOption> { new AvatarGrantOption(0, "默认") };
        }

        private static string NormalizeToken(string value)
        {
            var text = (value ?? string.Empty).Trim().Trim('`').Trim().ToLowerInvariant();
            var start = text.IndexOf('[', StringComparison.Ordinal);
            var end = start >= 0 ? text.IndexOf(']', start + 1) : -1;
            return start >= 0 && end > start
                ? text.Substring(start + 1, end - start - 1).Trim().Replace("_", string.Empty)
                : text.Replace("_", string.Empty);
        }
    }

    public sealed class ItemGrantExpirationCapability
    {
        public bool IsLimited { get; set; }

        public bool CanOverride { get; set; }

        public int DefaultExpireTime { get; set; }

        public bool IsExpired { get; set; }
    }

    internal static class ItemGrantExpirationOverride
    {
        internal const int MaximumDays = 3650;

        internal static bool TryResolve(
            ItemGrantExpirationCapability capability,
            int days,
            long now,
            out int expireTime,
            out string error)
        {
            expireTime = 0;
            error = null;
            if (capability == null || !capability.IsLimited || !capability.CanOverride)
            {
                error = "该物品不支持自定义期限";
                return false;
            }
            if (days <= 0 || days > MaximumDays)
            {
                error = "期限天数必须在 1-3650 之间";
                return false;
            }

            var value = now + days * 86400L;
            if (value <= 0 || value > int.MaxValue)
            {
                error = "期限超出服务端可存储范围";
                return false;
            }

            expireTime = (int)value;
            return true;
        }
    }
}
