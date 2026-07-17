using DfoGmTool.ServerCore.Game.CharacterData;
using DfoGmTool.ServerCore.Game.SelectCharacter;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Skills
{
    public sealed class SkillPointState
    {
        public int TotalSp { get; set; }

        public int RemainingSp { get; set; }

        public int RemainingSpPage1 { get; set; }

        public int SpentSp { get; set; }

        public int SpentSpPage1 { get; set; }

        public int TotalTp { get; set; }

        public int RemainingTp { get; set; }

        public int RemainingTpPage1 { get; set; }

        public int SpentTp { get; set; }

        public int SpentTpPage1 { get; set; }

        public byte SyncedLevel { get; set; }
    }

    public struct SkillPointProtocolState
    {
        public ushort Page0Sp { get; set; }

        public ushort Page1Sp { get; set; }

        public ushort Page0Tp { get; set; }

        public ushort Page1Tp { get; set; }
    }

    public static class SkillStateService
    {
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
                SpentSp = page0.SpentSp,
                SpentSpPage1 = page1.SpentSp,
                TotalTp = page0.TotalTp,
                RemainingTp = page0.RemainingTp,
                RemainingTpPage1 = page1.RemainingTp,
                SpentTp = page0.SpentTp,
                SpentTpPage1 = page1.SpentTp,
                SyncedLevel = level,
            };
        }

        public static void ApplyProtocolMirrors(SkillInfoSnapshot skills, SkillPointState state)
        {
            if (skills == null || state == null) return;
            while (skills.Pages.Count < 2)
                skills.Pages.Add(new SkillInfoPageSnapshot());

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
            repository.SaveSkillProgress(characterId, skills, state);
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

        public static (SkillInfoSnapshot Skills, SkillPointState Points) ResetToInitial(
            SqliteCharacterProgressRepository repository,
            int characterId,
            byte job,
            byte growType,
            byte level,
            int bonusSp,
            int bonusTp)
        {
            var skills = InitialCharacterSkills.Build(job);
            AwakeningSkillGrantProvider.Apply(job, growType, skills);
            return PersistInitial(repository, null, null, characterId, skills, job, growType, level, bonusSp, bonusTp);
        }

        public static (SkillInfoSnapshot Skills, SkillPointState Points) ResetToInitial(
            SqliteCharacterProgressRepository repository,
            int characterId,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp)
        {
            return ResetToInitial(repository, characterId, job, 0, level, bonusSp, bonusTp);
        }

        internal static (SkillInfoSnapshot Skills, SkillPointState Points) ResetToInitial(
            SqliteCharacterProgressRepository repository,
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            byte job,
            byte growType,
            byte level,
            int bonusSp,
            int bonusTp)
        {
            if (repository == null) throw new System.ArgumentNullException(nameof(repository));
            if (connection == null) throw new System.ArgumentNullException(nameof(connection));
            if (transaction == null) throw new System.ArgumentNullException(nameof(transaction));

            var skills = InitialCharacterSkills.Build(job);
            AwakeningSkillGrantProvider.Apply(job, growType, skills);
            return PersistInitial(repository, connection, transaction, characterId, skills, job, growType, level, bonusSp, bonusTp);
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
            var synced = Synchronize(skills, job, level, bonusSp, bonusTp, growType, secondGrowType);
            if (persist)
                repository.SaveSkillProgress(characterId, synced.Skills, synced.Points);
            return synced;
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
            var synced = Synchronize(skills, job, level, bonusSp, bonusTp, growType, secondGrowType);
            if (persist)
                repository.SaveSkillProgress(connection, transaction, characterId, synced.Skills, synced.Points);
            return synced;
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

        private static (SkillInfoSnapshot Skills, SkillPointState Points) PersistInitial(
            SqliteCharacterProgressRepository repository,
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            SkillInfoSnapshot skills,
            byte job,
            byte growType,
            byte level,
            int bonusSp,
            int bonusTp)
        {
            Characters.CharacterStatComputer.DecodeGrowType(growType, out var firstGrow, out var secondGrow);
            var points = ResolvePointState(skills, job, level, bonusSp, bonusTp, firstGrow, secondGrow);
            ApplyProtocolMirrors(skills, points);

            if (connection != null && transaction != null)
                repository.SaveSkillProgress(connection, transaction, characterId, skills, points);
            else
                repository.SaveSkillProgress(characterId, skills, points);

            return (skills, points);
        }

        private static ushort ToUInt16(int value)
        {
            if (value < 0) return 0;
            return value > ushort.MaxValue ? ushort.MaxValue : (ushort)value;
        }
    }
}
