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

            // 改等级后清技能表: 降级不留超出等级门槛的非法技能, 自动成长技的条目
            // 等级随重建刷新; 下次选角由服务端重建面板, SP/TP 随派生自动回满。
            ResetSkillsForRebuild(characterId);

            return new { success = true, characterId, level, exp, skillsReset = true };
        }

        // 清空该角色已学技能: 服务端在下次选角加载时按 (职业, 转职, 觉醒, 等级)
        // 自动重建技能面板(与迁移23"清零重建"同一机制), 余额是派生值无需另算。
        private void ResetSkillsForRebuild(int characterId)
        {
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM character_skills WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // 玩家实际看到的 SP/TP: 总点数(等级表+加成) 与 剩余点数(扣除已学技能),
        // 用服务端 SkillStateService.LoadAndSync 同一条链计算。
        // growType 必须解码传入: 免费基线含转职送技/觉醒技, 缺了会把送技误计为花费。
        public object GetSpTp(int characterId)
        {
            byte job, level, growTypeRaw;
            int bonusSp, bonusTp;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT job, level, bonus_sp, bonus_tp, grow_type FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return Error("角色不存在: " + characterId);
                        job = (byte)reader.GetInt32(0);
                        level = (byte)reader.GetInt32(1);
                        bonusSp = reader.GetInt32(2);
                        bonusTp = reader.GetInt32(3);
                        growTypeRaw = (byte)reader.GetInt32(4);
                    }
                }
            }

            try
            {
                DfoGmTool.ServerCore.Game.Characters.CharacterStatComputer.DecodeGrowType(
                    growTypeRaw, out var firstGrow, out var secondGrow);
                var repository = new DfoGmTool.ServerCore.Game.CharacterData.SqliteCharacterProgressRepository(
                    _config.DatabasePath, _config.SchemaPath);
                var synced = DfoGmTool.ServerCore.Game.Skills.SkillStateService.LoadAndSync(
                    repository, characterId, job, level, bonusSp, bonusTp, persist: false,
                    growType: firstGrow, secondGrowType: secondGrow);
                if (synced.Points == null)
                    return Error("技能点状态加载失败");

                // 四池独立(page0=PVE树, page1=PVP树), 与服务端 INIT 包 header/tail 同源。
                return new
                {
                    characterId,
                    totalSp = synced.Points.TotalSp,
                    remainingSp = synced.Points.RemainingSp,
                    totalTp = synced.Points.TotalTp,
                    remainingTp = synced.Points.RemainingTp,
                    remainingSpPvp = synced.Points.RemainingSpPage1,
                    remainingTpPvp = synced.Points.RemainingTpPage1,
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
        private bool ApplyGrowType(int characterId, int? first, int? second)
        {
            byte level;
            uint exp;
            int current;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT grow_type, level, exp FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return false;
                        current = reader.GetInt32(0);
                        level = (byte)reader.GetInt32(1);
                        exp = (uint)reader.GetInt64(2);
                    }
                }

                var firstGrow = first ?? (current & 0xF);
                var secondGrow = second ?? ((current >> 4) & 0xF);
                var packed = (byte)((secondGrow << 4) | (firstGrow & 0xF));

                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "UPDATE characters SET grow_type = @grow, updated_at = CURRENT_TIMESTAMP WHERE character_id = @cid;";
                        cmd.Parameters.AddWithValue("@grow", (int)packed);
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        cmd.ExecuteNonQuery();
                    }

                    // 与服务端转职流程(清空重建+送技)同口径: 清掉技能表,
                    // 下次选角由服务端按新 grow_type 重建面板, SP/TP 随派生自动回满。
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "DELETE FROM character_skills WHERE character_id = @cid;";
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
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
                return ApplyGrowType(characterId, meta.GrowNumber, null);
            if (meta.JobChangeQuestValue == 2)
                return ApplyGrowType(characterId, null, meta.GrowNumber);
            return false;
        }

        public object GetGrowOptions(int characterId)
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
                        return new
                        {
                            characterId,
                            job,
                            first = grow & 0xF,
                            second = (grow >> 4) & 0xF,
                            options = _pvfIndex.GetJobGrowOptions(job),
                        };
                    }
                }
            }
        }

        public object SetGrowType(int characterId, int first, int second)
        {
            // 与服务端 CharacterStatComputer.ComputeStat 的守卫一致
            if (first < 0 || first > 5 || second < 0 || second > 2)
                return Error("转职范围 0-5, 觉醒范围 0-2");
            if (second > 0 && first == 0)
                return Error("未转职不能设置觉醒");

            if (!ApplyGrowType(characterId, first, second))
                return Error("角色不存在或写入失败: " + characterId);

            return new { success = true, characterId, first, second, skillsReset = true };
        }
    }
}
