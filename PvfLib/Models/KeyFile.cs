using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    public class KeyFile : PvfModelBase
    {
        #region 输入

        
        public List<string> Inputs { get; set; } = new List<string>();
        
        public List<string> StateChecks { get; set; } = new List<string>();
        
        public List<string> StateCheck2s { get; set; } = new List<string>();
        
        public List<string> DeleteInputs { get; set; } = new List<string>();

        #endregion

        #region 引用

        
        public List<string> OrImportKeys { get; set; } = new List<string>();
        
        public List<string> ImportKeys { get; set; } = new List<string>();

        #endregion

        #region 其他

        public int FixDirection { get; set; } = -1;
        public int Neutral { get; set; } = -1;
        public int UseWhenFree { get; set; } = -1;
        public int UseMyDirection { get; set; } = -1;
        public string ExcuteAction { get; set; }
        public string Percent { get; set; }

        #endregion
        #region 解析

        public static KeyFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new KeyFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var key = new KeyFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    
                    case "input": key.Inputs.Add(data); break;
                    case "state check": key.StateChecks.Add(data); break;
                    case "state check2": key.StateCheck2s.Add(data); break;
                    case "delete input": key.DeleteInputs.Add(data); break;

                    
                    case "or import key": key.OrImportKeys.Add(StripBacktick(data)); break;
                    case "import key": key.ImportKeys.Add(StripBacktick(data)); break;

                    
                    case "fix direction": key.FixDirection = ParseInt(data); break;
                    case "neutral": key.Neutral = ParseInt(data); break;
                    case "use when free": key.UseWhenFree = ParseInt(data); break;
                    case "use my direction": key.UseMyDirection = ParseInt(data); break;
                    case "excute action": key.ExcuteAction = StripBacktick(data); break;
                    case "percent": key.Percent = data; break;
                }
            }

            return key;
        }

        #endregion
    }
}
