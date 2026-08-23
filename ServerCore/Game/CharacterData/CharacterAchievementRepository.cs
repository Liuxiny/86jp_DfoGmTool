using DfoGmTool.ServerCore.Game.SelectCharacter;
using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.CharacterData
{
    internal sealed class CharacterAchievementRepository
    {
        private readonly string _connectionString;

        internal CharacterAchievementRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        internal AchievementCompleteSnapshot LoadAchievementComplete(int characterId)
        {
            var snapshot = new AchievementCompleteSnapshot();
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqliteCommand(
                    "SELECT achievement_id, p1, p2, p3, p4 FROM character_achievements WHERE character_id = @cid ORDER BY sort_order", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            snapshot.Entries.Add(new AchievementCompleteEntrySnapshot
                            {
                                AchievementId = reader.GetInt32(0),
                                P1 = (ushort)reader.GetInt32(1),
                                P2 = (ushort)reader.GetInt32(2),
                                P3 = (ushort)reader.GetInt32(3),
                                P4 = (ushort)reader.GetInt32(4),
                            });
                        }
                    }
                }
            }
            return snapshot;
        }

        internal void SaveAchievementComplete(int characterId, AchievementCompleteSnapshot snapshot)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = new SqliteCommand("DELETE FROM character_achievements WHERE character_id = @cid", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        cmd.ExecuteNonQuery();
                    }
                    for (int i = 0; i < snapshot.Entries.Count; i++)
                    {
                        var e = snapshot.Entries[i];
                        using (var cmd = new SqliteCommand(
                            "INSERT INTO character_achievements (character_id, sort_order, achievement_id, p1, p2, p3, p4) VALUES (@cid, @ord, @aid, @p1, @p2, @p3, @p4)", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@cid", characterId);
                            cmd.Parameters.AddWithValue("@ord", i);
                            cmd.Parameters.AddWithValue("@aid", e.AchievementId);
                            cmd.Parameters.AddWithValue("@p1", (int)e.P1);
                            cmd.Parameters.AddWithValue("@p2", (int)e.P2);
                            cmd.Parameters.AddWithValue("@p3", (int)e.P3);
                            cmd.Parameters.AddWithValue("@p4", (int)e.P4);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    tx.Commit();
                }
            }
        }

        // 运行时进度按条 upsert; 与选角快照共用同一张表(唯一存储)
        internal AchievementCompleteEntrySnapshot LoadOrCreateEntry(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int questId,
            ushort initialRemain1)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT p1, p2, p3, p4 FROM character_achievements WHERE character_id=@cid AND achievement_id=@aid;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@aid", questId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new AchievementCompleteEntrySnapshot
                        {
                            AchievementId = questId,
                            P1 = (ushort)reader.GetInt32(0),
                            P2 = (ushort)reader.GetInt32(1),
                            P3 = (ushort)reader.GetInt32(2),
                            P4 = (ushort)reader.GetInt32(3),
                        };
                    }
                }
            }

            var entry = new AchievementCompleteEntrySnapshot
            {
                AchievementId = questId,
                P1 = initialRemain1,
                P2 = 0,
                P3 = 0,
                P4 = 0,
            };
            SaveEntry(connection, transaction, characterId, entry);
            return entry;
        }

        internal void SaveEntry(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            AchievementCompleteEntrySnapshot entry)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
INSERT INTO character_achievements(character_id, sort_order, achievement_id, p1, p2, p3, p4)
VALUES(@cid,
       (SELECT COALESCE(MAX(sort_order),-1)+1 FROM character_achievements WHERE character_id=@cid),
       @aid, @p1, @p2, @p3, @p4)
ON CONFLICT(character_id, achievement_id)
DO UPDATE SET p1=excluded.p1, p2=excluded.p2, p3=excluded.p3, p4=excluded.p4;";
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@aid", entry.AchievementId);
                cmd.Parameters.AddWithValue("@p1", (int)entry.P1);
                cmd.Parameters.AddWithValue("@p2", (int)entry.P2);
                cmd.Parameters.AddWithValue("@p3", (int)entry.P3);
                cmd.Parameters.AddWithValue("@p4", (int)entry.P4);
                cmd.ExecuteNonQuery();
            }
        }

    }
}
