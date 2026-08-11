using System;
using System.Collections.Generic;
using System.Globalization;

namespace GmPvfLib
{
    
    
    
    
    public class EquipmentFile : PvfModelBase
    {
        #region 基本信息

        public string Name { get; set; }
        public string Name2 { get; set; }
        public string Explain { get; set; }
        public string BasicExplain { get; set; }
        public string DetailExplain { get; set; }
        public string FlavorText { get; set; }
        public int Grade { get; set; } = -1;
        public int Rarity { get; set; } = -1;
        public int MinimumLevel { get; set; } = -1;
        public int MaximumLevel { get; set; } = -1;

        #endregion

        #region 装备类型

        
        public string EquipmentType { get; set; }
        public int SubType { get; set; } = -1;
        
        public string AttachType { get; set; }
        public string ItemGroupName { get; set; }

        #endregion

        #region 战斗属性

        public int PhysicalAttack { get; set; }
        public int MagicalAttack { get; set; }
        public int PhysicalDefense { get; set; }
        public int MagicalDefense { get; set; }
        public int[] EquipmentPhysicalDefense { get; set; }
        public int[] EquipmentMagicalDefense { get; set; }
        public int[] EquipmentPhysicalAttack { get; set; }
        public int[] EquipmentMagicalAttack { get; set; }
        public int HpMax { get; set; }
        public int MpMax { get; set; }
        public int AttackSpeed { get; set; }
        public int CastSpeed { get; set; }
        public int MoveSpeed { get; set; }
        public int MpRegenSpeed { get; set; }
        public int HpRegenSpeed { get; set; }
        public int PhysicalCriticalHit { get; set; }
        public int MagicalCriticalHit { get; set; }
        public int HitRecovery { get; set; }
        public int AttackSuccess { get; set; }
        public int CreatureFoodConsumeRate { get; set; }

        #endregion

        #region 经济属性

        public int Price { get; set; } = -1;
        public int RepairPrice { get; set; } = -1;
        public int AddRepairPrice { get; set; } = -1;
        public int Value { get; set; } = -1;
        // [add price] is a signed purchase-price adjustment.  Zero means the
        // tag is absent; -1 is a valid adjustment and must not mean "missing".
        public int AddPrice { get; set; }
        public int AddValue { get; set; } = -1;
        public int CreationRate { get; set; } = -1;
        public int Durability { get; set; } = -1;
        public int Weight { get; set; } = -1;
        public int CoolTime { get; set; } = -1;
        public int InventoryLimit { get; set; } = -1;
        public string NeedMaterial { get; set; }
        /// <summary>
        /// Additive reinforcement/amplification success weight on a 100000-point scale.
        /// </summary>
        public int UpgradeProbabilityIncrease { get; set; }

        #endregion

        #region 外观

        
        public string Icon { get; set; }
        public string FieldImage { get; set; }
        public string IconMark { get; set; }
        public string MoveWav { get; set; }

        #endregion

        #region 其他常用

        public int PartSetIndex { get; set; } = -1;
        public int OutputIndex { get; set; } = -1;
        public int[] ForceResultItemRule { get; set; }
        public int ClearAvatar { get; set; }
        
        public string UsableJob { get; set; }
        public string ImpossibleContents { get; set; }
        public List<string> ImpossibleContentItems { get; set; } = new List<string>();

        /// <summary>
        /// PVF [item category] 段值，用于判定克隆装扮等特殊类别。
        /// </summary>
        public string ItemCategory { get; set; }

        #endregion
        #region 解析

