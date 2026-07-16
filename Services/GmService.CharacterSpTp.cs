using DfoGmTool.ServerCore.Game.CharacterData;
using DfoGmTool.ServerCore.Game.Skills;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        public object AdjustSpTpSynced(int characterId, int spDelta, int tpDelta)
        {
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    if (!TryLoadSpTpBase(conn, tx, characterId, out var job, out var level, out var bonusSp, out var bonusTp))
                        return Error("角色不存在: " + characterId);

                    var validation = ValidateBonusPointDelta(spDelta, tpDelta, bonusSp, bonusTp);
                    if (validation != null)
                        return Error(validation);

                    ApplyBonusPointDelta(conn, tx, characterId, spDelta, tpDelta);
                    bonusSp += spDelta;
                    bonusTp += tpDelta;

                    var synced = SyncSkillPoints(conn, tx, characterId, job, level, bonusSp, bonusTp);
                    tx.Commit();
                    return SpTpResult(characterId, bonusSp, bonusTp, synced);
                }
            }
        }

        public object ZeroRemainingSpTp(int characterId)
        {
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    if (!TryLoadSpTpBase(conn, tx, characterId, out var job, out var level, out var bonusSp, out var bonusTp))
                        return Error("角色不存在: " + characterId);

                    var current = SyncSkillPoints(conn, tx, characterId, job, level, bonusSp, bonusTp);
                    if (current.Points.RemainingSp <= 0 && current.Points.RemainingTp <= 0)
                        return Error("当前没有可归零的剩余 SP/TP");
                    if (current.Points.RemainingSp > bonusSp)
                        return Error("剩余 SP 大于附加 SP，归零会低于等级默认技能点，无法使用");
                    if (current.Points.RemainingTp > bonusTp)
                        return Error("剩余 TP 大于附加 TP，归零会低于等级默认技能点，无法使用");

                    var spDelta = -current.Points.RemainingSp;
                    var tpDelta = -current.Points.RemainingTp;
                    ApplyBonusPointDelta(conn, tx, characterId, spDelta, tpDelta);
                    bonusSp += spDelta;
                    bonusTp += tpDelta;

                    var synced = SyncSkillPoints(conn, tx, characterId, job, level, bonusSp, bonusTp);
                    tx.Commit();
                    return SpTpResult(characterId, bonusSp, bonusTp, synced);
                }
            }
        }

        private static string ValidateBonusPointDelta(int spDelta, int tpDelta, int bonusSp, int bonusTp)
        {
            if (spDelta < 0 && bonusSp <= 0)
                return "当前 SP 已经是等级默认值，不能继续减少附加 SP";
            if (tpDelta < 0 && bonusTp <= 0)
                return "当前 TP 已经是等级默认值，不能继续减少附加 TP";
            if (spDelta < 0 && -spDelta > bonusSp)
                return "减少 SP 不能低于当前等级默认技能点，可减少上限为 " + bonusSp;
            if (tpDelta < 0 && -tpDelta > bonusTp)
                return "减少 TP 不能低于当前等级默认技能点，可减少上限为 " + bonusTp;
            return null;
        }

        private static void ApplyBonusPointDelta(SqliteConnection conn, SqliteTransaction tx, int characterId, int spDelta, int tpDelta)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
UPDATE characters
SET bonus_sp = bonus_sp + @dsp,
    bonus_tp = bonus_tp + @dtp
WHERE character_id = @cid;";
                cmd.Parameters.AddWithValue("@dsp", spDelta);
                cmd.Parameters.AddWithValue("@dtp", tpDelta);
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.ExecuteNonQuery();
            }
        }

        private static (DfoGmTool.ServerCore.Game.SelectCharacter.SkillInfoSnapshot Skills, SkillPointState Points) SyncSkillPoints(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            byte job,
            byte level,
            int bonusSp,
            int bonusTp)
        {
            var repository = SqliteCharacterProgressRepository.FromConnectionString(conn.ConnectionString);
            return SkillStateService.LoadAndSync(
                repository,
                conn,
                tx,
                characterId,
                job,
                level,
                bonusSp,
                bonusTp,
                persist: true);
        }

        private static object SpTpResult(
            int characterId,
            int bonusSp,
            int bonusTp,
            (DfoGmTool.ServerCore.Game.SelectCharacter.SkillInfoSnapshot Skills, SkillPointState Points) synced)
        {
            return new
            {
                success = true,
                characterId,
                bonusSp,
                bonusTp,
                totalSp = synced.Points.TotalSp,
                remainingSp = synced.Points.RemainingSp,
                totalTp = synced.Points.TotalTp,
                remainingTp = synced.Points.RemainingTp,
            };
        }

        private static bool TryLoadSpTpBase(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            out byte job,
            out byte level,
            out int bonusSp,
            out int bonusTp)
        {
            job = 0;
            level = 0;
            bonusSp = 0;
            bonusTp = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT job, level, bonus_sp, bonus_tp FROM characters WHERE character_id = @cid;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                        return false;
                    job = (byte)reader.GetInt32(0);
                    level = (byte)reader.GetInt32(1);
                    bonusSp = reader.GetInt32(2);
                    bonusTp = reader.GetInt32(3);
                    return true;
                }
            }
        }
    }
}
