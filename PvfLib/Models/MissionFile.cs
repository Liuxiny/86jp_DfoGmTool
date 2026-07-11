using System;

namespace GmPvfLib
{
    
    
    
    
    public class MissionFile : PvfModelBase
    {
        #region 基本信息

        
        public string Grade { get; set; }
        
        public string RankRange { get; set; }
        
        public string NameText { get; set; }
        
        public string CondText { get; set; }
        
        public string Condition { get; set; }
        
        public int ExpRatio { get; set; } = -1;

        #endregion

        #region 奖励

        
        public string RewardVictoryEmblem { get; set; }
        
        public string RewardItem { get; set; }

        #endregion
        #region 解析

        public static MissionFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new MissionFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var msn = new MissionFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    case "grade": msn.Grade = data; break;
                    case "rank range": msn.RankRange = data; break;
                    case "name_text": msn.NameText = StripBacktick(data); break;
                    case "cond_text": msn.CondText = StripBacktick(data); break;
                    case "condition": msn.Condition = data; break;
                    case "exp_ratio": msn.ExpRatio = ParseInt(data); break;
                    case "reward victory emblem": msn.RewardVictoryEmblem = data; break;
                    case "reward item": msn.RewardItem = data; break;
                }
            }

            return msn;
        }

        #endregion
    }
}
