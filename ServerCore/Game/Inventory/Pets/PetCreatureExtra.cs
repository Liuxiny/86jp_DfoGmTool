using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed partial class SqliteInventoryStore
    {
        private const int CommonPrefixData0EBaseOffset = 0x0E;
        private const int PetTailData0ABaseOffset = 0x0A;
        private const int PetEnchantCardIdOffset = 0x0E;
        private const int PetEnchantUpgradeCountOffset = 0x12;
        private const int PetTradeRestrictionOffset = 0x4C;
        private const int PetSealRemainUseCountOffset = 0x52;
        private const int CommonTailData2FBaseOffset = 0x2F;
        private const int CommonPrefixEnchantCardIdIndex = PetEnchantCardIdOffset - CommonPrefixData0EBaseOffset;
        private const int CommonPrefixEnchantUpgradeCountIndex = PetEnchantUpgradeCountOffset - CommonPrefixData0EBaseOffset;
        private const int CommonTailTradeRestrictionIndex = PetTradeRestrictionOffset - CommonTailData2FBaseOffset;
        private const int CommonTailRemainUseCountIndex = PetSealRemainUseCountOffset - CommonTailData2FBaseOffset;
        private const int CommonTailTradeRestrictionCompatIndex = CommonTailTradeRestrictionIndex - 1;
        private const int CommonTailRemainUseCountCompatIndex = CommonTailRemainUseCountIndex - 1;
        private const int PetTailEnchantCardIdIndex = PetEnchantCardIdOffset - PetTailData0ABaseOffset;
        private const int PetTailEnchantUpgradeCountIndex = PetEnchantUpgradeCountOffset - PetTailData0ABaseOffset;
        private const int PetTailTradeRestrictionIndex = PetTradeRestrictionOffset - PetTailData0ABaseOffset;
        private const int PetTailRemainUseCountIndex = PetSealRemainUseCountOffset - PetTailData0ABaseOffset;
        private const byte PetTradeRestrictionNone = 0;
        private const byte PetTradeRestrictionExhausted = 1;
        private const string PetSealRemainUseCountInitializedProperty = "petSealRemainUseCountInitialized";
        private const string PetSealRemainUseCountProperty = "petSealRemainUseCount";

        private static Dictionary<int, string> LoadPetCreatureExtraJsonMap(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId)
        {
            var result = new Dictionary<int, string>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT creature_key, extra_json
FROM character_creatures
WHERE character_id = @cid;";
                command.Parameters.AddWithValue("@cid", characterId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var serial = reader.GetInt32(0);
                        if (serial > 0)
                            result[serial] = reader.IsDBNull(1) ? "{}" : reader.GetString(1);
                    }
                }
            }

            return result;
        }

        private static string ResolvePetCreatureInstanceExtraJson(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int petSerial,
            string candidateExtraJson)
        {
            var candidate = NormalizePetCreatureExtraJson(candidateExtraJson);
            var stored = LoadPetCreatureExtraJson(connection, transaction, characterId, petSerial);
            if (HasPetCreatureProtocolTail(stored))
                return stored;

            if (HasPetCreatureProtocolTail(candidate))
            {
                UpsertPetCreatureExtraJson(connection, transaction, characterId, petSerial, candidate);
                return candidate;
            }

            return candidate;
        }

        private static string MergePetCreatureInstanceExtraJsonForRead(
            string storedExtraJson,
            string candidateExtraJson)
        {
            var stored = NormalizePetCreatureExtraJson(storedExtraJson);
            return HasPetCreatureProtocolTail(stored)
                ? stored
                : NormalizePetCreatureExtraJson(candidateExtraJson);
        }

        private static string LoadPetCreatureExtraJson(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int petSerial)
        {
            if (petSerial <= 0)
                return CreateDefaultPetExtraJson();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT extra_json
FROM character_creatures
WHERE character_id = @cid
  AND creature_key = @serial
LIMIT 1;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@serial", petSerial);
                var value = command.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? CreateDefaultPetExtraJson()
                    : NormalizePetCreatureExtraJson(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        private static void UpsertPetCreatureExtraJson(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int petSerial,
            string extraJson)
        {
            if (petSerial <= 0)
                return;

            var normalized = NormalizePetCreatureExtraJson(extraJson);
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = @"
UPDATE character_creatures
SET extra_json = @extra
WHERE character_id = @cid
  AND creature_key = @serial;";
                update.Parameters.AddWithValue("@extra", normalized);
                update.Parameters.AddWithValue("@cid", characterId);
                update.Parameters.AddWithValue("@serial", petSerial);
                if (update.ExecuteNonQuery() > 0)
                    return;
            }

            EnsureCreatureListEntry(
                connection,
                transaction,
                characterId,
                petSerial,
                new CreatureDefaults(1, Array.Empty<byte>()));

            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = @"
UPDATE character_creatures
SET extra_json = @extra
WHERE character_id = @cid
  AND creature_key = @serial;";
                update.Parameters.AddWithValue("@extra", normalized);
                update.Parameters.AddWithValue("@cid", characterId);
                update.Parameters.AddWithValue("@serial", petSerial);
                update.ExecuteNonQuery();
            }
        }

        internal static string SetPetCreatureEnchantExtraJson(
            string extraJson,
            int enchantCardItemId,
            byte enchantUpgradeCount)
        {
            var json = ParseJsonObject(NormalizePetCreatureExtraJson(extraJson));
            var tail = ItemExtraView.Parse(json.ToJsonString()).Pet.TailData0A;
            BitConverter.GetBytes(enchantCardItemId).CopyTo(tail, PetTailEnchantCardIdIndex);
            tail[PetTailEnchantUpgradeCountIndex] = enchantUpgradeCount;
            json["tailData0A"] = ItemExtraView.ToHex(tail);
            json["petEnchantCardItemId"] = enchantCardItemId;
            json["petEnchantUpgradeCount"] = enchantUpgradeCount;
            return json.ToJsonString();
        }

        internal static void PersistPetCreatureExtraJson(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int petSerial,
            string extraJson)
        {
            UpsertPetCreatureExtraJson(connection, transaction, characterId, petSerial, extraJson);
            SyncEquippedPetCreatureExtraRaw(connection, transaction, characterId, petSerial, extraJson);
        }

        private static string BuildInitializedPetCreatureSealExtraJson(byte remainUseCount)
        {
            var json = ParseJsonObject(CreateDefaultPetExtraJson());
            var tail = ItemExtraView.Parse(json.ToJsonString()).Pet.TailData0A;
            tail[PetTailTradeRestrictionIndex] = remainUseCount <= 0
                ? PetTradeRestrictionExhausted
                : PetTradeRestrictionNone;
            tail[PetTailRemainUseCountIndex] = remainUseCount;
            json["tailData0A"] = ItemExtraView.ToHex(tail);
            json[PetSealRemainUseCountInitializedProperty] = true;
            json[PetSealRemainUseCountProperty] = remainUseCount;
            return json.ToJsonString();
        }

        private static bool TryResolvePetCreatureSealRemainUseCount(string extraJson, out byte remainUseCount)
        {
            remainUseCount = 0;
            var tail = ItemExtraView.Parse(extraJson).Pet.TailData0A;
            if (tail.Length <= PetTailRemainUseCountIndex)
                return false;

            if (TryReadJsonObject(extraJson, out var json))
            {
                if (TryReadJsonInt(json, PetSealRemainUseCountProperty, out var direct))
                {
                    remainUseCount = ClampByte(direct);
                    return true;
                }

                if (HasPetSealRemainUseCountInitialized(json))
                {
                    remainUseCount = tail[PetTailRemainUseCountIndex];
                    return true;
                }
            }

            if (tail[PetTailRemainUseCountIndex] > 0)
            {
                remainUseCount = tail[PetTailRemainUseCountIndex];
                return true;
            }

            return false;
        }

        private static void ApplyPetCreatureExtraToCommonPrefix(byte[] commonPrefixData0E, string petExtraJson)
        {
            if (commonPrefixData0E == null || commonPrefixData0E.Length <= CommonPrefixEnchantUpgradeCountIndex)
                return;

            var tail = ItemExtraView.Parse(petExtraJson).Pet.TailData0A;
            if (tail.Length <= PetTailEnchantUpgradeCountIndex)
                return;

            Buffer.BlockCopy(tail, PetTailEnchantCardIdIndex, commonPrefixData0E, CommonPrefixEnchantCardIdIndex, 4);
            commonPrefixData0E[CommonPrefixEnchantUpgradeCountIndex] = tail[PetTailEnchantUpgradeCountIndex];
        }

        private static void ApplyPetCreatureExtraToCommonTail(byte[] commonTailData2F, string petExtraJson)
        {
            if (commonTailData2F == null || commonTailData2F.Length <= CommonTailRemainUseCountIndex)
                return;

            var tail = ItemExtraView.Parse(petExtraJson).Pet.TailData0A;
            CopyPetExtraByte(tail, PetTailTradeRestrictionIndex, commonTailData2F, CommonTailTradeRestrictionIndex);
            CopyPetExtraByte(tail, PetTailRemainUseCountIndex, commonTailData2F, CommonTailRemainUseCountIndex);

            // Some client detail refresh paths read the pet body as an 84B common item
            // and use the adjacent tail indexes for these two pet-only fields.
            CopyPetExtraByte(tail, PetTailTradeRestrictionIndex, commonTailData2F, CommonTailTradeRestrictionCompatIndex);
            CopyPetExtraByte(tail, PetTailRemainUseCountIndex, commonTailData2F, CommonTailRemainUseCountCompatIndex);
        }

        private static void CopyPetExtraByte(byte[] source, int sourceIndex, byte[] target, int targetIndex)
        {
            if (source == null
                || target == null
                || sourceIndex < 0
                || targetIndex < 0
                || source.Length <= sourceIndex
                || target.Length <= targetIndex)
                return;

            target[targetIndex] = source[sourceIndex];
        }

        private static string NormalizePetCreatureExtraJson(string extraJson)
        {
            var json = ParseJsonObject(extraJson);
            var tail = ItemExtraView.Parse(extraJson).Pet.TailData0A;
            if (tail.Length != 74)
                tail = new byte[74];

            json["tailData0A"] = ItemExtraView.ToHex(tail);
            return json.ToJsonString();
        }

        private static bool HasPetCreatureProtocolTail(string extraJson)
        {
            if (HasPetCreatureExtraProtocolMarker(extraJson))
                return true;

            var tail = ItemExtraView.Parse(extraJson).Pet.TailData0A;
            for (var index = 0; index < tail.Length; index++)
                if (tail[index] != 0)
                    return true;

            return false;
        }

        private static bool HasPetCreatureExtraProtocolMarker(string extraJson)
        {
            return TryReadJsonObject(extraJson, out var json)
                && (json.ContainsKey(PetSealRemainUseCountInitializedProperty)
                    || json.ContainsKey(PetSealRemainUseCountProperty)
                    || json.ContainsKey("petEnchantCardItemId"));
        }

        private static void SyncEquippedPetCreatureExtraRaw(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int petSerial,
            string extraJson)
        {
            if (petSerial <= 0)
                return;

            var tail = ItemExtraView.Parse(extraJson).Pet.TailData0A;
            if (tail.Length <= PetTailEnchantUpgradeCountIndex)
                return;

            var enchantCardItemId = unchecked((uint)BitConverter.ToInt32(tail, PetTailEnchantCardIdIndex));
            var enchantUpgradeCount = tail[PetTailEnchantUpgradeCountIndex];

            byte[] raw = null;
            var itemId = 0;
            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = @"
SELECT item_id, raw_entry
FROM character_equipped_entries
WHERE character_id = @cid
  AND slot = @slot
LIMIT 1;";
                select.Parameters.AddWithValue("@cid", characterId);
                select.Parameters.AddWithValue("@slot", PetCreatureEquipSlot);
                using (var reader = select.ExecuteReader())
                {
                    if (!reader.Read())
                        return;

                    itemId = reader.GetInt32(0);
                    raw = reader.IsDBNull(1) ? null : (byte[])reader.GetValue(1);
                }
            }

            if (!IsCreatureItem(itemId) || ResolvePetCreatureSerialFromEquippedRaw(raw) != petSerial)
                return;

            byte[] updatedRaw;
            try
            {
                var item = InvenItem.Parse(raw);
                item.EnchantIndex = enchantCardItemId;
                item.EnchantUpgradeCount = enchantUpgradeCount;
                updatedRaw = item.ToBytes();
            }
            catch
            {
                var fields = MakeEquipListCodec.ParseDisplayFields(raw);
                fields.Enchant = enchantCardItemId;
                fields.EnchantUpgradeCount = enchantUpgradeCount;
                updatedRaw = MakeEquipListCodec.BuildEntryFromDisplayFields(PetCreatureEquipSlot, itemId, fields);
            }

            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = @"
