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
    public sealed partial class PvfIndexService
    {
        // 目录是否指向本版本开放的枢纽(城镇 或 worldmap.lst 里的开放世界地图);
        // elvengard/northmyre 这类残留目录不合格
        public bool IsOpenHubRegion(string regionKey)
        {
            if (regionKey == null)
                return false;
            // 用途性目录(活动/决斗场/深渊派对)是有效分组
            if (RegionLabels.ContainsKey(regionKey))
                return true;
            var hubs = _openHubKeys;
            if (hubs == null)
                return false;
            string alias;
            if (RegionAliases.TryGetValue(regionKey, out alias))
                regionKey = alias;
            return hubs.Contains(regionKey);
        }

        // 地图 → 所属副本 (各 .map 自带 [dungeon index])
        public int ResolveMapDungeon(int mapId)
        {
            var map = _mapDungeon;
            if (map == null || mapId <= 0)
                return -1;
            int dungeonId;
            return map.TryGetValue(mapId, out dungeonId) ? dungeonId : -1;
        }

        // 副本 → 开放区域: 只认 worldmap.lst 主控清单里的 wdm, 未开放区域的副本查不到
        public string ResolveDungeonRegion(int dungeonId)
        {
            var map = _dungeonRegion;
            if (map == null || dungeonId <= 0)
                return null;
            string region;
            return map.TryGetValue(dungeonId, out region) ? region : null;
        }

        // 区域中文名: 目录名匹配 town.lst 的 .twn 文件名时用其 [name], 否则原样返回目录名
        public string ResolveRegionName(string regionKey)
        {
            var names = _regionNames;
            if (regionKey == null || names == null)
                return regionKey;
            string name;
            return names.TryGetValue(regionKey, out name) ? name : regionKey;
        }

        // 开放枢纽目录集: town.lst 城镇基名 + worldmap.lst 开放世界地图基名
        private static HashSet<string> BuildOpenHubKeys(PvfArchive archive)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lst in new[] { "town/town.lst", "worldmap/worldmap.lst" })
            {
                string text;
                try
                {
                    text = archive.GetFileContent(lst);
                }
                catch
                {
                    continue;
                }
                if (string.IsNullOrEmpty(text))
                    continue;
                foreach (Match match in LstPattern.Matches(text))
                {
                    var file = match.Groups[2].Value.Replace('\\', '/');
                    var dot = file.LastIndexOf('.');
                    if (dot > 0)
                        result.Add(file.Substring(0, dot).ToLowerInvariant());
                }
            }
            return result;
        }

        // map.lst → 各 .map 的 [dungeon index]: 地图ID → 所属副本ID
        private Dictionary<int, int> BuildMapDungeonMap(PvfArchive archive)
        {
            var result = new Dictionary<int, int>();
            var lstPath = FindLstPath(archive, "map.lst");
            if (lstPath == null)
                return result;

            var lstText = archive.GetFileContent(lstPath);
            if (string.IsNullOrEmpty(lstText))
                return result;

            var rootFolder = lstPath.Contains("/") ? lstPath.Substring(0, lstPath.LastIndexOf('/')) : string.Empty;
            var entries = new List<KeyValuePair<int, string>>();
            foreach (Match match in LstPattern.Matches(lstText))
            {
                int id;
                if (int.TryParse(match.Groups[1].Value, out id))
                    entries.Add(new KeyValuePair<int, string>(id, match.Groups[2].Value));
            }

            var pairs = new KeyValuePair<int, int>?[entries.Count];
            Parallel.For(0, entries.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                var relative = entries[i].Value.Replace('\\', '/');
                var fullPath = string.IsNullOrEmpty(rootFolder) ? relative : rootFolder + "/" + relative;
                try
                {
                    var model = MapFile.Parse(archive.GetFileContent(fullPath));
                    if (model.DungeonId >= 0)
                        pairs[i] = new KeyValuePair<int, int>(entries[i].Key, model.DungeonId);
                }
                catch
                {
                    Interlocked.Increment(ref _parseFailures);
                }
            });

            foreach (var pair in pairs)
            {
                if (pair.HasValue && !result.ContainsKey(pair.Value.Key))
                    result[pair.Value.Key] = pair.Value.Value;
            }
            return result;
        }

        // 任务区域目录名与数据文件基名的拼写差异(逐个核实过的对应关系):
        // behimos=Behemoth.twn/behemoth.wdm 同地, zelba=Jelva.twn(音译Z/J),
        // anton=antonnormal.wdm(安徒恩), elvengard 的任务副本(1-9)落在
        // granfloris.wdm(格兰之森)的副本清单里
        private static readonly Dictionary<string, string> RegionAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "behimos", "behemoth" },
            { "zelba", "jelva" },
            { "anton", "antonnormal" },
            { "elvengard", "granfloris" },
        };

        // 非地理分组目录, 用途性标签
        private static readonly Dictionary<string, string> RegionLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "event", "活动" },
            { "pvp", "决斗场" },
            { "hell", "深渊派对" },
        };

        // worldmap.lst 是本版本开放世界地图的主控清单(granfloris/northmyre 等残留
        // 区域不在其中)。解析各开放 wdm 的 [dungeon] 清单(顶层数据为 副本ID 配对值
        // 交替, 截到首个 [in progress] 子块为止), 建 副本ID → 区域 索引。
        private static Dictionary<int, string> BuildDungeonRegionMap(PvfArchive archive)
        {
            var result = new Dictionary<int, string>();
            string wmLst;
            try
            {
                wmLst = archive.GetFileContent("worldmap/worldmap.lst");
            }
            catch
            {
                return result;
            }
            if (string.IsNullOrEmpty(wmLst))
                return result;

            foreach (Match match in LstPattern.Matches(wmLst))
            {
                var file = match.Groups[2].Value.Replace('\\', '/');
                if (!file.EndsWith(".wdm", StringComparison.OrdinalIgnoreCase))
                    continue;
                var regionKey = file.Substring(0, file.Length - 4).ToLowerInvariant();

                string text;
                try
                {
                    text = archive.GetFileContent("worldmap/" + file);
                }
                catch
                {
                    continue;
                }
                if (string.IsNullOrEmpty(text))
                    continue;

                var start = text.IndexOf("[dungeon]", StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    continue;
                start += "[dungeon]".Length;
                var end = text.IndexOf("[/dungeon]", start, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                    end = text.Length;
                var inProgress = text.IndexOf("[in progress]", start, StringComparison.OrdinalIgnoreCase);
                if (inProgress >= 0 && inProgress < end)
                    end = inProgress;

                var tokens = text.Substring(start, end - start)
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i + 1 < tokens.Length; i += 2)
                {
                    int dungeonId;
                    if (int.TryParse(tokens[i], out dungeonId) && dungeonId > 0 && !result.ContainsKey(dungeonId))
                        result[dungeonId] = regionKey;
                }
            }
            return result;
        }

        // 区域中文名双源: town.lst→.twn [name] + worldmap/*.wdm [name], 按文件基名(小写)索引
        private static Dictionary<string, string> BuildRegionNames(PvfArchive archive)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string lstText = null;
            try
            {
                lstText = archive.GetFileContent("town/town.lst");
            }
            catch { }

            if (!string.IsNullOrEmpty(lstText))
            {
                foreach (Match match in LstPattern.Matches(lstText))
                {
                    var file = match.Groups[2].Value.Replace('\\', '/');
                    var baseName = file.EndsWith(".twn", StringComparison.OrdinalIgnoreCase)
                        ? file.Substring(0, file.Length - 4)
                        : file;
                    var key = baseName.ToLowerInvariant();
                    if (result.ContainsKey(key))
                        continue;
                    try
                    {
                        var town = TownFile.Parse(archive.GetFileContent("town/" + file));
                        if (!string.IsNullOrWhiteSpace(town.Name))
                            result[key] = town.Name;
                    }
                    catch { }
                }
            }

            foreach (var file in archive.Files)
            {
                var path = ((file.Path ?? "") + "/" + (file.Name ?? "")).Replace('\\', '/').TrimStart('/');
                if (!path.StartsWith("worldmap/", StringComparison.OrdinalIgnoreCase)
                    || !path.EndsWith(".wdm", StringComparison.OrdinalIgnoreCase))
                    continue;

                var baseName = file.Name.Substring(0, file.Name.Length - 4).ToLowerInvariant();
                if (result.ContainsKey(baseName))
                    continue;
                try
                {
                    var text = archive.GetFileContent(file);
                    var match = NamePattern.Match(text);
                    if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                        result[baseName] = match.Groups[1].Value.Trim();
                }
                catch { }
            }

            foreach (var pair in RegionLabels)
            {
                if (!result.ContainsKey(pair.Key))
                    result[pair.Key] = pair.Value;
            }

            foreach (var alias in RegionAliases)
            {
                string name;
                if (!result.ContainsKey(alias.Key) && result.TryGetValue(alias.Value, out name))
                    result[alias.Key] = name;
            }

            return result;
        }
    }
}
