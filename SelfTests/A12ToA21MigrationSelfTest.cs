using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace DfoGmTool.SelfTests
{
    internal static class A12ToA21MigrationSelfTest
    {
        private const int MissingEpicPieceId = 909090;

        public static int Run()
        {
            var root = Path.Combine(Path.GetTempPath(), "dfo-gm-a12-a21-migration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var schema = Path.Combine(AppContext.BaseDirectory, "ServerCore", "Sqlite", "item_schema.sql");
            var pvf = Path.Combine(root, "a21.pvf");
            File.WriteAllBytes(pvf, Array.Empty<byte>()); // injected resolver keeps this fixture independent of a real PVF.
            var failures = 0;
            try
            {
                var source = Path.Combine(root, "a12.db");
                CreateSource(source, duplicateCharacterName: false);
                ProbeReadOnlyAndLeaveEmptySidecars(source);
                var before = Hash(source);
                var service = CreateService(source, pvf);

                var preview = service.Preview();
                Check("preview 成功且识别 A12 user_version=0", preview.Success && preview.Preview && preview.SourceUserVersion == 0, ref failures);
                Check("preview 返回源 SHA256 且零写入", preview.SourceSha256 == before && before == Hash(source), ref failures);
                Check("preview 报告 PVF 缺失物品", preview.PvFMissingItems > 0, ref failures);
                Check("PVF 排除项带物品 ID", preview.Issues.Any(x => x.Code == "pvf_missing" && x.ItemId == 999), ref failures);
                Check("伪史诗物品按 PVF 排除", preview.Issues.Count(x => x.Code == "pvf_missing" && x.ItemId == MissingEpicPieceId) >= 4, ref failures);
                Check("任务预览只接受合法 PVF/职业/值", preview.MigratedQuestCompletions == 2 && preview.MigratedActiveQuests == 2, ref failures);
                Check("任务预览列出非阻断排除原因", preview.Issues.Any(x => x.Code == "quest_id_invalid")
                    && preview.Issues.Any(x => x.Code == "quest_job_mismatch")
                    && preview.Issues.Any(x => x.Code == "completion_value_invalid")
                    && preview.Issues.Any(x => x.Code == "active_slot_invalid"), ref failures);

                var report = service.Execute(true, "uPdAtE");
                Check("execute 原子替换成功", report.Success && report.ReplacementCompleted && File.Exists(source), ref failures);
                Check("execute 使用预检源 SHA256", report.SourceSha256 == before, ref failures);
                Check("execute 报告迁移计数与预览一致", report.MigratedRows == preview.MigratedRows, ref failures);
                Check("完成状态只写入合法记录", Count(source, "SELECT COUNT(*) FROM character_quest_completions WHERE character_id=10") == 2
                    && Count(source, "SELECT completion_value FROM character_quest_completions WHERE character_id=10 AND quest_id=124") == 7
                    && Count(source, "SELECT completion_value FROM character_quest_completions WHERE character_id=10 AND quest_id=125") == 2, ref failures);
                Check("进行中任务跳过非法槽/重复/已完成", Count(source, "SELECT COUNT(*) FROM character_active_quests WHERE character_id=10") == 2, ref failures);
                Check("合法 activation_id 保留且非法值重建为 N GUID", ActiveActivationOk(source, 123, "00112233445566778899aabbccddeeff")
                    && ActiveActivationOk(source, 127, null), ref failures);
                Check("任务 payload 按实际最大完成任务重建", Count(source, "SELECT charac_invisible_falgs_payload_len FROM character_init_flags WHERE character_id=10") == 126, ref failures);
                Check("PVF 缺失物品不写入目标且执行成功", report.Success && !TargetHasItem(source, 999), ref failures);
                Check("伪史诗物品不写入目标史诗 blob", report.Success && !TargetEpicBlobContains(source, MissingEpicPieceId), ref failures);
                Check("成功替换后不留下旧 WAL/SHM", !File.Exists(source + "-wal") && !File.Exists(source + "-shm"), ref failures);
                Check("成功不留下自动回滚备份", !Directory.EnumerateFiles(root, "*.a12-rollback-*.db").Any(), ref failures);
                Check("目标 schema-v5 可被 guard 打开", GuardOk(source), ref failures);
                Check("账号/角色 ID 与 -1 槽位已保留并重排", Count(source, "SELECT COUNT(*) FROM accounts WHERE account_id=1") == 1 && Count(source, "SELECT slot_index FROM characters WHERE character_id=10") == 2, ref failures);
                Check("82B/extra_json 转 99B 且保留前缀", HasCore(source, 10, ItemCore.KindConsumable, 100, ItemCore.EnchantCardIdOffset, 0x123456), ref failures);
                Check("新版 82B 物品优先导入", Count(source, "SELECT COUNT(*) FROM character_inventory_items WHERE character_id=10 AND item_core IS NOT NULL") >= 4, ref failures);
                Check("虚拟金币/复活币进入 A21 槽", HasVirtual(source, 10, 0, 1234) && HasVirtual(source, 10, 1, 5), ref failures);
                Check("晶块按账号原生字段取各角色最大值且不重复累计", Count(source, "SELECT cube_black FROM accounts WHERE account_id=1") == 12, ref failures);
                Check("灵魂 new 优先、旧表补缺并按账号最大值写入", Count(source, "SELECT soul_10100115 FROM accounts WHERE account_id=1") == 7, ref failures);
                Check("宠物 detail 按旧 key 复制", Count(source, "SELECT COUNT(*) FROM character_creatures WHERE character_id=10 AND field04=42") == 1, ref failures);
                Check("时装 detail UID 重映射并保留数据", Count(source, "SELECT COUNT(*) FROM character_avatar_detail WHERE character_id=10 AND clear_avatar_id=77") == 1, ref failures);
                Check("slot28 写入 name-tag 状态", Count(source, "SELECT COUNT(*) FROM character_name_tag_state WHERE character_id=10 AND item_id=400") == 1, ref failures);
                Check("称号簿新表写入", Count(source, "SELECT COUNT(*) FROM character_titlebook_items WHERE character_id=10") == 1, ref failures);
                Check("锁与新槽位一致", Count(source, "SELECT COUNT(*) FROM character_item_locks WHERE character_id=10 AND equipment_lock_id=9") == 1, ref failures);
                Check("外键检查为 0", Count(source, "PRAGMA foreign_key_check;") == 0, ref failures);

                var rollback = Path.Combine(root, "rollback.db");
                CreateSource(rollback, duplicateCharacterName: true);
                var rollbackHash = Hash(rollback);
                var failed = CreateService(rollback, pvf).Execute(true, A12ToA21MigrationService.RequiredConfirmation);
                Check("事务/建库失败返回结构化错误", !failed.Success && !string.IsNullOrWhiteSpace(failed.Error), ref failures);
                Check("失败不替换源文件", rollbackHash == Hash(rollback) && Count(rollback, "SELECT COUNT(*) FROM accounts") == 1, ref failures);

                var confirmationIndex = 0;
                foreach (var confirmation in new[] { "update", "UPDATE" })
                {
                    var confirmationPath = Path.Combine(root, "confirmation-" + confirmationIndex++ + ".db");
                    CreateSource(confirmationPath, duplicateCharacterName: false);
                    var accepted = CreateService(confirmationPath, pvf).Execute(true, confirmation);
                    Check("确认词 " + confirmation + " 可执行", accepted.Success && accepted.ReplacementCompleted, ref failures);
                }
                var wrongConfirmationPath = Path.Combine(root, "confirmation-wrong.db");
                CreateSource(wrongConfirmationPath, duplicateCharacterName: false);
                var wrongConfirmationHash = Hash(wrongConfirmationPath);
                var rejectedConfirmation = CreateService(wrongConfirmationPath, pvf).Execute(true, "upgrade");
                Check("其他确认词拒绝且源文件不变", !rejectedConfirmation.Success && wrongConfirmationHash == Hash(wrongConfirmationPath), ref failures);

                var newOnly = Path.Combine(root, "new-only.db");
                CreateSource(newOnly, duplicateCharacterName: false);
                ExecSql(newOnly, "DROP TABLE character_items; ALTER TABLE characters ADD COLUMN source_extra TEXT; PRAGMA user_version=987;");
                var newOnlyPreview = CreateService(newOnly, pvf).Preview();
                Check("new-only + 额外列 + 任意 user_version 可预览", newOnlyPreview.Success && newOnlyPreview.SourceUserVersion == 987, ref failures);
                var newOnlyReport = CreateService(newOnly, pvf).Execute(true, "UPDATE");
                Check("new-only 可完成迁移", newOnlyReport.Success && newOnlyReport.ReplacementCompleted, ref failures);

                var residualInvalid = Path.Combine(root, "new-items-with-invalid-legacy.db");
                CreateSource(residualInvalid, duplicateCharacterName: false);
                ExecSql(residualInvalid, "ALTER TABLE character_items RENAME COLUMN item_template_id TO item_template_id_legacy;");
                var residualPreview = CreateService(residualInvalid, pvf).Preview();
                Check("残留角色物品表缺核心列明确拒绝", !residualPreview.Success
                    && residualPreview.Error.Contains("character_items.item_template_id", StringComparison.Ordinal), ref failures);

                var missingAccounts = Path.Combine(root, "missing-accounts.db");
                CreateSource(missingAccounts, duplicateCharacterName: false);
                ExecSql(missingAccounts, "PRAGMA foreign_keys=OFF; DROP TABLE accounts;");
                var missingAccountsPreview = CreateService(missingAccounts, pvf).Preview();
                Check("缺少 accounts 明确拒绝", !missingAccountsPreview.Success
                    && missingAccountsPreview.Error.Contains("表 accounts", StringComparison.Ordinal), ref failures);

                var missingCharacters = Path.Combine(root, "missing-characters.db");
                CreateSource(missingCharacters, duplicateCharacterName: false);
                ExecSql(missingCharacters, "PRAGMA foreign_keys=OFF; DROP TABLE characters;");
                var missingCharactersPreview = CreateService(missingCharacters, pvf).Preview();
                Check("缺少 characters 明确拒绝", !missingCharactersPreview.Success
                    && missingCharactersPreview.Error.Contains("表 characters", StringComparison.Ordinal), ref failures);

                var missingItems = Path.Combine(root, "missing-role-items.db");
                CreateSource(missingItems, duplicateCharacterName: false);
                ExecSql(missingItems, "DROP TABLE character_items; DROP TABLE character_new_items;");
                var missingItemsPreview = CreateService(missingItems, pvf).Preview();
                Check("缺少角色物品清单明确拒绝", !missingItemsPreview.Success
                    && missingItemsPreview.Error.Contains("此 S4A12 数据库结构版本不再支持", StringComparison.Ordinal), ref failures);

                var missingColumn = Path.Combine(root, "missing-role-item-column.db");
                CreateSource(missingColumn, duplicateCharacterName: false);
                ExecSql(missingColumn, "DROP TABLE character_items; ALTER TABLE character_new_items RENAME COLUMN item_core TO item_core_legacy;");
                var missingColumnPreview = CreateService(missingColumn, pvf).Preview();
                Check("角色物品核心列缺失明确拒绝", !missingColumnPreview.Success
                    && missingColumnPreview.Error.Contains("character_new_items.item_core", StringComparison.Ordinal), ref failures);

                var walPath = Path.Combine(root, "active-wal.db");
                CreateSource(walPath, duplicateCharacterName: false);
                var walHash = Hash(walPath);
                File.WriteAllBytes(walPath + "-wal", new byte[] { 0x37, 0x7A, 0x00, 0x01 });
                var walPreview = CreateService(walPath, pvf).Preview();
                Check("非空 WAL 预览 fail-closed", !walPreview.Success
                    && walPreview.Error.IndexOf("未合并的 WAL", StringComparison.OrdinalIgnoreCase) >= 0
                    && walPreview.Error.IndexOf("checkpoint", StringComparison.OrdinalIgnoreCase) >= 0, ref failures);
                Check("非空 WAL 拒绝时源 SHA 不变", walHash == Hash(walPath), ref failures);

                var locked = Path.Combine(root, "locked-sidecar.db");
                CreateSource(locked, duplicateCharacterName: false);
                File.WriteAllBytes(locked + "-shm", new byte[32768]);
                var lockedHash = Hash(locked);
                using (var heldSidecar = new FileStream(locked + "-shm", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var lockedPreview = CreateService(locked, pvf).Preview();
                    Check("被占用 SHM 预览拒绝", !lockedPreview.Success
                        && lockedPreview.Error.IndexOf("SHM", StringComparison.OrdinalIgnoreCase) >= 0, ref failures);
                }
                Check("被占用 SHM 拒绝时源 SHA 不变", lockedHash == Hash(locked), ref failures);
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine("A12ToA21MigrationSelfTest EXCEPTION: " + ex);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }

            Console.WriteLine(failures == 0 ? "A12ToA21MigrationSelfTest OK" : "A12ToA21MigrationSelfTest FAIL: " + failures);
            return failures == 0 ? 0 : 1;
        }

        private static A12ToA21MigrationService CreateService(string database, string pvf)
        {
            return new A12ToA21MigrationService(
                database,
                pvf,
                id => id != MissingEpicPieceId && new[] { 100, 200, 300, 400, 500, 600, 700, 3033, 10100115 }.Contains(id),
                id => id switch
                {
                    MissingEpicPieceId => ItemCore.KindEpicPiece,
                    100 or 300 or 3033 or 10100115 => ItemCore.KindConsumable,
                    200 or 400 or 700 => ItemCore.KindEquipment,
                    500 => ItemCore.KindCreature,
                    600 => ItemCore.KindAvatar,
                    _ => ItemCore.KindUnknown,
                },
                id => new[] { 123, 124, 125, 126, 127 }.Contains(id),
                (id, job, grow) => id != 126 && (id != 127 || (job == 0 && grow == 0)));
        }

        private static void CreateSource(string path, bool duplicateCharacterName)
        {
            using var connection = Open(path);
            Exec(connection, @"
CREATE TABLE accounts(account_id INTEGER PRIMARY KEY, m_id TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL DEFAULT '', cera INTEGER NOT NULL DEFAULT 0, cube_black INTEGER NOT NULL DEFAULT 0, epic_piece_counts BLOB NOT NULL DEFAULT X'');
CREATE TABLE characters(character_id INTEGER PRIMARY KEY, account_id INTEGER NOT NULL, name TEXT NOT NULL, job INTEGER NOT NULL DEFAULT 0, grow_type INTEGER NOT NULL DEFAULT 0, level INTEGER NOT NULL DEFAULT 1, gold INTEGER NOT NULL DEFAULT 0, coin INTEGER NOT NULL DEFAULT 0, slot_index INTEGER NOT NULL DEFAULT -1);
CREATE TABLE account_character_entries(id INTEGER PRIMARY KEY, entry_index INTEGER NOT NULL, slot_index INTEGER NOT NULL, name TEXT NOT NULL, name_bytes BLOB, body_after_name BLOB NOT NULL);
CREATE TABLE character_items(item_uid INTEGER PRIMARY KEY AUTOINCREMENT, owner_scope TEXT NOT NULL, owner_id INTEGER NOT NULL, character_id INTEGER, list_type INTEGER NOT NULL, slot_index INTEGER NOT NULL, item_template_id INTEGER NOT NULL, item_kind TEXT NOT NULL DEFAULT 'unknown', stack_count INTEGER NOT NULL DEFAULT 0, instance_value INTEGER NOT NULL DEFAULT 0, durability INTEGER NOT NULL DEFAULT 0, seal_flag INTEGER NOT NULL DEFAULT 0, option_value INTEGER NOT NULL DEFAULT 0, equipment_lock_id INTEGER NOT NULL DEFAULT 0, expire_time INTEGER NOT NULL DEFAULT 0, marker_16 INTEGER NOT NULL DEFAULT -1, pet_serial_or_handle INTEGER NOT NULL DEFAULT 0, extra_json TEXT NOT NULL DEFAULT '{}', item_core BLOB, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE character_new_items(item_uid INTEGER PRIMARY KEY AUTOINCREMENT, owner_scope TEXT NOT NULL, owner_id INTEGER NOT NULL, character_id INTEGER, list_type INTEGER NOT NULL, slot_index INTEGER NOT NULL, item_core BLOB NOT NULL, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE account_cargo_items(item_uid INTEGER PRIMARY KEY AUTOINCREMENT, account_id INTEGER NOT NULL, character_id INTEGER, slot_index INTEGER NOT NULL, item_template_id INTEGER NOT NULL, item_kind TEXT NOT NULL DEFAULT 'unknown', stack_count INTEGER NOT NULL DEFAULT 0, instance_value INTEGER NOT NULL DEFAULT 0, durability INTEGER NOT NULL DEFAULT 0, seal_flag INTEGER NOT NULL DEFAULT 0, option_value INTEGER NOT NULL DEFAULT 0, equipment_lock_id INTEGER NOT NULL DEFAULT 0, expire_time INTEGER NOT NULL DEFAULT 0, marker_16 INTEGER NOT NULL DEFAULT -1, extra_json TEXT NOT NULL DEFAULT '{}', item_core BLOB, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE account_cargo_new_items(item_uid INTEGER PRIMARY KEY AUTOINCREMENT, account_id INTEGER NOT NULL, character_id INTEGER, list_type INTEGER NOT NULL DEFAULT 12, slot_index INTEGER NOT NULL, item_core BLOB NOT NULL, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE character_equipped_entries(character_id INTEGER NOT NULL, slot INTEGER NOT NULL, item_id INTEGER NOT NULL, expire_time INTEGER NOT NULL DEFAULT 0, equipment_lock_id INTEGER NOT NULL DEFAULT 0, raw_entry BLOB NOT NULL, PRIMARY KEY(character_id,slot));
CREATE TABLE character_active_quests(character_id INTEGER NOT NULL, slot INTEGER NOT NULL, quest_id INTEGER NOT NULL, trigger_value INTEGER NOT NULL DEFAULT 0, activation_id TEXT, PRIMARY KEY(character_id,slot));
CREATE TABLE character_quest_completions(character_id INTEGER NOT NULL, quest_id INTEGER NOT NULL, completion_value INTEGER NOT NULL, PRIMARY KEY(character_id,quest_id));
CREATE TABLE character_invisible_falgs(character_id INTEGER NOT NULL, slot_index INTEGER NOT NULL, flag_value INTEGER NOT NULL, PRIMARY KEY(character_id,slot_index));
CREATE TABLE character_achievement_complete(character_id INTEGER NOT NULL, sort_order INTEGER NOT NULL, achievement_id INTEGER NOT NULL, p1 INTEGER NOT NULL DEFAULT 0, p2 INTEGER NOT NULL DEFAULT 0, p3 INTEGER NOT NULL DEFAULT 0, p4 INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(character_id,sort_order));
CREATE TABLE character_item_locks(character_id INTEGER NOT NULL, sort_order INTEGER, type_or_list INTEGER, item_key_or_slot INTEGER, state INTEGER NOT NULL, extra_value INTEGER, equipment_lock_id INTEGER, inventory_list_type INTEGER, slot INTEGER);
CREATE TABLE character_creatures(character_id INTEGER NOT NULL, sort_order INTEGER NOT NULL, creature_key INTEGER NOT NULL, field04 INTEGER NOT NULL DEFAULT 0, mode_flag INTEGER NOT NULL DEFAULT 0, progress_value INTEGER NOT NULL DEFAULT 0, mode1_field0a INTEGER NOT NULL DEFAULT 0, mode1_field0b INTEGER NOT NULL DEFAULT 0, field_after_value INTEGER NOT NULL DEFAULT 0, creature_text BLOB, tail_flag INTEGER NOT NULL DEFAULT 0, extra_json TEXT NOT NULL DEFAULT '{}');
CREATE TABLE character_avatar_detail(item_uid INTEGER PRIMARY KEY, owner_id INTEGER NOT NULL DEFAULT 0, character_id INTEGER NOT NULL DEFAULT 0, item_id INTEGER NOT NULL DEFAULT 0, expire_date INTEGER NOT NULL DEFAULT 0, clear_avatar_id INTEGER NOT NULL DEFAULT 0, jewel_socket BLOB, color1 INTEGER NOT NULL DEFAULT 0, color2 INTEGER NOT NULL DEFAULT 0, delete_date INTEGER NOT NULL DEFAULT 0);
CREATE TABLE character_new_titlebook(character_id INTEGER NOT NULL, category INTEGER NOT NULL, slot_index INTEGER NOT NULL, item_core BLOB NOT NULL);
CREATE TABLE account_cargo_state(account_id INTEGER PRIMARY KEY, selection_key INTEGER NOT NULL DEFAULT 0, value32 INTEGER NOT NULL DEFAULT 0, item_count INTEGER NOT NULL DEFAULT 0);
CREATE TABLE account_settings(account_id INTEGER PRIMARY KEY, main_game_option BLOB, quickchat_bank0 BLOB, quickchat_bank1 BLOB, hotkey_key_type INTEGER NOT NULL DEFAULT 0, hotkey_slots BLOB);
CREATE TABLE account_premiums(account_id INTEGER NOT NULL, premium_type INTEGER NOT NULL, end_time INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(account_id,premium_type));
CREATE TABLE character_container_state(character_id INTEGER NOT NULL, list_type INTEGER NOT NULL, list_param16 INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(character_id,list_type));");
            Exec(connection, "INSERT INTO accounts(account_id,m_id,cera,cube_black) VALUES(1,'source-account',100,9);");
            Exec(connection, $"INSERT INTO characters(character_id,account_id,name,level,gold,coin,slot_index) VALUES(10,1,'source-role',90,1234,5,-1);INSERT INTO characters(character_id,account_id,name,slot_index) VALUES(11,1,'{(duplicateCharacterName ? "source-role" : "source-role-2")}',-1);");
            Exec(connection, "INSERT INTO account_character_entries(id,entry_index,slot_index,name,body_after_name) VALUES(1,0,2,'source-role',X'00');");

            var extra = "{\"extData0\":7,\"prefixData0E\":\"5634120002030405\",\"middleData1A\":\"0000000000000000000000000000000000\",\"tailData2F\":\"00000000000000000000000000000000000000000000000000000000000000000000000000\"}";
            Exec(connection, $"INSERT INTO character_items(owner_scope,owner_id,character_id,list_type,slot_index,item_template_id,item_kind,stack_count,instance_value,equipment_lock_id,extra_json) VALUES('character',10,10,0,65,100,'stackable',7,7,9,'{extra}');");
            Exec(connection, "INSERT INTO character_items(owner_scope,owner_id,character_id,list_type,slot_index,item_template_id,item_kind,stack_count,instance_value) VALUES('character',10,10,0,66,999,'stackable',2,2);");
            Exec(connection, $"INSERT INTO character_items(owner_scope,owner_id,character_id,list_type,slot_index,item_template_id,item_kind,stack_count,instance_value) VALUES('character',10,10,0,67,{MissingEpicPieceId},'epic-piece',2,2);");
            Exec(connection, "INSERT INTO character_items(owner_scope,owner_id,character_id,list_type,slot_index,item_template_id,item_kind,pet_serial_or_handle,extra_json) VALUES('character',10,10,7,0,500,'pet',77,'{}');");
            Exec(connection, "INSERT INTO character_items(owner_scope,owner_id,character_id,list_type,slot_index,item_template_id,item_kind,stack_count,instance_value) VALUES('character',10,10,0,354,3033,'stackable',15,15),('character',11,11,0,354,3033,'stackable',10,10),('character',10,10,0,360,10100115,'stackable',9,9),('character',11,11,0,360,10100115,'stackable',7,7);");

            var newEquipment = MakeCore82(ItemCore.KindEquipment, 200, 1);
            ExecBlob(connection, "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core) VALUES('character',10,10,0,66,@core);", newEquipment);
            var missingEpic = MakeCore82(ItemCore.KindEpicPiece, MissingEpicPieceId, 2);
            ExecBlob(connection, "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core) VALUES('character',10,10,0,67,@core);", missingEpic);
            var newCube = MakeCore82(ItemCore.KindConsumable, 3033, 12);
            ExecBlob(connection, "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core) VALUES('character',10,10,0,354,@core);", newCube);
            var newSoul = MakeCore82(ItemCore.KindConsumable, 10100115, 4);
            ExecBlob(connection, "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core) VALUES('character',10,10,0,360,@core);", newSoul);
            var newAvatar = MakeCore82(ItemCore.KindAvatar, 600, 123);
            ExecBlob(connection, "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core) VALUES('character',10,10,1,0,@core);", newAvatar);
            ExecBlob(connection, "INSERT INTO character_avatar_detail(item_uid,owner_id,character_id,item_id,expire_date,clear_avatar_id,jewel_socket,color1,color2) VALUES(123,1,10,600,999,77,zeroblob(30),11,22);", null);
            var title = MakeCore82(ItemCore.KindEquipment, 700, 1);
            ExecBlob(connection, "INSERT INTO character_new_titlebook(character_id,category,slot_index,item_core) VALUES(10,0,1,@core);", title);

            Exec(connection, "INSERT INTO account_cargo_items(account_id,character_id,slot_index,item_template_id,item_kind,stack_count,instance_value) VALUES(1,10,4,300,'stackable',3,3);");
            Exec(connection, $"INSERT INTO account_cargo_items(account_id,character_id,slot_index,item_template_id,item_kind,stack_count,instance_value) VALUES(1,10,5,{MissingEpicPieceId},'epic-piece',2,2);");
            var newCargo = MakeCore82(ItemCore.KindConsumable, 300, 4);
            ExecBlob(connection, "INSERT INTO account_cargo_new_items(account_id,character_id,slot_index,item_core) VALUES(1,10,4,@core);", newCargo);
            ExecBlob(connection, "INSERT INTO account_cargo_new_items(account_id,character_id,slot_index,item_core) VALUES(1,10,6,@core);", missingEpic);
            Exec(connection, "INSERT INTO character_equipped_entries(character_id,slot,item_id,expire_time,raw_entry) VALUES(10,28,400,55,X'');");
            Exec(connection, "INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value,activation_id) VALUES(10,0,123,4,'00112233445566778899aabbccddeeff'),(10,1,127,8,NULL),(10,2,127,8,'bad'),(10,30,123,1,NULL),(10,3,124,1,NULL);");
            Exec(connection, "INSERT INTO character_quest_completions(character_id,quest_id,completion_value) VALUES(10,124,7),(10,126,1),(10,30000,1),(10,125,0);");
            Exec(connection, "INSERT INTO character_invisible_falgs(character_id,slot_index,flag_value) VALUES(10,125,2),(10,124,3),(10,127,0),(10,128,1);");
            Exec(connection, "INSERT INTO character_achievement_complete(character_id,sort_order,achievement_id,p1) VALUES(10,0,456,7);");
            Exec(connection, "INSERT INTO character_item_locks(character_id,sort_order,type_or_list,item_key_or_slot,state,extra_value) VALUES(10,9,0,65,1,60);");
            Exec(connection, "INSERT INTO character_creatures(character_id,sort_order,creature_key,field04,extra_json) VALUES(10,0,77,42,'{\"pet\":1}');");
        }

        private static void ProbeReadOnlyAndLeaveEmptySidecars(string path)
        {
            try { DatabaseCompatibilityGuard.Validate(path); } catch { }
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                ForeignKeys = true,
                Pooling = false
            }.ConnectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' LIMIT 1;";
                command.ExecuteScalar();
            }
            SqliteConnection.ClearAllPools();
            if (!File.Exists(path + "-shm")) File.WriteAllBytes(path + "-shm", new byte[32768]);
            if (!File.Exists(path + "-wal")) File.WriteAllBytes(path + "-wal", Array.Empty<byte>());
            if (new FileInfo(path + "-wal").Length != 0)
                throw new InvalidOperationException("自测只允许制造零长度 WAL sidecar。");
        }

        private static byte[] MakeCore82(byte kind, int itemId, int value)
        {
            var core = ItemCore.Create(kind, itemId);
            core.Value = value;
            return core.ToBytes().Take(ItemCore.LegacySize).ToArray();
        }

        private static bool HasCore(string path, int character, byte kind, int itemId, int offset, int value)
        {
            using var c = Open(path);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT item_core FROM character_inventory_items WHERE character_id=@cid;";
            cmd.Parameters.AddWithValue("@cid", character);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var b = (byte[])r[0];
                if (b.Length == ItemCore.Size && b[0] == kind && BitConverter.ToInt32(b, 1) == itemId && BitConverter.ToInt32(b, offset) == value) return true;
            }
            return false;
        }

        private static bool HasVirtual(string path, int character, int slot, int count)
        {
            using var c = Open(path);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT item_core FROM character_inventory_items WHERE character_id=@cid AND list_type=0 AND slot_index=@slot;";
            cmd.Parameters.AddWithValue("@cid", character); cmd.Parameters.AddWithValue("@slot", slot);
            var b = cmd.ExecuteScalar() as byte[];
            return b != null && BitConverter.ToInt32(b, ItemCore.ValueOffset) == count;
        }

        private static bool GuardOk(string path)
        {
            try { DatabaseCompatibilityGuard.Validate(path); return Count(path, "PRAGMA integrity_check;") == 1 && Count(path, "PRAGMA foreign_key_check;") == 0; }
            catch { return false; }
        }

        private static bool ActiveActivationOk(string path, int questId, string expected)
        {
            using var c = Open(path);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT activation_id FROM character_active_quests WHERE character_id=10 AND quest_id=@qid;";
            cmd.Parameters.AddWithValue("@qid", questId);
            var value = Convert.ToString(cmd.ExecuteScalar());
            if (string.IsNullOrEmpty(value) || !Guid.TryParseExact(value, "N", out var parsed) || parsed == Guid.Empty)
                return false;
            return expected == null || string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static int Count(string path, string sql)
        {
            using var c = Open(path); using var cmd = c.CreateCommand(); cmd.CommandText = sql;
            if (sql.StartsWith("PRAGMA integrity_check", StringComparison.OrdinalIgnoreCase)) return string.Equals(Convert.ToString(cmd.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if (sql.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase)) { using var r = cmd.ExecuteReader(); var n = 0; while (r.Read()) n++; return n; }
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private static void ExecSql(string path, string sql)
        {
            using var c = Open(path);
            Exec(c, sql);
        }

        private static bool TargetHasItem(string path, int itemId)
        {
            using var c = Open(path);
            foreach (var table in new[] { "character_inventory_items", "account_inventory_items", "character_titlebook_items" })
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT item_core FROM " + table + " WHERE item_core IS NOT NULL;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (reader[0] is byte[] bytes && bytes.Length == ItemCore.Size
                        && ItemCore.FromBytes(bytes).ItemId == itemId)
                        return true;
                }
            }
            return false;
        }

        private static bool TargetEpicBlobContains(string path, int itemId)
        {
            using var c = Open(path);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT epic_piece_counts FROM accounts;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!(reader[0] is byte[] blob)) continue;
                for (var offset = 0; offset + sizeof(int) <= blob.Length; offset += sizeof(int))
                    if (BitConverter.ToInt32(blob, offset) == itemId)
                        return true;
            }
            return false;
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static SqliteConnection Open(string path)
        {
            var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ConnectionString);
            c.Open(); return c;
        }

        private static void Exec(SqliteConnection connection, string sql)
        {
            using var cmd = connection.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
        }

        private static void ExecBlob(SqliteConnection connection, string sql, byte[] blob)
        {
            using var cmd = connection.CreateCommand(); cmd.CommandText = sql;
            if (sql.Contains("@core", StringComparison.Ordinal)) cmd.Parameters.Add("@core", SqliteType.Blob).Value = blob ?? Array.Empty<byte>();
            cmd.ExecuteNonQuery();
        }

        private static void Check(string name, bool condition, ref int failures)
        {
            Console.WriteLine((condition ? "[PASS] " : "[FAIL] ") + name);
            if (!condition) failures++;
        }
    }
}
