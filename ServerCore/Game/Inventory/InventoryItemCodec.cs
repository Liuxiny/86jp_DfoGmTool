using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    /// <summary>
    /// Static serialization / deserialization helpers for inventory items.
    /// Extracted from SqliteInventoryStore to keep the store focused on DB operations.
    /// </summary>
    internal static class InventoryItemCodec
    {
        internal static CommonInventoryItem ReadCommonItem(SqliteDataReader reader, string extraJson)
        {
            return new CommonInventoryItem
            {
                SlotIndex = Convert.ToInt16(reader.GetInt32(1), CultureInfo.InvariantCulture),
                ItemTemplateId = reader.GetInt32(2),
                CountOrInstanceValue = reader.GetInt32(4),
                Durability = Convert.ToUInt16(reader.GetInt32(6), CultureInfo.InvariantCulture),
                SealFlag = Convert.ToByte(reader.GetInt32(7), CultureInfo.InvariantCulture),
                ExpireTime = reader.GetInt32(9),
                Marker16 = reader.GetInt32(10),
                ExtData0 = Convert.ToByte(ReadIntValue(extraJson, "extData0"), CultureInfo.InvariantCulture),
                PrefixData0E = ReadHexValue(extraJson, "prefixData0E", 8),
                MiddleData1A = ReadHexValue(extraJson, "middleData1A", 17),
                TailData2F = ReadHexValue(extraJson, "tailData2F", 37),
                JewelSocket = ReadHexValue(extraJson, "jewelSocket", 30),
                EquipmentLockId = reader.FieldCount > 13
                    ? Convert.ToByte(reader.GetInt32(12), CultureInfo.InvariantCulture)
                    : (byte)0,
            };
        }

        internal static AvatarInventoryItem ReadAvatarItem(SqliteDataReader reader, string extraJson)
        {
            return new AvatarInventoryItem
            {
                SlotIndex = Convert.ToInt16(reader.GetInt32(1), CultureInfo.InvariantCulture),
                AvatarItemId = reader.GetInt32(2),
                ExpireTime = reader.GetInt32(9),
                OptionValue = Convert.ToByte(reader.GetInt32(8), CultureInfo.InvariantCulture),
                UnknownFixed30 = reader.GetInt32(10),
                UnknownFixed4 = Convert.ToUInt16(ReadIntValue(extraJson, "unknownFixed4"), CultureInfo.InvariantCulture),
                Reserved0 = ReadHexValue(extraJson, "reserved0", 5),
                Reserved1 = ReadHexValue(extraJson, "reserved1", 71),
                Reserved2 = ReadHexValue(extraJson, "reserved2", 30),
                TailData = ReadHexValue(extraJson, "tailData", 7),
            };
        }

        internal static AvatarInventoryItem ReadEquipmentAsAvatarItem(SqliteDataReader reader, string extraJson)
        {
            var common = ReadCommonItem(reader, extraJson);
            var buf = new byte[126];
            buf[0] = (byte)(common.SlotIndex & 0xFF);
            buf[1] = (byte)((common.SlotIndex >> 8) & 0xFF);
            buf[2] = (byte)(common.ItemTemplateId & 0xFF);
            buf[3] = (byte)((common.ItemTemplateId >> 8) & 0xFF);
            buf[4] = (byte)((common.ItemTemplateId >> 16) & 0xFF);
            buf[5] = (byte)((common.ItemTemplateId >> 24) & 0xFF);
            buf[6] = (byte)(common.CountOrInstanceValue & 0xFF);
            buf[7] = (byte)((common.CountOrInstanceValue >> 8) & 0xFF);
            buf[8] = (byte)((common.CountOrInstanceValue >> 16) & 0xFF);
            buf[9] = (byte)((common.CountOrInstanceValue >> 24) & 0xFF);
            buf[10] = common.ExtData0;
            buf[11] = (byte)(common.Durability & 0xFF);
            buf[12] = (byte)((common.Durability >> 8) & 0xFF);
            buf[13] = common.SealFlag;
            Array.Copy(common.PrefixData0E, 0, buf, 14, 8);
            buf[22] = (byte)(common.Marker16 & 0xFF);
            buf[23] = (byte)((common.Marker16 >> 8) & 0xFF);
            buf[24] = (byte)((common.Marker16 >> 16) & 0xFF);
            buf[25] = (byte)((common.Marker16 >> 24) & 0xFF);
            Array.Copy(common.MiddleData1A, 0, buf, 26, 17);
            buf[43] = (byte)(common.ExpireTime & 0xFF);
            buf[44] = (byte)((common.ExpireTime >> 8) & 0xFF);
            buf[45] = (byte)((common.ExpireTime >> 16) & 0xFF);
            buf[46] = (byte)((common.ExpireTime >> 24) & 0xFF);
            Array.Copy(common.TailData2F, 0, buf, 47, 37);

            byte optionValue = buf[11];
            Array.Clear(buf, 6, 78);
            buf[84] = 0x1E;
            buf[118] = 0x04;
            var jewel = ReadHexValue(extraJson, "jewelSocket", 30);
            if (jewel != null && jewel.Length == 30)
                Array.Copy(jewel, 0, buf, 88, 30);

            return new AvatarInventoryItem
            {
                SlotIndex = BitConverter.ToInt16(buf, 0),
                AvatarItemId = BitConverter.ToInt32(buf, 2),
                ExpireTime = common.ExpireTime,
                Reserved0 = CharacterItemListSnapshot.Slice(buf, 6, 5),
                OptionValue = optionValue,
                Reserved1 = CharacterItemListSnapshot.Slice(buf, 12, 71),
                UnknownFixed30 = BitConverter.ToInt32(buf, 83),
                Reserved2 = CharacterItemListSnapshot.Slice(buf, 87, 30),
                UnknownFixed4 = BitConverter.ToUInt16(buf, 117),
                TailData = CharacterItemListSnapshot.Slice(buf, 119, 7),
            };
        }

        internal static PetInventoryItem ReadPetItem(SqliteDataReader reader, string extraJson)
        {
            var slot = reader.GetInt32(1);
            var isPetConsumable = string.Equals(reader.GetString(3), "pet", StringComparison.Ordinal)
                && slot >= SqliteInventoryStore.PetConsumableSlotStart
                && slot <= SqliteInventoryStore.PetConsumableSlotEnd;
            var serialOrInstanceValue = slot >= SqliteInventoryStore.PetEquipmentSlotStart
                && slot <= SqliteInventoryStore.PetEquipmentSlotEnd
                ? reader.GetInt32(5)
                : reader.GetInt32(11);
            var stackCount = isPetConsumable
                ? Math.Max(reader.GetInt32(4), Math.Max(reader.GetInt32(5), reader.GetInt32(11)))
                : 1;
            return new PetInventoryItem
            {
                SlotIndex = Convert.ToInt16(slot, CultureInfo.InvariantCulture),
                CreatureItemId = reader.GetInt32(2),
                CreatureSerialOrHandle = serialOrInstanceValue,
                StackCount = stackCount,
                ExpireTime = reader.GetInt32(9),
                TailData0A = ReadHexValue(extraJson, "tailData0A", 74),
            };
        }

        internal static string InferCommonItemKind(CommonInventoryItem item)
        {
            if (item.ItemTemplateId <= 0)
                return "special";

            if (item.ExpireTime != 0)
                return "special";

            return item.Marker16 == 0 ? "stackable" : "equipment";
        }

        internal static string SerializeCommon(CommonInventoryItem item)
        {
            return "{"
                + "\"extData0\":" + item.ExtData0.ToString(CultureInfo.InvariantCulture)
                + ",\"prefixData0E\":\"" + ToHex(item.PrefixData0E) + "\""
                + ",\"middleData1A\":\"" + ToHex(item.MiddleData1A) + "\""
                + ",\"tailData2F\":\"" + ToHex(item.TailData2F) + "\""
                + ",\"jewelSocket\":\"" + ToHex(item.JewelSocket) + "\""
                + "}";
        }

        internal static string SerializeAvatar(AvatarInventoryItem item)
        {
            return "{"
                + "\"reserved0\":\"" + ToHex(item.Reserved0) + "\""
                + ",\"reserved1\":\"" + ToHex(item.Reserved1) + "\""
                + ",\"reserved2\":\"" + ToHex(item.Reserved2) + "\""
                + ",\"unknownFixed4\":" + item.UnknownFixed4.ToString(CultureInfo.InvariantCulture)
                + ",\"tailData\":\"" + ToHex(item.TailData) + "\""
                + "}";
        }

        internal static string SerializePet(PetInventoryItem item)
        {
            return "{\"tailData0A\":\"" + ToHex(item.TailData0A) + "\"}";
        }

        internal static string ToHex(byte[] data)
        {
            return BitConverter.ToString(data ?? new byte[0]).Replace("-", string.Empty);
        }

        internal static int ReadIntValue(string json, string propertyName)
        {
            var token = "\"" + propertyName + "\":";
            var start = json.IndexOf(token, StringComparison.Ordinal);
            if (start < 0)
                return 0;

            start += token.Length;
            var end = json.IndexOfAny(new[] { ',', '}' }, start);
            if (end < 0)
                end = json.Length;

            var valueText = json.Substring(start, end - start);
            return int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        internal static byte[] ReadHexValue(string json, string propertyName, int expectedLength)
        {
            var token = "\"" + propertyName + "\":\"";
            var start = json.IndexOf(token, StringComparison.Ordinal);
            if (start < 0)
                return new byte[expectedLength];

            start += token.Length;
            var end = json.IndexOf('"', start);
            if (end < 0)
                return new byte[expectedLength];

            var hex = json.Substring(start, end - start);
            return FromHex(hex, expectedLength);
        }

        internal static string ReadRawStringValue(string json, string propertyName)
        {
            var token = "\"" + propertyName + "\":\"";
            var start = json.IndexOf(token, StringComparison.Ordinal);
            if (start < 0) return null;
            start += token.Length;
            var end = json.IndexOf('"', start);
            if (end < 0) return null;
            return json.Substring(start, end - start);
        }

        internal static byte[] FromHex(string hex, int expectedLength)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return new byte[expectedLength];

            var length = Math.Min(expectedLength, hex.Length / 2);
            var buffer = new byte[expectedLength];
            for (var index = 0; index < length; index++)
                buffer[index] = byte.Parse(hex.Substring(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            return buffer;
        }
    }
}
