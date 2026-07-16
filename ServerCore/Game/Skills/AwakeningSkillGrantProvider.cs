using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DfoGmTool.ServerCore.Game.SelectCharacter;

namespace DfoGmTool.ServerCore.Game.Skills
{
    public static class AwakeningSkillGrantProvider
    {
        private static readonly object _lock = new object();
        private static Dictionary<int, Dictionary<int, List<GrantSkill>>> _grantsByJobAndGrow;

        public static void Apply(byte job, byte growType, SkillInfoSnapshot snapshot)
        {
            if (snapshot == null || growType == 0)
                return;

            var grants = Find(job, growType);
            if (grants == null || grants.Count == 0)
                return;

            while (snapshot.Pages.Count < 2)
                snapshot.Pages.Add(new SkillInfoPageSnapshot());

            foreach (var grant in grants)
            {
                if (grant.SkillId <= 0 || grant.SkillId > ushort.MaxValue)
                    continue;

                var skill = SkillDataProvider.GetSkill(job, grant.SkillId);
                if (skill == null)
                    throw new InvalidOperationException("skill static data not found: job=" + job + " skill=" + grant.SkillId);

                var level = grant.Level <= 0 ? 1 : grant.Level;
                if (level > byte.MaxValue)
                    level = byte.MaxValue;

                AddToPage(snapshot.Pages[0], skill, (ushort)grant.SkillId, (byte)level);
                AddToPage(snapshot.Pages[1], skill, (ushort)grant.SkillId, (byte)level);
            }
        }

        private static List<GrantSkill> Find(byte job, byte growType)
        {
            var grants = Load();
            Dictionary<int, List<GrantSkill>> byGrow;
            if (!grants.TryGetValue(job, out byGrow))
                return null;

            List<GrantSkill> skills;
            return byGrow.TryGetValue(growType, out skills) ? skills : null;
        }

        private static Dictionary<int, Dictionary<int, List<GrantSkill>>> Load()
        {
            lock (_lock)
            {
                if (_grantsByJobAndGrow != null)
                    return _grantsByJobAndGrow;

                var result = new Dictionary<int, Dictionary<int, List<GrantSkill>>>();
                var path = Path.Combine(AppContext.BaseDirectory, "awakening_skill_grants.json");
                if (!File.Exists(path))
                    path = Path.Combine(Directory.GetCurrentDirectory(), "awakening_skill_grants.json");
                if (!File.Exists(path))
                    return _grantsByJobAndGrow = result;

                try
                {
                    var model = JsonSerializer.Deserialize<GrantFile>(
                        File.ReadAllText(path),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (model?.Grants == null)
                        return _grantsByJobAndGrow = result;

                    foreach (var grant in model.Grants)
                    {
                        if (grant == null || grant.GrowType == null || grant.Skills == null)
                            continue;

                        Dictionary<int, List<GrantSkill>> byGrow;
                        if (!result.TryGetValue(grant.Job, out byGrow))
                            result[grant.Job] = byGrow = new Dictionary<int, List<GrantSkill>>();

                        foreach (var grow in grant.GrowType)
                        {
                            if (grow <= 0 || grow > byte.MaxValue)
                                continue;
                            byGrow[grow] = new List<GrantSkill>(grant.Skills);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DfoGmTool.ServerCore.FileLogger.Log("[AwakeningSkillGrantProvider] load failed: " + ex.Message);
                }

                _grantsByJobAndGrow = result;
                return _grantsByJobAndGrow;
            }
        }

        private static void AddToPage(
            SkillInfoPageSnapshot page,
            SkillStaticData skill,
            ushort skillId,
            byte level)
        {
            if (page == null)
                return;

            foreach (var entry in page.Entries)
            {
                if (entry.SkillId == skillId)
                {
                    if (entry.Level < level)
                        entry.Level = level;
                    return;
                }
            }

            var group = ReformSkillGroup(skill.RawGroup, skill.IsActive, skill.NumGrowtypes);
            var slot = AllocateSkillSlot(skill.IsActive, group, skill.Job, page);
            if (slot < 0 || slot > byte.MaxValue)
                throw new InvalidOperationException("no free skill slot: job=" + skill.Job + " skill=" + skillId);

            page.Entries.Add(new SkillInfoEntrySnapshot
            {
                Slot = (byte)slot,
                SkillId = skillId,
                Level = level,
            });
        }

        private static int ReformSkillGroup(int rawGroup, bool isActive, int numGrowtypes)
        {
            if (isActive)
                return 3;
            if (rawGroup >= 0 && rawGroup <= 3)
                return numGrowtypes <= 2 ? 1 : 0;
            if (rawGroup == 4)
                return 2;
            return rawGroup;
        }

        private static int AllocateSkillSlot(
            bool isActive,
            int finalGroup,
            int job,
            SkillInfoPageSnapshot page)
        {
            var used = new HashSet<int>();
            foreach (var entry in page.Entries)
                used.Add(entry.Slot);

            if (isActive && job != 9)
            {
                var active = FirstFreeSkillSlot(used, 0, 6);
                if (active >= 0)
                    return active;
                active = FirstFreeSkillSlot(used, 198, 204);
                if (active >= 0)
                    return active;
            }

            if (finalGroup < 0 || finalGroup > 3)
                finalGroup = 0;

            var start = finalGroup == 0 ? 6 : finalGroup == 1 ? 54 : finalGroup == 2 ? 102 : 150;
            var end = finalGroup == 0 ? 54 : finalGroup == 1 ? 102 : finalGroup == 2 ? 150 : 198;
            return FirstFreeSkillSlot(used, start, end);
        }

        private static int FirstFreeSkillSlot(HashSet<int> used, int start, int end)
        {
            for (var slot = start; slot < end; slot++)
            {
                if (!used.Contains(slot))
                    return slot;
            }
            return -1;
        }

        private sealed class GrantFile
        {
            public List<GrantEntry> Grants { get; set; }
        }

        private sealed class GrantEntry
        {
            public int Job { get; set; }
            public List<int> GrowType { get; set; }
            public List<GrantSkill> Skills { get; set; }
        }

        private sealed class GrantSkill
        {
            public int SkillId { get; set; }
            public int Level { get; set; }
        }
    }
}
