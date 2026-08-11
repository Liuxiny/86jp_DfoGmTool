using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GmPvfLib;

namespace DfoGmTool.Services
{
    // 从 Script.pvf 建物品/任务名字索引, 用于搜索和发放前校验。
    // 索引建完就释放归档本体, 常驻内存只有名字字典。
    // partial 按域拆分: Items(物品索引/搜索/分类) Jobs(职业名/转职觉醒)
    // Quests(任务元数据) World(区域/副本/地图映射)
    public sealed partial class PvfIndexService
    {
        private static readonly Regex NamePattern = new Regex(@"\[name\]\s*`([^`]*)`", RegexOptions.Compiled);
        private static readonly Regex LstPattern = new Regex(@"(\d+)\s+`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex BacktickPattern = new Regex("`([^`]+)`", RegexOptions.Compiled);

        private readonly string _pvfPath;
        private volatile Dictionary<int, string> _itemNames;
        private volatile Dictionary<int, string> _itemKinds;
        private volatile Dictionary<int, int> _itemRarities;
        private volatile Dictionary<int, ItemExpirationDefinition> _itemExpirations;
        private volatile Dictionary<int, QuestMeta> _questMeta;
        private volatile Dictionary<string, string> _regionNames;
        private volatile Dictionary<int, string> _dungeonRegion;
        private volatile Dictionary<int, int> _mapDungeon;
        private volatile List<int> _dungeonPermissionIds;
        private volatile HashSet<string> _openHubKeys;
        private volatile Dictionary<int, JobNameInfo> _jobNames;
        private volatile List<ItemEntry> _searchList;
        // 合法物品 ID 来自两个 LST 的原始正整数集合，而不是已成功解析且
        // 有名称的条目。Build 完成后该 HashSet 不再修改，仅在下一次索引
        // 生成时以新引用整体替换，读侧 ContainsItemId 为 O(1)。
        private volatile HashSet<int> _validItemIds;
        private volatile string _buildError;
        // 构建期间解析失败(被跳过)的条目数。数据源可热切换，计数必须归属当前索引实例。
        private int _parseFailures;

        public PvfIndexService(string pvfPath)
        {
            _pvfPath = pvfPath;
        }

        public bool IsReady => _itemNames != null;
        public string BuildError => _buildError;

        public bool ContainsItemId(int itemId)
        {
            var valid = _validItemIds;
            return itemId > 0 && valid != null && valid.Contains(itemId);
        }

        internal HashSet<int> CopyValidItemIds()
        {
            var valid = _validItemIds;
            return valid == null
                ? new HashSet<int>()
                : new HashSet<int>(valid);
        }

        public void WarmInBackground()
        {
            Task.Run(() =>
            {
                try
                {
                    Build();
                }
                catch (Exception ex)
                {
                    _buildError = ex.Message;
                    Console.WriteLine("[PvfIndex] 索引构建失败: " + ex);
                }
            });
        }

        private void Build()
        {
            Interlocked.Exchange(ref _parseFailures, 0);
            var itemNames = new Dictionary<int, string>();
            var searchList = new List<ItemEntry>();
            var validItemIds = new HashSet<int>();

            using (var archive = PvfArchive.Open(_pvfPath))
            {
                // 职业名表很小, 先建好尽快可用(角色列表页面一打开就要)
                _jobNames = BuildJobNames(archive);
                _regionNames = BuildRegionNames(archive);
                _dungeonRegion = BuildDungeonRegionMap(archive);
                _mapDungeon = BuildMapDungeonMap(archive);
                _dungeonPermissionIds = BuildDungeonPermissionIds(archive);
                _openHubKeys = BuildOpenHubKeys(archive);
                BuildKind(archive, "equipment/equipment.lst", "equipment", itemNames, searchList, validItemIds);
                BuildKind(archive, "stackable/stackable.lst", "stackable", itemNames, searchList, validItemIds);
                _questMeta = BuildQuestMeta(archive);
            }

            searchList.Sort((a, b) => a.Id.CompareTo(b.Id));
            var itemKinds = new Dictionary<int, string>(searchList.Count);
            var itemRarities = new Dictionary<int, int>(searchList.Count);
            var itemExpirations = new Dictionary<int, ItemExpirationDefinition>(searchList.Count);
            foreach (var entry in searchList)
            {
                if (!itemKinds.ContainsKey(entry.Id))
                {
                    itemKinds[entry.Id] = entry.Kind;
                    itemRarities[entry.Id] = entry.Rarity;
                    itemExpirations[entry.Id] = new ItemExpirationDefinition(
                        true,
                        entry.AbsoluteExpirationUnixTime,
                        entry.UsablePeriodDays,
                        entry.DailyDeleteItem,
                        entry.HasInvalidExpirationDefinition);
                }
            }

            _searchList = searchList;
            _itemKinds = itemKinds;
            _itemRarities = itemRarities;
            _itemExpirations = itemExpirations;
            _validItemIds = validItemIds;
            _itemNames = itemNames;
            var failures = _parseFailures;
            Console.WriteLine($"[PvfIndex] 索引就绪: 物品 {itemNames.Count}, 任务 {(_questMeta != null ? _questMeta.Count : 0)}"
                + (failures > 0 ? $", 解析失败被跳过 {failures} 条" : ""));
        }

        private static string FindLstPath(PvfArchive archive, string fileName)
        {
            foreach (var file in archive.Files)
            {
                var name = file.Name ?? string.Empty;
                if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrEmpty(file.Path) ? name : file.Path.Replace('\\', '/') + "/" + name;
            }
            return null;
        }
    }
}
