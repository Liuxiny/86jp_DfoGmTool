using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.TitleBook
{
    public sealed class CharacterTitleBookRepository
    {
        private readonly string _connectionString;
        private readonly TitleBookStaticDataProvider _staticData;

        public CharacterTitleBookRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _staticData = TitleBookStaticDataProvider.LoadDefault();
        }

        public List<TitleBookCategorySnapshot> LoadSnapshots(int characterId)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var result = new List<TitleBookCategorySnapshot>();
                for (var category = 0; category < TitleBookStaticDataProvider.CategoryCapacities.Count; category++)
                {
                    var blob = LoadCategoryBlob(connection, null, characterId, category);
                    var items = blob != null
                        ? LoadCategoryItems(category, blob)
                        : LoadLegacyCategoryItems(connection, null, characterId, category);

                    if (blob == null && !HasAnyItem(items))
                        items = LoadLegacyCategoryItems(connection, characterId, category);

                    result.Add(BuildSnapshot(category, items));
                }
                return result;
            }
        }

        public TitleBookInventoryItem LoadItem(SqliteConnection connection, SqliteTransaction transaction, int characterId, int category, int bookIndex)
        {
            var capacity = GetCapacity(category);
            if (bookIndex < 0 || bookIndex >= capacity)
                return null;

            var blob = LoadCategoryBlob(connection, transaction, characterId, category);
            if (blob == null)
            {
                var legacy = LoadLegacyItem(connection, transaction, characterId, category, bookIndex);
                return legacy ?? TitleBookInventoryItemCodec.CreateEmpty(category, (ushort)bookIndex);
            }

            blob = NormalizeCategoryBlob(blob, capacity);

            if (blob.Length < (bookIndex + 1) * TitleBookInventoryItemCodec.PersistedRecordSize)
                return TitleBookInventoryItemCodec.CreateEmpty(category, (ushort)bookIndex);

            var record = new byte[TitleBookInventoryItemCodec.PersistedRecordSize];
            Buffer.BlockCopy(blob, bookIndex * TitleBookInventoryItemCodec.PersistedRecordSize, record, 0, record.Length);
            return TitleBookInventoryItemCodec.Deserialize(category, (ushort)bookIndex, record);
        }

        public void SaveItem(SqliteConnection connection, SqliteTransaction transaction, int characterId, TitleBookInventoryItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            var capacity = GetCapacity(item.Category);
            if (item.BookIndex >= capacity)
                throw new ArgumentOutOfRangeException(nameof(item.BookIndex));

            EnsureCharacterRow(connection, transaction, characterId);

            var existingBlob = LoadCategoryBlob(connection, transaction, characterId, item.Category);
            var blob = existingBlob == null
                ? BuildBlobFromItems(LoadLegacyCategoryItems(connection, transaction, characterId, item.Category), capacity)
                : NormalizeCategoryBlob(existingBlob, capacity);
            var record = TitleBookInventoryItemCodec.Serialize(item);
            Buffer.BlockCopy(record, 0, blob, item.BookIndex * TitleBookInventoryItemCodec.PersistedRecordSize, record.Length);
            SaveCategoryBlob(connection, transaction, characterId, item.Category, blob);
        }

        public void ClearItem(SqliteConnection connection, SqliteTransaction transaction, int characterId, int category, int bookIndex)
        {
            SaveItem(connection, transaction, characterId, TitleBookInventoryItemCodec.CreateEmpty(category, (ushort)bookIndex));
        }

        public TitleBookCategorySnapshot LoadSnapshot(int characterId, int category)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                return LoadSnapshot(connection, null, characterId, category);
            }
        }

        public TitleBookCategorySnapshot LoadSnapshot(SqliteConnection connection, SqliteTransaction transaction, int characterId, int category)
        {
            var blob = LoadCategoryBlob(connection, transaction, characterId, category);
            return BuildSnapshot(category, blob != null
                ? LoadCategoryItems(category, blob)
                : LoadLegacyCategoryItems(connection, transaction, characterId, category));
        }

        private static List<TitleBookInventoryItem> LoadCategoryItems(int category, byte[] blob)
        {
            var capacity = GetCapacity(category);
            blob = NormalizeCategoryBlob(blob, capacity);
            var items = new List<TitleBookInventoryItem>(capacity);
            for (var index = 0; index < capacity; index++)
            {
                var record = new byte[TitleBookInventoryItemCodec.PersistedRecordSize];
                Buffer.BlockCopy(blob, index * TitleBookInventoryItemCodec.PersistedRecordSize, record, 0, record.Length);
                items.Add(TitleBookInventoryItemCodec.Deserialize(category, (ushort)index, record));
            }
            return items;
        }

        private static TitleBookCategorySnapshot BuildSnapshot(int category, List<TitleBookInventoryItem> items)
        {
            var snapshot = new TitleBookCategorySnapshot
            {
                InfoType = 0,
                OwnerId16 = 0,
                Category = category,
            };

            foreach (var item in items)
            {
                if (item == null || item.IsEmpty)
                    continue;

                snapshot.Entries.Add(TitleBookInventoryItemCodec.ToListEntry(item));
            }

            return snapshot;
        }

        private List<TitleBookInventoryItem> LoadLegacyCategoryItems(SqliteConnection connection, int characterId, int category)
        {
            return LoadLegacyCategoryItems(connection, null, characterId, category);
        }

        private List<TitleBookInventoryItem> LoadLegacyCategoryItems(SqliteConnection connection, SqliteTransaction transaction, int characterId, int category)
        {
            var items = new List<TitleBookInventoryItem>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT entries_blob
FROM character_achievement_chunks
WHERE character_id = @cid AND chunk_index = @category;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@category", category);
                var blob = command.ExecuteScalar() as byte[];
                if (blob == null)
                    return items;

                for (var off = 0; off + TitleBookInventoryItemCodec.TitleBookListEntrySize <= blob.Length; off += TitleBookInventoryItemCodec.TitleBookListEntrySize)
                {
                    var bookIndex = BitConverter.ToUInt16(blob, off);
                    var item = new TitleBookInventoryItem
                    {
                        Category = category,
                        BookIndex = bookIndex,
                        Slot = bookIndex,
                        ItemId = BitConverter.ToInt32(blob, off + 2),
                        Value = BitConverter.ToInt32(blob, off + 6),
                        Attr = blob[off + 10],
                        Durability = BitConverter.ToUInt16(blob, off + 11),
                        SealFlag = blob[off + 13],
                        EnchantIndex = BitConverter.ToInt32(blob, off + 14),
                        EnchantUpgradeCount = blob[off + 18],
                        AmplifyType = blob[off + 19],
                        AmplifyValue = BitConverter.ToUInt16(blob, off + 20),
                    };
                    items.Add(item);
                }
            }
            return items;
        }

        private TitleBookInventoryItem LoadLegacyItem(SqliteConnection connection, SqliteTransaction transaction, int characterId, int category, int bookIndex)
        {
            foreach (var item in LoadLegacyCategoryItems(connection, transaction, characterId, category))
            {
                if (item.BookIndex == bookIndex)
                    return item;
            }
            return null;
        }

        private static bool HasAnyItem(List<TitleBookInventoryItem> items)
        {
            foreach (var item in items)
            {
                if (item != null && !item.IsEmpty)
                    return true;
            }
            return false;
        }

        private static byte[] LoadCategoryBlob(SqliteConnection connection, SqliteTransaction transaction, int characterId, int category)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"SELECT {ColumnName(category)} FROM character_titlebook WHERE character_id = @cid;";
                command.Parameters.AddWithValue("@cid", characterId);
                var value = command.ExecuteScalar();
                return value == DBNull.Value ? null : value as byte[];
            }
        }

        private static void SaveCategoryBlob(SqliteConnection connection, SqliteTransaction transaction, int characterId, int category, byte[] blob)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"UPDATE character_titlebook SET {ColumnName(category)} = @blob, updated_at = CURRENT_TIMESTAMP WHERE character_id = @cid;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@blob", blob);
                command.ExecuteNonQuery();
            }
        }

        private static void EnsureCharacterRow(SqliteConnection connection, SqliteTransaction transaction, int characterId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT OR IGNORE INTO character_titlebook (character_id, format_version) VALUES (@cid, 1);";
                command.Parameters.AddWithValue("@cid", characterId);
                command.ExecuteNonQuery();
            }
        }

        private static byte[] NormalizeCategoryBlob(byte[] blob, int capacity)
        {
            var expected = capacity * TitleBookInventoryItemCodec.PersistedRecordSize;
            var result = new byte[expected];
            if (blob == null)
                return result;

            var legacySize = TitleBookInventoryItemCodec.CommonNetworkSize;
            if (TitleBookInventoryItemCodec.PersistedRecordSize != legacySize
                && blob.Length == capacity * legacySize)
            {
                for (var index = 0; index < capacity; index++)
                {
                    Buffer.BlockCopy(
                        blob,
                        index * legacySize,
                        result,
                        index * TitleBookInventoryItemCodec.PersistedRecordSize,
                        legacySize);
                }
                return result;
            }

            Buffer.BlockCopy(blob, 0, result, 0, Math.Min(blob.Length, result.Length));
            return result;
        }

        private static byte[] BuildBlobFromItems(List<TitleBookInventoryItem> items, int capacity)
        {
            var blob = NormalizeCategoryBlob(null, capacity);
            foreach (var item in items)
            {
                if (item == null || item.BookIndex >= capacity)
                    continue;

                var record = TitleBookInventoryItemCodec.Serialize(item);
                Buffer.BlockCopy(record, 0, blob, item.BookIndex * TitleBookInventoryItemCodec.PersistedRecordSize, record.Length);
            }
            return blob;
        }

        private static int GetCapacity(int category)
        {
            if (category < 0 || category >= TitleBookStaticDataProvider.CategoryCapacities.Count)
                throw new ArgumentOutOfRangeException(nameof(category));

            return TitleBookStaticDataProvider.CategoryCapacities[category];
        }

        private static string ColumnName(int category)
        {
            switch (category)
            {
                case 0: return "general";
                case 1: return "specific";
                case 2: return "pvp";
                case 3: return "despair";
                case 4: return "event";
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

    }
}
