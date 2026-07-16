using System;
using System.Collections.Generic;
using DfoGmTool.ServerCore.Game.CharacterData;
using DfoGmTool.ServerCore.Game.Characters;
using DfoGmTool.ServerCore.Game.Quests;
using DfoGmTool.ServerCore.Game.Skills;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
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
            if (!ApplyJobAndGrowType(characterId, job, first, second, out error))
                return Error(error ?? ("角色不存在或写入失败: " + characterId));

            return new { success = true, characterId, job, first, second, skillsInitialized = true };
        }

        private bool ApplyJobAndGrowType(int characterId, int? job, int first, int second, out string error)
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
                        SyncAwakeningQuestState(conn, tx, characterId, targetJob, first, second);

                        var repository = SqliteCharacterProgressRepository.FromConnectionString(conn.ConnectionString);
                        SkillStateService.ResetToInitial(
                            repository,
                            conn,
                            tx,
                            characterId,
                            (byte)targetJob,
                            packedGrow,
                            level,
                            bonusSp,
                            bonusTp);
                    }

                    tx.Commit();
                    return true;
                }
            }
        }

        private void SyncAwakeningQuestState(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            int job,
            int first,
            int second)
        {
            if (first <= 0 || second <= 0)
                return;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO character_init_flags (character_id) VALUES (@cid);";
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.ExecuteNonQuery();
            }

            var flags = new HashSet<int>(DefaultPromotedQuestFlags);
            foreach (var questId in _pvfIndex.ResolveAwakeningQuestChain(job, first, second: false))
                flags.Add(questId);
            if (second >= 2)
            {
                foreach (var questId in _pvfIndex.ResolveAwakeningQuestChain(job, first, second: true))
                    flags.Add(questId);
            }

            foreach (var questId in flags)
            {
                if (questId <= 0 || questId > ushort.MaxValue)
                    continue;
                QuestRepository.MarkQuestCleared(conn, tx, characterId, (ushort)questId);
            }
        }
    }
}
