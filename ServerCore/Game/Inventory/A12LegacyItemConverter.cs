using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    /// <summary>
    /// Pure S4A12 item conversion helpers.
    ///
    /// This type deliberately has no database or filesystem code.  The old
    /// rows are materialised by the caller, converted here, and persisted by
    /// the A21 migration transaction.  ItemCore.ToBytes() is the only writer
    /// for the target blob; it therefore emits the current 99-byte shape and
    /// leaves the A21-only 17-byte tail at its safe default.
    /// </summary>
    internal static class A12LegacyItemConverter
    {
        internal const int A12ItemCoreSize = ItemCore.LegacySize;
        internal const int A21ItemCoreSize = ItemCore.Size;

        internal static byte[] ToA21Bytes(ItemCore core)
        {
            if (core == null)
                throw new ArgumentNullException(nameof(core));

            var bytes = core.ToBytes();
            if (bytes.Length != A21ItemCoreSize)
                throw new InvalidOperationException("当前 ItemCore.ToBytes() 未返回 99 字节。" );

            return bytes;
        }

        internal sealed class CharacterItemRow
        {
            public long ItemUid { get; set; }
            public string OwnerScope { get; set; }
            public int OwnerId { get; set; }
            public int CharacterId { get; set; }
            public InventoryListType ListType { get; set; }
            public short SlotIndex { get; set; }
            public int ItemTemplateId { get; set; }
            public string ItemKindText { get; set; }
            public int StackCount { get; set; }
            public int InstanceValue { get; set; }
            public ushort Durability { get; set; }
            public byte SealFlag { get; set; }
            public byte OptionValue { get; set; }
            public byte EquipmentLockId { get; set; }
            public int ExpireTime { get; set; }
            public int Marker16 { get; set; }
            public int PetSerialOrHandle { get; set; }
            public string ExtraJson { get; set; }
            public string CreatedAt { get; set; }
            public string UpdatedAt { get; set; }

            internal static CharacterItemRow FromDictionary(IReadOnlyDictionary<string, object> row)
            {
                if (row == null)
                    throw new ArgumentNullException(nameof(row));

                return new CharacterItemRow
                {
                    ItemUid = ReadInt64(row, "item_uid"),
                    OwnerScope = ReadString(row, "owner_scope"),
                    OwnerId = ReadInt32(row, "owner_id"),
                    CharacterId = ReadInt32(row, "character_id"),
                    ListType = (InventoryListType)ReadInt32(row, "list_type"),
                    SlotIndex = ToInt16(ReadInt32(row, "slot_index")),
                    ItemTemplateId = ReadInt32(row, "item_template_id"),
                    ItemKindText = ReadString(row, "item_kind"),
                    StackCount = ReadInt32(row, "stack_count"),
                    InstanceValue = ReadInt32(row, "instance_value"),
                    Durability = ToUInt16(ReadInt32(row, "durability")),
                    SealFlag = ToByte(ReadInt32(row, "seal_flag")),
                    OptionValue = ToByte(ReadInt32(row, "option_value")),
                    EquipmentLockId = ToByte(ReadInt32(row, "equipment_lock_id")),
                    ExpireTime = ReadInt32(row, "expire_time"),
                    Marker16 = ReadInt32(row, "marker_16"),
                    PetSerialOrHandle = ReadInt32(row, "pet_serial_or_handle"),
                    ExtraJson = ReadString(row, "extra_json", "{}"),
                    CreatedAt = ReadString(row, "created_at"),
                    UpdatedAt = ReadString(row, "updated_at"),
                };
            }
        }

        internal sealed class AccountCargoItemRow
        {
            public long ItemUid { get; set; }
            public int AccountId { get; set; }
            public int CharacterId { get; set; }
            public short SlotIndex { get; set; }
            public int ItemTemplateId { get; set; }
            public string ItemKindText { get; set; }
            public int StackCount { get; set; }
            public int InstanceValue { get; set; }
            public ushort Durability { get; set; }
            public byte SealFlag { get; set; }
            public byte OptionValue { get; set; }
            public int ExpireTime { get; set; }
            public int Marker16 { get; set; }
            public byte EquipmentLockId { get; set; }
            public string ExtraJson { get; set; }
            public string CreatedAt { get; set; }
            public string UpdatedAt { get; set; }

            internal static AccountCargoItemRow FromDictionary(IReadOnlyDictionary<string, object> row)
            {
                if (row == null)
                    throw new ArgumentNullException(nameof(row));

                return new AccountCargoItemRow
                {
                    ItemUid = ReadInt64(row, "item_uid"),
                    AccountId = ReadInt32(row, "account_id"),
                    CharacterId = ReadInt32(row, "character_id"),
                    SlotIndex = ToInt16(ReadInt32(row, "slot_index")),
                    ItemTemplateId = ReadInt32(row, "item_template_id"),
                    ItemKindText = ReadString(row, "item_kind"),
                    StackCount = ReadInt32(row, "stack_count"),
                    InstanceValue = ReadInt32(row, "instance_value"),
                    Durability = ToUInt16(ReadInt32(row, "durability")),
                    SealFlag = ToByte(ReadInt32(row, "seal_flag")),
                    OptionValue = ToByte(ReadInt32(row, "option_value")),
                    ExpireTime = ReadInt32(row, "expire_time"),
                    Marker16 = ReadInt32(row, "marker_16"),
                    EquipmentLockId = ToByte(ReadInt32(row, "equipment_lock_id")),
                    ExtraJson = ReadString(row, "extra_json", "{}"),
                    CreatedAt = ReadString(row, "created_at"),
                    UpdatedAt = ReadString(row, "updated_at"),
                };
            }
        }

        internal sealed class EquippedEntryRow
        {
            public int CharacterId { get; set; }
            public short SlotIndex { get; set; }
            public int ItemTemplateId { get; set; }
            public int ExpireTime { get; set; }
            public byte EquipmentLockId { get; set; }
            public byte[] RawEntry { get; set; } = Array.Empty<byte>();

            internal static EquippedEntryRow FromDictionary(IReadOnlyDictionary<string, object> row)
            {
                if (row == null)
                    throw new ArgumentNullException(nameof(row));

                return new EquippedEntryRow
                {
                    CharacterId = ReadInt32(row, "character_id"),
                    SlotIndex = ToInt16(ReadInt32(row, "slot")),
                    ItemTemplateId = ReadInt32(row, "item_id"),
                    ExpireTime = ReadInt32(row, "expire_time"),
                    EquipmentLockId = ToByte(ReadInt32(row, "equipment_lock_id")),
                    RawEntry = ReadBlob(row, "raw_entry"),
                };
            }
        }

        /// <summary>
        /// All byte slices that were stored in A12 extra_json.  Keeping the
        /// raw slices in the result lets the caller persist avatar details or
        /// produce an audit record without re-parsing the JSON.
        /// </summary>
        internal sealed class LegacyExtraData
        {
            private LegacyExtraData(JsonObject json)
            {
                ExtData0 = ReadJsonByte(json, "extData0");
                PrefixData0E = ReadHexFixed(json, "prefixData0E", 8);
                MiddleData1A = ReadHexFixed(json, "middleData1A", 17);
                TailData2F = ReadHexFixed(json, "tailData2F", 37);
                AvatarReserved0 = ReadHexFixed(json, "reserved0", 5);
                AvatarReserved1 = ReadHexFixed(json, "reserved1", 71);
                AvatarReserved2 = ReadHexFixed(json, "reserved2", AvatarSocketDataCodec.Length);
                AvatarSocketData = AvatarSocketDataCodec.Normalize(AvatarReserved2);
                AvatarTailData = ReadHexFixed(json, "tailData", 7);
                PetTailData0A = ReadHexFixed(json, "tailData0A", 74);
                ClearAvatarId = ReadFirstJsonInt(json, "clearAvatarId", "clear_avatar_id", "clearAvatar");
                UnknownFixed4 = ToUInt16(ReadFirstJsonInt(json, "unknownFixed4"));
            }

            public byte ExtData0 { get; }
            public byte[] PrefixData0E { get; }
            public byte[] MiddleData1A { get; }
            public byte[] TailData2F { get; }
            public byte[] AvatarReserved0 { get; }
            public byte[] AvatarReserved1 { get; }
            public byte[] AvatarReserved2 { get; }
            public byte[] AvatarSocketData { get; }
            public byte[] AvatarTailData { get; }
            public byte[] PetTailData0A { get; }
            public int ClearAvatarId { get; }
            public ushort UnknownFixed4 { get; }

            internal static LegacyExtraData Parse(string extraJson)
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

                return new LegacyExtraData(json ?? new JsonObject());
            }
        }

        /// <summary>
        /// Avatar detail returned by the conversion.  The normalized fields
        /// map directly to A21 character_avatar_detail; the reserved fields
        /// remain available for diagnostics and lossless migration reports.
        /// </summary>
        internal sealed class AvatarDetailData
        {
            public long AvatarUid { get; set; }
            public int OwnerId { get; set; }
            public int CharacterId { get; set; }
            public int ItemId { get; set; }
            public int ExpireDate { get; set; }
            public int ClearAvatarId { get; set; }
            public byte[] JewelSocket { get; set; } = new byte[AvatarSocketDataCodec.Length];
            public ushort Color1 { get; set; }
            public ushort Color2 { get; set; }
            public int DeleteDate { get; set; }
            public byte[] Reserved0 { get; set; } = new byte[5];
            public byte[] Reserved1 { get; set; } = new byte[71];
            public byte[] Reserved2 { get; set; } = new byte[AvatarSocketDataCodec.Length];
            public byte[] TailData { get; set; } = new byte[7];
            public ushort UnknownFixed4 { get; set; }

            internal AvatarDetail ToAvatarDetail()
            {
                return new AvatarDetail
                {
                    AvatarUid = AvatarUid,
                    OwnerId = OwnerId,
                    CharacterId = CharacterId,
                    ItemId = ItemId,
                    ExpireDate = ExpireDate,
                    ClearAvatarId = ClearAvatarId,
                    JewelSocket = AvatarSocketDataCodec.Normalize(JewelSocket),
                    Color1 = Color1,
                    Color2 = Color2,
                    DeleteDate = DeleteDate,
                };
            }
        }

        /// <summary>
        /// The source pet key/handle and the value that A21 will use as
        /// character_creatures.creature_key.  It is returned separately so
        /// the migration service can map a colliding key without changing the
        /// item conversion itself.
        /// </summary>
        internal sealed class PetLegacyAssociation
        {
            public int CharacterId { get; set; }
            public long ItemUid { get; set; }
            public short SlotIndex { get; set; }
            public int ItemId { get; set; }
            public int SourcePetSerialOrHandle { get; set; }
            public int A21CreatureKey { get; set; }
            public int CreatureExtra { get; set; }
            public byte ItemKind { get; set; }
            public bool IsEquipped { get; set; }

            public bool IsCreatureInstance
            {
                get => ItemKind == ItemCore.KindCreature;
            }
        }

        internal static ItemCore BuildCoreFromCharacterItem(CharacterItemRow row)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            if (row.ListType == InventoryListType.Main && IsMainVirtualCurrencySlot(row.SlotIndex))
                return BuildMainVirtualCurrencyCore(row.SlotIndex, Math.Max(row.StackCount, row.InstanceValue));

            var payload = LegacyExtraData.Parse(row.ExtraJson);
            var itemKind = ResolveItemKind(row);
            var core = ItemCore.Create(itemKind, row.ItemTemplateId);
            core.EquipmentLockId = row.EquipmentLockId;

            if (itemKind == ItemCore.KindAvatar)
            {
                ApplyAvatarCharacterItemPayload(core, row, payload);
                return core;
            }

            if (row.ListType == InventoryListType.Pet)
            {
                ApplyPetCharacterItemPayload(core, row, payload);
                return core;
            }

            ApplyCommonCharacterItemPayload(core, row, payload);
            return core;
        }

        internal static bool TryBuildCoreFromCharacterItem(
            CharacterItemRow row,
            out ItemCore core,
            out string reason)
        {
            core = null;
            reason = null;
            if (row == null)
            {
                reason = "row_null";
                return false;
            }

            try
            {
                core = BuildCoreFromCharacterItem(row);
                if (core.ItemKind == ItemCore.KindUnknown)
                {
                    reason = "item_kind_unknown";
                    core = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "item_convert_failed:" + ex.GetType().Name;
                core = null;
                return false;
            }
        }

        internal static ItemCore BuildCoreFromAccountCargoItem(AccountCargoItemRow row)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            var payload = LegacyExtraData.Parse(row.ExtraJson);
            var itemKind = ResolveAccountCargoItemKind(row);
            var core = ItemCore.Create(itemKind, row.ItemTemplateId);
            core.EquipmentLockId = row.EquipmentLockId;
            ApplyAccountCargoItemPayload(core, row, payload);
            return core;
        }

        internal static bool TryBuildCoreFromAccountCargoItem(
            AccountCargoItemRow row,
            out ItemCore core,
            out string reason)
        {
            core = null;
            reason = null;
            if (row == null)
            {
                reason = "row_null";
                return false;
            }

            try
            {
                core = BuildCoreFromAccountCargoItem(row);
                if (core.ItemKind == ItemCore.KindUnknown)
                {
                    reason = "item_kind_unknown";
                    core = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "item_convert_failed:" + ex.GetType().Name;
                core = null;
                return false;
            }
        }

        internal static ItemCore BuildCoreFromEquippedEntry(
            EquippedEntryRow row,
            byte itemKind,
            MakeEquipListCodec.DisplayFields fields)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            var core = ItemCore.Create(itemKind, row.ItemTemplateId);
            core.Value = unchecked((int)fields.InstanceValue);
            core.Attr = fields.Reinforce;
            core.Durability = fields.Durability;
            core.SealFlag = fields.SealFlag;
            core.EnchantCardId = unchecked((int)fields.Enchant);
            core.EnchantUpgradeCount = fields.EnchantUpgradeCount;
            core.AmplifyType = fields.AmplifyType;
            core.AmplifyValue = fields.AmplifyValue;
            core.ExpireTime = row.ExpireTime != 0 ? row.ExpireTime : fields.ExpireTime;
            core.EquipmentLockId = row.EquipmentLockId;

            if (itemKind == ItemCore.KindCreature)
                core.Marker16 = fields.Marker16 == 0 ? ItemCore.Marker16Default : unchecked((int)fields.Marker16);

            ApplyChronicleOptions(core, fields.ChronicleOptions);
            core.EmblemSocketCount = fields.EmblemSocketCount;
            core.EmblemId1 = fields.EmblemId1;
            core.EmblemId2 = fields.EmblemId2;
            core.Rune = fields.Rune;
            ApplyRandomOptions(core, fields);
            core.RandomOptionState = fields.RandomOptionState;
            core.RandomOptionChangedIndex = fields.RandomOptionChangedIndex;
            core.RandomOptionChangeState = fields.RandomOptionChangeState;
            core.RandomOptionChange.Type = fields.RandomOptionChangeType;
            core.RandomOptionChange.Value1 = fields.RandomOptionChangeValue1;
            core.RandomOptionChange.Value2 = fields.RandomOptionChangeValue2;
            core.GenuineUpgrade = fields.Forging;
            core.EmancipateEquipmentLevel = fields.EmancipateEquipmentLevel;
            core.TradeRestriction = fields.TradeRestriction;
            core.TailUnknown0 = fields.TailUnknown0;
            core.TailUnknown1 = fields.TailUnknown1;
            core.TailUnknown2 = fields.TailUnknown2;
            core.TailUnknown3 = fields.TailUnknown3;
            core.RemainUseCount = fields.RemainUseCount;
            core.SortLockFlag = fields.SortLockFlag;
            return core;
        }

        internal static bool TryBuildCoreFromEquippedEntry(
            EquippedEntryRow row,
            out ItemCore core,
            out MakeEquipListCodec.DisplayFields fields,
            out string reason)
        {
            core = null;
            fields = default(MakeEquipListCodec.DisplayFields);
            reason = null;
            if (row == null)
            {
                reason = "row_null";
                return false;
            }

            if (!TryResolveEquippedItemKind(row.SlotIndex, out var itemKind))
            {
                reason = "equipped_slot_unknown";
                return false;
            }

            if (row.ItemTemplateId <= 0)
            {
                reason = "item_id_invalid";
                return false;
            }

            try
            {
                fields = MakeEquipListCodec.ParseDisplayFields(row.RawEntry ?? Array.Empty<byte>());
                core = BuildCoreFromEquippedEntry(row, itemKind, fields);
                return true;
            }
            catch (Exception ex)
            {
                reason = "equipped_convert_failed:" + ex.GetType().Name;
                core = null;
                return false;
            }
        }

        internal static AvatarDetailData BuildAvatarDetailData(
            CharacterItemRow row,
            ItemCore core,
            long avatarUid)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            if (core == null)
                throw new ArgumentNullException(nameof(core));

            var payload = LegacyExtraData.Parse(row.ExtraJson);
            return new AvatarDetailData
            {
                AvatarUid = avatarUid,
                OwnerId = row.OwnerId,
                CharacterId = row.CharacterId,
                ItemId = row.ItemTemplateId,
                ExpireDate = row.ExpireTime != 0 ? row.ExpireTime : core.ExpireTime,
                ClearAvatarId = payload.ClearAvatarId,
                JewelSocket = AvatarSocketDataCodec.Normalize(payload.AvatarSocketData),
                Color1 = ReadUInt16(payload.AvatarTailData, 0),
                Color2 = ReadUInt16(payload.AvatarTailData, 2),
                Reserved0 = CopyFixed(payload.AvatarReserved0, 5),
                Reserved1 = CopyFixed(payload.AvatarReserved1, 71),
                Reserved2 = CopyFixed(payload.AvatarReserved2, AvatarSocketDataCodec.Length),
                TailData = CopyFixed(payload.AvatarTailData, 7),
                UnknownFixed4 = payload.UnknownFixed4,
            };
        }

        internal static AvatarDetail BuildAvatarDetail(
            CharacterItemRow row,
            ItemCore core,
            long avatarUid)
        {
            return BuildAvatarDetailData(row, core, avatarUid).ToAvatarDetail();
        }

        internal static AvatarDetailData BuildAvatarDetailData(
            EquippedEntryRow row,
            MakeEquipListCodec.DisplayFields fields,
            long avatarUid)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            var colorData = CopyFixed(fields.ExpansionData, 4);
            return new AvatarDetailData
            {
                AvatarUid = avatarUid,
                OwnerId = row.CharacterId,
                CharacterId = row.CharacterId,
                ItemId = row.ItemTemplateId,
                ExpireDate = row.ExpireTime != 0 ? row.ExpireTime : fields.ExpireTime,
                ClearAvatarId = unchecked((int)fields.ClearAvatarId),
                JewelSocket = AvatarSocketDataCodec.Normalize(fields.JewelSocket),
                Color1 = ReadUInt16(colorData, 0),
                Color2 = ReadUInt16(colorData, 2),
                Reserved2 = CopyFixed(fields.JewelSocket, AvatarSocketDataCodec.Length),
            };
        }

        internal static AvatarDetail BuildAvatarDetail(
            EquippedEntryRow row,
            MakeEquipListCodec.DisplayFields fields,
            long avatarUid)
        {
            return BuildAvatarDetailData(row, fields, avatarUid).ToAvatarDetail();
        }

        internal static PetLegacyAssociation BuildPetAssociation(
            CharacterItemRow row,
            ItemCore core)
        {
            if (row == null || core == null || row.ListType != InventoryListType.Pet)
                return null;

            return new PetLegacyAssociation
            {
                CharacterId = row.CharacterId,
                ItemUid = row.ItemUid,
                SlotIndex = row.SlotIndex,
                ItemId = row.ItemTemplateId,
                SourcePetSerialOrHandle = row.PetSerialOrHandle,
                A21CreatureKey = core.CreatureUid,
                CreatureExtra = core.Marker16,
                ItemKind = core.ItemKind,
                IsEquipped = false,
            };
        }

        internal static PetLegacyAssociation BuildPetAssociation(
            EquippedEntryRow row,
            ItemCore core,
            MakeEquipListCodec.DisplayFields fields)
        {
            if (row == null || core == null || core.ItemKind != ItemCore.KindCreature)
                return null;

            return new PetLegacyAssociation
            {
                CharacterId = row.CharacterId,
                ItemUid = 0,
                SlotIndex = row.SlotIndex,
                ItemId = row.ItemTemplateId,
                SourcePetSerialOrHandle = unchecked((int)fields.InstanceValue),
                A21CreatureKey = core.CreatureUid,
                CreatureExtra = unchecked((int)fields.Marker16),
                ItemKind = core.ItemKind,
                IsEquipped = true,
            };
        }

        internal static bool TryResolveEquippedItemKind(short slotIndex, out byte itemKind)
        {
            if (slotIndex >= 0 && slotIndex <= 10)
            {
                itemKind = ItemCore.KindAvatar;
                return true;
            }

            if ((slotIndex >= 11 && slotIndex <= 23) || slotIndex == 29)
            {
                itemKind = ItemCore.KindEquipment;
                return true;
            }

            if (slotIndex == 24)
            {
                itemKind = ItemCore.KindCreature;
                return true;
            }

            if (slotIndex >= 25 && slotIndex <= 27)
            {
                itemKind = ItemCore.KindCreatureEquipment;
                return true;
            }

            itemKind = ItemCore.KindUnknown;
            return false;
        }

        internal static IEnumerable<TitleBookItem> EnumerateTitleBookBlob(byte[] blob)
        {
            if (blob == null)
                yield break;

            for (var offset = 0; offset + LegacyTitleBookCoreCodec.RecordSize <= blob.Length; offset += LegacyTitleBookCoreCodec.RecordSize)
            {
                var core = LegacyTitleBookCoreCodec.DecodeRecord(blob, offset);
                if (core == null || core.IsEmpty)
                    continue;

                yield return new TitleBookItem
                {
                    SlotIndex = BitConverter.ToInt16(blob, offset),
                    Core = core,
                };
            }
        }

        internal sealed class TitleBookItem
        {
            public short SlotIndex { get; set; }
            public ItemCore Core { get; set; }
        }

        private static ItemCore BuildMainVirtualCurrencyCore(short slotIndex, int count)
        {
            return new ItemCore
            {
                ItemKind = ItemCore.KindSpecialMaterial,
                ItemId = slotIndex,
                Count = Math.Max(0, count),
            };
        }

        private static void ApplyCommonCharacterItemPayload(ItemCore core, CharacterItemRow row, LegacyExtraData payload)
        {
            core.Value = IsStackCountItemKind(core.ItemKind) ? row.StackCount : row.InstanceValue;
            core.Attr = payload.ExtData0;
            core.Durability = row.Durability;
            core.SealFlag = row.SealFlag;
            ApplyPrefixData(core, payload.PrefixData0E);
            core.Marker16 = NormalizeMarker16(row.Marker16);
            ApplyMiddleData(core, payload.MiddleData1A);
            core.ExpireTime = row.ExpireTime;
            ApplyTailData(core, payload.TailData2F);
        }

        private static void ApplyAccountCargoItemPayload(ItemCore core, AccountCargoItemRow row, LegacyExtraData payload)
        {
            core.Value = IsStackCountItemKind(core.ItemKind) ? row.StackCount : row.InstanceValue;
            core.Attr = payload.ExtData0;
            core.Durability = row.Durability;
            core.SealFlag = row.SealFlag;
            ApplyPrefixData(core, payload.PrefixData0E);
            core.Marker16 = NormalizeMarker16(row.Marker16);
            ApplyMiddleData(core, payload.MiddleData1A);
            core.ExpireTime = row.ExpireTime;
            ApplyTailData(core, payload.TailData2F);
        }

        private static void ApplyAvatarCharacterItemPayload(ItemCore core, CharacterItemRow row, LegacyExtraData payload)
        {
            core.AvatarUid = 0;
            core.Attr = ReadByte(payload.AvatarReserved0, 4);
            core.Durability = ToUInt16(row.OptionValue | (ReadByte(payload.AvatarReserved1, 0) << 8));
            core.SealFlag = ReadByte(payload.AvatarReserved1, 1);
            ApplyPrefixData(core, payload.AvatarReserved1, 2);
            core.Marker16 = NormalizeMarker16(ReadInt32(payload.AvatarReserved1, 10));
            ApplyMiddleData(core, payload.AvatarReserved1, 14);
            core.ExpireTime = ReadInt32(payload.AvatarReserved1, 31);
            ApplyTailData(core, payload.AvatarReserved1, 35);
            core.SortLockFlag = ToByte(row.Marker16 & 0xFF);
        }

        private static void ApplyPetCharacterItemPayload(ItemCore core, CharacterItemRow row, LegacyExtraData payload)
        {
            core.Value = row.PetSerialOrHandle;
            core.Attr = ReadByte(payload.PetTailData0A, 0);
            core.Durability = ReadUInt16(payload.PetTailData0A, 1);
            core.SealFlag = ReadByte(payload.PetTailData0A, 3);
            ApplyPrefixData(core, payload.PetTailData0A, 4);
            core.Marker16 = NormalizeMarker16(ReadInt32(payload.PetTailData0A, 12));
            ApplyMiddleData(core, payload.PetTailData0A, 16);
            core.ExpireTime = ReadInt32(payload.PetTailData0A, 33);
            ApplyTailData(core, payload.PetTailData0A, 37);
        }

        private static byte ResolveItemKind(CharacterItemRow row)
        {
            // Kind resolution is intentionally local to the source row.  PVF
            // recognition is a migration-service policy; this pure converter
            // must not call the removed S4A12 slot-bound service.
            return ResolveLegacyItemKind(row.ItemKindText, row.ListType, row.SlotIndex);
        }

        private static byte ResolveAccountCargoItemKind(AccountCargoItemRow row)
        {
            return ResolveLegacyItemKind(row.ItemKindText, InventoryListType.AccountCargo, row.SlotIndex);
        }

        private static byte ResolveLegacyItemKind(string itemKindText, InventoryListType listType, short slot)
        {
            switch ((itemKindText ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "equipment": return ItemCore.KindEquipment;
                case "avatar": return ItemCore.KindAvatar;
                case "pet":
                case "creature": return ItemCore.KindCreature;
                case "pet-equipment":
                case "creature-equipment": return ItemCore.KindCreatureEquipment;
                case "pet-consumable":
                case "creature-consumable": return ItemCore.KindCreatureConsumable;
                case "stackable":
                case "consumable": return ItemCore.KindConsumable;
                case "material": return ItemCore.KindMaterial;
                case "quest": return ItemCore.KindQuest;
                case "expert-material":
                case "expert-job-material": return ItemCore.KindExpertJobMaterial;
                case "avatar-emblem": return ItemCore.KindAvatarEmblem;
                case "special":
                case "special-material": return ItemCore.KindSpecialMaterial;
                case "guild-medal": return ItemCore.KindGuildMedal;
                case "guardian-gem": return ItemCore.KindGuardianGem;
                case "epic-piece": return ItemCore.KindEpicPiece;
            }

            if (listType == InventoryListType.Pet)
            {
                if (slot <= 139) return ItemCore.KindCreature;
                if (slot <= 188) return ItemCore.KindCreatureEquipment;
                if (slot <= 239) return ItemCore.KindCreatureConsumable;
            }

            if (listType == InventoryListType.Avatar)
                return ItemCore.KindAvatar;

            if (listType == InventoryListType.Main)
            {
                if (slot >= 0 && slot <= 2) return ItemCore.KindSpecialMaterial;
                if (slot >= 9 && slot <= 64) return ItemCore.KindEquipment;
                if (slot >= 65 && slot <= 120) return ItemCore.KindConsumable;
                if (slot >= 121 && slot <= 176) return ItemCore.KindMaterial;
                if (slot >= 177 && slot <= 232) return ItemCore.KindQuest;
                if (slot >= 233 && slot <= 288) return ItemCore.KindExpertJobMaterial;
                if (slot >= 289 && slot <= 351) return ItemCore.KindAvatarEmblem;
            }

            return ItemCore.KindUnknown;
        }

        private static bool IsStackCountItemKind(byte itemKind)
        {
            return itemKind == ItemCore.KindConsumable
                || itemKind == ItemCore.KindMaterial
                || itemKind == ItemCore.KindQuest
                || itemKind == ItemCore.KindCreatureConsumable
                || itemKind == ItemCore.KindAvatarEmblem
                || itemKind == ItemCore.KindExpertJobMaterial
                || itemKind == ItemCore.KindSpecialMaterial;
        }

        private static bool IsMainVirtualCurrencySlot(short slotIndex)
        {
            return slotIndex >= 0 && slotIndex <= 2;
        }

        private static void ApplyPrefixData(ItemCore core, byte[] data, int offset = 0)
        {
            core.EnchantCardId = ReadInt32(data, offset);
            core.EnchantUpgradeCount = ReadByte(data, offset + 4);
            core.AmplifyType = ReadByte(data, offset + 5);
            core.AmplifyValue = ReadUInt16(data, offset + 6);
        }

        private static void ApplyMiddleData(ItemCore core, byte[] data, int offset = 0)
        {
            var optionCount = ReadByte(data, offset);
            if (optionCount > 0 || ReadInt32(data, offset + 1) != 0)
            {
                core.ChronicleOption0.OptionId = ReadInt32(data, offset + 1);
                core.ChronicleOption0.CharacJob = ReadByte(data, offset + 9);
                core.ChronicleOption0.FirstGrowType = ReadByte(data, offset + 11);
                core.ChronicleOption0.EquipmentType = ReadByte(data, offset + 13);
                core.ChronicleOption0.OptionNo = ReadByte(data, offset + 15);
            }

            if (optionCount > 1 || ReadInt32(data, offset + 5) != 0)
            {
                core.ChronicleOption1.OptionId = ReadInt32(data, offset + 5);
                core.ChronicleOption1.CharacJob = ReadByte(data, offset + 10);
                core.ChronicleOption1.FirstGrowType = ReadByte(data, offset + 12);
                core.ChronicleOption1.EquipmentType = ReadByte(data, offset + 14);
                core.ChronicleOption1.OptionNo = ReadByte(data, offset + 16);
            }
        }

        private static void ApplyTailData(ItemCore core, byte[] data, int offset = 0)
        {
            core.EmblemSocketCount = ReadByte(data, offset);
            core.EmblemId1 = ReadInt32(data, offset + 1);
            core.EmblemId2 = ReadInt32(data, offset + 5);
            core.Rune = ReadUInt16(data, offset + 9);
            core.RandomOption0.Type = ReadByte(data, offset + 12);
            core.RandomOption1.Type = ReadByte(data, offset + 13);
            core.RandomOption2.Type = ReadByte(data, offset + 14);
            core.RandomOption0.Value1 = ReadByte(data, offset + 15);
            core.RandomOption1.Value1 = ReadByte(data, offset + 16);
            core.RandomOption2.Value1 = ReadByte(data, offset + 17);
            core.RandomOption0.Value2 = ReadByte(data, offset + 18);
            core.RandomOption1.Value2 = ReadByte(data, offset + 19);
            core.RandomOption2.Value2 = ReadByte(data, offset + 20);
            core.RandomOptionState = ReadByte(data, offset + 21);
            core.RandomOptionChangedIndex = ReadByte(data, offset + 22, ItemCore.RandomOptionChangedIndexDefault);
            core.RandomOptionChangeState = ReadByte(data, offset + 23);
            core.RandomOptionChange.Type = ReadByte(data, offset + 24);
            core.RandomOptionChange.Value1 = ReadByte(data, offset + 25);
            core.RandomOptionChange.Value2 = ReadByte(data, offset + 26);
            core.GenuineUpgrade = ReadByte(data, offset + 27);
            core.EmancipateEquipmentLevel = ReadByte(data, offset + 28);
            core.TradeRestriction = ReadByte(data, offset + 29);
            core.TailUnknown0 = ReadUInt16(data, offset + 30);
            core.TailUnknown1 = ReadByte(data, offset + 32);
            core.TailUnknown2 = ReadByte(data, offset + 33);
            core.TailUnknown3 = ReadByte(data, offset + 34);
            core.RemainUseCount = ReadByte(data, offset + 35);
            core.SortLockFlag = ReadByte(data, offset + 36);
        }

        private static void ApplyChronicleOptions(ItemCore core, MakeEquipListCodec.ChronicleOptionFields[] fields)
        {
            if (fields == null)
                return;

            var options = new List<ChronicleOption>(Math.Min(fields.Length, 2));
            for (var index = 0; index < fields.Length && index < 2; index++)
            {
                options.Add(new ChronicleOption
                {
                    OptionId = fields[index].OptionId,
                    CharacJob = fields[index].CharacJob,
                    FirstGrowType = fields[index].FirstGrowType,
                    EquipmentType = fields[index].EquipmentType,
                    OptionNo = fields[index].OptionNo,
                });
            }

            core.SetChronicleOptions(options);
        }

        private static void ApplyRandomOptions(ItemCore core, MakeEquipListCodec.DisplayFields fields)
        {
            var options = new List<RandomOption>(3);
            for (var index = 0; index < 3; index++)
            {
                options.Add(new RandomOption
                {
                    Type = ReadArrayByte(fields.MagicSealTypes, index),
                    Value1 = ReadArrayByte(fields.MagicSealVal1s, index),
                    Value2 = ReadArrayByte(fields.MagicSealVal2s, index),
                });
            }

            core.SetRandomOptions(options);
        }

        private static int NormalizeMarker16(int value)
        {
            return value == 0 ? ItemCore.Marker16Default : value;
        }

        private static byte ReadArrayByte(byte[] data, int index)
        {
            return data != null && index >= 0 && index < data.Length ? data[index] : (byte)0;
        }

        private static byte ReadByte(byte[] data, int offset, byte defaultValue = 0)
        {
            return data != null && offset >= 0 && offset < data.Length ? data[offset] : defaultValue;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset + 1 >= data.Length)
                return 0;

            return BitConverter.ToUInt16(data, offset);
        }

        private static int ReadInt32(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset + 3 >= data.Length)
                return 0;

            return BitConverter.ToInt32(data, offset);
        }

        private static byte[] CopyFixed(byte[] data, int expectedLength)
        {
            var result = new byte[expectedLength];
            if (data == null || data.Length == 0)
                return result;

            Buffer.BlockCopy(data, 0, result, 0, Math.Min(data.Length, expectedLength));
            return result;
        }

        private static byte ReadJsonByte(JsonObject json, string propertyName)
        {
            return ToByte(ReadJsonInt(json, propertyName));
        }

        private static int ReadFirstJsonInt(JsonObject json, params string[] propertyNames)
        {
            if (propertyNames == null)
                return 0;

            foreach (var propertyName in propertyNames)
            {
                var value = ReadJsonInt(json, propertyName);
                if (value != 0)
                    return value;
            }

            return 0;
        }

        private static int ReadJsonInt(JsonObject json, string propertyName)
        {
            if (json == null || string.IsNullOrEmpty(propertyName)
                || !json.TryGetPropertyValue(propertyName, out var node) || node == null)
                return 0;

            return int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        private static byte[] ReadHexFixed(JsonObject json, string propertyName, int expectedLength)
        {
            return CopyFixed(ReadHexActual(json, propertyName), expectedLength);
        }

        private static byte[] ReadHexActual(JsonObject json, string propertyName)
        {
            if (json == null || string.IsNullOrEmpty(propertyName)
                || !json.TryGetPropertyValue(propertyName, out var node) || node == null)
                return Array.Empty<byte>();

            var hex = node.ToString();
            if (string.IsNullOrWhiteSpace(hex) || (hex.Length & 1) != 0)
                return Array.Empty<byte>();

            var data = new byte[hex.Length / 2];
            for (var index = 0; index < data.Length; index++)
            {
                if (!byte.TryParse(hex.Substring(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out data[index]))
                    return Array.Empty<byte>();
            }

            return data;
        }

        private static object ReadValue(IReadOnlyDictionary<string, object> row, string key)
        {
            return row != null && key != null && row.TryGetValue(key, out var value) ? value : null;
        }

        private static int ReadInt32(IReadOnlyDictionary<string, object> row, string key)
        {
            var value = ReadValue(row, key);
            if (value == null || value == DBNull.Value)
                return 0;

            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static long ReadInt64(IReadOnlyDictionary<string, object> row, string key)
        {
            var value = ReadValue(row, key);
            if (value == null || value == DBNull.Value)
                return 0;

            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static string ReadString(IReadOnlyDictionary<string, object> row, string key, string defaultValue = null)
        {
            var value = ReadValue(row, key);
            return value == null || value == DBNull.Value
                ? defaultValue
                : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static byte[] ReadBlob(IReadOnlyDictionary<string, object> row, string key)
        {
            var value = ReadValue(row, key);
            if (value is byte[] bytes && bytes.Length > 0)
            {
                var copy = new byte[bytes.Length];
                Buffer.BlockCopy(bytes, 0, copy, 0, bytes.Length);
                return copy;
            }

            return Array.Empty<byte>();
        }

        private static short ToInt16(int value)
        {
            if (value > short.MaxValue)
                return short.MaxValue;
            if (value < short.MinValue)
                return short.MinValue;
            return (short)value;
        }

        private static ushort ToUInt16(int value)
        {
            if (value < 0)
                return 0;
            if (value > ushort.MaxValue)
                return ushort.MaxValue;
            return (ushort)value;
        }

        private static byte ToByte(int value)
        {
            if (value < byte.MinValue)
                return byte.MinValue;
            if (value > byte.MaxValue)
                return byte.MaxValue;
            return (byte)value;
        }
    }
}
