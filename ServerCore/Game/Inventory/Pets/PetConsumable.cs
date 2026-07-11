using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed partial class SqliteInventoryStore
    {
        internal static bool IsPetConsumableRecord(ItemRecord source)
        {
            if (source == null)
                return false;

            return source.ListType == InventoryListType.Pet
                && source.ItemKind == "pet"
                && GetStackedRecordCount(source) > 0
                && source.SlotIndex >= PetConsumableSlotStart
                && source.SlotIndex <= PetConsumableSlotEnd;
        }

        internal static int GetStackedRecordCount(ItemRecord source)
        {
            if (source == null)
                return 0;

            if (source.ListType == InventoryListType.Pet
                && source.ItemKind == "pet"
                && source.SlotIndex >= PetConsumableSlotStart
                && source.SlotIndex <= PetConsumableSlotEnd)
            {
                return Math.Max(source.StackCount, Math.Max(source.InstanceValue, source.PetSerialOrHandle));
            }

            return source.StackCount;
        }

        private static PetSatietyMutation ApplyPetFoodSatiety(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int itemTemplateId)
        {
            if (!TryResolvePetFeedSatietyDelta(itemTemplateId, out var delta) || delta <= 0)
                return default(PetSatietyMutation);

            var activeCreatureKey = ResolveActiveCreatureKey(connection, transaction, characterId);
            if (activeCreatureKey > 0)
                return TryIncreaseCreatureSatiety(connection, transaction, characterId, activeCreatureKey, delta, out var activeMutation)
                    ? activeMutation
                    : default(PetSatietyMutation);

            // Pet food belongs to one active creature instance; do not guess when it cannot be resolved.
            return default(PetSatietyMutation);
        }

        internal static bool TryResolvePetFeedSatietyDelta(int itemTemplateId, out int delta)
        {
            delta = 0;
            var stackable = InventoryDbPrimitives.LoadStackableItem(itemTemplateId);
            if (stackable == null || string.IsNullOrWhiteSpace(stackable.StackableType))
                return false;

            var stackableType = stackable.StackableType.Replace("`", string.Empty).Trim();
            if (!stackableType.StartsWith("[feed]", StringComparison.OrdinalIgnoreCase))
                return false;

            var values = ParsePetFeedIntData(stackable.IntData);
            if (values.Count < 3)
                return false;

            delta = Math.Max(0, values[2]);
            return delta > 0;
        }

        private static List<int> ParsePetFeedIntData(string intData)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(intData))
                return result;

            foreach (var token in intData.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    result.Add(value);
            }

            return result;
        }

        private static int ResolveActiveCreatureKey(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            var equippedCreature = LoadPetCreatureEquippedEntry(connection, transaction, characterId);
            if (equippedCreature.HasValue && equippedCreature.Value.Serial > 0)
                return equippedCreature.Value.Serial;

            var candidates = new List<int>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT creature_buffer FROM character_subtype0_fields WHERE character_id = @characterId;";
                command.Parameters.AddWithValue("@characterId", characterId);

                var value = command.ExecuteScalar();
                if (value is byte[] buffer)
                {
                    AddCreatureBufferCandidate(candidates, buffer, 0, littleEndian: true);
                    AddCreatureBufferCandidate(candidates, buffer, 0, littleEndian: false);
                    AddCreatureBufferCandidate(candidates, buffer, 4, littleEndian: true);
                    AddCreatureBufferCandidate(candidates, buffer, 4, littleEndian: false);
                }
            }

            foreach (var candidate in candidates)
            {
                if (CreatureKeyExists(connection, transaction, characterId, candidate))
                    return candidate;
            }

            return 0;
        }

        private static void AddCreatureBufferCandidate(List<int> candidates, byte[] buffer, int offset, bool littleEndian)
        {
            if (buffer == null || buffer.Length < offset + 4)
                return;

            int value;
            if (littleEndian)
            {
                value = buffer[offset]
                    | (buffer[offset + 1] << 8)
                    | (buffer[offset + 2] << 16)
                    | (buffer[offset + 3] << 24);
            }
            else
            {
                value = (buffer[offset] << 24)
                    | (buffer[offset + 1] << 16)
                    | (buffer[offset + 2] << 8)
                    | buffer[offset + 3];
            }

            if (value > 0 && value < 1000000 && !candidates.Contains(value))
                candidates.Add(value);
        }

        private static bool CreatureKeyExists(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int creatureKey)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT 1
FROM character_creatures
WHERE character_id = @characterId
  AND creature_key = @creatureKey
LIMIT 1;";
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@creatureKey", creatureKey);
                return command.ExecuteScalar() != null;
            }
        }

        private static bool TryIncreaseCreatureSatiety(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int creatureKey,
            int delta,
            out PetSatietyMutation mutation)
        {
            mutation = default(PetSatietyMutation);
            var before = 0;
            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = @"
SELECT field04
FROM character_creatures
WHERE character_id = @characterId
  AND creature_key = @creatureKey
LIMIT 1;";
                select.Parameters.AddWithValue("@characterId", characterId);
                select.Parameters.AddWithValue("@creatureKey", creatureKey);

                using (var reader = select.ExecuteReader())
                {
                    if (!reader.Read())
                        return false;

                    before = Math.Max(0, Math.Min(100, reader.GetInt32(0)));
                }
            }

            if (before >= 100)
                return false;

            var after = Math.Min(100, before + delta);
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = @"
UPDATE character_creatures
SET field04 = @after
WHERE character_id = @characterId
  AND creature_key = @creatureKey;";
                update.Parameters.AddWithValue("@after", after);
                update.Parameters.AddWithValue("@characterId", characterId);
                update.Parameters.AddWithValue("@creatureKey", creatureKey);
                update.ExecuteNonQuery();
            }

            mutation = new PetSatietyMutation
            {
                CreatureKey = creatureKey,
                Before = before,
                After = after,
                Changed = after != before,
            };

            return mutation.Changed;
        }

        private struct PetSatietyMutation
        {
            public int CreatureKey;

            public int Before;

            public int After;

            public bool Changed;
        }
    }
}
