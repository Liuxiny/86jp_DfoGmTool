using System;
using System.Collections.Generic;
using System.Linq;
using DfoGmTool.ServerCore.Game.TitleBook;
using DfoGmTool.ServerCore.Game.Characters;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.Dungeon;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.Quests;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        public object ListCharacters(int accountId)
        {
            var result = new List<object>();
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT c.character_id, c.name, c.level, c.exp, c.job, c.grow_type,
       c.bonus_sp, c.bonus_tp, c.account_id, a.m_id
FROM characters c
JOIN accounts a ON a.account_id = c.account_id
WHERE (@aid < 0 OR c.account_id = @aid) AND c.delete_flag = 0
ORDER BY c.character_id;";
                    cmd.Parameters.AddWithValue("@aid", accountId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var job = reader.GetInt32(4);
                            var growType = reader.GetInt32(5);
                            result.Add(new
                            {
                                characterId = reader.GetInt32(0),
                                name = reader.GetString(1),
                                level = reader.GetInt32(2),
                                exp = reader.GetInt64(3),
                                job,
                                growType,
                                jobName = DisplayJobName(job, growType),
                                bonusSp = reader.GetInt32(6),
                                bonusTp = reader.GetInt32(7),
                                accountId = reader.GetInt32(8),
                                accountName = reader.GetString(9),
                            });
                        }
                    }
                }
            }
            return new { characters = result };
        }

        public object GetCharacter(int characterId)
        {
            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            WalletSnapshot wallet;
            using (var scope = _assetService.OpenScope(characterId, accountId))
            {
                wallet = _assetService.LoadWallet(scope);
            }

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT name, level, exp, job, grow_type, bonus_sp, bonus_tp
FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return Error("角色不存在: " + characterId);

                        var job = reader.GetInt32(3);
                        var growType = reader.GetInt32(4);
                        return new
                        {
                            characterId,
                            accountId,
                            name = reader.GetString(0),
                            level = reader.GetInt32(1),
                            exp = reader.GetInt64(2),
                            job,
                            jobName = DisplayJobName(job, growType),
                            growType,
                            bonusSp = reader.GetInt32(5),
                            bonusTp = reader.GetInt32(6),
                            maxLevel = ExpTableProvider.MaxLevel,
                            wallet = new
                            {
                                gold = wallet.Gold,
                                cera = wallet.Cera,
                                tokenCera = wallet.TokenCera,
                                luckyStar = (int)wallet.LuckyStar,
                            },
                        };
                    }
                }
            }
        }

        // 基础属性表: 用服务端 CharacterStatComputer 按 职业/等级/转职/觉醒 计算,
        // 与改等级时服务端落库的战斗属性同源同值。解码 82B 布局的具名字段。
        public object GetCharacterStats(int characterId)
        {
            byte job, level, growType;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT job, level, grow_type FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return Error("角色不存在: " + characterId);
                        job = (byte)reader.GetInt32(0);
                        level = (byte)reader.GetInt32(1);
                        growType = (byte)reader.GetInt32(2);
                    }
                }
            }

            int first, second;
            CharacterStatComputer.DecodeGrowType(growType, out first, out second);

            byte[] blob;
            try
            {
                blob = CharacterStatComputer.BuildAdditionalInfo(job, level, first, second);
            }
            catch (Exception ex)
            {
                return Error("属性计算失败: " + ex.Message);
            }

            int I16(int off) => (short)(blob[off] | blob[off + 1] << 8);
            int U16(int off) => blob[off] | blob[off + 1] << 8;
            long U32(int off) => (uint)(blob[off] | blob[off + 1] << 8 | blob[off + 2] << 16 | blob[off + 3] << 24);

            // 名称对齐客户端字符串表(面板词条 341-375); 中段 17 项异常抗性按同表 350-366
            // 顺序推定(数量与前后邻接字段都吻合), 本版本 .chr 不配置, 恒为 0
            var statusResLabels = new[]
            {
                "减速抗性", "冰冻抗性", "中毒抗性", "眩晕抗性", "诅咒抗性", "失明抗性",
                "感电抗性", "石化抗性", "睡眠抗性", "灼伤抗性", "即死抗性", "出血抗性",
                "穿刺抗性", "被攻击时回避率", "混乱抗性", "束缚抗性", "所有异常状态抗性",
            };

            var stats = new List<object>
            {
                new { key = "hpMax", label = "HP最大值", value = U32(0), zeroBlock = false },
                new { key = "mpMax", label = "MP最大值", value = U32(4), zeroBlock = false },
                new { key = "physAtk", label = "物理攻击力", value = (long)I16(8), zeroBlock = false },
                new { key = "physDef", label = "物理防御力", value = (long)I16(10), zeroBlock = false },
                new { key = "magAtk", label = "魔法攻击力", value = (long)I16(12), zeroBlock = false },
                new { key = "magDef", label = "魔法防御力", value = (long)I16(14), zeroBlock = false },
                new { key = "fireRes", label = "火属性抗性", value = (long)I16(16), zeroBlock = false },
                new { key = "iceRes", label = "冰属性抗性", value = (long)I16(18), zeroBlock = false },
                new { key = "darkRes", label = "暗属性抗性", value = (long)I16(20), zeroBlock = false },
                new { key = "lightRes", label = "光属性抗性", value = (long)I16(22), zeroBlock = false },
            };

            for (var i = 0; i < 17; i++)
            {
                stats.Add(new
                {
                    key = "statusRes" + i,
                    label = statusResLabels[i],
                    value = (long)U16(24 + i * 2),
                    zeroBlock = true,
                });
            }

            stats.Add(new { key = "inventoryLimit", label = "最大负重", value = U32(58), zeroBlock = false });
            stats.Add(new { key = "hpRegen", label = "HP恢复率", value = (long)U16(62), zeroBlock = false });
            stats.Add(new { key = "mpRegen", label = "MP恢复率", value = (long)U16(64), zeroBlock = false });
            stats.Add(new { key = "moveSpeed", label = "移动速度", value = U32(66), zeroBlock = false });
            stats.Add(new { key = "attackSpeed", label = "攻击速度", value = (long)U16(70), zeroBlock = false });
            stats.Add(new { key = "castSpeed", label = "施放速度", value = (long)U16(72), zeroBlock = false });
            stats.Add(new { key = "hitRecovery", label = "硬直", value = (long)U16(74), zeroBlock = false });
            stats.Add(new { key = "jumpPower", label = "跳跃力", value = (long)U16(76), zeroBlock = false });
            stats.Add(new { key = "weight", label = "重量", value = U32(78), zeroBlock = false });

            return new
            {
                characterId,
                job,
                level,
                growType,
                stats,
            };
        }

        public object SetLevel(int characterId, int level)
        {
            if (level < 1 || level > ExpTableProvider.MaxLevel)
                return Error("等级范围 1-" + ExpTableProvider.MaxLevel);

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            // exp 是累计值: 达到 N 级 = 越过 N-1 级的阈值。战斗属性由服务端代码在同一事务里重算。
            var exp = level > 1 ? (uint)ExpTableProvider.GetLevelThreshold(level - 1) : 0u;
            var updated = CharacterProgressService.PersistLevelAndExp(_config.ConnectionString, characterId, (byte)level, exp);
            if (!updated)
                return Error("写入失败");

            return new { success = true, characterId, level, exp };
        }

        public object MaxPersonalCargo(int characterId)
        {
            const int PersonalCargoListType = 2;
            const int MaxPersonalCargoCapacity = 152;

            if (!TryGetAccountId(characterId, out _))
                return Error("角色不存在: " + characterId);

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
INSERT INTO character_container_state (character_id, list_type, list_param16)
VALUES (@cid, @listType, @capacity)
ON CONFLICT(character_id, list_type) DO UPDATE SET list_param16 = excluded.list_param16;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    cmd.Parameters.AddWithValue("@listType", PersonalCargoListType);
                    cmd.Parameters.AddWithValue("@capacity", MaxPersonalCargoCapacity);
                    cmd.ExecuteNonQuery();
                }
            }

            return new
            {
                success = true,
                characterId,
                listType = PersonalCargoListType,
                listParam16 = MaxPersonalCargoCapacity,
            };
        }

        public object UnlockDungeonPermissions(int characterId, PvfIndexService pvfIndex)
        {
            const int MaxClearState = 4;

            if (!TryGetAccountId(characterId, out _))
                return Error("角色不存在: " + characterId);

            var dungeonIds = pvfIndex.GetDungeonPermissionIds();
            if (dungeonIds.Count == 0)
                return Error("PVF 中未读取到可开启难度的副本列表");

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "DELETE FROM character_dungeon_permissions WHERE character_id = @cid;";
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
INSERT INTO character_dungeon_permissions(character_id, sort_order, dungeon_id, clear_state)
VALUES (@cid, @sort, @dungeon, @state);";
                        var cidParam = cmd.Parameters.Add("@cid", SqliteType.Integer);
                        var sortParam = cmd.Parameters.Add("@sort", SqliteType.Integer);
                        var dungeonParam = cmd.Parameters.Add("@dungeon", SqliteType.Integer);
                        var stateParam = cmd.Parameters.Add("@state", SqliteType.Integer);
                        cidParam.Value = characterId;
                        stateParam.Value = MaxClearState;

                        for (var i = 0; i < dungeonIds.Count; i++)
                        {
                            sortParam.Value = i;
                            dungeonParam.Value = dungeonIds[i];
                            cmd.ExecuteNonQuery();
                        }
                    }

                    tx.Commit();
                }
            }

            return new
            {
                success = true,
                characterId,
                insertedCount = dungeonIds.Count,
                clearState = MaxClearState,
            };
        }

        public object DeleteCharacterPermanently(int characterId, string confirmText)
        {
            if (!string.Equals(confirmText, "删除角色", StringComparison.Ordinal))
                return Error("确认文本不正确，请输入：删除角色");

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var pragma = conn.CreateCommand())
                {
                    pragma.CommandText = "PRAGMA foreign_keys = ON;";
                    pragma.ExecuteNonQuery();
                }

                using (var tx = conn.BeginTransaction())
                {
                    byte[] nameBlob;
                    string name;
                    int accountId;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
SELECT CAST(name AS BLOB), name, account_id
FROM characters
WHERE character_id = @cid;";
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                                return Error("角色不存在: " + characterId);
                            nameBlob = (byte[])reader.GetValue(0);
                            name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                            accountId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                        }
                    }

                    var deletedQuestRows = 0;
                    var deletedAuditRows = 0;
                    var deletedItemRows = 0;
                    var deletedAccountEntryRows = 0;
                    var updatedTemplateRows = 0;
                    var replacementSeedCharacterId = ResolveReplacementSeedCharacterId(conn, tx, accountId, characterId);

                    if (TableExists(conn, tx, "character_active_quests"))
                        deletedQuestRows = ExecuteNonQuery(conn, tx,
                            "DELETE FROM character_active_quests WHERE character_id = @cid;",
                            ("@cid", characterId));

                    if (TableExists(conn, tx, "item_audit_log"))
                        deletedAuditRows = ExecuteNonQuery(conn, tx, @"
DELETE FROM item_audit_log
WHERE character_id = @cid
   OR (owner_scope = 'character' AND owner_id = @cid);",
                            ("@cid", characterId));

                    if (TableExists(conn, tx, "character_items"))
                        deletedItemRows = ExecuteNonQuery(conn, tx, @"
DELETE FROM character_items
WHERE character_id = @cid
   OR (owner_scope = 'character' AND owner_id = @cid);",
                            ("@cid", characterId));

                    if (TableExists(conn, tx, "account_character_entries"))
                        deletedAccountEntryRows = ExecuteNonQuery(conn, tx,
                            "DELETE FROM account_character_entries WHERE CAST(name AS BLOB) = @name;",
                            ("@name", nameBlob));

                    if (TableExists(conn, tx, "get_userinfo_template")
                        && ColumnExists(conn, tx, "get_userinfo_template", "seed_character_id"))
                    {
                        if (ColumnExists(conn, tx, "get_userinfo_template", "id"))
                        {
                            updatedTemplateRows += ExecuteNonQuery(conn, tx, @"
INSERT OR IGNORE INTO get_userinfo_template (id, seed_character_id)
VALUES (1, @seed);",
                                ("@seed", replacementSeedCharacterId));
                        }

                        updatedTemplateRows += ExecuteNonQuery(conn, tx, @"
UPDATE get_userinfo_template
SET seed_character_id = @seed
WHERE seed_character_id = @cid
   OR NOT EXISTS (
       SELECT 1
       FROM characters c
       WHERE c.character_id = get_userinfo_template.seed_character_id
         AND c.delete_flag = 0
   );",
                            ("@seed", replacementSeedCharacterId),
                            ("@cid", characterId));
                    }

                    var deletedCharacterRows = ExecuteNonQuery(conn, tx,
                        "DELETE FROM characters WHERE character_id = @cid;",
                        ("@cid", characterId));
                    if (deletedCharacterRows == 0)
                        return Error("角色删除失败: " + characterId);

                    tx.Commit();

                    return new
                    {
                        success = true,
                        characterId,
                        accountId,
                        name,
                        deletedQuestRows,
                        deletedAuditRows,
                        deletedItemRows,
                        deletedAccountEntryRows,
                        updatedTemplateRows,
                        replacementSeedCharacterId,
                        deletedCharacterRows,
                    };
                }
            }
        }

        private static int ResolveReplacementSeedCharacterId(
            SqliteConnection conn,
            SqliteTransaction tx,
            int accountId,
            int deletedCharacterId)
        {
            var sameAccount = ExecuteScalarInt(conn, tx, @"
SELECT character_id
FROM characters
WHERE account_id = @aid
  AND character_id <> @cid
  AND delete_flag = 0
ORDER BY character_id
LIMIT 1;",
                ("@aid", accountId),
                ("@cid", deletedCharacterId));
            if (sameAccount > 0)
                return sameAccount;

            var anyActive = ExecuteScalarInt(conn, tx, @"
SELECT character_id
FROM characters
WHERE character_id <> @cid
  AND delete_flag = 0
ORDER BY character_id
LIMIT 1;",
                ("@cid", deletedCharacterId));
            return anyActive > 0 ? anyActive : 1000;
        }

        private static int ExecuteScalarInt(
            SqliteConnection conn,
            SqliteTransaction tx,
            string sql,
            params (string Name, object Value)[] parameters)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                foreach (var parameter in parameters)
                    cmd.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
                var value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
            }
        }

        private static int ExecuteNonQuery(
            SqliteConnection conn,
            SqliteTransaction tx,
            string sql,
            params (string Name, object Value)[] parameters)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                foreach (var parameter in parameters)
                    cmd.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
                return cmd.ExecuteNonQuery();
            }
        }

        private static bool TableExists(SqliteConnection conn, SqliteTransaction tx, string tableName)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
                cmd.Parameters.AddWithValue("@name", tableName);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private static bool ColumnExists(SqliteConnection conn, SqliteTransaction tx, string tableName, string columnName)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info(@table) WHERE name = @column;";
                cmd.Parameters.AddWithValue("@table", tableName);
                cmd.Parameters.AddWithValue("@column", columnName);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        // 玩家实际看到的 SP/TP: 总点数(等级表+加成) 与 剩余点数(扣除已学技能),
        // 用服务端 SkillStateService.LoadAndSync 同一条链计算
        public object GetSpTp(int characterId)
        {
            byte job, level;
            int bonusSp, bonusTp;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT job, level, bonus_sp, bonus_tp FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return Error("角色不存在: " + characterId);
                        job = (byte)reader.GetInt32(0);
                        level = (byte)reader.GetInt32(1);
                        bonusSp = reader.GetInt32(2);
                        bonusTp = reader.GetInt32(3);
                    }
                }
            }

            try
            {
                var repository = new DfoGmTool.ServerCore.Game.CharacterData.SqliteCharacterProgressRepository(
                    _config.DatabasePath, _config.SchemaPath);
                var synced = DfoGmTool.ServerCore.Game.Skills.SkillStateService.LoadAndSync(
                    repository, characterId, job, level, bonusSp, bonusTp, persist: false);
                if (synced.Points == null)
                    return Error("技能点状态加载失败");

                return new
                {
                    characterId,
                    totalSp = synced.Points.TotalSp,
                    remainingSp = synced.Points.RemainingSp,
                    totalTp = synced.Points.TotalTp,
                    remainingTp = synced.Points.RemainingTp,
                    bonusSp,
                    bonusTp,
                };
            }
            catch (Exception ex)
            {
                return Error("SP/TP 计算失败: " + ex.Message);
            }
        }

        public object AdjustSpTp(int characterId, int spDelta, int tpDelta)
        {
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
UPDATE characters
SET bonus_sp = MAX(0, bonus_sp + @dsp),
    bonus_tp = MAX(0, bonus_tp + @dtp)
WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@dsp", spDelta);
                    cmd.Parameters.AddWithValue("@dtp", tpDelta);
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    if (cmd.ExecuteNonQuery() == 0)
                        return Error("角色不存在: " + characterId);
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT bonus_sp, bonus_tp FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        reader.Read();
                        return new { success = true, characterId, bonusSp = reader.GetInt32(0), bonusTp = reader.GetInt32(1) };
                    }
                }
            }
        }

        // 转职/觉醒写入, 与服务端 QuestService.UpdateGrowType 同语义:
        // grow_type 低4位=转职 高4位=觉醒, 改完用当前等级/经验重走
        // PersistLevelAndExp(它按库里新 grow_type 重算战斗属性, 同一事务)
        private bool ApplyGrowType(int characterId, int? job, int? first, int? second)
        {
            byte level;
            uint exp;
            int current;
            int currentJob;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT job, grow_type, level, exp FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return false;
                        currentJob = reader.GetInt32(0);
                        current = reader.GetInt32(1);
                        level = (byte)reader.GetInt32(2);
                        exp = (uint)reader.GetInt64(3);
                    }
                }

                var firstGrow = first ?? (current & 0xF);
                var secondGrow = second ?? ((current >> 4) & 0xF);
                var targetJob = job ?? currentJob;
                var packed = (byte)((secondGrow << 4) | (firstGrow & 0xF));

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE characters SET job = @job, grow_type = @grow, updated_at = CURRENT_TIMESTAMP WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@job", targetJob);
                    cmd.Parameters.AddWithValue("@grow", (int)packed);
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    cmd.ExecuteNonQuery();
                }
            }

            // 用新 grow_type 重算战斗属性(等级/经验原值回写)
            return CharacterProgressService.PersistLevelAndExp(_config.ConnectionString, characterId, level, exp);
        }

        // 任务奖励里的转职链: jcq=1 授转职(GrowNumber), jcq=2 授觉醒
        private bool ApplyGrowTypeFromQuest(int characterId, PvfIndexService.QuestMeta meta)
        {
            if (meta == null || meta.GrowNumber <= 0)
                return false;
            if (meta.JobChangeQuestValue == 1)
                return ApplyGrowType(characterId, null, meta.GrowNumber, null);
            if (meta.JobChangeQuestValue == 2)
                return ApplyGrowType(characterId, null, null, meta.GrowNumber);
            return false;
        }

        public object GetGrowOptions(int characterId, int? selectedJob)
        {
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT job, grow_type FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return Error("角色不存在: " + characterId);
                        var job = reader.GetInt32(0);
                        var grow = reader.GetInt32(1);
                        var optionJob = selectedJob ?? job;
                        var optionGrow = optionJob == job ? grow : 0;
                        return new
                        {
                            characterId,
                            currentJob = job,
                            job = optionJob,
                            first = optionGrow & 0xF,
                            second = (optionGrow >> 4) & 0xF,
                            jobs = _pvfIndex.GetAllJobOptions(),
                            options = _pvfIndex.GetJobGrowOptions(optionJob),
                        };
                    }
                }
            }
        }

        public object SetGrowType(int characterId, int? job, int first, int second)
        {
            if (job.HasValue && (job.Value < 0 || job.Value > byte.MaxValue))
                return Error("职业范围 0-255");
            // 与服务端 CharacterStatComputer.ComputeStat 的守卫一致
            if (first < 0 || first > 5 || second < 0 || second > 2)
                return Error("转职范围 0-5, 觉醒范围 0-2");
            if (second > 0 && first == 0)
                return Error("未转职不能设置觉醒");

            if (!ApplyGrowType(characterId, job, first, second))
                return Error("角色不存在或写入失败: " + characterId);

            return new { success = true, characterId, job, first, second };
        }
    }
}
