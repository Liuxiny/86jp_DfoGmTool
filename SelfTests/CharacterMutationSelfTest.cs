using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DfoGmTool.ServerCore.Game.Dungeon;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.Premium;
using DfoGmTool.ServerCore.Game.ReviveCoin;
using DfoGmTool.ServerCore.Game.TitleBook;
using DfoGmTool.ServerCore.GameWorld;
using DfoGmTool.ServerCore.Infrastructure;
using DfoGmTool.Services;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.SelfTests
{
    internal static class CharacterMutationSelfTest
    {
        private const int AccountId = 926014;
        private const int CharacterId = 926014;
        private static int _failures;

        internal static int Run()
        {
            _failures = 0;
            Console.WriteLine("=== CHARACTER_MUTATIONS selftest ===");

            var tempDb = Path.Combine(Path.GetTempPath(), "dfogm-character-mutations-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var schema = Path.Combine(AppContext.BaseDirectory, "ServerCore", "Sqlite", "item_schema.sql");
                var pvf = ResolveLatestServerPvf();
                if (pvf == null)
                {
                    Check("latest server PVF exists", false);
                    return 1;
                }

                SqliteDatabaseBootstrap.CreateTestDatabase(tempDb, schema);
                SeedCharacter(tempDb);

                if (!GmConfig.TryCreate(tempDb, pvf, out var config, out var error))
                {
                    Check("GM config can load temp db and PVF", false, error);
                    return 1;
                }

                PvfArchiveAccessor.Configure(pvf);
                PvfRuntimeCache.ResetForPvfChange();
                GmService.ResetPvfStaticData();

                var pvfIndex = new PvfIndexService(pvf);
                pvfIndex.WarmInBackground();
                WaitForIndex(pvfIndex);

                var gm = new GmService(config, pvfIndex);
                CheckPvfGrantClassifications(pvfIndex);
                CheckLevelAndExperience(gm, tempDb);
                CheckInventoryLimitOverride(gm, tempDb);
                CheckGoldLimitAndWalletCap(gm);
                CheckSpTpSync(gm, tempDb);
                CheckSharedSkillPagePointValidation();
                CheckJobGrowAndSkillReset(gm, pvfIndex, tempDb);
                CheckUnlockExtraEquipmentSlots(gm, tempDb);
                CheckDungeonPermissionScope(gm, pvfIndex, tempDb);
                CheckQuestActivationIdentity(gm, pvfIndex, tempDb);
                CheckPetGrantPersistence(gm, pvfIndex, tempDb);
                CheckAvatarMailPersistence(gm, pvfIndex, tempDb);
                CheckNameTagGrantPersistence(gm, pvfIndex, tempDb);
                CheckAccountPremiumGrantPersistence(gm, tempDb, pvfIndex);
                CheckSpecialRewardMailPersistence(gm, tempDb, pvfIndex);
                CheckDeliveryModeGrantPersistence(gm, pvfIndex, tempDb);
                CheckTitleQuestSynchronization(gm, pvfIndex, tempDb);
                CheckGrantAndConfigurationSlotBounds(gm, pvfIndex, tempDb);
                CheckMailStackSplitAndIdempotency(gm, pvfIndex, tempDb);
                CheckCloneOptionCoverage(gm, pvfIndex, tempDb);
                CheckCloneCharacterSlotIsolation(gm, tempDb);
                CheckAccountBackupRestoreSlotCompatibility(gm, tempDb);
                CheckMailboxClear(gm, tempDb);
                CheckDeleteCharacterSeedFallback(gm, tempDb);

                Console.WriteLine(_failures == 0
                    ? "CharacterMutationSelfTest OK"
                    : $"CharacterMutationSelfTest FAIL: {_failures}");
                return _failures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("CharacterMutationSelfTest EXCEPTION: " + ex);
                return 1;
            }
            finally
            {
                if (_failures == 0)
                {
                    try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
                }
                else
                {
                    Console.Error.WriteLine("Preserved temp db: " + tempDb);
                }
            }
        }

        private static void CheckPvfGrantClassifications(PvfIndexService pvfIndex)
        {
            var items = pvfIndex.AllItems;
            var nameTags = items.Where(item => string.Equals(item.TypeTag, "name tag", StringComparison.OrdinalIgnoreCase)).ToArray();
            Check("PVF contains name tag items", nameTags.Length > 0);
            Check("name tag items default to configurable 30-day grants",
                nameTags.Length > 0 && nameTags.All(item => item.RequiresConfiguration && item.UsablePeriodDays == 30));

            var creatures = items.Where(item => string.Equals(item.TypeTag, "creature", StringComparison.OrdinalIgnoreCase)).ToArray();
            Check("PVF contains creature items", creatures.Length > 0);
            Check("creatures include direct-grant items", creatures.Length > 0 && creatures.Any(item => !item.RequiresConfiguration));

            var petArtifacts = items.Where(item =>
                string.Equals(item.TypeTag, "artifact red", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.TypeTag, "artifact blue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.TypeTag, "artifact green", StringComparison.OrdinalIgnoreCase)).ToArray();
            var qualityPetArtifacts = petArtifacts.Where(item => item.SupportsQuality).ToArray();
            var directPetArtifacts = petArtifacts.Where(item => !item.SupportsQuality).ToArray();
            Check("PVF contains quality pet equipment", qualityPetArtifacts.Length > 0);
            Check("quality pet equipment opens configuration",
                qualityPetArtifacts.Length > 0 && qualityPetArtifacts.All(item => item.RequiresConfiguration));
            Check("PVF contains pet equipment without quality", directPetArtifacts.Length > 0);
            Check("pet equipment without quality is direct-grant",
                directPetArtifacts.Length > 0 && directPetArtifacts.All(item => !item.RequiresConfiguration));

            var directSpecialAvatars = items.Where(item =>
                (string.Equals(item.TypeTag, "weapon avatar", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.TypeTag, "aurora avatar", StringComparison.OrdinalIgnoreCase))
                && !item.RequiresConfiguration).ToArray();
            Check("PVF contains direct-grant weapon or aurora avatars", directSpecialAvatars.Length > 0);
        }

        private static void CheckLevelAndExperience(GmService gm, string dbPath)
        {
            var result = gm.SetLevel(CharacterId, 50);
            Check("SetLevel returns success", IsSuccess(result));

            using (var conn = Open(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT level, exp FROM characters WHERE character_id=@cid;";
                cmd.Parameters.AddWithValue("@cid", CharacterId);
                using (var reader = cmd.ExecuteReader())
                {
                    Check("character row exists after SetLevel", reader.Read());
                    var level = reader.GetInt32(0);
                    var exp = (uint)reader.GetInt64(1);
                    Check("level is persisted as requested", level == 50);
                    Check("exp threshold resolves back to requested level", ExpTableProvider.ApplyLevelUps(1, exp) == 50);
                    Check("level 50 exp equals threshold 49", exp == ExpTableProvider.GetLevelThreshold(49));
                }
            }
        }

        private static void CheckSpTpSync(GmService gm, string dbPath)
        {
            var before = gm.GetSpTp(CharacterId);
            var result = gm.AdjustSpTpSynced(CharacterId, 100, 5);
            Check("AdjustSpTpSynced returns success", IsSuccess(result));
            var after = gm.GetSpTp(CharacterId);
            var bonusSp = LoadInt(dbPath, "SELECT bonus_sp FROM characters WHERE character_id=926014");
            var bonusTp = LoadInt(dbPath, "SELECT bonus_tp FROM characters WHERE character_id=926014");

            Check("bonus SP increased", bonusSp == 110, "got " + bonusSp);
            Check("bonus TP increased", bonusTp == 8, "got " + bonusTp);
            Check("derived total SP updated", GetIntProperty(after, "totalSp") == GetIntProperty(before, "totalSp") + 100);
            Check("derived remaining SP updated", GetIntProperty(after, "remainingSp") == GetIntProperty(before, "remainingSp") + 100);
            Check("derived TP updated",
                GetIntProperty(after, "totalTp") == GetIntProperty(before, "totalTp") + 5
                && GetIntProperty(after, "remainingTp") == GetIntProperty(before, "remainingTp") + 5);
        }

        private static void CheckInventoryLimitOverride(GmService gm, string dbPath)
        {
            var normalLimit = LoadInt(dbPath,
                "SELECT stat_inventory_limit FROM character_subtype1_fields WHERE character_id=926014");

            var maxed = gm.SetInventoryLimitTo999(CharacterId);
            Check("set inventory limit to 999 returns success", IsSuccess(maxed));
            Check("999 inventory limit uses client storage scale",
                LoadInt(dbPath, "SELECT stat_inventory_limit FROM character_subtype1_fields WHERE character_id=926014") == 9990000);

            var restored = gm.RestoreNormalInventoryLimit(CharacterId);
            Check("restore normal inventory limit returns success", IsSuccess(restored));
            Check("restore recalculates the normal inventory limit",
                LoadInt(dbPath, "SELECT stat_inventory_limit FROM character_subtype1_fields WHERE character_id=926014") == normalLimit);
        }

        private static void CheckGoldLimitAndWalletCap(GmService gm)
        {
            Check("gold limit test restores level 60", IsSuccess(gm.SetLevel(CharacterId, 60)));
            var before = gm.GetGoldLimitStatus(CharacterId);
            Check("gold limit reports level-60 base cap", GetIntProperty(before, "goldCarryLimit") == 400_000_000);
            Check("wallet refuses gold above current cap",
                !IsSuccess(gm.SetWalletValue(CharacterId, "gold", 400_000_001)));

            var maxed = gm.SetMaximumGoldLimit(CharacterId);
            Check("max gold limit returns success", IsSuccess(maxed));
            Check("max gold limit is 800 million", GetIntProperty(maxed, "goldCarryLimit") == 800_000_000);
            Check("max gold limit cannot be applied twice", !IsSuccess(gm.SetMaximumGoldLimit(CharacterId)));
            Check("wallet accepts exactly the upgraded cap",
                IsSuccess(gm.SetWalletValue(CharacterId, "gold", 800_000_000)));
            Check("wallet refuses value above upgraded cap",
                !IsSuccess(gm.SetWalletValue(CharacterId, "gold", 800_000_001)));
        }

        private static void CheckSharedSkillPagePointValidation()
        {
            var points = new ServerCore.Game.Skills.SkillPointState
            {
                TotalSp = 100,
                SpentSp = 40,
                SpentSpPage1 = 80,
                TotalTp = 20,
                SpentTp = 20,
                SpentTpPage1 = 10,
            };

            Check("shared SP decrease accepts both pages staying non-negative",
                GmService.ValidatePointDelta(-20, 0, points) == null);
            Check("shared SP decrease rejects second page overdraft",
                GmService.ValidatePointDelta(-21, 0, points) != null);
            Check("shared TP decrease rejects first page overdraft",
                GmService.ValidatePointDelta(0, -1, points) != null);
            Check("positive shared point adjustment remains allowed",
                GmService.ValidatePointDelta(100, 100, points) == null);
        }

        private static void CheckJobGrowAndSkillReset(
            GmService gm,
            PvfIndexService pvfIndex,
            string dbPath)
        {
            var result = gm.SetGrowTypeFixed(CharacterId, 0, 1, 1);
            Check("SetGrowTypeFixed returns success", IsSuccess(result));
            var growType = LoadInt(dbPath, "SELECT grow_type FROM characters WHERE character_id=926014");
            var oldSkills = LoadInt(dbPath, "SELECT COUNT(*) FROM character_skills WHERE character_id=926014 AND skill_id=999");
            var skill33 = LoadInt(dbPath, "SELECT COUNT(*) FROM character_skills WHERE character_id=926014 AND skill_id=33");
            var skill197 = LoadInt(dbPath, "SELECT COUNT(*) FROM character_skills WHERE character_id=926014 AND skill_id=197");
            var flag101 = LoadInt(dbPath, "SELECT COUNT(*) FROM character_invisible_falgs WHERE character_id=926014 AND slot_index=101 AND flag_value=1");
            Check("grow_type packed as first + awakening", growType == 17, "got " + growType);
            Check("old skill residue removed", oldSkills == 0, "got " + oldSkills);
            Check("grow change preserves independent PVP skill state",
                LoadInt(dbPath, "SELECT COUNT(1) FROM character_pvp_skill_state WHERE character_id=926014") == 1
                && LoadInt(dbPath, "SELECT COUNT(1) FROM character_pvp_skills WHERE character_id=926014 AND skill_id=5555 AND level=3") == 1);
            Check("awakening grant skill 33 exists", skill33 > 0, "got " + skill33);
            Check("awakening grant skill 197 exists", skill197 > 0, "got " + skill197);
            Check("default promoted quest flag set", flag101 == 1, "got " + flag101);
            Check("skill points reset to full after class change",
                GetIntProperty(gm.GetSpTp(CharacterId), "totalSp") == GetIntProperty(gm.GetSpTp(CharacterId), "remainingSp"));

            var invalid = gm.SetGrowTypeFixed(CharacterId, 0, 0, 1);
            Check("invalid awakening without first grow is rejected", !IsSuccess(invalid));

            var profession = gm.CompleteProfessionQuests(CharacterId, pvfIndex, null);
            Check("profession quest completion accepts the current validated branch",
                IsSuccess(profession)
                && GetIntProperty(profession, "first") == 1
                && GetIntProperty(profession, "second") >= 1);
            Check("profession quest completion preserves independent PVP skills",
                LoadInt(dbPath, "SELECT COUNT(1) FROM character_pvp_skills WHERE character_id=926014 AND skill_id=5555 AND level=3") == 1);
        }

        private static void CheckUnlockExtraEquipmentSlots(GmService gm, string dbPath)
        {
            gm.SetLevel(CharacterId, 70);
            var result = gm.UnlockExtraEquipmentSlots(CharacterId);
            Check("UnlockExtraEquipmentSlots returns success", IsSuccess(result));
            Check("all three special equipment slots persist as unlocked",
                LoadInt(dbPath, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=926014") == 7);
            Check("character detail recognizes left and right slots within state 7",
                GetBoolProperty(gm.GetCharacter(CharacterId), "extraEquipmentSlotsUnlocked"));
            Check("equipment-slot quest status recognizes left and right bits within state 7",
                GetBoolProperty(gm.GetExtraEquipmentSlotQuestStatus(CharacterId), "unlocked"));
        }

        private static void CheckDungeonPermissionScope(
            GmService gm,
            PvfIndexService pvfIndex,
            string dbPath)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, @"INSERT OR REPLACE INTO character_dungeon_permissions
(character_id,sort_order,dungeon_id,clear_state) VALUES(926014,0,60000,2);");
                tx.Commit();
            }

            var expectedIds = pvfIndex.GetDungeonPermissionIds().Distinct().ToArray();
            var first = gm.UnlockDungeonPermissions(CharacterId, pvfIndex);
            Check("dungeon permission unlock writes account-scoped difficulty state", IsSuccess(first)
                && GetStringProperty(first, "scope") == "accountDifficulty"
                && LoadInt(dbPath, "SELECT COUNT(1) FROM account_dungeon_permissions WHERE account_id=926014 AND clear_state=4") == expectedIds.Length);
            Check("dungeon permission unlock preserves character-specific mechanism state",
                LoadInt(dbPath, @"SELECT COUNT(1) FROM character_dungeon_permissions
WHERE character_id=926014 AND dungeon_id=60000 AND clear_state=2") == 1);

            var second = gm.UnlockDungeonPermissions(CharacterId, pvfIndex);
            Check("dungeon permission unlock is idempotent",
                IsSuccess(second)
                && GetIntProperty(second, "changedCount") == 0
                && LoadInt(dbPath, "SELECT COUNT(1) FROM account_dungeon_permissions WHERE account_id=926014") == expectedIds.Length);
        }

        private static void CheckQuestActivationIdentity(
            GmService gm,
            PvfIndexService pvfIndex,
            string dbPath)
        {
            const int questId = 65000;
            var eventId = Guid.NewGuid();
            string firstActivation;
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                var activation = ServerCore.Game.Quests.QuestRepository.InsertActiveQuest(
                    conn, tx, CharacterId, 29, questId, 7);
                firstActivation = activation.ToString();
                Check("quest repository creates a valid activation identity",
                    !string.IsNullOrWhiteSpace(firstActivation) && firstActivation.Length == 32);
                Check("quest event inbox accepts the active run identity",
                    ServerCore.Game.Quests.QuestRepository.TryInsertProgressEvent(
                        conn, tx, CharacterId, activation, eventId, "gm-selftest"));
                tx.Commit();
            }

            var ready = gm.MarkQuestReady(CharacterId, questId, firstActivation);
            Check("MarkQuestReady uses activation-aware CAS", IsSuccess(ready)
                && GetStringProperty(ready, "activationId") == firstActivation
                && GetIntProperty(ready, "version") == 1
                && LoadInt(dbPath, $@"SELECT COUNT(1) FROM character_active_quests
WHERE character_id=926014 AND quest_id={questId} AND trigger_value=0
  AND version=1 AND activation_id='{firstActivation}'") == 1);

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                var stale = ServerCore.Game.Quests.QuestRepository.LoadActiveQuests(conn, tx, CharacterId)
                    .Single(quest => quest.QuestId == questId);
                Exec(conn, tx, $@"UPDATE character_active_quests SET version=version+1
WHERE character_id=926014 AND quest_id={questId};");
                Check("stale quest CAS cannot overwrite a newer version",
                    !ServerCore.Game.Quests.QuestRepository.TryUpdateTriggerValueCas(
                        conn,
                        tx,
                        CharacterId,
                        stale.QuestId,
                        stale.ActivationId,
                        stale.Version,
                        stale.TriggerValue,
                        9));
                tx.Commit();
            }

            string secondActivation;
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $"DELETE FROM character_active_quests WHERE character_id=926014 AND quest_id={questId};");
                var replacement = ServerCore.Game.Quests.QuestRepository.InsertActiveQuest(
                    conn, tx, CharacterId, 29, questId, 4);
                secondActivation = replacement.ToString();
                Check("reactivating the same quest creates a distinct run identity",
                    secondActivation != firstActivation);
                Check("same event identity is isolated between quest activations",
                    ServerCore.Game.Quests.QuestRepository.TryInsertProgressEvent(
                        conn, tx, CharacterId, replacement, eventId, "gm-selftest"));
                tx.Commit();
            }

            var staleUiReady = gm.MarkQuestReady(CharacterId, questId, firstActivation);
            Check("a stale UI activation cannot mutate a replacement quest run",
                !IsSuccess(staleUiReady)
                && GetStringProperty(staleUiReady, "error").Contains("重新接取", StringComparison.Ordinal)
                && LoadInt(dbPath, $@"SELECT trigger_value FROM character_active_quests
WHERE character_id=926014 AND quest_id={questId}") == 4);

            var listed = gm.ListQuests(CharacterId, pvfIndex);
            var listedQuest = FindListedItem(listed, "quests", "questId", questId);
            Check("quest list returns activation identity and version",
                listedQuest != null
                && GetStringProperty(listedQuest, "activationId") == secondActivation
                && GetIntProperty(listedQuest, "version") == 0);
            Check("quest inbox keeps two activation-isolated event facts",
                LoadInt(dbPath, $@"SELECT COUNT(1) FROM quest_progress_event_inbox
WHERE character_id=926014 AND event_id='{eventId:N}' AND event_kind='gm-selftest'") == 2);
            Check("quest overview and search endpoints read the latest PVF index",
                GetStringProperty(gm.MainQuestOverview(CharacterId, pvfIndex), "error") == null
                && GetStringProperty(gm.AllVisibleQuestOverview(CharacterId, pvfIndex), "error") == null
                && GetStringProperty(gm.AchievementOverview(CharacterId, pvfIndex), "error") == null
                && GetStringProperty(gm.SearchQuests(CharacterId, "", "", "", 20, pvfIndex), "error") == null);

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $"DELETE FROM character_active_quests WHERE character_id=926014 AND quest_id={questId};");
                Exec(conn, tx, $"DELETE FROM quest_progress_event_inbox WHERE character_id=926014 AND event_kind='gm-selftest';");
                tx.Commit();
            }

            object dailyResult = null;
            foreach (var meta in pvfIndex.AllQuestMeta.Values
                .Where(meta => meta != null && (meta.Grade ?? string.Empty).Contains("daily", StringComparison.OrdinalIgnoreCase))
                .OrderBy(meta => meta.Id))
            {
                dailyResult = gm.MarkVisibleDailyQuestReady(CharacterId, meta.Id, pvfIndex);
                if (IsSuccess(dailyResult))
                    break;
            }
            Check("visible daily quest activation uses the latest 30-slot repository", IsSuccess(dailyResult), GetStringProperty(dailyResult, "error"));
            if (IsSuccess(dailyResult))
            {
                var dailyQuestId = GetIntProperty(dailyResult, "questId");
                var dailyActivation = GetStringProperty(dailyResult, "activationId");
                var secondReady = gm.MarkVisibleDailyQuestReady(CharacterId, dailyQuestId, pvfIndex);
                Check("re-readying a daily quest preserves its activation identity",
                    IsSuccess(secondReady)
                    && GetStringProperty(secondReady, "activationId") == dailyActivation);
                using (var conn = Open(dbPath))
                using (var tx = conn.BeginTransaction())
                {
                    Exec(conn, tx, $"DELETE FROM character_active_quests WHERE character_id=926014 AND quest_id={dailyQuestId};");
                    tx.Commit();
                }
            }
        }

        private static void CheckPetGrantPersistence(GmService gm, PvfIndexService pvfIndex, string dbPath)
        {
            var petArtifacts = pvfIndex.AllItems.Where(item =>
                string.Equals(item.TypeTag, "artifact red", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.TypeTag, "artifact blue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.TypeTag, "artifact green", StringComparison.OrdinalIgnoreCase)).ToArray();
            var qualityArtifact = petArtifacts.First(item => item.SupportsQuality);
            var directArtifact = petArtifacts.First(item => !item.SupportsQuality);
            var creature = pvfIndex.AllItems.First(item =>
                string.Equals(item.Kind, "equipment", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.TypeTag, "creature", StringComparison.OrdinalIgnoreCase));

            var beforeMessages = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;");
            var qualityGrant = gm.GiveItem(
                CharacterId,
                qualityArtifact.Id,
                1,
                new ItemGrantOptions { QualityMode = ItemQualityMode.Random },
                pvfIndex,
                "pet-quality-926014");
            Check("quality pet equipment mail grant succeeds", IsSuccess(qualityGrant));
            Check("mail grant tells online players to reopen mailbox without reselecting",
                GetStringProperty(qualityGrant, "notification") == "mailbox_reopen_required"
                && !GetBoolProperty(qualityGrant, "requiresReselect")
                && GetStringProperty(qualityGrant, "deliveryHint").Contains("无需重新选择角色", StringComparison.Ordinal));
            Check("quality pet equipment grant does not write inventory",
                CountCoreItem(dbPath, CharacterId, qualityArtifact.Id) == 0);
            var qualityCore = LoadMailAttachmentCore(dbPath, qualityArtifact.Id);
            Check("quality pet equipment options are encoded in the mail attachment",
                qualityCore != null
                && qualityCore.ItemId == qualityArtifact.Id
                && qualityCore.ItemKind == ItemCore.KindCreatureEquipment);

            var replayGrant = gm.GiveItem(
                CharacterId,
                qualityArtifact.Id,
                1,
                new ItemGrantOptions { QualityMode = ItemQualityMode.Random },
                pvfIndex,
                "pet-quality-926014");
            Check("same mail request is durably idempotent",
                IsSuccess(replayGrant)
                && GetBoolProperty(replayGrant, "replayed")
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == beforeMessages + 1);
            var conflictingReplay = gm.GiveItem(
                CharacterId,
                qualityArtifact.Id,
                2,
                new ItemGrantOptions { QualityMode = ItemQualityMode.Random },
                pvfIndex,
                "pet-quality-926014");
            Check("same request id with different payload is rejected",
                !IsSuccess(conflictingReplay)
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == beforeMessages + 1);

            var directGrant = gm.GiveItem(CharacterId, directArtifact.Id, 1, null, pvfIndex, "pet-direct-926014");
            Check("pet equipment without quality is mailed", IsSuccess(directGrant));
            var creatureGrant = gm.GiveItem(CharacterId, creature.Id, 1, null, pvfIndex, "creature-926014");
            Check("creature is mailed without direct persistence", IsSuccess(creatureGrant));
            Check("pet and creature grants create only mailbox state",
                LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == beforeMessages + 3
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_system_mail_audit;") == beforeMessages + 3
                && CountCoreItem(dbPath, CharacterId, directArtifact.Id) == 0
                && CountCoreItem(dbPath, CharacterId, creature.Id) == 0);
        }

        private static void CheckNameTagGrantPersistence(GmService gm, PvfIndexService pvfIndex, string dbPath)
        {
            var nameTags = pvfIndex.AllItems
                .Where(item => string.Equals(item.TypeTag, "name tag", StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            Check("PVF contains enough name tags for grant persistence", nameTags.Length > 0);
            if (nameTags.Length == 0)
                return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var beforeMessages = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;");
            var firstGrant = gm.GiveItem(
                CharacterId,
                nameTags[0].Id,
                1,
                new ItemGrantOptions { ExpirationDays = 30 },
                pvfIndex,
                "name-tag-first-926014");
            Check("name tag direct grant succeeds",
                IsSuccess(firstGrant)
                && string.Equals(GetStringProperty(firstGrant, "delivery"), "direct_name_tag", StringComparison.Ordinal)
                && GetBoolProperty(firstGrant, "requiresReselect"));
            Check("name tag is not inserted into character inventory",
                CountCoreItem(dbPath, CharacterId, nameTags[0].Id) == 0);
            var nameTagExpire = LoadLong(dbPath, $"SELECT expire_time FROM character_name_tag_state WHERE character_id={CharacterId};");
            Check("name tag direct grant writes dedicated state",
                LoadInt(dbPath, $"SELECT COUNT(*) FROM character_name_tag_state WHERE character_id={CharacterId} AND item_id={nameTags[0].Id}") == 1);
            Check("name tag expiration is encoded in dedicated state",
                nameTagExpire >= now + 29L * 86400L
                && nameTagExpire <= now + 31L * 86400L);
            Check("name tag no longer writes old equipped rows",
                LoadInt(dbPath, $"SELECT COUNT(*) FROM character_equipped_entries WHERE character_id={CharacterId} AND slot=28") == 0);
            Check("name tag direct grant does not create mail",
                LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == beforeMessages);

            var renewGrant = gm.GiveItem(
                CharacterId,
                nameTags[0].Id,
                1,
                new ItemGrantOptions { ExpirationDays = 15 },
                pvfIndex,
                "name-tag-renew-926014");
            Check("same name tag direct renewal succeeds",
                IsSuccess(renewGrant)
                && string.Equals(GetStringProperty(renewGrant, "delivery"), "direct_name_tag", StringComparison.Ordinal)
                && LoadLong(dbPath, $"SELECT expire_time FROM character_name_tag_state WHERE character_id={CharacterId};") > nameTagExpire);

            if (nameTags.Length > 1)
            {
                var replaceGrant = gm.GiveItem(
                    CharacterId,
                    nameTags[1].Id,
                    1,
                    new ItemGrantOptions { ExpirationDays = 5 },
                    pvfIndex,
                    "name-tag-replace-926014");
                Check("different name tag direct replacement succeeds",
                    IsSuccess(replaceGrant)
                    && string.Equals(GetStringProperty(replaceGrant, "delivery"), "direct_name_tag", StringComparison.Ordinal)
                    && GetBoolProperty(replaceGrant, "requiresReselect"));
                Check("different name tag replaces dedicated state immediately",
                    LoadInt(dbPath, $"SELECT COUNT(*) FROM character_name_tag_state WHERE character_id={CharacterId} AND item_id={nameTags[1].Id}") == 1);
            }
            Check("name tag grants leave mailbox unchanged", LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == beforeMessages);
        }

        private static void CheckAvatarMailPersistence(GmService gm, PvfIndexService pvfIndex, string dbPath)
        {
            PvfIndexService.ItemEntry avatar = null;
            AvatarGrantOption option = null;
            var durationDays = 0;
            foreach (var candidate in pvfIndex.AllItems.Where(item =>
                         string.Equals(item.Kind, "equipment", StringComparison.OrdinalIgnoreCase)))
            {
                var metadata = ItemMetadataResolver.Resolve(candidate.Id);
                if (metadata == null
                    || !ItemMetadataResolver.IsAvatarMetadata(metadata)
                    || !ItemMetadataResolver.TryLoadEquipmentFile(candidate.Id, out var equipment)
                    || !AvatarGrantPolicy.IsUsableByJob(equipment.UsableJob, 0))
                {
                    continue;
                }

                var avatarMetadata = AvatarEquipmentMetadataReader.Read(equipment);
                var options = AvatarGrantPolicy.ResolveOptions(
                    equipment.EquipmentType,
                    equipment.Grade,
                    avatarMetadata.SelectAbilities,
                    0,
                    avatarMetadata.AbilityCaseIndex);
                var positiveDuration = AvatarDurationResolver.Resolve(candidate.Id)
                    .FirstOrDefault(value => value.DurationDays > 0)
                    ?.DurationDays ?? 0;
                if (options.Count == 0 || positiveDuration <= 0)
                    continue;

                avatar = candidate;
                option = options[0];
                durationDays = positiveDuration;
                break;
            }

            Check("PVF contains a job-compatible limited avatar for mail tests", avatar != null);
            if (avatar == null)
                return;

            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            var grant = gm.GiveItem(
                CharacterId,
                avatar.Id,
                1,
                new ItemGrantOptions
                {
                    AvatarOptionValue = option.Value,
                    ExpirationDays = durationDays,
                },
                pvfIndex,
                "avatar-mail-926014");
            var core = LoadMailAttachmentCore(dbPath, avatar.Id);
            Check("avatar mail grant succeeds without pre-creating inventory detail", IsSuccess(grant)
                && CountCoreItem(dbPath, CharacterId, avatar.Id) == 0);
            Check("avatar option and duration are encoded in ItemCore",
                core != null
                && core.ItemKind == ItemCore.KindAvatar
                && core.AvatarUid == 0
                && core.AbilityNo == option.Value
                && core.ExpireTime >= now + (durationDays - 1L) * 86400L
                && core.ExpireTime <= now + (durationDays + 1L) * 86400L);
        }

        private static void CheckAccountPremiumGrantPersistence(GmService gm, string dbPath, PvfIndexService pvfIndex)
        {
            var entry = PremiumCatalog.Load().Entries
                .OrderBy(value => value.PremiumType)
                .ThenBy(value => value.DurationDays)
                .FirstOrDefault();
            Check("PVF contains account premium contract items", entry != null);
            if (entry == null)
                return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var firstGrant = gm.GiveItem(CharacterId, entry.ItemCode, 1, null, pvfIndex, "premium-first-926014");
            Check("account premium contract direct grant succeeds",
                IsSuccess(firstGrant)
                && string.Equals(GetStringProperty(firstGrant, "delivery"), "direct_premium", StringComparison.Ordinal)
                && GetBoolProperty(firstGrant, "requiresReselect"));
            Check("account premium contract does not enter character inventory",
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_items WHERE character_id={CharacterId} AND item_template_id={entry.ItemCode}") == 0);
            var firstExpire = LoadLong(dbPath, $"SELECT end_time FROM account_premiums WHERE account_id={AccountId} AND premium_type={entry.PremiumType};");
            Check("account premium state is activated directly",
                firstExpire >= now + entry.DurationDays * 86400L - 5
                && firstExpire <= now + entry.DurationDays * 86400L + 5);

            var secondGrant = gm.GiveItem(CharacterId, entry.ItemCode, 2, null, pvfIndex, "premium-second-926014");
            Check("account premium contract second direct grant succeeds",
                IsSuccess(secondGrant)
                && string.Equals(GetStringProperty(secondGrant, "delivery"), "direct_premium", StringComparison.Ordinal));
            Check("account premium duration extends directly",
                LoadLong(dbPath, $"SELECT end_time FROM account_premiums WHERE account_id={AccountId} AND premium_type={entry.PremiumType};") >= firstExpire + entry.DurationDays * 2L * 86400L - 5);
            Check("account premium grants do not create mail",
                LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_messages WHERE sender_character_id={CharacterId} AND idempotency_key LIKE 'gm:premium-%';") == 0);
        }

        private static void CheckSpecialRewardMailPersistence(
            GmService gm,
            string dbPath,
            PvfIndexService pvfIndex)
        {
            const int cubeItemId = 3033;
            var cubeBefore = LoadLong(dbPath, $"SELECT cube_black FROM accounts WHERE account_id={AccountId};");
            var reviveBefore = LoadLong(dbPath, $@"SELECT COALESCE(MAX(stack_count), 0)
FROM character_items WHERE character_id={CharacterId} AND list_type=0 AND slot_index=1;");

            var cubeGrant = gm.GiveItem(
                CharacterId,
                cubeItemId,
                17,
                null,
                pvfIndex,
                "cube-black-mail-926014");
            Check("cube fragment mail grant succeeds", IsSuccess(cubeGrant));
            Check("cube fragment remains uncredited before mail claim",
                CurrencyService.IsCubeFragment(cubeItemId)
                && LoadLong(dbPath, $"SELECT cube_black FROM accounts WHERE account_id={AccountId};") == cubeBefore
                && LoadLong(dbPath, $"SELECT item_count FROM mailbox_attachments WHERE item_template_id={cubeItemId} ORDER BY attachment_id DESC LIMIT 1;") == 17);

            var reviveGrant = gm.GiveItem(
                CharacterId,
                ReviveCoinService.ConsumableItemId,
                3,
                null,
                pvfIndex,
                "revive-mail-926014");
            Check("revive coin mail grant succeeds", IsSuccess(reviveGrant));
            Check("revive coin wallet remains unchanged before mail claim",
                LoadLong(dbPath, $@"SELECT COALESCE(MAX(stack_count), 0)
FROM character_items WHERE character_id={CharacterId} AND list_type=0 AND slot_index=1;") == reviveBefore
                && LoadLong(dbPath, $"SELECT item_count FROM mailbox_attachments WHERE item_template_id={ReviveCoinService.ConsumableItemId} ORDER BY attachment_id DESC LIMIT 1;") == 3);
        }

        private static void CheckDeliveryModeGrantPersistence(
            GmService gm,
            PvfIndexService pvfIndex,
            string dbPath)
        {
            var stackCandidate = pvfIndex.AllItems
                .Select(item => new { Item = item, Metadata = ItemMetadataResolver.Resolve(item.Id) })
                .Where(value => value.Metadata != null
                    && value.Metadata.IsStackable
                    && value.Metadata.StackLimit >= 2
                    && value.Metadata.StackLimit <= 1000000)
                .Where(value =>
                {
                    value.Metadata.GetSlotRange(out var start, out var end);
                    return start == 65 && end == 120;
                })
                .OrderBy(value => value.Metadata.StackLimit)
                .FirstOrDefault();
            Check("PVF contains an inventory stack candidate", stackCandidate != null);
            if (stackCandidate == null)
                return;

            // The legacy/default path must remain mail-only. Exercise every
            // normalization edge before testing explicit inventory writes.
            var beforeMail = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;");
            var beforeRows = CountCoreItem(dbPath, CharacterId, stackCandidate.Item.Id);
            var modeInputs = new[] { null, "   ", "unknown", "mail" };
            for (var index = 0; index < modeInputs.Length; index++)
            {
                var modeGrant = gm.GiveItem(
                    CharacterId,
                    stackCandidate.Item.Id,
                    1,
                    null,
                    pvfIndex,
                    "delivery-mail-" + index.ToString("D2") + "-926014",
                    modeInputs[index]);
                Check("missing/unknown delivery mode stays mail (" + (modeInputs[index] ?? "missing") + ")",
                    IsSuccess(modeGrant)
                    && string.Equals(GetStringProperty(modeGrant, "delivery"), "mail", StringComparison.Ordinal)
                    && CountCoreItem(dbPath, CharacterId, stackCandidate.Item.Id) == beforeRows);
            }
            Check("all normalized mail grants create mailbox state",
                LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == beforeMail + modeInputs.Length);

            const int deliveryCharacterId = 926018;
            SeedDeliveryModeCharacter(dbPath, deliveryCharacterId);
            try
            {
                var equipmentCandidate = pvfIndex.AllItems
                    .Select(item => new { Item = item, Metadata = ItemMetadataResolver.Resolve(item.Id) })
                    .Where(value => value.Metadata != null
                        && string.Equals(value.Metadata.ItemKind, "equipment", StringComparison.Ordinal)
                        && !ItemMetadataResolver.IsAvatarMetadata(value.Metadata)
                        && !ItemMetadataResolver.IsPetCreatureMetadata(value.Metadata)
                        && !ItemMetadataResolver.IsPetArtifactMetadata(value.Metadata)
                        && !ItemMetadataResolver.RequiresManualGrantType(value.Metadata))
                    .FirstOrDefault();
                Check("PVF contains an ordinary equipment inventory candidate", equipmentCandidate != null);
                if (equipmentCandidate != null)
                {
                    var beforeEquipmentMail = LoadInt(dbPath,
                        $"SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id={deliveryCharacterId};");
                    var equipmentGrant = gm.GiveItem(
                        deliveryCharacterId,
                        equipmentCandidate.Item.Id,
                        1,
                        null,
                        pvfIndex,
                        "delivery-inventory-equipment-926018",
                        " inventory ");
                    var equipmentSlot = GetIntProperty(equipmentGrant, "slot");
                    var equipmentList = GetIntProperty(equipmentGrant, "listType");
                    Check("inventory mode writes ordinary equipment without mail",
                        IsSuccess(equipmentGrant)
                        && string.Equals(GetStringProperty(equipmentGrant, "delivery"), "inventory", StringComparison.Ordinal)
                        && GetBoolProperty(equipmentGrant, "requiresReselect")
                        && equipmentSlot >= 9
                        && CountCoreItem(dbPath, deliveryCharacterId, equipmentCandidate.Item.Id) == 1
                        && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id={deliveryCharacterId};") == beforeEquipmentMail);
                    if (equipmentSlot >= 0)
                        gm.DeleteItemAt(deliveryCharacterId, equipmentList, equipmentSlot, 0);
                }

                var stackLimit = stackCandidate.Metadata.StackLimit;
                var baseCount = stackLimit - 1;
                var baseStack = gm.GiveItem(
                    deliveryCharacterId,
                    stackCandidate.Item.Id,
                    baseCount,
                    null,
                    pvfIndex,
                    "delivery-inventory-stack-base-926018",
                    "inventory");
                Check("inventory stack grant creates a bounded base stack",
                    IsSuccess(baseStack)
                    && GetStringProperty(baseStack, "delivery") == "inventory"
                    && LoadCoreRows(dbPath, deliveryCharacterId, stackCandidate.Item.Id).Sum(row => row.Count) == baseCount);

                var splitRequestCount = stackLimit + 5;
                var splitGrant = gm.GiveItem(
                    deliveryCharacterId,
                    stackCandidate.Item.Id,
                    splitRequestCount,
                    null,
                    pvfIndex,
                    "delivery-inventory-stack-split-926018",
                    "inventory");
                var splitRows = LoadCoreRows(dbPath, deliveryCharacterId, stackCandidate.Item.Id);
                Check("inventory stack grant merges then splits at PVF limit",
                    IsSuccess(splitGrant)
                    && splitRows.Sum(row => row.Count) == baseCount + splitRequestCount
                    && splitRows.All(row => row.Count <= stackLimit)
                    && splitRows.Count >= 2
                    && GetIntProperty(splitGrant, "slot") >= 0);
                gm.RemoveItem(deliveryCharacterId, stackCandidate.Item.Id, baseCount + splitRequestCount);

                // Fill the same stack range after creating a partial existing
                // stack. TryGrant must roll back the partial merge when no new
                // slot is available, leaving the old count unchanged.
                var rollbackBase = gm.GiveItem(
                    deliveryCharacterId,
                    stackCandidate.Item.Id,
                    baseCount,
                    null,
                    pvfIndex,
                    "delivery-inventory-stack-rollback-base-926018",
                    "inventory");
                var rollbackRows = LoadCoreRows(dbPath, deliveryCharacterId, stackCandidate.Item.Id);
                var rollbackBefore = rollbackRows.FirstOrDefault();
                SeedOccupiedStackRange(
                    dbPath,
                    deliveryCharacterId,
                    65,
                    120,
                    rollbackBefore.Slot,
                    ItemCore.KindConsumable);
                var rollbackGrant = gm.GiveItem(
                    deliveryCharacterId,
                    stackCandidate.Item.Id,
                    2,
                    null,
                    pvfIndex,
                    "delivery-inventory-stack-rollback-926018",
                    "inventory");
                var rollbackAfter = LoadCoreRows(dbPath, deliveryCharacterId, stackCandidate.Item.Id)
                    .FirstOrDefault(row => row.Slot == rollbackBefore.Slot);
                Check("inventory stack no-slot failure rolls back partial merge",
                    !IsSuccess(rollbackGrant)
                    && rollbackAfter.Count == rollbackBefore.Count
                    && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id={deliveryCharacterId};") == 0);
                DeleteStackRange(dbPath, deliveryCharacterId, 65, 120);

                var avatar = FindInventoryAvatarCandidate(pvfIndex, out var avatarOption, out var avatarDays);
                Check("PVF contains a configurable avatar inventory candidate", avatar != null);
                if (avatar != null)
                {
                    var avatarGrant = gm.GiveItem(
                        deliveryCharacterId,
                        avatar.Id,
                        1,
                        new ItemGrantOptions
                        {
                            AvatarOptionValue = avatarOption,
                            ExpirationDays = avatarDays,
                        },
                        pvfIndex,
                        "delivery-inventory-avatar-926018",
                        "inventory");
                    var avatarSlot = GetIntProperty(avatarGrant, "slot");
                    var avatarCore = avatarSlot >= 0 ? LoadCore(dbPath, deliveryCharacterId, 1, avatarSlot) : null;
                    Check("inventory avatar writes core and avatar detail",
                        IsSuccess(avatarGrant)
                        && avatarCore != null
                        && avatarCore.ItemKind == ItemCore.KindAvatar
                        && LoadInt(dbPath, $"SELECT COUNT(*) FROM character_avatar_detail WHERE character_id={deliveryCharacterId} AND item_uid={avatarCore.AvatarUid};") == 1);
                    if (avatarSlot >= 0)
                        gm.DeleteItemAt(deliveryCharacterId, 1, avatarSlot, 0);
                }

                var creature = pvfIndex.AllItems.FirstOrDefault(item =>
                    string.Equals(item.Kind, "equipment", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.TypeTag, "creature", StringComparison.OrdinalIgnoreCase)
                    && ItemMetadataResolver.IsPetCreatureMetadata(ItemMetadataResolver.Resolve(item.Id)));
                Check("PVF contains a creature inventory candidate", creature != null);
                if (creature != null)
                {
                    var creatureGrant = gm.GiveItem(
                        deliveryCharacterId,
                        creature.Id,
                        1,
                        null,
                        pvfIndex,
                        "delivery-inventory-creature-926018",
                        "inventory");
                    var creatureSlot = GetIntProperty(creatureGrant, "slot");
                    var creatureCore = creatureSlot >= 0 ? LoadCore(dbPath, deliveryCharacterId, 7, creatureSlot) : null;
                    Check("inventory creature writes core and creature detail",
                        IsSuccess(creatureGrant)
                        && creatureCore != null
                        && creatureCore.ItemKind == ItemCore.KindCreature
                        && LoadInt(dbPath, $"SELECT COUNT(*) FROM character_creatures WHERE character_id={deliveryCharacterId} AND creature_key={creatureCore.CreatureUid};") == 1);
                    if (creatureSlot >= 0)
                        gm.DeleteItemAt(deliveryCharacterId, 7, creatureSlot, 0);
                }

                var cubeBefore = LoadLong(dbPath, $"SELECT cube_black FROM accounts WHERE account_id={AccountId};");
                var cubeMailCount = LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id={deliveryCharacterId};");
                var cubeMail = gm.GiveItem(
                    deliveryCharacterId,
                    3033,
                    17,
                    null,
                    pvfIndex,
                    "delivery-cube-mail-926018",
                    "mail");
                Check("cube mail mode waits for mailbox claim",
                    IsSuccess(cubeMail)
                    && GetStringProperty(cubeMail, "delivery") == "mail"
                    && LoadLong(dbPath, $"SELECT cube_black FROM accounts WHERE account_id={AccountId};") == cubeBefore
                    && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id={deliveryCharacterId};") == cubeMailCount + 1);
                var cubeDirect = gm.GiveItem(
                    deliveryCharacterId,
                    3033,
                    17,
                    null,
                    pvfIndex,
                    "delivery-cube-inventory-926018",
                    "inventory");
                Check("cube inventory mode credits shared account state immediately",
                    IsSuccess(cubeDirect)
                    && GetStringProperty(cubeDirect, "delivery") == "direct_cube"
                    && GetBoolProperty(cubeDirect, "requiresReselect")
                    && LoadLong(dbPath, $"SELECT cube_black FROM accounts WHERE account_id={AccountId};") == cubeBefore + 17
                    && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id={deliveryCharacterId};") == cubeMailCount + 1);
                gm.AdjustCubeFragment(AccountId, 3033, -17);

                var reviveBefore = LoadCore(dbPath, deliveryCharacterId, 0, ReviveCoinService.WalletSlot)?.Count ?? 0;
                var reviveMailCount = LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id={deliveryCharacterId};");
                var reviveMail = gm.GiveItem(
                    deliveryCharacterId,
                    ReviveCoinService.ConsumableItemId,
                    3,
                    null,
                    pvfIndex,
                    "delivery-revive-mail-926018",
                    "mail");
                Check("revive mail mode leaves wallet unchanged",
                    IsSuccess(reviveMail)
                    && GetStringProperty(reviveMail, "delivery") == "mail"
                    && (LoadCore(dbPath, deliveryCharacterId, 0, ReviveCoinService.WalletSlot)?.Count ?? 0) == reviveBefore);
                var reviveDirect = gm.GiveItem(
                    deliveryCharacterId,
                    ReviveCoinService.ConsumableItemId,
                    3,
                    null,
                    pvfIndex,
                    "delivery-revive-inventory-926018",
                    "inventory");
                Check("revive inventory mode credits wallet immediately",
                    IsSuccess(reviveDirect)
                    && GetStringProperty(reviveDirect, "delivery") == "direct_revive"
                    && GetIntProperty(reviveDirect, "slot") == 1
                    && (LoadCore(dbPath, deliveryCharacterId, 0, ReviveCoinService.WalletSlot)?.Count ?? 0) == reviveBefore + 3
                    && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id={deliveryCharacterId};") == reviveMailCount + 1);
                gm.SetWalletValue(deliveryCharacterId, "revive", reviveBefore);

                var reviveOneMail = gm.GiveItem(
                    deliveryCharacterId,
                    ReviveCoinService.ItemId,
                    2,
                    null,
                    pvfIndex,
                    "delivery-revive1-mail-926018",
                    "mail");
                Check("revive item id 1 mail mode remains claimable",
                    IsSuccess(reviveOneMail)
                    && GetStringProperty(reviveOneMail, "delivery") == "mail"
                    && (LoadCore(dbPath, deliveryCharacterId, 0, ReviveCoinService.WalletSlot)?.Count ?? 0) == reviveBefore);
                var reviveOneDirect = gm.GiveItem(
                    deliveryCharacterId,
                    ReviveCoinService.ItemId,
                    2,
                    null,
                    pvfIndex,
                    "delivery-revive1-inventory-926018",
                    "inventory");
                Check("revive item id 1 inventory mode writes wallet directly",
                    IsSuccess(reviveOneDirect)
                    && GetStringProperty(reviveOneDirect, "delivery") == "direct_revive"
                    && (LoadCore(dbPath, deliveryCharacterId, 0, ReviveCoinService.WalletSlot)?.Count ?? 0) == reviveBefore + 2);
                gm.SetWalletValue(deliveryCharacterId, "revive", reviveBefore);

                var nameTag = pvfIndex.AllItems.FirstOrDefault(item =>
                    string.Equals(item.TypeTag, "name tag", StringComparison.OrdinalIgnoreCase));
                var nameTagMessages = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;");
                if (nameTag != null)
                {
                    foreach (var mode in new[] { "mail", "inventory" })
                    {
                        var tagGrant = gm.GiveItem(
                            CharacterId,
                            nameTag.Id,
                            1,
                            new ItemGrantOptions { ExpirationDays = 1 },
                            pvfIndex,
                            "delivery-name-tag-" + mode + "-926014",
                            mode);
                        Check("name tag remains dedicated direct state in " + mode + " mode",
                            IsSuccess(tagGrant)
                            && GetStringProperty(tagGrant, "delivery") == "direct_name_tag"
                            && GetBoolProperty(tagGrant, "requiresReselect")
                            && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == nameTagMessages);
                    }
                }

                var premium = PremiumCatalog.Load().Entries.FirstOrDefault();
                var premiumMessages = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;");
                if (premium != null)
                {
                    foreach (var mode in new[] { "mail", "inventory" })
                    {
                        var premiumGrant = gm.GiveItem(
                            CharacterId,
                            premium.ItemCode,
                            1,
                            null,
                            pvfIndex,
                            "delivery-premium-" + mode + "-926014",
                            mode);
                        Check("premium contract remains dedicated direct state in " + mode + " mode",
                            IsSuccess(premiumGrant)
                            && GetStringProperty(premiumGrant, "delivery") == "direct_premium"
                            && GetBoolProperty(premiumGrant, "requiresReselect")
                            && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == premiumMessages);
                    }
                }

                var unsupportedSpecial = pvfIndex.AllItems
                    .Select(item => item.Id)
                    .FirstOrDefault(itemId =>
                    {
                        var metadata = ItemMetadataResolver.Resolve(itemId);
                        return metadata != null
                            && string.Equals(metadata.ItemKind, "special", StringComparison.Ordinal)
                            && !CurrencyService.IsCubeFragment(itemId)
                            && !ReviveCoinService.IsReviveCoinReward(itemId)
                            && !PremiumCatalog.Load().TryGetValue(itemId, out _, out _);
                    });
                if (unsupportedSpecial <= 0)
                    unsupportedSpecial = 2147483000;
                var beforeUnsupportedMail = LoadInt(
                    dbPath,
                    $"SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id={deliveryCharacterId};");
                var unsupportedGrant = gm.GiveItem(
                    deliveryCharacterId,
                    unsupportedSpecial,
                    1,
                    null,
                    pvfIndex,
                    "delivery-unsupported-special-926018",
                    "inventory");
                Check("unsupported special inventory grant is rejected without fallback mail",
                    !IsSuccess(unsupportedGrant)
                    && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_messages WHERE receiver_character_id={deliveryCharacterId};")
                        == beforeUnsupportedMail
                    && CountCoreItem(dbPath, deliveryCharacterId, unsupportedSpecial) == 0);
            }
            finally
            {
                CleanupDeliveryModeCharacter(dbPath, deliveryCharacterId);
            }
        }

        private static void CheckMailStackSplitAndIdempotency(
            GmService gm,
            PvfIndexService pvfIndex,
            string dbPath)
        {
            var candidate = pvfIndex.AllItems
                .Select(item => new { Item = item, Metadata = ItemMetadataResolver.Resolve(item.Id) })
                .Where(value => value.Metadata != null
                    && value.Metadata.IsStackable
                    && value.Metadata.StackLimit > 0
                    && value.Metadata.StackLimit <= 10000000)
                .OrderBy(value => value.Metadata.StackLimit)
                .FirstOrDefault();
            Check("PVF contains a bounded stackable item for split tests", candidate != null);
            if (candidate == null)
                return;

            var stackLimit = candidate.Metadata.StackLimit;
            var requestedCount = checked(stackLimit * 11);
            var requestId = "stack_split-926014";
            var beforeMessages = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;");
            var grant = gm.GiveItem(CharacterId, candidate.Item.Id, requestedCount, null, pvfIndex, requestId);
            Check("stackable grant splits at PVF stack limit",
                IsSuccess(grant)
                && GetIntProperty(grant, "messageCount") == 2
                && GetIntProperty(grant, "attachmentCount") == 11
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == beforeMessages + 2);

            var keyPrefix = "gm:" + requestId;
            var expectedKeyPredicate = $"(idempotency_key='{keyPrefix}' OR idempotency_key='{keyPrefix}:part:1')";
            var splitMessages = LoadInt(dbPath,
                $"SELECT COUNT(*) FROM mailbox_messages WHERE sender_character_id={CharacterId} AND {expectedKeyPredicate};");
            var splitAttachments = LoadInt(dbPath,
                $@"SELECT COUNT(*) FROM mailbox_attachments a
JOIN mailbox_messages m ON m.message_id=a.message_id
WHERE m.sender_character_id={CharacterId} AND (m.idempotency_key='{keyPrefix}' OR m.idempotency_key='{keyPrefix}:part:1');");
            var maxStack = LoadInt(dbPath,
                $@"SELECT COALESCE(MAX(a.item_count),0) FROM mailbox_attachments a
JOIN mailbox_messages m ON m.message_id=a.message_id
WHERE m.sender_character_id={CharacterId} AND (m.idempotency_key='{keyPrefix}' OR m.idempotency_key='{keyPrefix}:part:1');");
            var totalStack = LoadLong(dbPath,
                $@"SELECT COALESCE(SUM(a.item_count),0) FROM mailbox_attachments a
JOIN mailbox_messages m ON m.message_id=a.message_id
WHERE m.sender_character_id={CharacterId} AND (m.idempotency_key='{keyPrefix}' OR m.idempotency_key='{keyPrefix}:part:1');");
            Check("stack shards have bounded counts and deterministic totals",
                splitMessages == 2
                && splitAttachments == 11
                && maxStack <= stackLimit
                && totalStack == requestedCount);
            Check("each mail shard re-numbers ordinals from zero",
                LoadInt(dbPath,
                    $@"SELECT COUNT(*) FROM (
SELECT a.message_id, a.ordinal,
       ROW_NUMBER() OVER (PARTITION BY a.message_id ORDER BY a.ordinal)-1 AS expected
FROM mailbox_attachments a
JOIN mailbox_messages m ON m.message_id=a.message_id
WHERE m.sender_character_id={CharacterId} AND (m.idempotency_key='{keyPrefix}' OR m.idempotency_key='{keyPrefix}:part:1'))
WHERE ordinal<>expected;") == 0);

            var replay = gm.GiveItem(CharacterId, candidate.Item.Id, requestedCount, null, pvfIndex, requestId);
            Check("multi-mail request is durably idempotent",
                IsSuccess(replay)
                && GetBoolProperty(replay, "replayed")
                && GetIntProperty(replay, "messageCount") == 2
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == beforeMessages + 2);

            // Simulate a previous run that produced a third shard while a
            // changed PVF would now expect only two: replay must fail closed
            // instead of silently returning root + part:1.
            var extraKey = keyPrefix + ":part:2";
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $@"
INSERT INTO mailbox_messages
(sender_character_id,sender_account_id,sender_name,receiver_character_id,receiver_account_id,
 receiver_name,title,body,idempotency_key,request_hash,expire_at)
VALUES({CharacterId},{AccountId},'DNFadmin',{CharacterId},{AccountId},'character-mutation-selftest',
 'extra-shard','extra-shard','{extraKey}','extra-hash','9999-12-31 23:59:59');");
                tx.Commit();
            }
            var extraShardReplay = gm.GiveItem(CharacterId, candidate.Item.Id, requestedCount, null, pvfIndex, requestId);
            Check("unexpected extra mail shard fails closed",
                !IsSuccess(extraShardReplay)
                && GetStringProperty(extraShardReplay, "error").Contains("额外邮件分片", StringComparison.Ordinal));
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $"DELETE FROM mailbox_messages WHERE idempotency_key='{extraKey}';");
                tx.Commit();
            }

            // Remove one shard to emulate an interrupted/hand-edited history;
            // the next retry must fail closed instead of creating duplicates.
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $"DELETE FROM mailbox_messages WHERE idempotency_key='{keyPrefix}:part:1';");
                tx.Commit();
            }
            var partial = gm.GiveItem(CharacterId, candidate.Item.Id, requestedCount, null, pvfIndex, requestId);
            Check("partial multi-mail history fails closed",
                !IsSuccess(partial)
                && GetStringProperty(partial, "error").Contains("部分邮件分片", StringComparison.Ordinal));
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $"DELETE FROM mailbox_messages WHERE sender_character_id={CharacterId} AND {expectedKeyPredicate};");
                tx.Commit();
            }

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, @"CREATE TRIGGER gm_test_abort_multi_mail_audit
BEFORE INSERT ON mailbox_system_mail_audit
BEGIN SELECT RAISE(ABORT, 'gm multi audit failure'); END;");
                tx.Commit();
            }
            var beforeRollback = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;");
            var beforeRollbackAttachments = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_attachments;");
            var beforeRollbackAudits = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_system_mail_audit;");
            var beforeRollbackAuditAttachments = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_system_mail_audit_attachments;");
            var rollback = gm.GiveItem(
                CharacterId,
                candidate.Item.Id,
                requestedCount,
                null,
                pvfIndex,
                "stack-rollback-926014");
            Check("multi-mail audit failure rolls back every shard",
                !IsSuccess(rollback)
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == beforeRollback
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_attachments;") == beforeRollbackAttachments
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_system_mail_audit;") == beforeRollbackAudits
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_system_mail_audit_attachments;") == beforeRollbackAuditAttachments);
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "DROP TRIGGER gm_test_abort_multi_mail_audit;");
                tx.Commit();
            }
        }

        private static void CheckMailboxClear(GmService gm, string dbPath)
        {
            const int sharedCharacterId = 926017;
            const string campaignId = "mailbox-clear-selftest";
            const string exclusiveCampaignId = "mailbox-clear-exclusive-selftest";
            const long unrelatedOrphanMessageId = 987654321;
            long sharedMessageId;
            long exclusiveMessageId;
            long unrelatedOrphanAuditId;

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, @"
INSERT INTO characters(character_id, account_id, name, job, grow_type, level, exp, slot_index)
VALUES(926017, 926014, 'mailbox-shared-selftest', 0, 0, 1, 0, 9);");
                Exec(conn, tx, $@"
INSERT INTO mailbox_messages
(sender_character_id,sender_account_id,sender_name,receiver_character_id,receiver_account_id,
 receiver_name,title,body,idempotency_key,request_hash,expire_at)
VALUES(0,0,'GM',{CharacterId},{AccountId},'character-mutation-selftest',
 'shared-clear-mail','shared-clear-body','clear-shared','clear-shared-hash','2099-01-01 00:00:00');");
                sharedMessageId = LoadLastInsertId(conn, tx);
                Exec(conn, tx, $@"
INSERT INTO mailbox_recipients(message_id,character_id,folder) VALUES
({sharedMessageId},{CharacterId},0),
({sharedMessageId},{CharacterId},1),
({sharedMessageId},{sharedCharacterId},1);");
                Exec(conn, tx, $@"
INSERT INTO mailbox_attachments(message_id,ordinal,item_template_id,item_kind,item_count)
VALUES({sharedMessageId},0,910000,'stackable',7);");
                Exec(conn, tx, $@"
INSERT INTO mailbox_system_mail_audit
(message_id,actor_name,audit_reason,receiver_account_id,receiver_character_id,receiver_name,
 attachment_count,idempotency_key,request_hash,expire_at)
VALUES({sharedMessageId},'GM','clear-shared',926014,{CharacterId},'character-mutation-selftest',
 1,'clear-shared-audit','clear-shared-audit-hash','2099-01-01 00:00:00');");
                var sharedAuditId = LoadLastInsertId(conn, tx);
                Exec(conn, tx, $@"
INSERT INTO mailbox_system_mail_audit_attachments
(audit_id,ordinal,item_template_id,item_kind,item_count)
VALUES({sharedAuditId},0,910000,'stackable',7);");
                Exec(conn, tx, $@"
INSERT INTO mailbox_campaigns(campaign_id,payload_hash) VALUES('{campaignId}','clear-hash');");
                Exec(conn, tx, $@"
INSERT INTO mailbox_campaign_deliveries(campaign_id,character_id,message_id)
VALUES('{campaignId}',{CharacterId},{sharedMessageId});");

                Exec(conn, tx, $@"
INSERT INTO mailbox_messages
(sender_character_id,sender_account_id,sender_name,receiver_character_id,receiver_account_id,
 receiver_name,title,body,idempotency_key,request_hash,expire_at)
VALUES(0,0,'GM',{CharacterId},{AccountId},'character-mutation-selftest',
 'exclusive-clear-mail','exclusive-clear-body','clear-exclusive','clear-exclusive-hash','2099-01-01 00:00:00');");
                exclusiveMessageId = LoadLastInsertId(conn, tx);
                Exec(conn, tx, $@"
INSERT INTO mailbox_recipients(message_id,character_id,folder) VALUES({exclusiveMessageId},{CharacterId},0);");
                Exec(conn, tx, $@"
INSERT INTO mailbox_attachments(message_id,ordinal,item_template_id,item_kind,item_count)
VALUES({exclusiveMessageId},0,910001,'stackable',3);");
                Exec(conn, tx, $@"
INSERT INTO mailbox_system_mail_audit
(message_id,actor_name,audit_reason,receiver_account_id,receiver_character_id,receiver_name,
 attachment_count,idempotency_key,request_hash,expire_at)
VALUES({exclusiveMessageId},'GM','clear-exclusive',926014,{CharacterId},'character-mutation-selftest',
 1,'clear-exclusive-audit','clear-exclusive-audit-hash','2099-01-01 00:00:00');");
                Exec(conn, tx, $@"
INSERT INTO mailbox_campaigns(campaign_id,payload_hash) VALUES('{exclusiveCampaignId}','clear-exclusive-hash');");
                Exec(conn, tx, $@"
INSERT INTO mailbox_campaign_deliveries(campaign_id,character_id,message_id)
VALUES('{exclusiveCampaignId}',{CharacterId},{exclusiveMessageId});");
                Exec(conn, tx, $@"
INSERT INTO mailbox_system_mail_audit
(message_id,actor_name,audit_reason,receiver_account_id,receiver_character_id,receiver_name,
 attachment_count,idempotency_key,request_hash,expire_at)
VALUES({unrelatedOrphanMessageId},'GM','unrelated-orphan',926014,{sharedCharacterId},'mailbox-shared-selftest',
 1,'clear-orphan-audit','clear-orphan-audit-hash','2099-01-01 00:00:00');");
                unrelatedOrphanAuditId = LoadLastInsertId(conn, tx);
                Exec(conn, tx, $@"
INSERT INTO mailbox_system_mail_audit_attachments
(audit_id,ordinal,item_template_id,item_kind,item_count)
VALUES({unrelatedOrphanAuditId},0,910002,'stackable',1);");
                tx.Commit();
            }

            var result = gm.ClearCharacterMailbox(CharacterId);
            Check("clear mailbox returns recipient/message counters",
                IsSuccess(result)
                && GetIntProperty(result, "folder") == 0
                && GetIntProperty(result, "recipientCount") >= 2
                && GetIntProperty(result, "messageCount") >= 1);
            Check("clear mailbox removes only current folder-0 recipient",
                LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_recipients WHERE message_id={sharedMessageId} AND character_id={CharacterId} AND folder=0;") == 0
                && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_recipients WHERE message_id={sharedMessageId} AND character_id={CharacterId} AND folder=1;") == 1);
            Check("shared message and audit remain while another recipient exists",
                LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_messages WHERE message_id={sharedMessageId};") == 1
                && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_attachments WHERE message_id={sharedMessageId};") == 1
                && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_system_mail_audit WHERE message_id={sharedMessageId};") == 1);
            Check("exclusive message deletes attachments and system audit",
                LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_messages WHERE message_id={exclusiveMessageId};") == 0
                && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_attachments WHERE message_id={exclusiveMessageId};") == 0
                && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_system_mail_audit WHERE message_id={exclusiveMessageId};") == 0);

            using (var conn = Open(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT message_id FROM mailbox_campaign_deliveries WHERE campaign_id='{campaignId}';";
                var value = cmd.ExecuteScalar();
                Check("campaign delivery still references shared message", Convert.ToInt64(value) == sharedMessageId);
            }
            Check("isolated mail campaign reference is nulled on root deletion",
                LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_campaign_deliveries WHERE campaign_id='{exclusiveCampaignId}' AND message_id IS NULL;") == 1);
            Check("clear mailbox preserves unrelated orphan audit and attachment",
                LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_system_mail_audit WHERE audit_id={unrelatedOrphanAuditId} AND message_id={unrelatedOrphanMessageId};") == 1
                && LoadInt(dbPath, $"SELECT COUNT(*) FROM mailbox_system_mail_audit_attachments WHERE audit_id={unrelatedOrphanAuditId};") == 1);

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $"DELETE FROM mailbox_messages WHERE message_id={sharedMessageId};");
                Exec(conn, tx, $"DELETE FROM mailbox_campaigns WHERE campaign_id='{campaignId}';");
                Exec(conn, tx, $"DELETE FROM mailbox_campaigns WHERE campaign_id='{exclusiveCampaignId}';");
                Exec(conn, tx, $"DELETE FROM mailbox_system_mail_audit_attachments WHERE audit_id={unrelatedOrphanAuditId};");
                Exec(conn, tx, $"DELETE FROM mailbox_system_mail_audit WHERE audit_id={unrelatedOrphanAuditId};");
                Exec(conn, tx, $"DELETE FROM characters WHERE character_id={sharedCharacterId};");
                tx.Commit();
            }
        }

        private static void CheckTitleQuestSynchronization(GmService gm, PvfIndexService pvfIndex, string dbPath)
        {
            var candidate = pvfIndex.AllQuestMeta.Values
                .Where(meta => meta.RewardTitleItemId > 0)
                .Select(meta => new { meta.Id, Bound = gm.GetTitleBoundQuestIdsForTest(meta.Id) })
                .FirstOrDefault(value => value.Bound.Length > 1);
            Check("PVF contains a task-titlebook binding", candidate != null);
            if (candidate == null)
                return;

            var completed = gm.ForceCompleteQuest(CharacterId, candidate.Id);
            Check("completing a title reward quest succeeds", IsSuccess(completed));
            using (var conn = Open(dbPath))
            {
                var flags = ServerCore.Game.Quests.QuestRepository.LoadClearedFlags(conn, null, CharacterId);
                Check("completing one bound quest completes the whole title binding",
                    candidate.Bound.All(id => flags.TryGetValue(id, out var flag) && flag != 0));
            }
            Check("completing a bound quest inserts the title into the book", HasAnyTitleBookData(dbPath));

            var unclear = gm.UnclearQuest(CharacterId, candidate.Id);
            Check("unclearing a title reward quest succeeds", IsSuccess(unclear));
            using (var conn = Open(dbPath))
            {
                var flags = ServerCore.Game.Quests.QuestRepository.LoadClearedFlags(conn, null, CharacterId);
                Check("unclearing one bound quest clears the whole title binding",
                    candidate.Bound.All(id => !flags.TryGetValue(id, out var flag) || flag == 0));
            }
            Check("unclearing a bound quest removes the titlebook item", !HasAnyTitleBookData(dbPath));
            Check("unclearing a bound quest resets achievement progress",
                LoadInt(dbPath, "SELECT COUNT(1) FROM character_achievement_complete WHERE character_id=926014") == 0);
        }

        private static bool HasAnyTitleBookData(string dbPath)
        {
            return LoadInt(dbPath, $"SELECT COUNT(*) FROM character_new_titlebook WHERE character_id={CharacterId}") > 0;
        }

        private static void CheckCloneCharacterSlotIsolation(GmService gm, string dbPath)
        {
            var cloneName = "clone-slot";
            var result = gm.CloneCharacter(CharacterId, new CharacterCloneRequest
            {
                TargetAccountId = AccountId,
                NewName = cloneName,
                Options = new List<string> { "basic" },
            });
            Check("CloneCharacter returns success", IsSuccess(result));

            var clonedId = GetIntProperty(result, "characterId");
            Check("CloneCharacter creates a different character id", clonedId > 0 && clonedId != CharacterId);
            if (clonedId <= 0 || clonedId == CharacterId)
                return;
            Check("CloneCharacter assigns the next free slot",
                LoadInt(dbPath, $"SELECT slot_index FROM characters WHERE character_id={clonedId}") == 1);
            Check("CloneCharacter does not rename the source character",
                LoadInt(dbPath, "SELECT COUNT(1) FROM characters WHERE character_id=926014 AND name='character-mutation-selftest'") == 1);
            Check("CloneCharacter leaves no duplicate active slots",
                LoadInt(dbPath, @"
SELECT COUNT(1)
FROM (
    SELECT slot_index
    FROM characters
    WHERE account_id=926014 AND delete_flag=0
    GROUP BY slot_index
    HAVING COUNT(1) > 1
);") == 0);

            var second = gm.CloneCharacter(CharacterId, new CharacterCloneRequest
            {
                TargetAccountId = AccountId,
                NewName = "clone-slot-2",
                Options = new List<string> { "basic" },
            });
            Check("CloneCharacter supports a second consecutive clone", IsSuccess(second));
            var secondId = GetIntProperty(second, "characterId");
            Check("consecutive clones use distinct generated ids", secondId > 0 && secondId != clonedId);
            Check("second consecutive clone assigns another free slot",
                secondId > 0 && LoadInt(dbPath, $"SELECT slot_index FROM characters WHERE character_id={secondId}") == 2);
            Check("consecutive clones leave no duplicate active slots",
                LoadInt(dbPath, @"
SELECT COUNT(1)
FROM (
    SELECT slot_index
    FROM characters
    WHERE account_id=926014 AND delete_flag=0
    GROUP BY slot_index
    HAVING COUNT(1) > 1
);") == 0);

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $"DELETE FROM characters WHERE character_id={clonedId};");
                if (secondId > 0)
                    Exec(conn, tx, $"DELETE FROM characters WHERE character_id={secondId};");
                tx.Commit();
            }
        }

        private static void CheckCloneOptionCoverage(GmService gm, PvfIndexService pvfIndex, string dbPath)
        {
            var restrictedEquipment = pvfIndex.AllItems.FirstOrDefault(item =>
                string.Equals(item.Kind, "equipment", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.TypeTag, "weapon", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.UsableJob)
                && !AvatarGrantPolicy.IsUsableByJob(item.UsableJob, 0));
            Check("PVF contains equipment incompatible with job 0 for clone stripping", restrictedEquipment != null);
            var compatibleRestrictedEquipment = pvfIndex.AllItems.FirstOrDefault(item =>
            {
                var metadata = ItemMetadataResolver.Resolve(item.Id);
                return string.Equals(item.Kind, "equipment", StringComparison.OrdinalIgnoreCase)
                    && metadata != null
                    && !ItemMetadataResolver.IsAvatarMetadata(metadata)
                    && !ItemMetadataResolver.IsPetInventoryEquipment(item.Id)
                    && !string.IsNullOrWhiteSpace(item.UsableJob)
                    && !item.UsableJob.Replace("`", string.Empty).Trim().Equals("[all]", StringComparison.OrdinalIgnoreCase)
                    && AvatarGrantPolicy.IsUsableByJob(item.UsableJob, 0);
            });
            Check("PVF contains job-restricted equipment compatible with job 0", compatibleRestrictedEquipment != null);
            var compatibleRestrictedAvatar = pvfIndex.AllItems.FirstOrDefault(item =>
            {
                var metadata = ItemMetadataResolver.Resolve(item.Id);
                return metadata != null
                    && ItemMetadataResolver.IsAvatarMetadata(metadata)
                    && !string.IsNullOrWhiteSpace(item.UsableJob)
                    && !item.UsableJob.Replace("`", string.Empty).Trim().Equals("[all]", StringComparison.OrdinalIgnoreCase)
                    && AvatarGrantPolicy.IsUsableByJob(item.UsableJob, 0);
            });
            Check("PVF contains job-restricted avatar compatible with job 0", compatibleRestrictedAvatar != null);
            SeedCloneOptionRows(dbPath, restrictedEquipment, compatibleRestrictedEquipment, compatibleRestrictedAvatar);
            var unknownTableResult = gm.CloneCharacter(CharacterId, new CharacterCloneRequest
            {
                TargetAccountId = AccountId,
                NewName = "clunknown",
                Options = new List<string> { "basic" },
            });
            Check("clone rejects an unregistered character-owned table",
                !IsSuccess(unknownTableResult)
                && GetStringProperty(unknownTableResult, "error").Contains("未登记", StringComparison.Ordinal));
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "DROP TABLE character_dynamic_clone_selftest;");
                tx.Commit();
            }
            Check("inventory list reads the service-effective pet consumable count",
                GetListedItemCount(gm.ListItems(CharacterId, pvfIndex), 7, 189) == 37);
            CheckCloneCapacityIsolation(gm, dbPath);

            var basicOnlyId = CloneForOption(gm, "clbasic", "basic");
            Check("Clone basic-only does not copy active quests",
                basicOnlyId > 0 && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_active_quests WHERE character_id={basicOnlyId}") == 0);
            Check("Clone basic-only does not copy cleared quest flags",
                basicOnlyId > 0 && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_invisible_falgs WHERE character_id={basicOnlyId}") == 0);
            Check("Clone basic-only does not bypass the skills option for PVP skills",
                basicOnlyId > 0 && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_pvp_skills WHERE character_id={basicOnlyId}") == 0);
            Check("Clone basic-only does not bypass the misc option for expert job",
                basicOnlyId > 0 && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_expert_job WHERE character_id={basicOnlyId}") == 0);
            Check("Clone basic-only does not copy dungeon effect outbox records",
                basicOnlyId > 0 && LoadInt(dbPath, $"SELECT COUNT(1) FROM dungeon_persistent_effect_outbox WHERE character_id={basicOnlyId}") == 0);
            Check("Clone basic-only does not copy mercenary reward outbox records",
                basicOnlyId > 0 && LoadInt(dbPath, $"SELECT COUNT(1) FROM mercenary_reward_outbox WHERE character_id={basicOnlyId}") == 0);
            Check("Clone basic-only does not copy quest progress inbox records",
                basicOnlyId > 0 && LoadInt(dbPath, $"SELECT COUNT(1) FROM quest_progress_event_inbox WHERE character_id={basicOnlyId}") == 0);
            DeleteCharacterRow(dbPath, basicOnlyId);

            CheckCloneOption(gm, dbPath, "skills", "clskil", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_skills WHERE character_id={id} AND skill_id=4242") > 0
                && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_hotkey_slots WHERE character_id={id} AND slot_index=44") > 0
                && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_pvp_skill_state WHERE character_id={id}") == 1
                && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_pvp_skills WHERE character_id={id} AND skill_id=4343") == 1);
            CheckCloneOption(gm, dbPath, "quests", "clques", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_active_quests WHERE character_id={id} AND quest_id=42420") > 0
                && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_invisible_falgs WHERE character_id={id} AND slot_index=2424") > 0
                && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_quest_notify_selections WHERE character_id={id} AND quest_id=42420") == 1);
            CheckCloneOption(gm, dbPath, "titlebook", "cltitl", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_new_titlebook WHERE character_id={id} AND category=0 AND slot_index=42") > 0);
            CheckCloneOption(gm, dbPath, "dungeon", "cldung", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_dungeon_permissions WHERE character_id={id} AND dungeon_id=4242") > 0);
            CheckCloneOption(gm, dbPath, "daily", "cldail", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_daily_counters WHERE character_id={id} AND counter_key='clone_option_daily'") > 0
                && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_daily_challenge_claims WHERE character_id={id} AND group_index=4") == 1);
            CheckCloneOption(gm, dbPath, "wallet", "clwall", id => HasClonedItem(dbPath, id, 0, 0, "stackable"));
            CheckCloneOption(gm, dbPath, "quickSlots", "clquik", id => HasClonedItem(dbPath, id, 0, 3, "stackable"));
            CheckCloneOption(gm, dbPath, "mainEquipment", "cleqip", id => HasClonedItem(dbPath, id, 0, 9, "equipment"));
            CheckCloneOption(gm, dbPath, "consumables", "clcons", id => HasClonedItem(dbPath, id, 0, 65, "stackable"));
            CheckCloneOption(gm, dbPath, "materials", "clmatr", id => HasClonedItem(dbPath, id, 0, 121, "stackable"));
            CheckCloneOption(gm, dbPath, "questItems", "clqitm", id => HasClonedItem(dbPath, id, 0, 177, "stackable"));
            CheckCloneOption(gm, dbPath, "expertMaterials", "clexpm", id => HasClonedItem(dbPath, id, 0, 233, "stackable"));
            CheckCloneOption(gm, dbPath, "emblems", "clembl", id => HasClonedItem(dbPath, id, 0, 289, "stackable"));
            CheckCloneOption(gm, dbPath, "personalCargo", "clpcar", id =>
                HasClonedItem(dbPath, id, 2, 0, "stackable")
                && HasClonedItem(dbPath, id, 2, 7, "stackable")
                && !HasClonedItem(dbPath, id, 2, 8, "stackable"));
            CheckCloneOption(gm, dbPath, "equipped", "cleqed", id =>
                HasClonedItem(dbPath, id, 3, 12, "equipment")
                && CountCoreKind(dbPath, id, ItemCore.KindAvatar, listType: 0) == 0
                && (restrictedEquipment == null
                    || (CountCoreItem(dbPath, id, restrictedEquipment.Id, listType: 3) == 0
                        && CountCoreItem(dbPath, id, restrictedEquipment.Id, listType: 0) == 1))
                && (compatibleRestrictedEquipment == null
                    || (CountCoreItem(dbPath, id, compatibleRestrictedEquipment.Id, listType: 3) == 0
                        && CountCoreItem(dbPath, id, compatibleRestrictedEquipment.Id, listType: 0) == 1))
                && (compatibleRestrictedAvatar == null
                    || (CountCoreItem(dbPath, id, compatibleRestrictedAvatar.Id, listType: 3) == 0
                        && CountCoreItem(dbPath, id, compatibleRestrictedAvatar.Id, listType: 1) == 1)));
            CheckCloneOption(gm, dbPath, "avatars", "clavat", id => HasClonedItem(dbPath, id, 1, 1, "avatar"));
            CheckCloneOption(gm, dbPath, "pets", "clpets", id =>
                HasClonedItem(dbPath, id, 7, 0, "pet")
                && LoadInt(dbPath, $"SELECT creature_key FROM character_creatures WHERE character_id={id} AND sort_order=77") != 424277
                && (LoadCore(dbPath, id, 7, 0)?.Value ?? 0) == LoadInt(dbPath, $"SELECT creature_key FROM character_creatures WHERE character_id={id} AND sort_order=77"));
            CheckCloneOption(gm, dbPath, "petEquipment", "clpequ", id => HasClonedItem(dbPath, id, 7, 140, "equipment"));
            CheckCloneOption(gm, dbPath, "petConsumables", "clpcon", id =>
                HasClonedItem(dbPath, id, 7, 189, "pet")
                && (LoadCore(dbPath, id, 7, 189)?.Count ?? 0) == 37
                && GetListedItemCount(gm.ListItems(id, pvfIndex), 7, 189) == 37);
            CheckCloneOption(gm, dbPath, "locks", "cllock", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_item_locks WHERE character_id={id} AND equipment_lock_id=4242") > 0);
            CheckCloneOption(gm, dbPath, "misc", "clmisc", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_item_values WHERE character_id={id} AND list_kind='clone_option'") > 0
                && LoadInt(dbPath, $"SELECT enchanter_endurance FROM character_expert_job WHERE character_id={id}") == 4242
                && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_expert_job_recipes WHERE character_id={id} AND recipe_id=4242") == 1);
            CheckCloneOption(gm, dbPath, "audit", "claudi", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM item_audit_log WHERE character_id={id} AND action_name='clone_option_audit'") > 0);
        }

        private static void CheckGrantAndConfigurationSlotBounds(GmService gm, PvfIndexService pvfIndex, string dbPath)
        {
            var equipment = pvfIndex.AllItems.FirstOrDefault(item =>
            {
                if (!string.Equals(item.Kind, "equipment", StringComparison.OrdinalIgnoreCase))
                    return false;
                var metadata = ItemMetadataResolver.Resolve(item.Id);
                if (metadata == null
                    || ItemMetadataResolver.IsAvatarMetadata(metadata)
                    || ItemMetadataResolver.IsPetInventoryEquipment(item.Id)
                    || ItemMetadataResolver.RequiresManualGrantType(metadata))
                    return false;
                var capability = EquipmentGrantPolicy.Describe(metadata);
                return capability.CanUpgrade && capability.CanAmplify && capability.CanForge;
            });
            Check("PVF contains configurable ordinary equipment for slot-bound tests", equipment != null);
            if (equipment == null)
                return;

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "INSERT OR REPLACE INTO character_container_state(character_id,list_type,list_param16) VALUES(926014,0,0);");
                for (var slot = 9; slot <= 40; slot++)
                    SeedCloneOptionItem(conn, tx, 0, slot, slot == 40 ? equipment.Id : 980000 + slot, "equipment");
                SeedCloneOptionItem(conn, tx, 0, 41, equipment.Id, "equipment");
                tx.Commit();
            }

            var mailOptions = new ItemGrantOptions
            {
                QualityMode = ItemQualityMode.Top,
                UpgradeLevel = 7,
                AmplifyType = 3,
                ForgingLevel = 4,
            };
            var fullGrant = gm.GiveItem(CharacterId, equipment.Id, 1, mailOptions, pvfIndex, "full-bag-mail-926014");
            Check("mail grant succeeds even when the main bag is full", IsSuccess(fullGrant));
            Check("mail grant leaves unopened slot unchanged before claim",
                CountCoreItem(dbPath, CharacterId, equipment.Id, listType: 0) == 2);
            var mailedEquipment = LoadMailAttachmentCore(dbPath, equipment.Id);
            Check("mail attachment preserves advanced equipment options",
                mailedEquipment != null
                && mailedEquipment.Upgrade == 7
                && mailedEquipment.AmplifyType == 3
                && mailedEquipment.AmplifyValue > 0
                && mailedEquipment.GenuineUpgrade == 4);

            var beforeRejectedMail = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;");
            var tooMany = gm.GiveItem(
                CharacterId,
                equipment.Id,
                11,
                mailOptions,
                pvfIndex,
                "too-many-mail-926014");
            Check("more than ten non-stackable attachments are split into multiple mails",
                IsSuccess(tooMany)
                && GetIntProperty(tooMany, "messageCount") == 2
                && GetIntProperty(tooMany, "attachmentCount") == 11
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == beforeRejectedMail + 2);

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, @"CREATE TRIGGER gm_test_abort_mail_audit
BEFORE INSERT ON mailbox_system_mail_audit
BEGIN SELECT RAISE(ABORT, 'gm audit failure'); END;");
                tx.Commit();
            }
            var beforeRollback = LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;");
            var rollbackGrant = gm.GiveItem(
                CharacterId,
                equipment.Id,
                1,
                mailOptions,
                pvfIndex,
                "rollback-mail-926014");
            Check("audit failure rolls back message recipient and attachment atomically",
                !IsSuccess(rollbackGrant)
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_messages;") == beforeRollback
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_recipients;") == beforeRollback
                && LoadInt(dbPath, "SELECT COUNT(*) FROM mailbox_system_mail_audit;") == beforeRollback);
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "DROP TRIGGER gm_test_abort_mail_audit;");
                tx.Commit();
            }

            var configureOptions = new ItemGrantOptions
            {
                QualityMode = ItemQualityMode.Top,
                UpgradeLevel = 0,
                AmplifyType = 0,
                ForgingLevel = 0,
            };
            var openConfigure = gm.ConfigureInventoryItem(CharacterId, new InventoryItemConfigureRequest
            {
                ListType = (int)InventoryListType.Main,
                Slot = 40,
                Options = configureOptions,
            }, pvfIndex);
            Check("configuration accepts an opened main-bag slot", IsSuccess(openConfigure));

            var closedConfigure = gm.ConfigureInventoryItem(CharacterId, new InventoryItemConfigureRequest
            {
                ListType = (int)InventoryListType.Main,
                Slot = 41,
                Options = configureOptions,
            }, pvfIndex);
            Check("configuration rejects an unopened main-bag slot", !IsSuccess(closedConfigure));

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "UPDATE characters SET ex_equip_slot_stat=1 WHERE character_id=926014;");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_container_state(character_id,list_type,list_param16) VALUES(926014,2,8);");
                Exec(conn, tx, "INSERT OR REPLACE INTO account_cargo_state(account_id,selection_key) VALUES(926014,1);");
                SeedCloneOptionItem(conn, tx, 3, 21, equipment.Id, "equipment");
                SeedCloneOptionItem(conn, tx, 3, 22, equipment.Id, "equipment");
                SeedCloneOptionItem(conn, tx, 2, 7, equipment.Id, "equipment");
                SeedCloneOptionItem(conn, tx, 2, 8, equipment.Id, "equipment");
                SeedAccountCargoItem(conn, tx, 0, equipment.Id);
                SeedAccountCargoItem(conn, tx, 1, equipment.Id);
                tx.Commit();
            }
            var openSpecialConfigure = gm.ConfigureInventoryItem(CharacterId, new InventoryItemConfigureRequest
            {
                ListType = (int)InventoryListType.Equipment,
                Slot = 21,
                Options = configureOptions,
            }, pvfIndex);
            Check("configuration accepts an unlocked special-equipment slot", IsSuccess(openSpecialConfigure));
            var closedSpecialConfigure = gm.ConfigureInventoryItem(CharacterId, new InventoryItemConfigureRequest
            {
                ListType = (int)InventoryListType.Equipment,
                Slot = 22,
                Options = configureOptions,
            }, pvfIndex);
            Check("configuration rejects a locked special-equipment slot", !IsSuccess(closedSpecialConfigure));

            var schema = Path.Combine(AppContext.BaseDirectory, "ServerCore", "Sqlite", "item_schema.sql");
            var inventory = new NewInventoryStore(dbPath, schema);
            Check("configuration core accepts an opened personal-cargo slot",
                inventory.UpdateItemCore(CharacterId, AccountId, InventoryListType.PersonalCargo, 7,
                    core => { core.InstanceValue = 7007; return null; }, out _, out _));
            Check("configuration core rejects an unopened personal-cargo slot",
                !inventory.UpdateItemCore(CharacterId, AccountId, InventoryListType.PersonalCargo, 8,
                    core => { core.InstanceValue = 8008; return null; }, out _, out _));
            Check("configuration core accepts an opened account-cargo slot",
                inventory.UpdateItemCore(CharacterId, AccountId, InventoryListType.AccountCargo, 0,
                    core => { core.InstanceValue = 1000; return null; }, out _, out _));
            Check("configuration core rejects an unopened account-cargo slot",
                !inventory.UpdateItemCore(CharacterId, AccountId, InventoryListType.AccountCargo, 1,
                    core => { core.InstanceValue = 1001; return null; }, out _, out _));

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "DELETE FROM character_new_items WHERE character_id=926014 AND list_type=0 AND slot_index BETWEEN 9 AND 64;");
                Exec(conn, tx, "DELETE FROM character_new_items WHERE character_id=926014 AND list_type=3 AND slot_index IN (21,22);");
                Exec(conn, tx, "DELETE FROM character_new_items WHERE character_id=926014 AND list_type=2 AND slot_index IN (7,8);");
                Exec(conn, tx, "DELETE FROM account_cargo_new_items WHERE account_id=926014 AND slot_index IN (0,1);");
                Exec(conn, tx, "UPDATE character_container_state SET list_param16=24 WHERE character_id=926014 AND list_type=0;");
                Exec(conn, tx, "UPDATE characters SET ex_equip_slot_stat=7 WHERE character_id=926014;");
                tx.Commit();
            }
        }

        private static void CheckCloneOption(GmService gm, string dbPath, string option, string cloneName, Func<int, bool> assertion)
        {
            var clonedId = CloneForOption(gm, cloneName, option);
            Check("Clone option " + option + " has effect", clonedId > 0 && assertion(clonedId));
            DeleteCharacterRow(dbPath, clonedId);
        }

        private static int CloneForOption(GmService gm, string cloneName, params string[] options)
        {
            var result = gm.CloneCharacter(CharacterId, new CharacterCloneRequest
            {
                TargetAccountId = AccountId,
                NewName = cloneName,
                Options = options.ToList(),
            });
            Check("CloneCharacter option run succeeds: " + cloneName, IsSuccess(result), GetStringProperty(result, "error"));
            return GetIntProperty(result, "characterId");
        }

        private static void CheckCloneCapacityIsolation(GmService gm, string dbPath)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "INSERT OR REPLACE INTO character_container_state(character_id,list_type,list_param16) VALUES(926014,0,0);");
                Exec(conn, tx, "UPDATE characters SET ex_equip_slot_stat=1 WHERE character_id=926014;");
                tx.Commit();
            }

            var mainId = CloneForOption(gm, "clcapm", "mainEquipment");
            Check("clone respects source main-bag expansion stage",
                mainId > 0
                && HasClonedItem(dbPath, mainId, 0, 40, "equipment")
                && !HasClonedItem(dbPath, mainId, 0, 41, "equipment"));
            DeleteCharacterRow(dbPath, mainId);

            var equippedId = CloneForOption(gm, "clcape", "equipped");
            Check("clone respects each special-equipment unlock bit",
                equippedId > 0
                && HasClonedItem(dbPath, equippedId, 3, 21, "equipment")
                && !HasClonedItem(dbPath, equippedId, 3, 22, "equipment")
                && !HasClonedItem(dbPath, equippedId, 3, 23, "equipment"));
            DeleteCharacterRow(dbPath, equippedId);

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "UPDATE character_container_state SET list_param16=24 WHERE character_id=926014 AND list_type=0;");
                Exec(conn, tx, "UPDATE characters SET ex_equip_slot_stat=7 WHERE character_id=926014;");
                tx.Commit();
            }
        }

        private static bool HasClonedItem(string dbPath, int characterId, int listType, int slot, string kind)
        {
            _ = kind;
            return LoadCore(dbPath, characterId, listType, slot) != null;
        }

        private static void DeleteCharacterRow(string dbPath, int characterId)
        {
            if (characterId <= 0)
                return;
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $"DELETE FROM characters WHERE character_id={characterId};");
                tx.Commit();
            }
        }

        private static void SeedCloneOptionRows(
            string dbPath,
            PvfIndexService.ItemEntry restrictedEquipment,
            PvfIndexService.ItemEntry compatibleRestrictedEquipment,
            PvfIndexService.ItemEntry compatibleRestrictedAvatar)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, @"CREATE TABLE IF NOT EXISTS character_dynamic_clone_selftest (
row_id INTEGER PRIMARY KEY AUTOINCREMENT,
character_id INTEGER NOT NULL,
marker TEXT NOT NULL,
UNIQUE(character_id, marker),
FOREIGN KEY(character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);");
                Exec(conn, tx, "INSERT OR IGNORE INTO character_dynamic_clone_selftest(character_id, marker) VALUES(926014, 'dynamic-base');");
                Exec(conn, tx, @"INSERT INTO dungeon_persistent_effect_outbox
(source_event_id,effect_kind,effect_scope,scope_target,character_id,account_id,
 payload_version,payload_json,state,created_at,updated_at)
VALUES('clone-event','clone-effect',1,926014,926014,926014,
 1,'{}',0,1,1);");
                Exec(conn, tx, @"INSERT INTO mercenary_reward_outbox
(assignment_id,account_id,character_id,area_index,period_index,
 mail_title_key,mail_message_key)
VALUES(4242,926014,926014,1,1,'clone-title','clone-message');");
                Exec(conn, tx, @"INSERT INTO mercenary_reward_items
(outbox_id,ordinal,item_template_id,item_count)
SELECT outbox_id,0,910000,7
FROM mercenary_reward_outbox
WHERE assignment_id=4242;");
                Exec(conn, tx, @"INSERT INTO account_mercenary_assignments
(assignment_id,account_id,character_id,character_level,start_time,finish_time,
 area_index,period_index)
VALUES(4243,926014,926014,60,1,2,1,1);");
                Exec(conn, tx, @"INSERT INTO quest_progress_event_inbox
(character_id,activation_id,event_id,event_kind)
VALUES(926014,'clone-activation','clone-event','clone-kind');");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_skills(character_id, page_index, slot, skill_id, level) VALUES(926014, 0, 44, 4242, 1);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_pvp_skill_state(character_id) VALUES(926014);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_pvp_skills(character_id,page_index,slot,skill_id,level) VALUES(926014,0,43,4343,2);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_hotkey_slots(character_id, slot_index, hotkey_value) VALUES(926014, 44, 4242);");
                Exec(conn, tx, @"INSERT OR REPLACE INTO character_active_quests
(character_id,slot,quest_id,trigger_value,activation_id)
VALUES(926014,44,42420,7,'clone-active-quest');");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_invisible_falgs(character_id, slot_index, flag_value) VALUES(926014, 2424, 1);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_quest_notify_selections(character_id,slot_index,quest_id) VALUES(926014,0,42420);");
                SeedNewTitleBookItem(conn, tx, 0, 42, 904242);
                Exec(conn, tx, "INSERT OR REPLACE INTO character_dungeon_permissions(character_id, sort_order, dungeon_id, clear_state) VALUES(926014, 42, 4242, 4);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_daily_counters(character_id, counter_key, period, value) VALUES(926014, 'clone_option_daily', 'day', 1);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_daily_challenge_claims(character_id,group_index) VALUES(926014,4);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_expert_job(character_id,enchanter_endurance) VALUES(926014,4242);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_expert_job_recipes(character_id,recipe_id) VALUES(926014,4242);");
                SeedCloneOptionItem(conn, tx, 3, 12, 424212, "equipment");
                SeedCloneOptionItem(conn, tx, 3, 0, compatibleRestrictedAvatar?.Id ?? 930002, "avatar");
                if (restrictedEquipment != null)
                {
                    SeedCloneOptionItem(conn, tx, 3, 11, restrictedEquipment.Id, "equipment");
                }
                if (compatibleRestrictedEquipment != null)
                {
                    SeedCloneOptionItem(conn, tx, 3, 13, compatibleRestrictedEquipment.Id, "equipment");
                }
                Exec(conn, tx, "INSERT OR REPLACE INTO character_creatures(character_id, sort_order, creature_key, field04) VALUES(926014, 77, 424277, 1);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_item_locks(character_id, equipment_lock_id, inventory_list_type, slot, state) VALUES(926014, 4242, 0, 9, 1);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_item_values(character_id, list_kind, sort_order, item_id, value) VALUES(926014, 'clone_option', 1, 4242, 9);");
                Exec(conn, tx, "INSERT INTO item_audit_log(owner_scope, owner_id, character_id, action_name, list_type, slot_index, item_template_id, delta_stack_count, payload_json) VALUES('character', 926014, 926014, 'clone_option_audit', 0, 0, 4242, 1, '{}');");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_container_state(character_id,list_type,list_param16) VALUES(926014,0,24);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_container_state(character_id,list_type,list_param16) VALUES(926014,1,0);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_container_state(character_id,list_type,list_param16) VALUES(926014,2,8);");

                SeedCloneOptionItem(conn, tx, 0, 0, 910000, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 3, 910003, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 9, 910009, "equipment");
                SeedCloneOptionItem(conn, tx, 0, 40, 910040, "equipment");
                SeedCloneOptionItem(conn, tx, 0, 41, 910041, "equipment");
                SeedCloneOptionItem(conn, tx, 0, 65, 910065, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 121, 910121, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 177, 910177, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 233, 910233, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 289, 910289, "stackable");
                SeedCloneOptionItem(conn, tx, 2, 0, 920000, "stackable");
                SeedCloneOptionItem(conn, tx, 2, 7, 920007, "stackable");
                SeedCloneOptionItem(conn, tx, 2, 8, 920008, "stackable");
                SeedCloneOptionItem(conn, tx, 1, 1, 930001, "avatar");
                SeedCloneOptionItem(conn, tx, 3, 21, 930021, "equipment");
                SeedCloneOptionItem(conn, tx, 3, 22, 930022, "equipment");
                SeedCloneOptionItem(conn, tx, 3, 23, 930023, "equipment");
                SeedCloneOptionItem(conn, tx, 7, 0, 970000, "pet");
                SeedCloneOptionItem(conn, tx, 7, 140, 970140, "equipment");
                SeedCloneOptionItem(conn, tx, 7, 189, 970189, "pet", 37);
                tx.Commit();
            }
        }

        private static void SeedCloneOptionItem(SqliteConnection conn, SqliteTransaction tx, int listType, int slot, int itemId, string kind, int count = 1)
        {
            byte coreKind;
            if (string.Equals(kind, "avatar", StringComparison.OrdinalIgnoreCase)) coreKind = ItemCore.KindAvatar;
            else if (listType == 7 && slot <= 139) coreKind = ItemCore.KindCreature;
            else if (listType == 7 && slot <= 188) coreKind = ItemCore.KindCreatureEquipment;
            else if (listType == 7) coreKind = ItemCore.KindCreatureConsumable;
            else if (listType == 2 || listType == 3 || (listType == 0 && slot >= 9 && slot <= 64)) coreKind = ItemCore.KindEquipment;
            else if (listType == 0 && slot == 0) coreKind = ItemCore.KindSpecialMaterial;
            else if (listType == 0 && slot >= 121 && slot <= 176) coreKind = ItemCore.KindMaterial;
            else if (listType == 0 && slot >= 177 && slot <= 232) coreKind = ItemCore.KindQuest;
            else if (listType == 0 && slot >= 233 && slot <= 288) coreKind = ItemCore.KindExpertJobMaterial;
            else if (listType == 0 && slot >= 289) coreKind = ItemCore.KindAvatarEmblem;
            else coreKind = ItemCore.KindConsumable;
            var core = ItemCore.Create(coreKind, itemId);
            if (coreKind == ItemCore.KindCreature) core.Value = 424277;
            else if (coreKind == ItemCore.KindAvatar) core.Value = 424201;
            else if (coreKind != ItemCore.KindEquipment && coreKind != ItemCore.KindCreatureEquipment) core.Count = count;
            else core.InstanceValue = 10000;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT OR REPLACE INTO character_new_items
    (owner_scope, owner_id, character_id, list_type, slot_index, item_core)
VALUES ('character', 926014, 926014, @list, @slot, @core);";
                cmd.Parameters.AddWithValue("@list", listType);
                cmd.Parameters.AddWithValue("@slot", slot);
                cmd.Parameters.AddWithValue("@core", core.ToBytes());
                cmd.ExecuteNonQuery();
            }
            if (coreKind == ItemCore.KindAvatar)
                Exec(conn, tx, $@"INSERT OR REPLACE INTO character_avatar_detail
(item_uid,owner_id,character_id,item_id,jewel_socket) VALUES(424201,926014,926014,{itemId},zeroblob(30));");
        }

        private static void SeedAccountCargoItem(SqliteConnection conn, SqliteTransaction tx, int slot, int itemId)
        {
            var core = ItemCore.Create(ItemCore.KindEquipment, itemId);
            core.InstanceValue = 10000;
            using var command = conn.CreateCommand();
            command.Transaction = tx;
            command.CommandText = @"INSERT INTO account_cargo_new_items
(account_id,character_id,list_type,slot_index,item_core)
VALUES(926014,926014,12,@slot,@core);";
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core.ToBytes());
            command.ExecuteNonQuery();
        }

        private static void SeedNewTitleBookItem(SqliteConnection conn, SqliteTransaction tx, int category, int slot, int itemId)
        {
            var core = ItemCore.Create(ItemCore.KindEquipment, itemId);
            using var command = conn.CreateCommand();
            command.Transaction = tx;
            command.CommandText = "INSERT OR REPLACE INTO character_new_titlebook(character_id,category,slot_index,item_core) VALUES(926014,@category,@slot,@core);";
            command.Parameters.AddWithValue("@category", category);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core.ToBytes());
            command.ExecuteNonQuery();
        }

        private static void CheckAccountBackupRestoreSlotCompatibility(GmService gm, string dbPath)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, @"
INSERT INTO characters(character_id, account_id, name, job, grow_type, level, exp, slot_index)
VALUES(926016, 926014, 'backup-slot-test', 0, 0, 1, 0, 8);");
                Exec(conn, tx, @"INSERT INTO account_increase_chance_lottery_progress
(account_id,item_template_id,reward_index) VALUES(926014,424242,3);");
                Exec(conn, tx, @"INSERT INTO mailbox_messages
(message_id,sender_character_id,sender_account_id,sender_name,
 receiver_character_id,receiver_account_id,receiver_name,title,body,
 idempotency_key,request_hash,expire_at)
VALUES(824242,0,0,'GM',926014,926014,'character-mutation-selftest',
 'backup-mail-selftest','backup-mail-body','backup-mail-source','backup-mail-hash','2099-01-01 00:00:00');");
                Exec(conn, tx, @"INSERT INTO mailbox_recipients
(recipient_id,message_id,character_id,folder) VALUES(824243,824242,926014,0);");
                Exec(conn, tx, @"INSERT INTO mailbox_attachments
(attachment_id,message_id,ordinal,item_template_id,item_kind,item_count)
VALUES(824244,824242,0,910000,'stackable',7);");
                Exec(conn, tx, @"INSERT INTO mailbox_system_mail_audit
(audit_id,message_id,actor_name,audit_reason,receiver_account_id,
 receiver_character_id,receiver_name,attachment_count,idempotency_key,
 request_hash,expire_at)
VALUES(824245,824242,'GM','backup-selftest',926014,926014,
 'character-mutation-selftest',1,'backup-audit-source','backup-audit-hash','2099-01-01 00:00:00');");
                Exec(conn, tx, @"INSERT INTO mailbox_system_mail_audit_attachments
(audit_attachment_id,audit_id,ordinal,item_template_id,item_kind,item_count)
VALUES(824246,824245,0,910000,'stackable',7);");
                tx.Commit();
            }

            var exported = gm.ExportAccountBackup(AccountId) as AccountBackupFile;
            Check("ExportAccountBackup returns a backup file", exported != null);
            if (exported == null)
                return;

            var characterDump = exported.Tables.FirstOrDefault(t => t.Name.Equals("characters", StringComparison.OrdinalIgnoreCase));
            Check("account backup contains characters table", characterDump != null);
            if (characterDump == null)
                return;

            Check("current account backup uses version 2", exported.Version == 2);
            Check("account backup captures v52 account lottery progress",
                exported.Tables.Any(t => t.Name.Equals("account_increase_chance_lottery_progress", StringComparison.OrdinalIgnoreCase)));
            Check("account backup captures mailbox relation graph",
                new[]
                {
                    "mailbox_messages",
                    "mailbox_recipients",
                    "mailbox_attachments",
                    "mailbox_system_mail_audit",
                    "mailbox_system_mail_audit_attachments",
                }.All(tableName => exported.Tables.Any(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase))));
            Check("account backup captures mercenary reward item relations",
                exported.Tables.Any(t => t.Name.Equals("mercenary_reward_outbox", StringComparison.OrdinalIgnoreCase))
                && exported.Tables.Any(t => t.Name.Equals("mercenary_reward_items", StringComparison.OrdinalIgnoreCase)));

            var slotIndex = characterDump.Columns.FindIndex(c => c.Equals("slot_index", StringComparison.OrdinalIgnoreCase));
            Check("current account backup captures slot_index", slotIndex >= 0);
            RemoveBackupColumn(characterDump, "slot_index");
            var activeQuestDump = exported.Tables.FirstOrDefault(t =>
                t.Name.Equals("character_active_quests", StringComparison.OrdinalIgnoreCase));
            var questInboxDump = exported.Tables.FirstOrDefault(t =>
                t.Name.Equals("quest_progress_event_inbox", StringComparison.OrdinalIgnoreCase));
            RemoveBackupColumn(activeQuestDump, "activation_id");
            RemoveBackupColumn(questInboxDump, "activation_id");

            var rejectedV2 = gm.RestoreAccountBackup(exported);
            Check("v2 backup missing activation_id is rejected before restore",
                !IsSuccess(rejectedV2)
                && GetStringProperty(rejectedV2, "error").Contains("activation_id", StringComparison.Ordinal));
            Check("rejected v2 backup leaves source account intact",
                LoadInt(dbPath, "SELECT COUNT(1) FROM characters WHERE account_id=926014") == 2);

            RemoveBackupColumn(characterDump, "slot_index");
            exported.Version = 1;

            exported.Tables.Add(new AccountBackupTableDump
            {
                Name = "account_character_entries",
                Columns = new List<string> { "name" },
                Rows = new List<List<AccountBackupValue>>
                {
                    new List<AccountBackupValue> { new AccountBackupValue { Type = "text", Text = "deprecated-roster-cache" } },
                },
            });

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "INSERT INTO accounts(account_id,m_id,password_hash) VALUES(926099,'backup-conflict-owner','');");
                Exec(conn, tx, @"INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,slot_index)
VALUES(926099,926099,'backup-conflict-owner',0,0,1,0,0);");
                Exec(conn, tx, "DELETE FROM character_avatar_detail WHERE item_uid=424201;");
                Exec(conn, tx, @"INSERT INTO character_avatar_detail
(item_uid,owner_id,character_id,item_id,jewel_socket) VALUES(424201,926099,926099,930001,zeroblob(30));");
                Exec(conn, tx, "DELETE FROM character_creatures WHERE character_id=926014 AND creature_key=424277;");
                Exec(conn, tx, @"INSERT INTO character_creatures(character_id,sort_order,creature_key,field04)
VALUES(926099,77,424277,1);");
                Exec(conn, tx, @"UPDATE mailbox_messages
SET receiver_character_id=926099,
    receiver_account_id=926099,
    receiver_name='backup-conflict-owner',
    title='conflict-mail',
    body='conflict-mail-body',
    idempotency_key='conflict-mail-source',
    request_hash='conflict-mail-hash'
WHERE message_id=824242;");
                Exec(conn, tx, @"UPDATE mailbox_system_mail_audit
SET receiver_character_id=926099,
    receiver_account_id=926099,
    receiver_name='backup-conflict-owner',
    audit_reason='conflict-selftest',
    idempotency_key='conflict-audit-source',
    request_hash='conflict-audit-hash'
WHERE audit_id=824245;");
                tx.Commit();
            }

            var restored = gm.RestoreAccountBackup(exported);
            Check("RestoreAccountBackup accepts legacy backup without slot_index", IsSuccess(restored));
            Check("account restore reports v1 to v2 upgrade",
                GetIntProperty(restored, "sourceBackupVersion") == 1
                && GetIntProperty(restored, "upgradedFromVersion") == 1);
            Check("account restore remaps conflicting avatar logical UIDs",
                GetIntProperty(restored, "remappedAvatarUidCount") > 0
                && (LoadCore(dbPath, CharacterId, 1, 1)?.Value ?? 0) != 424201
                && LoadInt(dbPath, $"SELECT COUNT(*) FROM character_avatar_detail WHERE character_id={CharacterId} AND item_uid={(LoadCore(dbPath, CharacterId, 1, 1)?.Value ?? 0)}") == 1);
            Check("account restore remaps conflicting creature logical UIDs",
                GetIntProperty(restored, "remappedCreatureUidCount") > 0
                && (LoadCore(dbPath, CharacterId, 7, 0)?.Value ?? 0) != 424277
                && LoadInt(dbPath, $"SELECT COUNT(*) FROM character_creatures WHERE character_id={CharacterId} AND creature_key={(LoadCore(dbPath, CharacterId, 7, 0)?.Value ?? 0)}") == 1);
            Check("legacy account restore rebuilds unique character slots",
                LoadInt(dbPath, @"
SELECT COUNT(1)
FROM (
    SELECT slot_index
    FROM characters
    WHERE account_id=926014 AND delete_flag=0
    GROUP BY slot_index
    HAVING COUNT(1) > 1
);") == 0);
            Check("legacy account restore assigns compact slots",
                LoadInt(dbPath, "SELECT COUNT(1) FROM characters WHERE account_id=926014 AND character_id IN (926014, 926016) AND slot_index IN (0, 1)") == 2);
            Check("legacy account restore synthesizes v52 activation IDs",
                LoadInt(dbPath, @"SELECT COUNT(1) FROM character_active_quests
WHERE character_id=926014 AND quest_id=42420 AND activation_id LIKE 'legacy-active-%'") == 1
                && LoadInt(dbPath, @"SELECT COUNT(1) FROM quest_progress_event_inbox
WHERE character_id=926014 AND event_id='clone-event' AND activation_id LIKE 'legacy-inbox-%'") == 1);
            Check("account restore preserves v52 skill quest daily and expert tables",
                LoadInt(dbPath, "SELECT COUNT(1) FROM character_pvp_skills WHERE character_id=926014 AND skill_id=4343") == 1
                && LoadInt(dbPath, "SELECT COUNT(1) FROM character_quest_notify_selections WHERE character_id=926014 AND quest_id=42420") == 1
                && LoadInt(dbPath, "SELECT COUNT(1) FROM character_daily_challenge_claims WHERE character_id=926014 AND group_index=4") == 1
                && LoadInt(dbPath, "SELECT COUNT(1) FROM character_expert_job WHERE character_id=926014 AND enchanter_endurance=4242") == 1);
            Check("account restore preserves v52 account lottery progress",
                LoadInt(dbPath, @"SELECT COUNT(1) FROM account_increase_chance_lottery_progress
WHERE account_id=926014 AND item_template_id=424242 AND reward_index=3") == 1);
            Check("account restore preserves mercenary reward item relations",
                LoadInt(dbPath, @"SELECT COUNT(1) FROM mercenary_reward_outbox o
JOIN mercenary_reward_items i ON i.outbox_id=o.outbox_id
WHERE o.character_id=926014 AND o.assignment_id=4242
  AND i.ordinal=0 AND i.item_template_id=910000 AND i.item_count=7") == 1);

            var restoredMessageId = LoadLong(dbPath, @"SELECT message_id FROM mailbox_messages
WHERE receiver_character_id=926014 AND title='backup-mail-selftest'");
            var restoredAuditId = LoadLong(dbPath, @"SELECT audit_id FROM mailbox_system_mail_audit
WHERE receiver_character_id=926014 AND audit_reason='backup-selftest'");
            Check("account restore remaps conflicting mailbox message IDs",
                GetIntProperty(restored, "remappedMailboxMessageIdCount") == 1
                && restoredMessageId > 0
                && restoredMessageId != 824242
                && LoadInt(dbPath, "SELECT COUNT(1) FROM mailbox_messages WHERE message_id=824242 AND receiver_character_id=926099") == 1);
            Check("account restore preserves mailbox recipient and attachment relations",
                LoadInt(dbPath, $@"SELECT COUNT(1) FROM mailbox_recipients r
JOIN mailbox_attachments a ON a.message_id=r.message_id
WHERE r.character_id=926014 AND r.message_id={restoredMessageId}
  AND a.ordinal=0 AND a.item_template_id=910000 AND a.item_count=7") == 1);
            Check("account restore remaps audit IDs and preserves audit attachments",
                GetIntProperty(restored, "remappedMailboxAuditIdCount") == 1
                && restoredAuditId > 0
                && restoredAuditId != 824245
                && LoadInt(dbPath, $@"SELECT COUNT(1) FROM mailbox_system_mail_audit a
JOIN mailbox_system_mail_audit_attachments x ON x.audit_id=a.audit_id
WHERE a.audit_id={restoredAuditId} AND a.message_id={restoredMessageId}
  AND x.ordinal=0 AND x.item_template_id=910000 AND x.item_count=7") == 1);

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "DELETE FROM characters WHERE character_id=926016;");
                Exec(conn, tx, "DELETE FROM characters WHERE character_id=926099;");
                Exec(conn, tx, "DELETE FROM accounts WHERE account_id=926099;");
                tx.Commit();
            }
        }

        private static void RemoveBackupColumn(AccountBackupTableDump dump, string columnName)
        {
            if (dump == null)
                return;
            var index = dump.Columns.FindIndex(column =>
                column.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;
            dump.Columns.RemoveAt(index);
            foreach (var row in dump.Rows)
            {
                if (index < row.Count)
                    row.RemoveAt(index);
            }
        }

        private static void CheckDeleteCharacterSeedFallback(GmService gm, string dbPath)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, @"
INSERT INTO characters(character_id, account_id, name, job, grow_type, level, exp)
VALUES(926015, 926014, 'character-delete-seed-fallback', 0, 0, 1, 0);");
                Exec(conn, tx, "UPDATE get_userinfo_template SET seed_character_id = 926014 WHERE id = 1;");
                Exec(conn, tx, @"INSERT OR REPLACE INTO character_avatar_detail
(item_uid,owner_id,character_id,item_id,jewel_socket) VALUES(92926014,926014,926014,930001,zeroblob(30));");
                Exec(conn, tx, @"INSERT INTO inventory_audit_log_v2
(owner_scope,owner_id,character_id,account_id,action_name,payload_json)
VALUES('character',926014,926014,926014,'delete-cleanup-selftest','{}');");
                tx.Commit();
            }

            var activeAssignmentResult = gm.DeleteCharacterPermanently(CharacterId, "删除角色");
            Check("DeleteCharacterPermanently rejects active mercenary assignments",
                !IsSuccess(activeAssignmentResult)
                && GetStringProperty(activeAssignmentResult, "error").Contains("佣兵出战", StringComparison.Ordinal));
            Check("active mercenary rejection preserves the character",
                LoadInt(dbPath, "SELECT COUNT(1) FROM characters WHERE character_id=926014") == 1);

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "DELETE FROM account_mercenary_assignments WHERE character_id=926014;");
                tx.Commit();
            }

            var pendingRewardResult = gm.DeleteCharacterPermanently(CharacterId, "删除角色");
            Check("DeleteCharacterPermanently rejects pending mercenary reward delivery",
                !IsSuccess(pendingRewardResult)
                && GetStringProperty(pendingRewardResult, "error").Contains("奖励邮件", StringComparison.Ordinal));
            Check("pending mercenary reward rejection preserves the character",
                LoadInt(dbPath, "SELECT COUNT(1) FROM characters WHERE character_id=926014") == 1);

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, @"UPDATE mercenary_reward_outbox
SET delivery_status='delivered', delivered_at=CURRENT_TIMESTAMP
WHERE character_id=926014;");
                tx.Commit();
            }

            var result = gm.DeleteCharacterPermanently(CharacterId, "删除角色");
            Check("DeleteCharacterPermanently accepts delivered mercenary reward history", IsSuccess(result));
            Check("deleted character row removed",
                LoadInt(dbPath, "SELECT COUNT(1) FROM characters WHERE character_id=926014") == 0);
            Check("delete replacement seed uses same account survivor",
                LoadInt(dbPath, "SELECT seed_character_id FROM get_userinfo_template WHERE id=1") == 926015);
            Check("same account survivor remains active",
                LoadInt(dbPath, "SELECT COUNT(1) FROM characters WHERE character_id=926015 AND delete_flag=0") == 1);
            Check("delete removes avatar detail rows",
                LoadInt(dbPath, "SELECT COUNT(1) FROM character_avatar_detail WHERE character_id=926014 OR owner_id=926014") == 0);
            Check("delete removes inventory audit v2 rows",
                LoadInt(dbPath, "SELECT COUNT(1) FROM inventory_audit_log_v2 WHERE character_id=926014 OR (owner_scope='character' AND owner_id=926014)") == 0);
            Check("delete removes dungeon effect outbox rows",
                LoadInt(dbPath, "SELECT COUNT(1) FROM dungeon_persistent_effect_outbox WHERE character_id=926014") == 0);
            Check("delete removes mercenary reward outbox rows",
                LoadInt(dbPath, "SELECT COUNT(1) FROM mercenary_reward_outbox WHERE character_id=926014") == 0);
            Check("delete removes mercenary assignments",
                LoadInt(dbPath, "SELECT COUNT(1) FROM account_mercenary_assignments WHERE character_id=926014") == 0);
        }

        private static PvfIndexService.ItemEntry FindInventoryAvatarCandidate(
            PvfIndexService pvfIndex,
            out int optionValue,
            out int durationDays)
        {
            optionValue = 0;
            durationDays = 0;
            foreach (var candidate in pvfIndex.AllItems.Where(item =>
                         string.Equals(item.Kind, "equipment", StringComparison.OrdinalIgnoreCase)))
            {
                var metadata = ItemMetadataResolver.Resolve(candidate.Id);
                if (metadata == null
                    || !ItemMetadataResolver.IsAvatarMetadata(metadata)
                    || !ItemMetadataResolver.TryLoadEquipmentFile(candidate.Id, out var equipment)
                    || !AvatarGrantPolicy.IsUsableByJob(equipment.UsableJob, 0))
                {
                    continue;
                }

                var avatarMetadata = AvatarEquipmentMetadataReader.Read(equipment);
                var options = AvatarGrantPolicy.ResolveOptions(
                    equipment.EquipmentType,
                    equipment.Grade,
                    avatarMetadata.SelectAbilities,
                    0,
                    avatarMetadata.AbilityCaseIndex);
                var positiveDuration = AvatarDurationResolver.Resolve(candidate.Id)
                    .FirstOrDefault(value => value.DurationDays > 0)
                    ?.DurationDays ?? 0;
                if (options.Count == 0 || positiveDuration <= 0)
                    continue;

                optionValue = options[0].Value;
                durationDays = positiveDuration;
                return candidate;
            }
            return null;
        }

        private static void SeedDeliveryModeCharacter(string dbPath, int characterId)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $@"
INSERT INTO characters(character_id, account_id, name, job, grow_type, level, exp, bonus_sp, bonus_tp, slot_index)
VALUES({characterId}, {AccountId}, 'delivery-mode-selftest', 0, 0, 60, 0, 0, 0, 8);");
                tx.Commit();
            }
        }

        private static List<(int ListType, int Slot, int Count)> LoadCoreRows(
            string dbPath,
            int characterId,
            int itemId)
        {
            var rows = new List<(int, int, int)>();
            using var conn = Open(dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT list_type, slot_index, item_core
FROM character_new_items
WHERE character_id=@cid;";
            cmd.Parameters.AddWithValue("@cid", characterId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(2))
                    continue;
                var bytes = reader.GetValue(2) as byte[];
                if (bytes == null || bytes.Length != ItemCore.Size)
                    continue;
                var core = ItemCore.FromBytes(bytes);
                if (core.ItemId == itemId)
                    rows.Add((reader.GetInt32(0), reader.GetInt32(1), core.Count));
            }
            return rows;
        }

        private static void SeedOccupiedStackRange(
            string dbPath,
            int characterId,
            int start,
            int end,
            int keepSlot,
            byte itemKind)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                for (var slot = start; slot <= end; slot++)
                {
                    if (slot == keepSlot)
                        continue;
                    var core = ItemCore.Create(itemKind, 990000 + slot);
                    core.Count = 1;
                    using var command = conn.CreateCommand();
                    command.Transaction = tx;
                    command.CommandText = @"
INSERT OR REPLACE INTO character_new_items
    (owner_scope, owner_id, character_id, list_type, slot_index, item_core)
VALUES ('character', @owner, @owner, 0, @slot, @core);";
                    command.Parameters.AddWithValue("@owner", characterId);
                    command.Parameters.AddWithValue("@slot", slot);
                    command.Parameters.AddWithValue("@core", core.ToBytes());
                    command.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }

        private static void DeleteStackRange(string dbPath, int characterId, int start, int end)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $@"
DELETE FROM character_new_items
WHERE character_id={characterId} AND list_type=0
  AND slot_index BETWEEN {start} AND {end};");
                tx.Commit();
            }
        }

        private static void CleanupDeliveryModeCharacter(string dbPath, int characterId)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, $@"
DELETE FROM mailbox_system_mail_audit_attachments
WHERE audit_id IN (
    SELECT audit_id FROM mailbox_system_mail_audit
    WHERE message_id IN (SELECT message_id FROM mailbox_messages WHERE receiver_character_id={characterId})
); ");
                Exec(conn, tx, $@"
DELETE FROM mailbox_system_mail_audit
WHERE message_id IN (SELECT message_id FROM mailbox_messages WHERE receiver_character_id={characterId});");
                Exec(conn, tx, $@"
DELETE FROM mailbox_attachments
WHERE message_id IN (SELECT message_id FROM mailbox_messages WHERE receiver_character_id={characterId});");
                Exec(conn, tx, $@"
DELETE FROM mailbox_recipients
WHERE message_id IN (SELECT message_id FROM mailbox_messages WHERE receiver_character_id={characterId})
   OR character_id={characterId};");
                Exec(conn, tx, $"DELETE FROM mailbox_messages WHERE receiver_character_id={characterId};");
                Exec(conn, tx, $"DELETE FROM character_avatar_detail WHERE character_id={characterId} OR owner_id={characterId};");
                Exec(conn, tx, $"DELETE FROM character_creatures WHERE character_id={characterId};");
                Exec(conn, tx, $"DELETE FROM inventory_audit_log_v2 WHERE character_id={characterId} OR (owner_scope='character' AND owner_id={characterId});");
                Exec(conn, tx, $"DELETE FROM character_new_items WHERE character_id={characterId};");
                Exec(conn, tx, $"DELETE FROM character_container_state WHERE character_id={characterId};");
                Exec(conn, tx, $"DELETE FROM characters WHERE character_id={characterId};");
                tx.Commit();
            }
        }

        private static void SeedCharacter(string dbPath)
        {
            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "INSERT INTO accounts(account_id, m_id, password_hash) VALUES(926014, 'character-mutation-selftest', '');");
                Exec(conn, tx, @"
INSERT INTO characters(character_id, account_id, name, job, grow_type, level, exp, bonus_sp, bonus_tp)
VALUES(926014, 926014, 'character-mutation-selftest', 0, 0, 60, 0, 10, 3);");
                Exec(conn, tx, "INSERT INTO character_subtype1_fields(character_id) VALUES(926014);");
                Exec(conn, tx, "INSERT INTO character_init_flags(character_id) VALUES(926014);");
                Exec(conn, tx, @"
INSERT INTO character_skills(character_id, page_index, slot, skill_id, level) VALUES
(926014, 0, 5, 999, 1),
(926014, 1, 5, 999, 1);");
                Exec(conn, tx, "INSERT INTO character_pvp_skill_state(character_id) VALUES(926014);");
                Exec(conn, tx, @"INSERT INTO character_pvp_skills
(character_id,page_index,slot,skill_id,level) VALUES(926014,0,5,5555,3);");
                tx.Commit();
            }
        }

        private static string ResolveLatestServerPvf()
        {
            foreach (var root in EnumerateSearchRoots())
            {
                foreach (var path in EnumerateServerPvfCandidates(root))
                {
                    if (File.Exists(path))
                        return path;
                }

                var candidates = new[]
                {
                    Path.Combine(root, "Codes", "ServerS4A12_260716", "Server", "DfoServer", "bin", "Debug", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "Codes", "ServerS4A12_260716", "Server", "DfoServer", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "Codes", "ServerS4A12_260716", "dist", "linux-x64", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "Codes", "ServerS4A12_260714", "Server", "DfoServer", "bin", "Debug", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "Codes", "ServerS4A12_260714", "Server", "DfoServer", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "Codes", "ServerS4A12_260714", "dist", "linux-x64", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "ServerS4A12_260714", "Server", "DfoServer", "bin", "Debug", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "ServerS4A12_260714", "Server", "DfoServer", "Data", "Pvf", "Script.pvf"),
                    Path.Combine(root, "ServerS4A12_260714", "dist", "linux-x64", "Data", "Pvf", "Script.pvf"),
                };
                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                        return path;
                }
            }
            return null;
        }

        private static IEnumerable<string> EnumerateServerPvfCandidates(string root)
        {
            var baseDirs = new[]
            {
                root,
                Path.Combine(root, "Codes"),
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var baseDir in baseDirs)
            {
                if (!Directory.Exists(baseDir))
                    continue;

                foreach (var serverDir in Directory.GetDirectories(baseDir, "ServerS4A12_*")
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var path in new[]
                    {
                        Path.Combine(serverDir, "dist", "linux-x64", "Data", "Pvf", "Script.pvf"),
                        Path.Combine(serverDir, "Server", "DfoServer", "bin", "Release", "win-x64", "Data", "Pvf", "Script.pvf"),
                        Path.Combine(serverDir, "Server", "DfoServer", "bin", "Release", "linux-x64", "Data", "Pvf", "Script.pvf"),
                        Path.Combine(serverDir, "Server", "DfoServer", "bin", "Debug", "Data", "Pvf", "Script.pvf"),
                        Path.Combine(serverDir, "Server", "DfoServer", "Data", "Pvf", "Script.pvf"),
                    })
                    {
                        if (seen.Add(path))
                            yield return path;
                    }
                }
            }
        }

        private static string[] EnumerateSearchRoots()
        {
            var roots = new List<string>();
            AddRoot(roots, Directory.GetCurrentDirectory());
            AddRoot(roots, AppContext.BaseDirectory);

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                AddRoot(roots, dir.FullName);

            return roots.ToArray();
        }

        private static void AddRoot(List<string> roots, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }
            if (!roots.Contains(path))
                roots.Add(path);
        }

        private static void WaitForIndex(PvfIndexService index)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!index.IsReady && string.IsNullOrWhiteSpace(index.BuildError) && DateTime.UtcNow < deadline)
                Thread.Sleep(100);
            Check("PVF index ready", index.IsReady && string.IsNullOrWhiteSpace(index.BuildError), index.BuildError);
        }

        private static SqliteConnection Open(string dbPath)
        {
            var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
            conn.Open();
            return conn;
        }

        private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
        {
            using (var cmd = new SqliteCommand(sql, conn, tx))
                cmd.ExecuteNonQuery();
        }

        private static long LoadLastInsertId(SqliteConnection conn, SqliteTransaction tx)
        {
            using (var cmd = new SqliteCommand("SELECT last_insert_rowid();", conn, tx))
                return Convert.ToInt64(cmd.ExecuteScalar());
        }

        private static int LoadInt(string dbPath, string sql)
        {
            using (var conn = Open(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static ItemCore LoadCore(string dbPath, int characterId, int listType, int slot)
        {
            using var conn = Open(dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT item_core FROM character_new_items
WHERE character_id=@cid AND list_type=@list AND slot_index=@slot LIMIT 1;";
            cmd.Parameters.AddWithValue("@cid", characterId);
            cmd.Parameters.AddWithValue("@list", listType);
            cmd.Parameters.AddWithValue("@slot", slot);
            var value = cmd.ExecuteScalar() as byte[];
            return value != null && value.Length == ItemCore.Size ? ItemCore.FromBytes(value) : null;
        }

        private static int CountCoreItem(string dbPath, int characterId, int itemId, int? listType = null)
        {
            using var conn = Open(dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT item_core FROM character_new_items WHERE character_id=@cid"
                + (listType.HasValue ? " AND list_type=@list" : string.Empty) + ";";
            cmd.Parameters.AddWithValue("@cid", characterId);
            if (listType.HasValue) cmd.Parameters.AddWithValue("@list", listType.Value);
            var count = 0;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var bytes = reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0);
                if (bytes != null && bytes.Length == ItemCore.Size && ItemCore.FromBytes(bytes).ItemId == itemId) count++;
            }
            return count;
        }

        private static int CountCoreKind(string dbPath, int characterId, byte itemKind, int? listType = null)
        {
            using var conn = Open(dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT item_core FROM character_new_items WHERE character_id=@cid"
                + (listType.HasValue ? " AND list_type=@list" : string.Empty) + ";";
            cmd.Parameters.AddWithValue("@cid", characterId);
            if (listType.HasValue) cmd.Parameters.AddWithValue("@list", listType.Value);
            var count = 0;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var bytes = reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0);
                if (bytes != null && bytes.Length == ItemCore.Size && ItemCore.FromBytes(bytes).ItemKind == itemKind) count++;
            }
            return count;
        }

        private static long LoadLong(string dbPath, string sql)
        {
            using (var conn = Open(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        private static ItemCore LoadMailAttachmentCore(string dbPath, int itemId)
        {
            using var conn = Open(dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT item_core FROM mailbox_attachments
WHERE item_template_id=@item ORDER BY attachment_id DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("@item", itemId);
            var value = cmd.ExecuteScalar();
            return value is byte[] bytes && bytes.Length == ItemCore.Size
                ? ItemCore.FromBytes(bytes)
                : null;
        }

        private static int GetIntProperty(object value, string propertyName)
        {
            if (value == null)
                return 0;
            var prop = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return prop == null ? 0 : Convert.ToInt32(prop.GetValue(value));
        }

        private static int GetListedItemCount(object value, int listType, int slot)
        {
            if (value == null)
                return 0;
            var itemsProperty = value.GetType().GetProperty("items", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (!(itemsProperty?.GetValue(value) is System.Collections.IEnumerable items))
                return 0;
            foreach (var item in items)
            {
                if (GetIntProperty(item, "listType") == listType && GetIntProperty(item, "slot") == slot)
                    return GetIntProperty(item, "count");
            }
            return 0;
        }

        private static object FindListedItem(
            object value,
            string collectionProperty,
            string itemIdProperty,
            int itemId)
        {
            if (value == null)
                return null;
            var property = value.GetType().GetProperty(
                collectionProperty,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (!(property?.GetValue(value) is System.Collections.IEnumerable items))
                return null;
            foreach (var item in items)
            {
                if (GetIntProperty(item, itemIdProperty) == itemId)
                    return item;
            }
            return null;
        }

        private static bool GetBoolProperty(object value, string propertyName)
        {
            if (value == null)
                return false;
            var prop = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return prop != null && Convert.ToBoolean(prop.GetValue(value));
        }

        private static string GetStringProperty(object value, string propertyName)
        {
            if (value == null) return null;
            var prop = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return prop?.GetValue(value)?.ToString();
        }

        private static bool IsSuccess(object result)
        {
            if (result == null)
                return false;
            var prop = result.GetType().GetProperty("success", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return prop != null && Convert.ToBoolean(prop.GetValue(result));
        }

        private static void Check(string name, bool condition, string error = null)
        {
            if (condition)
            {
                Console.WriteLine("PASS " + name);
                return;
            }

            _failures++;
            Console.Error.WriteLine("FAIL " + name + (string.IsNullOrWhiteSpace(error) ? string.Empty : ": " + error));
        }

    }
}
