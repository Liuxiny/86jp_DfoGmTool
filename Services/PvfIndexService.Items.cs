using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DfoGmTool.ServerCore.Game.Inventory;
using GmPvfLib;

namespace DfoGmTool.Services
{
    public sealed partial class PvfIndexService
    {
        public sealed class ItemEntry
        {
            public int Id;
            public string Name;
            public string Kind;      // equipment / stackable
            public string TypeTag;   // [weapon]/[coat]/[material]/... 的首个标签(去壳小写)
            public string Segment;   // 堆叠物的背包入格分类(与服务端 GetSlotRange 同语义), 装备为 null
            public string Special;   // 品质细分: legacy(传承)/boss(领主神器)/sealed(魔法封印), 无则 null
            public int Rarity;
            public int MinLevel;
            public int Grade;
            public string UsableJob;
            public int AbsoluteExpirationUnixTime;
            public int UsablePeriodDays;
            public bool DailyDeleteItem;
            public bool HasInvalidExpirationDefinition;
            public bool RequiresManualGrantType;
        }

        public readonly struct ItemExpirationDefinition
        {
            internal ItemExpirationDefinition(
                bool isKnown,
                int absoluteExpirationUnixTime,
                int usablePeriodDays,
                bool dailyDeleteItem,
                bool hasInvalidDefinition)
            {
                IsKnown = isKnown;
                AbsoluteExpirationUnixTime = absoluteExpirationUnixTime;
                UsablePeriodDays = usablePeriodDays;
                DailyDeleteItem = dailyDeleteItem;
                HasInvalidDefinition = hasInvalidDefinition;
            }

            public bool IsKnown { get; }

            public int AbsoluteExpirationUnixTime { get; }

            public int UsablePeriodDays { get; }

            public bool DailyDeleteItem { get; }

            public bool HasInvalidDefinition { get; }
        }

        private static readonly Regex ItemCategoryPattern = new Regex(
            @"\[item category\]\s*`?([^`\r\n\[]+)", RegexOptions.Compiled);

        // 品质细分识别(均经实物验证):
        //   [item category] legacy    → 传承(紫, 10104 传承:智慧女神的纱棉长袍)
        //   [item category] boss drop → 领主神器(100300063 凝视者之眸)
        //   [random option]           → 魔法封印(2224104 密制镇魂安曲剑, "(魔法封印)"前缀是客户端运行时加的)
        private static string EquipSpecial(string text)
        {
            var category = ItemCategoryPattern.Match(text);
            if (category.Success)
            {
                var value = category.Groups[1].Value.Trim();
                if (value == "legacy")
                    return "legacy";
                if (value == "boss drop")
                    return "boss";
            }
            if (text.Contains("[random option]"))
                return "sealed";
            return null;
        }

        // 与服务端 ItemMetadataResolver.GetSlotRange 同语义的背包分类
        private static string StackSegment(string stackableType)
        {
            if (string.IsNullOrWhiteSpace(stackableType))
                return "消耗品";
            var st = stackableType.Replace("`", "").Trim().ToLowerInvariant();
            if (st.StartsWith("[material]"))
                return st.EndsWith("4") ? "特殊材料" : "材料";
            if (st.StartsWith("[quest]"))
                return "任务品";
            if (st.StartsWith("[material expert job]"))
                return "副职业材料";
            if (st.StartsWith("[avatar emblem]"))
                return "徽章";
            return "消耗品";
        }

        public IReadOnlyList<ItemEntry> AllItems => _searchList;

        public string ResolveItemName(int itemId)
        {
            var names = _itemNames;
            if (names == null)
                return null;
            string name;
            return names.TryGetValue(itemId, out name) ? name : null;
        }

        public string ResolveItemKind(int itemId)
        {
            var kinds = _itemKinds;
            if (kinds == null)
                return null;
            string kind;
            return kinds.TryGetValue(itemId, out kind) ? kind : null;
        }

        // 品级(0-6), 索引未就绪或未知物品返回 -1(前端按 -1 不着色)
        public int ResolveItemRarity(int itemId)
        {
            var rarities = _itemRarities;
            if (rarities == null)
                return -1;
            int rarity;
            return rarities.TryGetValue(itemId, out rarity) ? rarity : -1;
        }

        public ItemExpirationDefinition ResolveItemExpiration(int itemId)
        {
            var expirations = _itemExpirations;
            if (expirations == null)
                return default;

            return expirations.TryGetValue(itemId, out var expiration)
                ? expiration
                : default;
        }

        // 发放界面的分类清单: 装备按部位标签, 堆叠物按背包入格分类(与背包页同款)
        public object GetItemCategories()
        {
            var list = _searchList;
            if (list == null)
                return new { ready = false, equipment = new object[0], stackable = new object[0] };

            var equipment = list
                .Where(e => e.Kind == "equipment")
                .GroupBy(e => e.TypeTag ?? "(无标签)")
                .Select(g => (object)new { tag = g.Key, count = g.Count() })
                .ToArray();

