using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    public class AttackFile : PvfModelBase
    {
        #region 攻击类型

        
        public string AttackType { get; set; }
        
        public string ElementalProperty { get; set; }
        
        public string DamageReaction { get; set; }
        public int WeaponDamageApply { get; set; } = -1;
        public int AttackEnemy { get; set; } = -1;
        public int AttackFriend { get; set; } = -1;
        public int AttackMyself { get; set; } = -1;

        #endregion

        #region 击打效果

        
        public string HitInfo { get; set; }
        public string HitWav { get; set; }
        
        public string AttackDirection { get; set; }
        public int PushAside { get; set; } = -1;
        public int LiftUp { get; set; } = -1;
        public int KnuckBack { get; set; } = -1;

        #endregion

        #region 伤害

        public int DamageBonus { get; set; } = -1;
        public int AbsoluteDamage { get; set; } = -1;
        public int IgnoreDefense { get; set; } = -1;
        public int IgnoreWeight { get; set; } = -1;
        public string MonsterDamageRate { get; set; }

        #endregion

        #region 异常状态

        
        public string ActiveStatus { get; set; }
        
        public bool HasStuck { get; set; }
        
        public bool HasStun { get; set; }

        #endregion

        #region 视觉效果

        public bool NoBlood { get; set; }
        public bool HasBlood { get; set; }
        
        public string Pvp { get; set; }

        #endregion
        #region 解析

        public static AttackFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new AttackFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var atk = new AttackFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    
                    case "attack type": atk.AttackType = StripBacktick(data); break;
                    case "elemental property": atk.ElementalProperty = StripBacktick(data); break;
                    case "damage reaction": atk.DamageReaction = StripBacktick(data); break;
                    case "weapon damage apply": atk.WeaponDamageApply = ParseInt(data); break;
                    case "attack enemy": atk.AttackEnemy = ParseInt(data); break;
                    case "attack friend": atk.AttackFriend = ParseInt(data); break;
                    case "attack myself": atk.AttackMyself = ParseInt(data); break;

                    
                    case "hit info": atk.HitInfo = data; break;
                    case "hit wav": atk.HitWav = StripBacktick(data); break;
                    case "attack direction": atk.AttackDirection = StripBacktick(data); break;
                    case "push aside": atk.PushAside = ParseInt(data); break;
                    case "lift up": atk.LiftUp = ParseInt(data); break;
                    case "knuck back": atk.KnuckBack = ParseInt(data); break;

                    
                    case "damage bonus": atk.DamageBonus = ParseInt(data); break;
                    case "absolute damage": atk.AbsoluteDamage = ParseInt(data); break;
                    case "ignore defense": atk.IgnoreDefense = ParseInt(data); break;
                    case "ignore weight": atk.IgnoreWeight = ParseInt(data); break;
                    case "monster damage rate": atk.MonsterDamageRate = data; break;

                    
                    case "active status": atk.ActiveStatus = data; break;
                    case "stuck": atk.HasStuck = true; break;
                    case "stun": atk.HasStun = true; break;

                    
                    case "no blood": atk.NoBlood = true; break;
                    case "blood": atk.HasBlood = true; break;
                    case "pvp": atk.Pvp = data; break;
                }
            }

            return atk;
        }

        #endregion
    }
}
