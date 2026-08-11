using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    public sealed class TitleChangeTargetEntry
    {
        public int ItemId { get; set; }

        public int SuccessRate { get; set; }
    }

    public sealed class TitleChangeMainEntry
    {
        public int SourceItemId { get; set; }

        public int SuccessRate { get; set; }

        public List<TitleChangeTargetEntry> Targets { get; } =
            new List<TitleChangeTargetEntry>();
    }

    public sealed class TitleChangeMainFile
    {
        public List<TitleChangeMainEntry> Entries { get; } =
            new List<TitleChangeMainEntry>();

        public static TitleChangeMainFile Parse(string content)
        {
            var file = new TitleChangeMainFile();
            if (string.IsNullOrWhiteSpace(content))
                return file;

            var root = new ScriptParser().Parse(content);
            foreach (var node in root.GetChildren("title change item id"))
            {
                var sourceItemId = PvfScriptValueReader.ReadFirstInteger(node, content);
                var successRate = PvfScriptValueReader.ReadFirstInteger(
                    node.GetChild("success rate"),
                    content);
                var targetValues = PvfScriptValueReader.ReadIntegers(
                    node.GetChild("resource title item"),
                    content);
                if (sourceItemId <= 0
                    || successRate < 0
                    || targetValues.Count == 0
                    || targetValues.Count % 2 != 0)
                {
                    continue;
                }

                var entry = new TitleChangeMainEntry
                {
                    SourceItemId = sourceItemId,
                    SuccessRate = successRate,
                };

                var valid = true;
                for (var index = 0; index < targetValues.Count; index += 2)
                {
                    if (targetValues[index] <= 0 || targetValues[index + 1] < 0)
                    {
                        valid = false;
                        break;
                    }

                    entry.Targets.Add(new TitleChangeTargetEntry
                    {
                        ItemId = targetValues[index],
                        SuccessRate = targetValues[index + 1],
                    });
                }

                if (!valid)
                    continue;

                file.Entries.Add(entry);
            }

            return file;
        }
    }

    public sealed class TitleChangeWeightedItem
    {
        public int ItemId { get; set; }

        public int Weight { get; set; }
    }

    public sealed class TitleChangeSubEntry
    {
        public int TargetItemId { get; set; }

        public List<TitleChangeWeightedItem> SuccessItems { get; } =
            new List<TitleChangeWeightedItem>();

        public List<TitleChangeWeightedItem> FailureItems { get; } =
            new List<TitleChangeWeightedItem>();
    }

    public sealed class TitleChangeSubFile
    {
        public List<TitleChangeSubEntry> Entries { get; } =
            new List<TitleChangeSubEntry>();

        public static TitleChangeSubFile Parse(string content)
        {
            var file = new TitleChangeSubFile();
            if (string.IsNullOrWhiteSpace(content))
                return file;

            var root = new ScriptParser().Parse(content);
            foreach (var node in root.GetChildren("resource title item id"))
            {
                var targetItemId = PvfScriptValueReader.ReadFirstInteger(node, content);
                if (targetItemId <= 0)
                    continue;

                var entry = new TitleChangeSubEntry { TargetItemId = targetItemId };
                if (!TryReadWeightedItems(
                        node.GetChild("success title item"),
                        content,
                        entry.SuccessItems)
                    || !TryReadWeightedItems(
                        node.GetChild("fail title item"),
                        content,
                        entry.FailureItems))
                {
                    continue;
                }

                file.Entries.Add(entry);
            }

            return file;
        }

        private static bool TryReadWeightedItems(
            ScriptNode node,
            string content,
            ICollection<TitleChangeWeightedItem> result)
        {
            var values = PvfScriptValueReader.ReadIntegers(node, content);
            if (values.Count % 2 != 0)
                return false;

            for (var index = 0; index < values.Count; index += 2)
            {
                if (values[index] <= 0 || values[index + 1] <= 0)
                    return false;

                result.Add(new TitleChangeWeightedItem
                {
                    ItemId = values[index],
                    Weight = values[index + 1],
                });
            }

            return true;
        }
    }

}
