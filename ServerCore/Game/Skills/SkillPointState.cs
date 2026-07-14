using DfoGmTool.ServerCore.Game.SelectCharacter;
using DfoGmTool.ServerCore.Game.CharacterData;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Skills
{
    public sealed class SkillPointState
    {
        public int TotalSp { get; set; }

        public int RemainingSp { get; set; }

        public int TotalTp { get; set; }

        public int RemainingTp { get; set; }

        public byte SyncedLevel { get; set; }

        public bool HasPersistedState { get; set; }
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
        public static SkillPointState ResolvePointState(
            SkillInfoSnapshot skills,
            SkillPointState persisted,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp)
        {
            var calculated = SkillPointCalculator.Calculate(job, level, bonusSp, bonusTp, skills);

            var state = new SkillPointState
            {
                TotalSp = calculated.TotalSp,
                TotalTp = calculated.TotalTp,
                SyncedLevel = level,
                HasPersistedState = persisted != null && persisted.HasPersistedState
            };

            if (persisted != null && persisted.HasPersistedState)
            {
                var gainedSp = calculated.TotalSp - persisted.TotalSp;
                var gainedTp = calculated.TotalTp - persisted.TotalTp;
                state.RemainingSp = Clamp(persisted.RemainingSp + gainedSp, 0, calculated.TotalSp);
                state.RemainingTp = Clamp(persisted.RemainingTp + gainedTp, 0, calculated.TotalTp);
            }
            else
            {
                state.RemainingSp = calculated.RemainingSp;
                state.RemainingTp = calculated.RemainingTp;
            }

            return state;
        }

        public static void ApplyProtocolMirrors(SkillInfoSnapshot skills, SkillPointState state)
        {
            if (skills == null || state == null) return;
            while (skills.Pages.Count < 2)
                skills.Pages.Add(new SkillInfoPageSnapshot());

            skills.Pages[0].HeaderValue = ToUInt16(state.RemainingSp);
            skills.Tail0 = ToUInt16(state.RemainingTp);
            skills.Tail1 = 0;
            skills.HasTailValues = true;
        }

        public static SkillPointProtocolState GetProtocolState(
            SkillInfoSnapshot skills,
            SkillPointState points)
        {
            var fallbackSp = ToUInt16(points != null ? points.RemainingSp : 0);
            var sharedTp = ToUInt16(points != null ? points.RemainingTp : 0);
            return new SkillPointProtocolState
            {
                Page0Sp = GetProtocolPageSp(skills, 0, fallbackSp),
                Page1Sp = GetProtocolPageSp(skills, 1, 0),
                // 当前业务模型的 TP 在两套技能页之间共享，因此两个协议槽写同一绝对值。
                // 历史 Tail1 曾被旧代码写入 SP，不能作为任何 TP 状态来源。
                Page0Tp = sharedTp,
                Page1Tp = sharedTp,
            };
        }

        public static SkillPointProtocolState LoadProtocolState(
            SqliteCharacterProgressRepository repository,
            int characterId,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp,
            bool persist)
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
                persist);
            return GetProtocolState(synced.Skills, synced.Points);
        }

        private static ushort GetProtocolPageSp(
            SkillInfoSnapshot skills,
            int pageIndex,
            ushort fallback)
        {
            if (skills != null
                && pageIndex >= 0
                && pageIndex < skills.Pages.Count
                && skills.Pages[pageIndex] != null)
            {
                return skills.Pages[pageIndex].HeaderValue;
            }

            return fallback;
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

        public static (SkillInfoSnapshot Skills, SkillPointState Points) ResetToInitial(
            SqliteCharacterProgressRepository repository,
            int characterId,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp)
        {
            var skills = InitialCharacterSkills.Build(job);
            var points = ResolvePointState(skills, null, job, level, bonusSp, bonusTp);
            points.RemainingSp = points.TotalSp;
            points.RemainingTp = points.TotalTp;
            Persist(repository, characterId, skills, points);
            return (skills, points);
        }

        public static (SkillInfoSnapshot Skills, SkillPointState Points) LoadAndSync(
            SqliteCharacterProgressRepository repository,
            int characterId,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp,
            bool persist)
        {
            var skills = repository.LoadSkills(characterId);
            var persisted = repository.LoadSkillPointState(characterId);
            var synced = Synchronize(
                skills, persisted, job, level, bonusSp, bonusTp);
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
            bool persist)
        {
            if (repository == null) throw new System.ArgumentNullException(nameof(repository));
            if (connection == null) throw new System.ArgumentNullException(nameof(connection));
            if (transaction == null) throw new System.ArgumentNullException(nameof(transaction));

            var skills = repository.LoadSkills(connection, transaction, characterId);
            var persisted = repository.LoadSkillPointState(connection, transaction, characterId);
            var synced = Synchronize(
                skills, persisted, job, level, bonusSp, bonusTp);
            if (persist)
            {
                repository.SaveSkillProgress(
                    connection, transaction, characterId, synced.Skills, synced.Points);
            }
            return synced;
        }

        private static (SkillInfoSnapshot Skills, SkillPointState Points) Synchronize(
            SkillInfoSnapshot skills,
            SkillPointState persisted,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp)
        {
            var oldTotalSp = ResolvePreviousTotalSp(skills, persisted, job, level, bonusSp, bonusTp);
            var points = ResolvePointState(skills, persisted, job, level, bonusSp, bonusTp);
            SyncSkillPageRemainingSp(skills, job, level, bonusSp, oldTotalSp, points.TotalSp);
            ApplyProtocolMirrors(skills, points);
            return (skills, points);
        }

        private static int ResolvePreviousTotalSp(
            SkillInfoSnapshot skills,
            SkillPointState persisted,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp)
        {
            if (persisted != null && persisted.HasPersistedState)
                return persisted.TotalSp;

            return SkillPointCalculator.Calculate(job, level, bonusSp, bonusTp, skills, 0).TotalSp;
        }

        private static void SyncSkillPageRemainingSp(
            SkillInfoSnapshot skills,
            byte job,
            byte level,
            int bonusSp,
            int oldTotalSp,
            int newTotalSp)
        {
            if (skills == null || skills.Pages.Count == 0)
                return;

            var gainedSp = newTotalSp - oldTotalSp;
            if (gainedSp == 0)
                return;

            for (var i = 1; i < skills.Pages.Count; i++)
            {
                var page = skills.Pages[i];
                if (page == null)
                    continue;

                var calculated = SkillPointCalculator.Calculate(job, level, bonusSp, 0, skills, i);
                var remaining = page.HeaderValue > 0
                    ? page.HeaderValue + gainedSp
                    : calculated.RemainingSp;
                page.HeaderValue = ToUInt16(remaining);
            }
        }

        private static ushort ToUInt16(int value)
        {
            return (ushort)Clamp(value, 0, ushort.MaxValue);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int Max(int a, int b)
        {
            return a > b ? a : b;
        }
    }
}
