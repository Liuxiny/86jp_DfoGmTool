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
        public int MaxLevel = 1;
        public int RequiredLevel;
        public int NumGrowtypes;
        public int RawGroup;
        public bool IsSpecial;
        public bool IsTpSkill;
        public int[] PreRequiredSkills;
        public int[] SpCostPerLevel;
        public int[] TpCostPerLevel;
        // 逐 growType 等级上限(6槽, 按 growType 0-5 索引); 上限 0 = 该方向不可学
        public int[] GrowtypeMaxLevels;
        // 逐 觉醒段 等级上限(12槽 = growType*2 + (觉醒段-1)); 0 = 不可学
        public int[] SecondGrowtypeMaxLevels;
        // 等级门槛间隔: reqLevel + (targetLv-1)*levelInterval <= characLevel
        public int LevelInterval = 1;
        // [fixed level skill] 标志: 等级按角色等级自动派生, purchase cost=0, 不消耗 SP。
        // 公式: base + max(0, charLevel-reqLevel) / interval * addPerInterval
        public bool IsFixedLevelSkill;
        public int FixedLevelBase;
        public int FixedLevelInterval = 1;
        public int FixedLevelAddPerInterval = 1;

        public int GetFixedLevel(int charLevel)
        {
            if (!IsFixedLevelSkill) return 0;
            if (charLevel < RequiredLevel) return 0;
            var level = FixedLevelBase + (charLevel - RequiredLevel) / FixedLevelInterval * FixedLevelAddPerInterval;
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

        // 该 (growType, 觉醒段) 下的等级上限。老服口径(CSkill::can_learn):
        // 觉醒段>0 先查 12 槽表(growType*2+段-1), 值为 0 再回落 6 槽表;
        // 两表都缺省(数组为空)时回落 MaxLevel; 最终 0 = 该方向不可学。
        public int GetMaxLevelFor(int growType, int secondGrowType)
        {
            if (secondGrowType > 0
                && SecondGrowtypeMaxLevels != null
                && SecondGrowtypeMaxLevels.Length > 0)
            {
                var idx = growType * 2 + (secondGrowType - 1);
                var secondMax = idx >= 0 && idx < SecondGrowtypeMaxLevels.Length
                    ? SecondGrowtypeMaxLevels[idx]
                    : 0;
                if (secondMax > 0)
                    return secondMax;
            }

            if (GrowtypeMaxLevels != null && GrowtypeMaxLevels.Length > 0)
            {
                return growType >= 0 && growType < GrowtypeMaxLevels.Length
                    ? GrowtypeMaxLevels[growType]
                    : 0;
            }

            return MaxLevel;
        }

        // 注: [skill fitness growtype]/[skill fitness second growtype] 是技能从属标记
        // (记录该技能属于哪些方向/觉醒段), 不是 SP 折扣——"fitness=百分比折扣"的旧解读
        // 已被实测推翻(斩铁式+1 真机成本 45 整)。门禁走 GetMaxLevelFor, 成本走费用表原值;
        // fitness 数组仅剩的用途是 NumGrowtypes(数组长度)参与槽位分组。
    }

    public static class SkillDataProvider
    {
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
                MaxLevel = skl.MaximumLevel > 0 ? skl.MaximumLevel : 1,
                RequiredLevel = skl.RequiredLevel > 0 ? skl.RequiredLevel : 0,
                NumGrowtypes = CountInts(skl.SkillFitnessGrowtype),
                RawGroup = skl.SkillClass >= 0 ? skl.SkillClass : 0,
                IsSpecial = skillIndex >= 200 && skillIndex <= 208,
                IsTpSkill = !string.IsNullOrWhiteSpace(skl.FeatureSkillType) && skl.FeatureSkillType.Trim() != "0",
                PreRequiredSkills = ParseInts(skl.PreRequiredSkill),
                SpCostPerLevel = ParseInts(skl.PurchaseCost),
                TpCostPerLevel = ParseInts(skl.SpecialPurchaseCost),
                GrowtypeMaxLevels = ParseInts(skl.GrowtypeMaximumLevel),
                SecondGrowtypeMaxLevels = ParseInts(skl.SecondGrowtypeMaximumLevel),
                LevelInterval = ParseLevelInterval(skl.RequiredLevelRange),
                IsFixedLevelSkill = skl.IsFixedLevelSkill,
                FixedLevelBase = skl.FixedLevelBase,
                FixedLevelInterval = skl.FixedLevelInterval,
                FixedLevelAddPerInterval = skl.FixedLevelAddPerInterval,
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

        private static int ParseLevelInterval(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 1;
            if (int.TryParse(s.Trim(), out var v) && v > 0) return v;
            return 1;
        }
    }
}
