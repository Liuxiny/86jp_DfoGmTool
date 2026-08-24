using System;
using System.Collections.Generic;
using DfoGmTool.ServerCore.Game.Characters;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.Skills;
using GmPvfLib;

namespace DfoGmTool.SelfTests
{
    internal static class ItemGrantOptionsSelfTest
    {
        private static int _failures;

        internal static int Run()
        {
            _failures = 0;
            Console.WriteLine("=== ITEM_GRANT_OPTIONS selftest ===");

            PvfCharacterJobCatalog.ConfigureForTests(BuildJobCatalogFixture());
            try
            {
                CheckEquipmentCapabilities();
                CheckEquipmentEncoding();
                CheckEquipmentLimits();
                CheckQualitySeeds();
                CheckAvatarRules();
                CheckAvatarEquipmentMetadataAdapter();
                CheckAvatarSkillFiltering();
                CheckAvatarExtraJson();
                CheckAvatarDurationDeduplication();
                CheckExpirationRules();
                CheckCubeRoutes();
                CheckA21SpecialMaterialRoute();
            }
            finally
            {
                PvfCharacterJobCatalog.ResetForPvfChange();
            }

            Console.WriteLine(_failures == 0
                ? "ItemGrantOptionsSelfTest OK"
                : $"ItemGrantOptionsSelfTest FAIL: {_failures}");
            return _failures == 0 ? 0 : 1;
        }

        private static void CheckEquipmentCapabilities()
        {
            var weapon = EquipmentGrantPolicy.Describe(new ItemMetadata
            {
                ItemKind = "equipment",
                EquipmentType = "[weapon]",
                MinimumLevel = 55,
                Rarity = 2,
            });
            Check("weapon supports upgrade", weapon.CanUpgrade);
            Check("weapon supports amplify", weapon.CanAmplify);
            Check("weapon supports forging", weapon.CanForge);
            Check("upgrade max is 31", weapon.MaxUpgradeLevel == 31);
            Check("forging max is 8", weapon.MaxForgingLevel == 8);

            var armor = EquipmentGrantPolicy.Describe(new ItemMetadata
            {
                ItemKind = "equipment",
                EquipmentType = "[coat]",
                MinimumLevel = 55,
                Rarity = 2,
            });
            Check("armor supports upgrade", armor.CanUpgrade);
            Check("armor supports amplify", armor.CanAmplify);
            Check("armor cannot forge", !armor.CanForge);

            var title = EquipmentGrantPolicy.Describe(new ItemMetadata
            {
                ItemKind = "equipment",
                EquipmentType = "[title name]",
                MinimumLevel = 85,
                Rarity = 4,
            });
            Check("title cannot upgrade", !title.CanUpgrade);
            Check("title cannot amplify", !title.CanAmplify);

            var lowLevel = EquipmentGrantPolicy.Describe(new ItemMetadata
            {
                ItemKind = "equipment",
                EquipmentType = "[ring]",
                MinimumLevel = 54,
                Rarity = 2,
            });
            Check("level 54 cannot amplify", !lowLevel.CanAmplify);

            var lowRarity = EquipmentGrantPolicy.Describe(new ItemMetadata
            {
                ItemKind = "equipment",
                EquipmentType = "[ring]",
                MinimumLevel = 55,
                Rarity = 1,
            });
            Check("rarity below purple cannot amplify", !lowRarity.CanAmplify);
        }

        private static void CheckEquipmentEncoding()
        {
            var metadata = new ItemMetadata
            {
                ItemKind = "equipment",
                EquipmentType = "[weapon]",
                MinimumLevel = 55,
                Rarity = 2,
            };
            var options = new ItemGrantOptions
            {
                QualityMode = ItemQualityMode.Top,
                UpgradeLevel = 18,
                AmplifyType = 3,
                ForgingLevel = 8,
            };

            Check("build +18 strength red weapon", EquipmentGrantPolicy.TryBuildExtraJson(
                metadata,
                options,
                _ => 5,
                out var extraJson,
                out var error), error);
            var extra = ItemExtraView.Parse(extraJson);
            Check("upgrade encoded in extData0", extra.Equipment.Upgrade == 18);
            Check("PVF red type 3 means strength", extra.Equipment.AmplifyType == 3);
            Check("red initial value comes from PVF table", extra.Equipment.AmplifyValue == 5);
            Check("forging encoded at tailData2F[27]", extra.Equipment.Forging == 8);

            Check("red type labels use PVF order",
                EquipmentGrantPolicy.GetAmplifyTypeLabel(1) == "体力"
                && EquipmentGrantPolicy.GetAmplifyTypeLabel(2) == "精神"
                && EquipmentGrantPolicy.GetAmplifyTypeLabel(3) == "力量"
                && EquipmentGrantPolicy.GetAmplifyTypeLabel(4) == "智力");
        }

