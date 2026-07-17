using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using DfoGmTool.ServerCore.Game.SelectCharacter;
using DfoGmTool.ServerCore.Infrastructure;

namespace DfoGmTool.ServerCore.Game.CharacterData
{
    public sealed class SqliteSubtype1Repository
    {
        private readonly string _connectionString;

        public SqliteSubtype1Repository(string databasePath, string schemaFilePath)
        {
            _connectionString = SqliteDatabaseBootstrap.Initialize(databasePath, schemaFilePath);
        }

        private SqliteSubtype1Repository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public static SqliteSubtype1Repository FromConnectionString(string connectionString)
        {
            return new SqliteSubtype1Repository(connectionString);
        }

        public bool HasData(int characterId)
        {
            using (var conn = Open())
            using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM character_subtype1_fields WHERE character_id=@cid", conn))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public UserInfoAdditionSnapshot Load(int characterId)
        {
            var snap = new UserInfoAdditionSnapshot();

            using (var conn = Open())
            {
                
                using (var cmd = new SqliteCommand(@"SELECT
                    stat_hp_max, stat_mp_max, stat_physical_attack, stat_physical_defense,
                    stat_magical_attack, stat_magical_defense, stat_fire_resistance, stat_water_resistance,
                    stat_dark_resistance, stat_light_resistance, stat_inventory_limit,
                    stat_hp_regen_speed, stat_mp_regen_speed, stat_move_speed, stat_attack_speed,
                    stat_cast_speed, stat_hit_recovery, stat_jump_power, stat_weight, stat_level,
                    name_tag_item_id, name_tag_expire_time, skill_tree_index, equipped_creature_level, equip_list_trailing,
                    manage_level, flag_byte, guild_power_war, server_timestamp, quest_shop_count,
                    progress1, progress2
                FROM character_subtype1_fields WHERE character_id=@cid", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return null;
                        snap.StatHpMax = (uint)r.GetInt64(0);
                        snap.StatMpMax = (uint)r.GetInt64(1);
                        snap.StatPhysicalAttack = (short)r.GetInt32(2);
                        snap.StatPhysicalDefense = (short)r.GetInt32(3);
                        snap.StatMagicalAttack = (short)r.GetInt32(4);
                        snap.StatMagicalDefense = (short)r.GetInt32(5);
                        snap.StatFireResistance = (short)r.GetInt32(6);
                        snap.StatWaterResistance = (short)r.GetInt32(7);
                        snap.StatDarkResistance = (short)r.GetInt32(8);
                        snap.StatLightResistance = (short)r.GetInt32(9);
                        snap.StatInventoryLimit = (uint)r.GetInt64(10);
                        snap.StatHpRegenSpeed = (ushort)r.GetInt32(11);
                        snap.StatMpRegenSpeed = (ushort)r.GetInt32(12);
                        snap.StatMoveSpeed = (uint)r.GetInt64(13);
                        snap.StatAttackSpeed = (ushort)r.GetInt32(14);
                        snap.StatCastSpeed = (ushort)r.GetInt32(15);
                        snap.StatHitRecovery = (ushort)r.GetInt32(16);
                        snap.StatJumpPower = (ushort)r.GetInt32(17);
                        snap.StatWeight = (uint)r.GetInt64(18);
                        snap.StatLevel = (byte)r.GetInt32(19);
                        snap.NameTagItemId = (uint)r.GetInt64(20);
                        snap.NameTagExpireTime = (uint)r.GetInt64(21);
                        snap.SkillTreeIndex = NormalizeSkillTreeIndexForClient(r.GetInt32(22));
                        snap.EquippedCreatureLevel = (byte)r.GetInt32(23);
                        snap.ManageLevel = (byte)r.GetInt32(25);
                        snap.FlagByte = (byte)r.GetInt32(26);
                        snap.GuildPowerWar = (uint)r.GetInt64(27);
                        snap.ServerTimestamp = (uint)r.GetInt64(28);
                        snap.QuestShopCount = (ushort)r.GetInt32(29);
                        snap.Progress1 = (uint)r.GetInt64(30);
                        snap.Progress2 = (uint)r.GetInt64(31);
                    }
                }

                
                using (var cmd = new SqliteCommand("SELECT exp, ex_equip_slot_stat, clone_title_item_id FROM characters WHERE character_id=@cid", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            snap.CharacExp = (uint)r.GetInt64(0);
                            snap.ExEquipSlotStat = (byte)r.GetInt32(1);
                            snap.CloneTitleItemId = r.IsDBNull(2) ? 0u : (uint)r.GetInt64(2);
                        }
                    }
                }

                
                
                using (var cmd = new SqliteCommand(@"
SELECT slot, item_id, raw_entry
FROM character_equipped_entries
WHERE character_id=@cid AND (expire_time<=0 OR expire_time>@now)
ORDER BY slot", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int slot = r.GetInt32(0);
                            int itemId = r.GetInt32(1);
                            var raw = (byte[])r.GetValue(2);

                            int diff = Game.Inventory.InvenItem.VerifyRoundTrip(raw, out var item);
                            if (diff >= 0)
                                throw new System.IO.InvalidDataException(
                                    $"[Subtype1Repo] char {characterId} slot {slot} item {itemId}: InvenItem roundtrip 首差 offset {diff} (rawLen={raw.Length})");

                            snap.EquippedEntries.Add(new EquippedEntrySnapshot
                            {
                                Slot = slot,
                                ItemId = itemId,
                                RawEntry = raw,
                                Item = item,
                            });
                        }
                    }
                }

                
                using (var cmd = new SqliteCommand("SELECT dim_key, val1, val2 FROM character_dimensions WHERE character_id=@cid ORDER BY sort_order", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            snap.Dimensions.Add(new DimensionEntrySnapshot
                            {
                                Key = (uint)r.GetInt64(0),
                                Val1 = (byte)r.GetInt32(1),
                                Val2 = (byte)r.GetInt32(2),
                            });
                        }
                    }
                }

                
                using (var cmd = new SqliteCommand("SELECT flag1, flag2, flag3, flag4 FROM character_dimension_flags WHERE character_id=@cid", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            snap.DimFlag1 = (byte)r.GetInt32(0);
                            snap.DimFlag2 = (byte)r.GetInt32(1);
                            snap.DimFlag3 = (byte)r.GetInt32(2);
                            snap.DimFlag4 = (byte)r.GetInt32(3);
                        }
                    }
                }

