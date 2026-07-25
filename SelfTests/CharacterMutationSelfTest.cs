using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DfoGmTool.ServerCore.Game.Dungeon;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.Premium;
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

                SqliteDatabaseBootstrap.Initialize(tempDb, schema);
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
                CheckJobGrowAndSkillReset(gm, tempDb);
                CheckUnlockExtraEquipmentSlots(gm, tempDb);
                CheckPetGrantPersistence(gm, pvfIndex, tempDb);
                CheckNameTagGrantPersistence(gm, pvfIndex, tempDb);
                CheckAccountPremiumGrantPersistence(gm, tempDb, pvfIndex);
                CheckTitleQuestSynchronization(gm, pvfIndex, tempDb);
                CheckCloneOptionCoverage(gm, pvfIndex, tempDb);
                CheckCloneCharacterSlotIsolation(gm, tempDb);
                CheckAccountBackupRestoreSlotCompatibility(gm, tempDb);
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

        private static void CheckJobGrowAndSkillReset(GmService gm, string dbPath)
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
            Check("awakening grant skill 33 exists", skill33 > 0, "got " + skill33);
            Check("awakening grant skill 197 exists", skill197 > 0, "got " + skill197);
            Check("default promoted quest flag set", flag101 == 1, "got " + flag101);
            Check("skill points reset to full after class change",
                GetIntProperty(gm.GetSpTp(CharacterId), "totalSp") == GetIntProperty(gm.GetSpTp(CharacterId), "remainingSp"));

            var invalid = gm.SetGrowTypeFixed(CharacterId, 0, 0, 1);
            Check("invalid awakening without first grow is rejected", !IsSuccess(invalid));
        }

        private static void CheckUnlockExtraEquipmentSlots(GmService gm, string dbPath)
        {
            gm.SetLevel(CharacterId, 70);
            var result = gm.UnlockExtraEquipmentSlots(CharacterId);
            Check("UnlockExtraEquipmentSlots returns success", IsSuccess(result));
            Check("left and right equipment slots persist as unlocked",
                LoadInt(dbPath, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=926014") == 3);
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

            var qualityGrant = gm.GiveItem(
                CharacterId,
                qualityArtifact.Id,
                1,
                new ItemGrantOptions { QualityMode = ItemQualityMode.Random },
                pvfIndex);
            Check("quality pet equipment grant succeeds", IsSuccess(qualityGrant));
            var qualitySlot = GetIntProperty(qualityGrant, "slot");
            var firstQualitySeed = LoadCore(dbPath, CharacterId, 7, qualitySlot)?.Value ?? 0;
            Check("quality pet equipment persists a quality seed", firstQualitySeed != 0);

            var configured = gm.ConfigureInventoryItem(CharacterId, new InventoryItemConfigureRequest
            {
                ListType = (int)InventoryListType.Pet,
                Slot = qualitySlot,
                Options = new ItemGrantOptions { QualityMode = ItemQualityMode.Top },
            }, pvfIndex);
            Check("inventory can reconfigure quality pet equipment", IsSuccess(configured));
            Check("inventory pet equipment quality uses the protocol instance field",
                (LoadCore(dbPath, CharacterId, 7, qualitySlot)?.Value ?? 0)
                == unchecked((int)ItemQuality.TopQualitySeed));

            var directGrant = gm.GiveItem(CharacterId, directArtifact.Id, 1, null, pvfIndex);
            Check("pet equipment without quality grants without options", IsSuccess(directGrant));
            var directSlot = GetIntProperty(directGrant, "slot");
            Check("pet equipment without quality keeps instance value empty",
                (LoadCore(dbPath, CharacterId, 7, directSlot)?.Value ?? -1) == 0);

            var creatureGrant = gm.GiveItem(CharacterId, creature.Id, 1, null, pvfIndex);
            Check("creature grants without configuration", IsSuccess(creatureGrant));
            var creatureSlot = GetIntProperty(creatureGrant, "slot");
            var creatureCore = LoadCore(dbPath, CharacterId, 7, creatureSlot);
            Check("creature core references its detail row",
                creatureCore != null && creatureCore.Value > 0
                    && LoadInt(dbPath, $"SELECT COUNT(*) FROM character_creatures WHERE character_id={CharacterId} AND creature_key={creatureCore.Value}") == 1,
                $"item={creature.Id}, slot={creatureSlot}, uid={creatureCore?.Value ?? 0}");
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
            var firstGrant = gm.GiveItem(
                CharacterId,
                nameTags[0].Id,
                1,
                new ItemGrantOptions { ExpirationDays = 30 },
                pvfIndex);
            Check("name tag grant succeeds", IsSuccess(firstGrant));
            Check("name tag grant reports equipped slot 28", GetIntProperty(firstGrant, "slot") == 28);
            Check("name tag is not inserted into character inventory",
                CountCoreItem(dbPath, CharacterId, nameTags[0].Id) == 0);
            Check("name tag persists in dedicated new state",
                LoadInt(dbPath, $"SELECT item_id FROM character_name_tag_state WHERE character_id={CharacterId}") == nameTags[0].Id);
            var firstExpire = LoadInt(dbPath,
                $"SELECT expire_time FROM character_name_tag_state WHERE character_id={CharacterId}");
            Check("name tag initial expiry is about 30 days",
                firstExpire > now + 29 * 86400L && firstExpire <= now + 31 * 86400L,
                "expire=" + firstExpire);
            Check("name tag no longer writes old equipped rows",
                LoadInt(dbPath, $"SELECT COUNT(*) FROM character_equipped_entries WHERE character_id={CharacterId} AND slot=28") == 0);

            var renewGrant = gm.GiveItem(
                CharacterId,
                nameTags[0].Id,
                1,
                new ItemGrantOptions { ExpirationDays = 15 },
                pvfIndex);
            Check("same name tag renew succeeds", IsSuccess(renewGrant));
            var renewedExpire = LoadInt(dbPath,
                $"SELECT expire_time FROM character_name_tag_state WHERE character_id={CharacterId}");
            Check("same name tag stacks expiry", renewedExpire >= firstExpire + 14 * 86400);

            if (nameTags.Length > 1)
            {
                var replaceGrant = gm.GiveItem(
                    CharacterId,
                    nameTags[1].Id,
                    1,
                    new ItemGrantOptions { ExpirationDays = 5 },
                    pvfIndex);
                Check("different name tag replace succeeds", IsSuccess(replaceGrant));
                Check("different name tag replaces slot 28 item",
                    LoadInt(dbPath, $"SELECT item_id FROM character_name_tag_state WHERE character_id={CharacterId}") == nameTags[1].Id);
                var replacedExpire = LoadInt(dbPath,
                    $"SELECT expire_time FROM character_name_tag_state WHERE character_id={CharacterId}");
                Check("different name tag resets expiry instead of stacking",
                    replacedExpire < renewedExpire && replacedExpire > now + 4 * 86400L);
            }
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
            var firstGrant = gm.GiveItem(CharacterId, entry.ItemCode, 1, null, pvfIndex);
            Check("account premium contract grant succeeds", IsSuccess(firstGrant));
            Check("account premium contract reports activation", GetBoolProperty(firstGrant, "premiumActivated"));
            Check("account premium contract reports premium type", GetIntProperty(firstGrant, "premiumType") == entry.PremiumType);
            Check("account premium contract does not enter character inventory",
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_items WHERE character_id={CharacterId} AND item_template_id={entry.ItemCode}") == 0);
            var firstExpire = LoadLong(dbPath,
                $"SELECT end_time FROM account_premiums WHERE account_id={AccountId} AND premium_type={entry.PremiumType}");
            Check("account premium contract persists to account_premiums",
                firstExpire > now + (entry.DurationDays * 86400L) - 5,
                "expire=" + firstExpire);

            var secondGrant = gm.GiveItem(CharacterId, entry.ItemCode, 2, null, pvfIndex);
            Check("account premium contract renew succeeds", IsSuccess(secondGrant));
            var renewedExpire = LoadLong(dbPath,
                $"SELECT end_time FROM account_premiums WHERE account_id={AccountId} AND premium_type={entry.PremiumType}");
            Check("account premium contract renew extends existing expiry",
                renewedExpire >= firstExpire + entry.DurationDays * 2L * 86400L - 5,
                $"first={firstExpire}, renewed={renewedExpire}, days={entry.DurationDays}");
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
                && !item.UsableJob.Replace("`", string.Empty).Trim().Equals("[all]", StringComparison.OrdinalIgnoreCase));
            Check("PVF contains job-restricted equipment for clone stripping", restrictedEquipment != null);
            SeedCloneOptionRows(dbPath, restrictedEquipment);
            Check("inventory list reads the service-effective pet consumable count",
                GetListedItemCount(gm.ListItems(CharacterId, pvfIndex), 7, 189) == 37);

            var basicOnlyId = CloneForOption(gm, "clbasic", "basic");
            Check("Clone basic-only does not copy active quests",
                basicOnlyId > 0 && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_active_quests WHERE character_id={basicOnlyId}") == 0);
            Check("Clone basic-only does not copy cleared quest flags",
                basicOnlyId > 0 && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_invisible_falgs WHERE character_id={basicOnlyId}") == 0);
            Check("Clone basic-only dynamically copies new character-owned tables",
                basicOnlyId > 0 && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_dynamic_clone_selftest WHERE character_id={basicOnlyId} AND marker='dynamic-base'") == 1);
            Check("dynamic clone table regenerates integer auto primary keys",
                basicOnlyId > 0 && LoadInt(dbPath, $@"
SELECT COUNT(1)
FROM character_dynamic_clone_selftest target
JOIN character_dynamic_clone_selftest source ON source.character_id=926014 AND source.marker='dynamic-base'
WHERE target.character_id={basicOnlyId} AND target.marker='dynamic-base' AND target.row_id<>source.row_id;") == 1);
            DeleteCharacterRow(dbPath, basicOnlyId);

            CheckCloneOption(gm, dbPath, "skills", "clskil", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_skills WHERE character_id={id} AND skill_id=4242") > 0
                && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_hotkey_slots WHERE character_id={id} AND slot_index=44") > 0);
            CheckCloneOption(gm, dbPath, "quests", "clques", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_active_quests WHERE character_id={id} AND quest_id=42420") > 0
                && LoadInt(dbPath, $"SELECT COUNT(1) FROM character_invisible_falgs WHERE character_id={id} AND slot_index=2424") > 0);
            CheckCloneOption(gm, dbPath, "titlebook", "cltitl", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_new_titlebook WHERE character_id={id} AND category=0 AND slot_index=42") > 0);
            CheckCloneOption(gm, dbPath, "dungeon", "cldung", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_dungeon_permissions WHERE character_id={id} AND dungeon_id=4242") > 0);
            CheckCloneOption(gm, dbPath, "daily", "cldail", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_daily_counters WHERE character_id={id} AND counter_key='clone_option_daily'") > 0);
            CheckCloneOption(gm, dbPath, "wallet", "clwall", id => HasClonedItem(dbPath, id, 0, 0, "stackable"));
            CheckCloneOption(gm, dbPath, "quickSlots", "clquik", id => HasClonedItem(dbPath, id, 29, 0, "stackable"));
            CheckCloneOption(gm, dbPath, "mainEquipment", "cleqip", id => HasClonedItem(dbPath, id, 0, 9, "equipment"));
            CheckCloneOption(gm, dbPath, "consumables", "clcons", id => HasClonedItem(dbPath, id, 0, 65, "stackable"));
            CheckCloneOption(gm, dbPath, "materials", "clmatr", id => HasClonedItem(dbPath, id, 0, 121, "stackable"));
            CheckCloneOption(gm, dbPath, "questItems", "clqitm", id => HasClonedItem(dbPath, id, 0, 177, "stackable"));
            CheckCloneOption(gm, dbPath, "expertMaterials", "clexpm", id => HasClonedItem(dbPath, id, 0, 233, "stackable"));
            CheckCloneOption(gm, dbPath, "emblems", "clembl", id => HasClonedItem(dbPath, id, 0, 289, "stackable"));
            CheckCloneOption(gm, dbPath, "specialMaterials", "clsplm", id => HasClonedItem(dbPath, id, 0, 345, "stackable"));
            CheckCloneOption(gm, dbPath, "mainOther", "clothr", id => HasClonedItem(dbPath, id, 0, 360, "stackable"));
            CheckCloneOption(gm, dbPath, "personalCargo", "clpcar", id => HasClonedItem(dbPath, id, 2, 0, "stackable"));
            CheckCloneOption(gm, dbPath, "equipped", "cleqed", id =>
                HasClonedItem(dbPath, id, 3, 12, "equipment")
                && (restrictedEquipment == null
                    || (CountCoreItem(dbPath, id, restrictedEquipment.Id, listType: 3) == 0
                        && CountCoreItem(dbPath, id, restrictedEquipment.Id, listType: 0) == 1)));
            CheckCloneOption(gm, dbPath, "avatars", "clavat", id => HasClonedItem(dbPath, id, 1, 1, "avatar"));
            CheckCloneOption(gm, dbPath, "pets", "clpets", id =>
                HasClonedItem(dbPath, id, 7, 0, "pet")
                && LoadInt(dbPath, $"SELECT creature_key FROM character_creatures WHERE character_id={id} AND sort_order=77") == 424277);
            CheckCloneOption(gm, dbPath, "petEquipment", "clpequ", id => HasClonedItem(dbPath, id, 7, 140, "equipment"));
            CheckCloneOption(gm, dbPath, "petConsumables", "clpcon", id =>
                HasClonedItem(dbPath, id, 7, 189, "pet")
                && (LoadCore(dbPath, id, 7, 189)?.Count ?? 0) == 37
                && GetListedItemCount(gm.ListItems(id, pvfIndex), 7, 189) == 37);
            CheckCloneOption(gm, dbPath, "locks", "cllock", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_item_locks WHERE character_id={id} AND equipment_lock_id=4242") > 0);
            CheckCloneOption(gm, dbPath, "misc", "clmisc", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM character_item_values WHERE character_id={id} AND list_kind='clone_option'") > 0);
            CheckCloneOption(gm, dbPath, "audit", "claudi", id =>
                LoadInt(dbPath, $"SELECT COUNT(1) FROM item_audit_log WHERE character_id={id} AND action_name='clone_option_audit'") > 0);
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

        private static void SeedCloneOptionRows(string dbPath, PvfIndexService.ItemEntry restrictedEquipment)
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
                Exec(conn, tx, "INSERT OR REPLACE INTO character_skills(character_id, page_index, slot, skill_id, level) VALUES(926014, 0, 44, 4242, 1);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_hotkey_slots(character_id, slot_index, hotkey_value) VALUES(926014, 44, 4242);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_active_quests(character_id, slot, quest_id, trigger_value) VALUES(926014, 44, 42420, 7);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_invisible_falgs(character_id, slot_index, flag_value) VALUES(926014, 2424, 1);");
                SeedNewTitleBookItem(conn, tx, 0, 42, 904242);
                Exec(conn, tx, "INSERT OR REPLACE INTO character_dungeon_permissions(character_id, sort_order, dungeon_id, clear_state) VALUES(926014, 42, 4242, 4);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_daily_counters(character_id, counter_key, period, value) VALUES(926014, 'clone_option_daily', 'day', 1);");
                SeedCloneOptionItem(conn, tx, 3, 12, 424212, "equipment");
                if (restrictedEquipment != null)
                {
                    SeedCloneOptionItem(conn, tx, 3, 11, restrictedEquipment.Id, "equipment");
                }
                Exec(conn, tx, "INSERT OR REPLACE INTO character_creatures(character_id, sort_order, creature_key, field04) VALUES(926014, 77, 424277, 1);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_item_locks(character_id, equipment_lock_id, inventory_list_type, slot, state) VALUES(926014, 4242, 0, 9, 1);");
                Exec(conn, tx, "INSERT OR REPLACE INTO character_item_values(character_id, list_kind, sort_order, item_id, value) VALUES(926014, 'clone_option', 1, 4242, 9);");
                Exec(conn, tx, "INSERT INTO item_audit_log(owner_scope, owner_id, character_id, action_name, list_type, slot_index, item_template_id, delta_stack_count, payload_json) VALUES('character', 926014, 926014, 'clone_option_audit', 0, 0, 4242, 1, '{}');");

                SeedCloneOptionItem(conn, tx, 0, 0, 910000, "stackable");
                SeedCloneOptionItem(conn, tx, 29, 0, 910003, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 9, 910009, "equipment");
                SeedCloneOptionItem(conn, tx, 0, 65, 910065, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 121, 910121, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 177, 910177, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 233, 910233, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 289, 910289, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 345, 910345, "stackable");
                SeedCloneOptionItem(conn, tx, 0, 360, 910360, "stackable");
                SeedCloneOptionItem(conn, tx, 2, 0, 920000, "stackable");
                SeedCloneOptionItem(conn, tx, 1, 1, 930001, "avatar");
                SeedCloneOptionItem(conn, tx, 7, 0, 970000, "pet");
                SeedCloneOptionItem(conn, tx, 7, 140, 970140, "equipment");
                SeedCloneOptionItem(conn, tx, 7, 189, 970189, "pet", 37);
                tx.Commit();
            }
        }

        private static void SeedCloneOptionItem(SqliteConnection conn, SqliteTransaction tx, int listType, int slot, int itemId, string kind, int count = 1)
        {
            byte coreKind;
            if (listType == 1) coreKind = ItemCore.KindAvatar;
            else if (listType == 7 && slot <= 139) coreKind = ItemCore.KindCreature;
            else if (listType == 7 && slot <= 188) coreKind = ItemCore.KindCreatureEquipment;
            else if (listType == 7) coreKind = ItemCore.KindCreatureConsumable;
            else if (listType == 3 || (listType == 0 && slot >= 9 && slot <= 64)) coreKind = ItemCore.KindEquipment;
            else if (listType == 0 && slot == 0) coreKind = ItemCore.KindSpecialMaterial;
            else if (listType == 0 && slot >= 121 && slot <= 176) coreKind = ItemCore.KindMaterial;
            else if (listType == 0 && slot >= 177 && slot <= 232) coreKind = ItemCore.KindQuest;
            else if (listType == 0 && slot >= 233 && slot <= 288) coreKind = ItemCore.KindExpertJobMaterial;
            else if (listType == 0 && slot >= 289) coreKind = ItemCore.KindAvatarEmblem;
            else coreKind = ItemCore.KindConsumable;
            var core = ItemCore.Create(coreKind, itemId);
            if (coreKind == ItemCore.KindCreature) core.Value = 77;
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
                Exec(conn, tx, @"INSERT OR REPLACE INTO character_avatar_detail
(item_uid,owner_id,character_id,item_id,jewel_socket) VALUES(424201,926014,926014,930001,zeroblob(30));");
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

            var slotIndex = characterDump.Columns.FindIndex(c => c.Equals("slot_index", StringComparison.OrdinalIgnoreCase));
            Check("current account backup captures slot_index", slotIndex >= 0);
            if (slotIndex >= 0)
            {
                characterDump.Columns.RemoveAt(slotIndex);
                foreach (var row in characterDump.Rows)
                    row.RemoveAt(slotIndex);
            }

            exported.Tables.Add(new AccountBackupTableDump
            {
                Name = "account_character_entries",
                Columns = new List<string> { "name" },
                Rows = new List<List<AccountBackupValue>>
                {
                    new List<AccountBackupValue> { new AccountBackupValue { Type = "text", Text = "deprecated-roster-cache" } },
                },
            });

            var restored = gm.RestoreAccountBackup(exported);
            Check("RestoreAccountBackup accepts legacy backup without slot_index", IsSuccess(restored));
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

            using (var conn = Open(dbPath))
            using (var tx = conn.BeginTransaction())
            {
                Exec(conn, tx, "DELETE FROM characters WHERE character_id=926016;");
                tx.Commit();
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
                tx.Commit();
            }

            var result = gm.DeleteCharacterPermanently(CharacterId, "删除角色");
            Check("DeleteCharacterPermanently returns success", IsSuccess(result));
            Check("deleted character row removed",
                LoadInt(dbPath, "SELECT COUNT(1) FROM characters WHERE character_id=926014") == 0);
            Check("delete replacement seed uses same account survivor",
                LoadInt(dbPath, "SELECT seed_character_id FROM get_userinfo_template WHERE id=1") == 926015);
            Check("same account survivor remains active",
                LoadInt(dbPath, "SELECT COUNT(1) FROM characters WHERE character_id=926015 AND delete_flag=0") == 1);
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

        private static long LoadLong(string dbPath, string sql)
        {
            using (var conn = Open(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
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
