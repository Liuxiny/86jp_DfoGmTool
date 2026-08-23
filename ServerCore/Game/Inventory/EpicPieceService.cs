using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DfoGmTool.ServerCore.GameWorld;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    // A21 native equivalent: epicpieceinfo.etc defines the blob index order;
    // accounts.epic_piece_counts stores one little-endian int32 per entry.
    internal static class EpicPieceService
    {
        private static readonly object CatalogLock = new object();
        private static Lazy<Catalog> CatalogData = new Lazy<Catalog>(LoadCatalog);

        internal static void ResetForPvfChange()
        {
            lock (CatalogLock)
                CatalogData = new Lazy<Catalog>(LoadCatalog);
        }

        internal static bool IsEpicPiece(int itemId)
            => itemId > 0 && CatalogData.Value.IndexByPieceId.ContainsKey(itemId);

        internal static bool TryGetIndex(int itemId, out int index)
            => CatalogData.Value.IndexByPieceId.TryGetValue(itemId, out index);

        internal static IReadOnlyList<(int ItemId, int Count)> LoadEntries(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int accountId)
        {
            if (connection == null || accountId <= 0)
                return Array.Empty<(int, int)>();

            var blob = LoadBlob(connection, transaction, accountId);
            var entries = CatalogData.Value.PieceIds;
            var result = new List<(int, int)>();
            for (var index = 0; index < entries.Count; index++)
            {
                var count = ReadCount(blob, index);
                if (count > 0)
                    result.Add((entries[index], count));
            }
            return result;
        }

        internal static bool TryAdjust(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int accountId,
            int itemId,
            int delta,
            out int before,
            out int after,
            out string error)
        {
            before = 0;
            after = 0;
            error = null;
            if (connection == null || accountId <= 0)
            {
                error = "账号无效";
                return false;
            }
            if (!TryGetIndex(itemId, out var index))
            {
                error = "物品不是当前 A21 史诗碎片图鉴条目";
                return false;
            }

            var blob = LoadBlob(connection, transaction, accountId);
            var updated = ApplyBlobDelta(
                blob,
                index,
                CatalogData.Value.PieceIds.Count,
                delta,
                out before,
                out after);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE accounts
SET epic_piece_counts = @blob
WHERE account_id = @accountId;";
                command.Parameters.AddWithValue("@accountId", accountId);
                command.Parameters.Add("@blob", SqliteType.Blob).Value = updated;
                if (command.ExecuteNonQuery() == 0)
                {
                    error = "账号不存在: " + accountId;
                    return false;
                }
            }

            return true;
        }

        // Kept small and deterministic so the blob ordering/overflow rules can be
        // self-tested without requiring a live server inventory lease.
        internal static byte[] ApplyBlobDelta(
            byte[] source,
            int index,
            int catalogCount,
            int delta,
            out int before,
            out int after)
        {
            if (index < 0 || catalogCount <= 0 || index >= catalogCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            var blob = new byte[checked(catalogCount * sizeof(int))];
            if (source != null)
                Buffer.BlockCopy(source, 0, blob, 0, Math.Min(source.Length, blob.Length));

            var offset = checked(index * sizeof(int));
            before = Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(offset, sizeof(int))));
            var next = (long)before + delta;
            after = next <= 0 ? 0 : next >= int.MaxValue ? int.MaxValue : (int)next;
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(offset, sizeof(int)), after);
            return blob;
        }

        private static byte[] LoadBlob(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int accountId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT epic_piece_counts FROM accounts WHERE account_id=@accountId;";
                command.Parameters.AddWithValue("@accountId", accountId);
                var value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? Array.Empty<byte>() : (byte[])value;
            }
        }

        private static Catalog LoadCatalog()
        {
            try
            {
                var text = PvfArchiveAccessor.ReadText("etc/epicpieceinfo.etc");
                var drop = ExtractBlock(text, "equipment piece drop info");
                var values = ParseInts(ExtractBlock(drop, "piece list"));
                if (values.Count == 0 || values.Count % 2 != 0)
                    return Catalog.Empty;

                var pieces = new List<int>();
                var seenPieces = new HashSet<int>();
                var seenOutputs = new HashSet<int>();
                for (var i = 0; i + 1 < values.Count; i += 2)
                {
                    var outputId = values[i];
                    var pieceId = values[i + 1];
                    if (outputId > 0
                        && pieceId > 0
                        && !seenOutputs.Contains(outputId)
                        && !seenPieces.Contains(pieceId))
                    {
                        seenOutputs.Add(outputId);
                        seenPieces.Add(pieceId);
                        pieces.Add(pieceId);
                    }
                }

                var indexByPieceId = new Dictionary<int, int>();
                for (var i = 0; i < pieces.Count; i++)
                    indexByPieceId[pieces[i]] = i;
                return new Catalog(pieces, indexByPieceId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[EpicPiece] catalog load failed: " + ex.Message);
                return Catalog.Empty;
            }
        }

        private static string ExtractBlock(string text, string tag)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            var open = "[" + tag + "]";
            var close = "[/" + tag + "]";
            var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;
            start += open.Length;
            var end = text.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }

        private static List<int> ParseInts(string text)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(text))
                return result;
            foreach (Match match in Regex.Matches(text, @"-?\d+"))
                if (int.TryParse(match.Value, out var value))
                    result.Add(value);
            return result;
        }

        private static int ReadCount(byte[] blob, int index)
        {
            var offset = checked(index * sizeof(int));
            if (blob == null || offset < 0 || offset + sizeof(int) > blob.Length)
                return 0;
            return Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(offset, sizeof(int))));
        }

        private sealed class Catalog
        {
            internal static readonly Catalog Empty = new Catalog(
                new List<int>(),
                new Dictionary<int, int>());

            internal Catalog(List<int> pieceIds, Dictionary<int, int> indexByPieceId)
            {
                PieceIds = pieceIds;
                IndexByPieceId = indexByPieceId;
            }

            internal List<int> PieceIds { get; }
            internal Dictionary<int, int> IndexByPieceId { get; }
        }
    }
}
