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

            (9, "晶块从 character_items 归集账号", CurrencyService.MigrateCubeFragmentsFromCharacterItems),

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
        };

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
