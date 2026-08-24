using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.GameWorld;
using GmPvfLib;
using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    /// <summary>
    /// Offline, one-way S4A12 -> S4A21 file conversion.
    ///
    /// The input path is the old database itself.  A new A21 schema-v8 file is
    /// built beside it and only after all checks pass is it atomically moved to
    /// the original path.  There is intentionally no A21 -> A12 path and no
    /// in-place table clearing.
    /// </summary>
    public sealed class A12ToA21MigrationService
    {
        public const string RequiredConfirmation = "update";
        public const int TargetSchemaVersion = 8;

        private static readonly object MigrationGate = new object();
        private readonly string _databasePath;
        private readonly string _pvfPath;
        private readonly Func<int, bool> _containsItemId;
        private readonly Func<int, byte> _resolveItemKind;
        private readonly bool _customPvfResolver;
        private readonly Func<int, bool> _containsQuestId;
        private readonly Func<int, int, int, bool> _questMatchesCharacter;

        public A12ToA21MigrationService(
            string databasePath,
            string pvfPath,
            Func<int, bool> containsItemId = null,
            Func<int, byte> resolveItemKind = null,
            Func<int, bool> containsQuestId = null,
            Func<int, int, int, bool> questMatchesCharacter = null)
        {
            _databasePath = Path.GetFullPath(databasePath ?? throw new ArgumentNullException(nameof(databasePath)));
            _pvfPath = Path.GetFullPath(pvfPath ?? throw new ArgumentNullException(nameof(pvfPath)));
            _containsItemId = containsItemId ?? DefaultContainsItemId;
            _resolveItemKind = resolveItemKind ?? DefaultResolveItemKind;
            _customPvfResolver = containsItemId != null || resolveItemKind != null;
            _containsQuestId = containsQuestId;
            _questMatchesCharacter = questMatchesCharacter;
        }

        public A12ToA21MigrationReport Preview()
        {
            try
            {
                var context = LoadContext();
                return Analyze(context, preview: true);
            }
            catch (Exception ex)
            {
                return Failure(ex);
            }
        }

        public A12ToA21MigrationReport Execute(bool userBackedUp, string confirmation)
        {
            if (!userBackedUp)
                return Failure(new InvalidOperationException("请先确认已经自行备份 A12 数据库文件。"));
            if (!string.Equals(confirmation, RequiredConfirmation, StringComparison.OrdinalIgnoreCase))
                return Failure(new InvalidOperationException("执行确认词不正确。"));

            lock (MigrationGate)
            {
                string tempPath = null;
                string rollbackPath = null;
                var stage = "load";
                try
                {
                    var context = LoadContext();
                    stage = "analyze";
                    var report = Analyze(context, preview: false);
                    if (!report.Success)
                        return report;

                    // Read-only handle with FileShare.Read prevents a server
                    // from opening the source for writes while the new file
                    // is being prepared.  Empty sidecars are harmless; an
                    // uncheckpointed WAL is never safe to discard.
                    stage = "lock";
                    using var sourceLock = new FileStream(_databasePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var lockedHash = ComputeSha256(_databasePath);
                    if (!string.Equals(lockedHash, context.SourceSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("源数据库在预检后发生变化，已拒绝替换。");

                    stage = "build";
                    tempPath = BuildTemporaryPath();
                    report.TemporaryDatabasePath = tempPath;
                    BuildA21Database(context, tempPath, report);
                    stage = "validate";
                    ValidateA21Database(tempPath);

                    var finalHash = ComputeSha256(_databasePath);
                    if (!string.Equals(finalHash, context.SourceSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("源数据库在迁移期间发生变化，已拒绝替换。");

                    // SQLite connections are all closed by BuildA21Database.
                    // Clear the provider pools as a final safety net, then
                    // re-check and remove only safe source sidecars before
                    // moving the main file.  Any cleanup failure aborts while
                    // the original database is still in its original path.
                    stage = "sidecar-cleanup";
                    SqliteConnection.ClearAllPools();
                    EnsureSidecarsReadyForReplacement(_databasePath);
                    var cleanedHash = ComputeSha256(_databasePath);
                    if (!string.Equals(cleanedHash, context.SourceSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("清理数据库 sidecar 后源文件发生变化，已拒绝替换。");
                    DeleteSourceSidecars(_databasePath);

                    // Move the old file out of the way first so a failed second
                    // move can restore it.  The rollback file is deleted after
                    // success; no automatic backup is retained.
                    stage = "replace";
                    sourceLock.Dispose();
                    rollbackPath = _databasePath + ".a12-rollback-" + Guid.NewGuid().ToString("N") + ".db";
                    stage = "replace-old";
                    File.Move(_databasePath, rollbackPath);
                    try
                    {
                        stage = "replace-new";
                        File.Move(tempPath, _databasePath);
                        tempPath = null;
                    }
                    catch
                    {
                        if (File.Exists(_databasePath))
                            File.Delete(_databasePath);
                        File.Move(rollbackPath, _databasePath);
                        rollbackPath = null;
                        throw;
                    }

                    try
                    {
                        File.Delete(rollbackPath);
                        rollbackPath = null;
                    }
                    catch (Exception cleanup)
                    {
                        report.Issues.Add(new A12ToA21MigrationIssue
                        {
                            Table = "filesystem",
                            Code = "rollback_cleanup_pending",
                            Message = "迁移已成功，但临时回滚文件未能删除：" + cleanup.Message,
                        });
                    }

                    report.ReplacementCompleted = true;
                    report.DatabasePath = _databasePath;
                    report.TemporaryDatabasePath = null;
                    report.SourceSha256 = context.SourceSha256;
                    return report;
                }
                catch (Exception ex)
                {
                    if (!string.IsNullOrEmpty(rollbackPath) && File.Exists(rollbackPath) && !File.Exists(_databasePath))
                    {
                        try { File.Move(rollbackPath, _databasePath); rollbackPath = null; } catch { }
                    }
                    if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                    var failure = Failure(new InvalidOperationException(stage + ": " + ex.GetBaseException().Message, ex));
                    failure.TemporaryDatabasePath = tempPath;
                    failure.RollbackPath = rollbackPath;
                    return failure;
                }
            }
        }

        private MigrationContext LoadContext()
        {
            ValidateInputPaths();
            ConfigurePvf();
            using var source = Open(_databasePath, SqliteOpenMode.ReadOnly);
            var tables = ReadTableNames(source);
            var columns = ReadTableColumns(source, tables);
            RequireA12Tables(tables, columns);
            var data = new SourceDatabase(_databasePath, ComputeSha256(_databasePath), ReadUserVersion(source), tables);
            // Unknown source tables are reported by name only.  Loading every
            // table here made a large production database needlessly consume
            // memory and also copied tables whose A21 semantics are unknown.
            foreach (var table in MigrationSourceTables)
                if (tables.Contains(table))
                    data.AddRows(table, ReadRows(source, table));
            var after = ComputeSha256(_databasePath);
            if (!string.Equals(data.Sha256, after, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("源数据库在只读预检期间发生变化。");
            var questCatalog = _containsQuestId != null
                ? null
                : QuestPvfCatalog.Load(_pvfPath);
            return new MigrationContext(data, _pvfPath, _databasePath, questCatalog);
        }

        private A12ToA21MigrationReport Analyze(MigrationContext context, bool preview, bool includeItemAnalysis = true)
        {
            var report = new A12ToA21MigrationReport
            {
                Success = true,
                Preview = preview,
                DatabasePath = context.DatabasePath,
                SourceSha256 = context.SourceSha256,
                SourceUserVersion = context.Source.UserVersion,
                SourceTableCount = context.Source.Tables.Count,
                PvfPath = context.PvfPath,
                SourceOnlyTables = context.Source.Tables
                    .Where(x => !A21Tables.Contains(x))
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            };

            var sourceAccounts = context.Source.Rows("accounts");
            var sourceCharacters = context.Source.Rows("characters");
            report.SourceAccounts = sourceAccounts.Count;
            report.SourceCharacters = sourceCharacters.Count;
            report.SourceCharacterItems = context.Source.Rows("character_items").Count + context.Source.Rows("character_new_items").Count;
            report.SourceAccountItems = context.Source.Rows("account_cargo_items").Count + context.Source.Rows("account_cargo_new_items").Count;
            report.SourceEquippedItems = context.Source.Rows("character_equipped_entries").Count;

            var accountIds = new HashSet<int>();
            foreach (var account in sourceAccounts)
            {
                var id = Int(account, "account_id");
                if (id <= 0 || !accountIds.Add(id))
                    Issue(report, "accounts", id, "account_invalid", "账号 ID 无效或重复，跳过。");
                else
                    report.MigratedAccounts++;
            }
            var characterIds = new HashSet<int>();
            foreach (var character in sourceCharacters)
            {
                var id = Int(character, "character_id");
                var accountId = Int(character, "account_id");
                if (id <= 0 || !characterIds.Add(id) || !accountIds.Contains(accountId))
                    Issue(report, "characters", id, "character_invalid", "角色 ID/账号外键无效，跳过。");
                else
                    report.MigratedCharacters++;
            }

            if (includeItemAnalysis)
                ValidateMainExpandStages(context.Source, characterIds);
            if (includeItemAnalysis)
                AnalyzeItems(context, report, characterIds, accountIds);
            AnalyzeQuestState(context, report, characterIds);
            report.MigratedRows += report.MigratedAccounts + report.MigratedCharacters;
            return report;
        }

        private void AnalyzeItems(MigrationContext context, A12ToA21MigrationReport report, HashSet<int> characterIds, HashSet<int> accountIds)
        {
            foreach (var row in context.Source.Rows("character_new_items"))
            {
                var characterId = Int(row, "character_id");
                if (characterId <= 0) characterId = Int(row, "owner_id");
                var list = Int(row, "list_type");
                var slot = Int(row, "slot_index");
                if (!characterIds.Contains(characterId))
                {
                    Issue(report, "character_new_items", Long(row, "item_uid"), "character_missing", "角色不存在，跳过新版物品。");
                    continue;
                }
                var core = ConvertCore(row, list, slot, out var reason);
                if (core == null)
                {
                    var itemId = SourceItemId(row);
                    if (itemId > 0 && !_containsItemId(itemId))
                    {
                        Issue(report, "character_new_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", itemId);
                    }
                    else Issue(report, "character_new_items", Long(row, "item_uid"), reason ?? "item_invalid", "新版物品无法转换为 A21 ItemCore。");
                    continue;
                }
                if (core.ItemKind == ItemCore.KindEpicPiece || (list == (int)InventoryListType.Main && slot <= 2))
                {
                    report.MigratedRows++;
                    continue;
                }
                if (!_containsItemId(core.ItemId))
                {
                    Issue(report, "character_new_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", core.ItemId);
                    continue;
                }
                if (TryResolveRange(core.ItemKind, list, slot, ResolveMainExpandStage(context.Source, characterId), out _, out _, out _)) report.MigratedRows++;
                else Issue(report, "character_new_items", Long(row, "item_uid"), "slot_invalid", "A21 原生物品类型没有可用槽位。");
            }
            foreach (var row in context.Source.Rows("character_items"))
            {
                var characterId = Int(row, "character_id");
                if (!characterIds.Contains(characterId))
                {
                    Issue(report, "character_items", Long(row, "item_uid"), "character_missing", "角色不存在，跳过物品。");
                    continue;
                }
                var core = ConvertCore(row, Int(row, "list_type"), Int(row, "slot_index"), out var reason);
                if (core == null)
                {
                    var itemId = SourceItemId(row);
                    if (itemId > 0 && !_containsItemId(itemId))
                    {
                        Issue(report, "character_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", itemId);
                    }
                    else Issue(report, "character_items", Long(row, "item_uid"), reason ?? "item_invalid", "旧物品无法转换为 A21 ItemCore。");
                    continue;
                }
                if (core.ItemKind == ItemCore.KindEpicPiece)
                {
                    report.EpicPieceRows++;
                    report.MigratedRows++;
                    continue;
                }
                if (!IsVirtual(row, core) && !_containsItemId(core.ItemId))
                {
                    Issue(report, "character_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", core.ItemId);
                    continue;
                }
                if (!TryResolveRange(core.ItemKind, Int(row, "list_type"), Int(row, "slot_index"), ResolveMainExpandStage(context.Source, characterId), out _, out _, out _))
                    Issue(report, "character_items", Long(row, "item_uid"), "slot_invalid", "A21 原生物品类型没有可用槽位。");
                else
                    report.MigratedRows++;
            }
            foreach (var row in context.Source.Rows("account_cargo_new_items"))
            {
                var accountId = Int(row, "account_id");
                var core = ConvertCore(row, (int)InventoryListType.AccountCargo, Int(row, "slot_index"), out var reason);
                if (!accountIds.Contains(accountId))
                {
                    Issue(report, "account_cargo_new_items", Long(row, "item_uid"), "account_missing", "账号不存在，跳过新版仓库物品。");
                    continue;
                }
                if (core == null)
                {
                    var itemId = SourceItemId(row);
                    if (itemId > 0 && !_containsItemId(itemId))
                        Issue(report, "account_cargo_new_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", itemId);
                    else
                        Issue(report, "account_cargo_new_items", Long(row, "item_uid"), reason ?? "item_invalid", "新版仓库物品无法转换。");
                    continue;
                }
                if (core.ItemKind == ItemCore.KindEpicPiece || _containsItemId(core.ItemId)) report.MigratedRows++;
                else
                {
                    Issue(report, "account_cargo_new_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", core.ItemId);
                }
            }
            foreach (var row in context.Source.Rows("account_cargo_items"))
            {
                var accountId = Int(row, "account_id");
                if (!accountIds.Contains(accountId))
                {
                    Issue(report, "account_cargo_items", Long(row, "item_uid"), "account_missing", "账号不存在，跳过仓库物品。");
                    continue;
                }
                var core = ConvertCore(row, (int)InventoryListType.AccountCargo, Int(row, "slot_index"), out var reason);
                if (core == null)
                {
                    var itemId = SourceItemId(row);
                    if (itemId > 0 && !_containsItemId(itemId))
                    {
                        Issue(report, "account_cargo_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", itemId);
                    }
                    else Issue(report, "account_cargo_items", Long(row, "item_uid"), reason ?? "item_invalid", "旧仓库物品无法转换为 A21 ItemCore。");
                    continue;
                }
                if (core.ItemKind == ItemCore.KindEpicPiece) { report.EpicPieceRows++; report.MigratedRows++; continue; }
                if (!_containsItemId(core.ItemId))
                {
                    Issue(report, "account_cargo_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", core.ItemId);
                }
                else
                    report.MigratedRows++;
            }
            foreach (var row in context.Source.Rows("character_equipped_entries"))
            {
                var characterId = Int(row, "character_id");
                var core = ConvertEquippedCore(row, out var reason);
                if (!characterIds.Contains(characterId))
                    Issue(report, "character_equipped_entries", Int(row, "slot"), "character_missing", "角色不存在，跳过穿戴物品。");
                else if (core == null)
                    Issue(report, "character_equipped_entries", Int(row, "slot"), reason ?? "item_invalid", "旧穿戴条目无法转换。");
                else if (Int(row, "slot") == 28)
                {
                    report.NameTagRows++;
                    report.MigratedRows++;
                }
                else if (!TryResolveEquipmentRange(core.ItemKind, Int(row, "slot"), out _, out _, out _))
                {
                    Issue(report, "character_equipped_entries", Int(row, "slot"), "slot_invalid", "A12 穿戴槽位无法映射到 A21。", core.ItemId);
                }
                else if (!_containsItemId(core.ItemId))
                {
                    Issue(report, "character_equipped_entries", Int(row, "slot"), "pvf_missing", "当前 A21 PVF 不包含该物品。", core.ItemId);
                }
                else
                    report.MigratedRows++;
            }
        }

        private void AnalyzeQuestState(MigrationContext context, A12ToA21MigrationReport report, HashSet<int> characters)
        {
            var completionKeys = new HashSet<(int Character, int Quest)>();
            foreach (var row in context.Source.Rows("character_quest_completions"))
            {
                var character = Int(row, "character_id");
                var quest = Int(row, "quest_id");
                if (!TryValidateQuestRow(context, character, quest, characters, out var code, out var message))
                {
                    IssueQuest(report, "character_quest_completions", character, quest, code, message);
                    continue;
                }
                var value = Long(row, "completion_value");
                if (value < 1 || value > byte.MaxValue)
                {
                    IssueQuest(report, "character_quest_completions", character, quest, "completion_value_invalid", "完成值必须在 1-255，跳过。");
                    continue;
                }
                if (!completionKeys.Add((character, quest)))
                {
                    IssueQuest(report, "character_quest_completions", character, quest, "completion_duplicate", "同一角色的任务完成记录重复，保留第一条。");
                    continue;
                }
                report.MigratedQuestCompletions++;
            }

            // A12 的早期数据库把完成位图展开为 slot_index/flag_value；
            // 新表优先，旧表只补充没有新表记录的任务。
            foreach (var row in context.Source.Rows("character_invisible_falgs"))
            {
                var character = Int(row, "character_id");
                var quest = Int(row, "slot_index");
                if (!TryValidateQuestRow(context, character, quest, characters, out var code, out var message))
                {
                    IssueQuest(report, "character_invisible_falgs", character, quest, code, message);
                    continue;
                }
                var value = Long(row, "flag_value");
                if (value < 1 || value > byte.MaxValue)
                {
                    IssueQuest(report, "character_invisible_falgs", character, quest, "completion_value_invalid", "完成值必须在 1-255，跳过。");
                    continue;
                }
                if (!completionKeys.Add((character, quest)))
                {
                    IssueQuest(report, "character_invisible_falgs", character, quest, "completion_duplicate", "新完成表已有相同任务，旧位图记录跳过。");
                    continue;
                }
                report.MigratedQuestCompletions++;
            }

            var activeSlots = new HashSet<(int Character, int Slot)>();
            var activeQuests = new HashSet<(int Character, int Quest)>();
            foreach (var row in context.Source.Rows("character_active_quests"))
            {
                var character = Int(row, "character_id");
                var slot = Int(row, "slot");
                var quest = Int(row, "quest_id");
                if (!TryValidateQuestRow(context, character, quest, characters, out var code, out var message))
                {
                    IssueQuest(report, "character_active_quests", character, quest, code, message);
                    continue;
                }
                if (slot < 0 || slot >= 30)
                {
                    IssueQuest(report, "character_active_quests", character, quest, "active_slot_invalid", "进行中任务槽位必须在 0-29，跳过。");
                    continue;
                }
                var trigger = Long(row, "trigger_value");
                if (trigger < 0 || trigger > uint.MaxValue)
                {
                    IssueQuest(report, "character_active_quests", character, quest, "trigger_value_invalid", "进行中任务触发值超出 A21 范围，跳过。");
                    continue;
                }
                if (completionKeys.Contains((character, quest)))
                {
                    IssueQuest(report, "character_active_quests", character, quest, "active_already_completed", "任务已有完成状态，不再写入进行中任务。");
                    continue;
                }
                if (!activeSlots.Add((character, slot)) || !activeQuests.Add((character, quest)))
                {
                    IssueQuest(report, "character_active_quests", character, quest, "active_duplicate", "同一角色的进行中任务槽位或任务号重复，跳过重复项。");
                    continue;
                }
                report.MigratedActiveQuests++;
            }
            report.MigratedRows += report.MigratedQuestCompletions + report.MigratedActiveQuests;
        }

        private bool TryValidateQuestRow(
            MigrationContext context,
            int character,
            int quest,
            HashSet<int> validCharacters,
            out string code,
            out string message)
        {
            code = null;
            message = null;
            if (!validCharacters.Contains(character))
            {
                code = "character_missing";
                message = "角色不存在，跳过任务状态。";
                return false;
            }
            if (quest < 1 || quest > 29999)
            {
                code = "quest_id_invalid";
                message = "任务 ID 必须在 1-29999，跳过。";
                return false;
            }

            var contains = _containsQuestId != null
                ? _containsQuestId(quest)
                : context.QuestCatalog != null && context.QuestCatalog.Contains(quest);
            if (!contains)
            {
                code = "quest_pvf_missing";
                message = "当前 A21 PVF 的 quest.lst 不包含该任务，跳过。";
                return false;
            }

            if (_questMatchesCharacter != null)
            {
                var characterRow = context.Source.Rows("characters")
                    .FirstOrDefault(row => Int(row, "character_id") == character);
                if (characterRow != null
                    && !_questMatchesCharacter(quest, Int(characterRow, "job"), Int(characterRow, "grow_type")))
                {
                    code = "quest_job_mismatch";
                    message = "任务不适用于该角色当前职业/转职，跳过。";
                    return false;
                }
            }
            else if (context.QuestCatalog != null)
            {
                var characterRow = context.Source.Rows("characters")
                    .FirstOrDefault(row => Int(row, "character_id") == character);
                if (characterRow != null
                    && !context.QuestCatalog.MatchesCharacter(
                        quest,
                        Int(characterRow, "job"),
                        Int(characterRow, "grow_type")))
                {
                    code = "quest_job_mismatch";
                    message = "任务不适用于该角色当前职业/转职，跳过。";
                    return false;
                }
            }
            return true;
        }

        private void BuildA21Database(MigrationContext context, string tempPath, A12ToA21MigrationReport report)
        {
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "ServerCore", "Sqlite", "item_schema.sql");
            CreateA21MigrationDatabase(tempPath, schemaPath);
            using var target = Open(tempPath, SqliteOpenMode.ReadWrite);
            using var transaction = target.BeginTransaction(deferred: false);
            var targetColumns = ReadTableColumns(target, ReadTableNames(target));
            var validAccounts = new HashSet<int>();
            var validCharacters = new HashSet<int>();

            InsertAccounts(context.Source, target, transaction, targetColumns, validAccounts, report);
            InsertCharacters(context.Source, target, transaction, targetColumns, validAccounts, validCharacters, report);
            CopyCommonTables(context, target, transaction, targetColumns, validAccounts, validCharacters, report);
            ImportVirtualCurrencies(context.Source, target, transaction, validAccounts, validCharacters, report);
            ImportCharacterItems(context.Source, target, transaction, validCharacters, report);
            ImportAccountItems(context.Source, target, transaction, validAccounts, report);
            ImportEquippedItems(context.Source, target, transaction, validCharacters, report);
            ImportAchievementsAndTitleBook(context.Source, target, transaction, validCharacters, report);
            ReconcileAccountCargoState(target, transaction);
            CheckForeignKeys(target, transaction);
            transaction.Commit();
        }

        private static void InsertAccounts(SourceDatabase source, SqliteConnection target, SqliteTransaction tx, Dictionary<string, HashSet<string>> columns, HashSet<int> valid, A12ToA21MigrationReport report)
        {
            foreach (var row in source.Rows("accounts"))
            {
                var id = Int(row, "account_id");
                if (id <= 0 || !valid.Add(id)) continue;
                if (!InsertMappedRow(target, tx, "accounts", row, columns["accounts"], id, null, null, ignoreIdentity: false))
                    throw new InvalidOperationException("账号写入失败: " + id);
            }
        }

        private static void InsertCharacters(SourceDatabase source, SqliteConnection target, SqliteTransaction tx, Dictionary<string, HashSet<string>> columns, HashSet<int> validAccounts, HashSet<int> validCharacters, A12ToA21MigrationReport report)
        {
            var entries = source.Rows("account_character_entries")
                .GroupBy(x => (Account: Int(x, "account_id"), Name: Text(x, "name")))
                .ToDictionary(x => x.Key, x => Int(x.First(), "slot_index"));
            var occupied = new Dictionary<int, HashSet<int>>();
            foreach (var row in source.Rows("characters"))
            {
                var id = Int(row, "character_id");
                var account = Int(row, "account_id");
                if (id <= 0 || account <= 0 || !validAccounts.Contains(account) || validCharacters.Contains(id)) continue;
                if (!occupied.TryGetValue(account, out var accountSlots))
                    occupied[account] = accountSlots = new HashSet<int>();
                var slot = row.ContainsKey("slot_index") ? Int(row, "slot_index") : -1;
                if (entries.TryGetValue((account, Text(row, "name")), out var entrySlot)
                    || entries.TryGetValue((0, Text(row, "name")), out entrySlot))
                    slot = entrySlot;
                if (slot < 0 || slot > 31 || accountSlots.Contains(slot))
                    slot = FindFreeCharacterSlot(accountSlots);
                if (slot < 0)
                {
                    Issue(report, "characters", id, "character_slots_full", "账号角色槽位已满，跳过角色。");
                    continue;
                }
                if (InsertMappedRow(target, tx, "characters", row, columns["characters"], id, account, slot, ignoreIdentity: false))
                {
                    validCharacters.Add(id);
                    accountSlots.Add(slot);
                    continue;
                }
                throw new InvalidOperationException("角色写入失败: " + id);
            }
        }

        private void CopyCommonTables(MigrationContext context, SqliteConnection target, SqliteTransaction tx, Dictionary<string, HashSet<string>> targetColumns, HashSet<int> accounts, HashSet<int> characters, A12ToA21MigrationReport report)
        {
            var source = context.Source;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["account_settings"] = "account_settings", ["account_premiums"] = "account_premiums", ["account_cargo_state"] = "account_cargo_state",
                ["character_collectbox_slots"] = "character_collectbox_slots", ["character_container_state"] = "character_container_state",
                ["character_dimension_flags"] = "character_dimension_flags", ["character_dimensions"] = "character_dimensions",
                ["character_dungeon_permissions"] = "character_dungeon_permissions", ["character_growth_weapon_stages"] = "character_growth_weapon_stages",
                ["character_hotkey_slots"] = "character_hotkey_slots",
                ["character_pvp_missions"] = "character_pvp_missions", ["character_subtype0_fields"] = "character_subtype0_fields",
                ["character_subtype1_fields"] = "character_subtype1_fields", ["character_init_flags"] = "character_init_flags",
                ["character_skills"] = "character_skills",
            };
            foreach (var pair in map)
            {
                if (!source.HasTable(pair.Key) || !targetColumns.ContainsKey(pair.Value)) continue;
                foreach (var row in source.Rows(pair.Key))
                {
                    var account = row.ContainsKey("account_id") ? Int(row, "account_id") : 0;
                    var character = row.ContainsKey("character_id") ? Int(row, "character_id") : 0;
                    if (account != 0 && !accounts.Contains(account) || character != 0 && !characters.Contains(character)) continue;
                    InsertMappedRow(target, tx, pair.Value, row, targetColumns[pair.Value], character == 0 ? (int?)null : character, account == 0 ? (int?)null : account, null, ignoreIdentity: true);
                }
            }
            ImportQuestState(context, target, tx, targetColumns, characters, report);
            // Item locks are written after destination slots are known.  Copying
            // old coordinates here would leave a lock pointing at a remapped
            // slot.
        }

        private void ImportQuestState(
            MigrationContext context,
            SqliteConnection target,
            SqliteTransaction tx,
            Dictionary<string, HashSet<string>> targetColumns,
            HashSet<int> characters,
            A12ToA21MigrationReport report)
        {
            if (!targetColumns.ContainsKey("character_quest_completions"))
                return;

            var completionKeys = new HashSet<(int Character, int Quest)>();
            foreach (var row in context.Source.Rows("character_quest_completions"))
            {
                var character = Int(row, "character_id");
                var quest = Int(row, "quest_id");
                if (!TryValidateQuestRow(context, character, quest, characters, out var code, out var message))
                {
                    IssueQuest(report, "character_quest_completions", character, quest, code, message);
                    continue;
                }
                var value = Long(row, "completion_value");
                if (value < 1 || value > byte.MaxValue)
                {
                    IssueQuest(report, "character_quest_completions", character, quest, "completion_value_invalid", "完成值必须在 1-255，跳过。");
                    continue;
                }
                if (!completionKeys.Add((character, quest)))
                    continue;

                InsertValues(
                    target,
                    tx,
                    "character_quest_completions",
                    new[] { "character_id", "quest_id", "completion_value" },
                    new object[] { character, quest, (byte)value });
            }

            foreach (var row in context.Source.Rows("character_invisible_falgs"))
            {
                var character = Int(row, "character_id");
                var quest = Int(row, "slot_index");
                if (!TryValidateQuestRow(context, character, quest, characters, out var code, out var message))
                {
                    IssueQuest(report, "character_invisible_falgs", character, quest, code, message);
                    continue;
                }
                var value = Long(row, "flag_value");
                if (value < 1 || value > byte.MaxValue)
                {
                    IssueQuest(report, "character_invisible_falgs", character, quest, "completion_value_invalid", "完成值必须在 1-255，跳过。");
                    continue;
                }
                if (!completionKeys.Add((character, quest)))
                    continue;

                InsertValues(
                    target,
                    tx,
                    "character_quest_completions",
                    new[] { "character_id", "quest_id", "completion_value" },
                    new object[] { character, quest, (byte)value });
            }

            if (targetColumns.ContainsKey("character_active_quests"))
            {
                var activeSlots = new HashSet<(int Character, int Slot)>();
                var activeQuests = new HashSet<(int Character, int Quest)>();
                foreach (var row in context.Source.Rows("character_active_quests"))
                {
                    var character = Int(row, "character_id");
                    var slot = Int(row, "slot");
                    var quest = Int(row, "quest_id");
                    if (!TryValidateQuestRow(context, character, quest, characters, out var code, out var message))
                    {
                        IssueQuest(report, "character_active_quests", character, quest, code, message);
                        continue;
                    }
                    if (slot < 0 || slot >= 30)
                    {
                        IssueQuest(report, "character_active_quests", character, quest, "active_slot_invalid", "进行中任务槽位必须在 0-29，跳过。");
                        continue;
                    }
                    if (completionKeys.Contains((character, quest)))
                    {
                        IssueQuest(report, "character_active_quests", character, quest, "active_already_completed", "任务已有完成状态，不再写入进行中任务。");
                        continue;
                    }
                    var trigger = Long(row, "trigger_value");
                    if (trigger < 0 || trigger > uint.MaxValue)
                    {
                        IssueQuest(report, "character_active_quests", character, quest, "trigger_value_invalid", "进行中任务触发值超出 A21 范围，跳过。");
                        continue;
                    }
                    if (!activeSlots.Add((character, slot)) || !activeQuests.Add((character, quest)))
                    {
                        IssueQuest(report, "character_active_quests", character, quest, "active_duplicate", "同一角色的进行中任务槽位或任务号重复，跳过重复项。");
                        continue;
                    }
                    var activation = Text(row, "activation_id").Trim();
                    if (!Guid.TryParseExact(activation, "N", out var parsed)
                        || parsed == Guid.Empty)
                        activation = Guid.NewGuid().ToString("N");

                    InsertValues(
                        target,
                        tx,
                        "character_active_quests",
                        new[] { "character_id", "slot", "quest_id", "trigger_value", "version", "activation_id" },
                        new object[] { character, slot, quest, trigger, 0, activation });
                }
            }

            RebuildQuestPayload(target, tx, targetColumns, characters);
        }

        private static void RebuildQuestPayload(
            SqliteConnection target,
            SqliteTransaction tx,
            Dictionary<string, HashSet<string>> targetColumns,
            HashSet<int> characters)
        {
            if (!targetColumns.ContainsKey("character_init_flags"))
                return;

            foreach (var character in characters)
            {
                long payloadLength = 0;
                using (var max = target.CreateCommand())
                {
                    max.Transaction = tx;
                    max.CommandText = "SELECT COALESCE(MAX(quest_id) + 1, 0) FROM character_quest_completions WHERE character_id=@cid;";
                    max.Parameters.AddWithValue("@cid", character);
                    payloadLength = Convert.ToInt64(max.ExecuteScalar(), CultureInfo.InvariantCulture);
                }

                InsertValues(
                    target,
                    tx,
                    "character_init_flags",
                    new[] { "character_id", "charac_invisible_falgs_payload_len" },
                    new object[] { character, payloadLength });
                using var update = target.CreateCommand();
                update.Transaction = tx;
                update.CommandText = "UPDATE character_init_flags SET charac_invisible_falgs_payload_len=@len WHERE character_id=@cid;";
                update.Parameters.AddWithValue("@len", payloadLength);
                update.Parameters.AddWithValue("@cid", character);
                update.ExecuteNonQuery();
            }
        }

        private void ImportVirtualCurrencies(SourceDatabase source, SqliteConnection target, SqliteTransaction tx, HashSet<int> accounts, HashSet<int> characters, A12ToA21MigrationReport report)
        {
            var max = new Dictionary<(int Character, int Slot), int>();
            var newVirtualSlots = new HashSet<(int Character, int Slot)>();
            foreach (var row in source.Rows("characters"))
            {
                var character = Int(row, "character_id");
                if (!characters.Contains(character)) continue;
                if (!max.ContainsKey((character, 0))) max[(character, 0)] = Int(row, "gold");
                if (!max.ContainsKey((character, 1))) max[(character, 1)] = Int(row, "coin");
            }
            foreach (var row in source.Rows("character_new_items"))
            {
                var character = Int(row, "character_id");
                if (character <= 0) character = Int(row, "owner_id");
                if (!characters.Contains(character)) continue;
                var slot = Int(row, "slot_index");
                if (character <= 0 || Int(row, "list_type") != (int)InventoryListType.Main || slot < 0 || slot > 2) continue;
                var core = ConvertCore(row, (int)InventoryListType.Main, slot, out _);
                if (core != null)
                {
                    newVirtualSlots.Add((character, slot));
                    max[(character, slot)] = core.Count;
                }
            }
            foreach (var row in source.Rows("character_items"))
            {
                var list = Int(row, "list_type");
                var slot = Int(row, "slot_index");
                if (list != (int)InventoryListType.Main || slot < 0 || slot > 2) continue;
                var key = (Int(row, "character_id"), slot);
                if (!characters.Contains(key.Item1)) continue;
                if (newVirtualSlots.Contains(key)) continue;
                var count = Math.Max(Int(row, "stack_count"), Int(row, "instance_value"));
                if (!max.TryGetValue(key, out var old) || count > old) max[key] = count;
            }
            foreach (var item in max)
            {
                if (item.Key.Character <= 0 || item.Value <= 0) continue;
                var core = ItemCore.Create(ItemCore.KindSpecialMaterial, item.Key.Slot);
                core.Count = item.Value;
                InsertCharacterCore(target, tx, item.Key.Character, InventoryListType.Main, (short)item.Key.Slot, core);
            }

            var newAccountVirtualSlots = new HashSet<(int Character, int Slot)>();
            foreach (var row in source.Rows("character_new_items"))
            {
                var character = Int(row, "character_id");
                if (character <= 0) character = Int(row, "owner_id");
                if (!characters.Contains(character)) continue;
                var slot = Int(row, "slot_index");
                if (Int(row, "list_type") != (int)InventoryListType.Main || !CurrencyService.IsAccountWarehouseSlot(slot)) continue;
                var core = ConvertCore(row, (int)InventoryListType.Main, slot, out _);
                var itemId = core?.ItemId ?? Int(row, "item_template_id");
                if (itemId <= 0)
                    itemId = CurrencyService.IsCubeFragmentSlot(slot)
                        ? CurrencyService.GetCubeFragmentItemIdFromSlot(slot)
                        : CurrencyService.GetSoulWarehouseItemIdFromSlot(slot);
                if (!CurrencyService.IsAccountWarehouseItem(itemId)) continue;
                newAccountVirtualSlots.Add((character, slot));
                var account = FindAccountForCharacter(source, character);
                if (!accounts.Contains(account)) continue;
                var count = core?.Count ?? Math.Max(Int(row, "stack_count"), Int(row, "instance_value"));
                if (CurrencyService.IsCubeFragment(itemId))
                    AddCube(target, tx, account, itemId, count);
                else if (CurrencyService.IsSoulWarehouseItem(itemId))
                    AddSoul(target, tx, account, itemId, count);
            }
            foreach (var row in source.Rows("character_items"))
            {
                var character = Int(row, "character_id");
                var slot = Int(row, "slot_index");
                if (!characters.Contains(character)
                    || Int(row, "list_type") != (int)InventoryListType.Main
                    || !CurrencyService.IsAccountWarehouseSlot(slot)) continue;
                if (newAccountVirtualSlots.Contains((character, slot))) continue;
                var itemId = Int(row, "item_template_id");
                if (itemId <= 0)
                    itemId = CurrencyService.IsCubeFragmentSlot(slot)
                        ? CurrencyService.GetCubeFragmentItemIdFromSlot(slot)
                        : CurrencyService.GetSoulWarehouseItemIdFromSlot(slot);
                if (!CurrencyService.IsAccountWarehouseItem(itemId)) continue;
                var account = FindAccountForCharacter(source, Int(row, "character_id"));
                if (!accounts.Contains(account)) continue;
                var count = Math.Max(Int(row, "stack_count"), Int(row, "instance_value"));
                if (CurrencyService.IsCubeFragment(itemId))
                    AddCube(target, tx, account, itemId, count);
                else if (CurrencyService.IsSoulWarehouseItem(itemId))
                    AddSoul(target, tx, account, itemId, count);
            }
        }

        private void ImportCharacterItems(SourceDatabase source, SqliteConnection target, SqliteTransaction tx, HashSet<int> characters, A12ToA21MigrationReport report)
        {
            var occupied = new HashSet<(int Character, int List, int Slot)>();
            var preferredNew = new HashSet<(int Character, int List, int Slot, int ItemId)>();
            var epicKeys = new HashSet<(int Character, int List, int Slot, int ItemId)>();
            foreach (var row in source.Rows("character_new_items"))
            {
                if (!string.IsNullOrEmpty(Text(row, "owner_scope")) && !string.Equals(Text(row, "owner_scope"), "character", StringComparison.OrdinalIgnoreCase)) continue;
                var character = Int(row, "character_id");
                if (character <= 0) character = Int(row, "owner_id");
                if (!characters.Contains(character)) continue;
                var sourceList = Int(row, "list_type");
                var sourceSlot = Int(row, "slot_index");
                var core = ConvertCore(row, sourceList, sourceSlot, out var reason);
                if (core == null)
                {
                    var itemId = SourceItemId(row);
                    if (itemId > 0 && !_containsItemId(itemId))
                        Issue(report, "character_new_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", itemId);
                    else
                        Issue(report, "character_new_items", Long(row, "item_uid"), reason ?? "item_invalid", "新版 82B 物品无法转换。");
                    continue;
                }
                ApplySourceLock(core, source, character, sourceList, sourceSlot);
                if (core.ItemKind == ItemCore.KindEpicPiece)
                {
                    if (epicKeys.Add((character, sourceList, sourceSlot, core.ItemId)))
                        AddEpicPiece(target, tx, FindAccountForCharacter(source, character), core.ItemId, core.Count);
                    preferredNew.Add((character, sourceList, sourceSlot, core.ItemId));
                    continue;
                }
                if (sourceList == (int)InventoryListType.Main
                    && (sourceSlot <= 2 || CurrencyService.IsAccountWarehouseSlot(sourceSlot))) continue;
                if (!_containsItemId(core.ItemId))
                {
                    Issue(report, "character_new_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", core.ItemId);
                    continue;
                }
                if (!TryResolveRange(core.ItemKind, sourceList, sourceSlot, ResolveMainExpandStage(source, character), out var list, out var start, out var end))
                {
                    Issue(report, "character_new_items", Long(row, "item_uid"), "slot_invalid", "A21 原生物品类型没有可用槽位。");
                    continue;
                }
                var slot = FindFree(occupied, character, (int)list, sourceSlot, start, end);
                if (slot < 0)
                {
                    Issue(report, "character_new_items", Long(row, "item_uid"), "target_full", "A21 背包槽位不足。");
                    continue;
                }
                occupied.Add((character, (int)list, slot));
                preferredNew.Add((character, sourceList, sourceSlot, core.ItemId));
                InsertItemWithDetails(target, tx, character, FindAccountForCharacter(source, character), list, (short)slot, core, row, source, report);
            }
            foreach (var row in source.Rows("character_items"))
            {
                var character = Int(row, "character_id");
                var sourceList = Int(row, "list_type");
                var sourceSlot = Int(row, "slot_index");
                if (!characters.Contains(character) || sourceList == 0 && sourceSlot <= 2) continue;
                var core = ConvertCore(row, sourceList, sourceSlot, out var reason);
                if (core == null) continue;
                if (core.ItemKind == ItemCore.KindEpicPiece)
                {
                    var epicKey = (character, sourceList, sourceSlot, core.ItemId);
                    if (!preferredNew.Contains(epicKey) && epicKeys.Add(epicKey))
                        AddEpicPiece(target, tx, FindAccountForCharacter(source, character), core.ItemId, core.Count);
                    continue;
                }
                if (sourceSlot >= 354) continue;
                ApplySourceLock(core, source, character, sourceList, sourceSlot);
                if (!_containsItemId(core.ItemId)) continue;
                if (!TryResolveRange(core.ItemKind, sourceList, sourceSlot, ResolveMainExpandStage(source, character), out var list, out var start, out var end)) continue;
                var requested = sourceSlot;
                var slot = FindFree(occupied, character, (int)list, requested, start, end);
                if (slot < 0) { Issue(report, "character_items", Long(row, "item_uid"), "target_full", "A21 背包槽位不足，未覆盖目标物品。"); continue; }
                occupied.Add((character, (int)list, slot));
                var itemAccount = FindAccountForCharacter(source, character);
                InsertItemWithDetails(target, tx, character, itemAccount, list, (short)slot, core, row, source, report);
            }
        }

        private void ImportAccountItems(SourceDatabase source, SqliteConnection target, SqliteTransaction tx, HashSet<int> accounts, A12ToA21MigrationReport report)
        {
            var occupied = new HashSet<(int Account, int Slot)>();
            var preferredNew = new HashSet<(int Account, int Slot, int ItemId)>();
            var epicKeys = new HashSet<(int Account, int Slot, int ItemId)>();
            foreach (var row in source.Rows("account_cargo_new_items"))
            {
                var account = Int(row, "account_id");
                if (!accounts.Contains(account)) continue;
                var sourceSlot = Int(row, "slot_index");
                var core = ConvertCore(row, (int)InventoryListType.AccountCargo, sourceSlot, out var reason);
                if (core == null)
                {
                    var itemId = SourceItemId(row);
                    if (itemId > 0 && !_containsItemId(itemId))
                        Issue(report, "account_cargo_new_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", itemId);
                    else
                        Issue(report, "account_cargo_new_items", Long(row, "item_uid"), reason ?? "item_invalid", "新版账号仓库物品无法转换。");
                    continue;
                }
                if (core.ItemKind == ItemCore.KindEpicPiece)
                {
                    if (epicKeys.Add((account, sourceSlot, core.ItemId)))
                        AddEpicPiece(target, tx, account, core.ItemId, core.Count);
                    preferredNew.Add((account, sourceSlot, core.ItemId));
                    continue;
                }
                if (!_containsItemId(core.ItemId))
                {
                    Issue(report, "account_cargo_new_items", Long(row, "item_uid"), "pvf_missing", "当前 A21 PVF 不包含该物品。", core.ItemId);
                    continue;
                }
                var slot = FindFreeAccount(occupied, account, sourceSlot);
                if (slot < 0)
                {
                    Issue(report, "account_cargo_new_items", Long(row, "item_uid"), "target_full", "A21 账号仓库已满。");
                    continue;
                }
                occupied.Add((account, slot));
                preferredNew.Add((account, sourceSlot, core.ItemId));
                InsertAccountCoreWithDetails(target, tx, account, Int(row, "character_id"), (short)slot, core, row, source, report);
            }
            foreach (var row in source.Rows("account_cargo_items"))
            {
                var account = Int(row, "account_id");
                if (!accounts.Contains(account)) continue;
                var core = ConvertCore(row, (int)InventoryListType.AccountCargo, Int(row, "slot_index"), out _);
                if (core == null) continue;
                if (preferredNew.Contains((account, Int(row, "slot_index"), core.ItemId))) continue;
                if (core.ItemKind == ItemCore.KindEpicPiece)
                {
                    if (epicKeys.Add((account, Int(row, "slot_index"), core.ItemId)))
                        AddEpicPiece(target, tx, account, core.ItemId, core.Count);
                    continue;
                }
                if (!_containsItemId(core.ItemId)) continue;
                var requested = Int(row, "slot_index");
                var slot = FindFreeAccount(occupied, account, requested);
                if (slot < 0) { Issue(report, "account_cargo_items", Long(row, "item_uid"), "target_full", "A21 账号仓库已满。"); continue; }
                occupied.Add((account, slot));
                InsertAccountCoreWithDetails(target, tx, account, Int(row, "character_id"), (short)slot, core, row, source, report);
            }
        }

        private void ImportEquippedItems(SourceDatabase source, SqliteConnection target, SqliteTransaction tx, HashSet<int> characters, A12ToA21MigrationReport report)
        {
            var occupied = new HashSet<(int Character, int List, int Slot)>();
            foreach (var row in source.Rows("character_equipped_entries"))
            {
                var character = Int(row, "character_id");
                var sourceSlot = Int(row, "slot");
                if (!characters.Contains(character)) continue;
                var core = ConvertEquippedCore(row, out _);
                if (core == null) continue;
                if (core.EquipmentLockId == 0)
                    core.EquipmentLockId = ToByte(Int(row, "equipment_lock_id"));
                if (sourceSlot == 28)
                {
                    InsertNameTag(target, tx, character, Int(row, "item_id"), Int(row, "expire_time"));
                    continue;
                }
                if (!_containsItemId(core.ItemId)
                    || !TryResolveEquipmentRange(core.ItemKind, sourceSlot, out var requestedSlot, out var start, out var end))
                    continue;
                var destinationList = ResolveEquippedList(core.ItemKind);
                var slot = FindFreeEquipment(occupied, character, destinationList, requestedSlot, start, end);
                if (slot < 0) { Issue(report, "character_equipped_entries", sourceSlot, "target_full", "A21 穿戴槽位已占用。"); continue; }
                occupied.Add((character, (int)destinationList, slot));
                InsertItemWithDetails(target, tx, character, FindAccountForCharacter(source, character), destinationList, (short)slot, core, row, source, report);
            }
        }

        private void ImportAchievementsAndTitleBook(SourceDatabase source, SqliteConnection target, SqliteTransaction tx, HashSet<int> characters, A12ToA21MigrationReport report)
        {
            if (source.HasTable("character_achievement_complete"))
                foreach (var row in source.Rows("character_achievement_complete"))
                    if (characters.Contains(Int(row, "character_id")))
                        InsertValues(target, tx, "character_achievements", new[] { "character_id", "sort_order", "achievement_id", "p1", "p2", "p3", "p4" }, new object[] { Int(row, "character_id"), Int(row, "sort_order"), Int(row, "achievement_id"), Int(row, "p1"), Int(row, "p2"), Int(row, "p3"), Int(row, "p4") });

            var titleKeys = new HashSet<(int Character, int Category, int Slot)>();
            foreach (var row in source.Rows("character_new_titlebook"))
            {
                var character = Int(row, "character_id");
                var category = Int(row, "category");
                var slot = Int(row, "slot_index");
                var core = ConvertTitleCore(Blob(row, "item_core"), out var reason);
                if (!characters.Contains(character) || core == null)
                {
                    if (characters.Contains(character) && Blob(row, "item_core") != null)
                        Issue(report, "character_new_titlebook", slot, reason ?? "title_invalid", "新版称号簿条目无法转换。");
                    continue;
                }
                if (category < 0 || category > 4 || slot < 0 || !_containsItemId(core.ItemId))
                {
                    Issue(report, "character_new_titlebook", slot, "pvf_missing", "当前 A21 PVF 不包含称号物品。", core.ItemId);
                    continue;
                }
                InsertTitleBookItem(target, tx, character, category, slot, core, titleKeys);
            }

            var titleColumns = new[] { "general", "specific", "pvp", "despair", "event" };
            var capacities = new[] { 80, 170, 50, 100, 100 };
            foreach (var row in source.Rows("character_titlebook"))
            {
                var character = Int(row, "character_id");
                if (!characters.Contains(character)) continue;
                for (var category = 0; category < titleColumns.Length; category++)
                {
                    var blob = Blob(row, titleColumns[category]);
                    if (blob == null) continue;
                    var width = blob.Length == capacities[category] * 84 ? 84 : LegacyTitleBookCoreCodec.RecordSize;
                    for (var slot = 0; slot < capacities[category]; slot++)
                    {
                        var offset = slot * width;
                        if (offset + width > blob.Length) break;
                        var normalized = new byte[LegacyTitleBookCoreCodec.RecordSize];
                        Buffer.BlockCopy(blob, offset, normalized, 0, Math.Min(width, normalized.Length));
                        var core = LegacyTitleBookCoreCodec.DecodeRecord(normalized, 0);
                        if (core.IsEmpty || titleKeys.Contains((character, category, slot))) continue;
                        if (!_containsItemId(core.ItemId))
                        {
                            Issue(report, "character_titlebook", slot, "pvf_missing", "当前 A21 PVF 不包含称号物品。", core.ItemId);
                            continue;
                        }
                        core.ItemKind = _resolveItemKind(core.ItemId);
                        if (core.ItemKind == ItemCore.KindUnknown) continue;
                        InsertTitleBookItem(target, tx, character, category, slot, core, titleKeys);
                    }
                }
            }

            foreach (var row in source.Rows("character_achievement_chunks"))
            {
                var character = Int(row, "character_id");
                var category = Int(row, "chunk_index");
                var blob = Blob(row, "entries_blob");
                if (!characters.Contains(character) || blob == null || category < 0 || category > 4) continue;
                for (var offset = 0; offset + LegacyTitleBookCoreCodec.ListEntrySize <= blob.Length; offset += LegacyTitleBookCoreCodec.ListEntrySize)
                {
                    if (!LegacyTitleBookCoreCodec.TryDecodeListEntry(blob, offset, out var slot, out var core)
                        || core.IsEmpty || titleKeys.Contains((character, category, slot))) continue;
                    if (!_containsItemId(core.ItemId))
                    {
                        Issue(report, "character_achievement_chunks", slot, "pvf_missing", "当前 A21 PVF 不包含称号物品。", core.ItemId);
                        continue;
                    }
                    core.ItemKind = _resolveItemKind(core.ItemId);
                    if (core.ItemKind == ItemCore.KindUnknown) continue;
                    InsertTitleBookItem(target, tx, character, category, slot, core, titleKeys);
                }
            }
        }

        private ItemCore ConvertTitleCore(byte[] blob, out string reason)
        {
            reason = null;
            if (blob == null || (blob.Length != ItemCore.LegacySize && blob.Length != ItemCore.Size))
            {
                reason = "item_core_length";
                return null;
            }
            var bytes = new byte[ItemCore.Size];
            Buffer.BlockCopy(blob, 0, bytes, 0, blob.Length);
            var core = ItemCore.FromBytes(bytes);
            core.ItemKind = _resolveItemKind(core.ItemId);
            if (core.ItemKind == ItemCore.KindUnknown)
            {
                reason = "a21_kind_unknown";
                return null;
            }
            return core;
        }

        private static void InsertTitleBookItem(SqliteConnection target, SqliteTransaction tx, int character, int category, int slot, ItemCore core, HashSet<(int Character, int Category, int Slot)> keys)
        {
            if (!keys.Add((character, category, slot))) return;
            InsertValues(target, tx, "character_titlebook_items", new[] { "character_id", "category", "slot_index", "item_core" }, new object[] { character, category, slot, core.ToBytes() });
        }

        private void InsertItemWithDetails(SqliteConnection target, SqliteTransaction tx, int character, int account, InventoryListType list, short slot, ItemCore core, Dictionary<string, object> row, SourceDatabase source, A12ToA21MigrationReport report)
        {
            if (core.ItemKind == ItemCore.KindAvatar)
            {
                var sourceAvatarUid = core.AvatarUid;
                core.AvatarUid = checked((int)AllocateSequence(target, tx, "character_avatar_uid_sequence"));
                InsertCharacterCore(target, tx, character, list, slot, core);
                InsertTargetLock(target, tx, character, list, slot, core.EquipmentLockId, FindLock(source, character, core.EquipmentLockId));
                var detail = FindAvatarDetail(source, sourceAvatarUid) is { } sourceDetail
                    ? AvatarDetailFromSource(sourceDetail, core, character, account)
                    : row.ContainsKey("item_id")
                    ? AvatarDetailFromEquipped(row, core, character, account)
                    : AvatarDetailFromLegacy(row, core, character, account);
                InsertAvatarDetail(target, tx, detail);
                return;
            }
            if (core.ItemKind == ItemCore.KindCreature)
            {
                var sourceCreatureKey = core.Value;
                core.CreatureUid = checked((int)AllocateSequence(target, tx, "character_creature_uid_sequence"));
                InsertCharacterCore(target, tx, character, list, slot, core);
                InsertTargetLock(target, tx, character, list, slot, core.EquipmentLockId, FindLock(source, character, core.EquipmentLockId));
                InsertCreatureDetail(target, tx, character, core.CreatureUid, FindCreatureDetail(source, character, sourceCreatureKey), row);
                return;
            }
            InsertCharacterCore(target, tx, character, list, slot, core);
            InsertTargetLock(target, tx, character, list, slot, core.EquipmentLockId, FindLock(source, character, core.EquipmentLockId));
        }

        private void InsertAccountCoreWithDetails(SqliteConnection target, SqliteTransaction tx, int account, int character, short slot, ItemCore core, Dictionary<string, object> row, SourceDatabase source, A12ToA21MigrationReport report)
        {
            if (core.ItemKind == ItemCore.KindAvatar)
            {
                var sourceAvatarUid = core.AvatarUid;
                core.AvatarUid = checked((int)AllocateSequence(target, tx, "character_avatar_uid_sequence"));
                InsertAccountCore(target, tx, account, slot, core);
                var detail = FindAvatarDetail(source, sourceAvatarUid) is { } sourceDetail
                    ? AvatarDetailFromSource(sourceDetail, core, character, account)
                    : AvatarDetailFromLegacy(row, core, character, account);
                InsertAvatarDetail(target, tx, detail);
                return;
            }
            if (core.ItemKind == ItemCore.KindCreature)
            {
                var sourceCreatureKey = core.CreatureUid;
                core.CreatureUid = checked((int)AllocateSequence(target, tx, "character_creature_uid_sequence"));
                InsertAccountCore(target, tx, account, slot, core);
                if (character > 0)
                    InsertCreatureDetail(target, tx, character, core.CreatureUid, FindCreatureDetail(source, character, sourceCreatureKey), row);
                return;
            }
            InsertAccountCore(target, tx, account, slot, core);
        }

        private ItemCore ConvertCore(Dictionary<string, object> row, int listType, int slot, out string reason)
        {
            reason = null;
            var blob = Blob(row, "item_core");
            ItemCore core;
            if (blob != null)
            {
                if (blob.Length != ItemCore.LegacySize && blob.Length != ItemCore.Size)
                {
                    reason = "item_core_length";
                    return null;
                }
                var bytes = new byte[ItemCore.Size];
                Buffer.BlockCopy(blob, 0, bytes, 0, blob.Length);
                core = ItemCore.FromBytes(bytes);
            }
            else if (listType == (int)InventoryListType.AccountCargo && row.ContainsKey("item_template_id"))
            {
                if (!A12LegacyItemConverter.TryBuildCoreFromAccountCargoItem(
                        A12LegacyItemConverter.AccountCargoItemRow.FromDictionary(row), out core, out reason))
                    return null;
            }
            else
            {
                if (!A12LegacyItemConverter.TryBuildCoreFromCharacterItem(
                        A12LegacyItemConverter.CharacterItemRow.FromDictionary(row), out core, out reason))
                    return null;
            }

            var itemId = core.ItemId != 0 ? core.ItemId : Int(row, "item_template_id");
            if (itemId < 0)
            {
                reason = "item_id_invalid";
                return null;
            }
            core.ItemId = itemId;
            if (listType == (int)InventoryListType.Main && slot >= 0 && slot <= 2)
                core.ItemKind = ItemCore.KindSpecialMaterial;
            else
                core.ItemKind = _resolveItemKind(itemId);
            if (core.ItemKind == ItemCore.KindUnknown)
            {
                reason = "a21_kind_unknown";
                return null;
            }
            if (core.ItemKind == ItemCore.KindEpicPiece && !_containsItemId(core.ItemId))
            {
                reason = "pvf_missing";
                return null;
            }
            return core;
        }

        private ItemCore ConvertEquippedCore(Dictionary<string, object> row, out string reason)
        {
            reason = null;
            var equipped = A12LegacyItemConverter.EquippedEntryRow.FromDictionary(row);
            if (equipped.ItemTemplateId <= 0)
            {
                reason = "item_id_invalid";
                return null;
            }
            try
            {
                var fields = MakeEquipListCodec.ParseDisplayFields(equipped.RawEntry ?? Array.Empty<byte>());
                var kind = _resolveItemKind(equipped.ItemTemplateId);
                if (kind == ItemCore.KindUnknown && !A12LegacyItemConverter.TryResolveEquippedItemKind(equipped.SlotIndex, out kind))
                {
                    reason = "equipped_kind_unknown";
                    return null;
                }
                var core = A12LegacyItemConverter.BuildCoreFromEquippedEntry(equipped, kind, fields);
                core.ItemKind = kind;
                return core;
            }
            catch (Exception ex)
            {
                reason = "equipped_convert_failed:" + ex.GetType().Name;
                return null;
            }
        }

        private static AvatarDetail AvatarDetailFromLegacy(Dictionary<string, object> row, ItemCore core, int character, int account)
        {
            var source = A12LegacyItemConverter.CharacterItemRow.FromDictionary(row);
            source.OwnerId = account;
            source.CharacterId = character;
            return A12LegacyItemConverter.BuildAvatarDetail(source, core, core.AvatarUid);
        }

        private static AvatarDetail AvatarDetailFromEquipped(Dictionary<string, object> row, ItemCore core, int character, int account)
        {
            var source = A12LegacyItemConverter.EquippedEntryRow.FromDictionary(row);
            var fields = MakeEquipListCodec.ParseDisplayFields(source.RawEntry ?? Array.Empty<byte>());
            var detail = A12LegacyItemConverter.BuildAvatarDetail(source, fields, core.AvatarUid);
            detail.OwnerId = account;
            detail.CharacterId = character;
            return detail;
        }

        private static Dictionary<string, object> FindAvatarDetail(SourceDatabase source, int oldUid)
        {
            if (source == null || oldUid <= 0) return null;
            return source.Rows("character_avatar_detail")
                .FirstOrDefault(x => Int(x, "item_uid") == oldUid);
        }

        private static AvatarDetail AvatarDetailFromSource(Dictionary<string, object> row, ItemCore core, int character, int account)
        {
            var socket = Blob(row, "jewel_socket");
            return new AvatarDetail
            {
                AvatarUid = core.AvatarUid,
                OwnerId = account,
                CharacterId = character,
                ItemId = Int(row, "item_id") != 0 ? Int(row, "item_id") : core.ItemId,
                ExpireDate = Int(row, "expire_date") != 0 ? Int(row, "expire_date") : core.ExpireTime,
                ClearAvatarId = Int(row, "clear_avatar_id"),
                JewelSocket = AvatarSocketDataCodec.Normalize(socket),
                Color1 = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, Int(row, "color1"))),
                Color2 = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, Int(row, "color2"))),
                DeleteDate = Int(row, "delete_date"),
            };
        }

        private static void InsertAvatarDetail(SqliteConnection c, SqliteTransaction tx, AvatarDetail d)
        {
            InsertValues(c, tx, "character_avatar_detail", new[] { "item_uid", "owner_id", "character_id", "item_id", "expire_date", "clear_avatar_id", "jewel_socket", "color1", "color2", "delete_date" }, new object[] { d.AvatarUid, d.OwnerId, d.CharacterId, d.ItemId, d.ExpireDate, d.ClearAvatarId, AvatarSocketDataCodec.Normalize(d.JewelSocket), d.Color1, d.Color2, d.DeleteDate });
        }

        private static void InsertCreatureDetail(SqliteConnection c, SqliteTransaction tx, int character, int uid, Dictionary<string, object> row)
        {
            InsertCreatureDetail(c, tx, character, uid, null, row);
        }

        private static void InsertCreatureDetail(SqliteConnection c, SqliteTransaction tx, int character, int uid, Dictionary<string, object> source, Dictionary<string, object> itemRow)
        {
            InsertValues(c, tx, "character_creatures", new[] { "character_id", "sort_order", "creature_key", "field04", "mode_flag", "progress_value", "mode1_field0a", "mode1_field0b", "field_after_value", "creature_text", "tail_flag", "extra_json" }, new object[]
            {
                character,
                NextCreatureOrder(c, tx, character),
                uid,
                source == null ? 100 : Int(source, "field04"),
                source == null ? 0 : Int(source, "mode_flag"),
                source == null ? 0 : Int(source, "progress_value"),
                source == null ? 0 : Int(source, "mode1_field0a"),
                source == null ? 0 : Int(source, "mode1_field0b"),
                source == null ? 1 : Int(source, "field_after_value"),
                source == null ? null : Blob(source, "creature_text"),
                source == null ? 0 : Int(source, "tail_flag"),
                source == null ? "{}" : (Text(source, "extra_json") == string.Empty ? "{}" : Text(source, "extra_json"))
            });
        }

        private static Dictionary<string, object> FindCreatureDetail(SourceDatabase source, int character, int oldKey)
        {
            if (source == null || oldKey == 0) return null;
            return source.Rows("character_creatures")
                .FirstOrDefault(x => Int(x, "character_id") == character && Int(x, "creature_key") == oldKey);
        }

        private static Dictionary<string, object> FindLock(SourceDatabase source, int character, int lockId)
        {
            if (source == null || lockId <= 0) return null;
            return source.Rows("character_item_locks").FirstOrDefault(x =>
                Int(x, "character_id") == character
                && (Int(x, "equipment_lock_id") == lockId || Int(x, "sort_order") == lockId));
        }

        private static void ApplySourceLock(ItemCore core, SourceDatabase source, int character, int list, int slot)
        {
            if (core == null || core.EquipmentLockId != 0 || source == null) return;
            var row = source.Rows("character_item_locks").FirstOrDefault(x =>
                Int(x, "character_id") == character
                && (Int(x, "type_or_list") == list || Int(x, "inventory_list_type") == list)
                && (Int(x, "item_key_or_slot") == slot || Int(x, "slot") == slot));
            if (row != null)
                core.EquipmentLockId = ToByte(Int(row, "equipment_lock_id") != 0 ? Int(row, "equipment_lock_id") : Int(row, "sort_order"));
        }

        private static void InsertTargetLock(SqliteConnection c, SqliteTransaction tx, int character, InventoryListType list, short slot, byte lockId, Dictionary<string, object> source)
        {
            if (lockId == 0) return;
            InsertValues(c, tx, "character_item_locks", new[] { "character_id", "equipment_lock_id", "inventory_list_type", "slot", "state", "remaining_seconds" }, new object[]
            {
                character,
                lockId,
                (int)list,
                slot,
                source == null ? 1 : Int(source, "state"),
                source == null ? (object)DBNull.Value : (source.ContainsKey("remaining_seconds") ? Int(source, "remaining_seconds") : (object)DBNull.Value)
            });
        }

        private static InventoryListType ResolveEquippedList(byte kind)
        {
            // All character_equipped_entries are worn state in A21.  The
            // equipment list owns avatar slots 0-11 as well as equipment,
            // creature, artifact, charm and medal slots; list 1 is the
            // avatar bag only.
            return InventoryListType.Equipment;
        }

        private static int NextCreatureOrder(SqliteConnection c, SqliteTransaction tx, int character)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT COALESCE(MAX(sort_order)+1,0) FROM character_creatures WHERE character_id=@id;"; cmd.Parameters.AddWithValue("@id", character); return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static void InsertNameTag(SqliteConnection c, SqliteTransaction tx, int character, int item, int expire)
        {
            InsertValues(c, tx, "character_name_tag_state", new[] { "character_id", "item_id", "expire_time" }, new object[] { character, item, expire });
        }

        private static void InsertCharacterCore(SqliteConnection c, SqliteTransaction tx, int character, InventoryListType list, short slot, ItemCore core)
        {
            InsertValues(c, tx, "character_inventory_items", new[] { "character_id", "list_type", "slot_index", "item_core" }, new object[] { character, (int)list, slot, core.ToBytes() }, ignore: false);
        }

        private static void InsertAccountCore(SqliteConnection c, SqliteTransaction tx, int account, short slot, ItemCore core)
        {
            InsertValues(c, tx, "account_inventory_items", new[] { "account_id", "slot_index", "item_core" }, new object[] { account, slot, core.ToBytes() }, ignore: false);
        }

        private static void ReconcileAccountCargoState(SqliteConnection c, SqliteTransaction tx)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE account_cargo_state
SET item_count = (SELECT COUNT(*) FROM account_inventory_items i WHERE i.account_id = account_cargo_state.account_id);";
            cmd.ExecuteNonQuery();
        }

        private static void AddCube(SqliteConnection c, SqliteTransaction tx, int account, int item, int count)
        {
            if (count <= 0) return;
            var current = CurrencyService.LoadCubeFragments(c, tx, account)
                .FirstOrDefault(x => x.ItemId == item).Count;
            if (count > current)
                CurrencyService.AddCubeFragment(c, tx, account, item, count - current);
        }

        private static void AddSoul(SqliteConnection c, SqliteTransaction tx, int account, int item, int count)
        {
            if (count <= 0) return;
            var current = CurrencyService.LoadSoulWarehouseCounts(c, tx, account)
                .FirstOrDefault(x => x.ItemId == item).Count;
            if (count > current)
                CurrencyService.SetSoulWarehouseCount(c, tx, account, item, count);
        }

        private static void AddEpicPiece(SqliteConnection c, SqliteTransaction tx, int account, int item, int count)
        {
            if (account <= 0 || count <= 0) return;
            EpicPieceService.TryAdjust(c, tx, account, item, count, out _, out _, out _);
        }

        private static int FindAccountForCharacter(SourceDatabase source, int character)
        {
            var row = source.Rows("characters").FirstOrDefault(x => Int(x, "character_id") == character); return row == null ? 0 : Int(row, "account_id");
        }

        private static int FindFreeCharacterSlot(HashSet<int> occupied)
        {
            for (var slot = 0; slot <= 31; slot++)
                if (!occupied.Contains(slot)) return slot;
            return -1;
        }

        private static int FindFree(HashSet<(int Character, int List, int Slot)> occupied, int character, int list, int requested, int start, int end)
        {
            var compactMain = list == (int)InventoryListType.Main && start >= 9 && start <= 351;
            if (!compactMain && requested >= start && requested <= end && !occupied.Contains((character, list, requested)))
                return requested;
            // Main-bag migration is intentionally compact: source coordinates
            // are not stable across the A12 -> A21 range layout.
            for (var slot = start; slot <= end; slot++)
                if (!occupied.Contains((character, list, slot))) return slot;
            return -1;
        }
        private static int FindFreeAccount(HashSet<(int Account, int Slot)> occupied, int account, int requested)
        {
            if (requested >= A21InventorySlotPolicy.AccountCargoSlotStart
                && requested <= A21InventorySlotPolicy.AccountCargoSlotEnd
                && !occupied.Contains((account, requested)))
                return requested;
            for (var slot = A21InventorySlotPolicy.AccountCargoSlotStart;
                 slot <= A21InventorySlotPolicy.AccountCargoSlotEnd;
                 slot++)
                if (!occupied.Contains((account, slot)))
                    return slot;
            return -1;
        }
        private static int FindFreeEquipment(HashSet<(int Character, int List, int Slot)> occupied, int character, InventoryListType list, int requested, int start, int end)
        { if (requested >= start && requested <= end && !occupied.Contains((character, (int)list, requested))) return requested; for (var slot = start; slot <= end; slot++) if (!occupied.Contains((character, (int)list, slot))) return slot; return -1; }

        private static void ValidateMainExpandStages(SourceDatabase source, IEnumerable<int> characterIds)
        {
            foreach (var characterId in characterIds)
                ResolveMainExpandStage(source, characterId);
        }

        private static int ResolveMainExpandStage(SourceDatabase source, int characterId)
        {
            var row = source.Rows("character_container_state")
                .FirstOrDefault(value => Int(value, "character_id") == characterId
                    && Int(value, "list_type") == (int)InventoryListType.Main);
            var stage = row == null ? A21InventorySlotPolicy.MainExpandStageFull : Int(row, "list_param16");
            if (!IsValidMainExpandStage(stage))
                throw new InvalidOperationException($"角色 {characterId} 主背包扩展状态无效: {stage}");
            return stage;
        }

        private static bool IsValidMainExpandStage(int stage)
        {
            return A21InventorySlotPolicy.TryNormalizeMainExpandStage(stage, out _);
        }

        private static bool TryResolveRange(byte kind, int sourceList, int sourceSlot, int mainExpandStage, out InventoryListType list, out int start, out int end)
        {
            list = InventoryListType.Main; start = end = 0;
            if (sourceList == (int)InventoryListType.PersonalCargo)
            {
                list = InventoryListType.PersonalCargo;
                start = A21InventorySlotPolicy.PersonalCargoSlotStart;
                end = A21InventorySlotPolicy.PersonalCargoSlotEnd;
                return true;
            }
            if (sourceList == (int)InventoryListType.Main
                && sourceSlot >= A21InventorySlotPolicy.MainQuickSlotStart
                && sourceSlot <= A21InventorySlotPolicy.MainQuickSlotEnd)
            {
                start = end = sourceSlot;
                return true;
            }
            if (kind == ItemCore.KindSpecialMaterial && sourceList == 0 && sourceSlot <= 2) { start = end = sourceSlot; return true; }
            if (sourceList == (int)InventoryListType.GuildMedal)
            {
                if (kind == ItemCore.KindGuildMedal
                    && sourceSlot >= A21InventorySlotPolicy.GuildMedalSlotStart
                    && sourceSlot <= A21InventorySlotPolicy.GuildMedalSlotEnd)
                {
                    list = InventoryListType.GuildMedal;
                    start = A21InventorySlotPolicy.GuildMedalSlotStart;
                    end = A21InventorySlotPolicy.GuildMedalSlotEnd;
                    return true;
                }
                if (kind == ItemCore.KindGuardianGem
                    && sourceSlot >= A21InventorySlotPolicy.GuardianGemSlotStart
                    && sourceSlot <= A21InventorySlotPolicy.GuardianGemSlotEnd)
                {
                    list = InventoryListType.GuildMedal;
                    start = A21InventorySlotPolicy.GuardianGemSlotStart;
                    end = A21InventorySlotPolicy.GuardianGemSlotEnd;
                    return true;
                }
                return false;
            }
            if (!NewInventoryStore.TryGetRange(kind, out list, out var s, out var e)) return false;
            start = s;
            end = e;
            if (list == InventoryListType.Main
                && kind != ItemCore.KindAvatarEmblem
                && start >= A21InventorySlotPolicy.MainEquipmentSlotStart
                && start <= A21InventorySlotPolicy.MainExpertSlotEnd)
            {
                if (!A21InventorySlotPolicy.TryGetMainRange(kind, mainExpandStage, out var openStart, out var openEnd))
                    return false;
                start = openStart;
                end = openEnd;
            }
            return end >= start;
        }
        private static bool TryResolveEquipmentRange(byte kind, int sourceSlot, out int requestedSlot, out int start, out int end)
        {
            requestedSlot = start = end = 0;
            if (kind == ItemCore.KindAvatar && sourceSlot >= 0 && sourceSlot <= 10)
            {
                requestedSlot = sourceSlot;
                start = 0;
                end = 11;
                return true;
            }
            if (kind == ItemCore.KindEquipment)
            {
                if (sourceSlot >= 11 && sourceSlot <= 20)
                {
                    requestedSlot = sourceSlot + 1;
                    start = 12;
                    end = 21;
                    return true;
                }
                if (sourceSlot >= 21 && sourceSlot <= 23)
                {
                    requestedSlot = sourceSlot + 1;
                    start = 22;
                    end = 24;
                    return true;
                }
                if (sourceSlot == 29)
                {
                    requestedSlot = 30;
                    start = end = 30;
                    return true;
                }
            }
            if (kind == ItemCore.KindCreature && sourceSlot == 24)
            {
                requestedSlot = start = end = 25;
                return true;
            }
            if (kind == ItemCore.KindCreatureEquipment && sourceSlot >= 25 && sourceSlot <= 27)
            {
                requestedSlot = sourceSlot + 1;
                start = 26;
                end = 28;
                return true;
            }
            if (kind == ItemCore.KindGuildMedal && sourceSlot == 30)
            {
                requestedSlot = start = end = 31;
                return true;
            }
            return false;
        }

        private void ConfigurePvf()
        {
            if (_customPvfResolver)
            {
                // Self-tests may inject a deterministic resolver; still require
                // the caller to provide a real path so production requests do
                // not silently omit the PVF input.
                if (!File.Exists(_pvfPath))
                    throw new FileNotFoundException("PVF 文件不存在。", _pvfPath);
                return;
            }
            PvfArchiveAccessor.Configure(_pvfPath);
            ItemMetadataResolver.ResetForPvfChange();
            EpicPieceService.ResetForPvfChange();
            if (string.IsNullOrWhiteSpace(PvfArchiveAccessor.ReadText("stackable/stackable.lst"))) throw new InvalidOperationException("A21 PVF 缺少 stackable/stackable.lst。");
        }
        private static bool DefaultContainsItemId(int itemId) => ItemMetadataResolver.GetEquipmentEntry(itemId) != null || ItemMetadataResolver.GetStackableEntry(itemId) != null || ItemMetadataResolver.IsEpicPieceItem(itemId);
        private static byte DefaultResolveItemKind(int itemId)
        {
            if (ItemMetadataResolver.IsEpicPieceItem(itemId)) return ItemCore.KindEpicPiece;
            var metadata = ItemMetadataResolver.Resolve(itemId);
            if (metadata == null) return ItemCore.KindUnknown;
            return NewInventoryStore.TryResolveKindAndRange(
                metadata,
                null,
                out var kind,
                out _,
                out _,
                out _,
                out _)
                ? kind
                : ItemCore.KindUnknown;
        }

        private void ValidateInputPaths()
        {
            if (!File.Exists(_databasePath)) throw new FileNotFoundException("数据库文件不存在。", _databasePath);
            if (!File.Exists(_pvfPath)) throw new FileNotFoundException("PVF 文件不存在。", _pvfPath);
            EnsureSourceSidecarsReadable(_databasePath);
        }
        private static void EnsureSourceSidecarsReadable(string databasePath)
        {
            RejectNonEmptyWal(databasePath + "-wal");
            EnsureSidecarUnlocked(databasePath + "-wal", "WAL");
            EnsureSidecarUnlocked(databasePath + "-shm", "SHM");
        }

        private static void EnsureSidecarsReadyForReplacement(string databasePath)
        {
            // Re-read the WAL size after every SQLite handle is closed.  A
            // non-empty WAL may contain the only copy of committed data.
            RejectNonEmptyWal(databasePath + "-wal");
            EnsureSidecarUnlocked(databasePath + "-wal", "WAL");
            EnsureSidecarUnlocked(databasePath + "-shm", "SHM");
        }

        private static void RejectNonEmptyWal(string path)
        {
            if (!File.Exists(path)) return;
            var length = new FileInfo(path).Length;
            if (length > 0)
                throw new InvalidOperationException(
                    "数据库包含未合并的 WAL（" + length.ToString(CultureInfo.InvariantCulture) + " 字节），已拒绝迁移；请在原服务器机器停止服务并执行 checkpoint，或使用 SQLite backup 导出后再迁移。不要只复制主数据库文件。");
        }

        private static void EnsureSidecarUnlocked(string path, string label)
        {
            if (!File.Exists(path)) return;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                if (stream.Length > 0 && !OperatingSystem.IsMacOS())
                {
                    stream.Lock(0, stream.Length);
                    stream.Unlock(0, stream.Length);
                }
            }
            catch (FileNotFoundException)
            {
                // A concurrently removed sidecar is safe; the main file is
                // checked again before replacement.
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException("检测到被占用的 " + label + " 文件，请先停止原服务器并关闭数据库连接。", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException("无法确认 " + label + " 文件未被占用，已拒绝迁移；请先停止原服务器。", ex);
            }
        }

        private static void DeleteSourceSidecars(string databasePath)
        {
            DeleteSidecar(databasePath + "-wal", "WAL", requireEmpty: true);
            DeleteSidecar(databasePath + "-shm", "SHM", requireEmpty: false);
        }

        private static void DeleteSidecar(string path, string label, bool requireEmpty)
        {
            if (!File.Exists(path)) return;
            var length = new FileInfo(path).Length;
            if (requireEmpty && length != 0)
                throw new InvalidOperationException("清理前 " + label + " 文件已变为非空，已拒绝替换；请回到原服务器机器执行 checkpoint。");
            EnsureSidecarUnlocked(path, label);
            if (!File.Exists(path)) return;
            if (requireEmpty && new FileInfo(path).Length != 0)
                throw new InvalidOperationException("清理前 " + label + " 文件已变为非空，已拒绝替换；请回到原服务器机器执行 checkpoint。");
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new InvalidOperationException("无法清理 " + label + " 文件，已拒绝替换并保留原数据库。", ex);
            }
        }

        private static void RequireA12Tables(HashSet<string> tables, Dictionary<string, HashSet<string>> columns)
        {
            var missing = new List<string>();
            HasColumns(tables, columns, "accounts", new[] { "account_id", "m_id" }, missing);
            HasColumns(tables, columns, "characters", new[] { "character_id", "account_id", "name" }, missing);

            var hasLegacyItems = tables.Contains("character_items");
            var hasNewItems = tables.Contains("character_new_items");
            if (!hasLegacyItems && !hasNewItems)
                missing.Add("表 character_items 或 character_new_items");
            if (hasLegacyItems)
                HasColumns(tables, columns, "character_items", new[] { "character_id", "list_type", "slot_index", "item_template_id" }, missing);
            if (hasNewItems)
            {
                HasColumns(tables, columns, "character_new_items", new[] { "list_type", "slot_index", "item_core" }, missing);
                if (!columns["character_new_items"].Contains("character_id") && !columns["character_new_items"].Contains("owner_id"))
                    missing.Add("character_new_items.character_id 或 character_new_items.owner_id");
            }

            if (missing.Count > 0)
                throw new InvalidOperationException("此 S4A12 数据库结构版本不再支持：" + string.Join(", ", missing));

        }

        private static void HasColumns(HashSet<string> tables, Dictionary<string, HashSet<string>> columns, string table, IEnumerable<string> required, List<string> missing)
        {
            if (!tables.Contains(table))
            {
                missing.Add("表 " + table);
                return;
            }
            foreach (var column in required)
                if (!columns[table].Contains(column))
                    missing.Add(table + "." + column);
        }
        private static string BuildTemporaryPathFor(string source) => source + ".a21-migration-" + Guid.NewGuid().ToString("N") + ".tmp.db";
        private string BuildTemporaryPath() => BuildTemporaryPathFor(_databasePath);
        private static void ValidateA21Database(string path)
        {
            DatabaseCompatibilityGuard.Validate(path);
            using var c = Open(path, SqliteOpenMode.ReadOnly);
            using (var integrity = c.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(Convert.ToString(integrity.ExecuteScalar(), CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A21 integrity_check 失败。");
            }
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_key_check;";
            using var r = cmd.ExecuteReader();
            if (r.Read()) throw new InvalidOperationException("A21 外键检查失败。");
        }

        private static void CheckForeignKeys(SqliteConnection c, SqliteTransaction tx)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "PRAGMA foreign_key_check;";
            using var r = cmd.ExecuteReader();
            if (r.Read()) throw new InvalidOperationException("A21 外键检查失败。");
        }

        private static void CreateA21MigrationDatabase(string path, string schemaPath)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var connection = Open(path, SqliteOpenMode.ReadWriteCreate);
            using var command = connection.CreateCommand();
            command.CommandText = File.ReadAllText(schemaPath);
            command.ExecuteNonQuery();
            command.CommandText = "PRAGMA user_version = " + TargetSchemaVersion.ToString(CultureInfo.InvariantCulture) + ";";
            command.ExecuteNonQuery();
            command.CommandText = @"
INSERT OR REPLACE INTO schema_metadata
    (singleton_id, baseline_id, schema_version, created_at, updated_at)
VALUES
    (1, '86jp-database-v1', " + TargetSchemaVersion.ToString(CultureInfo.InvariantCulture) + @", CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);";
            command.ExecuteNonQuery();
        }

        private static SqliteConnection Open(string path, SqliteOpenMode mode) { var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, ForeignKeys = true, Pooling = false }.ConnectionString); c.Open(); return c; }
        private static HashSet<string> ReadTableNames(SqliteConnection c) { var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';"; using var r = cmd.ExecuteReader(); while (r.Read()) set.Add(r.GetString(0)); return set; }
        private static Dictionary<string, HashSet<string>> ReadTableColumns(SqliteConnection c, HashSet<string> tables) { var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase); foreach (var table in tables) { var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA table_info(\"" + table.Replace("\"", "\"\"") + "\");"; using var r = cmd.ExecuteReader(); while (r.Read()) set.Add(r.GetString(1)); result[table] = set; } return result; }
        private static List<Dictionary<string, object>> ReadRows(SqliteConnection c, string table) { var list = new List<Dictionary<string, object>>(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\";"; using var r = cmd.ExecuteReader(); while (r.Read()) { var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i); list.Add(row); } return list; }
        private static long ReadUserVersion(SqliteConnection c) { using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA user_version;"; return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture); }
        private static string ComputeSha256(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }

        // Minimal quest.lst/character.lst reader for migration. It intentionally
        // keeps only the fields needed to reject unsafe quest state; all names
        // and other quest behavior remain owned by PvfIndexService.
        private sealed class QuestPvfCatalog
        {
            private static readonly Regex LstPattern = new Regex(@"(\d+)\s+`([^`]+)`", RegexOptions.Compiled);
            private static readonly Regex JobValuePattern = new Regex(@"\[job\]\s*(?:\r?\n)?\s*(?<value>[^\r\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex TagPattern = new Regex(@"`(?<backtick>[^`]*)`|\[(?<bracket>[^\]]+)\]", RegexOptions.Compiled);
            private readonly HashSet<int> _ids;
            private readonly Dictionary<int, QuestRule> _rules;
            private readonly Dictionary<int, HashSet<string>> _jobTags;

            private QuestPvfCatalog(HashSet<int> ids, Dictionary<int, QuestRule> rules, Dictionary<int, HashSet<string>> jobTags)
            {
                _ids = ids;
                _rules = rules;
                _jobTags = jobTags;
            }

            internal static QuestPvfCatalog Load(string path)
            {
                try
                {
                    using var archive = PvfArchive.Open(path);
                    var characterLst = archive.Files.FirstOrDefault(file => string.Equals(file?.Name, "character.lst", StringComparison.OrdinalIgnoreCase));
                    var questLst = archive.Files.FirstOrDefault(file => string.Equals(file?.Name, "quest.lst", StringComparison.OrdinalIgnoreCase));
                    if (questLst == null)
                        return Empty();

                    var jobTags = new Dictionary<int, HashSet<string>>();
                    if (characterLst != null)
                    {
                        var characterRoot = NormalizeRoot(characterLst.Path);
                        foreach (Match match in LstPattern.Matches(archive.GetFileContent(characterLst) ?? string.Empty))
                        {
                            if (!int.TryParse(match.Groups[1].Value, out var jobId))
                                continue;
                            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            var relative = match.Groups[2].Value.Replace('\\', '/').TrimStart('/');
                            var fullPath = string.IsNullOrEmpty(characterRoot) ? relative : characterRoot + "/" + relative;
                            try
                            {
                                var text = archive.GetFileContent(fullPath) ?? string.Empty;
                                var value = JobValuePattern.Match(text);
                                if (value.Success)
                                    tags.UnionWith(ExtractTags(value.Groups["value"].Value));
                                var stem = Path.GetFileNameWithoutExtension(relative);
                                if (!string.IsNullOrWhiteSpace(stem))
                                    tags.Add(NormalizeTag(stem));
                            }
                            catch { }
                            jobTags[jobId] = tags;
                        }
                    }

                    var ids = new HashSet<int>();
                    var rules = new Dictionary<int, QuestRule>();
                    var questRoot = NormalizeRoot(questLst.Path);
                    foreach (Match match in LstPattern.Matches(archive.GetFileContent(questLst) ?? string.Empty))
                    {
                        if (!int.TryParse(match.Groups[1].Value, out var id) || id < 1 || id > 29999)
                            continue;
                        ids.Add(id);
                        var relative = match.Groups[2].Value.Replace('\\', '/').TrimStart('/');
                        var fullPath = string.IsNullOrEmpty(questRoot) ? relative : questRoot + "/" + relative;
                        try
                        {
                            var text = archive.GetFileContent(fullPath);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                var quest = QuestFile.Parse(text);
                                rules[id] = new QuestRule
                                {
                                    Job = NormalizeTagText(quest.Job),
                                    TargetCharacter = NormalizeTagText(quest.TargetCharacter),
                                    GrowType = quest.GrowType,
                                    JobChangeQuestValue = quest.JobChangeQuestValue,
                                };
                            }
                        }
                        catch { }
                    }
                    return new QuestPvfCatalog(ids, rules, jobTags);
                }
                catch
                {
                    return Empty();
                }
            }

            internal bool Contains(int id) => id >= 1 && id <= 29999 && _ids.Contains(id);

            internal bool MatchesCharacter(int id, int job, int grow)
            {
                if (!Contains(id) || !_rules.TryGetValue(id, out var rule))
                    return Contains(id);
                if (!MatchesJob(rule.Job, job) || !MatchesJob(rule.TargetCharacter, job))
                    return false;
                if (rule.JobChangeQuestValue == 2 || rule.JobChangeQuestValue == 3)
                {
                    var first = grow & 0xF;
                    return rule.GrowType == -1 || rule.GrowType == first;
                }
                return rule.GrowType == -1
                    || rule.JobChangeQuestValue == 1
                    || rule.JobChangeQuestValue == 10
                    || rule.JobChangeQuestValue == 20
                    || grow < 0
                    || rule.GrowType == grow;
            }

            private bool MatchesJob(string value, int job)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return true;
                var required = ExtractTags(value);
                if (required.Count == 0)
                    required.Add(NormalizeTag(value));
                if (required.Contains("all"))
                    return true;
                return _jobTags.TryGetValue(job, out var actual) && required.Any(actual.Contains);
            }

            private static QuestPvfCatalog Empty() => new QuestPvfCatalog(
                new HashSet<int>(),
                new Dictionary<int, QuestRule>(),
                new Dictionary<int, HashSet<string>>());

            private static string NormalizeRoot(string value) => (value ?? string.Empty).Replace('\\', '/').Trim('/');
            private static string NormalizeTagText(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();
            private static string NormalizeTag(string value) => (value ?? string.Empty).Trim().Trim('`', '[', ']').ToLowerInvariant().Replace("_", string.Empty).Replace(" ", string.Empty).Replace("\t", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
            private static HashSet<string> ExtractTags(string value)
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in TagPattern.Matches(value ?? string.Empty))
                {
                    var token = match.Groups["backtick"].Success ? match.Groups["backtick"].Value : match.Groups["bracket"].Value;
                    var normalized = NormalizeTag(token);
                    if (!string.IsNullOrEmpty(normalized)) result.Add(normalized);
                }
                return result;
            }

            private sealed class QuestRule
            {
                internal string Job;
                internal string TargetCharacter;
                internal int GrowType;
                internal int JobChangeQuestValue;
            }
        }

        private static bool InsertMappedRow(SqliteConnection c, SqliteTransaction tx, string table, Dictionary<string, object> source, HashSet<string> targetColumns, int? characterId, int? accountId, int? slotIndex, bool ignoreIdentity)
        {
            var cols = new List<string>();
            var vals = new List<object>();
            foreach (var pair in source)
            {
                if (!targetColumns.Contains(pair.Key)
                    || pair.Key.Equals("item_uid", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("id", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("gold", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("coin", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("account_id", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("character_id", StringComparison.OrdinalIgnoreCase)) continue;
                cols.Add(pair.Key);
                vals.Add(pair.Value ?? DBNull.Value);
            }
            if (table.Equals("accounts", StringComparison.OrdinalIgnoreCase))
            {
                cols.Insert(0, "account_id");
                vals.Insert(0, accountId ?? Int(source, "account_id"));
            }
            else if (table.Equals("characters", StringComparison.OrdinalIgnoreCase))
            {
                cols.Insert(0, "character_id");
                vals.Insert(0, characterId ?? Int(source, "character_id"));
                if (targetColumns.Contains("account_id"))
                {
                    cols.Insert(1, "account_id");
                    vals.Insert(1, accountId ?? Int(source, "account_id"));
                }
            }
            else
            {
                if (targetColumns.Contains("account_id") && accountId.HasValue)
                {
                    cols.Insert(0, "account_id");
                    vals.Insert(0, accountId.Value);
                }
                if (targetColumns.Contains("character_id") && characterId.HasValue)
                {
                    var index = cols.FindIndex(x => x.Equals("account_id", StringComparison.OrdinalIgnoreCase)) + 1;
                    cols.Insert(index, "character_id");
                    vals.Insert(index, characterId.Value);
                }
            }
            if (targetColumns.Contains("slot_index") && slotIndex.HasValue)
            {
                var index = cols.FindIndex(x => x.Equals("slot_index", StringComparison.OrdinalIgnoreCase));
                if (index >= 0) vals[index] = slotIndex.Value;
                else { cols.Add("slot_index"); vals.Add(slotIndex.Value); }
            }
            return InsertValues(c, tx, table, cols, vals, ignoreIdentity);
        }
        private static bool InsertValues(SqliteConnection c, SqliteTransaction tx, string table, IReadOnlyList<string> cols, IReadOnlyList<object> vals, bool ignore = true, bool ignoreConflicts = true)
        { if (cols.Count == 0) return false; using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = (ignore && ignoreConflicts ? "INSERT OR IGNORE" : "INSERT") + " INTO " + table + "(" + string.Join(",", cols) + ") VALUES(" + string.Join(",", cols.Select((_, i) => "@p" + i)) + ");"; for (var i = 0; i < vals.Count; i++) cmd.Parameters.AddWithValue("@p" + i, vals[i] ?? DBNull.Value); return cmd.ExecuteNonQuery() > 0; }
        private static long AllocateSequence(SqliteConnection c, SqliteTransaction tx, string table) { using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO " + table + " DEFAULT VALUES; SELECT last_insert_rowid();"; return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture); }

        private static readonly HashSet<string> A21Tables = new HashSet<string>(new[] { "accounts", "characters", "account_settings", "account_premiums", "account_cargo_state", "character_collectbox_slots", "character_container_state", "character_dimension_flags", "character_dimensions", "character_dungeon_permissions", "character_growth_weapon_stages", "character_hotkey_slots", "character_pvp_missions", "character_subtype0_fields", "character_subtype1_fields", "character_init_flags", "character_skills", "character_active_quests", "character_quest_completions", "character_item_locks", "character_achievements", "character_titlebook_items", "character_inventory_items", "account_inventory_items", "character_avatar_detail", "character_avatar_uid_sequence", "character_creature_uid_sequence", "character_creatures", "character_name_tag_state", "character_daily_challenge_entry_claims", "character_daily_challenge_progress_events", "character_item_states", "schema_metadata", "get_userinfo_template", "inventory_audit_log" }, StringComparer.OrdinalIgnoreCase);

        private static readonly string[] MigrationSourceTables =
        {
            "accounts", "characters", "account_character_entries",
            "character_items", "character_new_items",
            "account_cargo_items", "account_cargo_new_items",
            "character_equipped_entries", "character_active_quests",
            "character_quest_completions", "character_invisible_falgs",
            "character_achievement_complete", "character_achievement_chunks",
            "character_titlebook", "character_new_titlebook",
            "character_item_locks", "character_creatures",
            "character_avatar_detail", "account_cargo_state",
            "account_settings", "account_premiums",
            "character_collectbox_slots", "character_container_state",
            "character_dimension_flags", "character_dimensions",
            "character_dungeon_permissions", "character_growth_weapon_stages",
            "character_hotkey_slots",
            "character_pvp_missions", "character_subtype0_fields",
            "character_subtype1_fields", "character_init_flags", "character_skills"
        };

        private static bool IsVirtual(Dictionary<string, object> row, ItemCore core) => core.ItemKind == ItemCore.KindSpecialMaterial && Int(row, "list_type") == 0 && Int(row, "slot_index") <= 2;
        private static byte ToByte(int value) => (byte)Math.Max(0, Math.Min(byte.MaxValue, value));
        private static int Int(Dictionary<string, object> row, string name) => row.TryGetValue(name, out var v) && v != null && v != DBNull.Value ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
        private static int SourceItemId(Dictionary<string, object> row)
        {
            var id = Int(row, "item_template_id");
            if (id > 0) return id;
            var blob = Blob(row, "item_core");
            return blob != null && blob.Length >= ItemCore.ItemIdOffset + sizeof(int)
                ? BitConverter.ToInt32(blob, ItemCore.ItemIdOffset)
                : 0;
        }
        private static long Long(Dictionary<string, object> row, string name) => row.TryGetValue(name, out var v) && v != null && v != DBNull.Value ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : 0;
        private static string Text(Dictionary<string, object> row, string name) => row.TryGetValue(name, out var v) && v != null && v != DBNull.Value ? Convert.ToString(v, CultureInfo.InvariantCulture) : string.Empty;
        private static byte[] Blob(Dictionary<string, object> row, string name) => row.TryGetValue(name, out var v) && v is byte[] b ? b : null;
        private static void IssueQuest(A12ToA21MigrationReport report, string table, int character, int quest, string code, string message)
        {
            var key = table + "\0" + character.ToString(CultureInfo.InvariantCulture) + "\0"
                + quest.ToString(CultureInfo.InvariantCulture) + "\0" + code;
            if (!report.IssueKeys.Add(key)) return;
            report.SkippedRows++;
            report.Issues.Add(new A12ToA21MigrationIssue
            {
                Table = table,
                SourceId = quest,
                Code = code,
                Message = "角色 " + character.ToString(CultureInfo.InvariantCulture) + "：" + message,
            });
        }

        private static void Issue(A12ToA21MigrationReport report, string table, long sourceId, string code, string message, int itemId = 0)
        {
            var key = table + "\0" + sourceId.ToString(CultureInfo.InvariantCulture) + "\0" + code;
            if (!report.IssueKeys.Add(key)) return;
            report.SkippedRows++;
            if (string.Equals(code, "pvf_missing", StringComparison.OrdinalIgnoreCase))
                report.PvFMissingItems++;
            report.Issues.Add(new A12ToA21MigrationIssue
            {
                Table = table,
                SourceId = sourceId,
                ItemId = itemId > 0 ? itemId : (int?)null,
                Code = code,
                Message = message,
            });
        }
        private static A12ToA21MigrationReport Failure(Exception ex) => new A12ToA21MigrationReport { Success = false, Error = ex?.Message ?? "迁移失败。" };

        private sealed class MigrationContext { internal MigrationContext(SourceDatabase source, string pvf, string database, QuestPvfCatalog questCatalog) { Source = source; PvfPath = pvf; DatabasePath = database; QuestCatalog = questCatalog; } internal SourceDatabase Source { get; } internal string PvfPath { get; } internal string DatabasePath { get; } internal QuestPvfCatalog QuestCatalog { get; } internal string SourceSha256 => Source.Sha256; }
        private sealed class SourceDatabase { internal SourceDatabase(string path, string hash, long version, HashSet<string> tables) { Path = path; Sha256 = hash; UserVersion = version; Tables = tables; } internal string Path { get; } internal string Sha256 { get; } internal long UserVersion { get; } internal HashSet<string> Tables { get; } private readonly Dictionary<string, List<Dictionary<string, object>>> data = new(StringComparer.OrdinalIgnoreCase); internal bool HasTable(string t) => Tables.Contains(t); internal void AddRows(string t, List<Dictionary<string, object>> rows) => data[t] = rows; internal IReadOnlyList<Dictionary<string, object>> Rows(string t) => data.TryGetValue(t, out var rows) ? rows : Array.Empty<Dictionary<string, object>>(); }
    }

    public sealed class A12ToA21MigrationReport
    {
        public bool Success { get; internal set; }
        public bool Preview { get; internal set; }
        public bool ReplacementCompleted { get; internal set; }
        public string Error { get; internal set; }
        public string DatabasePath { get; internal set; }
        public string PvfPath { get; internal set; }
        public string SourceSha256 { get; internal set; }
        public long SourceUserVersion { get; internal set; }
        public int SourceTableCount { get; internal set; }
        public int SourceAccounts { get; internal set; }
        public int SourceCharacters { get; internal set; }
        public int SourceCharacterItems { get; internal set; }
        public int SourceAccountItems { get; internal set; }
        public int SourceEquippedItems { get; internal set; }
        public int MigratedQuestCompletions { get; internal set; }
        public int MigratedActiveQuests { get; internal set; }
        public int MigratedAccounts { get; internal set; }
        public int MigratedCharacters { get; internal set; }
        public int MigratedRows { get; internal set; }
        public int SkippedRows { get; internal set; }
        public int PvFMissingItems { get; internal set; }
        public int EpicPieceRows { get; internal set; }
        public int NameTagRows { get; internal set; }
        public string TemporaryDatabasePath { get; internal set; }
        public string RollbackPath { get; internal set; }
        public string[] SourceOnlyTables { get; internal set; } = Array.Empty<string>();
        public List<A12ToA21MigrationIssue> Issues { get; } = new List<A12ToA21MigrationIssue>();
        internal HashSet<string> IssueKeys { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    public sealed class A12ToA21MigrationIssue
    {
        public string Table { get; internal set; }
        public long SourceId { get; internal set; }
        public int? ItemId { get; internal set; }
        public string Code { get; internal set; }
        public string Message { get; internal set; }
    }
}
