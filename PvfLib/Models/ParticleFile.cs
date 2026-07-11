using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    public class ParticleFile : PvfModelBase
    {
        #region 运动

        
        public string MoveType { get; set; }
        
        public string MoveVariable1 { get; set; }
        
        public string MoveVariable2 { get; set; }
        
        public string LandingType { get; set; }
        public int Gravity { get; set; } = -1;

        #endregion

        #region 轴向

        public string XAxis { get; set; }
        public string YAxis { get; set; }
        public string ZAxis { get; set; }

        #endregion

        #region 生命周期/创建

        public int LifeTime { get; set; } = -1;
        public int MaximumCreateNumber { get; set; } = -1;
        public int MaximumCreateTime { get; set; } = -1;
        public int CreationFrequency { get; set; } = -1;
        public string CreationPos { get; set; }
        public string CreateAreaSize { get; set; }
        public string CreatePosCorrect { get; set; }
        public string CreateAreaRotationAngle { get; set; }
        public string HowToCreateObject { get; set; }

        #endregion

        #region 旋转/变换

        public string RotationAngle { get; set; }
        public string RotationAngleVariable { get; set; }
        public string RotationAngleRangeType { get; set; }
        public string RotationRandomAngle { get; set; }
        public string ChangingType { get; set; }
        public int ChangingTerm { get; set; } = -1;
        public int KeepChanging { get; set; } = -1;
        public string TransparentValue { get; set; }
        public string ExpansionRate { get; set; }

        #endregion

        #region 显示

        public string ObjectType { get; set; }
        
        public List<string> Objects { get; set; } = new List<string>();
        public int DisplayEffectRate { get; set; } = -1;
        public int ImageRandomRotate { get; set; } = -1;
        public int ImageRandomScale { get; set; } = -1;
        public string Layer { get; set; }
        public string RandomRange { get; set; }
        public string Rectangle { get; set; }

        #endregion

        #region 声音

        public string StartSound { get; set; }

        #endregion
        #region 解析

        public static ParticleFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new ParticleFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var ptl = new ParticleFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    
                    case "move type": ptl.MoveType = StripBacktick(data); break;
                    case "move variable 1": ptl.MoveVariable1 = data; break;
                    case "move variable 2": ptl.MoveVariable2 = data; break;
                    case "landing type": ptl.LandingType = StripBacktick(data); break;
                    case "gravity": ptl.Gravity = ParseInt(data); break;

                    
                    case "x axis": ptl.XAxis = data; break;
                    case "y axis": ptl.YAxis = data; break;
                    case "z axis": ptl.ZAxis = data; break;

                    
                    case "life time": ptl.LifeTime = ParseInt(data); break;
                    case "maximum create number": ptl.MaximumCreateNumber = ParseInt(data); break;
                    case "maximum create time": ptl.MaximumCreateTime = ParseInt(data); break;
                    case "creation frequency": ptl.CreationFrequency = ParseInt(data); break;
                    case "creation pos": ptl.CreationPos = data; break;
                    case "create area size": ptl.CreateAreaSize = data; break;
                    case "create pos correct": ptl.CreatePosCorrect = data; break;
                    case "create area rotation angle": ptl.CreateAreaRotationAngle = data; break;
                    case "how to create object": ptl.HowToCreateObject = data; break;

                    
                    case "rotation angle": ptl.RotationAngle = data; break;
                    case "rotation angle variable": ptl.RotationAngleVariable = data; break;
                    case "rotation angle range type": ptl.RotationAngleRangeType = data; break;
                    case "rotation random angle": ptl.RotationRandomAngle = data; break;
                    case "changing type": ptl.ChangingType = data; break;
                    case "changing term": ptl.ChangingTerm = ParseInt(data); break;
                    case "keep changing": ptl.KeepChanging = ParseInt(data); break;
                    case "transparent value": ptl.TransparentValue = data; break;
                    case "expansion rate": ptl.ExpansionRate = data; break;

                    
                    case "object type": ptl.ObjectType = StripBacktick(data); break;
                    case "object": ptl.Objects.Add(data); break;
                    case "display effect rate": ptl.DisplayEffectRate = ParseInt(data); break;
                    case "image random rotate": ptl.ImageRandomRotate = ParseInt(data); break;
                    case "image random scale": ptl.ImageRandomScale = ParseInt(data); break;
                    case "layer": ptl.Layer = StripBacktick(data); break;
                    case "random range": ptl.RandomRange = data; break;
                    case "rectangle": ptl.Rectangle = data; break;

                    
                    case "start sound": ptl.StartSound = StripBacktick(data); break;
                }
            }

            return ptl;
        }

        #endregion
    }
}
