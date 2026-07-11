using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    public class ScriptParser
    {
        private string _text;
        private List<string> _lines;
        private int[] _lineOffsets; 

        
        
        
        public string Format(string content, bool excludeRoot = true)
        {
            var root = Parse(content);
            return root.FormatTree(content, 0, excludeRoot);
        }

        
        
        
        public ScriptNode Parse(string content)
        {
            _text = content;
            _lines = SplitLines(content);
            BuildLineOffsets();

            var root = new ScriptNode
            {
                Tag = "ROOT",
                StartIndex = 0,
                EndIndex = content.Length,
                HasEndTag = false
            };

            ParseRootContent(root, 0, _lines.Count);
            return root;
        }

        #region 解析逻辑

        private void ParseRootContent(ScriptNode root, int startLine, int endLine)
        {
            int cur = startLine;
            while (cur < endLine)
            {
                string trimmed = _lines[cur].Trim();

                if (string.IsNullOrWhiteSpace(trimmed) || trimmed[0] == '#')
                {
                    cur++;
                    continue;
                }

                if (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
                {
                    string tag = trimmed.Substring(1, trimmed.Length - 2);
                    if (tag[0] == '/') { cur++; continue; }

                    var node = ParseNode(tag, cur, endLine);
                    if (node != null)
                    {
                        root.Children.Add(node);
                        cur = node.EndLineIndex + 1;
                    }
                    else cur++;
                }
                else
                {
                    root.DataItems.Add(CreateDataItem(cur));
                    cur++;
                }
            }
        }

        private ScriptNode ParseNode(string tag, int startLine, int endLine)
        {
            int endTagLine = FindEndTag(tag, startLine + 1, endLine);

            var node = new ScriptNode
            {
                Tag = tag,
                StartIndex = LineStart(startLine),
                StartLineIndex = startLine
            };

            if (endTagLine >= 0)
            {
                node.HasEndTag = true;
                node.EndLineIndex = endTagLine;
                node.EndIndex = LineEnd(endTagLine);
                ParseNodeContent(node, startLine + 1, endTagLine - 1);
            }
            else
            {
                node.HasEndTag = false;
                node.EndLineIndex = FindDataNodeEnd(startLine + 1, endLine);
                node.EndIndex = LineEnd(node.EndLineIndex);
                ParseDataRange(node, startLine + 1, node.EndLineIndex);
            }

            return node;
        }

        private void ParseNodeContent(ScriptNode node, int startLine, int endLine)
        {
            int cur = startLine;
            while (cur <= endLine)
            {
                string trimmed = _lines[cur].Trim();

                if (string.IsNullOrWhiteSpace(trimmed) || trimmed[0] == '#')
                {
                    cur++;
                    continue;
                }

                if (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
                {
                    string tag = trimmed.Substring(1, trimmed.Length - 2);
                    if (tag[0] == '/') { cur++; continue; }

                    var child = ParseNode(tag, cur, endLine);
                    if (child != null)
                    {
                        node.Children.Add(child);
                        cur = child.EndLineIndex + 1;
                    }
                    else cur++;
                }
                else
                {
                    node.DataItems.Add(CreateDataItem(cur));
                    cur++;
                }
            }
        }

        private void ParseDataRange(ScriptNode node, int startLine, int endLine)
        {
            for (int i = startLine; i <= endLine; i++)
            {
                string trimmed = _lines[i].Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && trimmed[0] != '#')
                    node.DataItems.Add(CreateDataItem(i));
            }
        }

        #endregion

        #region 查找辅助

        private int FindEndTag(string tag, int startLine, int endLine)
        {
            int depth = 0;
            string openTag = tag;
            string closeTag = "/" + tag;

            for (int i = startLine; i < endLine; i++)
            {
                string trimmed = _lines[i].Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed[0] == '#') continue;

                if (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
                {
                    string t = trimmed.Substring(1, trimmed.Length - 2);
                    if (t == openTag) depth++;
                    else if (t == closeTag)
                    {
                        if (depth == 0) return i;
                        depth--;
                    }
                }
            }
            return -1;
        }

        private int FindDataNodeEnd(int startLine, int endLine)
        {
            for (int i = startLine; i < endLine; i++)
            {
                string trimmed = _lines[i].Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed[0] == '#') continue;
                if (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
                    return i - 1;
            }
            return endLine - 1;
        }

        #endregion

        #region 行索引与偏移（O(1) 访问）

        private ScriptDataItem CreateDataItem(int lineIndex)
        {
            return new ScriptDataItem
            {
                StartIndex = LineStart(lineIndex),
                EndIndex = LineEnd(lineIndex),
                LineIndex = lineIndex
            };
        }

        
        
        
        private void BuildLineOffsets()
        {
            _lineOffsets = new int[_lines.Count + 1];
            int offset = 0;
            for (int i = 0; i < _lines.Count; i++)
            {
                _lineOffsets[i] = offset;
                offset += _lines[i].Length;
            }
            _lineOffsets[_lines.Count] = offset;
        }

        private int LineStart(int lineIndex)
        {
            return lineIndex >= 0 && lineIndex < _lineOffsets.Length
                ? _lineOffsets[lineIndex]
                : 0;
        }

        private int LineEnd(int lineIndex)
        {
            return lineIndex >= 0 && lineIndex + 1 < _lineOffsets.Length
                ? _lineOffsets[lineIndex + 1]
                : _text.Length;
        }

        private static List<string> SplitLines(string text)
        {
            var lines = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lines.Add(text.Substring(start, i - start + 1));
                    start = i + 1;
                }
            }
            if (start < text.Length)
                lines.Add(text.Substring(start));
            return lines;
        }

        #endregion
    }
}
