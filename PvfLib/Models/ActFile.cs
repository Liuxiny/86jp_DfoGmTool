using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GmPvfLib
{
    public sealed class ActBehaviorReference
    {
        public string Target { get; set; }
        public int BehaviorIndex { get; set; } = -1;
    }

    public sealed class ActTriggerInfo
    {
        public int? FrameStart { get; set; }
        public int? FrameEnd { get; set; }
        public List<string> Selectors { get; set; } = new List<string>();
        public List<string> ObjectTypes { get; set; } = new List<string>();
        public int? ObjectIndex { get; set; }
        public bool CheckedNo { get; set; }
        public string Comparison { get; set; }
        public int? ComparisonValue { get; set; }
        public bool ComparisonPercent { get; set; }
        public List<ActBehaviorReference> BehaviorReferences { get; set; } =
            new List<ActBehaviorReference>();
    }

    public sealed class ActBehaviorInfo
    {
        public int Index { get; set; }
        public bool SetsAction { get; set; }
        public int? CustomActionIndex { get; set; }
        public bool RestoresHp { get; set; }
        public int? RestoreHpValue { get; set; }
        public bool RestoreHpPercent { get; set; }
    }

    public class ActFile : PvfModelBase
    {
        public string BaseAnimation { get; set; }
        public bool HoldPosition { get; set; }
        public List<string> MotionSummaries { get; set; } = new List<string>();
        public List<string> TriggerSummaries { get; set; } = new List<string>();
        public List<string> BehaviorSummaries { get; set; } = new List<string>();
        public List<ActTriggerInfo> Triggers { get; set; } = new List<ActTriggerInfo>();
        public List<ActBehaviorInfo> Behaviors { get; set; } = new List<ActBehaviorInfo>();
        public bool HasNpcItemDrop { get; private set; }

        public int MotionCount => MotionSummaries.Count;
        public int TriggerCount => TriggerSummaries.Count;
        public int BehaviorCount => BehaviorSummaries.Count;

        public static ActFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new ActFile { Content = content ?? string.Empty, Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var act = new ActFile { Root = root, Content = content };
            act.HasNpcItemDrop = ContainsTagRecursive(root, "npc item drop");

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
                        act.Triggers.Add(ParseTrigger(node, content));
                        break;
                    case "behavior":
                        act.BehaviorSummaries.Add(BuildSummary(node, content));
                        act.Behaviors.Add(ParseBehavior(
                            node,
                            content,
                            act.Behaviors.Count));
                        break;
                }
            }

            return act;
        }

        private static ActTriggerInfo ParseTrigger(
            ScriptNode node,
            string content)
        {
            var result = new ActTriggerInfo();

            var frame = node.GetChild("frame");
            var frameValues = ParseInts(GetNodeData(frame, content));
            if (frameValues.Count > 0)
            {
                result.FrameStart = frameValues[0];
                result.FrameEnd = frameValues.Count > 1
                    ? frameValues[1]
                    : frameValues[0];
            }

            var which = node.GetChild("which");
            if (which != null)
            {
                AddDirectChildTags(which, result.Selectors);
                AddFollowingSectionTags(
                    node.Children,
                    node.Children.IndexOf(which),
                    result.Selectors);
            }

            var checkup = node.GetChild("checkup");
            if (checkup != null)
            {
                var isIndex = FindFirstNodeRecursive(checkup, "is index");
                var indexValues = ParseInts(GetNodeData(isIndex, content));
                if (indexValues.Count > 0)
                    result.ObjectIndex = indexValues[0];

                var objectType = FindFirstNodeRecursive(checkup, "is object type");
                if (objectType != null)
                    AddDirectChildTags(objectType, result.ObjectTypes);
            }

            result.CheckedNo = ContainsTagRecursive(node, "checked no");
            result.ComparisonPercent = ContainsTagRecursive(node, "%");

            foreach (var child in node.Children)
            {
                if (IsComparisonTag(child.Tag))
                {
                    result.Comparison = child.Tag.Trim();
                    var values = ParseInts(GetNodeData(child, content));
                    if (values.Count > 0)
                        result.ComparisonValue = values[0];
                    break;
                }
            }

            for (var childIndex = 0; childIndex < node.Children.Count; childIndex++)
            {
                var behaviorNode = node.Children[childIndex];
                if (!string.Equals(
                        behaviorNode.Tag,
                        "do behavior",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var targetNode in behaviorNode.Children)
                {
                    AddBehaviorReference(
                        targetNode,
                        content,
                        result.BehaviorReferences);
                }

                if (childIndex + 1 < node.Children.Count
                    && !IsTriggerSectionBoundary(
                        node.Children[childIndex + 1].Tag))
                {
                    AddBehaviorReference(
                        node.Children[childIndex + 1],
                        content,
                        result.BehaviorReferences);
                }
            }

            return result;
        }

        private static ActBehaviorInfo ParseBehavior(
            ScriptNode node,
            string content,
            int index)
        {
            var result = new ActBehaviorInfo
            {
                Index = index,
                SetsAction = ContainsTagRecursive(node, "set action"),
            };

            var custom = FindFirstNodeRecursive(node, "custom");
            var customValues = ParseInts(GetNodeData(custom, content));
            if (customValues.Count > 0)
                result.CustomActionIndex = customValues[0];

            var restore = FindFirstNodeRecursive(node, "restore");
            if (restore != null)
            {
                // RESTORE is commonly a section marker whose HP/% arguments
                // are following siblings rather than nested children.
                var hp = FindFirstNodeRecursive(node, "hp");
                var hpValues = ParseInts(GetNodeData(hp, content));
                result.RestoresHp = hp != null;
                if (hpValues.Count > 0)
                    result.RestoreHpValue = hpValues[0];
                result.RestoreHpPercent = ContainsTagRecursive(node, "%");
            }

            return result;
        }

        private static void AddFollowingSectionTags(
            IReadOnlyList<ScriptNode> siblings,
            int markerIndex,
            ICollection<string> destination)
        {
            if (siblings == null || markerIndex < 0)
                return;

            for (var index = markerIndex + 1; index < siblings.Count; index++)
            {
                var candidate = siblings[index];
                if (IsTriggerSectionBoundary(candidate.Tag))
                    break;

                var tag = NormalizeTag(candidate.Tag);
                if (!string.IsNullOrWhiteSpace(tag))
                    destination.Add(tag);
            }
        }

        private static void AddBehaviorReference(
            ScriptNode targetNode,
            string content,
            ICollection<ActBehaviorReference> destination)
        {
            var values = ParseInts(GetNodeData(targetNode, content));
            if (values.Count == 0)
                return;

            destination.Add(new ActBehaviorReference
            {
                Target = NormalizeTag(targetNode.Tag),
                BehaviorIndex = values[0],
            });
        }

        private static bool IsTriggerSectionBoundary(string tag)
            => string.Equals(tag, "frame", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "which", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "checkup", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "checked no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "do behavior", StringComparison.OrdinalIgnoreCase)
                || IsComparisonTag(tag);

        private static void AddDirectChildTags(
            ScriptNode node,
            ICollection<string> destination)
        {
            foreach (var child in node.Children)
            {
                var tag = NormalizeTag(child.Tag);
                if (!string.IsNullOrWhiteSpace(tag))
                    destination.Add(tag);
            }
        }

        private static ScriptNode FindFirstNodeRecursive(
            ScriptNode node,
            string tag)
        {
            if (node == null)
                return null;
            if (string.Equals(node.Tag, tag, StringComparison.OrdinalIgnoreCase))
                return node;

            foreach (var child in node.Children)
            {
                var match = FindFirstNodeRecursive(child, tag);
                if (match != null)
                    return match;
            }

            return null;
        }

        private static List<int> ParseInts(string data)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(data))
                return result;

            foreach (var value in data.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(value, out var parsed))
                    result.Add(parsed);
            }

            return result;
        }

        private static bool IsComparisonTag(string tag)
            => tag == "<"
                || tag == "<="
                || tag == "="
                || tag == ">="
                || tag == ">";

        private static bool ContainsTagRecursive(ScriptNode node, string tag)
        {
            if (node == null)
                return false;

            if (string.Equals(node.Tag, tag, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var child in node.Children)
            {
                if (ContainsTagRecursive(child, tag))
                    return true;
            }

            return false;
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