                // PvpResults/AbuseValues 保持空列表: 对应功能未实现, 旧表全库为空且无写入方, 已删除
            }

            return snap;
        }

        public int UpdateSkillTreeIndex(int characterId, byte skillTreeIndex)
        {
            var storedSkillTreeIndex = NormalizeSkillTreeIndexForStorage(skillTreeIndex);
            using (var conn = Open())
            using (var cmd = new SqliteCommand(@"
INSERT INTO character_subtype1_fields(character_id, skill_tree_index)
VALUES(@cid, @idx)
ON CONFLICT(character_id) DO UPDATE SET skill_tree_index=@idx;", conn))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                cmd.Parameters.AddWithValue("@idx", storedSkillTreeIndex);
                return cmd.ExecuteNonQuery();
            }
        }

        public byte? LoadSkillTreeIndex(int characterId)
        {
            using (var conn = Open())
            using (var cmd = new SqliteCommand(
                "SELECT skill_tree_index FROM character_subtype1_fields WHERE character_id=@cid", conn))
            {
                cmd.Parameters.AddWithValue("@cid", characterId);
                var value = cmd.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                    return null;

                return NormalizeSkillTreeIndexForClient(Convert.ToInt32(value));
            }
        }

        /// <summary>
        /// CharacterStatComputer.BuildAdditionalInfo 输出的 82 字节 stat blob,
        /// 拆成 character_subtype1_fields 各 stat_* 列。偏移与 BuildAdditionalInfo 写入顺序一致。
        /// </summary>
        private readonly struct CombatStatFields
        {
            public readonly long HpMax, MpMax, InventoryLimit, MoveSpeed, Weight;
            public readonly int PhysicalAttack, PhysicalDefense, MagicalAttack, MagicalDefense;
            public readonly int FireRes, WaterRes, DarkRes, LightRes;
            public readonly int HpRegen, MpRegen, AttackSpeed, CastSpeed, HitRecovery, JumpPower;

            private CombatStatFields(byte[] b)
            {
                int o = 0;
                HpMax = (long)BitConverter.ToUInt32(b, o); o += 4;
                MpMax = (long)BitConverter.ToUInt32(b, o); o += 4;
                PhysicalAttack = BitConverter.ToInt16(b, o); o += 2;
                PhysicalDefense = BitConverter.ToInt16(b, o); o += 2;
                MagicalAttack = BitConverter.ToInt16(b, o); o += 2;
                MagicalDefense = BitConverter.ToInt16(b, o); o += 2;
                FireRes = BitConverter.ToInt16(b, o); o += 2;
                WaterRes = BitConverter.ToInt16(b, o); o += 2;
                DarkRes = BitConverter.ToInt16(b, o); o += 2;
                LightRes = BitConverter.ToInt16(b, o); o += 2;
                o += 34; // 17 × u16 占位, 与 BuildAdditionalInfo 的零占位对齐
                InventoryLimit = (long)BitConverter.ToUInt32(b, o); o += 4;
                HpRegen = BitConverter.ToUInt16(b, o); o += 2;
                MpRegen = BitConverter.ToUInt16(b, o); o += 2;
                MoveSpeed = (long)BitConverter.ToUInt32(b, o); o += 4;
                AttackSpeed = BitConverter.ToUInt16(b, o); o += 2;
                CastSpeed = BitConverter.ToUInt16(b, o); o += 2;
                HitRecovery = BitConverter.ToUInt16(b, o); o += 2;
                JumpPower = BitConverter.ToUInt16(b, o); o += 2;
                Weight = (long)BitConverter.ToUInt32(b, o);
            }

            public static CombatStatFields Parse(byte[] blob)
            {
                if (blob == null || blob.Length < 82)
                    throw new ArgumentException($"[Subtype1Repo] stat blob 长度不足: {blob?.Length ?? 0}/82");
                return new CombatStatFields(blob);
            }

            public void AddTo(SqliteCommand cmd)
            {
                cmd.Parameters.AddWithValue("@hp", HpMax);
                cmd.Parameters.AddWithValue("@mp", MpMax);
                cmd.Parameters.AddWithValue("@pa", PhysicalAttack);
                cmd.Parameters.AddWithValue("@pd", PhysicalDefense);
                cmd.Parameters.AddWithValue("@ma", MagicalAttack);
                cmd.Parameters.AddWithValue("@md", MagicalDefense);
                cmd.Parameters.AddWithValue("@fr", FireRes);
                cmd.Parameters.AddWithValue("@wr", WaterRes);
                cmd.Parameters.AddWithValue("@dr", DarkRes);
                cmd.Parameters.AddWithValue("@lr", LightRes);
                cmd.Parameters.AddWithValue("@il", InventoryLimit);
                cmd.Parameters.AddWithValue("@hr", HpRegen);
                cmd.Parameters.AddWithValue("@mr", MpRegen);
                cmd.Parameters.AddWithValue("@ms", MoveSpeed);
                cmd.Parameters.AddWithValue("@as2", AttackSpeed);
                cmd.Parameters.AddWithValue("@cs", CastSpeed);
                cmd.Parameters.AddWithValue("@hrc", HitRecovery);
                cmd.Parameters.AddWithValue("@jp", JumpPower);
                cmd.Parameters.AddWithValue("@wt", Weight);
            }
        }

        /// <summary>
        /// 按升级后的新等级重算战斗属性(HP/MP/攻防/抗性/速度/重量)并持久化。
        /// statBlob = CharacterStatComputer.BuildAdditionalInfo(job, level, first, second)。
        /// 必须用升级后的 level: 14级以下用基础表, 15-49 用转职成长表, 50+ 用觉醒成长表。
        /// </summary>
        public int UpdateCombatStats(int characterId, byte[] statBlob)
        {
            using (var conn = Open())
                return UpdateCombatStatsOnConnection(conn, characterId, statBlob);
        }

        /// <summary>同连接版本, 供 RecomputeAllCombatStats 在单连接内顺序执行避免锁冲突;
        /// 传入 tx 可并入外部事务(等级与属性写同生共死)。</summary>
        internal static int UpdateCombatStatsOnConnection(SqliteConnection conn, int characterId, byte[] statBlob, SqliteTransaction tx = null)
        {
            var f = CombatStatFields.Parse(statBlob);
            using (var cmd = new SqliteCommand(@"
UPDATE character_subtype1_fields SET
    stat_hp_max=@hp, stat_mp_max=@mp,
    stat_physical_attack=@pa, stat_physical_defense=@pd,
    stat_magical_attack=@ma, stat_magical_defense=@md,
    stat_fire_resistance=@fr, stat_water_resistance=@wr,
    stat_dark_resistance=@dr, stat_light_resistance=@lr,
    stat_inventory_limit=@il,
    stat_hp_regen_speed=@hr, stat_mp_regen_speed=@mr,
    stat_move_speed=@ms, stat_attack_speed=@as2,
    stat_cast_speed=@cs, stat_hit_recovery=@hrc,
    stat_jump_power=@jp, stat_weight=@wt, stat_level=@sl
WHERE character_id=@cid;", conn))
            {
                cmd.Transaction = tx;
                f.AddTo(cmd);
                cmd.Parameters.AddWithValue("@cid", characterId);
                // stat_level 固定 100, 与种子创建(SqliteSelectCharacterDataSource 建号 INSERT)保持一致;
                // 该字段非角色等级锚点, 属性面板由上方各 stat_* 列直接驱动, 升级不修改。
                cmd.Parameters.AddWithValue("@sl", 100);
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 启动时一次性按当前等级重算所有角色战斗属性, 修复历史"升级未重算属性"的存量数据。
        /// 幂等: 重复执行结果一致。单连接内顺序执行避免 SQLite 锁冲突。
        /// </summary>
        public int RecomputeAllCombatStats()
        {
            int repaired = 0;
            using (var conn = Open())
            {
                // 先收集所有角色到内存再关 reader, 否则循环内 UPDATE 会触发 SQLite 锁冲突。
                var rows = new List<(int cid, byte job, byte level, byte grow)>();
                using (var cmd = new SqliteCommand(@"
SELECT s.character_id, c.job, c.level, c.grow_type
FROM character_subtype1_fields s
JOIN characters c ON c.character_id = s.character_id;", conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        rows.Add((r.GetInt32(0), (byte)r.GetInt32(1), (byte)r.GetInt32(2), (byte)r.GetInt32(3)));
                }

                foreach (var (cid, job, level, grow) in rows)
                {
                    try
                    {
                        DfoGmTool.ServerCore.Game.Characters.CharacterStatComputer.DecodeGrowType(grow, out int first, out int second);
                        var blob = DfoGmTool.ServerCore.Game.Characters.CharacterStatComputer.BuildAdditionalInfo(job, level, first, second);
                        if (UpdateCombatStatsOnConnection(conn, cid, blob) > 0)
                            repaired++;
                    }
                    catch (Exception ex)
                    {
                        DfoGmTool.ServerCore.FileLogger.Log($"[Subtype1Repo] RecomputeAllCombatStats cid={cid} skip: {ex.Message}");
                    }
                }
            }
            return repaired;
        }

        private static byte NormalizeSkillTreeIndexForClient(int skillTreeIndex)
        {
            if (skillTreeIndex < 0)
                return 0xFF;
            return skillTreeIndex == 0 ? (byte)0 : (byte)1;
        }

        private static int NormalizeSkillTreeIndexForStorage(byte skillTreeIndex)
        {
            if (skillTreeIndex == 0xFF)
                return -1;
            return skillTreeIndex == 0 ? 0 : 1;
        }

        private SqliteConnection Open()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}
