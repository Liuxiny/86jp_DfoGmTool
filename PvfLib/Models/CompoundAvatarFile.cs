using System.Collections.Generic;
using System.Linq;

namespace GmPvfLib
{
    public sealed class CompoundAvatarPool
    {
        public List<(int ItemId, int Weight)> RarePool { get; } = new List<(int ItemId, int Weight)>();
        public List<(int ItemId, int Weight)> UpperPool { get; } = new List<(int ItemId, int Weight)>();
    }

    public class CompoundAvatarFile : PvfModelBase
    {
        private static readonly string[] PartNames =
        {
            "hat", "hair", "face", "neck", "coat", "pants", "belt", "shoes"
        };

        public int Grade { get; set; } = -1;
        public int UpperGrade { get; set; } = -1;

        public Dictionary<string, int> RareRate { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> UpperRareRate { get; } = new Dictionary<string, int>();
        public Dictionary<string, CompoundAvatarPool> Parts { get; } = new Dictionary<string, CompoundAvatarPool>();

        public int MaterialItemId { get; set; } = -1;
        public int MaterialUpperItemId { get; set; } = -1;
        public int MaterialMasterItemId { get; set; } = -1;

        public static CompoundAvatarFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new CompoundAvatarFile { Root = new ScriptNode { Tag = "ROOT" }, Content = content ?? "" };

            var root = new ScriptParser().Parse(content);
            var file = new CompoundAvatarFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                var tag = node.Tag.ToLowerInvariant();
                switch (tag)
                {
                    case "grade":
                        file.Grade = ParseInt(node.GetFirstDataContent(content));
                        break;
                    case "upper grade":
                        file.UpperGrade = ParseInt(node.GetFirstDataContent(content));
                        break;
                    case "rare rate":
                        ParseRateBlock(node, content, file.RareRate);
                        break;
                    case "upper rare rate":
                        ParseRateBlock(node, content, file.UpperRareRate);
                        break;
                    case "material":
                        file.MaterialItemId = ParseMaterialItemId(node, content);
                        break;
                    case "material_upper":
                        file.MaterialUpperItemId = ParseMaterialItemId(node, content);
                        break;
                    case "material_master":
                        file.MaterialMasterItemId = ParseMaterialItemId(node, content);
                        break;
                    default:
                        if (tag.EndsWith(" avatar"))
                        {
                            var part = tag.Substring(0, tag.Length - " avatar".Length);
                            if (PartNames.Contains(part))
                                file.Parts[part] = ParseAvatarPool(node, content);
                        }
                        break;
                }
            }

            return file;
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

        private static void ParseRateBlock(ScriptNode node, string content, Dictionary<string, int> target)
        {
            var tokens = GetTokens(node, content).ToList();
            for (int i = 0; i + 1 < tokens.Count; i += 2)
            {
                var part = StripBacktick(tokens[i]);
                if (int.TryParse(tokens[i + 1], out var rate))
                    target[part] = rate;
            }
        }

        private static int ParseMaterialItemId(ScriptNode node, string content)
        {
            var tokens = GetTokens(node, content).ToList();
            return tokens.Count >= 2 && int.TryParse(tokens[1], out var id) ? id : -1;
        }

        private static CompoundAvatarPool ParseAvatarPool(ScriptNode node, string content)
        {
            var pool = new CompoundAvatarPool();
            var tokens = GetTokens(node, content).ToList();
            if (tokens.Count == 0 || !int.TryParse(tokens[0], out var rareCount))
                return pool;

            int idx = 1;
            for (int i = 0; i < rareCount && idx + 1 < tokens.Count + 1; i++, idx += 2)
            {
                if (int.TryParse(tokens[idx], out var itemId) && int.TryParse(tokens[idx + 1], out var weight))
                    pool.RarePool.Add((itemId, weight));
            }

            for (; idx + 1 < tokens.Count; idx += 2)
            {
                if (int.TryParse(tokens[idx], out var itemId) && int.TryParse(tokens[idx + 1], out var weight))
                    pool.UpperPool.Add((itemId, weight));
            }

            return pool;
        }
    }
}