UPDATE character_equipped_entries
SET raw_entry = @raw
WHERE character_id = @cid
  AND slot = @slot
  AND item_id = @itemId;";
                update.Parameters.AddWithValue("@raw", updatedRaw);
                update.Parameters.AddWithValue("@cid", characterId);
                update.Parameters.AddWithValue("@slot", PetCreatureEquipSlot);
                update.Parameters.AddWithValue("@itemId", itemId);
                update.ExecuteNonQuery();
            }
        }

        private static int ResolvePetCreatureSerialFromEquippedRaw(byte[] raw)
        {
            if (raw == null)
                return 0;

            try
            {
                var fields = MakeEquipListCodec.ParseDisplayFields(raw);
                var serial = unchecked((int)fields.InstanceValue);
                if (serial > 0)
                    return serial;
            }
            catch
            {
            }

            return raw.Length >= 9 ? BitConverter.ToInt32(raw, 5) : 0;
        }

        private static JsonObject ParseJsonObject(string jsonText)
        {
            if (!string.IsNullOrWhiteSpace(jsonText))
            {
                try
                {
                    if (JsonNode.Parse(jsonText) is JsonObject json)
                        return json;
                }
                catch
                {
                }
            }

            return new JsonObject();
        }

        private static bool TryReadJsonObject(string jsonText, out JsonObject json)
        {
            json = null;
            if (string.IsNullOrWhiteSpace(jsonText))
                return false;

            try
            {
                json = JsonNode.Parse(jsonText) as JsonObject;
                return json != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadJsonInt(JsonObject json, string propertyName, out int value)
        {
            value = 0;
            if (json == null || !json.TryGetPropertyValue(propertyName, out var node) || node == null)
                return false;

            try
            {
                value = node.GetValue<int>();
                return true;
            }
            catch
            {
                return int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }
        }

        private static bool HasPetSealRemainUseCountInitialized(JsonObject json)
        {
            if (json == null)
                return false;

            if (json.ContainsKey(PetSealRemainUseCountProperty))
                return true;

            if (!json.TryGetPropertyValue(PetSealRemainUseCountInitializedProperty, out var node) || node == null)
                return false;

            try
            {
                return node.GetValue<bool>();
            }
            catch
            {
                var text = node.ToString();
                return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "1", StringComparison.Ordinal);
            }
        }

        private static byte ClampByte(int value)
        {
            if (value <= 0)
                return 0;
            return value >= byte.MaxValue ? byte.MaxValue : (byte)value;
        }
    }
}