        private static void CheckEquipmentLimits()
        {
            var weapon = new ItemMetadata
            {
                ItemKind = "equipment",
                EquipmentType = "[weapon]",
                MinimumLevel = 55,
                Rarity = 2,
            };
            Check("upgrade 32 rejected", !EquipmentGrantPolicy.TryBuildExtraJson(
                weapon,
                new ItemGrantOptions { UpgradeLevel = 32 },
                _ => 5,
                out _,
                out _));
            Check("forging 9 rejected", !EquipmentGrantPolicy.TryBuildExtraJson(
                weapon,
                new ItemGrantOptions { ForgingLevel = 9 },
                _ => 5,
                out _,
                out _));

            var armor = new ItemMetadata
            {
                ItemKind = "equipment",
                EquipmentType = "[coat]",
                MinimumLevel = 55,
                Rarity = 2,
            };
            Check("armor forging rejected", !EquipmentGrantPolicy.TryBuildExtraJson(
                armor,
                new ItemGrantOptions { ForgingLevel = 1 },
                _ => 5,
                out _,
                out _));
        }

        private static void CheckQualitySeeds()
        {
            Check("top quality seed is confirmed value",
                ItemQuality.ResolveSeed(ItemQualityMode.Top) == ItemQuality.TopQualitySeed);
            for (var index = 0; index < 64; index++)
            {
                var seed = ItemQuality.ResolveSeed(ItemQualityMode.Random);
                Check("random quality seed is valid", seed != 0 && seed != ItemQuality.TopQualitySeed);
            }
        }

