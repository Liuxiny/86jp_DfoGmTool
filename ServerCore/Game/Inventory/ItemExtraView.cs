using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    // 只解释 character_items/account_cargo_items 的 extra_json，不绑定 ItemRecord。
    internal sealed class ItemExtraView
    {
        private readonly JsonObject _json;

        internal ItemExtraView(JsonObject json)
        {
            _json = json;
            Raw84 = new RawItemEntry84View(
                attr: ReadByte(json, "extData0"),
                prefixData0E: ReadHexFixed(json, "prefixData0E", 8),
                middleData1A: ReadHexFixed(json, "middleData1A", 17),
                tailData2F: ReadHexFixed(json, "tailData2F", 37),
                jewelSocket: ReadHexFixed(json, "jewelSocket", 30));
            Equipment = new EquipmentExtraView(
                Raw84.Attr,
                Raw84.PrefixData0E,
                ReadHexActual(json, "middleData1A"),
                Raw84.TailData2F,
                Raw84.JewelSocket);
            Avatar = new AvatarExtraView(
                ReadHexFixed(json, "reserved0", 5),
                ReadHexFixed(json, "reserved1", 71),
                ReadHexFixed(json, "reserved2", 30),
                Convert.ToUInt16(ReadInt(json, "unknownFixed4"), CultureInfo.InvariantCulture),
                ReadHexFixed(json, "tailData", 7));
            Pet = new PetExtraView(ReadHexFixed(json, "tailData0A", 74));
        }

        public RawItemEntry84View Raw84 { get; }

        public EquipmentExtraView Equipment { get; }

        public AvatarExtraView Avatar { get; }

        public PetExtraView Pet { get; }

        public static ItemExtraView Parse(string extraJson)
        {
            JsonObject json = null;
            if (!string.IsNullOrWhiteSpace(extraJson))
            {
                try
                {
                    json = JsonNode.Parse(extraJson) as JsonObject;
                }
                catch
                {
                    json = null;
                }
            }

            return new ItemExtraView(json ?? new JsonObject());
        }

        public string Serialize()
        {
            return _json.ToJsonString();
        }

        public void MergeInto(JsonObject target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            foreach (var property in _json)
                target[property.Key] = property.Value == null ? null : property.Value.DeepClone();
        }

        private static byte ReadByte(JsonObject json, string propertyName)
        {
            return Convert.ToByte(ReadInt(json, propertyName) & 0xFF, CultureInfo.InvariantCulture);
        }

        private static int ReadInt(JsonObject json, string propertyName)
        {
            if (!json.TryGetPropertyValue(propertyName, out var node) || node == null)
                return 0;

            return int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        private static byte[] ReadHexFixed(JsonObject json, string propertyName, int expectedLength)
        {
            var data = ReadHexActual(json, propertyName);
            if (data.Length == expectedLength)
                return data;

            var fixedData = new byte[expectedLength];
            Buffer.BlockCopy(data, 0, fixedData, 0, Math.Min(data.Length, expectedLength));
            return fixedData;
        }

        private static byte[] ReadHexActual(JsonObject json, string propertyName)
        {
            if (!json.TryGetPropertyValue(propertyName, out var node) || node == null)
                return Array.Empty<byte>();

            var hex = node.ToString();
            if (string.IsNullOrWhiteSpace(hex))
                return Array.Empty<byte>();

            var length = hex.Length / 2;
            var data = new byte[length];
            for (var index = 0; index < length; index++)
                data[index] = byte.Parse(hex.Substring(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return data;
        }

        internal static byte[] Copy(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<byte>();

            var copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);
            return copy;
        }

        internal static string ToHex(byte[] data)
        {
            return BitConverter.ToString(data ?? Array.Empty<byte>()).Replace("-", string.Empty);
        }
    }

    internal sealed class ItemExtraViewBuilder
    {
        public ItemExtraViewBuilder()
            : this(writeEquipment: true)
        {
        }

        private ItemExtraViewBuilder(bool writeEquipment)
        {
            Equipment = new EquipmentExtraViewBuilder();
            Avatar = new AvatarExtraViewBuilder();
            _writeEquipment = writeEquipment;
        }

        private readonly bool _writeEquipment;
        private bool _writeAvatar;

        public EquipmentExtraViewBuilder Equipment { get; }

        public AvatarExtraViewBuilder Avatar { get; }

        public static ItemExtraViewBuilder FromView(ItemExtraView view)
        {
            var builder = new ItemExtraViewBuilder();
            if (view != null)
                builder.Equipment.LoadFromView(view);
            return builder;
        }

        public static ItemExtraViewBuilder FromAvatarView(ItemExtraView view)
        {
            var builder = new ItemExtraViewBuilder(writeEquipment: false);
            builder._writeAvatar = true;
            if (view != null)
                builder.Avatar.LoadFromView(view);
            return builder;
        }

        public ItemExtraView Build()
        {
            var json = new JsonObject();
            if (_writeEquipment)
                Equipment.WriteTo(json);
            if (_writeAvatar)
                Avatar.WriteTo(json);
            return new ItemExtraView(json);
        }
    }

    internal sealed class EquipmentExtraViewBuilder
    {
        private byte[] _prefixData0E = new byte[8];
        private byte[] _middleData1A = Array.Empty<byte>();
        private byte[] _tailData2F = new byte[37];
        private byte[] _jewelSocket = Array.Empty<byte>();
        private bool _writeMiddleData1A;

        public byte ExtData0
        {
            get => (byte)((Upgrade & 0x1F) | ((ReSealCount & 0x07) << 5));
            set
            {
                Upgrade = (byte)(value & 0x1F);
                ReSealCount = (byte)((value >> 5) & 0x07);
            }
        }

        public byte Upgrade { get; set; }

        public byte ReSealCount { get; set; }

        public int EnchantCardId
        {
            get => BitConverter.ToInt32(_prefixData0E, 0);
            set => BitConverter.GetBytes(value).CopyTo(_prefixData0E, 0);
        }

        public byte EnchantUpgradeCount
        {
            get => _prefixData0E[4];
            set => _prefixData0E[4] = value;
        }

        public byte AmplifyType
        {
            get => _prefixData0E[5];
            set => _prefixData0E[5] = value;
        }

        public ushort AmplifyValue
        {
            get => BitConverter.ToUInt16(_prefixData0E, 6);
            set => BitConverter.GetBytes(value).CopyTo(_prefixData0E, 6);
        }

        public byte[] EmblemData
        {
            get
            {
                if (_tailData2F.Length == 0)
                    return Array.Empty<byte>();

                var length = 1 + _tailData2F[0] * 4;
                if (length > _tailData2F.Length)
                    return Array.Empty<byte>();

                var data = new byte[length];
                Buffer.BlockCopy(_tailData2F, 0, data, 0, data.Length);
                return data;
            }
            set
            {
                ClearRange(_tailData2F, 0, 9);
                var data = ItemExtraView.Copy(value);
                if (data.Length > 0)
                    Buffer.BlockCopy(data, 0, _tailData2F, 0, Math.Min(data.Length, 9));
            }
        }

        public ushort Rune
        {
            get => BitConverter.ToUInt16(_tailData2F, 9);
            set => BitConverter.GetBytes(value).CopyTo(_tailData2F, 9);
        }

        public byte SealCount
        {
            get => _tailData2F[11];
            set => _tailData2F[11] = value;
        }

        public byte[] SealTypes
        {
            get => CopyRange(_tailData2F, 12, 3);
            set => WriteRange(_tailData2F, 12, 3, value);
        }

        public byte[] SealVal1s
        {
            get => CopyRange(_tailData2F, 15, 3);
            set => WriteRange(_tailData2F, 15, 3, value);
        }

        public byte[] SealVal2s
        {
            get => CopyRange(_tailData2F, 18, 3);
            set => WriteRange(_tailData2F, 18, 3, value);
        }

        public byte[] SealTail
        {
            get
            {
                if (SealCount == 0 || _tailData2F.Length <= 21)
                    return Array.Empty<byte>();

                var length = 2;
                if (_tailData2F.Length > 22 && _tailData2F[22] != 0xFF)
                    length += 4;

                return CopyRange(_tailData2F, 21, Math.Min(length, _tailData2F.Length - 21));
            }
            set => WriteRange(_tailData2F, 21, _tailData2F.Length - 21, value);
        }

        public byte Forging
        {
            get => _tailData2F[27];
            set => _tailData2F[27] = value;
        }

        public byte[] TailData2F
        {
            get => ItemExtraView.Copy(_tailData2F);
            set => _tailData2F = FixedCopy(value, 37);
        }

        public byte[] MiddleData1A
        {
            get => ItemExtraView.Copy(_middleData1A);
            set
            {
                _middleData1A = ItemExtraView.Copy(value);
                _writeMiddleData1A = _middleData1A.Length > 0;
            }
        }

        public byte[] JewelSocket
        {
            get => ItemExtraView.Copy(_jewelSocket);
            set => _jewelSocket = ItemExtraView.Copy(value);
        }

        internal void LoadFromView(ItemExtraView view)
        {
            ExtData0 = view.Raw84.Attr;
            _prefixData0E = FixedCopy(view.Raw84.PrefixData0E, 8);
            _middleData1A = ItemExtraView.Copy(view.Raw84.MiddleData1A);
            _writeMiddleData1A = _middleData1A.Length > 0;
            _tailData2F = FixedCopy(view.Raw84.TailData2F, 37);
            _jewelSocket = ItemExtraView.Copy(view.Raw84.JewelSocket);
        }

        internal void WriteTo(JsonObject json)
        {
            json["extData0"] = ExtData0;
            json["prefixData0E"] = ItemExtraView.ToHex(_prefixData0E);
            if (_writeMiddleData1A)
                json["middleData1A"] = ItemExtraView.ToHex(_middleData1A);
            json["tailData2F"] = ItemExtraView.ToHex(_tailData2F);
            if (_jewelSocket.Length > 0)
                json["jewelSocket"] = ItemExtraView.ToHex(_jewelSocket);
        }

        private static byte[] FixedCopy(byte[] source, int length)
        {
            var data = new byte[length];
            if (source != null && source.Length > 0)
                Buffer.BlockCopy(source, 0, data, 0, Math.Min(source.Length, length));
            return data;
        }

        private static byte[] CopyRange(byte[] source, int offset, int length)
        {
            var data = new byte[length];
            if (source != null && source.Length > offset)
                Buffer.BlockCopy(source, offset, data, 0, Math.Min(length, source.Length - offset));
            return data;
        }

        private static void WriteRange(byte[] target, int offset, int length, byte[] value)
        {
            ClearRange(target, offset, length);
            var data = ItemExtraView.Copy(value);
            if (data.Length > 0)
                Buffer.BlockCopy(data, 0, target, offset, Math.Min(data.Length, length));
        }

        private static void ClearRange(byte[] target, int offset, int length)
        {
            if (target == null || length <= 0)
                return;

            Array.Clear(target, offset, Math.Min(length, target.Length - offset));
        }
    }

    internal sealed class AvatarExtraViewBuilder
    {
        private byte[] _reserved0 = new byte[5];
        private byte[] _reserved1 = new byte[71];
        private byte[] _reserved2 = new byte[30];
        private byte[] _tailData = new byte[7];

        public byte[] Reserved0
        {
            get => ItemExtraView.Copy(_reserved0);
            set => _reserved0 = FixedCopy(value, 5);
        }

        public byte[] Reserved1
        {
            get => ItemExtraView.Copy(_reserved1);
            set => _reserved1 = FixedCopy(value, 71);
        }

        public byte[] Reserved2
        {
            get => ItemExtraView.Copy(_reserved2);
            set => _reserved2 = FixedCopy(value, 30);
        }

        public ushort UnknownFixed4 { get; set; }

        public byte[] TailData
        {
            get => ItemExtraView.Copy(_tailData);
            set => _tailData = FixedCopy(value, 7);
        }

        internal void LoadFromView(ItemExtraView view)
        {
            Reserved0 = view.Avatar.Reserved0;
            Reserved1 = view.Avatar.Reserved1;
            Reserved2 = view.Avatar.Reserved2;
            UnknownFixed4 = view.Avatar.UnknownFixed4;
            TailData = view.Avatar.TailData;
        }

        internal void WriteTo(JsonObject json)
        {
            json["reserved0"] = ItemExtraView.ToHex(_reserved0);
            json["reserved1"] = ItemExtraView.ToHex(_reserved1);
            json["reserved2"] = ItemExtraView.ToHex(_reserved2);
            json["unknownFixed4"] = UnknownFixed4;
            json["tailData"] = ItemExtraView.ToHex(_tailData);
        }

        private static byte[] FixedCopy(byte[] source, int length)
        {
            var data = new byte[length];
            if (source != null && source.Length > 0)
                Buffer.BlockCopy(source, 0, data, 0, Math.Min(source.Length, length));
            return data;
        }
    }

    internal sealed class RawItemEntry84View
    {
        private readonly byte[] _prefixData0E;
        private readonly byte[] _middleData1A;
        private readonly byte[] _tailData2F;
        private readonly byte[] _jewelSocket;

        internal RawItemEntry84View(byte attr, byte[] prefixData0E, byte[] middleData1A, byte[] tailData2F, byte[] jewelSocket)
        {
            Attr = attr;
            _prefixData0E = ItemExtraView.Copy(prefixData0E);
            _middleData1A = ItemExtraView.Copy(middleData1A);
            _tailData2F = ItemExtraView.Copy(tailData2F);
            _jewelSocket = ItemExtraView.Copy(jewelSocket);
        }

        // 非装备路径是 attr；装备路径 bit[0..4] 是强化等级，bit[5..7] 是再封装次数。
        public byte Attr { get; }

        public byte[] PrefixData0E => ItemExtraView.Copy(_prefixData0E);

        public byte[] MiddleData1A => ItemExtraView.Copy(_middleData1A);

        public byte[] TailData2F => ItemExtraView.Copy(_tailData2F);

        public byte[] JewelSocket => ItemExtraView.Copy(_jewelSocket);
    }

    internal sealed class EquipmentExtraView
    {
        private readonly byte[] _prefixData0E;
        private readonly byte[] _middleData1A;
        private readonly byte[] _tailData2F;
        private readonly byte[] _jewelSocket;

        internal EquipmentExtraView(byte extData0, byte[] prefixData0E, byte[] middleData1A, byte[] tailData2F, byte[] jewelSocket)
        {
            ExtData0 = extData0;
            _prefixData0E = ItemExtraView.Copy(prefixData0E);
            _middleData1A = ItemExtraView.Copy(middleData1A);
            _tailData2F = ItemExtraView.Copy(tailData2F);
            _jewelSocket = ItemExtraView.Copy(jewelSocket);
            EnchantCardId = _prefixData0E.Length >= 4 ? BitConverter.ToInt32(_prefixData0E, 0) : 0;
            EnchantUpgradeCount = _prefixData0E.Length >= 5 ? _prefixData0E[4] : (byte)0;
            AmplifyType = _prefixData0E.Length >= 6 ? _prefixData0E[5] : (byte)0;
            AmplifyValue = _prefixData0E.Length >= 8 ? BitConverter.ToUInt16(_prefixData0E, 6) : (ushort)0;
            EmblemData = ParseEmblemData(_tailData2F);
            Rune = _tailData2F.Length >= 11 ? BitConverter.ToUInt16(_tailData2F, 9) : (ushort)0;
            SealCount = _tailData2F.Length > 11 ? _tailData2F[11] : (byte)0;
            SealTypes = ReadFixedSealBytes(_tailData2F, 12);
            SealVal1s = ReadFixedSealBytes(_tailData2F, 15);
            SealVal2s = ReadFixedSealBytes(_tailData2F, 18);
            SealTail = ParseSealTail(_tailData2F, SealCount);
            Forging = _tailData2F.Length > 27 ? _tailData2F[27] : (byte)0;
            ChronicleOptions = ParseChronicleOptions(_middleData1A);
            JewelSockets = ParseJewelSockets(_jewelSocket);
        }

        public byte ExtData0 { get; }

        public byte Upgrade => (byte)(ExtData0 & 0x1F);

        public byte ReSealCount => (byte)((ExtData0 >> 5) & 0x07);

        public int EnchantCardId { get; }

        public byte EnchantUpgradeCount { get; }

        public byte AmplifyType { get; }

        public ushort AmplifyValue { get; }

        public byte[] MiddleData1A => ItemExtraView.Copy(_middleData1A);

        public byte[] TailData2F => ItemExtraView.Copy(_tailData2F);

        public byte[] EmblemData { get; }

        public ushort Rune { get; }

        public byte SealCount { get; }

        public byte[] SealTypes { get; }

        public byte[] SealVal1s { get; }

        public byte[] SealVal2s { get; }

        public byte[] SealTail { get; }

        public byte Forging { get; }

        public byte[] JewelSocket => ItemExtraView.Copy(_jewelSocket);

        public IReadOnlyList<ChronicleOptionEntry> ChronicleOptions { get; }

        public IReadOnlyList<JewelSocketEntry> JewelSockets { get; }

        private static byte[] ParseEmblemData(byte[] tail)
        {
            if (tail == null || tail.Length == 0)
                return Array.Empty<byte>();

            var emblemLength = 1 + tail[0] * 4;
            if (emblemLength > tail.Length)
                return Array.Empty<byte>();

            var data = new byte[emblemLength];
            Buffer.BlockCopy(tail, 0, data, 0, data.Length);
            return data;
        }

        private static byte[] ReadFixedSealBytes(byte[] tail, int offset)
        {
            var data = new byte[3];
            if (tail == null)
                return data;

            for (var index = 0; index < data.Length && offset + index < tail.Length; index++)
                data[index] = tail[offset + index];
            return data;
        }

        private static byte[] ParseSealTail(byte[] tail, byte sealCount)
        {
            if (tail == null || sealCount == 0 || tail.Length <= 21)
                return Array.Empty<byte>();

            var length = 2;
            if (tail.Length > 22 && tail[22] != 0xFF)
                length += 4;

            length = Math.Min(length, tail.Length - 21);
            var data = new byte[length];
            Buffer.BlockCopy(tail, 21, data, 0, data.Length);
            return data;
        }

        private static IReadOnlyList<ChronicleOptionEntry> ParseChronicleOptions(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<ChronicleOptionEntry>();

            var count = Math.Min(data[0], (byte)2);
            var options = new List<ChronicleOptionEntry>(count);
            var offset = 1;
            for (var index = 0; index < count && offset + 8 <= data.Length; index++, offset += 8)
            {
                options.Add(new ChronicleOptionEntry(
                    BitConverter.ToInt32(data, offset),
                    data[offset + 4],
                    data[offset + 5],
                    data[offset + 6],
                    data[offset + 7]));
            }

            return options;
        }

        private static IReadOnlyList<JewelSocketEntry> ParseJewelSockets(byte[] data)
        {
            var sockets = new List<JewelSocketEntry>(5);
            for (var index = 0; index < 5; index++)
            {
                var offset = index * 6;
                if (data == null || offset + 6 > data.Length)
                {
                    sockets.Add(default);
                    continue;
                }

                sockets.Add(new JewelSocketEntry(
                    BitConverter.ToUInt16(data, offset),
                    BitConverter.ToUInt32(data, offset + 2)));
            }

            return sockets;
        }
    }

    internal readonly struct ChronicleOptionEntry
    {
        public ChronicleOptionEntry(int optionId, byte characJob, byte firstGrowType, byte equipmentType, byte optionNo)
        {
            OptionId = optionId;
            CharacJob = characJob;
            FirstGrowType = firstGrowType;
            EquipmentType = equipmentType;
            OptionNo = optionNo;
        }

        public int OptionId { get; }

        public byte CharacJob { get; }

        public byte FirstGrowType { get; }

        public byte EquipmentType { get; }

        public byte OptionNo { get; }
    }

    internal readonly struct JewelSocketEntry
    {
        public JewelSocketEntry(ushort socketType, uint emblemItemId)
        {
            SocketType = socketType;
            EmblemItemId = emblemItemId;
        }

        public ushort SocketType { get; }

        public uint EmblemItemId { get; }
    }

    internal sealed class AvatarExtraView
    {
        private readonly byte[] _reserved0;
        private readonly byte[] _reserved1;
        private readonly byte[] _reserved2;
        private readonly byte[] _tailData;

        internal AvatarExtraView(byte[] reserved0, byte[] reserved1, byte[] reserved2, ushort unknownFixed4, byte[] tailData)
        {
            _reserved0 = ItemExtraView.Copy(reserved0);
            _reserved1 = ItemExtraView.Copy(reserved1);
            _reserved2 = ItemExtraView.Copy(reserved2);
            UnknownFixed4 = unknownFixed4;
            _tailData = ItemExtraView.Copy(tailData);
        }

        public byte[] Reserved0 => ItemExtraView.Copy(_reserved0);

        public byte[] Reserved1 => ItemExtraView.Copy(_reserved1);

        public byte[] Reserved2 => ItemExtraView.Copy(_reserved2);

        public ushort UnknownFixed4 { get; }

        public byte[] TailData => ItemExtraView.Copy(_tailData);
    }

    internal sealed class PetExtraView
    {
        private readonly byte[] _tailData0A;

        internal PetExtraView(byte[] tailData0A)
        {
            _tailData0A = ItemExtraView.Copy(tailData0A);
        }

        public byte[] TailData0A => ItemExtraView.Copy(_tailData0A);
    }
}
