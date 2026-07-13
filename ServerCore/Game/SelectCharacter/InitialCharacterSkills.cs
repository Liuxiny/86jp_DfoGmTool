using DfoGmTool.ServerCore.GameWorld;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DfoGmTool.ServerCore.Game.SelectCharacter
{
    
    
    
    
    
    
    
    
    
    
    public static class InitialCharacterSkills
    {
        private static readonly Dictionary<byte, SkillInfoSnapshot> _cache = new Dictionary<byte, SkillInfoSnapshot>();
        private static readonly object _lock = new object();

        internal static void ResetForPvfChange()
        {
            lock (_lock)
                _cache.Clear();
        }

        public static SkillInfoSnapshot Build(byte job)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(job, out var cached)) return Clone(cached);
                var result = ParseFromPvf(job);
                _cache[job] = result;
                return Clone(result);
            }
        }

        private static SkillInfoSnapshot ParseFromPvf(byte job)
        {
            try
            {
                string lst = PvfArchiveAccessor.ReadText("character/character.lst");
                var lm = Regex.Match(lst ?? "", @"(?<!\d)" + (int)job + @"\s+`([^`]+)`");
                if (!lm.Success) return BuildEmpty();

                string text = PvfArchiveAccessor.ReadText("character/" + lm.Groups[1].Value);
                if (string.IsNullOrEmpty(text)) return BuildEmpty();

                int ivStart = text.IndexOf("[initial value]", StringComparison.OrdinalIgnoreCase);
                if (ivStart < 0) return BuildEmpty();

                int skillStart = text.IndexOf("[skill]", ivStart, StringComparison.OrdinalIgnoreCase);
                if (skillStart < 0) return BuildEmpty();
                skillStart += "[skill]".Length;

                int skillEnd = text.IndexOf("[/skill]", skillStart, StringComparison.OrdinalIgnoreCase);
                if (skillEnd < 0) skillEnd = text.Length;

                string skillSec = text.Substring(skillStart, skillEnd - skillStart).Trim();
                var nums = Regex.Matches(skillSec, @"\d+");

                var page0 = new SkillInfoPageSnapshot { HeaderValue = 0x0000 };
                byte activeSlot = 0, lifeSlot = 102;

                for (int i = 0; i + 1 < nums.Count; i += 2)
                {
                    ushort skillId = ushort.Parse(nums[i].Value);
                    byte level = byte.Parse(nums[i + 1].Value);

                    
                    
                    var data = Skills.SkillDataProvider.GetSkill(job, skillId);
                    if (data == null)
                        DfoGmTool.ServerCore.FileLogger.Log($"[InitialCharacterSkills] job={job} skill={skillId} .skl 读取失败, 按被动放置");
                    if (data != null && data.IsActive)
                        Add(page0, activeSlot++, skillId, level);
                    else
                        Add(page0, lifeSlot++, skillId, level);
                }

                var snap = new SkillInfoSnapshot { Tail0 = 0x0000, Tail1 = 0 };
                snap.Pages.Add(page0);
                
                var page1 = new SkillInfoPageSnapshot { HeaderValue = 0x0000 };
                foreach (var e in page0.Entries)
                    Add(page1, (byte)e.Slot, (ushort)e.SkillId, (byte)e.Level);
                snap.Pages.Add(page1);
                return snap;
            }
            catch (Exception ex)
            {
                DfoGmTool.ServerCore.FileLogger.Log($"[InitialCharacterSkills] job={job} PVF 读取失败: {ex.Message}");
                return BuildEmpty();
            }
        }

        private static SkillInfoSnapshot BuildEmpty()
        {
            var snap = new SkillInfoSnapshot { Tail0 = 0x0000, Tail1 = 0 };
            snap.Pages.Add(new SkillInfoPageSnapshot { HeaderValue = 0x0000 });
            snap.Pages.Add(new SkillInfoPageSnapshot { HeaderValue = 0x0000 });
            return snap;
        }

        private static void Add(SkillInfoPageSnapshot page, byte slot, ushort skillId, byte level)
            => page.Entries.Add(new SkillInfoEntrySnapshot { Slot = slot, SkillId = skillId, Level = level });

        private static SkillInfoSnapshot Clone(SkillInfoSnapshot source)
        {
            var copy = new SkillInfoSnapshot
            {
                Tail0 = source != null ? source.Tail0 : (ushort)0,
                Tail1 = source != null ? source.Tail1 : (ushort)0,
                HasTailValues = source != null && source.HasTailValues,
            };
            if (source == null) return copy;

            foreach (var page in source.Pages)
            {
                var pageCopy = new SkillInfoPageSnapshot { HeaderValue = page.HeaderValue };
                foreach (var entry in page.Entries)
                {
                    var entryCopy = new SkillInfoEntrySnapshot
                    {
                        Slot = entry.Slot,
                        SkillId = entry.SkillId,
                        Level = entry.Level,
                    };
                    foreach (var value in entry.ExtraValues)
                        entryCopy.ExtraValues.Add(value);
                    pageCopy.Entries.Add(entryCopy);
                }
                copy.Pages.Add(pageCopy);
            }
            return copy;
        }
    }
}
