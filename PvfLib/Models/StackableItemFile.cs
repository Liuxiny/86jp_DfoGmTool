using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GmPvfLib
{
    public class BoosterRewardEntry
    {
        public string RewardKind { get; set; }
        public int Group { get; set; }
        public int DrawCount { get; set; } = 1;
        public int ItemId { get; set; }
        public int Weight { get; set; } = 10000;
        public int Count { get; set; } = 1;
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
        public int SubType { get; set; } = -1;
        public string AttachType { get; set; }
        public string ItemGroupName { get; set; }
        public string ItemCategory { get; set; }
        public int StackLimit { get; set; } = -1;

        #endregion

        #region 经济属性

        public int Price { get; set; } = -1;
        public int Value { get; set; } = -1;
        public int Weight { get; set; } = -1;
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

        #endregion

        #region 强化/合成

        public int EnchantIndex { get; set; } = -1;
        public List<int> EnchantTable { get; set; } = new List<int>();
        // [action type] `[xxx]` p1 p2 ...: ActionTypeName="[xxx]", ActionTypeParams=[p1,p2,...]
        public string ActionTypeName { get; set; }
        public List<int> ActionTypeParams { get; set; } = new List<int>();
        public EquipmentUpgradeTicketInfo EquipmentReinforcementTicket { get; set; }
        public EquipmentUpgradeTicketInfo EquipmentAmplifyReinforcementTicket { get; set; }
        public EnchantRandomUpgradeInfo EnchantRandomUpgrade { get; set; }
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

        #endregion

        #region 关联数据

        
        public string Equipment { get; set; }
        
        public string StringData { get; set; }

        public List<string> StringDataItems { get; set; } = new List<string>();
        
        public string IntData { get; set; }
        
        public string PackageData { get; set; }
        public List<BoosterRewardEntry> PackageRewards { get; set; } = new List<BoosterRewardEntry>();
        public List<BoosterRewardEntry> RandomBoxRewards { get; set; } = new List<BoosterRewardEntry>();
        public List<RandomBoxRemovalItemEntry> RandomBoxRemovalItems { get; set; } = new List<RandomBoxRemovalItemEntry>();
        public string OutputItem { get; set; }
        public string InputItem { get; set; }
        public string NeedSkill { get; set; }
        public string NeedMaterial { get; set; }
        public int MonsterCardId { get; set; } = -1;
        public List<int> TargetItemIds { get; set; } = new List<int>();

        #endregion

        #region 战斗属性

        public int PhysicalAttack { get; set; }
        public int MagicalAttack { get; set; }
        public int PhysicalDefense { get; set; }
        public int MagicalDefense { get; set; }

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
                    case "sub type": stk.SubType = ParseInt(data); break;
                    case "attach type": stk.AttachType = StripBacktick(data); break;
                    case "item group name": stk.ItemGroupName = StripBacktick(data); break;
                    case "item category": stk.ItemCategory = StripBacktick(data); break;
                    case "stack limit": stk.StackLimit = ParseInt(data); break;

                    
                    case "price": stk.Price = ParseInt(data); break;
                    case "value": stk.Value = ParseInt(data); break;
                    case "weight": stk.Weight = ParseInt(data); break;
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

                    
                    case "enchant index": stk.EnchantIndex = ParseInt(data); break;
                    case "enchant table": stk.EnchantTable = ParseEnchantTableIndexes(node, content); break;
                    case "action type": ParseActionType(node, content, stk); break;
                    case "equipment reinforcement ticket": stk.EquipmentReinforcementTicket = ParseUpgradeTicket(node, content); break;
                    case "equipment amplify reinforcement ticket": stk.EquipmentAmplifyReinforcementTicket = ParseUpgradeTicket(node, content); break;
                    case "enchant random": stk.EnchantRandomUpgrade = ParseEnchantRandomUpgrade(node, content); break;
                    case "check usable itemlevel": stk.CheckUsableItemLevels = ParseIntList(node, content); break;
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
                    case "package data": stk.PackageData = data; break;
                    case "output item": stk.OutputItem = data; break;
                    case "input item": stk.InputItem = data; break;
                    case "need skill": stk.NeedSkill = data; break;
                    case "need material": stk.NeedMaterial = data; break;
                    case "monster card id": stk.MonsterCardId = ParseInt(data); break;
                    case "target item id": stk.TargetItemIds = ParseIntList(node, content); break;

                    
                    case "physical attack": stk.PhysicalAttack = ParseInt(data); break;
                    case "magical attack": stk.MagicalAttack = ParseInt(data); break;
                    case "physical defense": stk.PhysicalDefense = ParseInt(data); break;
                    case "magical defense": stk.MagicalDefense = ParseInt(data); break;
                }
            }

            stk.BoosterRewards = ParseBoosterInfo(root.GetChild("booster info"), content);
            stk.BoosterSelectionRewards = ParseBoosterSelection(root.GetChildren("booster select category"), content);
            stk.PackageRewards = ParsePackageRewards(stk.PackageData);
            var randomBox = root.GetChild("RANDOMBOX");
            stk.RandomBoxRewards = ParseRandomBoxRewards(randomBox, content);
            stk.RandomBoxRemovalItems = ParseRandomBoxRemovalItems(randomBox != null ? randomBox.GetChild("sealing removal item") : null, content);

            return stk;
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

        private static List<BoosterRewardEntry> ParsePackageRewards(string packageData)
        {
            var rewards = new List<BoosterRewardEntry>();
            var ints = ParseInts(packageData);
            for (var i = 0; i + 1 < ints.Count; i += 2)
            {
                if (ints[i] <= 0)
                    continue;

                rewards.Add(new BoosterRewardEntry
                {
                    RewardKind = "package",
                    Group = 0,
                    ItemId = ints[i],
                    Count = Math.Max(1, ints[i + 1]),
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

        private static void AddWeightedRewards(List<int> ints, string rewardKind, int fallbackGroup, List<BoosterRewardEntry> rewards)
        {
            if (ints == null || ints.Count == 0)
                return;

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

        private static List<int> ParseIntList(ScriptNode node, string content)
        {
            var result = new List<int>();
            if (node == null || node.DataItems == null)
                return result;

            foreach (var item in node.DataItems)
            {
                var raw = item.GetContent(content);
                foreach (var token in raw.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(StripBacktick(token), out var value))
                        result.Add(value);
                }
            }

            return result;
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

        private static int ParseFirstInt(ScriptNode node, string content)
        {
            var values = ParseIntList(node, content);
            return values.Count > 0 ? values[0] : -1;
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
