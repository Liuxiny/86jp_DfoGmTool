using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    public class SkillFile : PvfModelBase
    {
        #region 基本信息

        public string Name { get; set; }
        public string Explain { get; set; }
        public string BasicExplain { get; set; }
        public string ExplainEx { get; set; }
        public string BasicExplainEx { get; set; }
        
        public string Type { get; set; }
        public int SkillClass { get; set; } = -1;
        public string Icon { get; set; }
        public int MaximumLevel { get; set; } = -1;
        
        public string GrowtypeMaximumLevel { get; set; }

        #endregion

        #region 等级/需求

        public int RequiredLevel { get; set; } = -1;
        public string RequiredLevelRange { get; set; }
        public string PreRequiredSkill { get; set; }
        public string PurchaseCost { get; set; }
        public string SpecialPurchaseCost { get; set; }
        public string SpecialLevelUp { get; set; }

        #endregion

        #region 消耗

        public string ConsumeMp { get; set; }
        public string ConsumeItem { get; set; }
        public int CoolTime { get; set; } = -1;
        public int StartCoolTime { get; set; } = -1;
        public string CastingTime { get; set; }

        #endregion

        #region 操作

        public string Command { get; set; }
        public string CommandKeyExplain { get; set; }
        public string CommandCustomizing { get; set; }
        public string SkillCommandAdvantage { get; set; }

        #endregion

        #region 等级数据

        
        public List<string> LevelInfos { get; set; } = new List<string>();
        
        public List<string> StaticDatas { get; set; } = new List<string>();
        
        public List<string> LevelProperties { get; set; } = new List<string>();

        #endregion

        #region 适用类型

        public string SkillFitnessGrowtype { get; set; }
        public string SkillFitnessSecondGrowtype { get; set; }
        public string SecondGrowtypeMaximumLevel { get; set; }

        #endregion

        #region 固定等级技能(觉醒等自动按角色等级升级的技能)

        public bool IsFixedLevelSkill { get; set; }
        public int FixedLevelBase { get; set; }
        public int FixedLevelInterval { get; set; } = 1;
        public int FixedLevelAddPerInterval { get; set; } = 1;
        public string FeatureSkillType { get; set; }
        public int FeatureSkillIndex { get; set; } = -1;

        #endregion

        #region 场景限制

        
        public string Pvp { get; set; }
        
        public string Dungeon { get; set; }
        
        public string DeathTower { get; set; }
        public string Warroom { get; set; }

        #endregion

        #region 其他

        public string ExecutableStates { get; set; }
        public string ExecutableSkills { get; set; }
        public int DurabilityDecreaseRate { get; set; } = -1;
        public string WeaponEffectType { get; set; }
        public string SkillPreloadingImage { get; set; }
        public string ShakeScreen { get; set; }
        public int AutoCooltimeApply { get; set; } = -1;
        public string SkillStyleText { get; set; }

        #endregion
        #region 解析

        public static SkillFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new SkillFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var skl = new SkillFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    
                    case "name": skl.Name = StripBacktick(data); break;
                    case "explain": skl.Explain = StripBacktick(data); break;
                    case "basic explain": skl.BasicExplain = StripBacktick(data); break;
                    case "explain ex": skl.ExplainEx = StripBacktick(data); break;
                    case "basic explain ex": skl.BasicExplainEx = StripBacktick(data); break;
                    case "type": skl.Type = StripBacktick(data); break;
                    case "skill class": skl.SkillClass = ParseInt(data); break;
                    case "icon": skl.Icon = data; break;
                    case "maximum level": skl.MaximumLevel = ParseInt(data); break;
                    case "growtype maximum level": skl.GrowtypeMaximumLevel = data; break;

                    
                    case "required level": skl.RequiredLevel = ParseInt(data); break;
                    case "required level range": skl.RequiredLevelRange = data; break;
                    case "pre required skill": skl.PreRequiredSkill = data; break;
                    case "purchase cost": skl.PurchaseCost = data; break;
                    case "special purchase cost": skl.SpecialPurchaseCost = data; break;
                    case "special level up": skl.SpecialLevelUp = data; break;

                    
                    case "consume mp": skl.ConsumeMp = data; break;
                    case "consume item": skl.ConsumeItem = data; break;
                    case "cool time": skl.CoolTime = ParseInt(data); break;
                    case "start cool time": skl.StartCoolTime = ParseInt(data); break;
                    case "casting time": skl.CastingTime = data; break;

                    
                    case "command": skl.Command = data; break;
                    case "command key explain": skl.CommandKeyExplain = StripBacktick(data); break;
                    case "command customizing": skl.CommandCustomizing = data; break;
                    case "skill command advantage": skl.SkillCommandAdvantage = data; break;

                    
                    case "level info": skl.LevelInfos.Add(data); break;
                    case "static data": skl.StaticDatas.Add(data); break;
                    case "level property": skl.LevelProperties.Add(data); break;

                    
                    case "skill fitness growtype": skl.SkillFitnessGrowtype = data; break;
                    case "skill fitness second growtype": skl.SkillFitnessSecondGrowtype = data; break;
                    case "second growtype maximum level": skl.SecondGrowtypeMaximumLevel = data; break;
                    case "feature skill type": skl.FeatureSkillType = data; break;
                    case "feature skill index": skl.FeatureSkillIndex = ParseInt(data); break;

                    case "fixed level skill":
                        skl.IsFixedLevelSkill = true;
                        skl.FixedLevelBase = ParseInt(data);
                        foreach (var child in node.Children)
                        {
                            var childData = child.DataItems.Count > 0 ? child.GetFirstDataContent(content).Trim() : "";
                            switch (child.Tag.ToLowerInvariant())
                            {
                                case "interval level":
                                    skl.FixedLevelInterval = Math.Max(1, ParseInt(childData));
                                    break;
                                case "add level per interval":
                                    skl.FixedLevelAddPerInterval = Math.Max(1, ParseInt(childData));
                                    break;
                            }
                        }
                        break;
                    case "interval level": skl.FixedLevelInterval = Math.Max(1, ParseInt(data)); break;
                    case "add level per interval": skl.FixedLevelAddPerInterval = Math.Max(1, ParseInt(data)); break;

                    
                    case "pvp": skl.Pvp = data; break;
                    case "dungeon": skl.Dungeon = data; break;
                    case "death tower": skl.DeathTower = data; break;
                    case "warroom": skl.Warroom = data; break;

                    
                    case "executable states": skl.ExecutableStates = data; break;
                    case "executable skills": skl.ExecutableSkills = data; break;
                    case "durability decrease rate": skl.DurabilityDecreaseRate = ParseInt(data); break;
                    case "weapon effect type": skl.WeaponEffectType = data; break;
                    case "skill preloading image": skl.SkillPreloadingImage = data; break;
                    case "shake screen": skl.ShakeScreen = data; break;
                    case "auto cooltime apply": skl.AutoCooltimeApply = ParseInt(data); break;
                    case "skillstyletext": skl.SkillStyleText = data; break;
                }
            }

            return skl;
        }

        #endregion
    }
}
