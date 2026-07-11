using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    
    public class AiFile
    {
        
        public bool HasAiPattern { get; set; }
        
        public int ThinkCount { get; set; }
        
        public List<string> ImportAis { get; set; } = new List<string>();
        
        public string ReturnValue { get; set; }

        
        public ScriptNode Root { get; private set; }
        
        public string Content { get; private set; }

        #region 动态访问

        public string GetValue(string tag)
        {
            var node = Root.GetChild(tag);
            return node != null ? node.GetFirstDataContent(Content).Trim() : null;
        }

        public bool HasTag(string tag)
        {
            return Root.GetChild(tag) != null;
        }

        #endregion

        #region 解析

        public static AiFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new AiFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var ai = new AiFile { Root = root, Content = content };

            
            var patternNode = root.GetChild("ai pattern");
            if (patternNode != null)
            {
                ai.HasAiPattern = true;
                
                CollectMetadata(patternNode, ai, content);
            }
            else
            {
                
                CollectMetadata(root, ai, content);
            }

            return ai;
        }

        private static void CollectMetadata(ScriptNode parent, AiFile ai, string content)
        {
            if (parent.Children == null) return;
            foreach (var node in parent.Children)
            {
                string tag = node.Tag.ToLowerInvariant();
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (tag)
                {
                    case "think": ai.ThinkCount++; break;
                    case "import ai": ai.ImportAis.Add(StripBacktick(data)); break;
                    case "return":
                        if (ai.ReturnValue == null) ai.ReturnValue = data;
                        break;
                }
            }
        }

        #endregion

        #region 辅助

        private static string StripBacktick(string s)
        {
            if (s != null && s.Length >= 2 && s[0] == '`' && s[s.Length - 1] == '`')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        #endregion
    }
}
