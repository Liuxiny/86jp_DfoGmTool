// GM瘦身拷贝: 相对服务端原版删除了 SeedFromSnapshot 与其私有依赖 LoadSkillOwnerState
// (依赖未拷贝的 SelectCharacterInitializationSnapshot); 其余成员与服务端逐字一致(命名空间重写除外)。
using DfoGmTool.ServerCore.Game.SelectCharacter;
using DfoGmTool.ServerCore.Infrastructure;
using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Skills = DfoGmTool.ServerCore.Game.Skills;

namespace DfoGmTool.ServerCore.Game.CharacterData
{
    internal sealed class CharacterProgressSnapshot
    {
        internal CharacterProgressSnapshot(
            int accountId,
            byte job,
            byte level,
            uint exp,
            int bonusSp,
            int bonusTp,
            bool isHardcore,
            byte growType = 0)
        {
            AccountId = accountId;
            Job = job;
            Level = level;
            Exp = exp;
            BonusSp = bonusSp;
            BonusTp = bonusTp;
            IsHardcore = isHardcore;
            GrowType = growType;
        }

        internal int AccountId { get; }
        internal byte Job { get; }
        internal byte Level { get; }
        internal uint Exp { get; }
        internal int BonusSp { get; }
        internal int BonusTp { get; }
        internal bool IsHardcore { get; }
        internal byte GrowType { get; }
    }

    public sealed class SqliteCharacterProgressRepository
    {
        private readonly string _connectionString;

        public SqliteCharacterProgressRepository(string databasePath, string schemaFilePath)
        {
            _connectionString = SqliteDatabaseBootstrap.Initialize(databasePath, schemaFilePath);
        }

        private SqliteCharacterProgressRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public static SqliteCharacterProgressRepository FromConnectionString(string connectionString)
        {
            return new SqliteCharacterProgressRepository(connectionString);
        }

        internal CharacterProgressSnapshot LoadProgressSnapshot(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT c.account_id, c.job, c.level, c.exp, c.bonus_sp, c.bonus_tp,
       COALESCE(f.is_hardcore_mode, 0), c.grow_type
FROM characters c
LEFT JOIN character_subtype0_fields f ON f.character_id=c.character_id
WHERE c.character_id=@cid AND c.delete_flag=0;";
                command.Parameters.AddWithValue("@cid", characterId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    return new CharacterProgressSnapshot(
                        reader.GetInt32(0),
                        (byte)Math.Max(0, Math.Min(byte.MaxValue, reader.GetInt32(1))),
                        (byte)Math.Max(0, Math.Min(byte.MaxValue, reader.GetInt32(2))),
                        (uint)Math.Max(0L, Math.Min(uint.MaxValue, reader.GetInt64(3))),
                        reader.GetInt32(4),
                        reader.GetInt32(5),
                        reader.GetInt32(6) != 0,
                        (byte)Math.Max(0, Math.Min(byte.MaxValue, reader.GetInt32(7))));
                }
            }
        }

