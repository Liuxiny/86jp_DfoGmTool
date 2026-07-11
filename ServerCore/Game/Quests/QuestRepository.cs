// GM瘦身拷贝: 相对服务端原版删除了进行中任务区段(依赖 ActiveQuest); 保留成员与原版逐字一致
using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Quests
{
    // 任务两张表的唯一数据访问点:
    //   character_active_quests   进行中任务(槽位/任务号/触发器值)
    //   character_invisible_falgs 完成标记(slot_index=任务号, flag_value=完成值/问答分支值)
    // 这两张表的 SQL 只出现在这个文件里。需要并入外部事务的操作提供
    // (conn, tx) 静态变体; 实例方法自开连接, 供没有现成事务的调用方使用。
    public sealed class QuestRepository
    {
        private readonly string _connStr;

        public QuestRepository(string connStr)
        {
            _connStr = connStr;
        }

        // GM瘦身拷贝: 此处删除了进行中任务区段(LoadActiveQuests/SaveActiveQuests/InsertActiveQuest/
        // DeleteActiveQuest/UpdateTriggerValue/UpdateTriggerValues, 依赖未拷贝的 ActiveQuest 类)

        // ── 完成标记 ──

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

        // 全部非零完成标记(任务号 → 完成值), 供可接任务计算与选角初始化使用。
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

        // 按存储原样(含零值)全量读, 供选角初始化快照使用 -- 快照要求逐字节回放,
        // 与 LoadClearedFlags 的"只看非零"语义不同。
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

        // 写完成标记的同时抬高 init 载荷长度水位, 保证选角初始化包能覆盖到该任务号。
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

        // 初始化路径的整表重建(先清后写), 供选角种子数据载入使用。
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
