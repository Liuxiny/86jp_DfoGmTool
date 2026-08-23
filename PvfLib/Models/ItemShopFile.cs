using System;
using System.Collections.Generic;
using System.Linq;

namespace GmPvfLib
{
    public sealed class ItemShopListEntry
    {
        public int ShopId { get; set; }
        public string FilePath { get; set; }

        public string ArchivePath
        {
            get
            {
                if (string.IsNullOrEmpty(FilePath))
                    return string.Empty;
                return FilePath.StartsWith("itemshop/", StringComparison.OrdinalIgnoreCase)
                    ? FilePath
                    : "itemshop/" + FilePath;
            }
        }

        public override string ToString() => $"{ShopId} `{FilePath}`";
    }

    public sealed class ItemShopListFile
    {
        private Dictionary<int, ItemShopListEntry> _shopIdIndex;

        public List<ItemShopListEntry> Entries { get; } = new List<ItemShopListEntry>();

        public static ItemShopListFile Parse(string content)
        {
            var result = new ItemShopListFile();
            var list = LstFile.Parse(content);
            foreach (var entry in list.Entries)
            {
                result.Entries.Add(new ItemShopListEntry
                {
                    ShopId = entry.Id,
                    FilePath = entry.FilePath
                });
            }

            return result;
        }

        public ItemShopListEntry GetByShopId(int shopId)
        {
            ItemShopListEntry entry;
            return EnsureShopIdIndex().TryGetValue(shopId, out entry) ? entry : null;
        }

        public ItemShopListEntry GetByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var normalized = NormalizePath(path);
            for (int i = 0; i < Entries.Count; i++)
            {
                if (string.Equals(NormalizePath(Entries[i].FilePath), normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NormalizePath(Entries[i].ArchivePath), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return Entries[i];
                }
            }

            return null;
        }

        private Dictionary<int, ItemShopListEntry> EnsureShopIdIndex()
        {
            if (_shopIdIndex == null)
            {
                _shopIdIndex = new Dictionary<int, ItemShopListEntry>(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    var entry = Entries[i];
                    if (!_shopIdIndex.ContainsKey(entry.ShopId))
                        _shopIdIndex[entry.ShopId] = entry;
                }
            }

            return _shopIdIndex;
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/').TrimStart('.', '/');
        }
    }

    public sealed class ItemShopTab
    {
        public string Name { get; set; }
        public List<int> ItemIds { get; } = new List<int>();
    }

    public sealed class ItemShopCatalogEntry
    {
        public int ShopId { get; set; }
        public string FilePath { get; set; }
        public string ArchivePath { get; set; }
        public ItemShopFile Shop { get; set; }
        public int NpcId => Shop != null ? Shop.NpcId : -1;
    }

    public sealed class ItemShopCatalog
    {
        private Dictionary<int, ItemShopCatalogEntry> _shopIdIndex;
        private Dictionary<int, List<ItemShopCatalogEntry>> _npcIdIndex;

        public ItemShopListFile ListFile { get; private set; }
        public List<ItemShopCatalogEntry> Entries { get; } = new List<ItemShopCatalogEntry>();

