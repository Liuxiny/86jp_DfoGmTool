using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GmPvfLib
{
    
    
    
    
    public class AiConfigFile : PvfModelBase
    {
        #region 基本信息

        
        public string MinimumInfo { get; set; }
        
        public string AdditionalCharacterStatus { get; set; }
        
        public string CharacterStatusRate { get; set; }

        #endregion

        #region AI 行为

        public int ThinkTerm { get; set; } = -1;
        public int DestinationChangeTerm { get; set; } = -1;
        public int Warlike { get; set; } = -1;
        public int Vision { get; set; } = -1;
        public int TargetingBonus { get; set; } = -1;
        public int KeepDistanceWithTarget { get; set; } = -1;
        public int KeepRangeDistanceWithTarget { get; set; } = -1;
        public int FarAttackReactionRate { get; set; } = -1;
        public int AttackDamageRate { get; set; } = -1;

        #endregion

        #region 装备/技能

        
        public string Equipment { get; set; }
        
        public string Skill { get; set; }
        
        public string QuickSkill { get; set; }
        
        public string QuickItem { get; set; }
        
        public string KeyStream { get; set; }
        
        public string ArmorSubtype { get; set; }
        public string AddEquipmentStatusFromLevel { get; set; }

        #endregion

        #region 事件触发

        
        public List<string> OnAttacks { get; set; } = new List<string>();
        
        public List<string> OnDamages { get; set; } = new List<string>();
        
        public List<string> OnAppears { get; set; } = new List<string>();
        
        public List<string> OnDies { get; set; } = new List<string>();
        
        public List<string> OnKeys { get; set; } = new List<string>();
        
        public List<string> OnStands { get; set; } = new List<string>();

        #endregion

        #region 其他

        public int NoCooltime { get; set; } = -1;
        public int Team { get; set; } = -1;
        public int HpRate { get; set; } = -1;
        public int MpRate { get; set; } = -1;
        public int PhysicalAttackRate { get; set; } = -1;
        public int MagicalAttackRate { get; set; } = -1;
        public string AppearancePoint { get; set; }

        public List<AiDeathTowerItem> DeathTowerItems { get; set; } = new List<AiDeathTowerItem>();
        
        public List<string> PartyMembers { get; set; } = new List<string>();

        #endregion
        #region 解析

        public static AiConfigFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new AiConfigFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var aic = new AiConfigFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    
                    case "minimum info": aic.MinimumInfo = data; break;
                    case "additional character status": aic.AdditionalCharacterStatus = data; break;
                    case "character status rate": aic.CharacterStatusRate = data; break;

                    
                    case "think term": aic.ThinkTerm = ParseInt(data); break;
                    case "destination change term": aic.DestinationChangeTerm = ParseInt(data); break;
                    case "warlike": aic.Warlike = ParseInt(data); break;
                    case "vision": aic.Vision = ParseInt(data); break;
                    case "targeting bonus": aic.TargetingBonus = ParseInt(data); break;
                    case "keep distance with target": aic.KeepDistanceWithTarget = ParseInt(data); break;
                    case "keep range distance with target": aic.KeepRangeDistanceWithTarget = ParseInt(data); break;
                    case "far attack reaction rate": aic.FarAttackReactionRate = ParseInt(data); break;
                    case "attack damage rate": aic.AttackDamageRate = ParseInt(data); break;

                    
                    case "equipment": aic.Equipment = data; break;
                    case "skill": aic.Skill = data; break;
                    case "quick skill": aic.QuickSkill = data; break;
                    case "quick item": aic.QuickItem = data; break;
                    case "key stream": aic.KeyStream = data; break;
                    case "armor subtype": aic.ArmorSubtype = data; break;
                    case "add equipment status from level": aic.AddEquipmentStatusFromLevel = data; break;

                    
                    case "on attack": aic.OnAttacks.Add(data); break;
                    case "on damage": aic.OnDamages.Add(data); break;
                    case "on appear": aic.OnAppears.Add(data); break;
                    case "on die": aic.OnDies.Add(data); break;
                    case "on key": aic.OnKeys.Add(data); break;
                    case "on stand": aic.OnStands.Add(data); break;

                    
                    case "no cooltime": aic.NoCooltime = ParseInt(data); break;
                    case "team": aic.Team = ParseInt(data); break;
                    case "hp rate": aic.HpRate = ParseInt(data); break;
                    case "mp rate": aic.MpRate = ParseInt(data); break;
                    case "physical attack rate": aic.PhysicalAttackRate = ParseInt(data); break;
                    case "magical attack rate": aic.MagicalAttackRate = ParseInt(data); break;
                    case "appearance point": aic.AppearancePoint = data; break;
                    case "death tower item":
                        ParseDeathTowerItems(node, content, aic.DeathTowerItems);
                        break;
                    case "party member": aic.PartyMembers.Add(data); break;
                }
            }

            return aic;
        }

        private static void ParseDeathTowerItems(
            ScriptNode node,
            string content,
            ICollection<AiDeathTowerItem> result)
        {
            var numbers = new List<int>();
            foreach (var dataItem in node.DataItems)
            {
                var raw = dataItem.GetContent(content) ?? string.Empty;
                foreach (Match match in Regex.Matches(raw, @"-?\d+"))
                {
                    if (int.TryParse(match.Value, out var value))
                        numbers.Add(value);
                }
            }

            for (var index = 0; index + 1 < numbers.Count; index += 2)
            {
                result.Add(new AiDeathTowerItem
                {
                    ItemId = numbers[index],
                    DropRate = numbers[index + 1],
                });
            }
        }

        #endregion
    }

    public sealed class AiDeathTowerItem
    {
        public int ItemId { get; set; }
        public int DropRate { get; set; }
    }
}