        public SkillInfoSnapshot LoadSkills(int characterId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                return LoadSkills(conn, null, characterId);
            }
        }

        internal SkillInfoSnapshot LoadSkills(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId)
        {
            if (conn == null) throw new ArgumentNullException(nameof(conn));

            // page_header/tail 不再从 DB 读——由 SkillPointLedger 在发包前现算后写入 snapshot。
            var snapshot = new SkillInfoSnapshot();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT page_index, slot, skill_id, level, extra_values FROM character_skills WHERE character_id = @cid ORDER BY page_index, slot";
                cmd.Parameters.AddWithValue("@cid", characterId);
                using (var reader = cmd.ExecuteReader())
                {
                    var pages = new Dictionary<int, SkillInfoPageSnapshot>();
                    while (reader.Read())
                    {
                        var pageIdx = reader.GetInt32(0);
                        if (!pages.TryGetValue(pageIdx, out var page))
                        {
                            page = new SkillInfoPageSnapshot();
                            pages[pageIdx] = page;
                        }
                        int slot = reader.GetInt32(1);
                        if (slot < 0) continue;
                        var entry = new SkillInfoEntrySnapshot
                        {
                            Slot = (byte)slot,
                            SkillId = (ushort)reader.GetInt32(2),
                            Level = (byte)reader.GetInt32(3),
                        };
                        var extraBlob = reader.IsDBNull(4) ? null : (byte[])reader[4];
                        if (extraBlob != null)
                            foreach (var b in extraBlob)
                                entry.ExtraValues.Add(b);
                        page.Entries.Add(entry);
                    }
                    for (int i = 0; i < 2; i++)
                        snapshot.Pages.Add(pages.ContainsKey(i) ? pages[i] : new SkillInfoPageSnapshot());
                }
            }
            return snapshot;
        }

        // 新签名: 点数由 Ledger 派生, 不再写 character_skill_points。
        public void SaveSkillProgress(int characterId, SkillInfoSnapshot snapshot)
        {
            if (snapshot == null) return;
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    SaveSkillsCore(conn, tx, characterId, snapshot);
                    tx.Commit();
                }
            }
        }

        public void SaveSkillProgress(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            SkillInfoSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (conn == null) throw new ArgumentNullException(nameof(conn));
            if (tx == null) throw new ArgumentNullException(nameof(tx));

            SaveSkillsCore(conn, tx, characterId, snapshot);
        }

        private static void SaveSkillsCore(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            SkillInfoSnapshot snapshot)
        {
            using (var cmd = new SqliteCommand("DELETE FROM character_skills WHERE character_id = @cid", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.ExecuteNonQuery();
            }

            for (int pageIdx = 0; pageIdx < snapshot.Pages.Count; pageIdx++)
            {
                var page = snapshot.Pages[pageIdx];
                foreach (var entry in page.Entries)
                {
                    using (var cmd = new SqliteCommand(
                        "INSERT INTO character_skills (character_id, page_index, slot, skill_id, level, extra_values) VALUES (@cid, @page, @slot, @sid, @lvl, @extra)", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        cmd.Parameters.AddWithValue("@page", pageIdx);
                        cmd.Parameters.AddWithValue("@slot", (int)entry.Slot);
                        cmd.Parameters.AddWithValue("@sid", (int)entry.SkillId);
                        cmd.Parameters.AddWithValue("@lvl", (int)entry.Level);
                        cmd.Parameters.AddWithValue("@extra", entry.ExtraValues.Count > 0 ? (object)entry.ExtraValues.ToArray() : DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        // character_skill_points 已退役(迁移23删表)——SP/TP 全部由 Ledger 从已学技能派生。

        public bool HasSkills(int characterId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM character_skills WHERE character_id = @cid", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
        }

        public int ClearAllSkillCommands(int characterId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqliteCommand(
                    "UPDATE character_skills SET extra_values = NULL WHERE character_id = @cid AND extra_values IS NOT NULL", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    return cmd.ExecuteNonQuery();
                }
            }
        }

        public int UpdateSkillCommand(int characterId, ushort skillId, byte[] commandBytes)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqliteCommand(
                    "UPDATE character_skills SET extra_values = @extra WHERE character_id = @cid AND skill_id = @sid", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    cmd.Parameters.AddWithValue("@sid", (int)skillId);
                    cmd.Parameters.AddWithValue("@extra", commandBytes != null && commandBytes.Length > 0
                        ? (object)commandBytes
                        : DBNull.Value);
                    return cmd.ExecuteNonQuery();
                }
            }
        }

        public void SwapSkillSlot(int characterId, int page, int slot1, int slot2)
        {
            if (slot1 == slot2) return;
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    MoveSkillSlot(conn, tx, characterId, page, slot1, -1);    
                    MoveSkillSlot(conn, tx, characterId, page, slot2, slot1); 
                    MoveSkillSlot(conn, tx, characterId, page, -1, slot2);    
                    tx.Commit();
                }
            }
        }

        public bool MoveSkillToSlot(int characterId, int page, ushort skillId, int toSlot)
        {
            if (characterId <= 0)
                return false;

            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    var changed = MoveSkillIdToSlot(conn, tx, characterId, page, skillId, toSlot) > 0;
                    tx.Commit();
                    return changed;
                }
            }
        }

        public List<SkillSlotRecord> LoadSkillSlots(int characterId, int page)
        {
            var rows = new List<SkillSlotRecord>();
            if (characterId <= 0)
                return rows;

            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqliteCommand(
                    "SELECT slot, skill_id FROM character_skills WHERE character_id=@cid AND page_index=@page ORDER BY slot", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    cmd.Parameters.AddWithValue("@page", page);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            rows.Add(new SkillSlotRecord
                            {
                                Slot = reader.GetInt32(0),
                                SkillId = (ushort)reader.GetInt32(1),
                            });
                        }
                    }
                }
            }

            return rows;
        }

        public int MoveSkillsToSlots(int characterId, int page, IReadOnlyList<SkillSlotMove> moves)
        {
            if (characterId <= 0 || moves == null || moves.Count == 0)
                return 0;

            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    var changed = 0;
                    foreach (var move in moves)
                    {
                        changed += MoveSkillIdToSlot(conn, tx, characterId, page, move.SkillId, move.ToSlot);
                    }
                    tx.Commit();
                    return changed;
                }
            }
        }

        private static void MoveSkillSlot(SqliteConnection conn, SqliteTransaction tx, int cid, int page, int fromSlot, int toSlot)
        {
            using (var cmd = new SqliteCommand(
                "UPDATE character_skills SET slot = @to WHERE character_id = @cid AND page_index = @page AND slot = @from", conn, tx))
            {
                cmd.Parameters.AddWithValue("@to", toSlot);
                cmd.Parameters.AddWithValue("@cid", cid);
                cmd.Parameters.AddWithValue("@page", page);
                cmd.Parameters.AddWithValue("@from", fromSlot);
                cmd.ExecuteNonQuery();
            }
        }

        private static int MoveSkillIdToSlot(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            int page,
            ushort skillId,
            int toSlot)
        {
            MoveSkillSlot(conn, tx, characterId, page, toSlot, -1);
            using (var cmd = new SqliteCommand(
                "UPDATE character_skills SET slot = @to WHERE character_id = @cid AND page_index = @page AND skill_id = @sid", conn, tx))
            {
                cmd.Parameters.AddWithValue("@to", toSlot);
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@page", page);
                cmd.Parameters.AddWithValue("@sid", (int)skillId);
                return cmd.ExecuteNonQuery();
            }
        }

        public CreatureItemListSnapshot LoadCreatures(int characterId)
        {
            var snapshot = new CreatureItemListSnapshot();
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqliteCommand(
                    "SELECT creature_key, field04, mode_flag, progress_value, mode1_field0a, mode1_field0b, field_after_value, creature_text, tail_flag, extra_json FROM character_creatures WHERE character_id = @cid ORDER BY sort_order", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            snapshot.Entries.Add(new CreatureItemEntrySnapshot
                            {
                                CreatureKey = reader.GetInt32(0),
                                Field04 = (byte)reader.GetInt32(1),
                                ModeFlag = (byte)reader.GetInt32(2),
                                ProgressValue32 = reader.GetInt32(3),
                                Mode1Field0A = (byte)reader.GetInt32(4),
                                Mode1Field0B = (byte)reader.GetInt32(5),
                                FieldAfterValue32 = (byte)reader.GetInt32(6),
                                CreatureTextBytes = reader.IsDBNull(7) ? new byte[0] : (byte[])reader[7],
                                TailFlag = (byte)reader.GetInt32(8),
                                ExtraJson = reader.IsDBNull(9) ? "{}" : reader.GetString(9),
                            });
                        }
                    }
                }
            }
            return snapshot;
        }

        public void SaveCreatures(int characterId, CreatureItemListSnapshot snapshot)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = new SqliteCommand("DELETE FROM character_creatures WHERE character_id = @cid", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        cmd.ExecuteNonQuery();
                    }

                    for (int i = 0; i < snapshot.Entries.Count; i++)
                    {
                        var entry = snapshot.Entries[i];
                        using (var cmd = new SqliteCommand(
                            "INSERT INTO character_creatures (character_id, sort_order, creature_key, field04, mode_flag, progress_value, mode1_field0a, mode1_field0b, field_after_value, creature_text, tail_flag, extra_json) VALUES (@cid, @ord, @key, @f04, @mf, @pv, @m0a, @m0b, @fav, @txt, @tf, @extra)", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@cid", characterId);
                            cmd.Parameters.AddWithValue("@ord", i);
                            cmd.Parameters.AddWithValue("@key", entry.CreatureKey);
                            cmd.Parameters.AddWithValue("@f04", (int)entry.Field04);
                            cmd.Parameters.AddWithValue("@mf", (int)entry.ModeFlag);
                            cmd.Parameters.AddWithValue("@pv", entry.ProgressValue32);
                            cmd.Parameters.AddWithValue("@m0a", (int)entry.Mode1Field0A);
                            cmd.Parameters.AddWithValue("@m0b", (int)entry.Mode1Field0B);
                            cmd.Parameters.AddWithValue("@fav", (int)entry.FieldAfterValue32);
                            cmd.Parameters.AddWithValue("@txt", entry.CreatureTextBytes != null && entry.CreatureTextBytes.Length > 0 ? (object)entry.CreatureTextBytes : DBNull.Value);
                            cmd.Parameters.AddWithValue("@tf", (int)entry.TailFlag);
                            cmd.Parameters.AddWithValue("@extra", string.IsNullOrWhiteSpace(entry.ExtraJson) ? "{}" : entry.ExtraJson);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    tx.Commit();
                }
            }
        }

        public bool HasCreatures(int characterId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM character_creatures WHERE character_id = @cid", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
        }

        

        // GM瘦身拷贝: 此处删除了 SeedFromSnapshot 及其私有依赖 LoadSkillOwnerState
        // (依赖未拷贝的 SelectCharacterInitializationSnapshot, 仅服务端种子流程使用)。
    }

    public sealed class SkillSlotRecord
    {
        public int Slot { get; set; }

        public ushort SkillId { get; set; }
    }

    public sealed class SkillSlotMove
    {
        public ushort SkillId { get; set; }

        public int ToSlot { get; set; }
    }
}
