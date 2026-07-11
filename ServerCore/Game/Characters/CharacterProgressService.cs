using DfoGmTool.ServerCore.Game.CharacterData;
using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;
using System;

namespace DfoGmTool.ServerCore.Game.Characters
{
    public static class CharacterProgressService
    {
        public static bool PersistLevelAndExp(int characterId, byte level, uint exp)
        {
            return PersistLevelAndExp(
                characterId,
                level,
                exp,
                ServerPaths.DatabasePath,
                ServerPaths.SchemaFilePath);
        }

        public static bool PersistLevelAndExp(
            int characterId,
            byte level,
            uint exp,
            string databasePath,
            string schemaFilePath)
        {
            var connectionString = SqliteDatabaseBootstrap.Initialize(databasePath, schemaFilePath);
            return PersistLevelAndExp(connectionString, characterId, level, exp);
        }

        public static bool PersistLevelAndExp(
            string connectionString,
            int characterId,
            byte level,
            uint exp)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("connectionString is empty", nameof(connectionString));

            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                return PersistLevelAndExp(conn, characterId, level, exp);
            }
        }

        // 等级/经验写与战斗属性写必须同生共死: 崩在中间会留下"等级已升属性没跟上"的
        // 不一致状态(历史上启动时的全量重算就是为修这类存量而生)。显式事务包住两步。
        private static bool PersistLevelAndExp(
            SqliteConnection conn,
            int characterId,
            byte level,
            uint exp)
        {
            using (var tx = conn.BeginTransaction())
            {
                var updated = PersistLevelAndExp(conn, tx, characterId, level, exp);
                tx.Commit();
                return updated;
            }
        }

        // (conn, tx) 变体: 并入调用方的事务, 由调用方提交/回滚。
        // 任务完成结算走这里, 使"任务奖励 + 经验/等级/战斗属性"整体原子。
        public static bool PersistLevelAndExp(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            byte level,
            uint exp)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
UPDATE characters
SET level = @lvl, exp = @exp, updated_at = CURRENT_TIMESTAMP
WHERE character_id = @cid;";
                cmd.Parameters.AddWithValue("@lvl", (int)level);
                cmd.Parameters.AddWithValue("@exp", (long)exp);
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.ExecuteNonQuery();
            }

            byte job;
            byte growType;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT job, grow_type FROM characters WHERE character_id = @cid;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                        return false;   // 角色不存在: 随事务回滚, 不留半截写入

                    job = (byte)reader.GetInt32(0);
                    growType = (byte)reader.GetInt32(1);
                }
            }

            CharacterStatComputer.DecodeGrowType(growType, out int firstGrow, out int secondGrow);
            var combatStats = CharacterStatComputer.BuildAdditionalInfo(job, level, firstGrow, secondGrow);
            return SqliteSubtype1Repository.UpdateCombatStatsOnConnection(conn, characterId, combatStats, tx) > 0;
        }
    }
}
