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
                switch (node.Tag.ToLowerInvariant())
                {
                    case "rarity weight":
                        file.RarityWeights = ParseNameDoubleMap(node, content);
                        break;
                    case "option data":
                        file.OptionData = ParseOptionData(node, content);
                        break;
                }
            }

            return file;
        }

        private static List<AmplifyOptionData> ParseOptionData(ScriptNode node, string content)
        {
            var result = new List<AmplifyOptionData>();
            var tokens = ReadTokens(node, content);
            for (var i = 0; i + 2 < tokens.Count; i += 3)
            {
                if (!TryParseDouble(tokens[i + 1], out var cumulativeWeight)
                    || !TryParseDouble(tokens[i + 2], out var baseValue))
                    continue;

                result.Add(new AmplifyOptionData
                {
                    OptionType = ParseOptionType(tokens[i]),
                    CumulativeWeight = cumulativeWeight,
                    BaseValue = baseValue,
                });
            }

            return result;
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

        private static AmplifyOptionType ParseOptionType(string token)
        {
            switch (NormalizeName(token).ToLowerInvariant())
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
}