        public static ItemShopCatalog Load(PvfArchive archive, string listPath = "itemshop/itemshop.lst")
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));

            var listFile = ItemShopListFile.Parse(archive.GetFileContent(listPath));
            var catalog = new ItemShopCatalog
            {
                ListFile = listFile
            };

            foreach (var entry in listFile.Entries)
            {
                var archivePath = ResolveShopArchivePath(archive, entry);
                catalog.Entries.Add(new ItemShopCatalogEntry
                {
                    ShopId = entry.ShopId,
                    FilePath = entry.FilePath,
                    ArchivePath = archivePath,
                    Shop = ItemShopFile.Parse(archive.GetFileContent(archivePath))
                });
            }

            return catalog;
        }

        public static ItemShopCatalog Parse(string itemShopListContent, Func<string, string> readText)
        {
            if (readText == null)
                throw new ArgumentNullException(nameof(readText));

            var listFile = ItemShopListFile.Parse(itemShopListContent);
            var catalog = new ItemShopCatalog
            {
                ListFile = listFile
            };

            foreach (var entry in listFile.Entries)
            {
                var content = readText(entry.ArchivePath);
                catalog.Entries.Add(new ItemShopCatalogEntry
                {
                    ShopId = entry.ShopId,
                    FilePath = entry.FilePath,
                    ArchivePath = entry.ArchivePath,
                    Shop = ItemShopFile.Parse(content)
                });
            }

            return catalog;
        }

        public ItemShopCatalogEntry GetByShopId(int shopId)
        {
            ItemShopCatalogEntry entry;
            return EnsureShopIdIndex().TryGetValue(shopId, out entry) ? entry : null;
        }

        public List<int> GetShopIdsByNpcId(int npcId)
        {
            List<ItemShopCatalogEntry> entries;
            if (!EnsureNpcIdIndex().TryGetValue(npcId, out entries))
                return new List<int>();

            return entries.Select(entry => entry.ShopId).ToList();
        }

        public int GetShopIdByNpcId(int npcId, int defaultValue = -1)
        {
            var shopIds = GetShopIdsByNpcId(npcId);
            return shopIds.Count > 0 ? shopIds[0] : defaultValue;
        }

        public int GetNpcIdByShopId(int shopId, int defaultValue = -1)
        {
            var entry = GetByShopId(shopId);
            return entry != null ? entry.NpcId : defaultValue;
        }

        public List<List<int>> GetItemListsByShopId(int shopId)
        {
            var entry = GetByShopId(shopId);
            return entry != null && entry.Shop != null
                ? entry.Shop.GetItemLists()
                : new List<List<int>>();
        }

        public List<int> GetItemIdsByShopId(int shopId, bool distinct = false)
        {
            var entry = GetByShopId(shopId);
            return entry != null && entry.Shop != null
                ? entry.Shop.GetItemIds(distinct)
                : new List<int>();
        }

        private Dictionary<int, ItemShopCatalogEntry> EnsureShopIdIndex()
        {
            if (_shopIdIndex == null)
            {
                _shopIdIndex = new Dictionary<int, ItemShopCatalogEntry>(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    var entry = Entries[i];
                    if (!_shopIdIndex.ContainsKey(entry.ShopId))
                        _shopIdIndex[entry.ShopId] = entry;
                }
            }

            return _shopIdIndex;
        }

        private Dictionary<int, List<ItemShopCatalogEntry>> EnsureNpcIdIndex()
        {
            if (_npcIdIndex == null)
            {
                _npcIdIndex = new Dictionary<int, List<ItemShopCatalogEntry>>();
                for (int i = 0; i < Entries.Count; i++)
                {
                    var entry = Entries[i];
                    if (entry.NpcId < 0)
                        continue;

                    List<ItemShopCatalogEntry> entries;
                    if (!_npcIdIndex.TryGetValue(entry.NpcId, out entries))
                    {
                        entries = new List<ItemShopCatalogEntry>();
                        _npcIdIndex[entry.NpcId] = entries;
                    }

                    entries.Add(entry);
                }
            }

            return _npcIdIndex;
        }

        private static string ResolveShopArchivePath(PvfArchive archive, ItemShopListEntry entry)
        {
            var path = entry.ArchivePath;
            if (archive.FindFileIndex(path) >= 0)
                return path;

            var name = GetFileName(path);
            foreach (var candidate in new[]
            {
                "itemshop/(r)" + name,
                "itemshop/(f)" + name
            })
            {
                if (archive.FindFileIndex(candidate) >= 0)
                    return candidate;
            }

            var matches = archive.Files
                .Select(BuildArchivePath)
                .Where(candidate =>
                    candidate.StartsWith("itemshop/", StringComparison.OrdinalIgnoreCase) &&
                    GetFileName(candidate).EndsWith(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.IndexOf("/(r)", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 :
                    candidate.IndexOf("/(f)", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 2)
                .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return matches.Count > 0 ? matches[0] : path;
        }

        private static string BuildArchivePath(PvfFileData file)
        {
            if (string.IsNullOrEmpty(file.Path))
                return file.Name ?? string.Empty;
            if (string.IsNullOrEmpty(file.Name))
                return NormalizePath(file.Path);

            return NormalizePath(file.Path.TrimEnd('/', '\\') + "/" + file.Name);
        }

        private static string GetFileName(string path)
        {
            path = NormalizePath(path);
            var slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/').TrimStart('.', '/');
        }
    }

    public sealed class ItemShopFile : PvfModelBase
    {
        public int NpcId { get; private set; } = -1;
        public List<ItemShopTab> Tabs { get; } = new List<ItemShopTab>();

        public static ItemShopFile Parse(string content)
        {
            content = content ?? string.Empty;
            var root = new ScriptParser().Parse(content);
            var shop = new ItemShopFile
            {
                Content = content,
                Root = root,
                NpcId = ParseInt(root.GetChild("NPC")?.GetFirstDataContent(content))
            };

            var sellInfo = root.GetChild("sell info");
            if (sellInfo == null)
                return shop;

            foreach (var tabNode in sellInfo.GetChildren("tab"))
            {
                shop.Tabs.Add(ParseTab(tabNode, content));
            }

            return shop;
        }

        public ItemShopTab GetTab(string name)
        {
            return Tabs.FirstOrDefault(tab =>
                string.Equals(tab.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public List<List<int>> GetItemLists()
        {
            return Tabs
                .Select(tab => new List<int>(tab.ItemIds))
                .ToList();
        }

        public List<int> GetItemIds(bool distinct = false)
        {
            var itemIds = Tabs.SelectMany(tab => tab.ItemIds);
            return distinct
                ? itemIds.Distinct().ToList()
                : itemIds.ToList();
        }

        private static ItemShopTab ParseTab(ScriptNode tabNode, string content)
        {
            var tab = new ItemShopTab
            {
                Name = StripBacktick(tabNode.GetFirstDataContent(content)).Trim()
            };

            var itemList = tabNode.GetChild("item list");
            if (itemList != null)
            {
                foreach (var itemId in ParseItemIds(ReadData(itemList, content)))
                    tab.ItemIds.Add(itemId);
            }

            return tab;
        }

        private static string ReadData(ScriptNode node, string content)
        {
            if (node == null || node.DataItems.Count == 0)
                return string.Empty;

            return string.Join(" ", node.DataItems
                .Select(item => item.GetContent(content).Trim())
                .Where(line => line.Length > 0));
        }

        private static IEnumerable<int> ParseItemIds(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            var parts = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                int itemId;
                if (int.TryParse(parts[i], out itemId))
                    yield return itemId;
            }
        }
    }
}
