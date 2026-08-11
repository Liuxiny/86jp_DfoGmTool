using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using GmPvfLib;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    /// <summary>
    /// GM-only adapter for avatar metadata that is intentionally not part of the
    /// server-owned PvfLib EquipmentFile contract.
    /// </summary>
    internal static class AvatarEquipmentMetadataReader
    {
        internal static AvatarEquipmentMetadata Read(EquipmentFile equipment)
        {
            var result = new AvatarEquipmentMetadata();
            if (equipment?.Root?.Children == null || string.IsNullOrEmpty(equipment.Content))
                return result;

            foreach (var node in equipment.Root.Children)
            {
                if (node == null)
                    continue;

                if (string.Equals(node.Tag, "ability case index", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = node.GetFirstDataContent(equipment.Content);
                    if (TryReadInt(raw, out var value))
                        result.AbilityCaseIndex = value;
                }
                else if (string.Equals(node.Tag, "avatar select ability", StringComparison.OrdinalIgnoreCase))
                {
                    result.SelectAbilities = ParseSelectAbilities(node, equipment.Content);
                }
            }

            return result;
        }

        private static List<AvatarSelectAbilityEntry> ParseSelectAbilities(ScriptNode node, string content)
        {
            var result = new List<AvatarSelectAbilityEntry>();
            if (node?.DataItems == null)
                return result;

            var tokens = new List<string>();
            foreach (var item in node.DataItems)
                tokens.AddRange(ReadTokens(item.GetContent(content)));

            var index = 0;
            while (index + 1 < tokens.Count)
            {
                if (!TryReadInt(tokens[index], out var optionValue))
                {
                    index++;
                    continue;
                }

                var ability = NormalizeToken(tokens[index + 1]);
                index += 2;
                var entry = new AvatarSelectAbilityEntry
                {
                    OptionValue = optionValue,
                    Ability = ability,
                };

                if (string.Equals(ability, "SKILL_LEVEL", StringComparison.OrdinalIgnoreCase))
                {
                    if (index < tokens.Count)
                        entry.Job = NormalizeToken(tokens[index++]);
                    if (index < tokens.Count && TryReadInt(tokens[index], out var skillIndex))
                    {
                        entry.SkillIndex = skillIndex;
                        index++;
                    }
                    if (index < tokens.Count && TryReadInt(tokens[index], out var skillLevel))
                    {
                        entry.SkillLevel = skillLevel;
                        index++;
                    }
                }
                else
                {
                    if (index < tokens.Count && (tokens[index] == "+" || tokens[index] == "-"))
                        entry.Operator = tokens[index++];
                    if (index < tokens.Count && TryReadInt(tokens[index], out var amount))
                    {
                        entry.Amount = amount;
                        index++;
                    }
                }

                result.Add(entry);
            }

            return result;
        }

        private static IEnumerable<string> ReadTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            foreach (Match match in Regex.Matches(text, "`([^`]*)`|\\S+"))
            {
                var token = match.Groups[1].Success ? match.Groups[1].Value : match.Value;
                if (!string.IsNullOrWhiteSpace(token))
                    yield return token.Trim();
            }
        }

        private static bool TryReadInt(string value, out int result)
        {
            var token = NormalizeToken(value);
            return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static string NormalizeToken(string value)
        {
            var token = (value ?? string.Empty).Trim().Trim('`').Trim();
            if (token.StartsWith("[", StringComparison.Ordinal)
                && token.EndsWith("]", StringComparison.Ordinal)
                && token.Length > 2)
            {
                token = token.Substring(1, token.Length - 2);
            }
            return token.Trim();
        }
    }

    internal sealed class AvatarEquipmentMetadata
    {
        internal int AbilityCaseIndex { get; set; } = -1;

        internal List<AvatarSelectAbilityEntry> SelectAbilities { get; set; } = new List<AvatarSelectAbilityEntry>();
    }

    internal sealed class AvatarSelectAbilityEntry
    {
        internal int OptionValue { get; set; }

        internal string Ability { get; set; }

        internal string Operator { get; set; }

        internal int Amount { get; set; }

        internal string Job { get; set; }

        internal int SkillIndex { get; set; }

        internal int SkillLevel { get; set; }
    }
}
