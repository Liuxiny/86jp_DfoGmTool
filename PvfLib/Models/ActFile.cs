using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GmPvfLib
{
    
    
    
    
    public class ActFile : PvfModelBase
    {
        public string BaseAnimation { get; set; }
        public bool HoldPosition { get; set; }
        public List<string> MotionSummaries { get; set; } = new List<string>();
        public List<string> TriggerSummaries { get; set; } = new List<string>();
        public List<string> BehaviorSummaries { get; set; } = new List<string>();

        public int MotionCount => MotionSummaries.Count;
        public int TriggerCount => TriggerSummaries.Count;
        public int BehaviorCount => BehaviorSummaries.Count;

        public static ActFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new ActFile { Content = content ?? string.Empty, Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var act = new ActFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                switch (node.Tag.ToLowerInvariant())
                {
                    case "motion":
                        if (string.IsNullOrWhiteSpace(act.BaseAnimation))
                        {
                            string motionData = GetNodeData(node, content);
                            act.BaseAnimation = !string.IsNullOrWhiteSpace(motionData)
                                ? StripBacktick(motionData)
                                : FindFirstDataRecursive(node, "base ani", content);
                        }

                        string motionSummary = BuildSummary(node, content);
                        if (!string.IsNullOrWhiteSpace(motionSummary))
                            act.MotionSummaries.Add(motionSummary);
                        break;
                    case "hold position":
                        act.HoldPosition = true;
                        break;
                    case "trigger":
                        act.TriggerSummaries.Add(BuildSummary(node, content));
                        break;
                    case "behavior":
                        act.BehaviorSummaries.Add(BuildSummary(node, content));
                        break;
                }
            }

            return act;
        }

        private static string BuildSummary(ScriptNode node, string content)
        {
            var parts = new List<string>();
            foreach (var child in node.Children)
            {
                string data = GetNodeData(child, content);
                if (child.Children != null && child.Children.Count > 0)
                {
                    string nestedTags = string.Join(", ", child.Children.Select(c => NormalizeTag(c.Tag)).Take(4));
                    if (child.Children.Count > 4)
                        nestedTags += ", ...";

                    if (string.IsNullOrEmpty(data))
                        parts.Add(NormalizeTag(child.Tag) + ": " + nestedTags);
                    else
                        parts.Add(NormalizeTag(child.Tag) + "=" + TrimDisplay(data) + " (" + nestedTags + ")");
                }
                else if (!string.IsNullOrEmpty(data))
                {
                    parts.Add(NormalizeTag(child.Tag) + "=" + TrimDisplay(data));
                }
                else
                {
                    parts.Add(NormalizeTag(child.Tag));
                }
            }

            string summary = string.Join(" | ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            return string.IsNullOrWhiteSpace(summary) ? NormalizeTag(node.Tag) : summary;
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
            if (value.Length <= 80)
                return value;

            return value.Substring(0, 80) + "...";
        }

        private static string GetNodeData(ScriptNode node, string content)
        {
            if (node == null || node.DataItems == null || node.DataItems.Count == 0)
                return string.Empty;

            return string.Join(" ", node.DataItems.Select(item => item.GetContent(content).Trim()).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private static string FindFirstDataRecursive(ScriptNode node, string tag, string content)
        {
            if (node == null)
                return null;

            if (string.Equals(node.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                string data = GetNodeData(node, content);
                return string.IsNullOrWhiteSpace(data) ? null : StripBacktick(data);
            }

            foreach (var child in node.Children)
            {
                string value = FindFirstDataRecursive(child, tag, content);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }
    }
}
