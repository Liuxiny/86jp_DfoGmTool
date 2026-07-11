using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GmPvfLib
{
    public sealed class UpgradeTableFile : PvfModelBase
    {
        private const int UpgradeRowValueCount = 17;

        public List<UpgradeTableDefinition> Tables { get; set; } = new List<UpgradeTableDefinition>();
        public int NoticeLevel { get; set; } = -1;
        public List<int> LevelValues { get; set; } = new List<int>();
        public List<double> CostWeightsByRarity { get; set; } = new List<double>();
        public List<double> StatWeightsByRarity { get; set; } = new List<double>();
        public List<double> TypeWeights { get; set; } = new List<double>();
        public List<int> Costs { get; set; } = new List<int>();
        public List<double> RepairCostRatesByUpgradeLevel { get; set; } = new List<double>();
        public Dictionary<string, int> DestroyLevelByRarity { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> MaxUpgradeLevelByRarity { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public UpgradeDisjointConfig Disjoint { get; set; } = new UpgradeDisjointConfig();
        public Dictionary<int, double> CostWeightByUpgradeLevel { get; set; } = new Dictionary<int, double>();
        public List<int> UpgradeMachineFrameBaseTime { get; set; } = new List<int>();
        public List<UpgradeMachineFrameRate> UpgradeMachineFrameRates { get; set; } = new List<UpgradeMachineFrameRate>();
        public List<int[]> AmplificationConsts { get; set; } = new List<int[]>();

        public UpgradeTableDefinition GetTable(string tableType)
        {
            if (string.IsNullOrWhiteSpace(tableType))
                return null;

            foreach (var table in Tables)
            {
                if (string.Equals(table.TableType, tableType, StringComparison.OrdinalIgnoreCase))
                    return table;
            }

            return null;
        }

        public static UpgradeTableFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new UpgradeTableFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var file = new UpgradeTableFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                switch (node.Tag.ToLowerInvariant())
                {
                    case "table":
                        file.Tables.AddRange(ParseTables(node, content));
                        break;
                    case "notice":
                        file.NoticeLevel = ParseInt(node.GetFirstDataContent(content));
                        break;
                    case "level":
                        file.LevelValues = ParseIntList(node, content);
                        break;
                    case "cost weights by rarity":
                        file.CostWeightsByRarity = ParseDoubleList(node, content);
                        break;
                    case "stat weights by rarity":
                        file.StatWeightsByRarity = ParseDoubleList(node, content);
                        break;
                    case "type":
                        file.TypeWeights = ParseDoubleList(node, content);
                        break;
                    case "cost":
                        file.Costs = ParseIntList(node, content);
                        break;
                    case "repair cost rate by upgrade level":
                        file.RepairCostRatesByUpgradeLevel = ParseDoubleList(node, content);
                        break;
                    case "destroy level by rarity":
                        file.DestroyLevelByRarity = ParseStringIntMap(node, content);
                        break;
                    case "max upgrade level by rarity":
                        file.MaxUpgradeLevelByRarity = ParseStringIntMap(node, content);
                        break;
                    case "disjoint":
                        file.Disjoint = ParseDisjoint(node, content);
                        break;
                    case "cost weight by upgrade level":
                        file.CostWeightByUpgradeLevel = ParseIntDoubleMap(node, content);
                        break;
                    case "upgrade machine frame base time":
                        file.UpgradeMachineFrameBaseTime = ParseIntList(node, content);
                        break;
                    case "upgrade machine frame rate":
                        file.UpgradeMachineFrameRates = ParseMachineFrameRates(node, content);
                        break;
                    case "amplification const":
                        file.AmplificationConsts = ParseIntRows(node, content);
                        break;
                }
            }

            return file;
        }

        private static List<UpgradeTableDefinition> ParseTables(ScriptNode node, string content)
        {
            var tables = new List<UpgradeTableDefinition>();
            if (node == null)
                return tables;

            var tableTypeNodes = node.GetChildren("table type");
            if (tableTypeNodes.Count == 0)
                tableTypeNodes.Add(node);

            foreach (var tableTypeNode in tableTypeNodes)
            {
                var tokens = ReadTokens(tableTypeNode, content);
                if (tokens.Count == 0)
                    tokens = ReadTokens(node.GetContent(content));
                if (tokens.Count == 0)
                    continue;

                var table = new UpgradeTableDefinition();
                var numberStart = 0;
                if (!IsNumberToken(tokens[0]))
                {
                    table.TableType = NormalizeName(tokens[0]);
                    numberStart = 1;
                }

                var values = new List<double>();
                for (var i = numberStart; i < tokens.Count; i++)
                {
                    if (TryParseDouble(tokens[i], out var value))
                        values.Add(value);
                }

                for (var i = 0; i + UpgradeRowValueCount <= values.Count; i += UpgradeRowValueCount)
                {
                    var rowValues = new double[UpgradeRowValueCount];
                    values.CopyTo(i, rowValues, 0, UpgradeRowValueCount);
                    table.Rows.Add(new UpgradeTableRow
                    {
                        TableType = table.TableType,
                        RowIndex = table.Rows.Count,
                        Values = rowValues,
                    });
                }

                for (var i = table.Rows.Count * UpgradeRowValueCount; i < values.Count; i++)
                    table.TrailingValues.Add(values[i]);

                tables.Add(table);
            }

            return tables;
        }

        private static UpgradeDisjointConfig ParseDisjoint(ScriptNode node, string content)
        {
            var config = new UpgradeDisjointConfig();
            if (node == null)
                return config;

            foreach (var child in node.Children)
            {
                switch (child.Tag.ToLowerInvariant())
                {
                    case "disjoint bonus item":
                        config.DisjointBonusItemId = ParseInt(child.GetFirstDataContent(content));
                        break;
                    case "equip level const":
                        config.EquipLevelConst = ParseInt(child.GetFirstDataContent(content));
                        break;
                    case "upgrade failed bonus weight by rarity":
                        config.UpgradeFailedBonusWeightByRarity = ParseStringDoubleMap(child, content);
                        break;
                    case "upgrade const for bonus item count":
                        config.UpgradeConstForBonusItemCount = ParseInt(child.GetFirstDataContent(content));
                        break;
                    case "correction grade by rarity":
                        config.CorrectionGradeByRarity = ParseStringIntMap(child, content);
                        break;
                }
            }

            return config;
        }

        private static List<UpgradeMachineFrameRate> ParseMachineFrameRates(ScriptNode node, string content)
        {
            var result = new List<UpgradeMachineFrameRate>();
            if (node == null)
                return result;

            foreach (var item in node.DataItems)
            {
                var values = ParseDoubles(item.GetContent(content));
                if (values.Count == 0)
                    continue;

                result.Add(new UpgradeMachineFrameRate
                {
                    UpgradeLevel = ToInt(values[0]),
                    Values = values.ToArray(),
                });
            }

            return result;
        }

        private static List<int[]> ParseIntRows(ScriptNode node, string content)
        {
            var result = new List<int[]>();
            if (node == null)
                return result;

            foreach (var item in node.DataItems)
            {
                var row = ParseInts(item.GetContent(content));
                if (row.Count > 0)
                    AddIntRows(result, row);
            }

            if (result.Count == 0)
            {
                var lines = node.GetContent(content)
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var row = ParseInts(line);
                    if (row.Count > 0)
                        AddIntRows(result, row);
                }
            }

            return result;
        }

        private static void AddIntRows(List<int[]> result, List<int> row)
        {
            if (row.Count > 4 && row.Count % 4 == 0)
            {
                for (var i = 0; i < row.Count; i += 4)
                    result.Add(new[] { row[i], row[i + 1], row[i + 2], row[i + 3] });
                return;
            }

            result.Add(row.ToArray());
        }

        private static Dictionary<string, int> ParseStringIntMap(ScriptNode node, string content)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in ParseStringNumberPairs(node, content))
                result[pair.Name] = ToInt(pair.Value);
            return result;
        }

        private static Dictionary<string, double> ParseStringDoubleMap(ScriptNode node, string content)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in ParseStringNumberPairs(node, content))
                result[pair.Name] = pair.Value;
            return result;
        }

        private static Dictionary<int, double> ParseIntDoubleMap(ScriptNode node, string content)
        {
            var result = new Dictionary<int, double>();
            if (node == null)
                return result;

            foreach (var item in node.DataItems)
            {
                var values = ParseDoubles(item.GetContent(content));
                for (var i = 0; i + 1 < values.Count; i += 2)
                    result[ToInt(values[i])] = values[i + 1];
            }

            return result;
        }

        private static List<NameNumberPair> ParseStringNumberPairs(ScriptNode node, string content)
        {
            var result = new List<NameNumberPair>();
            if (node == null)
                return result;

            foreach (var item in node.DataItems)
            {
                var tokens = ReadTokens(item.GetContent(content));
                for (var i = 0; i + 1 < tokens.Count; i += 2)
                {
                    if (IsNumberToken(tokens[i]) || !TryParseDouble(tokens[i + 1], out var value))
                        continue;

                    result.Add(new NameNumberPair
                    {
                        Name = NormalizeName(tokens[i]),
                        Value = value,
                    });
                }
            }

            return result;
        }

        private static List<int> ParseIntList(ScriptNode node, string content)
        {
            var result = new List<int>();
            foreach (var value in ParseDoubleList(node, content))
                result.Add(ToInt(value));
            return result;
        }

        private static List<double> ParseDoubleList(ScriptNode node, string content)
        {
            var result = new List<double>();
            if (node == null)
                return result;

            foreach (var item in node.DataItems)
                result.AddRange(ParseDoubles(item.GetContent(content)));

            return result;
        }

        private static List<int> ParseInts(string text)
        {
            var result = new List<int>();
            foreach (var value in ParseDoubles(text))
                result.Add(ToInt(value));
            return result;
        }

        private static List<double> ParseDoubles(string text)
        {
            var result = new List<double>();
            foreach (var token in ReadTokens(text))
            {
                if (TryParseDouble(token, out var value))
                    result.Add(value);
            }

            return result;
        }

        private static List<string> ReadTokens(ScriptNode node, string content)
        {
            var result = new List<string>();
            if (node == null)
                return result;

            foreach (var item in node.DataItems)
                result.AddRange(ReadTokens(item.GetContent(content)));

            return result;
        }

        private static List<string> ReadTokens(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (Match match in Regex.Matches(text, @"`[^`]*`|-?\d+(?:\.\d+)?"))
                result.Add(match.Value);

            return result;
        }

        private static bool TryParseDouble(string token, out double value)
        {
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool IsNumberToken(string token)
        {
            return TryParseDouble(token, out _);
        }

        private static string NormalizeName(string token)
        {
            return (token ?? string.Empty).Trim().Trim('`').Trim();
        }

        private static int ToInt(double value)
        {
            return Convert.ToInt32(Math.Round(value, MidpointRounding.AwayFromZero));
        }

        private sealed class NameNumberPair
        {
            public string Name { get; set; }
            public double Value { get; set; }
        }
    }

    public sealed class UpgradeTableDefinition
    {
        public string TableType { get; set; }
        public List<UpgradeTableRow> Rows { get; set; } = new List<UpgradeTableRow>();
        public List<double> TrailingValues { get; set; } = new List<double>();

        public UpgradeTableRow GetRowByTargetLevel(int targetLevel)
        {
            foreach (var row in Rows)
            {
                if (row.TargetLevel == targetLevel)
                    return row;
            }

            return null;
        }
    }

    public sealed class UpgradeTableRow
    {
        public string TableType { get; set; }
        public int RowIndex { get; set; }
        public double[] Values { get; set; } = Array.Empty<double>();

        public int TargetLevel => RowIndex + 1;
        public double BaseGrowValue => GetValue(0);
        public int FailureWeight => GetInt(6);
        public int DerivedSuccessWeight => Math.Max(0, 100000 - FailureWeight);
        public int PenaltyType => GetInt(7);
        public int PenaltyValue => GetInt(8);
        public int MaterialItemId => GetInt(9);
        public int MaterialCount => GetInt(10);

        public double GetValue(int index)
        {
            return Values != null && index >= 0 && index < Values.Length ? Values[index] : 0;
        }

        public int GetInt(int index)
        {
            return Convert.ToInt32(Math.Round(GetValue(index), MidpointRounding.AwayFromZero));
        }
    }

    public sealed class UpgradeDisjointConfig
    {
        public int DisjointBonusItemId { get; set; } = -1;
        public int EquipLevelConst { get; set; } = -1;
        public Dictionary<string, double> UpgradeFailedBonusWeightByRarity { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public int UpgradeConstForBonusItemCount { get; set; } = -1;
        public Dictionary<string, int> CorrectionGradeByRarity { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class UpgradeMachineFrameRate
    {
        public int UpgradeLevel { get; set; }
        public double[] Values { get; set; } = Array.Empty<double>();
    }
}
