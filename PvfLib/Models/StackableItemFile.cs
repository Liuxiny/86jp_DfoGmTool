using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GmPvfLib
{
    public class BoosterRewardEntry
    {
        public string RewardKind { get; set; }
        public string CharacterJobLabel { get; set; }
        public int Group { get; set; }
        public int DrawCount { get; set; } = 1;
        public int ItemId { get; set; }
        public int Weight { get; set; } = 10000;
        public int Count { get; set; } = 1;
        public int UsablePeriodDays { get; set; }
    }

    public class RandomBoxRemovalItemEntry
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    public class EquipmentUpgradeTicketInfo
    {
        public int TargetLevel { get; set; } = -1;
        public int SuccessRatePercent { get; set; } = -1;
        public string ApplyMode { get; set; }
        public int ApplyValue { get; set; } = -1;
        public List<int> ExtraValues { get; set; } = new List<int>();

        // 普通强化券/增幅券用百分比保存，业务层按万分权重消费。
        public int SuccessWeight => SuccessRatePercent >= 0 ? SuccessRatePercent * 1000 : -1;
    }

    public class EnchantRandomUpgradeEntry
    {
        public int TargetLevel { get; set; }
        public int SuccessWeight { get; set; }
    }

    public class EnchantRandomUpgradeInfo
    {
        public List<int> RarityRestrictions { get; set; } = new List<int>();
        public int SlotRestriction { get; set; } = -1;
        public int SealRestriction { get; set; } = -1;
        public List<EnchantRandomUpgradeEntry> EnchantEntries { get; set; } = new List<EnchantRandomUpgradeEntry>();
    }

    public sealed class ThreeChronicleSkillOption
    {
        public int OptionNo { get; set; } = -1;
        public string Job { get; set; }
        public int SkillId { get; set; } = -1;
    }

    public sealed class ThreeChronicleEnchantInfo
    {
        public List<int> Probabilities { get; set; } = new List<int>();
        public List<ThreeChronicleEnchantCheck> Checks { get; set; } = new List<ThreeChronicleEnchantCheck>();
        public List<ThreeChronicleSkillOption> Skills { get; set; } = new List<ThreeChronicleSkillOption>();

        public ThreeChronicleSkillOption GetSkill(int optionNo)
        {
            return Skills.Find(skill => skill.OptionNo == optionNo);
        }
    }

    public sealed class ThreeChronicleEnchantCheck
    {
        public List<int> Values { get; set; } = new List<int>();
        public string EquipmentType { get; set; }
        public List<ThreeChronicleSkillOption> Skills { get; set; } = new List<ThreeChronicleSkillOption>();
    }

    public sealed class AmplificationRandomValueEntry
    {
        public int UpgradeLevel { get; set; }
        public int Weight { get; set; }
    }

    public sealed class StackableStatusIncreaseEntry
    {
        public string EffectType { get; set; }
        public List<int> Values { get; set; } = new List<int>();
    }

    public sealed class AvatarSelectAbilityChangeEntry
    {
        public string SourceScope { get; set; }
        public string TargetScope { get; set; }
        public int AvatarGrade { get; set; } = -1;
        public int Param { get; set; } = -1;
    }

    public sealed class EquipmentLevelEmancipateProbability
    {
        public int MaximumLevel { get; set; }
        public int Weight { get; set; }
    }

    public sealed class EquipmentLevelEmancipateCondition
    {
        public List<int> Rarities { get; set; } = new List<int>();
        public int MinimumLevel { get; set; } = -1;
        public int MaximumLevel { get; set; } = -1;
    }

    public sealed class EquipmentLevelEmancipateInfo
    {
        public List<EquipmentLevelEmancipateProbability> Probabilities { get; set; } = new List<EquipmentLevelEmancipateProbability>();
        public int UpgradeLevel { get; set; } = -1;
        public EquipmentLevelEmancipateCondition Condition { get; set; } = new EquipmentLevelEmancipateCondition();
        public List<int> IgnoreIndexes { get; set; } = new List<int>();
    }

    public sealed class LimitedCubeItemRequirement
    {
        public int ItemId { get; set; }

        public int Count { get; set; }
    }

    public sealed class LimitedCubeResultEntry
    {
        public int ItemId { get; set; }

        public short ResultValue { get; set; }

        public int Weight { get; set; }
    }

    public sealed class UpgradeLimitCubeInfo
    {
        public List<LimitedCubeItemRequirement> ConditionItems { get; } =
            new List<LimitedCubeItemRequirement>();

        public List<LimitedCubeItemRequirement> AdditionalMaterials { get; } =
            new List<LimitedCubeItemRequirement>();

        public List<LimitedCubeResultEntry> Results { get; } =
            new List<LimitedCubeResultEntry>();
    }

    
    
    
    
    public class StackableItemFile : PvfModelBase
    {
        #region 基本信息

        public string Name { get; set; }
        public string Explain { get; set; }
        public string FlavorText { get; set; }
        public int Grade { get; set; } = -1;
        public int Rarity { get; set; } = -1;
        public int MinimumLevel { get; set; } = -1;
        public int MaximumLevel { get; set; } = -1;

        #endregion

        #region 物品类型

        
        public string StackableType { get; set; }
        public List<string> AvatarEmblemTargetTypes { get; set; } = new List<string>();
        // [usable equip type] 限定可作用的装备部位, 例如品级调整箱按武器/防具/首饰分箱。空表示不限部位。
        public List<string> UsableEquipTypes { get; set; } = new List<string>();
        public List<AvatarSelectAbilityChangeEntry> AvatarSelectAbilityChanges { get; set; } = new List<AvatarSelectAbilityChangeEntry>();
        public bool HasAvatarSelectAbilityChange => AvatarSelectAbilityChanges.Count > 0;
        public byte AvatarEmblemSocketType { get; set; }
        public int SubType { get; set; } = -1;
        public string AttachType { get; set; }
        public string ItemGroupName { get; set; }
        public string ItemCategory { get; set; }
        public int StackLimit { get; set; } = -1;

        #endregion

        #region 经济属性

        public int Price { get; set; } = -1;
        // [add price] is a signed purchase-price adjustment.
        public int AddPrice { get; set; }
        public int Value { get; set; } = -1;
        public int Weight { get; set; } = -1;
        public int LotteryUseCost { get; set; }
        public int LotteryUseNeedItemId { get; set; }
        public int LotteryUseNeedItemCount { get; set; }
        public int CoolTime { get; set; } = -1;
        public string CooltimeGroup { get; set; }

        #endregion

        #region 外观

        public string Icon { get; set; }
        public string FieldImage { get; set; }
        public string IconMark { get; set; }
        public string MoveWav { get; set; }

        #endregion

        #region 使用限制

        public string UsableJob { get; set; }
        public string SuitableJob { get; set; }
        public string ImpossibleContents { get; set; }
        public List<string> ImpossibleContentItems { get; set; } = new List<string>();
        public int ExpirationDate { get; set; } = -1;
        public int UsablePeriod { get; set; } = -1;
        public int TradeLimit { get; set; } = -1;
        public int PortableDisjoint { get; set; } = -1;
        public string ExpertJobOnlyType { get; set; }
        public int ExpertJobOnlyLevel { get; set; } = -1;
        public int AlchemistExtractionIndex { get; set; } = -1;
        public int EnchanterExtractionIndex { get; set; } = -1;
        public int DollControllerExtractionIndex { get; set; } = -1;

        #endregion

        #region 强化/合成

        public int EnchantIndex { get; set; } = -1;
        public int Type { get; set; } = -1;
        public ThreeChronicleEnchantInfo ThreeChronicleEnchant { get; set; }
        public List<int> EnchantTable { get; set; } = new List<int>();
        // [action type] `[xxx]` p1 p2 ...: ActionTypeName="[xxx]", ActionTypeParams=[p1,p2,...]
        public string ActionTypeName { get; set; }
        public List<int> ActionTypeParams { get; set; } = new List<int>();
        public UpgradeLimitCubeInfo UpgradeLimitCube { get; set; }
        public EquipmentUpgradeTicketInfo EquipmentReinforcementTicket { get; set; }
        public EquipmentUpgradeTicketInfo EquipmentAmplifyReinforcementTicket { get; set; }
        public EnchantRandomUpgradeInfo EnchantRandomUpgrade { get; set; }
        public List<AmplificationRandomValueEntry> AmplificationRandomValues { get; set; } = new List<AmplificationRandomValueEntry>();
        public List<int> CheckUsableItemLevels { get; set; } = new List<int>();
        public int CheckUsableItemLevelMin => CheckUsableItemLevels.Count > 0 ? CheckUsableItemLevels[0] : -1;
        public int CheckUsableItemLevelMax => CheckUsableItemLevels.Count > 1 ? CheckUsableItemLevels[1] : -1;
        public string BoosterInfo { get; set; }
        public int BoosterCategoryNum { get; set; } = -1;
        public int BoosterSelectionNum { get; set; } = -1;
        public string BoosterSelectCategory { get; set; }
        public string BoosterCategoryName { get; set; }
        public List<BoosterRewardEntry> BoosterRewards { get; set; } = new List<BoosterRewardEntry>();
        public List<BoosterRewardEntry> BoosterSelectionRewards { get; set; } = new List<BoosterRewardEntry>();
        public int EmancipateTicket { get; set; } = -1;
        public EquipmentLevelEmancipateInfo EquipmentLevelEmancipate { get; set; }
        public int EmancipateGradeMax { get; set; } = -1;
        public int EmancipateAmplifyMax { get; set; } = -1;
        public int EmancipateGenuineGradeMax { get; set; } = -1;

        #endregion

        #region 关联数据

        
        public string Equipment { get; set; }
        
        public string StringData { get; set; }

        public List<string> StringDataItems { get; set; } = new List<string>();
        
        public string IntData { get; set; }
        
        public string PackageData { get; set; }
        public List<BoosterRewardEntry> PackageRewards { get; set; } = new List<BoosterRewardEntry>();
        public List<BoosterRewardEntry> RandomBoxRewards { get; set; } = new List<BoosterRewardEntry>();
        public List<BoosterRewardEntry> LegacyRewards { get; set; } = new List<BoosterRewardEntry>();
        public List<BoosterRewardEntry> UpgradableLegacyRewards { get; set; } = new List<BoosterRewardEntry>();
        public List<RandomBoxRemovalItemEntry> RandomBoxRemovalItems { get; set; } = new List<RandomBoxRemovalItemEntry>();
        public string OutputItem { get; set; }
        public string InputItem { get; set; }
        public string NeedSkill { get; set; }
        public string NeedMaterial { get; set; }
        public int MonsterCardId { get; set; } = -1;
        public List<int> MonsterCardIds { get; set; } = new List<int>();
        public int MonsterCardBind { get; set; } = -1;
        public List<int> TargetItemIds { get; set; } = new List<int>();
        public List<int> BeadLimitedUsableItemIds { get; set; } = new List<int>();

        #endregion

        #region 战斗属性

        public int PhysicalAttack { get; set; }
        public int MagicalAttack { get; set; }
        public int PhysicalDefense { get; set; }
        public int MagicalDefense { get; set; }
        public List<StackableStatusIncreaseEntry> StatusIncreases { get; set; } = new List<StackableStatusIncreaseEntry>();

        #endregion
        #region 解析

        public static StackableItemFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new StackableItemFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var stk = new StackableItemFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    
                    case "name": stk.Name = StripBacktick(data); break;
                    case "explain": stk.Explain = StripBacktick(data); break;
                    case "flavor text": stk.FlavorText = StripBacktick(data); break;
                    case "grade": stk.Grade = ParseInt(data); break;
                    case "rarity": stk.Rarity = ParseInt(data); break;
                    case "minimum level": stk.MinimumLevel = ParseInt(data); break;
                    case "maximum level": stk.MaximumLevel = ParseInt(data); break;

                    
                    case "stackable type": stk.StackableType = StripBacktick(data); break;
                    case "avatar emblem target type":
                        stk.AvatarEmblemTargetTypes = ParseStringList(node, content);
                        stk.AvatarEmblemSocketType = ResolveAvatarEmblemSocketType(stk.AvatarEmblemTargetTypes);
                        break;
                    case "usable equip type": stk.UsableEquipTypes = ParseStringList(node, content); break;
                    case "avatar select ability change": stk.AvatarSelectAbilityChanges = ParseAvatarSelectAbilityChanges(node, content); break;
                    case "sub type": stk.SubType = ParseInt(data); break;
                    case "attach type": stk.AttachType = StripBacktick(data); break;
                    case "item group name": stk.ItemGroupName = StripBacktick(data); break;
                    case "item category": stk.ItemCategory = StripBacktick(data); break;
                    case "stack limit": stk.StackLimit = ParseInt(data); break;

                    
                    case "price": stk.Price = ParseInt(data); break;
                    case "add price": stk.AddPrice = ParseInt(data); break;
                    case "value": stk.Value = ParseInt(data); break;
                    case "weight": stk.Weight = ParseInt(data); break;
                    case "lottery use cost": stk.LotteryUseCost = Math.Max(0, ParseInt(data)); break;
                    case "lottery use need item":
                    {
                        var values = ParseInts(data);
                        if (values.Count >= 2)
                        {
                            stk.LotteryUseNeedItemId = Math.Max(0, values[0]);
                            stk.LotteryUseNeedItemCount = Math.Max(0, values[1]);
                        }
                        break;
                    }
                    case "cool time": stk.CoolTime = ParseInt(data); break;
                    case "cooltime group": stk.CooltimeGroup = data; break;

                    
                    case "icon": stk.Icon = data; break;
                    case "field image": stk.FieldImage = data; break;
                    case "icon mark": stk.IconMark = data; break;
                    case "move wav": stk.MoveWav = StripBacktick(data); break;

                    
                    case "usable job": stk.UsableJob = StripBacktick(data); break;
                    case "suitable job": stk.SuitableJob = StripBacktick(data); break;
                    case "impossible contents":
                        stk.ImpossibleContents = data;
                        stk.ImpossibleContentItems = ParseStringList(node, content);
                        break;
                    case "expiration date": stk.ExpirationDate = ParseInt(data); break;
                    case "usable period": stk.UsablePeriod = ParseInt(data); break;
                    case "trade limit max": stk.TradeLimit = ParseInt(data); break;
                    case "portable disjoint": stk.PortableDisjoint = ParseInt(data); break;
                    case "expertjob only": ParseExpertJobOnly(node, content, stk); break;
                    case "alchemist extraction": stk.AlchemistExtractionIndex = ParseInt(data); break;
                    case "enchanter extraction": stk.EnchanterExtractionIndex = ParseInt(data); break;
                    case "doll_controller extraction": stk.DollControllerExtractionIndex = ParseInt(data); break;

                    
                    case "enchant index": stk.EnchantIndex = ParseInt(data); break;
                    case "type": stk.Type = ParseInt(data); break;
                    case "3choro enchant": stk.ThreeChronicleEnchant = ParseThreeChronicleEnchant(root, node, content); break;
                    case "enchant table": stk.EnchantTable = ParseEnchantTableIndexes(node, content); break;
                    case "action type": ParseActionType(node, content, stk); break;
                    case "upgrade limit cube info": stk.UpgradeLimitCube = ParseUpgradeLimitCubeInfo(node, content); break;
                    case "equipment reinforcement ticket": stk.EquipmentReinforcementTicket = ParseUpgradeTicket(node, content); break;
                    case "equipment amplify reinforcement ticket": stk.EquipmentAmplifyReinforcementTicket = ParseUpgradeTicket(node, content); break;
                    case "enchant random": stk.EnchantRandomUpgrade = ParseEnchantRandomUpgrade(node, content); break;
                    case "amplification random value": stk.AmplificationRandomValues = ParseAmplificationRandomValues(node, content); break;
                    case "check usable itemlevel": stk.CheckUsableItemLevels = ParseIntList(node, content); break;
                    case "emancipate ticket": stk.EmancipateTicket = ParseInt(data); break;
                    case "equipment level emancipate": stk.EquipmentLevelEmancipate = ParseEquipmentLevelEmancipate(node, content); break;
                    case "emancipate grade max": stk.EmancipateGradeMax = ParseInt(data); break;
                    case "emancipate amplify max": stk.EmancipateAmplifyMax = ParseInt(data); break;
                    case "emancipate genuinegrade max": stk.EmancipateGenuineGradeMax = ParseInt(data); break;
                    case "booster info": stk.BoosterInfo = data; break;
                    case "booster category num": stk.BoosterCategoryNum = ParseInt(data); break;
                    case "booster selection num": stk.BoosterSelectionNum = ParseInt(data); break;
                    case "booster select category": stk.BoosterSelectCategory = data; break;
                    case "booster category name": stk.BoosterCategoryName = data; break;

                    
                    case "equipment": stk.Equipment = data; break;
                    case "string data":
                        stk.StringData = data;
                        stk.StringDataItems = ParseStringList(node, content);
                        break;
                    case "int data": stk.IntData = data; break;
                    case "package data":
                        if (!string.IsNullOrWhiteSpace(data))
                            stk.PackageData = string.IsNullOrWhiteSpace(stk.PackageData) ? data : stk.PackageData + " " + data;
                        break;
                    case "output item": stk.OutputItem = data; break;
                    case "input item": stk.InputItem = data; break;
                    case "need skill": stk.NeedSkill = data; break;
                    case "need material": stk.NeedMaterial = data; break;
                    case "monster card id":
                    {
                        stk.MonsterCardIds = ParseIntList(node, content);
                        stk.MonsterCardId = ResolveFirstPositive(stk.MonsterCardIds, ParseInt(data));
                        break;
                    }
                    case "monstercard bind": stk.MonsterCardBind = ParseInt(data); break;
                    case "target item id": stk.TargetItemIds = ParseIntList(node, content); break;
                    case "bead limited usable item": stk.BeadLimitedUsableItemIds = ParseIntList(node, content); break;

                    
                    case "physical attack": stk.PhysicalAttack = ParseInt(data); break;
                    case "magical attack": stk.MagicalAttack = ParseInt(data); break;
                    case "physical defense": stk.PhysicalDefense = ParseInt(data); break;
                    case "magical defense": stk.MagicalDefense = ParseInt(data); break;
                    case "increase status type":
                        stk.StatusIncreases.AddRange(ParseStatusIncreases(node, content));
                        break;
                }
            }

            stk.BoosterRewards = ParseBoosterInfo(root.GetChild("booster info"), content);
            stk.BoosterSelectionRewards = ParseBoosterSelection(root.GetChildren("booster select category"), content);
            stk.PackageRewards = ParsePackageRewards(
                root.GetChildren("package data"),
                root.GetChildren("package data include usable period"),
                content);
            stk.LegacyRewards = ParseLegacyRewards(stk.IntData);
            stk.UpgradableLegacyRewards = ParseUpgradableLegacyRewards(stk.IntData);
            var randomBox = root.GetChild("RANDOMBOX");
            stk.RandomBoxRewards = ParseRandomBoxRewards(randomBox, content);
            stk.RandomBoxRemovalItems = ParseRandomBoxRemovalItems(randomBox != null ? randomBox.GetChild("sealing removal item") : null, content);

            return stk;
        }

        private static List<StackableStatusIncreaseEntry> ParseStatusIncreases(
            ScriptNode node,
            string content)
        {
            var result = new List<StackableStatusIncreaseEntry>();
            if (node == null || node.Children.Count != 0)
                return result;

            foreach (var item in node.DataItems)
            {
                var raw = item.GetContent(content);
                var match = Regex.Match(
                    raw ?? string.Empty,
                    @"^\s*`?\[(?<type>[^\]\r\n]+)\]`?(?<values>(?:\s+-?\d+)*)\s*$");
                if (!match.Success)
                    continue;

                result.Add(new StackableStatusIncreaseEntry
                {
                    EffectType = match.Groups["type"].Value.Trim(),
                    Values = ParseInts(match.Groups["values"].Value),
                });
            }

            return result;
        }

        private static List<BoosterRewardEntry> ParseBoosterInfo(ScriptNode node, string content)
        {
            var rewards = new List<BoosterRewardEntry>();
            if (node == null)
                return rewards;

            var fallbackGroup = 0;
            foreach (var child in node.Children)
            {
                fallbackGroup++;
                ParseBoosterRewardNode(child, content, child.Tag, fallbackGroup, rewards, weighted: true);
            }

            return rewards;
        }

        private static List<BoosterRewardEntry> ParseBoosterSelection(List<ScriptNode> categories, string content)
        {
            var rewards = new List<BoosterRewardEntry>();
            if (categories == null)
                return rewards;

            var fallbackGroup = 0;
            foreach (var category in categories)
            {
                fallbackGroup++;
                foreach (var child in category.Children)
                    ParseBoosterRewardNode(child, content, child.Tag, fallbackGroup, rewards, weighted: false);
            }

            return rewards;
        }

        private static List<BoosterRewardEntry> ParsePackageRewards(
            List<ScriptNode> packageNodes,
            List<ScriptNode> packageWithPeriodNodes,
            string content)
        {
            var rewards = new List<BoosterRewardEntry>();
            foreach (var node in packageNodes ?? new List<ScriptNode>())
            {
                var ints = ParseInts(node.GetContent(content));
                for (var i = 0; i + 1 < ints.Count; i += 2)
                    AddPackageReward(rewards, ints[i], ints[i + 1], 0);
            }

            foreach (var node in packageWithPeriodNodes ?? new List<ScriptNode>())
            {
                var ints = ParseInts(node.GetContent(content));
                for (var i = 0; i + 2 < ints.Count; i += 3)
                    AddPackageReward(rewards, ints[i], ints[i + 1], ints[i + 2]);
            }

            return rewards;
        }

        private static void AddPackageReward(
            List<BoosterRewardEntry> rewards,
            int itemId,
            int count,
            int usablePeriodDays)
        {
            if (itemId <= 0)
                return;

            rewards.Add(new BoosterRewardEntry
            {
                RewardKind = "package",
                Group = 0,
                ItemId = itemId,
                Count = Math.Max(1, count),
                UsablePeriodDays = Math.Max(0, usablePeriodDays),
            });
        }

        private static List<BoosterRewardEntry> ParseUpgradableLegacyRewards(string intData)
        {
            var rewards = new List<BoosterRewardEntry>();
            var ints = ParseInts(intData);
            // [upgradable legacy] pots store rewards as itemId/weight/count triples in [int data].
            for (var i = 0; i + 2 < ints.Count; i += 3)
            {
                if (ints[i] <= 0)
                    continue;

                rewards.Add(new BoosterRewardEntry
                {
                    RewardKind = "upgradable legacy",
                    Group = 0,
                    ItemId = ints[i],
                    Weight = Math.Max(0, ints[i + 1]),
                    Count = Math.Max(1, ints[i + 2]),
                });
            }

            return rewards;
        }

        private static List<BoosterRewardEntry> ParseLegacyRewards(string intData)
        {
            var rewards = new List<BoosterRewardEntry>();
            var ints = ParseInts(intData);
            // [legacy] pots store rewards as itemId/weight pairs in [int data].
            for (var i = 0; i + 1 < ints.Count; i += 2)
            {
                if (ints[i] <= 0)
                    continue;

                rewards.Add(new BoosterRewardEntry
                {
                    RewardKind = "legacy",
                    Group = 0,
                    ItemId = ints[i],
                    Weight = Math.Max(0, ints[i + 1]),
                    Count = 1,
                });
            }

            return rewards;
        }

        private static List<BoosterRewardEntry> ParseRandomBoxRewards(ScriptNode randomBox, string content)
        {
            var rewards = new List<BoosterRewardEntry>();
            if (randomBox == null)
                return rewards;

            var fallbackGroup = 0;
            foreach (var node in randomBox.GetChildren("int data"))
            {
                fallbackGroup++;
                var rewardCountBeforeNode = rewards.Count;
                var ints = new List<int>();
                foreach (var item in node.DataItems)
                    ints.AddRange(ParseInts(item.GetContent(content)));

                if (ints.Count >= 7)
                {
                    for (var i = 3; i + 3 < ints.Count; i += 4)
                    {
                        if (ints[i] <= 0)
                            continue;

                        rewards.Add(new BoosterRewardEntry
                        {
                            RewardKind = "randombox",
                            Group = fallbackGroup,
                            ItemId = ints[i],
                            Weight = Math.Max(0, ints[i + 1]),
                            Count = Math.Max(1, ints[i + 2]),
                        });
                    }
                }

                if (rewards.Count == rewardCountBeforeNode && ints.Count >= 2 && ints[0] > 0)
                {
                    rewards.Add(new BoosterRewardEntry
                    {
                        RewardKind = "randombox",
                        Group = fallbackGroup,
                        ItemId = ints[0],
                        Weight = 10000,
                        Count = Math.Max(1, ints[1]),
                    });
                }
            }

            return rewards;
        }

        private static List<RandomBoxRemovalItemEntry> ParseRandomBoxRemovalItems(ScriptNode node, string content)
        {
            var result = new List<RandomBoxRemovalItemEntry>();
            if (node == null)
                return result;

            var ints = new List<int>();
            foreach (var item in node.DataItems)
                ints.AddRange(ParseInts(item.GetContent(content)));

            var start = 0;
            if (ints.Count >= 1 && ints.Count == 1 + ints[0] * 2)
                start = 1;

            for (var i = start; i + 1 < ints.Count; i += 2)
            {
                result.Add(new RandomBoxRemovalItemEntry
                {
                    ItemId = ints[i],
                    Count = Math.Max(0, ints[i + 1]),
                });
            }

            return result;
        }

        private static void ParseBoosterRewardNode(
            ScriptNode node,
            string content,
            string rewardKind,
            int fallbackGroup,
            List<BoosterRewardEntry> rewards,
            bool weighted)
        {
            if (node == null)
                return;

            var characterJobLabel = GetCharacterJobLabel(node, content);
            if (weighted && string.Equals(rewardKind, "charactor", StringComparison.OrdinalIgnoreCase))
            {
                ParseCharacterRewards(node, content, rewardKind, fallbackGroup, characterJobLabel, rewards);
                foreach (var child in node.Children)
                    ParseBoosterRewardNode(child, content, child.Tag, fallbackGroup, rewards, weighted);
                return;
            }

            var ints = new List<int>();
            foreach (var item in node.DataItems)
                ints.AddRange(ParseInts(item.GetContent(content)));

            if (weighted)
                AddWeightedRewards(ints, rewardKind, fallbackGroup, rewards);
            else
                AddPairRewards(ints, rewardKind, fallbackGroup, rewards);

            foreach (var child in node.Children)
                ParseBoosterRewardNode(child, content, child.Tag, fallbackGroup, rewards, weighted);
        }

        private static void AddWeightedRewards(
            List<int> ints,
            string rewardKind,
            int fallbackGroup,
            List<BoosterRewardEntry> rewards)
        {
            if (ints == null || ints.Count == 0)
                return;

            if ((string.Equals(rewardKind, "avatar", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rewardKind, "special avatar", StringComparison.OrdinalIgnoreCase))
                && ints.Count >= 6
                && (ints.Count - 1) % 5 == 0)
            {
                var avatarDrawCount = Math.Max(1, ints[0]);
                for (var i = 1; i + 4 < ints.Count; i += 5)
                {
                    if (ints[i] <= 0)
                        continue;

                    rewards.Add(new BoosterRewardEntry
                    {
                        RewardKind = rewardKind,
                        Group = fallbackGroup,
                        DrawCount = avatarDrawCount,
                        ItemId = ints[i],
                        Weight = Math.Max(0, ints[i + 1]),
                        Count = Math.Max(1, ints[i + 2]),
                    });
                }

                return;
            }

            if (string.Equals(rewardKind, "broadcast", StringComparison.OrdinalIgnoreCase)
                && ints.Count >= 5
                && (ints.Count - 1) % 4 == 0)
            {
                AddRewardsByStride(ints, 1, 4, Math.Max(1, ints[0]), rewardKind, fallbackGroup, rewards);
                return;
            }

            var group = fallbackGroup;
            var drawCount = 1;
            var start = 0;
            if (ints.Count >= 4 && (ints.Count - 1) % 3 == 0)
            {
                // Repeated [etc] blocks under [booster info] are independent
                // reward pools. The leading value belongs to the block itself,
                // so keep the parser-assigned block group instead of merging
                // identical tags such as two "[etc] 1 ..." sections.
                drawCount = Math.Max(1, ints[0]);
                start = 1;
            }

            for (var i = start; i + 2 < ints.Count; i += 3)
            {
                if (ints[i] <= 0)
                    continue;

                rewards.Add(new BoosterRewardEntry
                {
                    RewardKind = rewardKind,
                    Group = group,
                    DrawCount = drawCount,
                    ItemId = ints[i],
                    Weight = Math.Max(0, ints[i + 1]),
                    Count = Math.Max(1, ints[i + 2]),
                });
            }
        }

        private static void AddPairRewards(List<int> ints, string rewardKind, int fallbackGroup, List<BoosterRewardEntry> rewards)
        {
            if (ints == null || ints.Count == 0)
                return;

            if (string.Equals(rewardKind, "avatar", StringComparison.OrdinalIgnoreCase)
                && ints.Count % 4 == 0)
            {
                for (var i = 0; i + 3 < ints.Count; i += 4)
                {
                    if (ints[i] <= 0)
                        continue;

                    rewards.Add(new BoosterRewardEntry
                    {
                        RewardKind = rewardKind,
                        Group = fallbackGroup,
                        ItemId = ints[i],
                        Count = Math.Max(1, ints[i + 1]),
                    });
                }

                return;
            }

            if (string.Equals(rewardKind, "default select", StringComparison.OrdinalIgnoreCase))
            {
                var itemCount = Math.Min(Math.Max(0, ints[0]), ints.Count - 1);
                for (var i = 0; i < itemCount; i++)
                {
                    var itemId = ints[i + 1];
                    if (itemId <= 0)
                        continue;

                    rewards.Add(new BoosterRewardEntry
                    {
                        RewardKind = rewardKind,
                        Group = fallbackGroup,
                        ItemId = itemId,
                        Count = 1,
                    });
                }

                return;
            }

            if (string.Equals(rewardKind, "random probability", StringComparison.OrdinalIgnoreCase)
                || rewardKind.StartsWith("booster equipment ", StringComparison.OrdinalIgnoreCase))
                return;

            var start = (ints.Count % 2) == 1 ? 1 : 0;
            var group = start == 1 ? ints[0] : fallbackGroup;
            for (var i = start; i + 1 < ints.Count; i += 2)
            {
                if (ints[i] <= 0)
                    continue;

                rewards.Add(new BoosterRewardEntry
                {
                    RewardKind = rewardKind,
                    Group = group,
                    ItemId = ints[i],
                    Count = Math.Max(1, ints[i + 1]),
                });
            }
        }

        private static void AddRewardsByStride(
            List<int> ints,
            int start,
            int stride,
            int drawCount,
            string rewardKind,
            int group,
            List<BoosterRewardEntry> rewards)
        {
            for (var i = start; i + 2 < ints.Count; i += stride)
            {
                if (ints[i] <= 0)
                    continue;

                rewards.Add(new BoosterRewardEntry
                {
                    RewardKind = rewardKind,
                    Group = group,
                    DrawCount = drawCount,
                    ItemId = ints[i],
                    Weight = Math.Max(0, ints[i + 1]),
                    Count = Math.Max(1, ints[i + 2]),
                });
            }
        }

        private static void ParseCharacterRewards(
            ScriptNode node,
            string content,
            string rewardKind,
            int group,
            string characterJobLabel,
            List<BoosterRewardEntry> rewards)
        {
            var values = new List<decimal>();
            foreach (var item in node.DataItems)
            {
                var matches = Regex.Matches(item.GetContent(content) ?? string.Empty, @"-?\d+(?:\.\d+)?");
                foreach (Match match in matches)
                {
                    if (decimal.TryParse(match.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                        values.Add(value);
                }
            }

            if (values.Count < 3 || values.Count % 3 != 0)
                return;

            var maxWeightScale = 0;
            for (var i = 4; i + 1 < values.Count; i += 3)
            {
                var scale = (decimal.GetBits(values[i])[3] >> 16) & 0x7F;
                maxWeightScale = Math.Max(maxWeightScale, scale);
            }
            var weightScale = 1m;
            for (var digit = 0; digit < maxWeightScale; digit++)
                weightScale *= 10m;

            var drawCount = Math.Max(1, decimal.ToInt32(decimal.Truncate(values[0])));
            for (var i = 3; i + 2 < values.Count; i += 3)
            {
                var itemId = decimal.ToInt32(decimal.Truncate(values[i]));
                if (itemId <= 0)
                    continue;

                rewards.Add(new BoosterRewardEntry
                {
                    RewardKind = rewardKind,
                    CharacterJobLabel = characterJobLabel,
                    Group = group,
                    DrawCount = drawCount,
                    ItemId = itemId,
                    Weight = Math.Max(0, decimal.ToInt32(values[i + 1] * weightScale)),
                    Count = Math.Max(1, decimal.ToInt32(decimal.Truncate(values[i + 2]))),
                });
            }
        }

        private static string GetCharacterJobLabel(ScriptNode node, string content)
        {
            if (node == null || !string.Equals(node.Tag, "charactor", StringComparison.OrdinalIgnoreCase))
                return null;

            foreach (var item in node.DataItems)
            {
                var match = Regex.Match(item.GetContent(content) ?? string.Empty, @"`?\[(?<job>[^\]]+)\]`?");
                if (match.Success)
                    return match.Groups["job"].Value.Trim();
            }

            return null;
        }

        private static List<int> ParseInts(string text)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            var matches = Regex.Matches(text, @"-?\d+");
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Value, out var value))
                    result.Add(value);
            }

            return result;
        }

        private static List<string> ParseStringList(ScriptNode node, string content)
        {
            var result = new List<string>();
            if (node == null || node.DataItems == null)
                return result;

            foreach (var item in node.DataItems)
            {
                var raw = item.GetContent(content).Trim();
                var matches = Regex.Matches(raw, "`([^`]*)`");
                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        var value = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                            result.Add(value);
                    }
                    continue;
                }

                var fallback = StripBacktick(raw);
                if (!string.IsNullOrWhiteSpace(fallback))
                    result.Add(fallback);
            }

            return result;
        }

        private static void ParseExpertJobOnly(ScriptNode node, string content, StackableItemFile item)
        {
            if (node == null || item == null)
                return;

            var raw = node.GetFirstDataContent(content);
            var typeMatch = Regex.Match(raw ?? string.Empty, "`([^`]*)`");
            var values = ParseInts(raw);
            if (typeMatch.Success)
                item.ExpertJobOnlyType = typeMatch.Groups[1].Value.Trim();
            if (values.Count > 0)
                item.ExpertJobOnlyLevel = values[0];
        }

        private static List<AvatarSelectAbilityChangeEntry> ParseAvatarSelectAbilityChanges(
            ScriptNode node,
            string content)
        {
            var result = new List<AvatarSelectAbilityChangeEntry>();
            if (node == null || node.DataItems == null)
                return result;

            string pendingSourceScope = null;
            foreach (var item in node.DataItems)
            {
                var raw = item.GetContent(content) ?? string.Empty;
                var scopes = ParseBracketScopes(raw);
                var values = ParseInts(raw);
                if (values.Count == 0)
                {
                    if (scopes.Count > 0)
                        pendingSourceScope = scopes[0];
                    continue;
                }

                result.Add(new AvatarSelectAbilityChangeEntry
                {
                    SourceScope = scopes.Count > 1 ? scopes[0] : pendingSourceScope,
                    TargetScope = scopes.Count > 1 ? scopes[1] : scopes.Count > 0 ? scopes[0] : null,
                    AvatarGrade = values[0],
                    Param = values.Count > 1 ? values[1] : -1,
                });
            }

            return result;
        }

        private static List<string> ParseBracketScopes(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (Match match in Regex.Matches(text, "`?\\[(?<scope>[^\\]]+)\\]`?"))
            {
                var scope = match.Groups["scope"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(scope))
                    result.Add("[" + scope + "]");
            }

            return result;
        }

        private static byte ResolveAvatarEmblemSocketType(IEnumerable<string> targetTypes)
        {
            byte socketType = 0;
            if (targetTypes == null)
                return socketType;

            foreach (var targetType in targetTypes)
                socketType |= MapAvatarEmblemTargetType(targetType);

            return socketType;
        }

        private static byte MapAvatarEmblemTargetType(string targetType)
        {
            if (string.IsNullOrWhiteSpace(targetType))
                return 0;

            var match = Regex.Match(targetType, @"\[\s*([ABCDSM])\s+socket\s*\]", RegexOptions.IgnoreCase);
            if (!match.Success || match.Groups.Count < 2)
                return 0;

            switch (char.ToUpperInvariant(match.Groups[1].Value[0]))
            {
                case 'A':
                    return 0x01;
                case 'B':
                    return 0x02;
                case 'C':
                    return 0x04;
                case 'D':
                    return 0x08;
                case 'S':
                    return 0x10;
                case 'M':
                    return 0xEF;
                default:
                    return 0;
            }
        }

        private static List<int> ParseIntList(ScriptNode node, string content)
        {
            return PvfScriptValueReader.ReadIntegers(node, content);
        }

        private static int ResolveFirstPositive(List<int> values, int fallback)
        {
            if (values != null)
            {
                foreach (var value in values)
                {
                    if (value > 0)
                        return value;
                }
            }

            return fallback;
        }

        private static UpgradeLimitCubeInfo ParseUpgradeLimitCubeInfo(
            ScriptNode node,
            string content)
        {
            if (node == null)
                return null;

            var info = new UpgradeLimitCubeInfo();
            if (!TryParseLimitedCubeRequirements(
                    node.GetChild("A condition item"),
                    content,
                    required: true,
                    info.ConditionItems)
                || !TryParseLimitedCubeRequirements(
                    node.GetChild("B condition item"),
                    content,
                    required: false,
                    info.AdditionalMaterials))
            {
                return null;
            }

            var resultValues = PvfScriptValueReader.ReadIntegers(
                node.GetChild("result item"),
                content);
            if (resultValues.Count == 0 || resultValues.Count % 3 != 0)
                return null;

            for (var index = 0; index < resultValues.Count; index += 3)
            {
                var itemId = resultValues[index];
                var resultValue = resultValues[index + 1];
                var weight = resultValues[index + 2];
                if (itemId <= 0
                    || resultValue <= 0
                    || resultValue > short.MaxValue
                    || weight <= 0)
                {
                    return null;
                }

                info.Results.Add(new LimitedCubeResultEntry
                {
                    ItemId = itemId,
                    ResultValue = (short)resultValue,
                    Weight = weight,
                });
            }

            return info;
        }

        private static bool TryParseLimitedCubeRequirements(
            ScriptNode node,
            string content,
            bool required,
            ICollection<LimitedCubeItemRequirement> result)
        {
            var values = PvfScriptValueReader.ReadIntegers(node, content);
            if ((required && values.Count == 0) || values.Count % 2 != 0)
                return false;

            for (var index = 0; index < values.Count; index += 2)
            {
                if (values[index] <= 0 || values[index + 1] <= 0)
                    return false;

                result.Add(new LimitedCubeItemRequirement
                {
                    ItemId = values[index],
                    Count = values[index + 1],
                });
            }

            return true;
        }

        private static void ParseActionType(ScriptNode node, string content, StackableItemFile stk)
        {
            if (node == null || node.DataItems == null)
                return;

            foreach (var item in node.DataItems)
            {
                var raw = item.GetContent(content);
                var nameMatch = Regex.Match(raw, "`([^`]*)`");
                if (!nameMatch.Success)
                    continue;

                stk.ActionTypeName = nameMatch.Groups[1].Value.Trim();
                var rest = raw.Substring(nameMatch.Index + nameMatch.Length);
                foreach (var token in rest.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(token, out var value))
                        stk.ActionTypeParams.Add(value);
                }
                break;
            }
        }

        private static EquipmentUpgradeTicketInfo ParseUpgradeTicket(ScriptNode node, string content)
        {
            var ticket = new EquipmentUpgradeTicketInfo();
            if (node == null || node.DataItems == null)
                return ticket;

            foreach (var item in node.DataItems)
            {
                var raw = item.GetContent(content);
                var nameMatch = Regex.Match(raw, "`([^`]*)`");
                if (nameMatch.Success)
                {
                    ApplyUpgradeTicketValues(ticket, ParseInts(raw.Substring(0, nameMatch.Index)));
                    ticket.ApplyMode = nameMatch.Groups[1].Value.Trim();
                    var rest = raw.Substring(nameMatch.Index + nameMatch.Length);
                    var modeValues = ParseInts(rest);
                    if (modeValues.Count > 0)
                        ticket.ApplyValue = modeValues[0];
                    if (modeValues.Count > 1)
                        ticket.ExtraValues.AddRange(modeValues.GetRange(1, modeValues.Count - 1));
                    continue;
                }

                var values = ParseInts(raw);
                if (values.Count >= 2 && ticket.TargetLevel < 0)
                {
                    ticket.TargetLevel = values[0];
                    ticket.SuccessRatePercent = values[1];
                    if (values.Count > 2)
                        ticket.ExtraValues.AddRange(values.GetRange(2, values.Count - 2));
                }
                else if (values.Count > 0)
                {
                    ticket.ExtraValues.AddRange(values);
                }
            }

            return ticket;
        }

        private static void ApplyUpgradeTicketValues(EquipmentUpgradeTicketInfo ticket, List<int> values)
        {
            if (ticket == null || values == null || values.Count == 0)
                return;

            if (values.Count >= 2 && ticket.TargetLevel < 0)
            {
                ticket.TargetLevel = values[0];
                ticket.SuccessRatePercent = values[1];
                if (values.Count > 2)
                    ticket.ExtraValues.AddRange(values.GetRange(2, values.Count - 2));
                return;
            }

            ticket.ExtraValues.AddRange(values);
        }

        private static EnchantRandomUpgradeInfo ParseEnchantRandomUpgrade(ScriptNode node, string content)
        {
            var info = new EnchantRandomUpgradeInfo();
            if (node == null)
                return info;

            foreach (var child in node.Children)
            {
                switch (child.Tag.ToLowerInvariant())
                {
                    case "er_grade":
                        info.RarityRestrictions = ParseIntList(child, content);
                        break;
                    case "er_slot":
                        info.SlotRestriction = ParseFirstInt(child, content);
                        break;
                    case "er_seal":
                        info.SealRestriction = ParseFirstInt(child, content);
                        break;
                    case "er_enchant":
                        info.EnchantEntries = ParseEnchantRandomEntries(child, content);
                        break;
                }
            }

            return info;
        }

        private static ThreeChronicleEnchantInfo ParseThreeChronicleEnchant(ScriptNode root, ScriptNode node, string content)
        {
            var info = new ThreeChronicleEnchantInfo();
            if (node == null)
                return info;

            var probability = node.GetChild("probability") ?? root?.GetChild("probability");
            if (probability != null)
                info.Probabilities = ParseIntList(probability, content);

            var checks = new List<ScriptNode>();
            checks.AddRange(node.GetChildren("check"));
            if (root != null)
            {
                foreach (var rootCheck in root.GetChildren("check"))
                {
                    if (!checks.Contains(rootCheck))
                        checks.Add(rootCheck);
                }
            }
            if (checks.Count == 0)
                return info;

            foreach (var check in checks)
            {
                var parsedCheck = new ThreeChronicleEnchantCheck();
                foreach (var item in check.DataItems)
                {
                    var raw = item.GetContent(content).Trim();
                    var values = System.Text.RegularExpressions.Regex.Matches(raw, @"-?\d+");
                    foreach (System.Text.RegularExpressions.Match value in values)
                    {
                        if (int.TryParse(value.Value, out var parsed))
                            parsedCheck.Values.Add(parsed);
                    }

                    var equipmentType = System.Text.RegularExpressions.Regex.Match(raw, @"`(?<type>[^`]+)`");
                    if (equipmentType.Success && parsedCheck.EquipmentType == null)
                    {
                        parsedCheck.EquipmentType = equipmentType.Groups["type"].Value.Trim();
                    }
                    else if (values.Count == 0)
                    {
                        var token = StripBacktick(raw);
                        if (!string.IsNullOrWhiteSpace(token) && parsedCheck.EquipmentType == null)
                            parsedCheck.EquipmentType = token.Trim();
                    }
                }

                foreach (var skillNode in check.GetChildren("skill"))
                {
                    if (skillNode.DataItems.Count == 0)
                        continue;

                    var definition = string.Empty;
                    foreach (var item in skillNode.DataItems)
                        definition += " " + item.GetContent(content).Trim();
                    var match = System.Text.RegularExpressions.Regex.Match(
                        definition,
                        @"^\s*(?<optionNo>-?\d+)\s+`?\[(?<job>[^\]]+)\]`?\s+(?<skillId>-?\d+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!match.Success
                        || !int.TryParse(match.Groups["optionNo"].Value, out var optionNo)
                        || optionNo < 0
                        || !int.TryParse(match.Groups["skillId"].Value, out var skillId))
                        continue;

                    var skill = new ThreeChronicleSkillOption
                    {
                        OptionNo = optionNo,
                        Job = "[" + match.Groups["job"].Value.Trim().ToLowerInvariant() + "]",
                        SkillId = skillId,
                    };
                    parsedCheck.Skills.Add(skill);
                    info.Skills.Add(skill);
                }

                info.Checks.Add(parsedCheck);
            }

            return info;
        }

        private static List<EnchantRandomUpgradeEntry> ParseEnchantRandomEntries(ScriptNode node, string content)
        {
            var result = new List<EnchantRandomUpgradeEntry>();
            var values = ParseIntList(node, content);
            var start = 0;
            if (values.Count > 0 && values.Count == 1 + values[0] * 2)
                start = 1;

            for (var i = start; i + 1 < values.Count; i += 2)
            {
                result.Add(new EnchantRandomUpgradeEntry
                {
                    TargetLevel = values[i],
                    SuccessWeight = values[i + 1],
                });
            }

            return result;
        }

        private static List<AmplificationRandomValueEntry> ParseAmplificationRandomValues(ScriptNode node, string content)
        {
            var result = new List<AmplificationRandomValueEntry>();
            var values = ParseIntList(node, content);
            for (var i = 0; i + 1 < values.Count; i += 2)
            {
                if (values[i] < 0 || values[i + 1] <= 0)
                    continue;

                result.Add(new AmplificationRandomValueEntry
                {
                    UpgradeLevel = values[i],
                    Weight = values[i + 1],
                });
            }

            return result;
        }

        private static int ParseFirstInt(ScriptNode node, string content)
        {
            var values = ParseIntList(node, content);
            return values.Count > 0 ? values[0] : -1;
        }

        private static EquipmentLevelEmancipateInfo ParseEquipmentLevelEmancipate(ScriptNode node, string content)
        {
            var info = new EquipmentLevelEmancipateInfo();
            if (node == null)
                return info;

            foreach (var child in node.Children)
            {
                switch (child.Tag.ToLowerInvariant())
                {
                    case "probability":
                        var probabilityValues = ParseIntList(child, content);
                        for (var i = 0; i + 1 < probabilityValues.Count; i += 2)
                        {
                            info.Probabilities.Add(new EquipmentLevelEmancipateProbability
                            {
                                MaximumLevel = probabilityValues[i],
                                Weight = probabilityValues[i + 1],
                            });
                        }
                        break;
                    case "equipment upgrade level":
                        info.UpgradeLevel = ParseFirstInt(child, content);
                        break;
                    case "equipment condition":
                        info.Condition = ParseEquipmentLevelEmancipateCondition(child, content);
                        break;
                    case "ignore index":
                        info.IgnoreIndexes = ParseIntList(child, content);
                        break;
                }
            }

            return info;
        }

        private static EquipmentLevelEmancipateCondition ParseEquipmentLevelEmancipateCondition(ScriptNode node, string content)
        {
            var condition = new EquipmentLevelEmancipateCondition();
            if (node == null)
                return condition;

            foreach (var child in node.Children)
            {
                switch (child.Tag.ToLowerInvariant())
                {
                    case "rarity": condition.Rarities = ParseIntList(child, content); break;
                    case "minimum level": condition.MinimumLevel = ParseFirstInt(child, content); break;
                    case "maximum level": condition.MaximumLevel = ParseFirstInt(child, content); break;
                }
            }

            // ScriptParser's legacy nested-block boundary can leave the final
            // unclosed scalar child without a DataItem. Keep the compatibility
            // local to this new PVF model so existing parsers retain their behavior.
            if (condition.MaximumLevel < 0)
                condition.MaximumLevel = ParseTaggedInt(node.GetContent(content), "maximum level");
            return condition;
        }

        private static int ParseTaggedInt(string block, string tag)
        {
            if (string.IsNullOrWhiteSpace(block) || string.IsNullOrWhiteSpace(tag))
                return -1;

            var match = Regex.Match(block,
                @"(?im)^\s*\[" + Regex.Escape(tag) + @"\]\s*\r?\n\s*(?<value>-?\d+)");
            return match.Success && int.TryParse(match.Groups["value"].Value, out var value) ? value : -1;
        }

        private static List<int> ParseEnchantTableIndexes(ScriptNode node, string content)
        {
            var result = new List<int>();
            if (node == null)
                return result;

            foreach (var enchantIndexNode in node.GetChildren("enchant index"))
            {
                var rawIndex = enchantIndexNode.GetFirstDataContent(content);
                var index = ParseInt(rawIndex);
                if (index >= 0 && !result.Contains(index))
                    result.Add(index);
            }

            return result;
        }

        #endregion
    }
}
