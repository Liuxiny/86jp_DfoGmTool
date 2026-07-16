using System;
using System.Collections.Generic;
using System.IO;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed class AvatarDurationOption
    {
        public int DurationDays { get; set; }

        public int CeraPrice { get; set; }
    }

    internal static class AvatarDurationResolver
    {
        private const string Tag = "[avatar type select]";
        private const string EndTag = "[/avatar type select]";
        private static readonly object Sync = new object();
        private static readonly Dictionary<int, IReadOnlyList<AvatarDurationOption>> Cache =
            new Dictionary<int, IReadOnlyList<AvatarDurationOption>>();

        internal static void ResetForPvfChange()
        {
            lock (Sync)
                Cache.Clear();
        }

        internal static IReadOnlyList<AvatarDurationOption> Resolve(int itemTemplateId)
        {
            lock (Sync)
            {
                if (Cache.TryGetValue(itemTemplateId, out var cached))
                    return cached;
            }

            IReadOnlyList<AvatarDurationOption> resolved = Array.Empty<AvatarDurationOption>();
            var entry = ItemMetadataResolver.GetEquipmentEntry(itemTemplateId);
            if (entry != null)
            {
                var text = GameWorld.PvfArchiveAccessor.ReadText(Path.Combine("equipment", entry.FilePath));
                resolved = Parse(text);
            }

            lock (Sync)
                Cache[itemTemplateId] = resolved;
            return resolved;
        }

        internal static bool ContainsDuration(IReadOnlyList<AvatarDurationOption> options, int days)
        {
            if (options == null)
                return false;
            foreach (var option in options)
            {
                if (option.DurationDays == days)
                    return true;
            }
            return false;
        }

        internal static IReadOnlyList<AvatarDurationOption> Parse(string text)
        {
            var result = new List<AvatarDurationOption>();
            var seenDays = new HashSet<int>();
            if (string.IsNullOrEmpty(text))
                return result;

            var start = text.IndexOf(Tag, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return result;
            start += Tag.Length;
            var end = text.IndexOf(EndTag, start, StringComparison.OrdinalIgnoreCase);
            var section = end > start ? text.Substring(start, end - start) : text.Substring(start);

            var values = new List<int>();
            foreach (var token in section.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(token, out var value))
                    break;
                values.Add(value);
            }

            for (var index = 0; index + 6 < values.Count; index += 7)
            {
                if (!seenDays.Add(values[index]))
                    continue;
                result.Add(new AvatarDurationOption
                {
                    DurationDays = values[index],
                    CeraPrice = values[index + 3],
                });
            }
            return result;
        }
    }
}
