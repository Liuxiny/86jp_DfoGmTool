using System;
using System.Collections.Generic;
using System.Globalization;

namespace GmPvfLib
{
    public sealed class DisjointFile : PvfModelBase
    {
        public Dictionary<string, List<int>> CubeIndexes { get; } = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        public double CubeCreationBase { get; set; } = 150;

        public List<double> CubeCreationMultipliers { get; } = new List<double>();

        public List<List<int>> AdditionalResults { get; } = new List<List<int>>();

        public List<DisjointAdditionalResultConst> AdditionalResultConsts { get; } = new List<DisjointAdditionalResultConst>();

        public List<DisjointExpandResult> ExpandResults { get; } = new List<DisjointExpandResult>();

        public static DisjointFile Parse(string content)
        {
            var root = new ScriptParser().Parse(content ?? string.Empty);
            var file = new DisjointFile { Root = root, Content = content ?? string.Empty };

            foreach (var node in root.Children)
            {
                switch ((node.Tag ?? string.Empty).ToLowerInvariant())
                {
                    case "cube index":
                        file.ParseCubeIndex(node);
                        break;
                    case "cube creation const":
                        file.ParseCubeCreationConst(node);
                        break;
                    case "additional result":
                        file.ParseAdditionalResults(node);
                        break;
                    case "additional result const":
                        file.ParseAdditionalResultConsts(node);
                        break;
                    case "additional result expand":
                        file.ParseExpandResults(node);
                        break;
                    case "additional result expand const":
                        file.ParseExpandConsts(node);
                        break;
                }
            }

            return file;
        }

        public int GetNoElementCubeItemId()
        {
            if (CubeIndexes.TryGetValue("[no element]", out var ids) && ids.Count > 0)
                return ids[0];

            return 3037;
        }

        public double GetCubeCreationMultiplier(int rarity)
        {
            if (CubeCreationMultipliers.Count == 0)
                return 1.0;

            var index = MapRarityToCubeCreationIndex(rarity);
            if (index < 0 || index >= CubeCreationMultipliers.Count)
                index = 0;

            return CubeCreationMultipliers[index];
        }

        public IReadOnlyList<int> GetAdditionalItems(int rarity)
        {
            var index = MapRarityToAdditionalIndex(rarity);
            if (index < 0 || index >= AdditionalResults.Count)
                return Array.Empty<int>();

            return AdditionalResults[index];
        }

        public DisjointAdditionalResultConst GetAdditionalConst(int rarity)
        {
            var index = MapRarityToAdditionalIndex(rarity);
            if (index < 0 || index >= AdditionalResultConsts.Count)
                return null;

            return AdditionalResultConsts[index];
        }

        public DisjointExpandResult GetExpandResult(int rarity)
        {
            var index = MapRarityToExpandIndex(rarity);
            if (index < 0 || index >= ExpandResults.Count)
                return null;

            return ExpandResults[index];
        }

