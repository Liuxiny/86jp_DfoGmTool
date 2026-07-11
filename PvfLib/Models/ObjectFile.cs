using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    public class ObjectFile : PvfModelBase
    {
        #region 基本信息

        public string Name { get; set; }
        
        public string Layer { get; set; }
        public int LayerLevel { get; set; } = -1;
        
        public string PassType { get; set; }
        public string Type { get; set; }
        public int FloatingHeight { get; set; } = -1;
        public int Width { get; set; } = -1;
        public int PiercingPower { get; set; } = -1;

        #endregion

        #region 动作/动画

        public string BasicAction { get; set; }
        public string BasicMotion { get; set; }
        
        public string EtcAction { get; set; }
        
        public string EtcMotion { get; set; }
        public string LastAction { get; set; }

        #endregion

        #region HP/销毁

        public int HpMax { get; set; } = -1;
        public string HpDestroy { get; set; }
        public string ObjectDestroyCondition { get; set; }
        public string DestroyParticle { get; set; }
        public bool IsHpByDifficulty { get; set; }

        #endregion

        #region 战斗

        
        public string AttackInfo { get; set; }
        
        public string EtcAttackInfo { get; set; }
        
        public string MatchingAttackInfo { get; set; }
        public int Team { get; set; } = -1;

        #endregion

        #region 追踪 (homing)

        public int HomingUse { get; set; } = -1;
        public int HomingFollow { get; set; } = -1;
        public string Homing { get; set; }
        public int HomingVelocity { get; set; } = -1;
        public int HomingCheckGap { get; set; } = -1;
        public int HomingTime { get; set; } = -1;

        #endregion

        #region 其他

        public string IntData { get; set; }
        public string StringData { get; set; }
        public string SoundCategory { get; set; }
        public string Particle { get; set; }
        public int Time { get; set; } = -1;
        public string Notice { get; set; }
        public string MovieUnit { get; set; }
        public int VanishOnMoveCollision { get; set; } = -1;

        #endregion
        #region 解析

        public static ObjectFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new ObjectFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var obj = new ObjectFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    case "name": obj.Name = StripBacktick(data); break;
                    case "layer": obj.Layer = StripBacktick(data); break;
                    case "layer level": obj.LayerLevel = ParseInt(data); break;
                    case "pass type": obj.PassType = StripBacktick(data); break;
                    case "type": obj.Type = StripBacktick(data); break;
                    case "floating height": obj.FloatingHeight = ParseInt(data); break;
                    case "width": obj.Width = ParseInt(data); break;
                    case "piercing power": obj.PiercingPower = ParseInt(data); break;

                    case "basic action": obj.BasicAction = StripBacktick(data); break;
                    case "basic motion": obj.BasicMotion = StripBacktick(data); break;
                    case "etc action": obj.EtcAction = data; break;
                    case "etc motion": obj.EtcMotion = data; break;
                    case "last action": obj.LastAction = StripBacktick(data); break;

                    case "hp max": obj.HpMax = ParseInt(data); break;
                    case "hp destroy": obj.HpDestroy = data; break;
                    case "object destroy condition": obj.ObjectDestroyCondition = data; break;
                    case "destroy particle": obj.DestroyParticle = data; break;
                    case "is hp by difficulty": obj.IsHpByDifficulty = ParseInt(data) != 0; break;

                    case "attack info": obj.AttackInfo = data; break;
                    case "etc attack info": obj.EtcAttackInfo = data; break;
                    case "matching attack info": obj.MatchingAttackInfo = data; break;
                    case "team": obj.Team = ParseInt(data); break;

                    case "homing use": obj.HomingUse = ParseInt(data); break;
                    case "homing follow": obj.HomingFollow = ParseInt(data); break;
                    case "homing": obj.Homing = data; break;
                    case "homing velocity": obj.HomingVelocity = ParseInt(data); break;
                    case "homing check gap": obj.HomingCheckGap = ParseInt(data); break;
                    case "homing time": obj.HomingTime = ParseInt(data); break;

                    case "int data": obj.IntData = data; break;
                    case "string data": obj.StringData = data; break;
                    case "sound category": obj.SoundCategory = data; break;
                    case "particle": obj.Particle = data; break;
                    case "time": obj.Time = ParseInt(data); break;
                    case "notice": obj.Notice = data; break;
                    case "movie unit": obj.MovieUnit = data; break;
                    case "vanish on move collision": obj.VanishOnMoveCollision = ParseInt(data); break;
                }
            }

            return obj;
        }

        #endregion
    }
}
