using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    public class MonsterFile : PvfModelBase
    {
        #region 基本信息

        public string Name { get; set; }
        public string FaceImage { get; set; }
        public string Category { get; set; }
        
        public string AbilityCategory { get; set; }
        
        public int[] Level { get; set; }

        #endregion

        #region 属性

        public int[] MoveSpeed { get; set; }
        public int[] AttackSpeed { get; set; }
        public int[] CastSpeed { get; set; }
        public int[] HitRecovery { get; set; }
        public int[] Weight { get; set; }
        public int Intelligence { get; set; } = -1;
        public int FloatingHeight { get; set; } = -1;
        public int Width { get; set; } = -1;

        #endregion

        #region 抗性 (min, max)

        public int[] FireResistance { get; set; }
        public int[] WaterResistance { get; set; }
        public int[] LightResistance { get; set; }
        public int[] DarkResistance { get; set; }
        public int[] LightningResistance { get; set; }
        public int[] StunResistance { get; set; }
        public int[] CurseResistance { get; set; }
        public int[] BlindResistance { get; set; }
        public int[] PoisonResistance { get; set; }
        public int[] FreezeResistance { get; set; }
        public int[] HoldResistance { get; set; }
        public int[] SleepResistance { get; set; }
        public int[] ConfuseResistance { get; set; }
        public int[] StoneResistance { get; set; }
        public int[] SlowResistance { get; set; }
        public int[] BleedingResistance { get; set; }

        #endregion

        #region 动作

        public string WaitingAction { get; set; }
        public string MoveAction { get; set; }
        public string SitAction { get; set; }
        public string DamageAction1 { get; set; }
        public string DamageAction2 { get; set; }
        public string DownAction { get; set; }
        public string OverturnAction { get; set; }
        
        public string AttackAction { get; set; }

        #endregion

        #region AI / 行为

        public string AiPattern { get; set; }
        public int Warlike { get; set; } = -1;
        public int Cooltime { get; set; } = -1;
        public int Vision { get; set; } = -1;
        public int ThinkTerm { get; set; } = -1;
        public int DestinationChangeTerm { get; set; } = -1;
        public int KeepDistanceWithTarget { get; set; } = -1;
        public int KeepRangeDistanceWithTarget { get; set; } = -1;
        public int LieFrame { get; set; } = -1;
        public string StuckbonusOnDamage { get; set; }

        #endregion

        #region 声音

        public string AmbientSound { get; set; }
        public string AttackSound { get; set; }
        public string AppearSound { get; set; }
        public string DamageSound { get; set; }
        public string DieSound { get; set; }

        #endregion

        #region 等级分类

        public string Normal { get; set; }
        public string Master { get; set; }
        public string Expert { get; set; }
        public string King { get; set; }
        public string Slayer { get; set; }

        #endregion

        #region 其他

        public string DieEffect { get; set; }
        
        public string AttackInfo { get; set; }
        
        public string Item { get; set; }

        #endregion
        #region 解析

        public static MonsterFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new MonsterFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var mob = new MonsterFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    
                    case "name": mob.Name = StripBacktick(data); break;
                    case "face image": mob.FaceImage = data; break;
                    case "category": mob.Category = StripBacktick(data); break;
                    case "ability category": mob.AbilityCategory = data; break;
                    case "level": mob.Level = ParseIntPair(data); break;

                    
                    case "move speed": mob.MoveSpeed = ParseIntPair(data); break;
                    case "attack speed": mob.AttackSpeed = ParseIntPair(data); break;
                    case "cast speed": mob.CastSpeed = ParseIntPair(data); break;
                    case "hit recovery": mob.HitRecovery = ParseIntPair(data); break;
                    case "weight": mob.Weight = ParseIntPair(data); break;
                    case "intelligence": mob.Intelligence = ParseInt(data); break;
                    case "floating height": mob.FloatingHeight = ParseInt(data); break;
                    case "width": mob.Width = ParseInt(data); break;

                    
                    case "fire resistance": mob.FireResistance = ParseIntPair(data); break;
                    case "water resistance": mob.WaterResistance = ParseIntPair(data); break;
                    case "light resistance": mob.LightResistance = ParseIntPair(data); break;
                    case "dark resistance": mob.DarkResistance = ParseIntPair(data); break;
                    case "lightning resistance": mob.LightningResistance = ParseIntPair(data); break;
                    case "stun resistance": mob.StunResistance = ParseIntPair(data); break;
                    case "curse resistance": mob.CurseResistance = ParseIntPair(data); break;
                    case "blind resistance": mob.BlindResistance = ParseIntPair(data); break;
                    case "poison resistance": mob.PoisonResistance = ParseIntPair(data); break;
                    case "freeze resistance": mob.FreezeResistance = ParseIntPair(data); break;
                    case "hold resistance": mob.HoldResistance = ParseIntPair(data); break;
                    case "sleep resistance": mob.SleepResistance = ParseIntPair(data); break;
                    case "confuse resistance": mob.ConfuseResistance = ParseIntPair(data); break;
                    case "stone resistance": mob.StoneResistance = ParseIntPair(data); break;
                    case "slow resistance": mob.SlowResistance = ParseIntPair(data); break;
                    case "bleeding resistance": mob.BleedingResistance = ParseIntPair(data); break;

                    
                    case "waiting action": mob.WaitingAction = StripBacktick(data); break;
                    case "move action": mob.MoveAction = StripBacktick(data); break;
                    case "sit action": mob.SitAction = StripBacktick(data); break;
                    case "damage action 1": mob.DamageAction1 = StripBacktick(data); break;
                    case "damage action 2": mob.DamageAction2 = StripBacktick(data); break;
                    case "down action": mob.DownAction = StripBacktick(data); break;
                    case "overturn action": mob.OverturnAction = StripBacktick(data); break;
                    case "attack action": mob.AttackAction = data; break;

                    
                    case "ai pattern": mob.AiPattern = StripBacktick(data); break;
                    case "warlike": mob.Warlike = ParseInt(data); break;
                    case "cooltime": mob.Cooltime = ParseInt(data); break;
                    case "vision": mob.Vision = ParseInt(data); break;
                    case "think term": mob.ThinkTerm = ParseInt(data); break;
                    case "destination change term": mob.DestinationChangeTerm = ParseInt(data); break;
                    case "keep distance with target": mob.KeepDistanceWithTarget = ParseInt(data); break;
                    case "keep range distance with target": mob.KeepRangeDistanceWithTarget = ParseInt(data); break;
                    case "lie frame": mob.LieFrame = ParseInt(data); break;
                    case "stuckbonus on damage": mob.StuckbonusOnDamage = data; break;

                    
                    case "ambient sound": mob.AmbientSound = StripBacktick(data); break;
                    case "attack sound": mob.AttackSound = StripBacktick(data); break;
                    case "appear sound": mob.AppearSound = StripBacktick(data); break;
                    case "damage sound": mob.DamageSound = StripBacktick(data); break;
                    case "die sound": mob.DieSound = StripBacktick(data); break;

                    
                    case "normal": mob.Normal = data; break;
                    case "master": mob.Master = data; break;
                    case "expert": mob.Expert = data; break;
                    case "king": mob.King = data; break;
                    case "slayer": mob.Slayer = data; break;

                    
                    case "die effect": mob.DieEffect = data; break;
                    case "attack info": mob.AttackInfo = data; break;
                    case "item": mob.Item = data; break;
                }
            }

            return mob;
        }

        #endregion
    }
}