        private void ParseCubeIndex(ScriptNode node)
        {
            foreach (var line in GetLines(node))
            {
                var tokens = SplitTokens(line);
                if (tokens.Count < 2)
                    continue;

                var key = NormalizeToken(tokens[0]);
                var ids = new List<int>();
                for (var i = 1; i < tokens.Count; i++)
                {
                    if (int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                        ids.Add(id);
                }

                if (!string.IsNullOrWhiteSpace(key) && ids.Count > 0)
                    CubeIndexes[key] = ids;
            }
        }

        private void ParseCubeCreationConst(ScriptNode node)
        {
            var values = ParseDoubles(GetFlattenedTokens(node));
            if (values.Count == 0)
                return;

            CubeCreationBase = values[0] > 0 ? values[0] : CubeCreationBase;
            CubeCreationMultipliers.Clear();
            for (var i = 1; i < values.Count; i++)
                CubeCreationMultipliers.Add(values[i]);
        }

        private void ParseAdditionalResults(ScriptNode node)
        {
            AdditionalResults.Clear();
            var numbers = ParseInts(GetFlattenedTokens(node));
            var index = 0;
            while (index < numbers.Count)
            {
                var count = Math.Max(0, numbers[index++]);
                var items = new List<int>();
                for (var i = 0; i < count && index < numbers.Count; i++, index++)
                {
                    if (numbers[index] > 0)
                        items.Add(numbers[index]);
                }

                AdditionalResults.Add(items);
            }
        }

        private void ParseAdditionalResultConsts(ScriptNode node)
        {
            AdditionalResultConsts.Clear();
            var values = ParseDoubles(GetFlattenedTokens(node));
            if (values.Count == 0)
                return;

            var rowCount = AdditionalResults.Count > 0
                ? AdditionalResults.Count
                : GuessAdditionalConstRowCount(values.Count);
            var index = 0;
            for (var row = 0; row < rowCount && index < values.Count; row++)
            {
                var remainingRows = rowCount - row - 1;
                if (values.Count - index == 1 + remainingRows * 4)
                {
                    AdditionalResultConsts.Add(new DisjointAdditionalResultConst
                    {
                        GreatCountDivisor = values[index],
                    });
                    index++;
                    continue;
                }

                AdditionalResultConsts.Add(new DisjointAdditionalResultConst
                {
                    GreatCountDivisor = index < values.Count ? values[index] : 0,
                    NormalCountDivisor = index + 1 < values.Count ? values[index + 1] : 0,
                    GreatChancePercent = index + 2 < values.Count ? values[index + 2] : 0,
                    NormalChancePercent = index + 3 < values.Count ? values[index + 3] : 0,
                });
                index += 4;
            }
        }

        private void ParseExpandResults(ScriptNode node)
        {
            ExpandResults.Clear();
            var numbers = ParseInts(GetFlattenedTokens(node));
            for (var i = 0; i + 1 < numbers.Count; i += 2)
            {
                var count = numbers[i];
                var itemId = numbers[i + 1];
                ExpandResults.Add(new DisjointExpandResult
                {
                    Enabled = count > 0 && itemId > 0,
                    ItemTemplateId = itemId,
                });
            }
        }

        private void ParseExpandConsts(ScriptNode node)
        {
            var values = ParseDoubles(GetFlattenedTokens(node));
            var valueIndex = 0;
            for (var i = 0; i < ExpandResults.Count && valueIndex < values.Count; i++)
            {
                var remainingResults = ExpandResults.Count - i - 1;
                if (!ExpandResults[i].Enabled
                    && ExpandResults[i].ItemTemplateId <= 0
                    && valueIndex + 1 < values.Count
                    && values.Count - (valueIndex + 2) == remainingResults * 3)
                {
                    ExpandResults[i].LevelDivisor = values[valueIndex];
                    ExpandResults[i].GreatChancePercent = values[valueIndex + 1];
                    ExpandResults[i].NormalChancePercent = 0;
                    valueIndex += 2;
                    continue;
                }

                if (valueIndex + 2 >= values.Count)
                    break;

                ExpandResults[i].LevelDivisor = values[valueIndex];
                ExpandResults[i].GreatChancePercent = values[valueIndex + 1];
                ExpandResults[i].NormalChancePercent = values[valueIndex + 2];
                valueIndex += 3;
            }
        }

        private IEnumerable<string> GetLines(ScriptNode node)
        {
            if (node == null || node.DataItems == null)
                yield break;

            foreach (var item in node.DataItems)
            {
                var line = item.GetContent(Content).Trim();
                if (!string.IsNullOrWhiteSpace(line))
                    yield return line;
            }
        }

        private List<string> GetFlattenedTokens(ScriptNode node)
        {
            var result = new List<string>();
            foreach (var line in GetLines(node))
                result.AddRange(SplitTokens(line));
            return result;
        }

        private static List<string> SplitTokens(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(line))
                return result;

            var start = -1;
            var inBacktick = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '`')
                    inBacktick = !inBacktick;

                if (char.IsWhiteSpace(ch) && !inBacktick)
                {
                    if (start >= 0)
                    {
                        result.Add(line.Substring(start, i - start));
                        start = -1;
                    }
                    continue;
                }

                if (start < 0)
                    start = i;
            }

            if (start >= 0)
                result.Add(line.Substring(start));

            return result;
        }

        private static List<int> ParseInts(IEnumerable<string> tokens)
        {
            var result = new List<int>();
            foreach (var token in tokens)
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    result.Add(value);
            }

            return result;
        }

        private static List<double> ParseDoubles(IEnumerable<string> tokens)
        {
            var result = new List<double>();
            foreach (var token in tokens)
            {
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    result.Add(value);
            }

            return result;
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var s = value.Trim();
            if (s.Length >= 2 && s[0] == '`' && s[s.Length - 1] == '`')
                s = s.Substring(1, s.Length - 2);

            return s.Trim().ToLowerInvariant();
        }

        private static int MapRarityToCubeCreationIndex(int rarity)
        {
            if (rarity < 0)
                return 0;

            return rarity;
        }

        private static int GuessAdditionalConstRowCount(int valueCount)
        {
            if (valueCount <= 0)
                return 0;
            if ((valueCount - 1) % 4 == 0)
                return 1 + (valueCount - 1) / 4;

            return (valueCount + 3) / 4;
        }

        private static int MapRarityToAdditionalIndex(int rarity)
        {
            if (rarity < 0)
                return 0;

            return rarity;
        }

        private static int MapRarityToExpandIndex(int rarity)
        {
            if (rarity == 6)
                return 5;

            if (rarity >= 0 && rarity <= 4)
                return rarity;

            return -1;
        }
    }

    public sealed class DisjointAdditionalResultConst
    {
        public double GreatCountDivisor { get; set; }

        public double NormalCountDivisor { get; set; }

        public double GreatChancePercent { get; set; }

        public double NormalChancePercent { get; set; }
    }

    public sealed class DisjointExpandResult
    {
        public bool Enabled { get; set; }

        public int ItemTemplateId { get; set; }

        public double LevelDivisor { get; set; }

        public double GreatChancePercent { get; set; }

        public double NormalChancePercent { get; set; }
    }
}
