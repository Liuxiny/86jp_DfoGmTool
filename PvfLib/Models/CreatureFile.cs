using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    public class CreatureFile : PvfModelBase
    {
        #region 基本信息

        public string Name { get; set; }
        public string Layer { get; set; }
        public int[] Width { get; set; }
        public int FloatingHeight { get; set; } = -1;
        public int CharacterSpan { get; set; } = -1;
        public int DirectionCorrect { get; set; } = -1;
        public int Gravity { get; set; } = -1;
        public int StartLevel { get; set; } = -1;
        public int PermissionLevel { get; set; } = -1;
        public int MaxLevel { get; set; } = -1;
        public int ParentMaxLevel { get; set; } = -1;

        #endregion

        #region 移动

        
        public string MoveSpeed { get; set; }

        #endregion

        #region 动作

        public string BasicMotion { get; set; }
        public string ResponseMotion { get; set; }
        public string WalkMotion { get; set; }
        public string RunMotion { get; set; }

        #endregion

        #region 技能

        
        public List<string> SkillInfos { get; set; } = new List<string>();
        
        public List<string> SkillRecoveryTimes { get; set; } = new List<string>();
        
        public List<string> SkillMotions { get; set; } = new List<string>();
        
        public List<string> SkillMps { get; set; } = new List<string>();
        
        public List<string> SkillTypes { get; set; } = new List<string>();
        
        public List<string> AttackInfos { get; set; } = new List<string>();
        
        public List<string> SkillNames { get; set; } = new List<string>();
        
        public List<string> SkillExplains { get; set; } = new List<string>();

        #endregion

        #region 进化

        public string EvolutionQuest { get; set; }
        public string EvolutionCreatureId { get; set; }
        public int EvolutionLevel { get; set; } = -1;

        #endregion

        #region 外观

        public string ArtifactSlot { get; set; }
        public int RevisionX { get; set; } = -1;
        public int RevisionY { get; set; } = -1;
        public int DefaultRandomMotionRate { get; set; } = -1;
        public int LearnOverskillLevel { get; set; } = -1;
        public int Piercing { get; set; } = -1;

        #endregion
        #region 解析

        public static CreatureFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new CreatureFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var cre = new CreatureFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    
                    case "name": cre.Name = StripBacktick(data); break;
                    case "layer": cre.Layer = StripBacktick(data); break;
                    case "width": cre.Width = ParseIntPair(data); break;
                    case "floating height": cre.FloatingHeight = ParseInt(data); break;
                    case "character span": cre.CharacterSpan = ParseInt(data); break;
                    case "direction correct": cre.DirectionCorrect = ParseInt(data); break;
                    case "gravity": cre.Gravity = ParseInt(data); break;
                    case "start level": cre.StartLevel = ParseInt(data); break;
                    case "permission level": cre.PermissionLevel = ParseInt(data); break;
                    case "max level": cre.MaxLevel = ParseInt(data); break;
                    case "parent max level": cre.ParentMaxLevel = ParseInt(data); break;

                    
                    case "move speed": cre.MoveSpeed = data; break;

                    
                    case "basic motion": cre.BasicMotion = StripBacktick(data); break;
                    case "response motion": cre.ResponseMotion = StripBacktick(data); break;
                    case "walk motion": cre.WalkMotion = StripBacktick(data); break;
                    case "run motion": cre.RunMotion = StripBacktick(data); break;

                    
                    case "skill info": cre.SkillInfos.Add(data); break;
                    case "skill recovery time": cre.SkillRecoveryTimes.Add(data); break;
                    case "skill motion": cre.SkillMotions.Add(data); break;
                    case "skill mp": cre.SkillMps.Add(data); break;
                    case "skill type": cre.SkillTypes.Add(data); break;
                    case "attack info": cre.AttackInfos.Add(data); break;
                    case "skill name": cre.SkillNames.Add(StripBacktick(data)); break;
                    case "skill explain": cre.SkillExplains.Add(StripBacktick(data)); break;

                    
                    case "evolution quest": cre.EvolutionQuest = StripBacktick(data); break;
                    case "evolution creature id": cre.EvolutionCreatureId = StripBacktick(data); break;
                    case "evolution level": cre.EvolutionLevel = ParseInt(data); break;

                    
                    case "artifact slot": cre.ArtifactSlot = data; break;
                    case "revision x": cre.RevisionX = ParseInt(data); break;
                    case "revision y": cre.RevisionY = ParseInt(data); break;
                    case "default random motion rate": cre.DefaultRandomMotionRate = ParseInt(data); break;
                    case "learn overskill level": cre.LearnOverskillLevel = ParseInt(data); break;
                    case "piercing": cre.Piercing = ParseInt(data); break;
                }
            }

            return cre;
        }

        #endregion
    }
}
