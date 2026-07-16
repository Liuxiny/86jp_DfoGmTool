using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Quests
{
    public sealed class QuestRepository
    {
        private readonly string _connStr;

        public QuestRepository(string connStr)
        {
            _connStr = connStr;
        }

        public List<ActiveQuest> LoadActiveQuests(int characterId)
        {
            using (var conn = new SqliteConnection(_connStr))
            {
                conn.Open();
                return LoadActiveQuests(conn, null, characterId);
            }
        }

        public static List<ActiveQuest> LoadActiveQuests(SqliteConnection conn, SqliteTransaction tx, int characterId)
        {
            var list = new List<ActiveQuest>();
            using (var cmd = new SqliteCommand(
                "SELECT slot, quest_id, trigger_value FROM character_active_quests WHERE character_id=@cid ORDER BY slot", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new ActiveQuest { Slot = r.GetInt32(0), QuestId = (ushort)r.GetInt32(1), TriggerValue = (uint)r.GetInt64(2) });
                }
            }

            return list;
        }

        public void SaveActiveQuests(int characterId, List<ActiveQuest> quests)
        {
            using (var conn = new SqliteConnection(_connStr))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    foreach (var q in quests)
                        InsertActiveQuest(conn, tx, characterId, q.Slot, q.QuestId, q.TriggerValue);
                    tx.Commit();
                }
            }
        }

        public static void InsertActiveQuest(SqliteConnection conn, SqliteTransaction tx, int characterId, int slot, ushort questId, uint triggerValue)
        {
            using (var cmd = new SqliteCommand(
                "INSERT OR REPLACE INTO character_active_quests (character_id, slot, quest_id, trigger_value) VALUES (@cid, @s, @qid, @tv)",
                conn,
                tx))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@s", slot);
                cmd.Parameters.AddWithValue("@qid", (int)questId);
                cmd.Parameters.AddWithValue("@tv", (long)triggerValue);
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteActiveQuest(int characterId, int slot)
        {
            using (var conn = new SqliteConnection(_connStr))
            {
                conn.Open();
                DeleteActiveQuest(conn, null, characterId, slot);
            }
        }

        public static void DeleteActiveQuest(SqliteConnection conn, SqliteTransaction tx, int characterId, int slot)
        {
            using (var cmd = new SqliteCommand(
                "DELETE FROM character_active_quests WHERE character_id=@cid AND slot=@s", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@s", slot);
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateTriggerValue(int characterId, int slot, uint triggerValue)
        {
            using (var conn = new SqliteConnection(_connStr))
            {
                conn.Open();
                UpdateTriggerValue(conn, null, characterId, slot, triggerValue);
            }
        }

        public static void UpdateTriggerValue(SqliteConnection conn, SqliteTransaction tx, int characterId, int slot, uint triggerValue)
        {
            using (var cmd = new SqliteCommand(
                "UPDATE character_active_quests SET trigger_value=@tv WHERE character_id=@cid AND slot=@s", conn, tx))
            {
                cmd.Parameters.AddWithValue("@tv", (long)triggerValue);
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@s", slot);
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateTriggerValues(int characterId, IReadOnlyList<ActiveQuest> quests)
        {
            if (quests == null || quests.Count == 0)
                return;

            using (var conn = new SqliteConnection(_connStr))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    foreach (var q in quests)
                        UpdateTriggerValue(conn, tx, characterId, q.Slot, q.TriggerValue);
                    tx.Commit();
                }
            }
        }

        public bool IsQuestCleared(int characterId, int questId)
        {
            return ReadClearedFlagValue(characterId, questId) != 0;
        }

        public static bool IsQuestCleared(SqliteConnection conn, SqliteTransaction tx, int characterId, int questId)
        {
            return ReadClearedFlagValue(conn, tx, characterId, questId) != 0;
        }

        public int ReadClearedFlagValue(int characterId, int questId)
        {
            using (var conn = new SqliteConnection(_connStr))
            {
                conn.Open();
                return ReadClearedFlagValue(conn, null, characterId, questId);
            }
        }

        public static int ReadClearedFlagValue(SqliteConnection conn, SqliteTransaction tx, int characterId, int questId)
        {
            using (var cmd = new SqliteCommand(
                "SELECT flag_value FROM character_invisible_falgs WHERE character_id=@cid AND slot_index=@idx",
                conn,
                tx))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@idx", questId);
                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 0;
            }
        }

        public Dictionary<int, int> LoadClearedFlags(int characterId)
        {
            using (var conn = new SqliteConnection(_connStr))
            {
                conn.Open();
                return LoadClearedFlags(conn, null, characterId);
            }
        }

        public static Dictionary<int, int> LoadClearedFlags(SqliteConnection conn, SqliteTransaction tx, int characterId)
        {
            var flags = new Dictionary<int, int>();
            using (var cmd = new SqliteCommand(
                "SELECT slot_index, flag_value FROM character_invisible_falgs WHERE character_id=@cid ORDER BY slot_index", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int slotIndex = r.GetInt32(0), flagValue = r.GetInt32(1);
                        if (flagValue != 0)
                            flags[slotIndex] = flagValue;
                    }
                }
            }

            return flags;
        }

        public static List<KeyValuePair<int, int>> LoadAllFlagEntries(SqliteConnection conn, SqliteTransaction tx, int characterId)
        {
            var entries = new List<KeyValuePair<int, int>>();
            using (var cmd = new SqliteCommand(
                "SELECT slot_index, flag_value FROM character_invisible_falgs WHERE character_id=@cid ORDER BY slot_index", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        entries.Add(new KeyValuePair<int, int>(r.GetInt32(0), r.GetInt32(1)));
                }
            }

            return entries;
        }

        public static void MarkQuestCleared(SqliteConnection conn, SqliteTransaction tx, int characterId, ushort questId, int flagValue = 1)
        {
            if (flagValue == 0)
                flagValue = 1;

            using (var cmd = new SqliteCommand(
                "INSERT OR REPLACE INTO character_invisible_falgs (character_id, slot_index, flag_value) VALUES (@cid, @idx, @flag)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@idx", (int)questId);
                cmd.Parameters.AddWithValue("@flag", flagValue);
                cmd.ExecuteNonQuery();
            }

            uint requiredLen = (uint)(questId + 1);
            using (var cmd = new SqliteCommand(
                "UPDATE character_init_flags SET charac_invisible_falgs_payload_len = MAX(charac_invisible_falgs_payload_len, @len) WHERE character_id = @cid", conn, tx))
            {
                cmd.Parameters.AddWithValue("@len", (long)requiredLen);
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.ExecuteNonQuery();
            }
        }

        public static void DeleteClearedFlag(SqliteConnection conn, SqliteTransaction tx, int characterId, ushort questId)
        {
            using (var cmd = new SqliteCommand(
                "DELETE FROM character_invisible_falgs WHERE character_id=@cid AND slot_index=@idx",
                conn,
                tx))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@idx", (int)questId);
                cmd.ExecuteNonQuery();
            }
        }

        public static void ReplaceAllClearedFlags(SqliteConnection conn, SqliteTransaction tx, int characterId, IReadOnlyList<KeyValuePair<int, int>> flags)
        {
            using (var cmd = new SqliteCommand("DELETE FROM character_invisible_falgs WHERE character_id = @cid", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.ExecuteNonQuery();
            }

            if (flags == null)
                return;

            foreach (var flag in flags)
            {
                using (var cmd = new SqliteCommand(
                    "INSERT INTO character_invisible_falgs (character_id, slot_index, flag_value) VALUES (@cid, @si, @fv)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    cmd.Parameters.AddWithValue("@si", flag.Key);
                    cmd.Parameters.AddWithValue("@fv", flag.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}
