using System.Collections.Generic;
using System.Linq;

namespace GmPvfLib
{
    public sealed class CollectBoxItemSlot
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    public sealed class CollectBoxEntry
    {
        public int Index { get; set; } = -1;
        public string BaseExpirationDate { get; set; }
        public string MaxExpirationDate { get; set; }
        public List<int> ExtendItemIds { get; } = new List<int>();
        public int SlotCount { get; set; }
        public int SlotsPerRow { get; set; }
        public List<CollectBoxItemSlot> Slots { get; } = new List<CollectBoxItemSlot>();
    }

    public class CollectBoxFile : PvfModelBase
    {
        public List<CollectBoxEntry> Entries { get; } = new List<CollectBoxEntry>();

        public static CollectBoxFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new CollectBoxFile { Root = new ScriptNode { Tag = "ROOT" }, Content = content ?? "" };

            var root = new ScriptParser().Parse(content);
            var file = new CollectBoxFile { Root = root, Content = content };

            foreach (var node in root.GetChildren("Collect Box Info"))
                file.Entries.Add(ParseEntry(node, content));

            return file;
        }

        public CollectBoxEntry GetByIndex(int index)
        {
            return Entries.FirstOrDefault(e => e.Index == index);
        }

        private static IEnumerable<string> GetTokens(ScriptNode node, string content)
        {
            foreach (var item in node.DataItems)
            {
                var line = item.GetContent(content);
                foreach (var tok in line.Split(new[] { ' ', '\t', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries))
                    yield return tok;
            }
        }

        private static CollectBoxEntry ParseEntry(ScriptNode entryNode, string content)
        {
            var entry = new CollectBoxEntry();

            var indexNode = entryNode.GetChild("Index");
            if (indexNode != null)
                entry.Index = ParseInt(indexNode.GetFirstDataContent(content));

            var baseDateNode = entryNode.GetChild("base expiration date");
            if (baseDateNode != null)
                entry.BaseExpirationDate = StripBacktick(baseDateNode.GetFirstDataContent(content));

            var maxDateNode = entryNode.GetChild("max expiration date");
            if (maxDateNode != null)
                entry.MaxExpirationDate = StripBacktick(maxDateNode.GetFirstDataContent(content));

            var extendNode = entryNode.GetChild("Extend Item");
            if (extendNode != null)
            {
                var tokens = GetTokens(extendNode, content).ToList();
                for (int i = 0; i + 1 < tokens.Count; i += 2)
                {
                    if (int.TryParse(tokens[i], out var itemId))
                        entry.ExtendItemIds.Add(itemId);
                }
            }

            var posNode = entryNode.GetChild("Item Position Info");
            if (posNode != null)
            {
                var tokens = GetTokens(posNode, content).ToList();
                if (tokens.Count >= 2 && int.TryParse(tokens[0], out var rowCount) && int.TryParse(tokens[1], out var perRow))
                {
                    // 前两个数字是"行数"和"每行几个槛位"，总槛位数 = rowCount * perRow，
                    // 不是 rowCount 本身（实测 PVF：Index2 "4 3" 后面跟了 4*3=12 对数据，
                    // 旧代码按 slotCount=rowCount=4 只读了前4对，漏掉了8对槛位）。
                    var totalSlots = rowCount * perRow;
                    entry.SlotCount = totalSlots;
                    entry.SlotsPerRow = perRow;
                    int idx = 2;
                    for (int i = 0; i < totalSlots && idx + 1 < tokens.Count; i++, idx += 2)
                    {
                        if (int.TryParse(tokens[idx], out var itemId) && int.TryParse(tokens[idx + 1], out var count))
                            entry.Slots.Add(new CollectBoxItemSlot { ItemId = itemId, Count = count });
                    }
                }
            }

            return entry;
        }
    }
}
