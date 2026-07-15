using DfoGmTool.ServerCore.Game.SelectCharacter;
using DfoGmTool.ServerCore.GameWorld;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DfoGmTool.ServerCore.Game.Skills
{
    // 启动时一次性从 PVF character/*.chr 预加载所有职业的完整技能图谱：
    // 创角初始技能 + 每个 growType 的转职送技 + 每个觉醒段的觉醒技。
    // 后续全部按 (job, growType, secondGrowType) 纯查表, 不再重复解析 .chr 文本。
    public static class CharacterSkillProfile
    {
        public struct SkillGrant
        {
            public ushort SkillIndex;
            public byte Level;
        }

        private sealed class JobProfile
        {
            // 创角初始技能 [initial value] [skill]
            public List<SkillGrant> InitialSkills = new List<SkillGrant>();
            // [growtype N] [skill], N=growType+1, 按 growType 索引(0=未转职段)
            public Dictionary<int, List<SkillGrant>> GrowTypeSkills = new Dictionary<int, List<SkillGrant>>();
            // [awakening N] [awakening skill], 按 (growType, awakeningIndex) 索引
            // key = growType * 100 + awakeningIndex
            public Dictionary<int, List<SkillGrant>> AwakeningSkills = new Dictionary<int, List<SkillGrant>>();
        }

        private static readonly object _lock = new object();
        private static Dictionary<byte, JobProfile> _profiles;

        // GM 本地补丁: 运行时切换 PVF 数据源时清空进程级缓存(PvfRuntimeCache 统一调度)。
        internal static void ResetForPvfChange()
        {
            lock (_lock)
            {
                _profiles = null;
            }
        }

        // 启动时调用, 或首次访问时惰性加载
        public static void Warmup()
        {
            lock (_lock)
            {
                if (_profiles != null) return;
                _profiles = LoadAll();
            }
        }

        // 创角初始技能
        public static IReadOnlyList<SkillGrant> GetInitialSkills(byte job)
        {
            EnsureLoaded();
            return _profiles.TryGetValue(job, out var p) ? p.InitialSkills : (IReadOnlyList<SkillGrant>)Array.Empty<SkillGrant>();
        }

        // 指定 (growType, secondGrowType) 下应免费授予的全部技能
        // (不含创角初始技能, 由调用方合并)。
        // 二觉角色累加 awakening 1 + awakening 2。
        public static IReadOnlyList<SkillGrant> GetGrowTypeGrants(byte job, int growType, int secondGrowType)
        {
            EnsureLoaded();
            if (!_profiles.TryGetValue(job, out var p))
                return Array.Empty<SkillGrant>();

            var result = new List<SkillGrant>();

            // 转职送技
            if (growType > 0 && p.GrowTypeSkills.TryGetValue(growType, out var gtSkills))
                result.AddRange(gtSkills);

            // 觉醒技: 累加 awakening 1..secondGrowType
            if (secondGrowType > 0 && growType > 0)
            {
                for (var awIdx = 1; awIdx <= secondGrowType; awIdx++)
                {
                    var key = growType * 100 + awIdx;
                    if (p.AwakeningSkills.TryGetValue(key, out var awSkills))
                        result.AddRange(awSkills);
                }
            }

            return result;
        }

        // 构建 (job, growType, secondGrowType) 的完整技能快照——所有"初始技能重建"
        // 场景(新角色/缺技能自动补种/转职重建)的唯一入口:
        // 1) InitialCharacterSkills 设计布局打底(主动 0+ 顺排/生活 102+; 黑暗武士的
        //    快捷栏+连招子技固定槽位就在这层, 不能用通用分配器重排);
        // 2) 叠加转职/觉醒送技, 新技能经 SkillSlotAllocator 落组区;
        // 3) fixed-level 技(等级自动成长)按角色等级抬升。
        public static SkillInfoSnapshot BuildSnapshot(byte job, int growType, int secondGrowType, int charLevel)
        {
            var snapshot = InitialCharacterSkills.Build(job) ?? new SkillInfoSnapshot();
            while (snapshot.Pages.Count < 2)
                snapshot.Pages.Add(new SkillInfoPageSnapshot());

            foreach (var page in snapshot.Pages)
            {
                foreach (var entry in page.Entries)
                {
                    var sd = SkillDataProvider.GetSkill(job, entry.SkillId);
                    if (sd == null || !sd.IsFixedLevelSkill) continue;
                    var fixedLv = sd.GetFixedLevel(charLevel);
                    if (fixedLv > entry.Level) entry.Level = (byte)Math.Min(fixedLv, byte.MaxValue);
                }
            }

            MergeGrants(snapshot, GetGrowTypeGrants(job, growType, secondGrowType), job, charLevel);
            return snapshot;
        }

        // 把送技列表并入快照(两页同步): 已有条目取更高等级, 新条目走分配器落槽。
        // 觉醒(chainType 2)在现有技能上植入觉醒技也走这里。
        public static void MergeGrants(
            SkillInfoSnapshot snapshot,
            IReadOnlyList<SkillGrant> grants,
            byte job,
            int charLevel)
        {
            if (snapshot == null || grants == null || grants.Count == 0) return;
            while (snapshot.Pages.Count < 2)
                snapshot.Pages.Add(new SkillInfoPageSnapshot());

            foreach (var page in snapshot.Pages)
            {
                foreach (var g in grants)
                {
                    var grantLevel = (int)g.Level;
                    var sd = SkillDataProvider.GetSkill(job, g.SkillIndex);
                    if (sd != null && sd.IsFixedLevelSkill)
                    {
                        var fixedLv = sd.GetFixedLevel(charLevel);
                        if (fixedLv > grantLevel) grantLevel = fixedLv;
                    }

                    var existing = page.Entries.Find(e => e.SkillId == g.SkillIndex);
                    if (existing != null)
                    {
                        if (grantLevel > existing.Level)
                            existing.Level = (byte)Math.Min(grantLevel, byte.MaxValue);
                        continue;
                    }

                    var group = sd != null ? SkillSlotAllocator.ReformGroup(sd.RawGroup, sd.IsActive, sd.NumGrowtypes) : 0;
                    var occupied = new HashSet<int>();
                    foreach (var e in page.Entries) occupied.Add(e.Slot);
                    var slot = SkillSlotAllocator.AllocateNewSlot(sd != null && sd.IsActive, group, job, occupied);
                    if (slot < 0)
                    {
                        // 组区满时顺扫全表找空位; 204 槽全满属数据异常, 大声报并跳过。
                        for (var s = 0; s < 204; s++)
                            if (!occupied.Contains(s)) { slot = s; break; }
                        if (slot < 0)
                        {
                            FileLogger.Log($"[CharacterSkillProfile] ERROR: no free slot for granted skill, dropped: job={job} skill={g.SkillIndex}");
                            continue;
                        }
                    }

                    page.Entries.Add(new SkillInfoEntrySnapshot
                    {
                        Slot = (byte)slot,
                        SkillId = g.SkillIndex,
                        Level = (byte)Math.Min(grantLevel, byte.MaxValue),
                    });
                }
            }
        }

        private static void EnsureLoaded()
        {
            if (_profiles != null) return;
            Warmup();
        }

        private static Dictionary<byte, JobProfile> LoadAll()
        {
            var profiles = new Dictionary<byte, JobProfile>();
            try
            {
                var lst = PvfArchiveAccessor.ReadText("character/character.lst");
                if (string.IsNullOrEmpty(lst)) return profiles;

                var pairs = Regex.Matches(lst, @"(\d+)\s+`([^`]+)`");
                foreach (Match m in pairs)
                {
                    var jobId = byte.Parse(m.Groups[1].Value);
                    var chrPath = "character/" + m.Groups[2].Value;
                    try
                    {
                        var text = PvfArchiveAccessor.ReadText(chrPath);
                        if (string.IsNullOrEmpty(text)) continue;
                        var profile = ParseChr(text, jobId);
                        profiles[jobId] = profile;
                        var totalGrants = 0;
                        foreach (var kv in profile.GrowTypeSkills) totalGrants += kv.Value.Count;
                        foreach (var kv in profile.AwakeningSkills) totalGrants += kv.Value.Count;
                        FileLogger.Log($"[CharacterSkillProfile] job={jobId} initial={profile.InitialSkills.Count} growTypeGrants={totalGrants} path={chrPath}");
                    }
                    catch (Exception ex)
                    {
                        FileLogger.Log($"[CharacterSkillProfile] job={jobId} parse error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[CharacterSkillProfile] character.lst load error: {ex.Message}");
            }
            return profiles;
        }

        private static JobProfile ParseChr(string text, byte job)
        {
            var profile = new JobProfile();

            // [initial value] 段的 [skill]
            var ivStart = text.IndexOf("[initial value]", StringComparison.OrdinalIgnoreCase);
            if (ivStart >= 0)
                profile.InitialSkills = ParseSkillPairsInSection(text, ivStart, "[skill]", "[/skill]");

            // 扫描全部 [growtype N] 段
            var gtPattern = new Regex(@"\[growtype\s+(\d+)\]", RegexOptions.IgnoreCase);
            foreach (Match gtMatch in gtPattern.Matches(text))
            {
                var n = int.Parse(gtMatch.Groups[1].Value);
                var growType = n - 1; // N = growType + 1
                var segStart = gtMatch.Index + gtMatch.Length;

                // 段结束 = 下一个 [growtype 或文件末尾
                var nextGt = gtPattern.Match(text, segStart);
                var segEnd = nextGt.Success ? nextGt.Index : text.Length;

                // [skill] 对
                var skills = ParseSkillPairsInSection(text, segStart, "[skill]", "[/skill]", segEnd);
                if (skills.Count > 0)
                    profile.GrowTypeSkills[growType] = skills;

                // 扫描该 growtype 段内的所有 [awakening N]
                var awPattern = new Regex(@"\[awakening\s+(\d+)\]", RegexOptions.IgnoreCase);
                var gtSegment = text.Substring(segStart, segEnd - segStart);
                foreach (Match awMatch in awPattern.Matches(gtSegment))
                {
                    var awIdx = int.Parse(awMatch.Groups[1].Value);
                    if (awIdx < 1) continue;
                    var awStart = segStart + awMatch.Index + awMatch.Length;
                    // 觉醒段结束 = 下一个 [awakening 或 [growtype 或段结束
                    var nextAw = awPattern.Match(text, awStart);
                    var awEnd = (nextAw.Success && nextAw.Index < segEnd) ? nextAw.Index : segEnd;

                    var awSkills = ParseSkillPairsInSection(text, awStart, "[awakening skill]", "[/awakening skill]", awEnd);
                    if (awSkills.Count > 0)
                    {
                        var key = growType * 100 + awIdx;
                        profile.AwakeningSkills[key] = awSkills;
                    }
                }
            }

            return profile;
        }

        private static List<SkillGrant> ParseSkillPairsInSection(string text, int searchFrom, string openTag, string closeTag, int searchLimit = -1)
        {
            var result = new List<SkillGrant>();
            if (searchLimit < 0) searchLimit = text.Length;

            var tagStart = text.IndexOf(openTag, searchFrom, searchLimit - searchFrom, StringComparison.OrdinalIgnoreCase);
            if (tagStart < 0) return result;
            tagStart += openTag.Length;

            var tagEnd = text.IndexOf(closeTag, tagStart, searchLimit - tagStart, StringComparison.OrdinalIgnoreCase);
            if (tagEnd < 0) tagEnd = searchLimit;

            var section = text.Substring(tagStart, tagEnd - tagStart).Trim();
            var nums = Regex.Matches(section, @"\d+");
            for (var i = 0; i + 1 < nums.Count; i += 2)
            {
                result.Add(new SkillGrant
                {
                    SkillIndex = ushort.Parse(nums[i].Value),
                    Level = byte.Parse(nums[i + 1].Value),
                });
            }
            return result;
        }
    }
}
