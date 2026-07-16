using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GmPvfLib;

namespace DfoGmTool.Services
{
    public sealed partial class PvfIndexService
    {
        private static readonly HashSet<string> DungeonPermissionPathPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "act1",
            "act2",
            "act3",
            "act4",
            "act5",
            "act6",
            "act7",
            "act8",
            "act9",
            "village",
            "sainthorn",
            "ancient",
            "timegate",
            "impossible",
            "stormofmetastasis",
            "southerndale",
            "castleofthedead",
            "anton",
            "anton_normal",
            "shonantournament",
            "gtimespiral",
            "tau_kingdom",
        };

        public IReadOnlyList<int> GetDungeonPermissionIds()
        {
            var ids = _dungeonPermissionIds;
            if (ids != null)
                return ids;
            return Array.Empty<int>();
        }

        private static List<int> BuildDungeonPermissionIds(PvfArchive archive)
        {
            var result = new List<int>();
            string text;
            try
            {
                text = archive.GetFileContent("dungeon/dungeon.lst");
            }
            catch
            {
                return result;
            }
            if (string.IsNullOrEmpty(text))
                return result;

            foreach (Match match in LstPattern.Matches(text))
            {
                int id;
                if (!int.TryParse(match.Groups[1].Value, out id) || id <= 0)
                    continue;
                var filePath = match.Groups[2].Value.Replace('\\', '/').Trim();
                if (filePath.Length == 0)
                    continue;
                var slash = filePath.IndexOf('/');
                var prefix = slash >= 0 ? filePath.Substring(0, slash) : filePath;
                if (DungeonPermissionPathPrefixes.Contains(prefix))
                    result.Add(id);
            }

            return result;
        }
    }
}