        private static void CheckAvatarRules()
        {
            var lancer = PvfCharacterJobCatalog.Current;
            lancer.TryGet(13, out var lancerInfo);
            Check("PVF job13 token is dynamic", lancerInfo != null
                && lancerInfo.Token == "demonic lancer"
                && lancerInfo.BaseName == "魔枪士"
                && lancerInfo.GrowTypeNames.Count == 2
                && lancerInfo.AwakeningNames.TryGetValue(1, out var lancerFirstAwakenings)
                && lancerFirstAwakenings.Count == 2
                && lancerInfo.AwakeningNames.TryGetValue(2, out var lancerSecondAwakenings)
                && lancerSecondAwakenings.Count == 2);
            Check("PVF job13 token is usable", AvatarGrantPolicy.IsUsableByJob("[demonic lancer]", 13));
            var lancerTags = lancer.GetJobTags(13);
            Check("PVF job13 task tag keeps token spaces",
                lancerTags.Length == 1 && lancerTags[0] == "[demonic lancer]");
            Check("PVF job13 task tag matches dynamically",
                lancer.MatchesJobTag("[demonic lancer]", 13));
            Check("unknown PVF job token is rejected", !AvatarGrantPolicy.IsUsableByJob("[unknown job]", 13));
            Check("priest avatar accepts priest character",
                AvatarGrantPolicy.IsUsableByJob("[priest]", 4));
            Check("priest avatar rejects swordman character",
                !AvatarGrantPolicy.IsUsableByJob("[priest]", 0));
            Check("all-job avatar accepts knight",
                AvatarGrantPolicy.IsUsableByJob("[all]", 12));
            Check("multi-job avatar accepts later at-swordman token",
                AvatarGrantPolicy.IsUsableByJob("[swordman]` `[demonic swordman]` `[at swordman]", 11));
            Check("dark knight avatar accepts demonic swordman category",
                AvatarGrantPolicy.IsUsableByJob("[demonic swordman]", 9));
            Check("DSSwordman fixture keeps direct awakening names",
                lancer.TryGet(9, out var darkInfo)
                && darkInfo.GrowTypeNames.Count == 0
                && darkInfo.AwakeningNames.TryGetValue(0, out var darkAwakenings)
                && darkAwakenings.Count == 2);
            Check("CreatorMage fixture keeps direct awakening names",
                lancer.TryGet(10, out var creatorInfo)
                && creatorInfo.GrowTypeNames.Count == 0
                && creatorInfo.AwakeningNames.TryGetValue(0, out var creatorAwakenings)
                && creatorAwakenings.Count == 2);
            Check("male swordman avatar rejects dark knight",
                !AvatarGrantPolicy.IsUsableByJob("[swordman]", 9));
            Check("male swordman avatar rejects female swordman",
                !AvatarGrantPolicy.IsUsableByJob("[swordman]", 11));

            var pantsSelectAbilities = new List<AvatarSelectAbilityEntry>
            {
                new AvatarSelectAbilityEntry
                {
                    OptionValue = 0,
                    Ability = "HP MAX",
                    Operator = "+",
                    Amount = 280,
                },
                new AvatarSelectAbilityEntry
                {
                    OptionValue = 1,
                    Ability = "MP MAX",
                    Operator = "+",
                    Amount = 280,
                },
                new AvatarSelectAbilityEntry
                {
                    OptionValue = 2,
                    Ability = "EQUIPMENT_PHYSICAL_DEFENSE",
                    Operator = "+",
                    Amount = 660,
                },
            };
            var coat = AvatarGrantPolicy.ResolveOptions("[coat avatar]", 2, null, 0, -1);
            var pants = AvatarGrantPolicy.ResolveOptions("[pants avatar]", 2, pantsSelectAbilities, 0, -1);
            Check("coat without ability case falls back to default option", coat.Count == 1 && coat[0].Value == 0);
            Check("grade 2 pants does not expose skill ids", !pants.Exists(x => x.IsSkill));
            Check("pants uses fixed option_value list 0 to 2",
                pants.Count == 3
                && pants[0].Value == 0
                && pants[1].Value == 1
                && pants[2].Value == 2);
            Check("grade 0 avatar has no selectable ability",
                AvatarGrantPolicy.ResolveOptions("[hat avatar]", 0, null, 0, -1).Count == 1);
            Check("PVF equipment type hat avatar routes as avatar",
                ItemMetadataResolver.IsAvatarMetadata(new ItemMetadata
                {
                    ItemKind = "equipment",
                    EquipmentType = "[hat avatar]",
                }));
            Check("PVF clear avatar category routes clone avatar as avatar",
                ItemMetadataResolver.IsAvatarMetadata(new ItemMetadata
                {
                    ItemKind = "equipment",
                    EquipmentType = "[hat avatar]",
                    ItemCategory = "clear avatar",
                    PvfFilePath = "clone/artifact_hat.equ",
                }));
            Check("ordinary equipment is not avatar",
                !ItemMetadataResolver.IsAvatarMetadata(new ItemMetadata
                {
                    ItemKind = "equipment",
                    EquipmentType = "[weapon]",
                    ItemCategory = "legacy",
                }));
            Check("avatar emblem stackable is not avatar inventory equipment",
                !ItemMetadataResolver.IsAvatarMetadata(new ItemMetadata
                {
                    ItemKind = "stackable",
                    StackableType = "[avatar emblem]",
                }));
            Check("shared PVF tag parser matches search list avatar tag",
                ItemMetadataResolver.FirstPvfTypeTag("`[hat avatar]`") == "hat avatar");
            Check("known weapon type does not require manual grant type",
                !ItemMetadataResolver.RequiresManualGrantType(new ItemMetadata
                {
                    ItemKind = "equipment",
                    EquipmentType = "[weapon]",
                }));
            Check("unknown equipment type requires manual grant type",
                ItemMetadataResolver.RequiresManualGrantType(new ItemMetadata
                {
                    ItemKind = "equipment",
                }));
            Check("known stackable tag does not require manual grant type",
                !ItemMetadataResolver.RequiresManualGrantType(new ItemMetadata
                {
                    ItemKind = "stackable",
                    StackableType = "[etc]",
                }));
            Check("unknown stackable tag requires manual grant type",
                ItemMetadataResolver.RequiresManualGrantType(new ItemMetadata
                {
                    ItemKind = "stackable",
                }));
            Check("PVF-missing special item is never manually routable",
                !ItemMetadataResolver.RequiresManualGrantType(new ItemMetadata
                {
                    ItemKind = "special",
                }));
        }

