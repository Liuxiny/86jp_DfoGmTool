using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DfoGmTool.ServerCore.Game.Skills;
using DfoGmTool.ServerCore.GameWorld;
using GmPvfLib;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal static class AvatarAbilityDataProvider
    {
        private static readonly object Sync = new object();
        private static AvatarAbilityData _data;

        internal static void ResetForPvfChange()
        {
            lock (Sync)
            {
                _data = null;
            }
        }

        internal static void WarmUp()
        {
            _ = GetData();
        }

        internal static List<AvatarGrantOption> ResolveCoatOptions(int abilityCaseIndex, int job)
        {
            if (abilityCaseIndex < 0)
                return new List<AvatarGrantOption>();

            var data = GetData();
            if (!data.AbilityCases.TryGetValue(abilityCaseIndex, out var entries))
                return new List<AvatarGrantOption>();

            var result = new List<AvatarGrantOption>();
            foreach (var entry in entries)
            {
                if (!string.Equals(entry.Ability, "SKILL_LEVEL", StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new AvatarGrantOption(
                    entry.OptionValue,
                    BuildSkillLabel(entry.Job, entry.SkillIndex, entry.SkillLevel, job),
                    true));
            }
            result.Sort((left, right) => left.Value.CompareTo(right.Value));
            return result;
        }

        internal static string BuildSelectAbilityLabel(AvatarSelectAbilityEntry entry, int job, out bool isSkill)
        {
            isSkill = false;
            if (entry == null)
                return string.Empty;

            if (string.Equals(entry.Ability, "SKILL_LEVEL", StringComparison.OrdinalIgnoreCase))
            {
                isSkill = true;
                return BuildSkillLabel(entry.Job, entry.SkillIndex, entry.SkillLevel, job);
            }

            var label = ResolveAbilityName(entry.Ability);
            if (entry.Amount > 0 && !string.IsNullOrWhiteSpace(entry.Operator))
                return label + " " + entry.Operator + entry.Amount.ToString(CultureInfo.InvariantCulture);
            return label;
        }

        internal static string ResolveAbilityName(string ability)
        {
            var token = NormalizeAbilityToken(ability);
            if (string.IsNullOrWhiteSpace(token))
                return string.Empty;

            var data = GetData();
            if (data.AbilityNames.TryGetValue(token, out var label)
                && !string.IsNullOrWhiteSpace(label))
                return label;

            return token.Replace('_', ' ');
        }

        private static AvatarAbilityData GetData()
        {
            lock (Sync)
            {
                if (_data != null)
                    return _data;

                _data = new AvatarAbilityData
                {
                    AbilityNames = LoadAbilityNames(),
                    AbilityCases = LoadAbilityCases(),
                };
                return _data;
            }
        }

        private static Dictionary<string, string> LoadAbilityNames()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var text = PvfArchiveAccessor.ReadText("etc/avatarabilitystringtable.etc");
                var inTable = false;
                var tokens = new List<string>();
                foreach (var line in SplitLines(text))
                {
                    var trimmed = line.Trim();
                    if (string.Equals(trimmed, "[avatar ability string table]", StringComparison.OrdinalIgnoreCase))
                    {
                        inTable = true;
                        continue;
                    }
                    if (string.Equals(trimmed, "[/avatar ability string table]", StringComparison.OrdinalIgnoreCase))
                        break;
                    if (!inTable)
                        continue;

                    tokens.AddRange(ReadTokens(trimmed));
                }

                for (var index = 0; index + 1 < tokens.Count; index += 2)
                {
                    var key = NormalizeAbilityToken(tokens[index]);
                    var value = tokens[index + 1]?.Trim();
                    if (!string.IsNullOrWhiteSpace(key) && !result.ContainsKey(key))
                        result.Add(key, value);
                }
            }
            catch
            {
                return result;
            }
            return result;
        }

        private static Dictionary<int, List<AvatarSelectAbilityEntry>> LoadAbilityCases()
        {
            var result = new Dictionary<int, List<AvatarSelectAbilityEntry>>();
            try
            {
                var text = PvfArchiveAccessor.ReadText("skill/abilitydatas.dat");
                var inCase = false;
                var caseTokens = new List<string>();
                foreach (var line in SplitLines(text))
                {
                    var trimmed = line.Trim();
                    if (string.Equals(trimmed, "[ability case]", StringComparison.OrdinalIgnoreCase))
                    {
                        inCase = true;
                        caseTokens.Clear();
                        continue;
                    }
                    if (string.Equals(trimmed, "[/ability case]", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseAbilityCase(caseTokens, out var caseIndex, out var entries)
                            && !result.ContainsKey(caseIndex))
                            result.Add(caseIndex, entries);
                        inCase = false;
                        caseTokens.Clear();
                        continue;
                    }
                    if (inCase)
                        caseTokens.AddRange(ReadTokens(trimmed));
                }
            }
            catch
            {
                return result;
            }
            return result;
        }

        private static bool TryParseAbilityCase(
            IReadOnlyList<string> tokens,
            out int caseIndex,
            out List<AvatarSelectAbilityEntry> entries)
        {
            caseIndex = -1;
            entries = new List<AvatarSelectAbilityEntry>();
            if (tokens == null || tokens.Count < 3)
                return false;
            if (!TryReadInt(tokens[0], out caseIndex))
                return false;

            var index = 1;

            while (index < tokens.Count)
            {
                var optionValue = entries.Count;
                if (TryReadInt(tokens[index], out var parsedOptionValue) && index + 1 < tokens.Count)
                {
                    optionValue = parsedOptionValue;
                    index++;
                }

                var ability = NormalizeAbilityToken(tokens[index++]);
                if (string.IsNullOrWhiteSpace(ability))
                    continue;

                var entry = new AvatarSelectAbilityEntry
                {
                    Ability = ability,
                    OptionValue = optionValue,
                };

                if (string.Equals(ability, "SKILL_LEVEL", StringComparison.OrdinalIgnoreCase))
                {
                    if (index < tokens.Count)
                        entry.Job = NormalizeJobToken(tokens[index++]);
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

                if (entry.OptionValue >= 0 && entry.OptionValue <= byte.MaxValue)
                    entries.Add(entry);
            }

            return caseIndex >= 0;
        }

        private static string BuildSkillLabel(string jobToken, int skillIndex, int skillLevel, int characterJob)
        {
            var skill = ResolveSkill(jobToken, skillIndex, characterJob);
            var name = !string.IsNullOrWhiteSpace(skill?.Name)
                ? skill.Name
                : "技能 " + skillIndex.ToString(CultureInfo.InvariantCulture);
            var suffix = skillLevel > 0 ? " +" + skillLevel.ToString(CultureInfo.InvariantCulture) : string.Empty;
            return name + suffix;
        }

        private static SkillStaticData ResolveSkill(string jobToken, int skillIndex, int characterJob)
        {
            var tokenJob = JobFromToken(jobToken);
            SkillStaticData skill = null;
            if (tokenJob >= 0)
                skill = SkillDataProvider.GetSkill(tokenJob, skillIndex);
            if (skill == null && characterJob >= 0)
                skill = SkillDataProvider.GetSkill(characterJob, skillIndex);
            return skill;
        }

        private static int JobFromToken(string token)
        {
            var normalized = NormalizeJobToken(token);
            return normalized switch
            {
                "swordman" => 0,
                "fighter" => 1,
                "gunner" => 2,
                "mage" => 3,
                "priest" => 4,
                "atgunner" => 5,
                "thief" => 6,
                "atfighter" => 7,
                "atmage" => 8,
                "demonicswordman" => 9,
                "creatormage" => 10,
                "atswordman" => 11,
                "knight" => 12,
                _ => -1,
            };
        }

        private static string NormalizeAbilityToken(string value)
        {
            var token = (value ?? string.Empty).Trim().Trim('`').Trim();
            if (token.StartsWith("[", StringComparison.Ordinal)
                && token.EndsWith("]", StringComparison.Ordinal)
                && token.Length > 2)
                token = token.Substring(1, token.Length - 2);
            return token.Trim();
        }

        private static string NormalizeJobToken(string value)
        {
            return NormalizeAbilityToken(value)
                .ToLowerInvariant()
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty);
        }

        private static bool TryReadInt(string value, out int number)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var line in lines)
                yield return line;
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

        private sealed class AvatarAbilityData
        {
            public Dictionary<string, string> AbilityNames { get; set; }

            public Dictionary<int, List<AvatarSelectAbilityEntry>> AbilityCases { get; set; }
        }
    }
}
