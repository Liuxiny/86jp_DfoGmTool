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
                    "SELECT achievement_id, p1, p2, p3, p4 FROM character_achievement_complete WHERE character_id = @cid ORDER BY sort_order", conn))
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
                    using (var cmd = new SqliteCommand("DELETE FROM character_achievement_complete WHERE character_id = @cid", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        cmd.ExecuteNonQuery();
                    }
                    for (int i = 0; i < snapshot.Entries.Count; i++)
                    {
                        var e = snapshot.Entries[i];
                        using (var cmd = new SqliteCommand(
                            "INSERT INTO character_achievement_complete (character_id, sort_order, achievement_id, p1, p2, p3, p4) VALUES (@cid, @ord, @aid, @p1, @p2, @p3, @p4)", conn, tx))
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
                cmd.CommandText = "SELECT p1, p2, p3, p4 FROM character_achievement_complete WHERE character_id=@cid AND achievement_id=@aid;";
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
INSERT INTO character_achievement_complete(character_id, sort_order, achievement_id, p1, p2, p3, p4)
VALUES(@cid,
       (SELECT COALESCE(MAX(sort_order),-1)+1 FROM character_achievement_complete WHERE character_id=@cid),
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

        internal List<AchievementListChunkSnapshot> LoadAchievementChunks(int characterId)
        {
            var chunks = new List<AchievementListChunkSnapshot>();
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqliteCommand(
                    "SELECT chunk_index, mode_byte, owner_id16, entries_blob FROM character_achievement_chunks WHERE character_id = @cid ORDER BY chunk_index", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var chunk = new AchievementListChunkSnapshot
                            {
                                ChunkIndex = reader.GetInt32(0),
                                ModeByte = (byte)reader.GetInt32(1),
                                OwnerId16 = (ushort)reader.GetInt32(2),
                            };
                            var blob = reader.IsDBNull(3) ? null : (byte[])reader[3];
                            if (blob != null)
                                DeserializeAchievementEntries(blob, chunk.Entries);
                            chunks.Add(chunk);
                        }
                    }
                }
            }
            return chunks;
        }

        internal void SaveAchievementChunks(int characterId, List<AchievementListChunkSnapshot> chunks)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = new SqliteCommand("DELETE FROM character_achievement_chunks WHERE character_id = @cid", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        cmd.ExecuteNonQuery();
                    }
                    foreach (var chunk in chunks)
                    {
                        using (var cmd = new SqliteCommand(
                            "INSERT INTO character_achievement_chunks (character_id, chunk_index, mode_byte, owner_id16, entries_blob) VALUES (@cid, @ci, @mb, @oid, @eb)", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@cid", characterId);
                            cmd.Parameters.AddWithValue("@ci", chunk.ChunkIndex);
                            cmd.Parameters.AddWithValue("@mb", (int)chunk.ModeByte);
                            cmd.Parameters.AddWithValue("@oid", (int)chunk.OwnerId16);
                            cmd.Parameters.AddWithValue("@eb", SerializeAchievementEntries(chunk.Entries));
                            cmd.ExecuteNonQuery();
                        }
                    }
                    tx.Commit();
                }
            }
        }

        private static byte[] SerializeAchievementEntries(List<AchievementListEntrySnapshot> entries)
        {
            var buf = new byte[entries.Count * 22];
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                int off = i * 22;
                Array.Copy(BitConverter.GetBytes(e.AchievementId), 0, buf, off, 2); off += 2;
                Array.Copy(BitConverter.GetBytes(e.ValueA), 0, buf, off, 4); off += 4;
                Array.Copy(BitConverter.GetBytes(e.ValueB), 0, buf, off, 4); off += 4;
                buf[off++] = e.CategoryByte;
                Array.Copy(BitConverter.GetBytes(e.LinkId), 0, buf, off, 2); off += 2;
                buf[off++] = e.Flag0;
                Array.Copy(BitConverter.GetBytes(e.ValueC), 0, buf, off, 4); off += 4;
                buf[off++] = e.Flag1;
                buf[off++] = e.Flag2;
                Array.Copy(BitConverter.GetBytes(e.TailValue), 0, buf, off, 2);
            }
            return buf;
        }

        private static void DeserializeAchievementEntries(byte[] blob, List<AchievementListEntrySnapshot> entries)
        {
            for (int off = 0; off + 22 <= blob.Length; off += 22)
            {
                entries.Add(new AchievementListEntrySnapshot
                {
                    AchievementId = BitConverter.ToUInt16(blob, off),
                    ValueA = BitConverter.ToInt32(blob, off + 2),
                    ValueB = BitConverter.ToInt32(blob, off + 6),
                    CategoryByte = blob[off + 10],
                    LinkId = BitConverter.ToUInt16(blob, off + 11),
                    Flag0 = blob[off + 13],
                    ValueC = BitConverter.ToInt32(blob, off + 14),
                    Flag1 = blob[off + 18],
                    Flag2 = blob[off + 19],
                    TailValue = BitConverter.ToUInt16(blob, off + 20),
                });
            }
        }
    }
}
