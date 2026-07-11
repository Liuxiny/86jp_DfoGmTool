using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    public class NpcFile : PvfModelBase
    {
        #region 基本信息

        
        public List<string> Names { get; set; } = new List<string>();
        
        public string Name => Names.Count > 0 ? Names[0] : null;
        public string FieldName { get; set; }
        
        public string Role { get; set; }

        #endregion

        #region 外观/动画

        public string FieldAnimation { get; set; }
        public int LookAround { get; set; } = -1;
        
        public string PopupFace { get; set; }
        public string SmallFace { get; set; }
        public string BigFace { get; set; }
        public string Img { get; set; }
        public string ClickArea { get; set; }

        #endregion

        #region 声音

        public string FieldWav { get; set; }
        public string PopupWav { get; set; }

        #endregion

        #region 对话

        
        public List<string> Dialogs { get; set; } = new List<string>();
        
        public string BalloonMessage { get; set; }
        public string QuestSpeech { get; set; }

        #endregion

        #region 好感度系统

        
        public List<string> Favors { get; set; } = new List<string>();
        
        public List<string> FavorDialogs { get; set; } = new List<string>();
        
        public List<string> Moods { get; set; } = new List<string>();
        
        public List<string> FavorLevels { get; set; } = new List<string>();
        
        public List<string> ItemInfos { get; set; } = new List<string>();
        public string PreferItem { get; set; }
        public string PreferItemGroup { get; set; }
        public string UnpreferItem { get; set; }
        public string UnpreferItemGroup { get; set; }
        public int DefaultFavor { get; set; } = -1;
        public int MaxGiftPerDay { get; set; } = -1;

        #endregion

        #region 其他

        public string FieldRole { get; set; }
        public string Skill { get; set; }
        public string EquipmentList { get; set; }
        public string IntData { get; set; }
        public string StringData { get; set; }

        #endregion
        #region 解析

        public static NpcFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new NpcFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var npc = new NpcFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    
                    case "name": npc.Names.Add(StripBacktick(data)); break;
                    case "field name": npc.FieldName = StripBacktick(data); break;
                    case "role": npc.Role = data; break;

                    
                    case "field animation": npc.FieldAnimation = StripBacktick(data); break;
                    case "look around": npc.LookAround = ParseInt(data); break;
                    case "popup face": npc.PopupFace = data; break;
                    case "small face": npc.SmallFace = data; break;
                    case "big face": npc.BigFace = data; break;
                    case "img": npc.Img = data; break;
                    case "click area": npc.ClickArea = data; break;

                    
                    case "field wav": npc.FieldWav = StripBacktick(data); break;
                    case "popup wav": npc.PopupWav = data; break;

                    
                    case "dialog": npc.Dialogs.Add(data); break;
                    case "balloon message": npc.BalloonMessage = data; break;
                    case "quest speech": npc.QuestSpeech = data; break;

                    
                    case "favor": npc.Favors.Add(data); break;
                    case "favor_dialog": npc.FavorDialogs.Add(data); break;
                    case "mood": npc.Moods.Add(data); break;
                    case "favor level": npc.FavorLevels.Add(data); break;
                    case "item info": npc.ItemInfos.Add(data); break;
                    case "prefer item": npc.PreferItem = data; break;
                    case "prefer item group": npc.PreferItemGroup = data; break;
                    case "unprefer item": npc.UnpreferItem = data; break;
                    case "unprefer item group": npc.UnpreferItemGroup = data; break;
                    case "default favor": npc.DefaultFavor = ParseInt(data); break;
                    case "max gift per day": npc.MaxGiftPerDay = ParseInt(data); break;

                    
                    case "field role": npc.FieldRole = data; break;
                    case "skill": npc.Skill = data; break;
                    case "equipment list": npc.EquipmentList = data; break;
                    case "int data": npc.IntData = data; break;
                    case "string data": npc.StringData = data; break;
                }
            }

            return npc;
        }

        #endregion
    }
}
