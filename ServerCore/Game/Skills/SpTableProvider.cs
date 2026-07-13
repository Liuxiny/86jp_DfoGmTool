using DfoGmTool.ServerCore.GameWorld;
using System;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.Skills
{
    public static class SpTableProvider
    {
        private static readonly object _lock = new object();
        private static Dictionary<int, int> _spPerLevel;

        internal static void ResetForPvfChange()
        {
            lock (_lock)
                _spPerLevel = null;
        }

        public static int GetSpAtLevel(int level)
        {
            EnsureLoaded();
            return _spPerLevel.TryGetValue(level, out var sp) ? sp : 0;
        }

        public static int GetTotalSp(int level)
        {
            EnsureLoaded();
            int total = 0;
            for (int lv = 1; lv <= level; lv++)
            {
                if (_spPerLevel.TryGetValue(lv, out var sp))
                    total += sp;
            }
            return total;
        }

        private static void EnsureLoaded()
        {
            if (_spPerLevel != null) return;
            lock (_lock)
            {
                if (_spPerLevel != null) return;
                _spPerLevel = ParseFromPvf();
            }
        }

        private static Dictionary<int, int> ParseFromPvf()
        {
            var result = new Dictionary<int, int>();
            try
            {
                var text = GameWorld.PvfArchiveAccessor.ReadText("Etc/spTable.etc");
                if (string.IsNullOrEmpty(text)) return result;

                var startTag = "[sp table]";
                var endTag = "[/sp table]";
                var start = text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
                var end = text.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
                if (start < 0 || end < 0) return result;

                var content = text.Substring(start + startTag.Length, end - start - startTag.Length).Trim();
                var tokens = content.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i + 1 < tokens.Length; i += 2)
                {
                    if (int.TryParse(tokens[i], out var level) && int.TryParse(tokens[i + 1], out var sp))
                        result[level] = sp;
                }

                FileLogger.Log($"[SpTableProvider] Loaded {result.Count} entries from PVF (Lv86 total={GetTotalSpInternal(result, 86)})");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[SpTableProvider] ERROR: {ex.Message}");
            }
            return result;
        }

        private static int GetTotalSpInternal(Dictionary<int, int> table, int level)
        {
            int total = 0;
            for (int lv = 1; lv <= level; lv++)
            {
                if (table.TryGetValue(lv, out var sp))
                    total += sp;
            }
            return total;
        }
    }
}
