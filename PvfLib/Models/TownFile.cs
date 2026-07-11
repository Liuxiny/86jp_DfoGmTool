using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GmPvfLib
{
    
    
    
    public class TownPermission
    {
        public int NeedLevel { get; set; } = -1;
        public int NeedQuest { get; set; } = -1;
        public string ExceptionalCharacter { get; set; }
        public int ExceptionalNeedQuest { get; set; } = -1;
        public int ExceptionalNeedLevel { get; set; } = -1;
        public string PlayMovie { get; set; }
    }

    
    
    
    public class TownArea
    {
        public int Id { get; set; }
        public string MapPath { get; set; }
        public int[] MinimapRect { get; set; }      
        public string AreaType { get; set; }         
        public int LinkedId { get; set; } = -1;      
        public int LinkedId2 { get; set; } = -1;     
        public TownPermission Permission { get; set; }
    }

    
    
    
    public class TownFile
    {
        public string Name { get; set; }
        public string EnteringTitle { get; set; }
        public string CutsceneImage { get; set; }
        public int CutsceneImageParam { get; set; } = -1;
        public int DungeonWhatMustBeCleared { get; set; } = -1;
        public int OnlyServerParsingDungeonWhatMustBeCleared { get; set; } = -1;
        public int LimitLevel { get; set; } = -1;
        public TownPermission Permission { get; set; }   
        public List<TownArea> Areas { get; set; } = new List<TownArea>();

        
        private static readonly Regex AreaLineRx = new Regex(
            @"^(\d+)\s+`([^`]+)`\s*(.*)", RegexOptions.Compiled);

        private static readonly Regex MinimapRx = new Regex(
            @"`\[minimap rect\]`\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+`\[/minimap rect\]`",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AreaTypeRx = new Regex(
            @"`\[(normal|gate|dungeon gate)\]`\s*(.*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        
        public static TownFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content)) return new TownFile();
            var root = new ScriptParser().Parse(content);
            return FromNode(root, content);
        }

        #region 树节点 → 类型化对象

        private static TownFile FromNode(ScriptNode root, string text)
        {
            var twn = new TownFile();
            foreach (var child in root.Children)
            {
                switch (child.Tag.ToLowerInvariant())
                {
                    case "name":
                        twn.Name = StripBacktick(child.GetFirstDataContent(text));
                        break;
                    case "entering title":
                        twn.EnteringTitle = StripBacktick(child.GetFirstDataContent(text));
                        break;
                    case "cutscene image":
                        ParseCutsceneImage(child.GetFirstDataContent(text), twn);
                        break;
                    case "dungeon what must be cleared":
                        twn.DungeonWhatMustBeCleared = ParseInt(child.GetFirstDataContent(text), -1);
                        break;
                    case "only server parsing dungeon what must be cleared":
                        twn.OnlyServerParsingDungeonWhatMustBeCleared = ParseInt(child.GetFirstDataContent(text), -1);
                        break;
                    case "limit level":
                        twn.LimitLevel = ParseInt(child.GetFirstDataContent(text), -1);
                        break;
                    case "permission":
                        twn.Permission = ParsePermission(child, text);
                        break;
                    case "area":
                        twn.Areas.Add(ParseArea(child, text));
                        break;
                }
            }
            return twn;
        }

        private static TownPermission ParsePermission(ScriptNode node, string text)
        {
            var perm = new TownPermission();
            foreach (var child in node.Children)
            {
                switch (child.Tag.ToLowerInvariant())
                {
                    case "need level":
                        perm.NeedLevel = ParseInt(child.GetFirstDataContent(text), -1);
                        break;
                    case "need quest":
                        perm.NeedQuest = ParseInt(child.GetFirstDataContent(text), -1);
                        break;
                    case "play movie":
                        perm.PlayMovie = StripBacktick(child.GetFirstDataContent(text));
                        break;
                    case "exceptional character":
                        perm.ExceptionalCharacter = StripBacktick(child.GetFirstDataContent(text));
                        
                        foreach (var sub in child.Children)
                        {
                            switch (sub.Tag.ToLowerInvariant())
                            {
                                case "need quest":
                                    perm.ExceptionalNeedQuest = ParseInt(sub.GetFirstDataContent(text), -1);
                                    break;
                                case "need level":
                                    perm.ExceptionalNeedLevel = ParseInt(sub.GetFirstDataContent(text), -1);
                                    break;
                            }
                        }
                        break;
                }
            }
            return perm;
        }

        private static TownArea ParseArea(ScriptNode node, string text)
        {
            var area = new TownArea();

            
            var permNode = node.GetChild("permission");
            if (permNode != null)
                area.Permission = ParsePermission(permNode, text);

            
            var dataLines = node.DataItems
                .Select(d => d.GetContent(text).Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s));
            string combined = string.Join(" ", dataLines).Trim();
            if (string.IsNullOrEmpty(combined)) return area;

            var m = AreaLineRx.Match(combined);
            if (m.Success)
            {
                area.Id = int.Parse(m.Groups[1].Value);
                area.MapPath = m.Groups[2].Value;
                ParseAreaRest(m.Groups[3].Value.Trim(), area);
            }
            return area;
        }

        #endregion

        #region 辅助方法

        private static string StripBacktick(string s)
        {
            if (s != null && s.Length >= 2 && s[0] == '`' && s[s.Length - 1] == '`')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        private static int ParseInt(string s, int defaultVal)
        {
            int result;
            return int.TryParse(s.Trim(), out result) ? result : defaultVal;
        }

        private static void ParseCutsceneImage(string val, TownFile twn)
        {
            var parts = val.Split(new[] { '`' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1) twn.CutsceneImage = parts[0].Trim();
            if (parts.Length >= 2)
            {
                int p;
                if (int.TryParse(parts[1].Trim(), out p)) twn.CutsceneImageParam = p;
            }
        }

        private static void ParseAreaRest(string rest, TownArea area)
        {
            if (string.IsNullOrEmpty(rest)) return;

            var mm = MinimapRx.Match(rest);
            if (mm.Success)
            {
                area.MinimapRect = new[]
                {
                    int.Parse(mm.Groups[1].Value),
                    int.Parse(mm.Groups[2].Value),
                    int.Parse(mm.Groups[3].Value),
                    int.Parse(mm.Groups[4].Value)
                };
                rest = rest.Substring(mm.Index + mm.Length).Trim();
            }

            var at = AreaTypeRx.Match(rest);
            if (at.Success)
            {
                area.AreaType = at.Groups[1].Value.ToLowerInvariant();
                ParseAreaTypeParams(at.Groups[2].Value.Trim(), area);
            }
        }

        private static void ParseAreaTypeParams(string s, TownArea area)
        {
            if (string.IsNullOrEmpty(s)) return;
            var nums = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (nums.Length >= 1)
            {
                int v;
                if (int.TryParse(nums[0], out v)) area.LinkedId = v;
            }
            if (nums.Length >= 2)
            {
                int v;
                if (int.TryParse(nums[1], out v)) area.LinkedId2 = v;
            }
        }

        #endregion
    }
}
