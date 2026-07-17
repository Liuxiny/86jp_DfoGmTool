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
                    if (!TryLoadSpTpBase(conn, tx, characterId, out var job, out var level, out var growType, out var bonusSp, out var bonusTp, out var skillTreeIndex))
                        return Error("角色不存在: " + characterId);

                    var current = SyncSkillPoints(conn, tx, characterId, job, level, growType, bonusSp, bonusTp);
                    var validation = ValidatePointDelta(spDelta, tpDelta, current.Points);
                    if (validation != null)
                        return Error(validation);

                    ApplyBonusPointDelta(conn, tx, characterId, spDelta, tpDelta);
                    bonusSp += spDelta;
                    bonusTp += tpDelta;

                    var synced = SyncSkillPoints(conn, tx, characterId, job, level, growType, bonusSp, bonusTp);
                    tx.Commit();
                    return SpTpResult(characterId, bonusSp, bonusTp, skillTreeIndex, synced);
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
                    if (!TryLoadSpTpBase(conn, tx, characterId, out var job, out var level, out var growType, out var bonusSp, out var bonusTp, out var skillTreeIndex))
                        return Error("角色不存在: " + characterId);

                    var current = SyncSkillPoints(conn, tx, characterId, job, level, growType, bonusSp, bonusTp);
                    var targetRemainingSp = skillTreeIndex >= 0
                        ? System.Math.Min(current.Points.RemainingSp, current.Points.RemainingSpPage1)
                        : current.Points.RemainingSp;
                    var targetRemainingTp = skillTreeIndex >= 0
                        ? System.Math.Min(current.Points.RemainingTp, current.Points.RemainingTpPage1)
                        : current.Points.RemainingTp;
                    if (targetRemainingSp == 0 && targetRemainingTp == 0)
                        return Error("当前没有可归零的剩余 SP/TP");

                    var spDelta = -targetRemainingSp;
                    var tpDelta = -targetRemainingTp;
                    var validation = ValidatePointDelta(spDelta, tpDelta, current.Points);
                    if (validation != null)
                        return Error(validation);
                    ApplyBonusPointDelta(conn, tx, characterId, spDelta, tpDelta);
                    bonusSp += spDelta;
                    bonusTp += tpDelta;

                    var synced = SyncSkillPoints(conn, tx, characterId, job, level, growType, bonusSp, bonusTp);
                    tx.Commit();
                    return SpTpResult(characterId, bonusSp, bonusTp, skillTreeIndex, synced);
                }
            }
        }

        internal static string ValidatePointDelta(int spDelta, int tpDelta, SkillPointState points)
        {
            if (points == null)
                return "无法读取技能点状态";

            if (spDelta < 0)
            {
                var page0 = points.TotalSp + spDelta - points.SpentSp;
                var page1 = points.TotalSp + spDelta - points.SpentSpPage1;
                if (page0 < 0 || page1 < 0)
                    return $"减少 SP 会使技能方案剩余点数为负数（第一页 {page0}，第二页 {page1}）";
            }
            if (tpDelta < 0)
            {
                var page0 = points.TotalTp + tpDelta - points.SpentTp;
                var page1 = points.TotalTp + tpDelta - points.SpentTpPage1;
                if (page0 < 0 || page1 < 0)
                    return $"减少 TP 会使技能方案剩余点数为负数（第一页 {page0}，第二页 {page1}）";
            }
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
            byte growType,
            int bonusSp,
            int bonusTp)
        {
            var repository = SqliteCharacterProgressRepository.FromConnectionString(conn.ConnectionString);
            DfoGmTool.ServerCore.Game.Characters.CharacterStatComputer.DecodeGrowType(growType, out var firstGrow, out var secondGrow);
            return SkillStateService.LoadAndSync(
                repository,
                conn,
                tx,
                characterId,
                job,
                level,
                bonusSp,
                bonusTp,
                persist: true,
                firstGrow,
                secondGrow);
        }

        private static object SpTpResult(
            int characterId,
            int bonusSp,
            int bonusTp,
            int skillTreeIndex,
            (DfoGmTool.ServerCore.Game.SelectCharacter.SkillInfoSnapshot Skills, SkillPointState Points) synced)
        {
            var currentPage = ResolveCurrentSkillPage(skillTreeIndex);
            return new
            {
                success = true,
                characterId,
                bonusSp,
                bonusTp,
                skillTreeIndex = skillTreeIndex < 0 ? 255 : skillTreeIndex,
                skillTreeUnlocked = skillTreeIndex >= 0,
                currentSkillPage = currentPage,
                totalSp = synced.Points.TotalSp,
                remainingSp = synced.Points.RemainingSp,
                remainingSpPage0 = synced.Points.RemainingSp,
                remainingSpPage1 = synced.Points.RemainingSpPage1,
                currentRemainingSp = GetRemainingSpForPage(synced.Points, currentPage),
                totalTp = synced.Points.TotalTp,
                remainingTp = synced.Points.RemainingTp,
                remainingTpPage0 = synced.Points.RemainingTp,
                remainingTpPage1 = synced.Points.RemainingTpPage1,
                currentRemainingTp = GetRemainingTpForPage(synced.Points, currentPage),
            };
        }

        private static int ResolveCurrentSkillPage(int skillTreeIndex)
        {
            return skillTreeIndex == 1 ? 1 : 0;
        }

        private static int GetRemainingSpForPage(SkillPointState points, int page)
        {
            if (points == null) return 0;
            return page == 1 ? points.RemainingSpPage1 : points.RemainingSp;
        }

        private static int GetRemainingTpForPage(SkillPointState points, int page)
        {
            if (points == null) return 0;
            return page == 1 ? points.RemainingTpPage1 : points.RemainingTp;
        }

        private static bool TryLoadSpTpBase(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            out byte job,
            out byte level,
            out byte growType,
            out int bonusSp,
            out int bonusTp,
            out int skillTreeIndex)
        {
            job = 0;
            level = 0;
            growType = 0;
            bonusSp = 0;
            bonusTp = 0;
            skillTreeIndex = -1;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
SELECT c.job, c.level, c.grow_type, c.bonus_sp, c.bonus_tp,
       COALESCE(s.skill_tree_index, -1)
FROM characters c
LEFT JOIN character_subtype1_fields s ON s.character_id = c.character_id
WHERE c.character_id = @cid;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                        return false;
                    job = (byte)reader.GetInt32(0);
                    level = (byte)reader.GetInt32(1);
                    growType = (byte)reader.GetInt32(2);
                    bonusSp = reader.GetInt32(3);
                    bonusTp = reader.GetInt32(4);
                    skillTreeIndex = reader.GetInt32(5);
                    return true;
                }
            }
        }
    }
}
