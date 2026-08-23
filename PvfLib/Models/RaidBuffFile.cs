using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    public sealed class RaidBuffEntry
    {
        public string Target { get; init; } = string.Empty;
        public int CooldownSeconds { get; init; }
        public int DurationSeconds { get; init; }
        public int EffectValue { get; init; }
    }

    public sealed class RaidBuffDefinition
    {
        public string TypeName { get; init; } = string.Empty;
        public List<RaidBuffEntry> Entries { get; } = new List<RaidBuffEntry>();
    }

    public sealed class RaidMonsterDefinition
    {
        public int DungeonId { get; init; }
        public List<int> NamedMonsterIds { get; } = new List<int>();
        public int BossMonsterId { get; set; }
        public List<int> InfectMonsterIds { get; } = new List<int>();
    }

    public sealed class RaidBuffFile : PvfModelBase
    {
        public List<RaidBuffDefinition> Buffs { get; } = new List<RaidBuffDefinition>();
        public List<RaidMonsterDefinition> Monsters { get; } = new List<RaidMonsterDefinition>();

        public static RaidBuffFile Parse(string content)
        {
            var text = content ?? string.Empty;
            var file = new RaidBuffFile
            {
                Root = string.IsNullOrEmpty(text)
                    ? new ScriptNode { Tag = "ROOT" }
                    : new ScriptParser().Parse(text),
                Content = text,
            };

            RaidBuffDefinition currentBuff = null;
            RaidMonsterDefinition currentMonster = null;
            string section = string.Empty;
            string pendingTag = string.Empty;

            foreach (var rawLine in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    var tag = line.Substring(1, line.Length - 2).Trim();
                    var closing = tag.StartsWith("/", StringComparison.Ordinal);
                    if (closing)
                        tag = tag.Substring(1).Trim();

                    if (EqualsTag(tag, "raid buff list"))
                    {
                        section = closing ? string.Empty : "buff";
                        continue;
                    }
                    if (EqualsTag(tag, "raid monster list"))
                    {
                        FlushMonster(file, ref currentMonster);
                        section = closing ? string.Empty : "monster";
                        continue;
                    }

                    if (section == "buff")
                    {
                        if (EqualsTag(tag, "buff"))
                        {
                            if (closing)
                                FlushBuff(file, ref currentBuff);
                            else
                            {
                                FlushBuff(file, ref currentBuff);
                                currentBuff = new RaidBuffDefinition();
                            }
                            pendingTag = string.Empty;
                            continue;
                        }
                        if (!closing && currentBuff != null)
                        {
                            if (EqualsTag(tag, "raid") || EqualsTag(tag, "party"))
                                pendingTag = tag.ToUpperInvariant();
                            else
                            {
                                currentBuff = new RaidBuffDefinition { TypeName = tag };
                                pendingTag = string.Empty;
                            }
                        }
                        continue;
                    }

                    if (section == "monster")
                    {
                        if (EqualsTag(tag, "dungeon"))
                        {
                            if (closing)
                                FlushMonster(file, ref currentMonster);
                            else
                            {
                                FlushMonster(file, ref currentMonster);
                                currentMonster = new RaidMonsterDefinition();
                            }
                            pendingTag = string.Empty;
                            continue;
                        }
                        if (!closing && currentMonster != null)
                            pendingTag = tag.ToLowerInvariant();
                    }
                    continue;
                }

                var values = ParseIntArray(line);
                if (values == null || values.Length == 0)
                    continue;

                if (section == "buff" && currentBuff != null
                    && (pendingTag == "RAID" || pendingTag == "PARTY")
                    && values.Length >= 3)
                {
                    currentBuff.Entries.Add(new RaidBuffEntry
                    {
                        Target = pendingTag,
                        CooldownSeconds = values[0],
                        DurationSeconds = values[1],
                        EffectValue = values[2],
                    });
                    pendingTag = string.Empty;
                }
                else if (section == "monster" && currentMonster != null)
                {
                    if (string.IsNullOrEmpty(pendingTag) && currentMonster.DungeonId == 0)
                    {
                        currentMonster = CloneWithDungeonId(currentMonster, values[0]);
                    }
                    else if (pendingTag == "named")
                        currentMonster.NamedMonsterIds.Add(values[0]);
                    else if (pendingTag == "boss")
                        currentMonster.BossMonsterId = values[0];
                    else if (pendingTag == "infect")
                        currentMonster.InfectMonsterIds.Add(values[0]);
                    pendingTag = string.Empty;
                }
            }

            FlushBuff(file, ref currentBuff);
            FlushMonster(file, ref currentMonster);
            return file;
        }

        private static RaidMonsterDefinition CloneWithDungeonId(RaidMonsterDefinition source, int dungeonId)
        {
            var result = new RaidMonsterDefinition
            {
                DungeonId = dungeonId,
                BossMonsterId = source.BossMonsterId,
            };
            result.NamedMonsterIds.AddRange(source.NamedMonsterIds);
            result.InfectMonsterIds.AddRange(source.InfectMonsterIds);
            return result;
        }

        private static void FlushBuff(RaidBuffFile file, ref RaidBuffDefinition definition)
        {
            if (definition != null && definition.TypeName.Length > 0 && definition.Entries.Count > 0)
                file.Buffs.Add(definition);
            definition = null;
        }

        private static void FlushMonster(RaidBuffFile file, ref RaidMonsterDefinition definition)
        {
            if (definition != null && definition.DungeonId > 0)
                file.Monsters.Add(definition);
            definition = null;
        }

        private static bool EqualsTag(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}