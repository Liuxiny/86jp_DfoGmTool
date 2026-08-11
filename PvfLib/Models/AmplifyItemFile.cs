using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GmPvfLib
{
    public sealed class AmplifyItemFile : PvfModelBase
    {
        public Dictionary<string, double> RarityWeights { get; set; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> OptionMappingTable { get; set; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public List<int> AmplificationRatesByRarity { get; set; } = new List<int>();

        public double AmplificationWeightByLevel { get; set; }

        public int EquipLevelConst { get; set; } = 55;

        public List<double> BonusWeightsByEquipmentType { get; set; } = new List<double>();

        public Dictionary<int, int> PurifyMaterials { get; set; } = new Dictionary<int, int>();

        public Dictionary<int, int> PurifyOnlyMaterials { get; set; } = new Dictionary<int, int>();

        public Dictionary<int, int> PurifyOnlyCeraMaterials { get; set; } = new Dictionary<int, int>();

        public List<AmplifyMaterialOption> InvestOptions { get; set; } = new List<AmplifyMaterialOption>();

        public List<AmplifyMaterialOption> ReinvestOptions { get; set; } = new List<AmplifyMaterialOption>();

        public List<AmplifyMaterialOption> RandomInvestUpgradeOptions { get; set; } = new List<AmplifyMaterialOption>();

        public List<AmplifyOptionData> OptionData { get; set; } = new List<AmplifyOptionData>();

        public double GetBaseValue(AmplifyOptionType optionType)
        {
            foreach (var option in OptionData)
            {
                if (option.OptionType == optionType)
                    return option.BaseValue;
            }

            return 0;
        }

        public static AmplifyItemFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new AmplifyItemFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var file = new AmplifyItemFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                if (string.Equals(node.Tag, "option mapping table", StringComparison.OrdinalIgnoreCase))
                    file.OptionMappingTable = ParseOptionMappingTable(node, content);
            }

            foreach (var node in root.Children)
            {
                switch (node.Tag.ToLowerInvariant())
                {
                    case "option mapping table":
                        break;
                    case "amplification rate by rarity":
                        file.AmplificationRatesByRarity = ParseIntList(node, content);
                        break;
                    case "amplification weight by level":
                        file.AmplificationWeightByLevel = ParseFirstDouble(node, content);
                        break;
                    case "rarity weight":
                        file.RarityWeights = ParseNameDoubleMap(node, content);
                        break;
                    case "equip level const":
                        file.EquipLevelConst = ParseFirstInt(node, content, file.EquipLevelConst);
                        break;
                    case "bonus weight by equipment type":
                        file.BonusWeightsByEquipmentType = ParseDoubleList(node, content);
                        break;
                    case "option data":
                        file.OptionData = ParseOptionData(node, content, file.OptionMappingTable);
                        break;
                    case "purify material":
                        AddItemCountMap(file.PurifyMaterials, node, content);
                        break;
                    case "purify only material":
                        AddItemCountMap(file.PurifyOnlyMaterials, node, content);
                        break;
                    case "purify only cera material":
                        AddItemCountMap(file.PurifyOnlyCeraMaterials, node, content);
                        break;
                    case "invest option":
                        file.InvestOptions.AddRange(ParseMaterialOptions(node, content, file.OptionMappingTable));
                        break;
                    case "reinvest option":
                        file.ReinvestOptions.AddRange(ParseMaterialOptions(node, content, file.OptionMappingTable));
                        break;
                    case "random invest upgrade option":
                        file.RandomInvestUpgradeOptions.AddRange(ParseMaterialOptions(node, content, file.OptionMappingTable));
                        break;
                }
            }

            return file;
        }

        private static List<AmplifyOptionData> ParseOptionData(ScriptNode node, string content, Dictionary<string, int> optionMappingTable)
        {
            var result = new List<AmplifyOptionData>();
            var tokens = ReadTokens(node, content);
            double cumulativeWeight = 0;
            for (var i = 0; i + 2 < tokens.Count; i += 3)
            {
                if (!TryParseDouble(tokens[i + 1], out var weight)
                    || !TryParseDouble(tokens[i + 2], out var baseValue))
                    continue;

                cumulativeWeight += weight;
                result.Add(new AmplifyOptionData
                {
                    OptionType = ParseOptionType(tokens[i], optionMappingTable),
                    CumulativeWeight = cumulativeWeight,
                    BaseValue = baseValue,
                });
            }

            return result;
        }

        private static List<AmplifyMaterialOption> ParseMaterialOptions(ScriptNode node, string content, Dictionary<string, int> optionMappingTable)
        {
            var result = new List<AmplifyMaterialOption>();
            var tokens = ReadTokens(node, content);
            for (var i = 0; i + 2 < tokens.Count; i += 3)
            {
                if (!int.TryParse(tokens[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                    || !int.TryParse(tokens[i + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                    continue;

                result.Add(new AmplifyMaterialOption
                {
                    OptionType = ParseOptionType(tokens[i], optionMappingTable),
                    ItemId = itemId,
                    Count = count,
                });
            }

            return result;
        }

        private static void AddItemCountMap(Dictionary<int, int> target, ScriptNode node, string content)
        {
            foreach (var pair in ParseItemCountMap(node, content))
                target[pair.Key] = pair.Value;
        }

        private static Dictionary<int, int> ParseItemCountMap(ScriptNode node, string content)
        {
            var result = new Dictionary<int, int>();
            var tokens = ReadTokens(node, content);
            for (var i = 0; i + 1 < tokens.Count; i += 2)
            {
                if (!int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                    || !int.TryParse(tokens[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                    continue;

                result[itemId] = count;
            }

            return result;
        }

        private static Dictionary<string, int> ParseOptionMappingTable(ScriptNode node, string content)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var tokens = ReadTokens(node, content);
            for (var i = 0; i + 1 < tokens.Count; i += 2)
            {
                if (!int.TryParse(tokens[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    continue;

                result[NormalizeName(tokens[i])] = value;
            }

            return result;
        }

        private static List<int> ParseIntList(ScriptNode node, string content)
        {
            var result = new List<int>();
            foreach (var token in ReadTokens(node, content))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    result.Add(value);
            }

            return result;
        }

        private static List<double> ParseDoubleList(ScriptNode node, string content)
        {
            var result = new List<double>();
            foreach (var token in ReadTokens(node, content))
            {
                if (TryParseDouble(token, out var value))
                    result.Add(value);
            }

            return result;
        }

        private static int ParseFirstInt(ScriptNode node, string content, int fallback)
        {
            foreach (var token in ReadTokens(node, content))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    return value;
            }

            return fallback;
        }

        private static double ParseFirstDouble(ScriptNode node, string content)
        {
            foreach (var token in ReadTokens(node, content))
            {
                if (TryParseDouble(token, out var value))
                    return value;
            }

            return 0;
        }

        private static Dictionary<string, double> ParseNameDoubleMap(ScriptNode node, string content)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var tokens = ReadTokens(node, content);
            for (var i = 0; i + 1 < tokens.Count; i += 2)
            {
                if (!TryParseDouble(tokens[i + 1], out var value))
                    continue;

                result[NormalizeName(tokens[i])] = value;
            }

            return result;
        }

        private static List<string> ReadTokens(ScriptNode node, string content)
        {
            var result = new List<string>();
            if (node == null)
                return result;

            foreach (var item in node.DataItems)
                result.AddRange(ReadTokens(item.GetContent(content)));

            if (result.Count == 0)
                result.AddRange(ReadTokens(node.GetContent(content)));

            return result;
        }

        private static List<string> ReadTokens(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (Match match in Regex.Matches(text, @"`[^`]*`|-?\d+(?:\.\d+)?"))
                result.Add(match.Value);

            return result;
        }

        private static AmplifyOptionType ParseOptionType(string token, Dictionary<string, int> optionMappingTable)
        {
            var normalized = NormalizeName(token);
            if (optionMappingTable != null && optionMappingTable.TryGetValue(normalized, out var mappedValue))
                return ToOptionType(mappedValue);

            switch (normalized.ToLowerInvariant())
            {
                case "[physical defense]":
                    return AmplifyOptionType.PhysicalDefense;
                case "[magical defense]":
                    return AmplifyOptionType.MagicalDefense;
                case "[physical attack]":
                    return AmplifyOptionType.PhysicalAttack;
                case "[magical attack]":
                    return AmplifyOptionType.MagicalAttack;
                case "[all]":
                    return AmplifyOptionType.All;
                default:
                    return AmplifyOptionType.None;
            }
        }

        private static AmplifyOptionType ToOptionType(int value)
        {
            return value >= (int)AmplifyOptionType.None && value <= (int)AmplifyOptionType.All
                ? (AmplifyOptionType)value
                : AmplifyOptionType.None;
        }

        private static bool TryParseDouble(string token, out double value)
        {
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string NormalizeName(string token)
        {
            return (token ?? string.Empty).Trim().Trim('`').Trim();
        }
    }

    public enum AmplifyOptionType
    {
        None = 0,
        PhysicalDefense = 1,
        MagicalDefense = 2,
        PhysicalAttack = 3,
        MagicalAttack = 4,
        All = 5,
    }

    public sealed class AmplifyOptionData
    {
        public AmplifyOptionType OptionType { get; set; }
        public double CumulativeWeight { get; set; }
        public double BaseValue { get; set; }
    }

    public sealed class AmplifyMaterialOption
    {
        public AmplifyOptionType OptionType { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
    }
}
