using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GmPvfLib
{
    
    
    
    public class ScriptNode
    {
        public string Tag { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public int StartLineIndex { get; set; }
        public int EndLineIndex { get; set; }
        public bool HasEndTag { get; set; }
        public List<ScriptNode> Children { get; set; } = new List<ScriptNode>();
        public List<ScriptDataItem> DataItems { get; set; } = new List<ScriptDataItem>();

        public string GetContent(string fullText)
        {
            if (StartIndex < 0 || EndIndex <= StartIndex || EndIndex > fullText.Length)
                return string.Empty;
            return fullText.Substring(StartIndex, EndIndex - StartIndex);
        }

        public ScriptNode GetChild(string tag)
        {
            return Children.FirstOrDefault(c =>
                string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase));
        }

        public List<ScriptNode> GetChildren(string tag)
        {
            return Children
                .Where(c => string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public string GetFirstDataContent(string fullText)
        {
            return DataItems.Count > 0
                ? DataItems[0].GetContent(fullText).Trim()
                : string.Empty;
        }

        public string FormatTree(string fullText, int indentLevel = 0, bool excludeRoot = false)
        {
            var sb = new StringBuilder();
            bool isRoot = Tag == "ROOT";

            if (isRoot && excludeRoot)
            {
                foreach (var child in Children)
                    sb.Append(child.FormatTree(fullText, indentLevel));

                string rootIndent = new string(' ', indentLevel * 4);
                foreach (var item in DataItems)
                {
                    string content = item.GetContent(fullText).Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                        sb.AppendLine($"{rootIndent}{content}");
                }
            }
            else
            {
                string indent = new string(' ', indentLevel * 4);
                string dataIndent = new string(' ', (indentLevel + 1) * 4);

                if (Tag != "DATA" && Tag != "ROOT")
                    sb.AppendLine($"{indent}[{Tag}]");

                foreach (var item in DataItems)
                {
                    string content = item.GetContent(fullText).Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                        sb.AppendLine($"{dataIndent}{content}");
                }

                foreach (var child in Children)
                    sb.Append(child.FormatTree(fullText, indentLevel + 1));

                if (HasEndTag && Tag != "DATA" && Tag != "ROOT")
                    sb.AppendLine($"{indent}[/{Tag}]");
            }

            return sb.ToString();
        }
    }

    
    
    
    public class ScriptDataItem
    {
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public int LineIndex { get; set; }

        public string GetContent(string fullText)
        {
            if (StartIndex < 0 || EndIndex <= StartIndex || EndIndex > fullText.Length)
                return string.Empty;
            return fullText.Substring(StartIndex, EndIndex - StartIndex);
        }
    }
}
