using GmPvfLib;
using DfoGmTool.ServerCore.GameWorld;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfoGmTool.ServerCore.Game.Skills
{
    
    
    
    
    public sealed class SkillStaticData
    {
        public int Job;
        public int SkillIndex;          
        public string PvfPath;
        public string Name;
        public bool IsActive;           
        public bool IsPassive;
        public int MaxLevel = 1;        
        public int RequiredLevel;       
        public int NumGrowtypes;        
        public int[] SkillFitnessGrowtypes;
        public int[] SkillFitnessSecondGrowtypes;
        public int RawGroup;            
        public bool IsSpecial;
        public bool IsTpSkill;
        public int[] PreRequiredSkills;
        public int[] SpCostPerLevel;
        public int[] TpCostPerLevel;
        public bool IsFixedLevelSkill;
        public int FixedLevelBase;
        public int FixedLevelInterval = 1;
        public int FixedLevelAddPerInterval = 1;

        public int GetFixedLevel(int charLevel)
        {
            if (!IsFixedLevelSkill) return 0;
            if (charLevel < RequiredLevel) return 0;
            var interval = FixedLevelInterval > 0 ? FixedLevelInterval : 1;
            var level = FixedLevelBase + (charLevel - RequiredLevel) / interval * FixedLevelAddPerInterval;
            var maxLv = MaxLevel > 0 ? MaxLevel : int.MaxValue;
            return Math.Min(level, maxLv);
        }

        public int SpCostFor(int fromLevel, int toLevel)
        {
            if (SpCostPerLevel == null || SpCostPerLevel.Length == 0) return 0;
            int sum = 0;
            for (int lv = fromLevel; lv < toLevel; lv++)
            {
                int idx = lv < SpCostPerLevel.Length ? lv : SpCostPerLevel.Length - 1;
                sum += SpCostPerLevel[idx];
            }
            return sum;
        }

        public int TpCostFor(int fromLevel, int toLevel)
        {
            if (TpCostPerLevel == null || TpCostPerLevel.Length == 0) return 0;
            int sum = 0;
            for (int lv = fromLevel; lv < toLevel; lv++)
            {
                int idx = lv < TpCostPerLevel.Length ? lv : TpCostPerLevel.Length - 1;
                sum += TpCostPerLevel[idx];
            }
            return sum;
        }
    }

    
    
    
    
    public static class SkillDataProvider
    {
        internal const int MaximumAvatarOptionRequiredLevel = 45;
        private static readonly HashSet<int> NonCombatSkillIds = new HashSet<int>
        {
            179, 181, 182, 183, 184, 191, 192, 193, 194,
        };
        private static readonly object _lock = new object();
        
        private static Dictionary<int, Dictionary<int, string>> _jobSkillPaths;
        
        private static readonly Dictionary<int, SkillStaticData> _cache = new Dictionary<int, SkillStaticData>();

        internal static void ResetForPvfChange()
        {
            lock (_lock)
            {
                _jobSkillPaths = null;
                _cache.Clear();
            }
        }

        internal static void WarmUp()
        {
            lock (_lock)
            {
                EnsureJobIndexLoaded();
            }
        }

        
        public static SkillStaticData GetSkill(int job, int skillIndex)
        {
            int key = (job << 16) | (skillIndex & 0xFFFF);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;

                EnsureJobIndexLoaded();
                SkillStaticData data = null;
                if (_jobSkillPaths.TryGetValue(job, out var paths) && paths.TryGetValue(skillIndex, out var sklRel))
                {
                    try { data = ParseSkill(job, skillIndex, sklRel); }
                    catch { data = null; }
                }
                _cache[key] = data; 
                return data;
            }
        }

        public static IReadOnlyList<SkillStaticData> GetAvatarOptionSkills(int job, int avatarGrade)
        {
            var result = new List<SkillStaticData>();
            lock (_lock)
            {
                EnsureJobIndexLoaded();
                if (!_jobSkillPaths.TryGetValue(job, out var paths))
                    return result;

                foreach (var pair in paths)
                {
                    if (pair.Key < 0 || pair.Key > byte.MaxValue)
                        continue;
                    SkillStaticData skill;
                    try
                    {
                        skill = GetSkillWithoutLock(job, pair.Key, pair.Value);
                    }
                    catch
                    {
                        continue;
                    }
                    if (!IsValidAvatarOptionSkill(skill, avatarGrade))
                        continue;
                    result.Add(skill);
                }
            }
            result.Sort((left, right) =>
            {
                return left.SkillIndex.CompareTo(right.SkillIndex);
            });
            return result;
        }

        public static bool IsValidAvatarOptionSkill(
            SkillStaticData skill,
            int avatarGrade)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.Name))
                return false;
            if (skill.IsTpSkill || skill.IsSpecial || skill.RequiredLevel <= 0)
                return false;
            var advancedOrHigher = avatarGrade >= 2;
            if (advancedOrHigher)
            {
                if (!skill.IsActive && !skill.IsPassive)
                    return false;
            }
            else if (!skill.IsActive || skill.RequiredLevel > MaximumAvatarOptionRequiredLevel)
                return false;
            if (NonCombatSkillIds.Contains(skill.SkillIndex)
                || skill.Name.IndexOf("((", StringComparison.Ordinal) >= 0
                || skill.Name.IndexOf("不使用", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return true;
        }

        private static SkillStaticData GetSkillWithoutLock(int job, int skillIndex, string path)
        {
            var key = (job << 16) | (skillIndex & 0xFFFF);
            if (_cache.TryGetValue(key, out var cached))
                return cached;
            SkillStaticData data;
            try { data = ParseSkill(job, skillIndex, path); }
            catch { data = null; }
            _cache[key] = data;
            return data;
        }

        private static bool IsAllowedForGrowType(int[] allowed, int firstGrowType)
        {
            if (allowed == null || allowed.Length == 0)
                return true;
            foreach (var value in allowed)
            {
                if (value == firstGrowType)
                    return true;
            }
            return false;
        }
        private static void EnsureJobIndexLoaded()
        {
            if (_jobSkillPaths != null) return;
            var map = new Dictionary<int, Dictionary<int, string>>();

            
            var jobLst = ParseLstPairs(PvfArchiveAccessor.ReadText("skill/skilllist.lst"));
            foreach (var kv in jobLst)
            {
                int job = kv.Key;
                string jobLstFile = kv.Value;             
                try
                {
                    var idxMap = ParseLstPairs(PvfArchiveAccessor.ReadText("skill/" + jobLstFile));
                    map[job] = idxMap;                    
                }
                catch {  }
            }
            _jobSkillPaths = map;
        }

        private static SkillStaticData ParseSkill(int job, int skillIndex, string sklRel)
        {
            var content = PvfArchiveAccessor.ReadText("skill/" + sklRel);
            var skl = SkillFile.Parse(content);

            var data = new SkillStaticData
            {
                Job = job,
                SkillIndex = skillIndex,
                PvfPath = sklRel,
                Name = skl.Name,
                IsActive = skl.Type != null && skl.Type.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0,
                IsPassive = skl.Type != null && skl.Type.IndexOf("passive", StringComparison.OrdinalIgnoreCase) >= 0,
                MaxLevel = skl.MaximumLevel > 0 ? skl.MaximumLevel : 1,
                RequiredLevel = skl.RequiredLevel > 0 ? skl.RequiredLevel : 0,
                NumGrowtypes = CountInts(skl.SkillFitnessGrowtype),
                SkillFitnessGrowtypes = ParseInts(skl.SkillFitnessGrowtype),
                SkillFitnessSecondGrowtypes = ParseInts(skl.SkillFitnessSecondGrowtype),
                RawGroup = skl.SkillClass >= 0 ? skl.SkillClass : 0,
                IsSpecial = skillIndex >= 200 && skillIndex <= 208,
                IsTpSkill = !string.IsNullOrWhiteSpace(skl.FeatureSkillType) && skl.FeatureSkillType.Trim() != "0",
                PreRequiredSkills = ParseInts(skl.PreRequiredSkill),
                SpCostPerLevel = ParseInts(skl.PurchaseCost),
                TpCostPerLevel = ParseInts(skl.SpecialPurchaseCost),
            };
            return data;
        }

        
        private static Dictionary<int, string> ParseLstPairs(string content)
        {
            var result = new Dictionary<int, string>();
            if (string.IsNullOrEmpty(content)) return result;
            int i = 0, n = content.Length;
            while (i < n)
            {
                
                while (i < n && (content[i] < '0' || content[i] > '9') && content[i] != '-') i++;
                int start = i;
                if (i < n && content[i] == '-') i++;
                while (i < n && content[i] >= '0' && content[i] <= '9') i++;
                if (i == start) break;
                if (!int.TryParse(content.Substring(start, i - start), out int id)) break;
                
                while (i < n && content[i] != '`') i++;
                if (i >= n) break;
                i++; 
                int vs = i;
                while (i < n && content[i] != '`') i++;
                if (i >= n) break;
                string val = content.Substring(vs, i - vs);
                i++; 
                result[id] = val.Trim();
            }
            return result;
        }

        private static int[] ParseInts(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new int[0];
            var list = new List<int>();
            foreach (var tok in s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(tok, out int v)) list.Add(v);
            return list.ToArray();
        }

        private static int CountInts(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            int c = 0;
            foreach (var tok in s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(tok, out _)) c++;
            return c;
        }
    }
}
