using DfoGmTool.ServerCore.Game.SelectCharacter;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.CharacterData
{
    internal sealed class CharacterAchievementProgressRepository
    {
        private const int RecordSize = 12;
        private readonly string _connectionString;

        internal CharacterAchievementProgressRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        internal AchievementCompleteSnapshot LoadSnapshot(int characterId)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                return LoadSnapshot(connection, null, characterId);
            }
        }

        internal AchievementCompleteSnapshot LoadSnapshot(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            var snapshot = new AchievementCompleteSnapshot();
            foreach (var entry in LoadEntries(connection, transaction, characterId).Values)
                snapshot.Entries.Add(entry);
            return snapshot;
        }

        internal AchievementCompleteEntrySnapshot LoadOrCreateEntry(SqliteConnection connection, SqliteTransaction transaction, int characterId, int questId, ushort initialRemain1)
        {
            var entries = LoadEntries(connection, transaction, characterId);
            if (entries.TryGetValue(questId, out var entry))
                return entry;

            entry = new AchievementCompleteEntrySnapshot
            {
                AchievementId = questId,
                P1 = initialRemain1,
                P2 = 0,
                P3 = 0,
                P4 = 0,
            };
            entries[questId] = entry;
            SaveEntries(connection, transaction, characterId, entries);
            return entry;
        }

        internal void SaveEntry(SqliteConnection connection, SqliteTransaction transaction, int characterId, AchievementCompleteEntrySnapshot entry)
        {
            var entries = LoadEntries(connection, transaction, characterId);
            entries[entry.AchievementId] = entry;
            SaveEntries(connection, transaction, characterId, entries);
        }

        private static Dictionary<int, AchievementCompleteEntrySnapshot> LoadEntries(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            var entries = new Dictionary<int, AchievementCompleteEntrySnapshot>();
            byte[] blob = null;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT achievement FROM character_achievement WHERE character_id = @cid;";
                command.Parameters.AddWithValue("@cid", characterId);
                var value = command.ExecuteScalar();
                if (value != null && value != DBNull.Value)
                    blob = value as byte[];
            }

            if (blob == null)
                return entries;

            for (var off = 0; off + RecordSize <= blob.Length; off += RecordSize)
            {
                var questId = BitConverter.ToInt32(blob, off);
                if (questId <= 0)
                    continue;

                entries[questId] = new AchievementCompleteEntrySnapshot
                {
                    AchievementId = questId,
                    P1 = BitConverter.ToUInt16(blob, off + 4),
                    P2 = BitConverter.ToUInt16(blob, off + 6),
                    P3 = BitConverter.ToUInt16(blob, off + 8),
                    P4 = BitConverter.ToUInt16(blob, off + 10),
                };
            }

            return entries;
        }

        private static void SaveEntries(SqliteConnection connection, SqliteTransaction transaction, int characterId, Dictionary<int, AchievementCompleteEntrySnapshot> entries)
        {
            var blob = new byte[entries.Count * RecordSize];
            var off = 0;
            foreach (var entry in entries.Values)
            {
                BitConverter.GetBytes(entry.AchievementId).CopyTo(blob, off);
                BitConverter.GetBytes(entry.P1).CopyTo(blob, off + 4);
                BitConverter.GetBytes(entry.P2).CopyTo(blob, off + 6);
                BitConverter.GetBytes(entry.P3).CopyTo(blob, off + 8);
                BitConverter.GetBytes(entry.P4).CopyTo(blob, off + 10);
                off += RecordSize;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO character_achievement(character_id, format_version, achievement, last_update_time, updated_at)
VALUES(@cid, 1, @blob, strftime('%s','now'), CURRENT_TIMESTAMP)
ON CONFLICT(character_id) DO UPDATE SET
    achievement = excluded.achievement,
    last_update_time = excluded.last_update_time,
    updated_at = CURRENT_TIMESTAMP;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@blob", blob);
                command.ExecuteNonQuery();
            }
        }
    }
}