        private static IEnumerable<PvfCharacterJobCatalog.JobInfo> BuildJobCatalogFixture()
        {
            var labels = new[]
            {
                "swordman", "fighter", "gunner", "mage", "priest", "at gunner", "thief",
                "at fighter", "at mage", "demonic swordman", "creator mage", "at swordman", "knight",
            };
            var names = new[]
            {
                "鬼剑士", "格斗家", "神枪手", "魔法师", "圣职者", "女神枪手", "暗夜使者",
                "男格斗家", "男魔法师", "黑暗武士", "缔造者", "女鬼剑士", "守护者",
            };
            var result = new List<PvfCharacterJobCatalog.JobInfo>();
            for (var id = 0; id < labels.Length; id++)
            {
                result.Add(new PvfCharacterJobCatalog.JobInfo
                {
                    Id = id,
                    Token = labels[id],
                    BaseName = names[id],
                });
            }

            var darkSwordman = new PvfCharacterJobCatalog.JobInfo
            {
                Id = 9,
                Token = "demonic swordman",
                BaseName = "黑暗武士",
                MaxGrowCount = 0,
                HasMaxGrowCount = true,
            };
            darkSwordman.AwakeningNames[0] = new List<string> { "黑暗觉醒", "黑暗二觉" };
            result[9] = darkSwordman;

            var creatorMage = new PvfCharacterJobCatalog.JobInfo
            {
                Id = 10,
                Token = "creator mage",
                BaseName = "缔造者",
                MaxGrowCount = 0,
                HasMaxGrowCount = true,
            };
            creatorMage.AwakeningNames[0] = new List<string> { "创造觉醒", "创造二觉" };
            result[10] = creatorMage;

            var lancer = new PvfCharacterJobCatalog.JobInfo
            {
                Id = 13,
                Token = "demonic lancer",
                BaseName = "魔枪士",
                MaxGrowCount = 2,
            };
            lancer.GrowTypeNames.Add("征战者");
            lancer.GrowTypeNames.Add("决战者");
            lancer.AwakeningNames[1] = new List<string> { "战魂", "不灭战神" };
            lancer.AwakeningNames[2] = new List<string> { "无双之魂", "圣武枪魂" };
            result.Add(lancer);
            return result;
        }

        private static void CheckExpirationRules()
        {
            var limited = new ItemGrantExpirationCapability
            {
                IsLimited = true,
                CanOverride = true,
            };
            Check("limited item accepts 30 days",
                ItemGrantExpirationOverride.TryResolve(limited, 30, 1_700_000_000, out var expire, out _)
                && expire == 1_700_000_000 + 30 * 86400);
            Check("limited item rejects zero days",
                !ItemGrantExpirationOverride.TryResolve(limited, 0, 1_700_000_000, out _, out _));
            Check("ordinary item rejects expiry override",
                !ItemGrantExpirationOverride.TryResolve(
                    new ItemGrantExpirationCapability { IsLimited = false, CanOverride = false },
                    30,
                    1_700_000_000,
                    out _,
                    out _));
        }

        private static void CheckAvatarSkillFiltering()
        {
            var level45ActiveSkill = new SkillStaticData
            {
                SkillIndex = 40,
                Name = "暴走",
                IsActive = true,
                RequiredLevel = 45,
                SkillFitnessGrowtypes = new[] { 1 },
            };
            Check("avatar allows active skill learned at level 45 regardless of subclass",
                SkillDataProvider.IsValidAvatarOptionSkill(
                    level45ActiveSkill,
                    1));

            var level46ActiveSkill = new SkillStaticData
            {
                SkillIndex = 238,
                Name = "等级过高技能",
                IsActive = true,
                RequiredLevel = 46,
            };
            Check("normal avatar rejects active skill first learned above level 45",
                !SkillDataProvider.IsValidAvatarOptionSkill(
                    level46ActiveSkill,
                    1));
            Check("advanced avatar allows active skill first learned above level 45",
                SkillDataProvider.IsValidAvatarOptionSkill(
                    level46ActiveSkill,
                    2));

            var rareHighLevelActiveSkill = new SkillStaticData
            {
                SkillIndex = 238,
                Name = "高等级主动技能",
                IsActive = true,
                RequiredLevel = 85,
            };
            Check("rare avatar allows active skills above level 45",
                SkillDataProvider.IsValidAvatarOptionSkill(
                    rareHighLevelActiveSkill,
                    3));

            var passiveSkill = new SkillStaticData
            {
                SkillIndex = 63,
                Name = "血气旺盛",
                IsActive = false,
                IsPassive = true,
                RequiredLevel = 15,
            };
            Check("normal avatar rejects passive skill",
                !SkillDataProvider.IsValidAvatarOptionSkill(passiveSkill, 1));
            Check("advanced avatar allows passive skill",
                SkillDataProvider.IsValidAvatarOptionSkill(passiveSkill, 2));
            Check("rare avatar allows passive skill",
                SkillDataProvider.IsValidAvatarOptionSkill(
                    passiveSkill,
                    3));

            var craftingSkill = new SkillStaticData
            {
                SkillIndex = 179,
                Name = "物品分解",
                IsActive = true,
                RequiredLevel = 20,
                SkillFitnessGrowtypes = new[] { 0, 1, 2, 3, 4 },
            };
            Check("avatar rejects crafting skill",
                !SkillDataProvider.IsValidAvatarOptionSkill(
                    craftingSkill,
                    3));

            var hiddenSkill = new SkillStaticData
            {
                SkillIndex = 43,
                Name = "((不使用))",
                IsActive = true,
                RequiredLevel = 1,
                SkillFitnessGrowtypes = new[] { 3 },
            };
            Check("avatar rejects hidden skill",
                !SkillDataProvider.IsValidAvatarOptionSkill(
                    hiddenSkill,
                    3));
        }