            var stackable = list
                .Where(e => e.Kind == "stackable")
                .GroupBy(e => e.Segment ?? "消耗品")
                .Select(g => (object)new { segment = g.Key, count = g.Count() })
                .ToArray();

            return new { ready = true, equipment, stackable };
        }

        public object SearchItems(string query, string kind, string tag, string segment, string special, int minLevel, int maxLevel, int rarity, int limit, int offset, string expiration, int characterJob = -1)
        {
            var list = _searchList;
            if (list == null)
                return new { success = false, error = _buildError != null ? "索引构建失败: " + _buildError : "物品索引还在构建中, 稍等几秒再搜" };

            if (limit <= 0 || limit > 200)
                limit = 100;
            if (offset < 0)
                offset = 0;

            query = (query ?? "").Trim();
            var numericId = -1;
            if (query.Length > 0)
                int.TryParse(query, out numericId);
            if (numericId <= 0)
                numericId = -1;

            expiration = (expiration ?? string.Empty).Trim().ToLowerInvariant();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var filtered = new List<ItemEntry>();
            foreach (var entry in list)
            {
                if (kind != null && entry.Kind != kind)
                    continue;
                if (tag != null && (entry.TypeTag ?? "(无标签)") != tag)
                    continue;
                if (segment != null && (entry.Segment ?? "消耗品") != segment)
                    continue;
                if (minLevel > 0 && entry.MinLevel < minLevel)
                    continue;
                if (maxLevel > 0 && entry.MinLevel > maxLevel)
                    continue;
                if (rarity >= 0 && entry.Rarity != rarity)
                    continue;
                if (special != null && entry.Special != special)
                    continue;
                if (!MatchesExpirationFilter(entry, expiration, now))
                    continue;
                if (characterJob >= 0
                    && IsAvatarType(entry.TypeTag)
                    && !AvatarGrantPolicy.IsUsableByJob(entry.UsableJob, characterJob))
                    continue;
                if (query.Length > 0
                    && entry.Id != numericId
                    && (entry.Name == null || entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                filtered.Add(entry);
            }

            var page = filtered.Skip(offset).Take(limit)
                .Select(e => (object)new
                {
                    itemId = e.Id,
                    name = e.Name,
                    kind = e.Kind,
                    tag = e.TypeTag,
                    segment = e.Segment,
                    special = e.Special,
                    rarity = e.Rarity,
                    minLevel = e.MinLevel,
                    grade = e.Grade,
                    usableJob = e.UsableJob,
                    requiresManualGrantType = e.RequiresManualGrantType,
                    templateExpiration = new
                    {
                        known = true,
                        absoluteExpireTime = e.AbsoluteExpirationUnixTime,
                        usablePeriodDays = e.UsablePeriodDays,
                        dailyDeleteItem = e.DailyDeleteItem,
                        invalid = e.HasInvalidExpirationDefinition,
                    },
                })
                .ToArray();

            return new { success = true, total = filtered.Count, offset, count = page.Length, results = page };
        }

        private static bool MatchesExpirationFilter(ItemEntry entry, string filter, long now)
        {
            var hasAbsoluteExpiration = entry.AbsoluteExpirationUnixTime > 0;
            var hasRelativeExpiration = entry.UsablePeriodDays > 0;
            var hasDailyDeletion = entry.DailyDeleteItem;

            switch (filter)
            {
                case "limited":
                    return hasAbsoluteExpiration || hasRelativeExpiration || hasDailyDeletion;
                case "none":
                    return !entry.HasInvalidExpirationDefinition
                        && !hasAbsoluteExpiration
                        && !hasRelativeExpiration
                        && !hasDailyDeletion;
                case "relative":
                    return hasRelativeExpiration;
                case "absolute":
                    return hasAbsoluteExpiration;
                case "daily":
                    return hasDailyDeletion;
                case "expired":
                    return hasAbsoluteExpiration && entry.AbsoluteExpirationUnixTime <= now;
                default:
                    return true;
            }
        }

        private static bool IsAvatarType(string typeTag)
        {
            return !string.IsNullOrWhiteSpace(typeTag)
                && typeTag.EndsWith(" avatar", StringComparison.OrdinalIgnoreCase);
        }

        public object Search(string query, int limit)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new { success = false, error = "query 不能为空" };
            if (limit <= 0 || limit > 100)
                limit = 30;

            var list = _searchList;
            if (list == null)
                return new { success = false, error = _buildError != null ? "索引构建失败: " + _buildError : "物品索引还在构建中, 稍等几秒再搜" };

            query = query.Trim();
            int numericId;
            var isNumeric = int.TryParse(query, out numericId);

            var results = new List<object>();
            foreach (var entry in list)
            {
                if (results.Count >= limit)
                    break;
                if ((isNumeric && entry.Id == numericId) ||
                    (entry.Name != null && entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    results.Add(new { itemId = entry.Id, name = entry.Name, kind = entry.Kind });
                }
            }

            return new { query, count = results.Count, results };
        }

        private static readonly Regex TagPattern = new Regex(@"\[([a-z ]+)\]", RegexOptions.Compiled);

        private static string FirstTag(string typeString)
        {
            return ItemMetadataResolver.FirstPvfTypeTag(typeString);
        }

        private static ItemExpirationDefinition ResolveEquipmentExpiration(EquipmentFile equipment)
        {
            var rawExpiration = equipment.GetStringValue("expiration date");
            if (string.IsNullOrWhiteSpace(rawExpiration) || rawExpiration.Trim() == "0")
                return new ItemExpirationDefinition(true, 0, 0, false, false);

            return ItemGrantExpirationResolver.TryParsePvfExpirationUnixTime(
                rawExpiration,
                -1,
                out var absoluteExpiration)
                ? new ItemExpirationDefinition(true, absoluteExpiration, 0, false, false)
                : new ItemExpirationDefinition(true, 0, 0, false, true);
        }

        private static ItemExpirationDefinition ResolveStackableExpiration(StackableItemFile stackable)
        {
            if (!StackableExpirationPolicyResolver.TryResolve(stackable, out var policy))
                return new ItemExpirationDefinition(true, 0, 0, false, true);

            return new ItemExpirationDefinition(
                true,
                policy.AbsoluteExpirationUnixTime,
                policy.UsablePeriodDays,
                policy.DailyDeleteItem,
                false);
        }

        private void BuildKind(PvfArchive archive, string lstPath, string kind,
            Dictionary<int, string> names, List<ItemEntry> searchList)
        {
            if (lstPath == null)
                return;

            var lstText = archive.GetFileContent(lstPath);
            if (string.IsNullOrEmpty(lstText))
                return;

            var rootFolder = lstPath.Contains("/") ? lstPath.Substring(0, lstPath.LastIndexOf('/')) : string.Empty;
            var entries = new List<KeyValuePair<int, string>>();
            foreach (Match match in LstPattern.Matches(lstText))
            {
                int id;
                if (int.TryParse(match.Groups[1].Value, out id))
                    entries.Add(new KeyValuePair<int, string>(id, match.Groups[2].Value));
            }

            var results = new ItemEntry[entries.Count];
            Parallel.For(0, entries.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                var relative = entries[i].Value.Replace('\\', '/');
                var fullPath = string.IsNullOrEmpty(rootFolder) ? relative : rootFolder + "/" + relative;
                try
                {
                    var text = archive.GetFileContent(fullPath);
                    if (string.IsNullOrEmpty(text))
                        return;

                    // 全字段解析取 名称/品质/等级/类型标签(发放界面按类型分区用)
                    if (kind == "equipment")
                    {
                        var model = EquipmentFile.Parse(text);
                        if (string.IsNullOrEmpty(model.Name))
                            return;
                        var expiration = ResolveEquipmentExpiration(model);
                        results[i] = new ItemEntry
                        {
                            Id = entries[i].Key,
                            Name = model.Name,
                            Kind = kind,
                            TypeTag = FirstTag(model.EquipmentType),
                            Special = EquipSpecial(text),
                            Rarity = model.Rarity,
                            MinLevel = model.MinimumLevel,
                            Grade = model.Grade,
                            UsableJob = model.UsableJob,
                            AbsoluteExpirationUnixTime = expiration.AbsoluteExpirationUnixTime,
                            UsablePeriodDays = expiration.UsablePeriodDays,
                            DailyDeleteItem = expiration.DailyDeleteItem,
                            HasInvalidExpirationDefinition = expiration.HasInvalidDefinition,
                            RequiresManualGrantType = ItemMetadataResolver.RequiresManualGrantType(new ItemMetadata
                            {
                                ItemKind = "equipment",
                                EquipmentType = model.EquipmentType,
                                ItemCategory = model.ItemCategory,
                            }),
                        };
                    }
                    else
                    {
                        var model = StackableItemFile.Parse(text);
                        if (string.IsNullOrEmpty(model.Name))
                            return;
                        var expiration = ResolveStackableExpiration(model);
                        results[i] = new ItemEntry
                        {
                            Id = entries[i].Key,
                            Name = model.Name,
                            Kind = kind,
                            TypeTag = FirstTag(model.StackableType),
                            Segment = StackSegment(model.StackableType),
                            Rarity = model.Rarity,
                            MinLevel = model.MinimumLevel,
                            Grade = model.Grade,
                            UsableJob = model.UsableJob,
                            AbsoluteExpirationUnixTime = expiration.AbsoluteExpirationUnixTime,
                            UsablePeriodDays = expiration.UsablePeriodDays,
                            DailyDeleteItem = expiration.DailyDeleteItem,
                            HasInvalidExpirationDefinition = expiration.HasInvalidDefinition,
                            RequiresManualGrantType = ItemMetadataResolver.RequiresManualGrantType(new ItemMetadata
                            {
                                ItemKind = "stackable",
                                StackableType = model.StackableType,
                            }),
                        };
                    }
                }
                catch
                {
                    Interlocked.Increment(ref _parseFailures);
                }
            });

            foreach (var entry in results)
            {
                if (entry == null)
                    continue;
                if (!names.ContainsKey(entry.Id))
                    names[entry.Id] = entry.Name;
                if (searchList != null)
                    searchList.Add(entry);
            }
        }
    }
}