        public static EquipmentFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new EquipmentFile { Content = content ?? "", Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var equ = new EquipmentFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : "";
                switch (node.Tag.ToLowerInvariant())
                {
                    
                    case "name": equ.Name = StripBacktick(data); break;
                    case "name2": equ.Name2 = StripBacktick(data); break;
                    case "explain": equ.Explain = StripBacktick(data); break;
                    case "basic explain": equ.BasicExplain = StripBacktick(data); break;
                    case "detail explain": equ.DetailExplain = StripBacktick(data); break;
                    case "flavor text": equ.FlavorText = StripBacktick(data); break;
                    case "grade": equ.Grade = ParseInt(data); break;
                    case "rarity": equ.Rarity = ParseInt(data); break;
                    case "minimum level": equ.MinimumLevel = ParseInt(data); break;
                    case "maximum level": equ.MaximumLevel = ParseInt(data); break;

                    
                    case "equipment type": equ.EquipmentType = data; break;
                    case "sub type": equ.SubType = ParseInt(data); break;
                    case "attach type": equ.AttachType = StripBacktick(data); break;
                    case "item group name": equ.ItemGroupName = StripBacktick(data); break;

                    
                    case "physical attack": equ.PhysicalAttack = ParseInt(data); break;
                    case "magical attack": equ.MagicalAttack = ParseInt(data); break;
                    case "physical defense": equ.PhysicalDefense = ParseInt(data); break;
                    case "magical defense": equ.MagicalDefense = ParseInt(data); break;
                    case "equipment physical defense": equ.EquipmentPhysicalDefense = ParseIntArray(data); break;
                    case "equipment magical defense": equ.EquipmentMagicalDefense = ParseIntArray(data); break;
                    case "equipment physical attack": equ.EquipmentPhysicalAttack = ParseIntArray(data); break;
                    case "equipment magical attack": equ.EquipmentMagicalAttack = ParseIntArray(data); break;
                    case "hp max": equ.HpMax = ParseInt(data); break;
                    case "mp max": equ.MpMax = ParseInt(data); break;
                    case "attack speed": equ.AttackSpeed = ParseInt(data); break;
                    case "cast speed": equ.CastSpeed = ParseInt(data); break;
                    case "move speed": equ.MoveSpeed = ParseInt(data); break;
                    case "mp regen speed": equ.MpRegenSpeed = ParseInt(data); break;
                    case "hp regen speed": equ.HpRegenSpeed = ParseInt(data); break;
                    case "physical critical hit": equ.PhysicalCriticalHit = ParseInt(data); break;
                    case "magical critical hit": equ.MagicalCriticalHit = ParseInt(data); break;
                    case "hit recovery": equ.HitRecovery = ParseInt(data); break;
                    case "attack success": equ.AttackSuccess = ParseInt(data); break;
                    case "creature food consume rate": equ.CreatureFoodConsumeRate = ParseInt(data); break;

                    
                    case "price": equ.Price = ParseInt(data); break;
                    case "repair price": equ.RepairPrice = ParseInt(data); break;
                    case "add repair price": equ.AddRepairPrice = ParseInt(data); break;
                    case "value": equ.Value = ParseInt(data); break;
                    case "add price": equ.AddPrice = ParseInt(data); break;
                    case "add value": equ.AddValue = ParseInt(data); break;
                    case "creation rate": equ.CreationRate = ParseInt(data); break;
                    case "durability": equ.Durability = ParseInt(data); break;
                    case "weight": equ.Weight = ParseInt(data); break;
                    case "cool time": equ.CoolTime = ParseInt(data); break;
                    case "inventory limit": equ.InventoryLimit = ParseInt(data); break;
                    case "need material": equ.NeedMaterial = data; break;
                    case "upgrade prob increase": equ.UpgradeProbabilityIncrease = ParseInt(data); break;

                    
                    case "icon": equ.Icon = data; break;
                    case "field image": equ.FieldImage = data; break;
                    case "icon mark": equ.IconMark = data; break;
                    case "move wav": equ.MoveWav = StripBacktick(data); break;

                    
                    case "part set index": equ.PartSetIndex = ParseInt(data); break;
                    case "output index": equ.OutputIndex = ParseInt(data); break;
                    case "force result item rule": equ.ForceResultItemRule = ParseIntArray(data); break;
                    case "clear avatar": equ.ClearAvatar = ParseInt(data); break;
                    case "usable job": equ.UsableJob = StripBacktick(data); break;
                    case "impossible contents":
                        equ.ImpossibleContents = data;
                        equ.ImpossibleContentItems = ParseStringList(node, content);
                        break;
                    case "item category": equ.ItemCategory = StripBacktick(data); break;
                }
            }

            return equ;
        }

        private static List<string> ParseStringList(ScriptNode node, string content)
        {
            var result = new List<string>();
            if (node == null || node.DataItems == null)
                return result;

            foreach (var item in node.DataItems)
            {
                var raw = item.GetContent(content).Trim();
                var value = StripBacktick(raw);
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value.Trim());
            }

            return result;
        }

        #endregion
    }
}
