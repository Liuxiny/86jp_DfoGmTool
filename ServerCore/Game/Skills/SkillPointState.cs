using DfoGmTool.ServerCore.Game.SelectCharacter;
using DfoGmTool.ServerCore.Game.CharacterData;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Skills
{
    // SP/TP 的会话视图。全部字段由 SkillPointLedger 从已学技能列表派生,
    // 不再有任何持久化余额(character_skill_points 已退役, 见迁移23)。
    // 四池独立: page0 和 page1 各自有独立的 SP+TP 余额。
    public sealed class SkillPointState
    {
        public int TotalSp { get; set; }

        public int RemainingSp { get; set; }

        // PVP 树(page1)的剩余 SP(独立池, 总量公式与 PVE 相同)。
        public int RemainingSpPage1 { get; set; }

        public int TotalTp { get; set; }

        // PVE 树(page0)的剩余 TP(remain_sfp[2])。
        public int RemainingTp { get; set; }

        // PVP 树(page1)的剩余 TP(remain_sfp[3])。
        public int RemainingTpPage1 { get; set; }

        public byte SyncedLevel { get; set; }
    }

    // NOTI 0x0025 中连续四个 u16 的绝对状态快照。
    public struct SkillPointProtocolState
    {
        public ushort Page0Sp { get; set; }

        public ushort Page1Sp { get; set; }

        public ushort Page0Tp { get; set; }

        public ushort Page1Tp { get; set; }
    }

    public static class SkillStateService
    {
        // 纯派生: 四池各自从对应页的已学技能算出, 逐树独立扣减。
        public static SkillPointState ResolvePointState(
            SkillInfoSnapshot skills,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp,
            int growType = 0,
            int secondGrowType = 0)
        {
            var page0 = SkillPointLedger.Compute(job, level, bonusSp, bonusTp, skills, 0, growType, secondGrowType);
            var page1 = SkillPointLedger.Compute(job, level, bonusSp, bonusTp, skills, 1, growType, secondGrowType);
            return new SkillPointState
            {
                TotalSp = page0.TotalSp,
                RemainingSp = page0.RemainingSp,
                RemainingSpPage1 = page1.RemainingSp,
                TotalTp = page0.TotalTp,
                RemainingTp = page0.RemainingTp,
                RemainingTpPage1 = page1.RemainingTp,
                SyncedLevel = level,
            };
        }

        // 协议镜像是 write-only: 发包前由派生值生成, 服务端任何逻辑不得读回。
        public static void ApplyProtocolMirrors(SkillInfoSnapshot skills, SkillPointState state)
        {
            if (skills == null || state == null) return;
            while (skills.Pages.Count < 2)
                skills.Pages.Add(new SkillInfoPageSnapshot());

            // 镜像映射: header0/header1/tail0/tail1 = 四池余额。
            skills.Pages[0].HeaderValue = ToUInt16(state.RemainingSp);
            skills.Pages[1].HeaderValue = ToUInt16(state.RemainingSpPage1);
            skills.Tail0 = ToUInt16(state.RemainingTp);
            skills.Tail1 = ToUInt16(state.RemainingTpPage1);
            skills.HasTailValues = true;
        }

        public static SkillPointProtocolState GetProtocolState(
            SkillInfoSnapshot skills,
            SkillPointState points)
        {
            return new SkillPointProtocolState
            {
                Page0Sp = ToUInt16(points != null ? points.RemainingSp : 0),
                Page1Sp = ToUInt16(points != null ? points.RemainingSpPage1 : 0),
                Page0Tp = ToUInt16(points != null ? points.RemainingTp : 0),
                Page1Tp = ToUInt16(points != null ? points.RemainingTpPage1 : 0),
            };
        }

        public static SkillPointProtocolState LoadProtocolState(
            SqliteCharacterProgressRepository repository,
            int characterId,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp,
            bool persist,
            int growType = 0,
            int secondGrowType = 0)
        {
            if (repository == null)
                throw new System.ArgumentNullException(nameof(repository));

            var synced = LoadAndSync(
                repository,
                characterId,
                job,
                level,
                bonusSp,
                bonusTp,
                persist,
                growType,
                secondGrowType);
            return GetProtocolState(synced.Skills, synced.Points);
        }

        public static void Persist(
            SqliteCharacterProgressRepository repository,
            int characterId,
            SkillInfoSnapshot skills,
            SkillPointState state)
        {
            if (repository == null || skills == null || state == null) return;
            ApplyProtocolMirrors(skills, state);
            repository.SaveSkillProgress(characterId, skills);
        }

        public static void ResolveAndPersist(
            SqliteCharacterProgressRepository repository,
            int characterId,
            SkillInfoSnapshot skills,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp,
            int growType = 0,
            int secondGrowType = 0)
        {
            var points = ResolvePointState(skills, job, level, bonusSp, bonusTp, growType, secondGrowType);
            Persist(repository, characterId, skills, points);
        }

        public static (SkillInfoSnapshot Skills, SkillPointState Points) LoadAndSync(
            SqliteCharacterProgressRepository repository,
            int characterId,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp,
            bool persist,
            int growType = 0,
            int secondGrowType = 0)
        {
            var skills = repository.LoadSkills(characterId);
            return Synchronize(skills, job, level, bonusSp, bonusTp, growType, secondGrowType);
        }

        internal static (SkillInfoSnapshot Skills, SkillPointState Points) LoadAndSync(
            SqliteCharacterProgressRepository repository,
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp,
            bool persist,
            int growType = 0,
            int secondGrowType = 0)
        {
            if (repository == null) throw new System.ArgumentNullException(nameof(repository));
            if (connection == null) throw new System.ArgumentNullException(nameof(connection));
            if (transaction == null) throw new System.ArgumentNullException(nameof(transaction));

            var skills = repository.LoadSkills(connection, transaction, characterId);
            return Synchronize(skills, job, level, bonusSp, bonusTp, growType, secondGrowType);
        }

        private static (SkillInfoSnapshot Skills, SkillPointState Points) Synchronize(
            SkillInfoSnapshot skills,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp,
            int growType = 0,
            int secondGrowType = 0)
        {
            var points = ResolvePointState(skills, job, level, bonusSp, bonusTp, growType, secondGrowType);
            ApplyProtocolMirrors(skills, points);
            return (skills, points);
        }

        private static ushort ToUInt16(int value)
        {
            if (value < 0) return 0;
            return value > ushort.MaxValue ? (ushort)ushort.MaxValue : (ushort)value;
        }
    }
}
