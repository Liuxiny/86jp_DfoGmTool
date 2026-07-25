using System;
using DfoGmTool.ServerCore.Game.Currency;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Sqlite
{
    // 版本化迁移: PRAGMA user_version 门控, 每条迁移每库只跑一次。
    //
    // 规则:
    //   1) 新增迁移 = 在 Steps 末尾追加下一个版本号, 禁止修改/删除已发布条目。
    //   2) item_schema.sql 始终保持"新库的完整最终形态"; 迁移只负责把旧库升上来。
    //      加列时两边都要写(schema + 迁移), 否则旧库缺列。
    //   3) 迁移体保持幂等(加列先查存在/重建先查建表SQL)作双保险, 但版本门控保证正常路径只执行一次。
    //   4) 破坏性变更(删列/改约束)用表重建或 DROP COLUMN, SQL 批内嵌 BEGIN/COMMIT 保证原子性。
    internal static class SqliteMigrations
    {
        private static readonly (int Version, string Name, Action<SqliteConnection> Apply)[] Steps =
        {
            (1, "accounts 账号级货币列", conn => SqliteSchemaMigrator.EnsureColumns(conn, "accounts", new[]
            {
                ("cera", "INTEGER NOT NULL DEFAULT 0"),
                ("token_cera", "INTEGER NOT NULL DEFAULT 0"),
                ("happy_token_cera", "INTEGER NOT NULL DEFAULT 0"),
                ("lucky_star", "INTEGER NOT NULL DEFAULT 0"),
            })),

            // 原 SqliteCharacterRepository 构造函数内散装补列(含原 InventoryMigrationRunner 独有4列)
            (2, "characters town/外观/进度列", conn => SqliteSchemaMigrator.EnsureColumns(conn, "characters", new[]
            {
                ("direction", "INTEGER NOT NULL DEFAULT 5"),
                ("area_state", "INTEGER NOT NULL DEFAULT 3"),
                ("name_bytes", "BLOB"),
                ("appearance_blob", "BLOB"),
                ("delete_flag", "INTEGER NOT NULL DEFAULT 0"),
                ("exp", "INTEGER NOT NULL DEFAULT 0"),
                ("ex_equip_slot_stat", "INTEGER NOT NULL DEFAULT 0"),
                ("pvp_grade", "INTEGER NOT NULL DEFAULT 0"),
                ("pvp_rating_grade", "INTEGER NOT NULL DEFAULT 0"),
                ("user_state", "INTEGER NOT NULL DEFAULT 0"),
                ("bonus_sp", "INTEGER NOT NULL DEFAULT 0"),
                ("bonus_tp", "INTEGER NOT NULL DEFAULT 0"),
                ("clone_title_item_id", "INTEGER NOT NULL DEFAULT 0"),
            })),

            (3, "character_equipped_entries 期限/锁列", conn => SqliteSchemaMigrator.EnsureColumns(conn, "character_equipped_entries", new[]
            {
                ("expire_time", "INTEGER NOT NULL DEFAULT 0"),
                ("equipment_lock_id", "INTEGER NOT NULL DEFAULT 0"),
            })),

            (4, "character_items equipment_lock_id 列", conn => SqliteSchemaMigrator.EnsureColumns(conn, "character_items", new[]
            {
                ("equipment_lock_id", "INTEGER NOT NULL DEFAULT 0"),
            })),

            (5, "character_item_locks 表重建", SqliteSchemaMigrator.MigrateCharacterItemLocks),

            (6, "character_items 唯一键重建(含item_kind)", SqliteSchemaMigrator.MigrateCharacterItemsUniqueConstraint),

            (7, "character_init_flags 角色选项blob列", conn => SqliteSchemaMigrator.EnsureColumns(conn, "character_init_flags", new[]
            {
                ("character_option_blob", "BLOB"),
            })),

            (8, "accounts 晶块6列", conn => SqliteSchemaMigrator.EnsureColumns(conn, "accounts", new[]
            {
                ("cube_black", "INTEGER NOT NULL DEFAULT 0"),
                ("cube_white", "INTEGER NOT NULL DEFAULT 0"),
                ("cube_red", "INTEGER NOT NULL DEFAULT 0"),
                ("cube_blue", "INTEGER NOT NULL DEFAULT 0"),
                ("cube_clear", "INTEGER NOT NULL DEFAULT 0"),
                ("cube_gold", "INTEGER NOT NULL DEFAULT 0"),
            })),

            // GM 不再在启动阶段隐式搬动旧背包；旧晶块只允许由显式双向迁移事务处理。
            (9, "旧晶块保留给显式背包迁移", _ => { }),

            // 原 AccountCharacterEntryRepository.SaveAll 内散装补列
            (10, "account_character_entries 选角条目列", conn => SqliteSchemaMigrator.EnsureColumns(conn, "account_character_entries", new[]
            {
                ("entry_index", "INTEGER NOT NULL DEFAULT 0"),
                ("slot_index", "INTEGER NOT NULL DEFAULT 0"),
                ("name", "TEXT NOT NULL DEFAULT ''"),
                ("name_bytes", "BLOB"),
                ("body_after_name", "BLOB NOT NULL DEFAULT X''"),
            })),

            // 原 SqliteUserInfoBlobRepository.SaveGetUserInfoResponseBlob 内散装补列
            (11, "get_userinfo_template response_blob 列", conn => SqliteSchemaMigrator.EnsureColumns(conn, "get_userinfo_template", new[]
            {
                ("response_blob", "BLOB"),
            })),

            // characters.gold/coin 是创建时写入后再无人读写的影子列(游戏内金币=character_items slot0,
            // 点券=accounts.cera), 留着只会误导调试。schema 已同步移除。
            (12, "characters 删除影子列 gold/coin", conn =>
                SqliteSchemaMigrator.DropColumnsIfExist(conn, "characters", "gold", "coin")),

            // character_init_flags 18列死数据清理(d85b887, 种子DB验证):
            // A) 被 account_settings/account_premiums 覆盖: hotkey_key_type, main_game_option_blob,
            //    quickchat_bank0/1, ack_premium_blob
            // B) 种子值全零且无动态写入: 其余13列(mailbox_*×4, ack_*等)
            (13, "character_init_flags 删除18列死数据", conn =>
                SqliteSchemaMigrator.DropColumnsIfExist(conn, "character_init_flags",
                    "hotkey_key_type", "main_game_option_blob", "quickchat_bank0", "quickchat_bank1",
                    "ack_premium_blob",
                    "shop_coin_event_flag", "level60_ui_state", "boss_tower_placeholder",
                    "event_info_tail_byte", "mailbox_loaded_count", "mailbox_mode",
                    "mailbox_not_loaded_count", "mailbox_unknown_count_c", "ack_account_reg_time",
                    "ack_quest_display_ids", "racing_dungeon_group_flags", "ack_post_tutorial_u16",
                    "ack_unread_tail")),

            // 高频点查缺失索引 + audit_log 溯源索引(只写表, 人工查账用)
            (14, "补齐点查索引 + audit_log 索引", conn => ExecuteBatch(conn, @"
CREATE INDEX IF NOT EXISTS idx_character_items_char_template
    ON character_items(character_id, list_type, item_template_id);
CREATE INDEX IF NOT EXISTS idx_character_creatures_key
    ON character_creatures(character_id, creature_key);
CREATE INDEX IF NOT EXISTS idx_dungeon_permissions_dungeon
    ON character_dungeon_permissions(character_id, dungeon_id);
CREATE INDEX IF NOT EXISTS idx_item_audit_log_char_time
    ON item_audit_log(character_id, created_at);")),

            // 每日/周常重置状态表(复活币每日领取等), 见 DailyResetService
            (15, "character_daily_reset/counters 每日重置", conn => ExecuteBatch(conn, @"
CREATE TABLE IF NOT EXISTS character_daily_reset (
    character_id INTEGER PRIMARY KEY,
    day_id       INTEGER NOT NULL DEFAULT 0,
    week_id      INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS character_daily_counters (
    character_id INTEGER NOT NULL,
    counter_key  TEXT    NOT NULL,
    period       TEXT    NOT NULL DEFAULT 'day' CHECK (period IN ('day', 'week')),
    value        INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, counter_key),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);")),

            (16, "accounts 赛丽亚幸运值列", conn => SqliteSchemaMigrator.EnsureColumns(conn, "accounts", new[]
            {
                ("seria_luck_value", "INTEGER NOT NULL DEFAULT 0"),
            })),

            // 黑暗武士组合技能页使用专用表；旧 character_init_bodies(0x01FD) 内容不迁移、不再读写。
            (17, "character_dark_knight_combo_skill_pages 专用表", conn => ExecuteBatch(conn, @"
CREATE TABLE IF NOT EXISTS character_dark_knight_combo_skill_pages (
    character_id INTEGER NOT NULL,
    page_index INTEGER NOT NULL CHECK (page_index >= 0 AND page_index <= 1),
    body BLOB NOT NULL,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (character_id, page_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);")),

            (18, "character_subtype0_fields mood_value", conn => SqliteSchemaMigrator.EnsureColumns(conn, "character_subtype0_fields", new[]
            {
                ("mood_value", "INTEGER NOT NULL DEFAULT 0"),
            })),

            (19, "accounts honor_exp", conn => SqliteSchemaMigrator.EnsureColumns(conn, "accounts", new[]
            {
                ("honor_exp", "INTEGER NOT NULL DEFAULT 0"),
            })),

            (20, "accounts growth_capsule_exp", conn => SqliteSchemaMigrator.EnsureColumns(conn, "accounts", new[]
            {
                ("growth_capsule_exp", "INTEGER NOT NULL DEFAULT 0"),
            })),

            (21, "character_creatures extra_json", conn => SqliteSchemaMigrator.EnsureColumns(conn, "character_creatures", new[]
            {
                ("extra_json", "TEXT NOT NULL DEFAULT '{}'"),
            })),

            // 封包回放机制退役: 选角 init 流不再从库里回放原始字节。
            // 仍在使用的数据迁入三张专用表, 其余 noti 改为动态构建:
            //   768(0x0300 晶体契约) → character_crystal_contract, 存量选择原样保留
            //   855(0x0357 租赁物品) → character_rental_items, 存量按旧存储编码解析后原样保留
            //   119(0x0077 宠物欢迎语) → character_pet_welcome_cache; 旧布局 occurrence 0=包体,
            //       occurrence 1000=缓存对应的宠物模板ID; 无模板ID元数据的行(旧抓包残留/
            //       新角色占位)在旧代码下也永远命中不了缓存, 不迁移
            //   53(0x0035 赛丽亚币): 从 accounts 钱包动态构建
            //   415(0x019F 支援兵): 发包流始终用动态包替换, 存量行从不被读取
            //   273(0x0111 联合服好友)/984(0x03D8 增率抽奖)/351(0x015F 制作技能点):
            //       统一发空态(新角色既有基线), 回放数据对单机服务端无意义
            //   897(0x0381 收集箱): builder 早已从 PVF + collectbox 进度表动态构建
            // 最后删除三张回放表(character_init_bodies/packet_sequence/getuserinfo_extra_packets)
            // 与 get_userinfo_template.response_blob 列(登录响应整包模板, 已无调用方)
            (22, "封包回放表退役 + 数据库整备(成就单一存储/表正名/死表死列清理)", conn =>
            {
                // 新库的 schema 已不再创建回放表; 补一个空壳让搬运语句在新库上也能跑, 结尾统一删除
                ExecuteBatch(conn, @"
CREATE TABLE IF NOT EXISTS character_init_bodies (
    character_id INTEGER NOT NULL,
    noti_type INTEGER NOT NULL,
    occurrence_index INTEGER NOT NULL DEFAULT 0,
    body BLOB NOT NULL,
    PRIMARY KEY (character_id, noti_type, occurrence_index)
);
CREATE TABLE IF NOT EXISTS character_crystal_contract (
    character_id INTEGER PRIMARY KEY,
    cube_type INTEGER NOT NULL DEFAULT 0,
    cube_grade INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS character_rental_items (
    character_id INTEGER NOT NULL,
    shop_entry_id INTEGER NOT NULL,
    inventory_template_id INTEGER NOT NULL DEFAULT 0,
    expire_time INTEGER NOT NULL,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_character_rental_items_char
    ON character_rental_items(character_id);
CREATE TABLE IF NOT EXISTS character_pet_welcome_cache (
    character_id INTEGER PRIMARY KEY,
    item_template_id INTEGER NOT NULL DEFAULT 0,
    body BLOB,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);");

                // 晶体契约: 2 字节 body → cube_type/cube_grade 两列
                var contracts = new System.Collections.Generic.List<(int cid, byte type, byte grade)>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT character_id, body FROM character_init_bodies WHERE noti_type = 768;";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var body = reader.IsDBNull(1) ? null : (byte[])reader[1];
                            if (body != null && body.Length >= 2)
                                contracts.Add((reader.GetInt32(0), body[0], body[1]));
                        }
                    }
                }
                foreach (var row in contracts)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT OR REPLACE INTO character_crystal_contract(character_id, cube_type, cube_grade) VALUES(@cid, @t, @g);";
                        cmd.Parameters.AddWithValue("@cid", row.cid);
                        cmd.Parameters.AddWithValue("@t", (int)row.type);
                        cmd.Parameters.AddWithValue("@g", (int)row.grade);
                        cmd.ExecuteNonQuery();
                    }
                }

                // 租赁物品: 旧存储编码 → 每件一行
                // 半迁移重跑时清掉上次未完成的搬运行, 保证幂等
                ExecuteBatch(conn, "DELETE FROM character_rental_items;");
                var rentals = new System.Collections.Generic.List<(int cid, byte[] body)>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT character_id, body FROM character_init_bodies WHERE noti_type = 855;";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            rentals.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : (byte[])reader[1]));
                    }
                }
                foreach (var row in rentals)
                {
                    var rental = new Game.SelectCharacter.RentalInfoSnapshot();
                    Game.SelectCharacter.RentalInfoSnapshot.ParseStorageBody(row.body, rental);
                    foreach (var item in rental.Items)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"INSERT INTO character_rental_items(character_id, shop_entry_id, inventory_template_id, expire_time)
VALUES(@cid, @sid, @inv, @exp);";
                            cmd.Parameters.AddWithValue("@cid", row.cid);
                            cmd.Parameters.AddWithValue("@sid", (long)item.ItemId);
                            cmd.Parameters.AddWithValue("@inv", (long)item.InventoryTemplateId);
                            cmd.Parameters.AddWithValue("@exp", (long)item.ExpireTime);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                // 宠物欢迎语缓存: 只搬有模板ID元数据的有效缓存
                var petCaches = new System.Collections.Generic.List<(int cid, int itemId, byte[] body)>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT meta.character_id, meta.body, msg.body
FROM character_init_bodies meta
LEFT JOIN character_init_bodies msg
    ON msg.character_id = meta.character_id AND msg.noti_type = 119 AND msg.occurrence_index = 0
WHERE meta.noti_type = 119 AND meta.occurrence_index = 1000;";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var meta = reader.IsDBNull(1) ? null : (byte[])reader[1];
                            if (meta == null || meta.Length < 4)
                                continue;
                            petCaches.Add((
                                reader.GetInt32(0),
                                BitConverter.ToInt32(meta, 0),
                                reader.IsDBNull(2) ? null : (byte[])reader[2]));
                        }
                    }
                }
                foreach (var row in petCaches)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT OR REPLACE INTO character_pet_welcome_cache(character_id, item_template_id, body)
VALUES(@cid, @item, @body);";
                        cmd.Parameters.AddWithValue("@cid", row.cid);
                        cmd.Parameters.AddWithValue("@item", row.itemId);
                        cmd.Parameters.AddWithValue("@body", (object)row.body ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }

                // 收尾: 三张回放表与 response_blob 列一并删除
                SqliteSchemaMigrator.DropColumnsIfExist(conn, "get_userinfo_template", "response_blob");
                ExecuteBatch(conn, @"
DROP TABLE IF EXISTS character_init_bodies;
DROP TABLE IF EXISTS packet_sequence;
DROP TABLE IF EXISTS getuserinfo_extra_packets;");

                // ── 以下为数据库整备(与回放退役同一迁移) ──
            // 1) 成就双存储收敛: character_achievement(blob, 运行时进度) 与
            //    character_achievement_complete(结构化, 选角快照) 存同类数据、两套写路径,
            //    迟早互相覆盖。复合主键改为 (character_id, achievement_id) 后, blob 里的
            //    进度条目(较新)并入结构化表, blob 表删除, 全系统单一存储。
            // 2) 表正名(早期误判, IDA 已定论): racing_dungeon_* → daily_challenge_*(NOTI 0x0286),
            //    unknown725 → daily_schedule_states(0x02D5), unknown730 → buy_restrict_items(0x02DA)
            // 3) champion_break_blob(9字节: i32+u8+i32) 拆为三个整数列
            // 4) 删除无写入方且全库为空的 character_pvp_results / character_abuse_values /
            //    global_server_event_phase, 以及花名册抓包回放缓存 account_character_entries
            //    (唯一调用方已死, 现行 GET_USERINFO 从 characters 表动态构建, 存量名单早已过时)
            // 5) account_settings 外键补 ON DELETE CASCADE(全库唯一漏配的一张)
                // ── 成就双存储收敛 ──
                ExecuteBatch(conn, @"
CREATE TABLE IF NOT EXISTS character_achievement_complete (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    achievement_id INTEGER NOT NULL,
    p1 INTEGER NOT NULL DEFAULT 0,
    p2 INTEGER NOT NULL DEFAULT 0,
    p3 INTEGER NOT NULL DEFAULT 0,
    p4 INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, achievement_id),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);");
                if (!IsPrimaryKeyColumn(conn, "character_achievement_complete", "achievement_id"))
                {
                    ExecuteBatch(conn, @"
DROP TABLE IF EXISTS character_achievement_complete_new;
CREATE TABLE character_achievement_complete_new (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    achievement_id INTEGER NOT NULL,
    p1 INTEGER NOT NULL DEFAULT 0,
    p2 INTEGER NOT NULL DEFAULT 0,
    p3 INTEGER NOT NULL DEFAULT 0,
    p4 INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, achievement_id),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
INSERT OR REPLACE INTO character_achievement_complete_new
    SELECT character_id, sort_order, achievement_id, p1, p2, p3, p4
    FROM character_achievement_complete ORDER BY sort_order;
DROP TABLE character_achievement_complete;
ALTER TABLE character_achievement_complete_new RENAME TO character_achievement_complete;");
                }
                if (TableExists(conn, "character_achievement"))
                {
                    var blobs = new System.Collections.Generic.List<(int cid, byte[] blob)>();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT character_id, achievement FROM character_achievement;";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                                blobs.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : (byte[])reader[1]));
                        }
                    }
                    foreach (var row in blobs)
                    {
                        if (row.blob == null)
                            continue;
                        for (var off = 0; off + 12 <= row.blob.Length; off += 12)
                        {
                            var questId = BitConverter.ToInt32(row.blob, off);
                            if (questId <= 0)
                                continue;
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = @"
INSERT INTO character_achievement_complete(character_id, sort_order, achievement_id, p1, p2, p3, p4)
VALUES(@cid,
       COALESCE((SELECT sort_order FROM character_achievement_complete WHERE character_id=@cid AND achievement_id=@aid),
                (SELECT COALESCE(MAX(sort_order),-1)+1 FROM character_achievement_complete WHERE character_id=@cid)),
       @aid, @p1, @p2, @p3, @p4)
ON CONFLICT(character_id, achievement_id)
DO UPDATE SET p1=excluded.p1, p2=excluded.p2, p3=excluded.p3, p4=excluded.p4;";
                                cmd.Parameters.AddWithValue("@cid", row.cid);
                                cmd.Parameters.AddWithValue("@aid", questId);
                                cmd.Parameters.AddWithValue("@p1", (int)BitConverter.ToUInt16(row.blob, off + 4));
                                cmd.Parameters.AddWithValue("@p2", (int)BitConverter.ToUInt16(row.blob, off + 6));
                                cmd.Parameters.AddWithValue("@p3", (int)BitConverter.ToUInt16(row.blob, off + 8));
                                cmd.Parameters.AddWithValue("@p4", (int)BitConverter.ToUInt16(row.blob, off + 10));
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                    ExecuteBatch(conn, "DROP TABLE character_achievement;");
                }

                // ── 表正名 ──
                RenameTableIfExists(conn, "character_racing_dungeon_groups", "character_daily_challenge_groups");
                RenameTableIfExists(conn, "character_racing_dungeon_entries", "character_daily_challenge_entries");
                RenameTableIfExists(conn, "character_racing_dungeon_tail_ids", "character_daily_challenge_tail_ids");
                RenameTableIfExists(conn, "character_unknown725", "character_daily_schedule_states");
                RenameTableIfExists(conn, "character_unknown730", "character_buy_restrict_items");

                // ── champion_break_blob 拆列 ──
                SqliteSchemaMigrator.EnsureColumns(conn, "character_init_flags", new[]
                {
                    ("champion_break_key_id", "INTEGER NOT NULL DEFAULT 0"),
                    ("champion_break_mode", "INTEGER NOT NULL DEFAULT 0"),
                    ("champion_break_value", "INTEGER NOT NULL DEFAULT 0"),
                });
                if (ColumnExists(conn, "character_init_flags", "champion_break_blob"))
                {
                    var rows = new System.Collections.Generic.List<(int cid, byte[] blob)>();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT character_id, champion_break_blob FROM character_init_flags WHERE champion_break_blob IS NOT NULL;";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                                rows.Add((reader.GetInt32(0), (byte[])reader[1]));
                        }
                    }
                    foreach (var row in rows)
                    {
                        if (row.blob == null || row.blob.Length < 9)
                            continue;
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"
UPDATE character_init_flags
SET champion_break_key_id=@k, champion_break_mode=@m, champion_break_value=@v
WHERE character_id=@cid;";
                            cmd.Parameters.AddWithValue("@cid", row.cid);
                            cmd.Parameters.AddWithValue("@k", BitConverter.ToInt32(row.blob, 0));
                            cmd.Parameters.AddWithValue("@m", (int)row.blob[4]);
                            cmd.Parameters.AddWithValue("@v", BitConverter.ToInt32(row.blob, 5));
                            cmd.ExecuteNonQuery();
                        }
                    }
                    SqliteSchemaMigrator.DropColumnsIfExist(conn, "character_init_flags", "champion_break_blob");
                }
                // ack_reserved_8b: SELECT ACK 客户端读取边界之后的尾字节, handler 不读;
                // 存量值是抓包切片错位的残渣, 直接删列(builder 固定写零)
                SqliteSchemaMigrator.DropColumnsIfExist(conn, "character_init_flags", "ack_reserved_8b");

                // ── 死表/死列清理 ──
                // account_character_entries: 选角花名册的抓包字节回放缓存, 唯一调用方早已
                // 改为从 characters 表动态构建(存量种子名单也早已与真实角色对不上)
                // character_event_info: 抓包时代的活动列表, event_data 全库全零,
                // 除种子角色外所有角色一直空态; 0x006C 统一发空列表
                ExecuteBatch(conn, @"
DROP TABLE IF EXISTS character_pvp_results;
DROP TABLE IF EXISTS character_abuse_values;
DROP TABLE IF EXISTS global_server_event_phase;
DROP TABLE IF EXISTS account_character_entries;
DROP TABLE IF EXISTS character_event_info;");

                // ── account_settings 外键补 CASCADE ──
                if (TableExists(conn, "account_settings")
                    && !TableSqlContains(conn, "account_settings", "ON DELETE CASCADE"))
                {
                    ExecuteBatch(conn, @"
DROP TABLE IF EXISTS account_settings_new;
CREATE TABLE account_settings_new (
    account_id INTEGER PRIMARY KEY,
    main_game_option BLOB,
    quickchat_bank0 BLOB,
    quickchat_bank1 BLOB,
    hotkey_key_type INTEGER NOT NULL DEFAULT 0,
    hotkey_slots BLOB,
    FOREIGN KEY (account_id) REFERENCES accounts(account_id) ON DELETE CASCADE
);
INSERT INTO account_settings_new SELECT account_id, main_game_option, quickchat_bank0, quickchat_bank1, hotkey_key_type, hotkey_slots FROM account_settings;
DROP TABLE account_settings;
ALTER TABLE account_settings_new RENAME TO account_settings;");
                }
            }),

            (23, "skill points derived from learned skills", MigrateSkillPointDerivation),

            (24, "characters slot_index column", conn =>
            {
                SqliteSchemaMigrator.EnsureColumns(conn, "characters", new[]
                {
                    ("slot_index", "INTEGER NOT NULL DEFAULT 0"),
                });

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
UPDATE characters SET slot_index = (
    SELECT cnt FROM (
        SELECT c1.character_id,
               (SELECT COUNT(*) FROM characters c2
                WHERE c2.account_id = c1.account_id
                  AND c2.delete_flag = 0
                  AND c2.character_id <= c1.character_id) - 1 AS cnt
        FROM characters c1
        WHERE c1.character_id = characters.character_id
    )
) WHERE delete_flag = 0;";
                    cmd.ExecuteNonQuery();
                }
            }),

            (25, "skill tree extension locked state", conn =>
            {
                SqliteSchemaMigrator.EnsureColumns(conn, "character_subtype1_fields", new[]
                {
                    ("skill_tree_index", "INTEGER NOT NULL DEFAULT -1"),
                });

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
UPDATE character_subtype1_fields
SET skill_tree_index = -1
WHERE skill_tree_index NOT IN (-1, 0, 1);";
                    cmd.ExecuteNonQuery();
                }
            }),
        };

        private static void MigrateSkillPointDerivation(SqliteConnection connection)
        {
            bool foreignKeysEnabled;
            using (var cmd = new SqliteCommand("PRAGMA foreign_keys;", connection))
                foreignKeysEnabled = Convert.ToInt32(cmd.ExecuteScalar()) != 0;
            if (foreignKeysEnabled)
                ExecuteBatch(connection, "PRAGMA foreign_keys=OFF;");

            try
            {
                ExecuteBatch(connection, @"
DROP TABLE IF EXISTS character_skill_points;
DROP TABLE IF EXISTS character_skill_tail;
DROP TABLE IF EXISTS character_skills;
CREATE TABLE character_skills (
    character_id INTEGER NOT NULL,
    page_index INTEGER NOT NULL DEFAULT 0,
    slot INTEGER NOT NULL DEFAULT -1,
    skill_id INTEGER NOT NULL DEFAULT 0,
    level INTEGER NOT NULL DEFAULT 0,
    extra_values BLOB,
    PRIMARY KEY (character_id, page_index, slot),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);");
            }
            finally
            {
                if (foreignKeysEnabled)
                    ExecuteBatch(connection, "PRAGMA foreign_keys=ON;");
            }
        }

        private static bool TableExists(SqliteConnection connection, string tableName)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@n;";
                cmd.Parameters.AddWithValue("@n", tableName);
                return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
        }

        private static void RenameTableIfExists(SqliteConnection connection, string oldName, string newName)
        {
            if (!TableExists(connection, oldName))
                return;
            // 同名新表已被 schema 创建时先清掉空壳, 保住旧表数据
            if (TableExists(connection, newName))
                ExecuteBatch(connection, $"DROP TABLE {newName};");
            ExecuteBatch(connection, $"ALTER TABLE {oldName} RENAME TO {newName};");
        }

        private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({tableName});";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }

        private static bool IsPrimaryKeyColumn(SqliteConnection connection, string tableName, string columnName)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({tableName});";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                            return reader.GetInt32(5) > 0;
                    }
                }
            }
            return false;
        }

        private static bool TableSqlContains(SqliteConnection connection, string tableName, string fragment)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@n;";
                cmd.Parameters.AddWithValue("@n", tableName);
                var sql = cmd.ExecuteScalar() as string;
                return sql != null && sql.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static void ExecuteBatch(SqliteConnection connection, string sql)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        public static void Apply(SqliteConnection connection)
        {
            long current = ReadUserVersion(connection);
            foreach (var (version, name, apply) in Steps)
            {
                if (version <= current)
                    continue;

                apply(connection);
                SetUserVersion(connection, version);
                FileLogger.Log($"[Db] migration v{version} applied: {name}");
            }
        }

        private static long ReadUserVersion(SqliteConnection connection)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version;";
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        private static void SetUserVersion(SqliteConnection connection, int version)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA user_version = {version};";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