        private static void CheckAvatarExtraJson()
        {
            const string expected = "{\"reserved0\":\"0000000000\",\"reserved1\":\"0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000\",\"reserved2\":\"000000000000000000000000000000000000000000000000000000000000\",\"unknownFixed4\":1024,\"tailData\":\"00000000000000\"}";
            Check("new avatar uses confirmed default extra_json",
                SqliteInventoryStore.CreateDefaultAvatarExtraJson() == expected);
            var parsed = ItemExtraView.Parse(expected);
            Check("avatar default extra_json fixed4 is 1024", parsed.Avatar.UnknownFixed4 == 1024);
            Check("avatar default extra_json reserved fields have protocol lengths",
                parsed.Avatar.Reserved0.Length == 5
                && parsed.Avatar.Reserved1.Length == 71
                && parsed.Avatar.Reserved2.Length == 30
                && parsed.Avatar.TailData.Length == 7);
        }

        private static void CheckAvatarEquipmentMetadataAdapter()
        {
            const string pvf = "[ability case index]\n42\n[/ability case index]\n"
                + "[avatar select ability]\n"
                + "0 `HP MAX` + 100\n"
                + "1 `SKILL_LEVEL` `[swordman]` 7 1\n"
                + "[/avatar select ability]";
            var equipment = EquipmentFile.Parse(pvf);
            var metadata = AvatarEquipmentMetadataReader.Read(equipment);

            Check("GM avatar adapter reads ability case index", metadata.AbilityCaseIndex == 42);
            Check("GM avatar adapter reads exact option count", metadata.SelectAbilities.Count == 2);
            Check("GM avatar adapter reads ordinary ability",
                metadata.SelectAbilities.Count > 0
                && metadata.SelectAbilities[0].OptionValue == 0
                && metadata.SelectAbilities[0].Ability == "HP MAX"
                && metadata.SelectAbilities[0].Operator == "+"
                && metadata.SelectAbilities[0].Amount == 100);
            Check("GM avatar adapter reads skill ability",
                metadata.SelectAbilities.Count > 1
                && metadata.SelectAbilities[1].OptionValue == 1
                && metadata.SelectAbilities[1].Ability == "SKILL_LEVEL"
                && metadata.SelectAbilities[1].Job == "swordman"
                && metadata.SelectAbilities[1].SkillIndex == 7
                && metadata.SelectAbilities[1].SkillLevel == 1);
        }

        private static void CheckAvatarDurationDeduplication()
        {
            const string pvf = "[avatar type select]\n"
                + "7 0 0 1500 0 0 0 "
                + "30 0 0 3000 0 0 0 "
                + "0 0 0 6000 0 0 0 "
                + "0 0 0 6500 0 0 3\n"
                + "[/avatar type select]";
            var options = AvatarDurationResolver.Parse(pvf);
            Check("avatar PVF duration values are deduplicated", options.Count == 3);
            Check("avatar PVF keeps permanent duration option",
                AvatarDurationResolver.ContainsDuration(options, 0));
        }

        private static void CheckCubeRoutes()
        {
            var expected = new Dictionary<int, int>
            {
                [3033] = 354,
                [3034] = 355,
                [3035] = 356,
                [3036] = 357,
                [3037] = 358,
                [3262] = 359,
            };
            foreach (var pair in expected)
            {
                Check($"cube {pair.Key} recognized", CurrencyService.IsCubeFragment(pair.Key));
                Check($"cube {pair.Key} account slot", CurrencyService.GetCubeFragmentSlot(pair.Key) == pair.Value);
            }
        }

        private static void CheckA21SpecialMaterialRoute()
        {
            var legacySpecial = new ItemMetadata
            {
                ItemKind = "stackable",
                StackableType = "[material] legacy4",
            };
            legacySpecial.GetSlotRange(out var start, out var end);
            Check("旧 special-material 不再占用 345-359", start == 121 && end == 176);
            Check("旧 special-material 手动路由被拒绝",
                !NewInventoryStore.TryResolveKindAndRange(
                    legacySpecial,
                    "special-material",
                    out _,
                    out _,
                    out _,
                    out _,
                    out _));
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
