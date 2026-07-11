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

        
        public struct DisplayFields
        {
            public uint InstanceValue;  
            public byte Reinforce;      
            public ushort Durability;   
            public uint Enchant;        
            public byte EnchantUpgradeCount;
            public byte AmplifyType;     // 低4位=增幅方向(1力量2智力3体力4精神), bit7=异界气息污染(0x80); 污染与增幅互斥, 净化前不可增幅
            public ushort AmplifyValue; 
            public ushort Rune;         
            public byte Forging;        
            public byte[] Emblem;       
            public byte[] JewelSocket;  
            public uint CreatureExtra;
            public byte MagicSealCount;      
            public byte[] MagicSealTypes;    
            public byte[] MagicSealVal1s;    
            public byte[] MagicSealVal2s;    
            public byte[] MagicSealTail;     
        }

        
        public static DisplayFields ParseDisplayFields(byte[] raw)
        {
            var f = new DisplayFields
            {
                InstanceValue = raw.Length >= 9 ? BitConverter.ToUInt32(raw, 5) : 0u,
                Reinforce = raw.Length > 9 ? raw[9] : (byte)0,
                Durability = raw.Length >= 12 ? BitConverter.ToUInt16(raw, 10) : (ushort)0,
                Enchant = raw.Length >= 20 ? BitConverter.ToUInt32(raw, 16) : 0u,
                EnchantUpgradeCount = raw.Length > 20 ? raw[20] : (byte)0,
                AmplifyType = raw.Length > 21 ? raw[21] : (byte)0,
                AmplifyValue = raw.Length >= 24 ? BitConverter.ToUInt16(raw, 22) : (ushort)0,
                Forging = raw.Length >= 10 ? raw[raw.Length - 10] : (byte)0, 
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
                    int el = BitConverter.ToInt32(raw, off); off += 4 + el;
                }
                if (slot >= 24 && CreatureExtraResolver.HasCreatureExtra(BitConverter.ToInt32(raw, 1)))
                {
                    if (off + 4 <= raw.Length)
                        f.CreatureExtra = BitConverter.ToUInt32(raw, off);
                    off += 4; 
                }
                int cc = raw[off]; off += 1 + cc * 8; 
                off += 4;                              
                int ecOff = off;
                int ec = raw[off];                     
                int emblemLen = 1 + ec * 4;
                f.Emblem = new byte[emblemLen];
                Buffer.BlockCopy(raw, ecOff, f.Emblem, 0, emblemLen);
                off += emblemLen;
                f.Rune = BitConverter.ToUInt16(raw, off); 
                off += 2;
                
                int sc = raw[off]; off++;
                f.MagicSealCount = (byte)sc;
                f.MagicSealTypes = new byte[3];
                f.MagicSealVal1s = new byte[3];
                f.MagicSealVal2s = new byte[3];
                for (int si = 0; si < sc && si < 3; si++)
                {
                    f.MagicSealTypes[si] = raw[off]; f.MagicSealVal1s[si] = raw[off + 1]; f.MagicSealVal2s[si] = raw[off + 2];
                    off += 3;
                }
                
                if (sc > 0)
                {
                    int tailStart = off;
                    off++; 
                    byte chk = raw[off]; off++;
                    if (chk != 0xFF) off += 4;
                    f.MagicSealTail = new byte[off - tailStart];
                    Buffer.BlockCopy(raw, tailStart, f.MagicSealTail, 0, f.MagicSealTail.Length);
                }
                else
                {
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
                bw.Write((uint)0);           
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
                    bw.Write(4);             
                    bw.Write((int)0);        
                }
                if (slot >= 24 && CreatureExtraResolver.HasCreatureExtra(itemId))
                {
                    var creatureExtra = f.CreatureExtra != 0 ? f.CreatureExtra : f.InstanceValue;
                    bw.Write(creatureExtra);
                }
                
                bw.Write((byte)0);           
                
                bw.Write((int)0);
                
                if (f.Emblem != null && f.Emblem.Length > 0)
                    bw.Write(f.Emblem);
                else
                    bw.Write((byte)0);       
                
                bw.Write(f.Rune);
                
                bw.Write(f.MagicSealCount);
                for (int i = 0; i < f.MagicSealCount && i < 3; i++)
                {
                    bw.Write(f.MagicSealTypes[i]);
                    bw.Write(f.MagicSealVal1s[i]);
                    bw.Write(f.MagicSealVal2s[i]);
                }
                if (f.MagicSealCount > 0 && f.MagicSealTail != null && f.MagicSealTail.Length > 0)
                    bw.Write(f.MagicSealTail);
                else if (f.MagicSealCount > 0)
                {
                    bw.Write(f.Forging);     
                    bw.Write((byte)0xFF);    
                }
                
                var tail10 = new byte[10];
                tail10[0] = f.Forging;
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
    }
}
