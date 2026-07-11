using System;
using System.Collections.Generic;
using System.Linq;

namespace GmPvfLib
{
    
    
    
    
    public class EvtFile : PvfModelBase
    {
        public int Id { get; set; } = -1;
        public int PurchaseGold { get; set; } = -1;
        public int DayForExpiration { get; set; } = -1;
        public int RewardItem { get; set; } = -1;
        public int EntryCount { get; set; }
        public int EventCount { get; set; }
        public int StepCount { get; set; }
        public List<string> ServerStateSummaries { get; set; } = new List<string>();
        public List<string> EntrySummaries { get; set; } = new List<string>();
        public List<string> EventSummaries { get; set; } = new List<string>();
        public List<string> StepSummaries { get; set; } = new List<string>();

        public static EvtFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new EvtFile { Content = content ?? string.Empty, Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var evt = new EvtFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = GetNodeData(node, content);
                switch (node.Tag.ToLowerInvariant())
                {
                    case "id":
                        evt.Id = ParseInt(data);
                        break;
                    case "purchase gold":
                        evt.PurchaseGold = ParseInt(data);
                        break;
                    case "day for expiration":
                        evt.DayForExpiration = ParseInt(data);
                        break;
                    case "reward item":
                        evt.RewardItem = ParseInt(data);
                        break;
                    case "entry":
                        evt.EntryCount++;
                        evt.EntrySummaries.Add(BuildSummary(node, content));
                        break;
                    case "event":
                        evt.EventCount++;
                        evt.EventSummaries.Add(BuildSummary(node, content));
                        break;
                    case "step":
                        evt.StepCount++;
                        evt.StepSummaries.Add(BuildSummary(node, content));
                        break;
                    case "general server on":
                    case "general server off":
                    case "event server on":
                    case "event server off":
                    case "starter server on":
                    case "starter server off":
                    case "on":
                    case "off":
                        evt.ServerStateSummaries.Add(NormalizeTag(node.Tag) + ": " + BuildSummary(node, content));
                        break;
                }
            }

            return evt;
        }

        private static string BuildSummary(ScriptNode node, string content)
        {
            var parts = new List<string>();
            foreach (var child in node.Children)
            {
                string data = GetNodeData(child, content);
                if (child.Children != null && child.Children.Count > 0)
                {
                    string nested = string.Join(", ", child.Children.Select(c => NormalizeTag(c.Tag)).Take(4));
                    if (child.Children.Count > 4)
                        nested += ", ...";

                    if (string.IsNullOrWhiteSpace(data))
                        parts.Add(NormalizeTag(child.Tag) + ": " + nested);
                    else
                        parts.Add(NormalizeTag(child.Tag) + "=" + TrimDisplay(data) + " (" + nested + ")");
                }
                else if (!string.IsNullOrWhiteSpace(data))
                {
                    parts.Add(NormalizeTag(child.Tag) + "=" + TrimDisplay(data));
                }
                else
                {
                    parts.Add(NormalizeTag(child.Tag));
                }
            }

            string selfData = GetNodeData(node, content);
            if (!string.IsNullOrWhiteSpace(selfData))
                parts.Insert(0, TrimDisplay(selfData));

            string summary = string.Join(" | ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            return string.IsNullOrWhiteSpace(summary) ? NormalizeTag(node.Tag) : summary;
        }

        private static string GetNodeData(ScriptNode node, string content)
        {
            if (node == null || node.DataItems == null || node.DataItems.Count == 0)
                return string.Empty;

            return string.Join(" ", node.DataItems.Select(item => item.GetContent(content).Trim()).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private static string NormalizeTag(string tag)
        {
            return tag?.Trim('[', ']') ?? string.Empty;
        }

        private static string TrimDisplay(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            value = StripBacktick(value);
            if (value.Length <= 100)
                return value;

            return value.Substring(0, 100) + "...";
        }
    }
}
