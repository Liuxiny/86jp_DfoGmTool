using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    
    public class ConfigFile : PvfModelBase
    {
        #region 基本信息

        public string Name { get; set; }
        public string Job { get; set; }
        public string Type { get; set; }
        public string CharacterJob { get; set; }
        public string Growtype { get; set; }

        #endregion

        #region 技能/零件

        
        public int SkillInfoCount { get; set; }
        
        public int IndexCount { get; set; }
        
        public int PartSetInfoCount { get; set; }
        
        public int IndexListCount { get; set; }

        #endregion

        #region 事件/条件

        public int EventCount { get; set; }
        public int TriggerCount { get; set; }
        public int ConditionCount { get; set; }
        public int QuestCount { get; set; }

        #endregion
        #region 解析

        public static ConfigFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new ConfigFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var co = new ConfigFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    case "name": co.Name = StripBacktick(data); break;
                    case "job": co.Job = StripBacktick(data); break;
                    case "type": co.Type = StripBacktick(data); break;
                    case "character job": co.CharacterJob = StripBacktick(data); break;
                    case "growtype": co.Growtype = data; break;

                    case "skill info": co.SkillInfoCount++; break;
                    case "index": co.IndexCount++; break;
                    case "part set info": co.PartSetInfoCount++; break;
                    case "index list": co.IndexListCount++; break;

                    case "event": co.EventCount++; break;
                    case "trigger": co.TriggerCount++; break;
                    case "condition": co.ConditionCount++; break;
                    case "quest": co.QuestCount++; break;
                }
            }

            return co;
        }

        #endregion
    }
}
