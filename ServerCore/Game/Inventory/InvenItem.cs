using System;
using System.Collections.Generic;
using System.IO;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    public sealed class InvenItem
    {
        
        public byte Slot;                 
        public int ItemId;                
        public uint Value;                
        public byte Attr;                 
        public ushort Durability;         
        public uint ClearAvatar;          
        public uint EnchantIndex;         
        public byte EnchantUpgradeCount;
        public byte AmplifyType;           // 低4位=增幅方向(1力量2智力3体力4精神), bit7=异界气息污染(0x80); 污染与增幅互斥
        public ushort AmplifyValue;       

        
        public byte[] JewelSocket;        
        public byte[] Expansion;          

        
        public uint CreatureExtra;        

        
        public List<byte[]> Chronicle = new List<byte[]>();   
        public uint TrailingU32;                              
        public List<uint> Emblems = new List<uint>();         
        public ushort Rune;                                   
        public List<SealEntry> Seals = new List<SealEntry>(); 
        public byte SealGenuineUpgrade;                       
        public byte SealCheck = 0xFF;                         
        public uint SealExtra;                                
        public byte[] Tail10 = new byte[10];                  

        public struct SealEntry
        {
            public byte Type;
            public byte Val1;
            public byte Val2;
        }

        private bool HasAvatarBlock => Slot <= 10;
        
        
        
        private bool HasCreatureExtra => Slot >= 24 && CreatureExtraResolver.HasCreatureExtra(ItemId);

        
        
        
        
        public static InvenItem Parse(byte[] raw)
        {
            if (raw == null || raw.Length < 24)
                throw new InvalidDataException($"InvenItem raw too short: {(raw == null ? 0 : raw.Length)}B");

            var it = new InvenItem();
            int o = 0;
            it.Slot = raw[o]; o += 1;
            it.ItemId = BitConverter.ToInt32(raw, o); o += 4;
            it.Value = BitConverter.ToUInt32(raw, o); o += 4;
            it.Attr = raw[o]; o += 1;
            it.Durability = BitConverter.ToUInt16(raw, o); o += 2;
            it.ClearAvatar = BitConverter.ToUInt32(raw, o); o += 4;
            it.EnchantIndex = BitConverter.ToUInt32(raw, o); o += 4;
            it.EnchantUpgradeCount = raw[o]; o += 1;
            it.AmplifyType = raw[o]; o += 1;
            it.AmplifyValue = BitConverter.ToUInt16(raw, o); o += 2;

            if (it.HasAvatarBlock)
            {
                int jewelLen = BitConverter.ToInt32(raw, o); o += 4;
                it.JewelSocket = Slice(raw, ref o, jewelLen);
                int expLen = BitConverter.ToInt32(raw, o); o += 4;
                it.Expansion = Slice(raw, ref o, expLen);
            }
            if (it.HasCreatureExtra)
            {
                it.CreatureExtra = BitConverter.ToUInt32(raw, o); o += 4;
            }

            int chronicleCount = raw[o]; o += 1;
            for (int i = 0; i < chronicleCount; i++)
                it.Chronicle.Add(Slice(raw, ref o, 8));

            it.TrailingU32 = BitConverter.ToUInt32(raw, o); o += 4;

            int emblemCount = raw[o]; o += 1;
            for (int i = 0; i < emblemCount; i++)
            {
                it.Emblems.Add(BitConverter.ToUInt32(raw, o));
                o += 4;
            }

            it.Rune = BitConverter.ToUInt16(raw, o); o += 2;

            int sealCount = raw[o]; o += 1;
            for (int i = 0; i < sealCount; i++)
            {
                it.Seals.Add(new SealEntry { Type = raw[o], Val1 = raw[o + 1], Val2 = raw[o + 2] });
                o += 3;
            }
            if (sealCount > 0)
            {
                it.SealGenuineUpgrade = raw[o]; o += 1;
                it.SealCheck = raw[o]; o += 1;
                if (it.SealCheck != 0xFF)
                {
                    it.SealExtra = BitConverter.ToUInt32(raw, o); o += 4;
                }
            }

            it.Tail10 = Slice(raw, ref o, 10);

            if (o != raw.Length)
                throw new InvalidDataException(
                    $"InvenItem parse incomplete: slot={it.Slot} itemId={it.ItemId} consumed {o}/{raw.Length}B");
            return it;
        }

        
        public void Write(Network.GamePacketWriter w)
        {
            w.WriteByte(Slot);
            w.WriteInt32(ItemId);
            w.WriteUInt32(Value);
            w.WriteByte(Attr);
            w.WriteUInt16(Durability);
            w.WriteUInt32(ClearAvatar);
            w.WriteUInt32(EnchantIndex);
            w.WriteByte(EnchantUpgradeCount);
            w.WriteByte(AmplifyType);
            w.WriteUInt16(AmplifyValue);

            if (HasAvatarBlock)
            {
                var jewel = JewelSocket ?? Array.Empty<byte>();
                w.WriteInt32(jewel.Length);
                w.WriteBytes(jewel);
                var exp = Expansion ?? Array.Empty<byte>();
                w.WriteInt32(exp.Length);
                w.WriteBytes(exp);
            }
            if (HasCreatureExtra)
                w.WriteUInt32(CreatureExtra);

            w.WriteByte((byte)Chronicle.Count);
            foreach (var c in Chronicle)
                w.WriteBytes(c);

            w.WriteUInt32(TrailingU32);

            w.WriteByte((byte)Emblems.Count);
            foreach (var e in Emblems)
                w.WriteUInt32(e);

            w.WriteUInt16(Rune);

            w.WriteByte((byte)Seals.Count);
            foreach (var s in Seals)
            {
                w.WriteByte(s.Type);
                w.WriteByte(s.Val1);
                w.WriteByte(s.Val2);
            }
            if (Seals.Count > 0)
            {
                w.WriteByte(SealGenuineUpgrade);
                w.WriteByte(SealCheck);
                if (SealCheck != 0xFF)
                    w.WriteUInt32(SealExtra);
            }

            w.WriteBytes(Tail10 != null && Tail10.Length == 10 ? Tail10 : new byte[10]);
        }

        public byte[] ToBytes()
        {
            var w = new Network.GamePacketWriter();
            Write(w);
            return w.ToArray();
        }

        
        public static int VerifyRoundTrip(byte[] raw, out InvenItem item)
        {
            item = Parse(raw);
            var rebuilt = item.ToBytes();
            if (rebuilt.Length != raw.Length)
                return Math.Min(rebuilt.Length, raw.Length);
            for (int i = 0; i < raw.Length; i++)
                if (rebuilt[i] != raw[i]) return i;
            return -1;
        }

        private static byte[] Slice(byte[] src, ref int offset, int len)
        {
            if (len < 0 || offset + len > src.Length)
                throw new InvalidDataException($"InvenItem slice out of range: off={offset} len={len} total={src.Length}");
            var dst = new byte[len];
            Buffer.BlockCopy(src, offset, dst, 0, len);
            offset += len;
            return dst;
        }
    }
}
