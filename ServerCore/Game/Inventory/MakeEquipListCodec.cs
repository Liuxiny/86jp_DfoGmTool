using System;
using System.Collections.Generic;
using System.IO;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    
    
    
    
    
    
    
    
    
    
    
    
    
    public static class MakeEquipListCodec
    {
        public sealed class Entry
        {
            public int Slot;
            public int ItemId;
            public int ExpireTime;
            public byte EquipmentLockId;
            public byte[] Raw;   
        }

        public sealed class ParsedEquipList
        {
            public byte[] Header;          
            public List<Entry> Entries;    
            public byte[] Trailer;         
        }

        private const int HeaderLen = 92;

        public static ParsedEquipList Parse(byte[] blob)
        {
            if (blob == null || blob.Length < HeaderLen + 1)
                throw new InvalidDataException($"equip blob too short: {(blob == null ? 0 : blob.Length)}B");

            var header = new byte[HeaderLen];
            Buffer.BlockCopy(blob, 0, header, 0, HeaderLen);

            int off = HeaderLen;
            int equipCount = blob[off];
            off += 1;

            var entries = new List<Entry>(equipCount);
            for (int i = 0; i < equipCount; i++)
            {
                int start = off;
                int slot = blob[off];
                int itemId = BitConverter.ToInt32(blob, off + 1);
                off = SkipEntry(blob, off);

                var raw = new byte[off - start];
                Buffer.BlockCopy(blob, start, raw, 0, raw.Length);
                entries.Add(new Entry { Slot = slot, ItemId = itemId, Raw = raw });
            }

            var trailer = new byte[blob.Length - off];
            Buffer.BlockCopy(blob, off, trailer, 0, trailer.Length);

            return new ParsedEquipList { Header = header, Entries = entries, Trailer = trailer };
        }

        public static byte[] Build(ParsedEquipList parsed)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(parsed.Header, 0, parsed.Header.Length);
                ms.WriteByte((byte)parsed.Entries.Count);
                foreach (var e in parsed.Entries)
                    ms.Write(e.Raw, 0, e.Raw.Length);
                ms.Write(parsed.Trailer, 0, parsed.Trailer.Length);
                return ms.ToArray();
            }
        }

        
        public struct ChronicleOptionFields
        {
            public int OptionId;
            public byte CharacJob;
            public byte FirstGrowType;
            public byte EquipmentType;
            public byte OptionNo;
        }

        public struct DisplayFields
        {
            public uint InstanceValue;  
            public byte Reinforce;      
            public ushort Durability;   
            public uint ClearAvatar;
            public byte SealFlag;
            public uint ClearAvatarId { get => ClearAvatar; set => ClearAvatar = value; }
            public uint Enchant;        
            public byte EnchantUpgradeCount;
            public byte AmplifyType;     // 低4位=增幅方向(1体力2精神3力量4智力), bit7=异界气息污染(0x80); 污染与增幅互斥, 净化前不可增幅
            public ushort AmplifyValue; 
            public ushort Rune;         
            public byte Forging;        
            public byte EmancipateEquipmentLevel;
            public byte TradeRestriction;
            public ushort TailUnknown0;
            public byte TailUnknown1;
            public byte TailUnknown2;
            public byte TailUnknown3;
            public byte RemainUseCount;
            public byte SortLockFlag;
            public byte EmblemSocketCount;
            public int EmblemId1;
            public int EmblemId2;
            public byte[] Emblem;       
            public byte[] JewelSocket;  
            public byte[] ExpansionData;
            public uint Marker16;
            public uint PetRemainSeconds { get => Marker16; set => Marker16 = value; }
            public uint CreatureExtra { get => Marker16; set => Marker16 = value; }
            public ChronicleOptionFields[] ChronicleOptions;
            public int ExpireTime;
            public byte MagicSealCount;      
            public byte[] MagicSealTypes;    
            public byte[] MagicSealVal1s;    
            public byte[] MagicSealVal2s;    
            public byte RandomOptionState;
            public byte RandomOptionChangedIndex;
            public byte RandomOptionChangeState;
            public byte RandomOptionChangeType;
            public byte RandomOptionChangeValue1;
            public byte RandomOptionChangeValue2;
            public byte[] MagicSealTail;     
        }

        
        public static DisplayFields ParseDisplayFields(byte[] raw)
        {
            var f = new DisplayFields
            {
                InstanceValue = raw.Length >= 9 ? BitConverter.ToUInt32(raw, 5) : 0u,
                Reinforce = raw.Length > 9 ? raw[9] : (byte)0,
                Durability = raw.Length >= 12 ? BitConverter.ToUInt16(raw, 10) : (ushort)0,
                ClearAvatar = raw.Length >= 16 ? BitConverter.ToUInt32(raw, 12) : 0u,
                SealFlag = raw.Length > 12 ? raw[12] : (byte)0,
                Enchant = raw.Length >= 20 ? BitConverter.ToUInt32(raw, 16) : 0u,
                EnchantUpgradeCount = raw.Length > 20 ? raw[20] : (byte)0,
                AmplifyType = raw.Length > 21 ? raw[21] : (byte)0,
                AmplifyValue = raw.Length >= 24 ? BitConverter.ToUInt16(raw, 22) : (ushort)0,
                Forging = raw.Length >= 10 ? raw[raw.Length - 10] : (byte)0, 
                EmancipateEquipmentLevel = raw.Length >= 10 ? raw[raw.Length - 9] : (byte)0,
                TradeRestriction = raw.Length >= 10 ? raw[raw.Length - 8] : (byte)0,
                TailUnknown0 = raw.Length >= 10 ? BitConverter.ToUInt16(raw, raw.Length - 7) : (ushort)0,
                TailUnknown1 = raw.Length >= 10 ? raw[raw.Length - 5] : (byte)0,
                TailUnknown2 = raw.Length >= 10 ? raw[raw.Length - 4] : (byte)0,
                TailUnknown3 = raw.Length >= 10 ? raw[raw.Length - 3] : (byte)0,
                RemainUseCount = raw.Length >= 10 ? raw[raw.Length - 2] : (byte)0,
                SortLockFlag = raw.Length >= 10 ? raw[raw.Length - 1] : (byte)0,
            };
            
            try
            {
                int slot = raw[0];
                int off = 24;
                if (slot <= 10)
                {
                    int jl = BitConverter.ToInt32(raw, off);
                    
                    if (jl > 0 && off + 4 + jl <= raw.Length)
                    {
                        f.JewelSocket = new byte[jl];
                        Buffer.BlockCopy(raw, off + 4, f.JewelSocket, 0, jl);
                    }
                    off += 4 + jl;
                    int el = BitConverter.ToInt32(raw, off);
                    off += 4;
                    if (el > 0 && off + el <= raw.Length)
                    {
                        f.ExpansionData = new byte[el];
                        Buffer.BlockCopy(raw, off, f.ExpansionData, 0, el);
                    }
                    off += el;
                }
                if (slot >= 24 && CreatureExtraResolver.HasCreatureExtra(BitConverter.ToInt32(raw, 1)))
                {
                    if (off + 4 <= raw.Length)
                        f.Marker16 = BitConverter.ToUInt32(raw, off);
                    off += 4; 
                }
                int cc = raw[off++];
                f.ChronicleOptions = new ChronicleOptionFields[Math.Max(0, cc)];
                for (var ci = 0; ci < cc && off + 8 <= raw.Length; ci++)
                {
                    f.ChronicleOptions[ci] = new ChronicleOptionFields
                    {
                        OptionId = BitConverter.ToInt32(raw, off),
                        CharacJob = raw[off + 4],
                        FirstGrowType = raw[off + 5],
                        EquipmentType = raw[off + 6],
                        OptionNo = raw[off + 7],
                    };
                    off += 8;
                }
                f.ExpireTime = off + 4 <= raw.Length ? BitConverter.ToInt32(raw, off) : 0;
                off += 4;                              
                int ecOff = off;
                int ec = raw[off];                     
                int emblemLen = 1 + ec * 4;
                f.Emblem = new byte[emblemLen];
                Buffer.BlockCopy(raw, ecOff, f.Emblem, 0, emblemLen);
                f.EmblemSocketCount = (byte)Math.Min(byte.MaxValue, ec);
                if (ec > 0 && emblemLen >= 5)
                    f.EmblemId1 = BitConverter.ToInt32(f.Emblem, 1);
                if (ec > 1 && emblemLen >= 9)
                    f.EmblemId2 = BitConverter.ToInt32(f.Emblem, 5);
                off += emblemLen;
                f.Rune = BitConverter.ToUInt16(raw, off); 
                off += 2;
                
                int sc = raw[off]; off++;
                f.MagicSealCount = (byte)sc;
                f.MagicSealTypes = new byte[3];
                f.MagicSealVal1s = new byte[3];
                f.MagicSealVal2s = new byte[3];
                for (int si = 0; si < sc && off + 3 <= raw.Length; si++)
                {
                    if (si < 3)
                    {
                        f.MagicSealTypes[si] = raw[off];
                        f.MagicSealVal1s[si] = raw[off + 1];
                        f.MagicSealVal2s[si] = raw[off + 2];
                    }
                    off += 3;
                }
                
                if (sc > 0)
                {
                    int tailStart = off;
                    f.RandomOptionState = off < raw.Length ? raw[off++] : (byte)0;
                    f.RandomOptionChangedIndex = off < raw.Length ? raw[off++] : (byte)0xFF;
                    if (f.RandomOptionChangedIndex != 0xFF && off + 4 <= raw.Length)
                    {
                        f.RandomOptionChangeState = raw[off++];
                        f.RandomOptionChangeType = raw[off++];
                        f.RandomOptionChangeValue1 = raw[off++];
                        f.RandomOptionChangeValue2 = raw[off++];
                    }
                    f.MagicSealTail = new byte[off - tailStart];
                    Buffer.BlockCopy(raw, tailStart, f.MagicSealTail, 0, f.MagicSealTail.Length);
                }
                else
                {
                    f.RandomOptionChangedIndex = 0xFF;
                    f.MagicSealTail = Array.Empty<byte>();
                }
            }
            catch { f.Rune = 0; }
            return f;
        }

        
        private static int SkipEntry(byte[] b, int off)
        {
            int entryStart = off;
            int slot = b[off];
            off += 24; 

            if (slot <= 10)
            {
                int jewelLen = BitConverter.ToInt32(b, off); off += 4 + jewelLen;
                int expLen = BitConverter.ToInt32(b, off); off += 4 + expLen;
            }
            
            
            
            if (slot >= 24 && CreatureExtraResolver.HasCreatureExtra(BitConverter.ToInt32(b, entryStart + 1)))
                off += 4;

            int chronicleCount = b[off]; off += 1 + chronicleCount * 8;  
            off += 4;                                                    
            int emblemCount = b[off]; off += 1 + emblemCount * 4;        
            off += 2;                                                    
            int magicSealCount = b[off]; off += 1 + magicSealCount * 3;            
            if (magicSealCount > 0)
            {
                off += 1;                  
                byte check = b[off]; off += 1;
                if (check != 0xFF)
                    off += 4;
            }
            off += 10; 

            return off;
        }

        
        public static Entry RemoveSlot(ParsedEquipList parsed, int slot)
        {
            int idx = parsed.Entries.FindIndex(e => e.Slot == slot);
            if (idx < 0) return null;
            var removed = parsed.Entries[idx];
            parsed.Entries.RemoveAt(idx);
            return removed;
        }

        
        public static byte[] SetSlotByte(byte[] raw, int slot)
        {
            var copy = new byte[raw.Length];
            Buffer.BlockCopy(raw, 0, copy, 0, raw.Length);
            copy[0] = (byte)slot;
            return copy;
        }

        
        
        public static byte[] BuildEntryFromDisplayFields(int slot, int itemId, DisplayFields f)
        {
            using (var w = new MemoryStream())
            using (var bw = new BinaryWriter(w))
            {
                
                bw.Write((byte)slot);
                bw.Write(itemId);
                bw.Write(f.InstanceValue);
                bw.Write(f.Reinforce);
                bw.Write(f.Durability);
                bw.Write(f.ClearAvatar != 0 ? f.ClearAvatar : f.SealFlag);
                bw.Write(f.Enchant);
                bw.Write(f.EnchantUpgradeCount);
                bw.Write(f.AmplifyType);
                bw.Write(f.AmplifyValue);
                
                if (slot <= 10)
                {
                    if (f.JewelSocket != null && f.JewelSocket.Length > 0)
                    {
                        bw.Write(f.JewelSocket.Length);
                        bw.Write(f.JewelSocket);
                    }
                    else
                    {
                        bw.Write(0); 
                    }
                    var expansion = NormalizeExpansionData(f.ExpansionData);
                    bw.Write(expansion.Length);
                    bw.Write(expansion);
                }
                if (slot >= 24 && CreatureExtraResolver.HasCreatureExtra(itemId))
                {
                    bw.Write(f.Marker16);
                }
                
                var chronicleOptions = f.ChronicleOptions ?? Array.Empty<ChronicleOptionFields>();
                bw.Write((byte)Math.Min(byte.MaxValue, chronicleOptions.Length));
                for (var i = 0; i < chronicleOptions.Length && i < byte.MaxValue; i++)
                {
                    bw.Write(chronicleOptions[i].OptionId);
                    bw.Write(chronicleOptions[i].CharacJob);
                    bw.Write(chronicleOptions[i].FirstGrowType);
                    bw.Write(chronicleOptions[i].EquipmentType);
                    bw.Write(chronicleOptions[i].OptionNo);
                }
                
                bw.Write(f.ExpireTime);
                
                var emblem = BuildEmblemData(f);
                bw.Write(emblem);
                
                bw.Write(f.Rune);
                
                var magicSealCount = (byte)Math.Min(3, (int)f.MagicSealCount);
                bw.Write(magicSealCount);
                for (int i = 0; i < magicSealCount; i++)
                {
                    bw.Write(ReadByte(f.MagicSealTypes, i));
                    bw.Write(ReadByte(f.MagicSealVal1s, i));
                    bw.Write(ReadByte(f.MagicSealVal2s, i));
                }
                if (magicSealCount > 0)
                    bw.Write(BuildRandomOptionDynamicTail(f));
                
                var tail10 = new byte[10];
                tail10[0] = f.Forging;
                tail10[1] = f.EmancipateEquipmentLevel;
                tail10[2] = f.TradeRestriction;
                BitConverter.GetBytes(f.TailUnknown0).CopyTo(tail10, 3);
                tail10[5] = f.TailUnknown1;
                tail10[6] = f.TailUnknown2;
                tail10[7] = f.TailUnknown3;
                tail10[8] = f.RemainUseCount;
                tail10[9] = f.SortLockFlag == 1 ? (byte)1 : (byte)0;
                bw.Write(tail10);
                return w.ToArray();
            }
        }

        
        
        
        
        
        public static void UpsertEntry(ParsedEquipList parsed, Entry entry)
        {
            int idx = parsed.Entries.FindIndex(e => e.Slot == entry.Slot);
            if (idx >= 0)
            {
                parsed.Entries[idx] = entry;
                return;
            }
            int insertAt = parsed.Entries.FindIndex(e => e.Slot > entry.Slot);
            if (insertAt < 0)
                parsed.Entries.Add(entry);
            else
                parsed.Entries.Insert(insertAt, entry);
        }

        internal static ChronicleOptionFields[] BuildChronicleOptions(byte[] middleData1A)
        {
            if (middleData1A == null || middleData1A.Length < 17)
                return Array.Empty<ChronicleOptionFields>();

            var count = Math.Min(2, (int)middleData1A[0]);
            var result = new ChronicleOptionFields[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = new ChronicleOptionFields
                {
                    OptionId = BitConverter.ToInt32(middleData1A, 1 + i * 4),
                    CharacJob = middleData1A[9 + i],
                    FirstGrowType = middleData1A[11 + i],
                    EquipmentType = middleData1A[13 + i],
                    OptionNo = middleData1A[15 + i],
                };
            }
            return result;
        }

        internal static byte[] BuildMiddleData1A(ChronicleOptionFields[] options)
        {
            var data = new byte[17];
            if (options == null || options.Length == 0)
                return data;

            var count = Math.Min(2, options.Length);
            data[0] = (byte)count;
            for (var i = 0; i < count; i++)
            {
                BitConverter.GetBytes(options[i].OptionId).CopyTo(data, 1 + i * 4);
                data[9 + i] = options[i].CharacJob;
                data[11 + i] = options[i].FirstGrowType;
                data[13 + i] = options[i].EquipmentType;
                data[15 + i] = options[i].OptionNo;
            }
            return data;
        }

        internal static byte[] NormalizeExpansionData(byte[] data)
        {
            var copy = new byte[4];
            if (data != null && data.Length > 0)
                Buffer.BlockCopy(data, 0, copy, 0, Math.Min(data.Length, copy.Length));
            return copy;
        }

        private static byte[] BuildEmblemData(DisplayFields fields)
        {
            if (fields.Emblem != null && fields.Emblem.Length > 0)
            {
                var copy = new byte[fields.Emblem.Length];
                Buffer.BlockCopy(fields.Emblem, 0, copy, 0, copy.Length);
                return copy;
            }

            var count = Math.Min(2, (int)fields.EmblemSocketCount);
            var data = new byte[1 + count * 4];
            data[0] = (byte)count;
            if (count > 0)
                BitConverter.GetBytes(fields.EmblemId1).CopyTo(data, 1);
            if (count > 1)
                BitConverter.GetBytes(fields.EmblemId2).CopyTo(data, 5);
            return data;
        }

        private static byte[] BuildRandomOptionDynamicTail(DisplayFields fields)
        {
            if (!HasExplicitRandomOptionDynamicFields(fields)
                && fields.MagicSealTail != null
                && fields.MagicSealTail.Length > 0)
            {
                var copy = new byte[fields.MagicSealTail.Length];
                Buffer.BlockCopy(fields.MagicSealTail, 0, copy, 0, copy.Length);
                return copy;
            }

            var changedIndex = HasExplicitRandomOptionDynamicFields(fields)
                ? fields.RandomOptionChangedIndex
                : (byte)0xFF;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(fields.RandomOptionState);
                bw.Write(changedIndex);
                if (changedIndex != 0xFF)
                {
                    bw.Write(fields.RandomOptionChangeState);
                    bw.Write(fields.RandomOptionChangeType);
                    bw.Write(fields.RandomOptionChangeValue1);
                    bw.Write(fields.RandomOptionChangeValue2);
                }
                return ms.ToArray();
            }
        }

        private static bool HasExplicitRandomOptionDynamicFields(DisplayFields fields)
        {
            return fields.RandomOptionState != 0
                || fields.RandomOptionChangedIndex != 0
                || fields.RandomOptionChangeState != 0
                || fields.RandomOptionChangeType != 0
                || fields.RandomOptionChangeValue1 != 0
                || fields.RandomOptionChangeValue2 != 0;
        }

        private static byte ReadByte(byte[] data, int index)
        {
            return data != null && index >= 0 && index < data.Length ? data[index] : (byte)0;
        }
    }
}
