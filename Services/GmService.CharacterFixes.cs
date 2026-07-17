using System;
using System.Collections.Generic;
using System.Linq;
using DfoGmTool.ServerCore.Game.CharacterData;
using DfoGmTool.ServerCore.Game.Characters;
using DfoGmTool.ServerCore.Game.Quests;
using DfoGmTool.ServerCore.Game.Skills;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        private enum GrowSkillSyncMode
        {
            None,
            Rebuild,
            MergeAwakening,
        }

        private static readonly int[] DefaultPromotedQuestFlags =
        {
            101, 1016, 1776, 1796, 1797, 1808, 1809, 1942,
        };

        public object SetGrowTypeFixed(int characterId, int? job, int first, int second)
        {
            if (job.HasValue && (job.Value < 0 || job.Value > byte.MaxValue))
                return Error("职业范围 0-255");
            if (first < 0 || first > 15 || second < 0 || second > 15)
                return Error("转职/觉醒范围必须为 0-15");
            if (second > 0 && first == 0)
                return Error("未转职不能设置觉醒");

            string error;
            if (!ApplyJobAndGrowType(characterId, job, first, second, GrowSkillSyncMode.Rebuild, out error))
                return Error(error ?? ("角色不存在或写入失败: " + characterId));

            return new { success = true, characterId, job, first, second, skillsInitialized = true };
        }

        private bool ApplyJobAndGrowType(
            int characterId,
            int? job,
            int first,
            int second,
            GrowSkillSyncMode skillSyncMode,
            out string error)
        {
            error = null;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    byte level;
                    uint exp;
                    int bonusSp;
                    int bonusTp;
                    int currentJob;
                    int currentGrow;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "SELECT job, grow_type, level, exp, bonus_sp, bonus_tp FROM characters WHERE character_id = @cid;";
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                tx.Rollback();
                                error = "角色不存在: " + characterId;
                                return false;
                            }

                            currentJob = reader.GetInt32(0);
                            currentGrow = reader.GetInt32(1);
                            level = (byte)reader.GetInt32(2);
                            exp = (uint)reader.GetInt64(3);
                            bonusSp = reader.GetInt32(4);
                            bonusTp = reader.GetInt32(5);
                        }
                    }

                    var targetJob = job ?? currentJob;
                    if (!ValidateGrowLevelRequirement(level, first, second, out error))
                    {
                        tx.Rollback();
                        return false;
                    }

                    if (!_pvfIndex.TryValidateJobGrowOption(targetJob, first, second, out error))
                    {
                        tx.Rollback();
                        return false;
                    }

                    var packedGrow = (byte)((second << 4) | (first & 0xF));
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
UPDATE characters
SET job = @job, grow_type = @grow, updated_at = CURRENT_TIMESTAMP
WHERE character_id = @cid;";
                        cmd.Parameters.AddWithValue("@job", targetJob);
                        cmd.Parameters.AddWithValue("@grow", (int)packedGrow);
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        if (cmd.ExecuteNonQuery() == 0)
                        {
                            tx.Rollback();
                            error = "角色不存在: " + characterId;
                            return false;
                        }
                    }

                    if (!CharacterProgressService.PersistLevelAndExp(conn, tx, characterId, level, exp))
                    {
                        tx.Rollback();
                        error = "等级/经验/属性重算失败";
                        return false;
                    }

                    if (targetJob != currentJob || packedGrow != currentGrow)
                    {
                        SyncProfessionQuestState(conn, tx, characterId, targetJob, first, second);
                        SyncGrowSkills(conn, tx, characterId, (byte)targetJob, first, second, level, bonusSp, bonusTp, skillSyncMode);
                    }

                    tx.Commit();
                    return true;
                }
            }
        }

        private bool ApplyGrowTypeDeltaFromQuest(int characterId, PvfIndexService.QuestMeta meta)
        {
            if (meta == null || meta.GrowNumber <= 0)
                return false;

            var chainType = meta.RewardChainType > 0 ? meta.RewardChainType : meta.JobChangeQuestValue;
            if (chainType != 1 && chainType != 2)
                return false;

            int currentGrow;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT grow_type FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    var value = cmd.ExecuteScalar();
                    if (value == null || value == DBNull.Value)
                        return false;
                    currentGrow = Convert.ToInt32(value);
                }
            }

            var first = currentGrow & 0xF;
            var second = (currentGrow >> 4) & 0xF;
            if (chainType == 1)
                first = meta.GrowNumber;
            else
                second = meta.GrowNumber;

            string error;
            return ApplyJobAndGrowType(
                characterId,
                null,
                first,
                second,
                chainType == 1 ? GrowSkillSyncMode.Rebuild : GrowSkillSyncMode.MergeAwakening,
                out error);
        }

        private static bool ValidateGrowLevelRequirement(byte level, int first, int second, out string error)
        {
            error = null;
            if (first > 0 && level < 15)
            {
                error = "角色达到 15 级后才能转职";
                return false;
            }
            if (second >= 1 && level < 50)
            {
                error = "角色达到 50 级后才能觉醒";
                return false;
            }
            if (second >= 2 && level < 75)
            {
                error = "角色达到 75 级后才能二次觉醒";
                return false;
            }
            return true;
        }

        private void SyncGrowSkills(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            byte job,
            int first,
            int second,
            byte level,
            int bonusSp,
            int bonusTp,
            GrowSkillSyncMode skillSyncMode)
        {
            var repository = SqliteCharacterProgressRepository.FromConnectionString(conn.ConnectionString);
            if (skillSyncMode == GrowSkillSyncMode.Rebuild)
            {
                var snapshot = CharacterSkillProfile.BuildSnapshot(job, first, second, level);
                var points = SkillStateService.ResolvePointState(snapshot, job, level, bonusSp, bonusTp, first, second);
                SkillStateService.ApplyProtocolMirrors(snapshot, points);
                repository.SaveSkillProgress(conn, tx, characterId, snapshot, points);
                return;
            }

            if (skillSyncMode == GrowSkillSyncMode.MergeAwakening)
            {
                var snapshot = repository.LoadSkills(conn, tx, characterId);
                var grants = CharacterSkillProfile.GetGrowTypeGrants(job, first, second);
                CharacterSkillProfile.MergeGrants(snapshot, grants, job, level);
                var points = SkillStateService.ResolvePointState(snapshot, job, level, bonusSp, bonusTp, first, second);
                SkillStateService.ApplyProtocolMirrors(snapshot, points);
                repository.SaveSkillProgress(conn, tx, characterId, snapshot, points);
                return;
            }

            SkillStateService.LoadAndSync(repository, conn, tx, characterId, job, level, bonusSp, bonusTp, persist: true, growType: first, secondGrowType: second);
        }

        private void SyncProfessionQuestState(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            int job,
            int first,
            int second)
        {
            if (first <= 0)
                return;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO character_init_flags (character_id) VALUES (@cid);";
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.ExecuteNonQuery();
            }

            var flags = new HashSet<int>(DefaultPromotedQuestFlags);
            foreach (var questId in ResolveProfessionQuestIds(job, first, stage: 1))
                flags.Add(questId);
            if (second >= 1)
                foreach (var questId in ResolveProfessionQuestIds(job, first, stage: 2))
                    flags.Add(questId);
            if (second >= 2)
                foreach (var questId in ResolveProfessionQuestIds(job, first, stage: 3))
                    flags.Add(questId);

            foreach (var questId in flags)
            {
                if (questId <= 0 || questId > ushort.MaxValue)
                    continue;
                QuestRepository.MarkQuestCleared(conn, tx, characterId, (ushort)questId);
            }
        }

        private int[] ResolveProfessionQuestIds(int job, int first, int stage)
        {
            var all = _pvfIndex.AllQuestMeta;
            if (all == null || first <= 0)
                return Array.Empty<int>();

            if (stage == 2)
            {
                var named = ResolveNamedAwakeningQuestIds(job, first, second: false);
                if (named.Length > 0) return named;
            }
            if (stage == 3)
            {
                var named = ResolveNamedAwakeningQuestIds(job, first, second: true);
                if (named.Length > 0) return named;
            }

            var rewardChainType = stage == 1 ? 1 : 2;
            var growNumber = stage == 1 ? first : stage - 1;
            var grow = (stage <= 1 ? first : first) | ((stage >= 3 ? 2 : stage >= 2 ? 1 : 0) << 4);
            return all.Values
                .Where(m => m != null
                    && QuestMatchesCharacter(m, job, grow)
                    && (stage == 1 || m.GrowType == first)
                    && MatchesProfessionQuestStage(m, stage, rewardChainType, growNumber))
                .OrderBy(m => EffectiveLevel(m))
                .ThenBy(m => m.Id)
                .Select(m => m.Id)
                .ToArray();
        }

        private static bool MatchesProfessionQuestStage(PvfIndexService.QuestMeta meta, int stage, int rewardChainType, int growNumber)
        {
            if (meta == null)
                return false;
            if (meta.RewardChainType == rewardChainType && meta.GrowNumber == growNumber)
                return true;
            if (meta.JobChangeQuestValue != stage)
                return false;
            return stage == 1
                ? meta.GrowNumber == growNumber
                : meta.GrowNumber <= 0 || meta.GrowNumber == growNumber;
        }

        private int[] ResolveNamedAwakeningQuestIds(int job, int first, bool second)
        {
            var all = _pvfIndex.AllQuestMeta;
            if (all == null || first <= 0)
                return Array.Empty<int>();

            var prefix = second ? "二次觉醒 - " : "觉醒 - ";
            var grow = first | ((second ? 2 : 1) << 4);
            return all.Values
                .Where(m => m != null
                    && m.Name != null
                    && m.Name.StartsWith(prefix, StringComparison.Ordinal)
                    && !IsDragonLeapWeaponQuest(m)
                    && m.GrowType == first
                    && QuestMatchesCharacter(m, job, grow))
                .OrderBy(m => EffectiveLevel(m))
                .ThenBy(m => m.Id)
                .Select(m => m.Id)
                .ToArray();
        }

        private static bool IsDragonLeapWeaponQuest(PvfIndexService.QuestMeta meta)
        {
            return meta != null
                && meta.Name != null
                && meta.Name.IndexOf("龙跃武器", StringComparison.Ordinal) >= 0;
        }
    }
}
