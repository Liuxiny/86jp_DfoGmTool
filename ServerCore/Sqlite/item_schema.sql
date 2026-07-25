PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS accounts (
    account_id     INTEGER PRIMARY KEY AUTOINCREMENT,
    m_id           TEXT    NOT NULL UNIQUE,
    password_hash  TEXT    NOT NULL DEFAULT '',
    last_login_ip  TEXT    NOT NULL DEFAULT '',
    last_login_at  TEXT,
    created_at     TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    cera           INTEGER NOT NULL DEFAULT 0,
    token_cera     INTEGER NOT NULL DEFAULT 0,
    happy_token_cera INTEGER NOT NULL DEFAULT 0,
    lucky_star     INTEGER NOT NULL DEFAULT 0,
    seria_luck_value INTEGER NOT NULL DEFAULT 0,
    cube_black     INTEGER NOT NULL DEFAULT 0,
    cube_white     INTEGER NOT NULL DEFAULT 0,
    cube_red       INTEGER NOT NULL DEFAULT 0,
    cube_blue      INTEGER NOT NULL DEFAULT 0,
    cube_clear     INTEGER NOT NULL DEFAULT 0,
    cube_gold      INTEGER NOT NULL DEFAULT 0,
    honor_exp      INTEGER NOT NULL DEFAULT 0,
    growth_capsule_exp INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS characters (
    character_id INTEGER PRIMARY KEY,
    account_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    job INTEGER NOT NULL DEFAULT 0,
    grow_type INTEGER NOT NULL DEFAULT 0,
    level INTEGER NOT NULL DEFAULT 1,
    pvp_grade INTEGER NOT NULL DEFAULT 0,
    pvp_rating_grade INTEGER NOT NULL DEFAULT 0,
    user_state INTEGER NOT NULL DEFAULT 0,
    -- 货币不在本表: 金币/复活币/胜点=character_new_items 主背包虚拟槽0/1/2, 点券系=accounts.cera等 (旧 gold/coin 影子列已由迁移v12删除)
    town_id INTEGER NOT NULL DEFAULT 0,
    area_id INTEGER NOT NULL DEFAULT 0,
    pos_x INTEGER NOT NULL DEFAULT 0,
    pos_y INTEGER NOT NULL DEFAULT 0,
    direction INTEGER NOT NULL DEFAULT 5,
    area_state INTEGER NOT NULL DEFAULT 3,
    name_bytes BLOB,
    appearance_blob BLOB,
    clone_title_item_id INTEGER NOT NULL DEFAULT 0,
    delete_flag INTEGER NOT NULL DEFAULT 0,
    exp INTEGER NOT NULL DEFAULT 0,
    ex_equip_slot_stat INTEGER NOT NULL DEFAULT 0,
    bonus_sp INTEGER NOT NULL DEFAULT 0,
    bonus_tp INTEGER NOT NULL DEFAULT 0,
    slot_index INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (account_id) REFERENCES accounts(account_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_characters_name_unique
    ON characters(name);

CREATE INDEX IF NOT EXISTS idx_characters_account
    ON characters(account_id, delete_flag);

-- 金币携带上限与拍卖额上限是两个独立持久化值；升级事务会同步推进两者。
CREATE TABLE IF NOT EXISTS character_gold_limits (
    character_id       INTEGER PRIMARY KEY,
    gold_carry_limit   INTEGER NOT NULL,
    auction_gold_limit INTEGER NOT NULL,
    updated_at         TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_container_state (
    character_id INTEGER NOT NULL,
    list_type INTEGER NOT NULL,
    list_param16 INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, list_type),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_items (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')),
    owner_id INTEGER NOT NULL,
    character_id INTEGER,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_template_id INTEGER NOT NULL,
    item_kind TEXT NOT NULL DEFAULT 'unknown' CHECK (item_kind IN ('unknown', 'stackable', 'equipment', 'avatar', 'pet', 'special')),
    stack_count INTEGER NOT NULL DEFAULT 0,
    instance_value INTEGER NOT NULL DEFAULT 0,
    durability INTEGER NOT NULL DEFAULT 0,
    seal_flag INTEGER NOT NULL DEFAULT 0,
    option_value INTEGER NOT NULL DEFAULT 0,
    equipment_lock_id INTEGER NOT NULL DEFAULT 0,
    expire_time INTEGER NOT NULL DEFAULT 0,
    marker_16 INTEGER NOT NULL DEFAULT 0,
    pet_serial_or_handle INTEGER NOT NULL DEFAULT 0,
    extra_json TEXT NOT NULL DEFAULT '{}',
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(owner_scope, owner_id, list_type, slot_index, item_kind),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_character_items_owner_container
    ON character_items(owner_scope, owner_id, list_type, slot_index);

CREATE INDEX IF NOT EXISTS idx_character_items_template
    ON character_items(item_template_id);

CREATE INDEX IF NOT EXISTS idx_character_items_character
    ON character_items(character_id, list_type, slot_index);

CREATE INDEX IF NOT EXISTS idx_character_items_char_template
    ON character_items(character_id, list_type, item_template_id);

CREATE TABLE IF NOT EXISTS character_new_items (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')),
    owner_id INTEGER NOT NULL,
    character_id INTEGER,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_core BLOB NOT NULL CHECK(length(item_core) = 82),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(owner_scope, owner_id, list_type, slot_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_character_new_items_character_space
    ON character_new_items(character_id, list_type, slot_index);

CREATE TABLE IF NOT EXISTS character_avatar_detail (
    item_uid INTEGER PRIMARY KEY,
    owner_id INTEGER NOT NULL DEFAULT 0,
    character_id INTEGER NOT NULL DEFAULT 0,
    item_id INTEGER NOT NULL DEFAULT 0,
    expire_date INTEGER NOT NULL DEFAULT 0,
    clear_avatar_id INTEGER NOT NULL DEFAULT 0,
    jewel_socket BLOB NOT NULL CHECK(length(jewel_socket) = 30),
    color1 INTEGER NOT NULL DEFAULT 0,
    color2 INTEGER NOT NULL DEFAULT 0,
    delete_date INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_character_avatar_detail_character
    ON character_avatar_detail(character_id);

CREATE TABLE IF NOT EXISTS character_name_tag_state (
    character_id INTEGER PRIMARY KEY,
    item_id INTEGER NOT NULL DEFAULT 0,
    expire_time INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_avatar_uid_sequence (
    avatar_uid INTEGER PRIMARY KEY AUTOINCREMENT
);

CREATE TABLE IF NOT EXISTS account_cargo_state (
    account_id INTEGER PRIMARY KEY,
    selection_key INTEGER NOT NULL DEFAULT 0,
    value32 INTEGER NOT NULL DEFAULT 0,
    item_count INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS account_cargo_items (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    account_id INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_template_id INTEGER NOT NULL,
    item_kind TEXT NOT NULL DEFAULT 'unknown',
    stack_count INTEGER NOT NULL DEFAULT 0,
    instance_value INTEGER NOT NULL DEFAULT 0,
    durability INTEGER NOT NULL DEFAULT 0,
    seal_flag INTEGER NOT NULL DEFAULT 0,
    option_value INTEGER NOT NULL DEFAULT 0,
    expire_time INTEGER NOT NULL DEFAULT 0,
    marker_16 INTEGER NOT NULL DEFAULT 0,
    extra_json TEXT NOT NULL DEFAULT '{}',
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(account_id, slot_index),
    FOREIGN KEY (account_id) REFERENCES accounts(account_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS account_cargo_new_items (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    account_id INTEGER NOT NULL,
    character_id INTEGER,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_core BLOB NOT NULL CHECK(length(item_core) = 82),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(account_id, slot_index),
    FOREIGN KEY (account_id) REFERENCES accounts(account_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS account_premiums (
    account_id INTEGER NOT NULL,
    premium_type INTEGER NOT NULL,
    end_time INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (account_id, premium_type),
    FOREIGN KEY (account_id) REFERENCES accounts(account_id) ON DELETE CASCADE
);


CREATE TABLE IF NOT EXISTS item_audit_log (
    audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
    owner_scope TEXT NOT NULL,
    owner_id INTEGER NOT NULL,
    character_id INTEGER,
    action_name TEXT NOT NULL,
    list_type INTEGER,
    slot_index INTEGER,
    item_uid INTEGER,
    item_template_id INTEGER,
    delta_stack_count INTEGER NOT NULL DEFAULT 0,
    payload_json TEXT NOT NULL DEFAULT '{}',
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_item_audit_log_char_time
    ON item_audit_log(character_id, created_at);

CREATE TABLE IF NOT EXISTS inventory_audit_log_v2 (
    audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    session_id TEXT,
    owner_scope TEXT NOT NULL DEFAULT 'character' CHECK(owner_scope IN ('character', 'account')),
    owner_id INTEGER NOT NULL DEFAULT 0,
    character_id INTEGER NOT NULL DEFAULT 0,
    account_id INTEGER NOT NULL DEFAULT 0,
    action_name TEXT NOT NULL,
    list_type INTEGER,
    slot_index INTEGER,
    item_id INTEGER NOT NULL DEFAULT 0,
    item_kind INTEGER NOT NULL DEFAULT 0,
    value_before INTEGER NOT NULL DEFAULT 0,
    value_after INTEGER NOT NULL DEFAULT 0,
    count_before INTEGER NOT NULL DEFAULT 0,
    count_after INTEGER NOT NULL DEFAULT 0,
    count_delta INTEGER NOT NULL DEFAULT 0,
    before_core_hash TEXT,
    after_core_hash TEXT,
    payload_json TEXT NOT NULL DEFAULT '{}'
);

CREATE INDEX IF NOT EXISTS idx_inventory_audit_v2_char_time
    ON inventory_audit_log_v2(character_id, created_at);

CREATE INDEX IF NOT EXISTS idx_inventory_audit_v2_account_time
    ON inventory_audit_log_v2(account_id, created_at);

CREATE INDEX IF NOT EXISTS idx_inventory_audit_v2_action_time
    ON inventory_audit_log_v2(action_name, created_at);

-- SP/TP 由 SkillPointLedger 从已学技能全量派生, 不落库(迁移23退役了镜像表)。
CREATE TABLE IF NOT EXISTS character_skills (
    character_id INTEGER NOT NULL,
    page_index INTEGER NOT NULL DEFAULT 0,
    slot INTEGER NOT NULL DEFAULT -1,
    skill_id INTEGER NOT NULL DEFAULT 0,
    level INTEGER NOT NULL DEFAULT 0,
    extra_values BLOB,
    PRIMARY KEY (character_id, page_index, slot),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_dark_knight_combo_skill_pages (
    character_id INTEGER NOT NULL,
    page_index INTEGER NOT NULL CHECK (page_index >= 0 AND page_index <= 1),
    body BLOB NOT NULL,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (character_id, page_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
-- 守护者盾牌 deck: slot 0=当前主盾, slot 1..4=备用盾。
-- 空槽不落行，repository 加载时固定补齐为 5 个零值槽。
CREATE TABLE IF NOT EXISTS character_knight_shield_deck (
    character_id INTEGER NOT NULL,
    slot_index INTEGER NOT NULL CHECK (slot_index >= 0 AND slot_index <= 4),
    shield_item_id INTEGER NOT NULL CHECK (shield_item_id > 0),
    PRIMARY KEY (character_id, slot_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_mercenary_support (
    owner_character_id INTEGER NOT NULL,
    slot INTEGER NOT NULL,
    support_character_id INTEGER NOT NULL,
    skill_id INTEGER NOT NULL,
    striker_skill_id INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (owner_character_id, slot),
    FOREIGN KEY (owner_character_id) REFERENCES characters(character_id) ON DELETE CASCADE,
    FOREIGN KEY (support_character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_creatures (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    creature_key INTEGER NOT NULL,
    field04 INTEGER NOT NULL DEFAULT 0,
    mode_flag INTEGER NOT NULL DEFAULT 0,
    progress_value INTEGER NOT NULL DEFAULT 0,
    mode1_field0a INTEGER NOT NULL DEFAULT 0,
    mode1_field0b INTEGER NOT NULL DEFAULT 0,
    field_after_value INTEGER NOT NULL DEFAULT 0,
    creature_text BLOB,
    tail_flag INTEGER NOT NULL DEFAULT 0,
    extra_json TEXT NOT NULL DEFAULT '{}',
    PRIMARY KEY (character_id, sort_order),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_character_creatures_key
    ON character_creatures(character_id, creature_key);

CREATE TABLE IF NOT EXISTS character_creature_uid_sequence (
    creature_uid INTEGER PRIMARY KEY AUTOINCREMENT
);

-- Removed 18 columns verified via seed DB (DfoDbGenerator) as safe:
--   A) Overwritten by account_settings/account_premiums: hotkey_key_type, main_game_option_blob,
--      quickchat_bank0, quickchat_bank1, ack_premium_blob
--   B) Seed value = 0/all-zero, no dynamic write: shop_coin_event_flag, level60_ui_state,
--      boss_tower_placeholder, event_info_tail_byte, mailbox_loaded_count, mailbox_mode,
--      mailbox_not_loaded_count, mailbox_unknown_count_c, ack_account_reg_time,
--      ack_quest_display_ids, racing_dungeon_group_flags, ack_post_tutorial_u16, ack_unread_tail
CREATE TABLE IF NOT EXISTS character_init_flags (
    character_id INTEGER PRIMARY KEY,
    pc_room_state INTEGER NOT NULL DEFAULT 0,                       -- seed=2
    expert_job_blob BLOB,                                           -- QuestService writes on job change
    champion_break_key_id INTEGER NOT NULL DEFAULT 0,               -- NOTI 0x025B: i32 key + u8 mode + i32 value
    champion_break_mode INTEGER NOT NULL DEFAULT 0,
    champion_break_value INTEGER NOT NULL DEFAULT 0,
    character_option_blob BLOB,                                     -- CMD 0x01C0 SAVE_CHARACTER_OPTION
    charac_invisible_falgs_payload_len INTEGER NOT NULL DEFAULT 0,  -- QuestService writes; seed=21000
    racing_dungeon_current_enter_count INTEGER NOT NULL DEFAULT 0,  -- seed=5
    -- CMD 0x0004 SELECT_CHARACTER ACK (non-zero seeds retained)
    ack_char_slot_index INTEGER NOT NULL DEFAULT 0,                 -- overwritten by TownId at runtime; seed=2
    ack_fatigue_battery INTEGER NOT NULL DEFAULT 0,                 -- seed=3073
    ack_fatigue_grownup_buff INTEGER NOT NULL DEFAULT 0,            -- seed=513
    ack_trade_punish_flag INTEGER NOT NULL DEFAULT 0,               -- seed=30
    ack_extra_field_86jp INTEGER NOT NULL DEFAULT 0,                -- seed=9247
    ack_tutorial_skipable INTEGER NOT NULL DEFAULT 0,               -- DungeonTutorialHandler writes
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_item_values (
    character_id INTEGER NOT NULL,
    list_kind TEXT NOT NULL,
    sort_order INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    value INTEGER NOT NULL,
    PRIMARY KEY (character_id, list_kind, sort_order),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_item_locks (
    character_id INTEGER NOT NULL,
    equipment_lock_id INTEGER NOT NULL,
    inventory_list_type INTEGER NOT NULL,
    slot INTEGER NOT NULL,
    state INTEGER NOT NULL,
    remaining_seconds INTEGER,
    PRIMARY KEY (character_id, equipment_lock_id),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_growth_weapon_stages (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    stage_id INTEGER NOT NULL,
    PRIMARY KEY (character_id, sort_order),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_pvp_missions (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    mission_id INTEGER NOT NULL,
    progress_value INTEGER NOT NULL,
    PRIMARY KEY (character_id, sort_order),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_dungeon_permissions (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    dungeon_id INTEGER NOT NULL,
    clear_state INTEGER NOT NULL,
    PRIMARY KEY (character_id, sort_order),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_dungeon_permissions_dungeon
    ON character_dungeon_permissions(character_id, dungeon_id);

CREATE TABLE IF NOT EXISTS character_hotkey_slots (
    character_id INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    hotkey_value INTEGER NOT NULL,
    PRIMARY KEY (character_id, slot_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- IDA 正名: 实际协议 NOTI 0x0164 CLEAR_QUEST_LIST(已清除任务 30000-bit bitmap)
-- 原名 character_invisible_falgs 是早期误判，保留表名避免 migration
CREATE TABLE IF NOT EXISTS character_invisible_falgs (
    character_id INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    flag_value INTEGER NOT NULL,
    PRIMARY KEY (character_id, slot_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- NOTI 0x0286 DAILY_CHALLENGE(每日挑战), 旧名 character_racing_dungeon_* (早期误判)
CREATE TABLE IF NOT EXISTS character_daily_challenge_groups (
    character_id INTEGER NOT NULL,
    group_index INTEGER NOT NULL,
    group_id INTEGER NOT NULL,
    PRIMARY KEY (character_id, group_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_daily_challenge_entries (
    character_id INTEGER NOT NULL,
    group_index INTEGER NOT NULL,
    entry_index INTEGER NOT NULL,
    track_like_id INTEGER NOT NULL,
    value_a INTEGER NOT NULL,
    value_b INTEGER NOT NULL,
    PRIMARY KEY (character_id, group_index, entry_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_daily_challenge_tail_ids (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    id_value INTEGER NOT NULL,
    PRIMARY KEY (character_id, sort_order),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- 成就唯一存储: 选角快照与运行时进度共用本表(旧 character_achievement blob 已并入)
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
);

CREATE TABLE IF NOT EXISTS character_titlebook (
    character_id INTEGER NOT NULL PRIMARY KEY,
    format_version INTEGER NOT NULL DEFAULT 1,
    general BLOB,
    specific BLOB,
    pvp BLOB,
    despair BLOB,
    event BLOB,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_new_titlebook (
    character_id INTEGER NOT NULL,
    category INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_core BLOB NOT NULL CHECK(length(item_core) = 82),
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (character_id, category, slot_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- IDA 正名: 实际协议 NOTI 0x0166 TITLE_BOOK_LIST(称号簿, 非成就)
-- 22B/entry: titleId + flag + 时间戳, PVF titlebook/ 交叉验证
CREATE TABLE IF NOT EXISTS character_achievement_chunks (
    character_id INTEGER NOT NULL,
    chunk_index INTEGER NOT NULL,
    mode_byte INTEGER NOT NULL DEFAULT 0,
    owner_id16 INTEGER NOT NULL DEFAULT 0,
    entries_blob BLOB,
    PRIMARY KEY (character_id, chunk_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- NOTI 0x02D5 DAILYSCHEDULE_CONTENTS_STATE(每日副本计费状态), 旧名 character_unknown725
CREATE TABLE IF NOT EXISTS character_daily_schedule_states (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    param_a INTEGER NOT NULL,
    mode_or_state INTEGER NOT NULL,
    content_id INTEGER NOT NULL,
    param_b INTEGER NOT NULL,
    PRIMARY KEY (character_id, sort_order),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- NOTI 0x02DA BUY_RESTRICT_ITEM_LIST(限购物品列表), 旧名 character_unknown730
CREATE TABLE IF NOT EXISTS character_buy_restrict_items (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    entry_id INTEGER NOT NULL,
    sentinel_or_value INTEGER NOT NULL,
    flag INTEGER NOT NULL,
    PRIMARY KEY (character_id, sort_order),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS get_userinfo_template (
    id INTEGER PRIMARY KEY DEFAULT 1,
    seed_character_id INTEGER NOT NULL DEFAULT 1000,
    pkt0_routing_byte7 INTEGER NOT NULL DEFAULT 0,
    gate_or_count1 INTEGER NOT NULL DEFAULT 32,
    gate_or_count2 INTEGER NOT NULL DEFAULT 32,
    flag_or_manage INTEGER NOT NULL DEFAULT 2,
    key_or_point INTEGER NOT NULL DEFAULT 0,
    unknown16 INTEGER NOT NULL DEFAULT 0,
    unknown32 INTEGER NOT NULL DEFAULT 0,
    pkt2_result_code INTEGER NOT NULL DEFAULT 1,
    pkt2_character_key INTEGER NOT NULL DEFAULT 0,
    pkt2_slot_flag1 INTEGER NOT NULL DEFAULT 0,
    pkt2_slot_flag2 INTEGER NOT NULL DEFAULT 1,
    pkt2_state_flag INTEGER NOT NULL DEFAULT 255,
    pkt2_flag3 INTEGER NOT NULL DEFAULT 1,
    pkt2_reserved INTEGER NOT NULL DEFAULT 0
);

-- 宠物欢迎语缓存(NOTI 0x0077 body; 可随时从 PVF 造物脚本重建, 缓存避免选角时读 PVF)
CREATE TABLE IF NOT EXISTS character_pet_welcome_cache (
    character_id INTEGER PRIMARY KEY,
    item_template_id INTEGER NOT NULL DEFAULT 0,
    body BLOB,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- 租赁物品(NOTI 0x0357 面板的服务端存储; 选角时会按背包/装备重建)
CREATE TABLE IF NOT EXISTS character_rental_items (
    character_id INTEGER NOT NULL,
    shop_entry_id INTEGER NOT NULL,
    inventory_template_id INTEGER NOT NULL DEFAULT 0,
    expire_time INTEGER NOT NULL,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_character_rental_items_char
    ON character_rental_items(character_id);

-- 晶体契约选择(NOTI 0x0300 的 cube_type/cube_grade 两字节)
CREATE TABLE IF NOT EXISTS character_crystal_contract (
    character_id INTEGER PRIMARY KEY,
    cube_type INTEGER NOT NULL DEFAULT 0,
    cube_grade INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- NOTI 0x0002 subtype 0 (USERINFO Minimum) 104B tail 的结构化字段。
-- 布局: Reverse/INIT_PACKET/0x0002_USERINFO_SUBTYPE0.md (IDA readUserInfoMinimum 0xF55490 逐 PacketPop 验证)
-- 不入表的字段: isAlive(+38,恒1) / 86jp_reserved(+46..+52,客户端 dead store) / isOver14(+70,恒100)
--               progressA/B(+57/+61) 与 skillTreeIndex(+79) 同源 character_subtype1_fields (客户端同 obj 偏移 0x394/0x398)
CREATE TABLE IF NOT EXISTS character_subtype0_fields (
    character_id INTEGER PRIMARY KEY,
    name_tag_item_id INTEGER NOT NULL DEFAULT 0,        -- 旧列保留兼容；86 tail首u32改由 characters.clone_title_item_id 提供
    creature_field1 INTEGER NOT NULL DEFAULT 0,         -- +4  u8
    creature_field2 INTEGER NOT NULL DEFAULT 0,         -- +5  u8
    creature_field3 INTEGER NOT NULL DEFAULT 0,         -- +6  u8 (客户端读后未用)
    creature_field4 INTEGER NOT NULL DEFAULT 0,         -- +7  u8 (客户端读后未用)
    creature_buffer BLOB,                               -- +8  8B i64; low32!=0 → 创建宠物实体到 slot 24 (sub_F55120)
    stamina INTEGER NOT NULL DEFAULT 0,                 -- +16 u8  体力 (readEntryByteOffset648)
    fatigue_penalty INTEGER NOT NULL DEFAULT 0,         -- +17 u32 疲劳恢复惩罚 (readEntryDwordOffset672)
    is_event_character INTEGER NOT NULL DEFAULT 0,      -- +21 u8
    pc_room_id INTEGER NOT NULL DEFAULT 65537,          -- +22 u32 (sub_F502B0; 真机无PC房=0x00010001)
    is_private_store INTEGER NOT NULL DEFAULT 0,        -- +26 u8
    is_premium_pc_room INTEGER NOT NULL DEFAULT 0,      -- +27 u8
    server_group_id INTEGER NOT NULL DEFAULT 0,         -- +28 u8 (readEntryByteOffset704)
    black_count INTEGER NOT NULL DEFAULT 0,             -- +29 u32
    guild_level INTEGER NOT NULL DEFAULT 0,             -- +33 u8 (sub_F51710)
    chaos_point INTEGER NOT NULL DEFAULT 0,             -- +34 u32
    disguise_kind INTEGER NOT NULL DEFAULT 0,           -- +39 u8 (sub_F53450)
    is_disguised INTEGER NOT NULL DEFAULT 0,            -- +40 u8
    expert_job_type INTEGER NOT NULL DEFAULT 0,         -- +41 u8  副职业类型 (sub_F51830)
    expert_job_exp INTEGER NOT NULL DEFAULT 0,          -- +42 u32 副职业经验
    is_hardcore_mode INTEGER NOT NULL DEFAULT 0,        -- +53 u8 (readHardcoreMinimum)
    is_hardcore_dead INTEGER NOT NULL DEFAULT 0,        -- +54 u8
    hardcore_death_count INTEGER NOT NULL DEFAULT 0,    -- +55 u16
    user_state_bits INTEGER NOT NULL DEFAULT 3,         -- +65 u8 复合位 (sub_F50340; 3=城镇可见)
    chat_ban_end_time INTEGER NOT NULL DEFAULT 0,       -- +66 u32
    fatigue_update INTEGER NOT NULL DEFAULT 0,          -- +71 u16
    return_user_flag INTEGER NOT NULL DEFAULT 1,        -- +73 u8 (sub_1FAC210; 默认1=旧builder新角色基线)
    channel_display_mode INTEGER NOT NULL DEFAULT 0,    -- +74 u16
    channel_type INTEGER NOT NULL DEFAULT 0,            -- +76 u8
    channel_id INTEGER NOT NULL DEFAULT 2,              -- legacy field, no longer serialized into subtype0 +77
    mood_value INTEGER NOT NULL DEFAULT 0,              -- +77 u16 mood popup default; 0=normal
    is_return_user INTEGER NOT NULL DEFAULT 0,          -- +80 u8
    link_slot_enabled INTEGER NOT NULL DEFAULT 0,       -- +81 u8
    link_type_a INTEGER NOT NULL DEFAULT 0,             -- +82 u8 (sub_F50410)
    link_type_b INTEGER NOT NULL DEFAULT 0,             -- +83 u8
    emotion_index INTEGER NOT NULL DEFAULT 0,           -- +84 u16
    action_byte INTEGER NOT NULL DEFAULT 0,             -- +86 u8
    fatigue_display_update INTEGER NOT NULL DEFAULT 0,  -- +87 u16
    costume_flag INTEGER NOT NULL DEFAULT 0,            -- +89 u8 obj[865]
    aura_flag INTEGER NOT NULL DEFAULT 0,               -- +90 u8 obj+868
    pet_display_flag INTEGER NOT NULL DEFAULT 0,        -- +91 u8 obj+872
    title_display_flag INTEGER NOT NULL DEFAULT 0,      -- +92 u8 obj[876]
    pvp_stat_a INTEGER NOT NULL DEFAULT 0,              -- +93 u32 (sub_F50BA0)
    pvp_win_streak INTEGER NOT NULL DEFAULT 0,          -- +97 u8
    pvp_lose_streak INTEGER NOT NULL DEFAULT 0,         -- +98 u8
    pvp_rank_point INTEGER NOT NULL DEFAULT 0,          -- +99 u32
    trailing_byte INTEGER NOT NULL DEFAULT 0,           -- +103 u8
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_subtype1_fields (
    character_id INTEGER PRIMARY KEY,
    stat_hp_max INTEGER NOT NULL DEFAULT 0,
    stat_mp_max INTEGER NOT NULL DEFAULT 0,
    stat_physical_attack INTEGER NOT NULL DEFAULT 0,
    stat_physical_defense INTEGER NOT NULL DEFAULT 0,
    stat_magical_attack INTEGER NOT NULL DEFAULT 0,
    stat_magical_defense INTEGER NOT NULL DEFAULT 0,
    stat_fire_resistance INTEGER NOT NULL DEFAULT 0,
    stat_water_resistance INTEGER NOT NULL DEFAULT 0,
    stat_dark_resistance INTEGER NOT NULL DEFAULT 0,
    stat_light_resistance INTEGER NOT NULL DEFAULT 0,
    -- u16[17] 状态异常抗性(slow/freeze/poison/stun 等, ACTIVESTATUS_TAG) 不入表:
    -- .chr 不配置+十角色样本全零 → builder 直写 34B 零
    stat_inventory_limit INTEGER NOT NULL DEFAULT 0,
    stat_hp_regen_speed INTEGER NOT NULL DEFAULT 0,
    stat_mp_regen_speed INTEGER NOT NULL DEFAULT 0,
    stat_move_speed INTEGER NOT NULL DEFAULT 0,
    stat_attack_speed INTEGER NOT NULL DEFAULT 0,
    stat_cast_speed INTEGER NOT NULL DEFAULT 0,
    stat_hit_recovery INTEGER NOT NULL DEFAULT 0,
    stat_jump_power INTEGER NOT NULL DEFAULT 0,
    stat_weight INTEGER NOT NULL DEFAULT 0,
    stat_level INTEGER NOT NULL DEFAULT 0,
    name_tag_item_id INTEGER NOT NULL DEFAULT 0,     -- 名称装饰卡 itemId (sub_F546B0 i64 low32 → slot 28; 旧误名 skill_tree_check)
    name_tag_expire_time INTEGER NOT NULL DEFAULT 0, -- 名称装饰卡到期时间 (i64 high32)
    skill_tree_index INTEGER NOT NULL DEFAULT -1, -- -1=第二技能页未购买，0/1=已购买且为当前页
    equipped_creature_level INTEGER NOT NULL DEFAULT 0,
    equip_list_trailing INTEGER NOT NULL DEFAULT 0,
    manage_level INTEGER NOT NULL DEFAULT 0,
    flag_byte INTEGER NOT NULL DEFAULT 0,
    guild_power_war INTEGER NOT NULL DEFAULT 0,
    server_timestamp INTEGER NOT NULL DEFAULT 0,
    quest_shop_count INTEGER NOT NULL DEFAULT 0,
    progress1 INTEGER NOT NULL DEFAULT 0,
    progress2 INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_equipped_entries (
    character_id INTEGER NOT NULL,
    slot INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    expire_time INTEGER NOT NULL DEFAULT 0,
    equipment_lock_id INTEGER NOT NULL DEFAULT 0,
    raw_entry BLOB NOT NULL,
    PRIMARY KEY (character_id, slot),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_dimensions (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    dim_key INTEGER NOT NULL,
    val1 INTEGER NOT NULL DEFAULT 0,
    val2 INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, sort_order),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_dimension_flags (
    character_id INTEGER PRIMARY KEY,
    flag1 INTEGER NOT NULL DEFAULT 0,
    flag2 INTEGER NOT NULL DEFAULT 0,
    flag3 INTEGER NOT NULL DEFAULT 0,
    flag4 INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_sort_item_locks (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    state INTEGER NOT NULL,
    PRIMARY KEY (character_id, sort_order),
    UNIQUE(character_id, list_type, slot_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- 收集箱(SAO/生肖之灵等)槛位存档: 记录角色在某个收集箱(box_index=PVF [Index])的某个槛位放了哪个宝珠。
-- 宝珠本质仍是背包里的道具(character_new_items/在线 InventoryService), 这里只是"哪个itemId被摆在收集箱槛位里"的状态表,
-- 放入/取出时由 CollectBoxRuntimeService 联动在线背包扣减/归还。
CREATE TABLE IF NOT EXISTS character_collectbox_slots (
    character_id INTEGER NOT NULL,
    box_index INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    PRIMARY KEY (character_id, box_index, slot_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_active_quests (
    character_id INTEGER NOT NULL,
    slot INTEGER NOT NULL,
    quest_id INTEGER NOT NULL,
    trigger_value INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, slot),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- 每日/周常门控。日界=北京时间06:00, 周界=ISO周一(DailyResetService)。
-- 本表只记录"该角色的周期状态属于哪一天/哪一周"; 所有具体状态(标记/次数)
-- 一律存 character_daily_counters, 不在本表加任何业务列。
CREATE TABLE IF NOT EXISTS character_daily_reset (
    character_id INTEGER PRIMARY KEY,
    day_id       INTEGER NOT NULL DEFAULT 0,
    week_id      INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- 每日/周常状态账本: 一功能一 key, 新功能零 schema 改动。
-- counter_key 用自描述蛇形名(如 'tower_entry_used'); 布尔标记=cap1计数(领取即 value=1)。
-- period 决定清理周期: 跨天删 'day' 行 / 跨周删 'week' 行(DailyResetService.EnsureRowAndRollover)。
-- 同一 key 的 period 以首次写入为准, 调用方必须始终传同一值。
CREATE TABLE IF NOT EXISTS character_daily_counters (
    character_id INTEGER NOT NULL,
    counter_key  TEXT    NOT NULL,
    period       TEXT    NOT NULL DEFAULT 'day' CHECK (period IN ('day', 'week')),
    value        INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, counter_key),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

-- 绝望之塔永久楼层进度。客户端始终请求第一层入口，服务端按最高通关层重定向到下一层。
CREATE TABLE IF NOT EXISTS character_tower_of_despair_progress (
    character_id INTEGER PRIMARY KEY,
    highest_cleared_floor INTEGER NOT NULL DEFAULT 0
        CHECK (highest_cleared_floor >= 0 AND highest_cleared_floor <= 100),
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS account_settings (
    account_id INTEGER PRIMARY KEY,
    main_game_option BLOB,
    quickchat_bank0 BLOB,
    quickchat_bank1 BLOB,
    hotkey_key_type INTEGER NOT NULL DEFAULT 0,
    hotkey_slots BLOB,
    FOREIGN KEY (account_id) REFERENCES accounts(account_id) ON DELETE CASCADE
);

INSERT OR IGNORE INTO accounts (account_id, m_id, password_hash) VALUES
    (1, '10038', '');

-- character 和 container_state 由 EnsureInitialized 从封包样本动态 seed（不再硬编码）
