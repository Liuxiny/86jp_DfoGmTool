using DfoGmTool.ServerCore.Game.SelectCharacter;
using System;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.Skills
{
    public sealed class SkillPointTotals
    {
        public int TotalSp;
        public int SpentSp;
        public int RemainingSp;
        public int TotalTp;
        public int SpentTp;
        public int RemainingTp;
    }

    // SP/TP 唯一账本: 点数永远从"已学技能列表"全量派生, 不存在持久化的余额。
    //   总量 SP = Σ spTable(1..level) + bonusSp; TP = level-49(50级起) + bonusTp
    //   已用 = Σ 每个已学技能从免费基线到当前等级的购买成本(TP 技走 TP 池)
    //   免费基线 = 创角初始技能 + 转职送技 + 觉醒技(三层合并, 不计花费)
    public static class SkillPointLedger
    {
        public static SkillPointTotals Compute(
            byte job,
            byte level,
            int bonusSp,
            int bonusTp,
            SkillInfoSnapshot skills,
            int pageIndex,
            int growType = 0,
            int secondGrowType = 0)
        {
            var totalSp = SpTableProvider.GetTotalSp(level) + bonusSp;
            var totalTp = TpTableProvider.GetTotalTp(level) + bonusTp;
            var spentSp = 0;
            var spentTp = 0;

            var baseline = BuildFreeBaseline(job, growType, secondGrowType);

            if (skills != null && pageIndex >= 0 && pageIndex < skills.Pages.Count
                && skills.Pages[pageIndex] != null)
            {
                foreach (var entry in skills.Pages[pageIndex].Entries)
                {
                    var sd = SkillDataProvider.GetSkill(job, entry.SkillId);
                    if (sd == null)
                    {
                        // 已学技能查无 .skl 数据 = 无法计费, 大声记日志而不是静默按 0 计
                        // (真机种子里就有 5 个这类技能: 83/251/84/135/138, 疑共通技/任务被动)。
                        LogMissingSkillDataOnce(job, entry.SkillId);
                        continue;
                    }

                    baseline.TryGetValue(entry.SkillId, out var baseLevel);
                    // fixed level skill: 自动等级按角色等级派生, 不消耗 SP
                    if (sd.IsFixedLevelSkill)
                    {
                        var fixedLevel = sd.GetFixedLevel(level);
                        if (fixedLevel > baseLevel) baseLevel = (byte)System.Math.Min(fixedLevel, byte.MaxValue);
                    }
                    if (entry.Level <= baseLevel)
                        continue;

                    // 成本=费用表原值, 无任何百分比折扣(2026-07-16 实测定案:
                    // 斩铁式+1 真机成本 45 整; [skill fitness ...] 是从属标记非折扣)。
                    var cost = sd.IsTpSkill
                        ? sd.TpCostFor(baseLevel, entry.Level)
                        : sd.SpCostFor(baseLevel, entry.Level);

                    if (sd.IsTpSkill)
                        spentTp += cost;
                    else
                        spentSp += cost;
                }
            }

            return new SkillPointTotals
            {
                TotalSp = totalSp,
                SpentSp = spentSp,
                RemainingSp = Math.Max(0, totalSp - spentSp),
                TotalTp = totalTp,
                SpentTp = spentTp,
                RemainingTp = Math.Max(0, totalTp - spentTp),
            };
        }

        private static readonly HashSet<int> _loggedMissingSkills = new HashSet<int>();
        private static readonly object _logLock = new object();

        private static void LogMissingSkillDataOnce(byte job, int skillId)
        {
            var key = (job << 16) | (skillId & 0xFFFF);
            lock (_logLock)
            {
                if (!_loggedMissingSkills.Add(key))
                    return;
            }
            FileLogger.Log($"[SkillPointLedger] WARNING: learned skill has NO skill data, cost treated as 0: job={job} skillId={skillId}");
        }

        // 免费基线: 这些等级不计入已用点数。
        // 创角初始技能 + 转职送技 + 觉醒技, 数据源均为 PVF, 同一 skillIndex 取最大免费等级。
        internal static Dictionary<ushort, byte> BuildFreeBaseline(byte job, int growType, int secondGrowType)
        {
            var baseline = new Dictionary<ushort, byte>();
            foreach (var g in CharacterSkillProfile.GetInitialSkills(job))
                baseline[g.SkillIndex] = g.Level;

            foreach (var g in CharacterSkillProfile.GetGrowTypeGrants(job, growType, secondGrowType))
            {
                if (!baseline.TryGetValue(g.SkillIndex, out var existing) || g.Level > existing)
                    baseline[g.SkillIndex] = g.Level;
            }

            return baseline;
        }
    }
}
