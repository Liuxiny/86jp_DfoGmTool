using DfoGmTool.ServerCore.GameWorld;
using DfoGmTool.ServerCore.Network;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DfoGmTool.ServerCore.Game.Characters
{
    /// <summary>
    ///
    ///
    /// </summary>
    public static class CharacterStatComputer
    {
        private sealed class StatVector
        {
            public int HpMax, MpMax, PhysAtk, PhysDef, MagAtk, MagDef;
            public int FireRes, WaterRes, DarkRes, LightRes;
            public int InventoryLimit, HpRegen, MpRegen, MoveSpeed;
            public int AttackSpeed, CastSpeed, HitRecovery, JumpPower, Weight;

            public void Add(StatVector o)
            {
                HpMax += o.HpMax; MpMax += o.MpMax;
                PhysAtk += o.PhysAtk; PhysDef += o.PhysDef; MagAtk += o.MagAtk; MagDef += o.MagDef;
                FireRes += o.FireRes; WaterRes += o.WaterRes; DarkRes += o.DarkRes; LightRes += o.LightRes;
                InventoryLimit += o.InventoryLimit; HpRegen += o.HpRegen; MpRegen += o.MpRegen;
                MoveSpeed += o.MoveSpeed; AttackSpeed += o.AttackSpeed; CastSpeed += o.CastSpeed;
                HitRecovery += o.HitRecovery; JumpPower += o.JumpPower; Weight += o.Weight;
            }

            public StatVector Clone()
            {
                return (StatVector)MemberwiseClone();
            }
        }

        private sealed class JobStatTables
        {
            public StatVector Base;
            public StatVector[] Growtype = new StatVector[7];
            public StatVector[,] Awakening = new StatVector[7, 3];  // [growtype N, second 1..2]
        }

        private static readonly Dictionary<byte, JobStatTables> _cache = new Dictionary<byte, JobStatTables>();
        private static readonly object _lock = new object();

        internal static void ResetForPvfChange()
        {
            lock (_lock)
                _cache.Clear();
        }

        private static JobStatTables BuildFallback()
        {
            return new JobStatTables
            {
                Base = new StatVector
                {
                    HpMax = 1800, MpMax = 1400, PhysAtk = 75, PhysDef = 75, MagAtk = 45, MagDef = 45,
                    InventoryLimit = 480000, MpRegen = 500, MoveSpeed = 8500,
                    AttackSpeed = 8500, CastSpeed = 7000, HitRecovery = 6000, JumpPower = 4300, Weight = 500000,
                },
            };
        }

        /// <summary>
        /// </summary>
        public static void DecodeGrowType(byte growType, out int first, out int second)
        {
            first = growType & 0xF;
            second = (growType >> 4) & 0xF;
        }

        /// <summary>
        /// </summary>
        private const int PremiumHpBonus = 9800;
        private const int PremiumMpBonus = 10500;

        public static byte[] BuildAdditionalInfo(byte job, byte level, int firstGrow = 0, int secondGrow = 0)
        {
            var v = ComputeStat(job, level, firstGrow, secondGrow);
            v.HpMax += PremiumHpBonus;
            v.MpMax += PremiumMpBonus;
            var w = new GamePacketWriter();
            w.WriteUInt32((uint)v.HpMax);            // +0
            w.WriteUInt32((uint)v.MpMax);            // +4
            w.WriteInt16((short)v.PhysAtk);          // +8
            w.WriteInt16((short)v.PhysDef);          // +10
            w.WriteInt16((short)v.MagAtk);           // +12
            w.WriteInt16((short)v.MagDef);           // +14
            w.WriteInt16((short)v.FireRes);          // +16
            w.WriteInt16((short)v.WaterRes);         // +18
            w.WriteInt16((short)v.DarkRes);          // +20
            w.WriteInt16((short)v.LightRes);         // +22
            for (int i = 0; i < 17; i++)
                w.WriteUInt16(0);
            w.WriteUInt32((uint)v.InventoryLimit);   // +58
            w.WriteUInt16((ushort)v.HpRegen);        // +62
            w.WriteUInt16((ushort)v.MpRegen);        // +64
            w.WriteUInt32((uint)v.MoveSpeed);        // +66
            w.WriteUInt16((ushort)v.AttackSpeed);    // +70
            w.WriteUInt16((ushort)v.CastSpeed);      // +72
            w.WriteUInt16((ushort)v.HitRecovery);    // +74
            w.WriteUInt16((ushort)v.JumpPower);      // +76
            w.WriteUInt32((uint)v.Weight);           // +78
            return w.ToArray();                      // = 82B
        }

        private static StatVector ComputeStat(byte job, int level, int first, int second)
        {
            if (first < 0 || first > 5 || second < 0 || second > 2)
                throw new ArgumentException($"[CharacterStatComputer] 非法成长参数 job={job} first={first} second={second} (守卫 first≤5, second≤2)");
            if (level < 1)
                level = 1;

            var t = GetTables(job);
            var acc = t.Base.Clone();
            if (level == 1)
                return acc;

            var g1 = GetGrowth(t, job, 0, 0);
            var gFirst = GetGrowth(t, job, first, 0);
            var gAwk = GetGrowth(t, job, first, second);
            for (int i = 1; i < level; i++)
            {
                if (i <= 14) acc.Add(g1);
                else if (i <= 49) acc.Add(gFirst);
                else acc.Add(gAwk);
            }
            return acc;
        }

        private static StatVector GetGrowth(JobStatTables t, byte job, int first, int second)
        {
            var g = second == 0 ? t.Growtype[first + 1] : t.Awakening[first + 1, second];
            if (g == null)
                throw new InvalidOperationException(
                    $"[CharacterStatComputer] job={job} 缺成长表 [growtype {first + 1}]" + (second > 0 ? $" [awakening {second}]" : ""));
            return g;
        }

        private static JobStatTables GetTables(byte job)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(job, out var cached))
                    return cached;
                JobStatTables t;
                try { t = ParseFromPvf(job); }
                catch (Exception ex)
                {
                    DfoGmTool.ServerCore.FileLogger.Log($"[CharacterStatComputer] job={job} PVF 属性解析失败, 用兜底: {ex.Message}");
                    t = BuildFallback();
                }
                _cache[job] = t;
                return t;
            }
        }

        private static JobStatTables ParseFromPvf(byte job)
        {
            // character.lst: `0 \`Swordman/Swordman.chr\` 1 \`Fighter/Fighter.chr\` ...`
            string lst = PvfArchiveAccessor.ReadText("character/character.lst");
            var lm = Regex.Match(lst ?? "", @"(?<!\d)" + (int)job + @"\s+`([^`]+)`");
            if (!lm.Success) throw new Exception($"job {job} 不在 character.lst");

            string text = PvfArchiveAccessor.ReadText("character/" + lm.Groups[1].Value);
            if (string.IsNullOrEmpty(text)) throw new Exception($"读不到 {lm.Groups[1].Value}");

            int initPos = text.IndexOf("[initial value]", StringComparison.OrdinalIgnoreCase);
            if (initPos < 0) throw new Exception($"{lm.Groups[1].Value} 无 [initial value] 段");

            var gtPos = new int[8];
            for (int n = 1; n <= 6; n++)
                gtPos[n] = text.IndexOf("[growtype " + n + "]", StringComparison.OrdinalIgnoreCase);
            gtPos[7] = text.Length;

            var t = new JobStatTables();
            t.Base = ParseVector(text.Substring(initPos, NextBoundary(gtPos, 1, text.Length) - initPos));

            for (int n = 1; n <= 6; n++)
            {
                if (gtPos[n] < 0) continue;
                int blockEnd = NextBoundary(gtPos, n + 1, text.Length);
                string block = text.Substring(gtPos[n], blockEnd - gtPos[n]);

                int aw1 = block.IndexOf("[awakening 1]", StringComparison.OrdinalIgnoreCase);
                int aw2 = block.IndexOf("[awakening 2]", StringComparison.OrdinalIgnoreCase);

                int ownEnd = aw1 >= 0 ? aw1 : (aw2 >= 0 ? aw2 : block.Length);
                t.Growtype[n] = ParseVector(block.Substring(0, ownEnd));
                if (aw1 >= 0)
                    t.Awakening[n, 1] = ParseVector(block.Substring(aw1, (aw2 > aw1 ? aw2 : block.Length) - aw1));
                if (aw2 >= 0)
                    t.Awakening[n, 2] = ParseVector(block.Substring(aw2));
            }

            if (t.Growtype[1] == null)
                throw new Exception($"{lm.Groups[1].Value} 无 [growtype 1] 段(未转职成长表必需)");
            return t;
        }

        private static int NextBoundary(int[] gtPos, int fromN, int textLength)
        {
            for (int n = fromN; n <= 6; n++)
                if (gtPos[n] >= 0)
                    return gtPos[n];
            return textLength;
        }

        private static StatVector ParseVector(string sec)
        {
            return new StatVector
            {
                HpMax = Stat(sec, "HP MAX"),
                MpMax = Stat(sec, "MP MAX"),
                PhysAtk = Stat(sec, "physical attack"),
                PhysDef = Stat(sec, "physical defense"),
                MagAtk = Stat(sec, "magical attack"),
                MagDef = Stat(sec, "magical defense"),
                FireRes = Stat(sec, "fire resistance"),
                WaterRes = Stat(sec, "water resistance"),
                DarkRes = Stat(sec, "dark resistance"),
                LightRes = Stat(sec, "light resistance"),
                InventoryLimit = Stat(sec, "inventory limit"),
                HpRegen = Stat(sec, "HP regen speed"),
                MpRegen = Stat(sec, "MP regen speed"),
                MoveSpeed = Stat(sec, "move speed"),
                AttackSpeed = Stat(sec, "attack speed"),
                CastSpeed = Stat(sec, "cast speed"),
                HitRecovery = Stat(sec, "hit recovery"),
                JumpPower = Stat(sec, "jump power"),
                Weight = Stat(sec, "weight"),
            };
        }

        private static int Stat(string section, string key)
        {
            var m = Regex.Match(section, @"\[" + Regex.Escape(key) + @"\]\s*([-\d.]+)", RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return 0;
            return (int)((float)v * 10.0);
        }
    }
}
