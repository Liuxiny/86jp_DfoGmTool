using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal static class AvatarDetailCodec
    {
        internal static AvatarDetail ReadDetail(SqliteDataReader reader)
        {
            return FromRecord(ReadRecord(reader));
        }

        internal static AvatarDetailRecord ReadRecord(SqliteDataReader reader)
        {
            return new AvatarDetailRecord
            {
                AvatarUid = reader.GetInt64(0),
                OwnerId = reader.GetInt32(1),
                CharacterId = reader.GetInt32(2),
                ItemId = reader.GetInt32(3),
                ExpireDate = reader.GetInt32(4),
                ClearAvatarId = reader.GetInt32(5),
                JewelSocket = reader.IsDBNull(6) ? Array.Empty<byte>() : (byte[])reader[6],
                Color1 = Convert.ToUInt16(reader.GetInt32(7), CultureInfo.InvariantCulture),
                Color2 = Convert.ToUInt16(reader.GetInt32(8), CultureInfo.InvariantCulture),
                DeleteDate = reader.GetInt32(9),
            };
        }

        internal static AvatarDetail FromRecord(AvatarDetailRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            return new AvatarDetail
            {
                AvatarUid = record.AvatarUid,
                OwnerId = record.OwnerId,
                CharacterId = record.CharacterId,
                ItemId = record.ItemId,
                ExpireDate = record.ExpireDate,
                ClearAvatarId = record.ClearAvatarId,
                JewelSocket = record.JewelSocket,
                Color1 = record.Color1,
                Color2 = record.Color2,
                DeleteDate = record.DeleteDate,
            };
        }

        internal static AvatarDetailRecord ToRecord(AvatarDetail detail)
        {
            if (detail == null)
                throw new ArgumentNullException(nameof(detail));

            return new AvatarDetailRecord
            {
                AvatarUid = detail.AvatarUid,
                OwnerId = detail.OwnerId,
                CharacterId = detail.CharacterId,
                ItemId = detail.ItemId,
                ExpireDate = detail.ExpireDate,
                ClearAvatarId = detail.ClearAvatarId,
                JewelSocket = detail.JewelSocket,
                Color1 = detail.Color1,
                Color2 = detail.Color2,
                DeleteDate = detail.DeleteDate,
            };
        }

        internal sealed class AvatarDetailRecord
        {
            public long AvatarUid { get; set; }

            public int OwnerId { get; set; }

            public int CharacterId { get; set; }

            public int ItemId { get; set; }

            public int ExpireDate { get; set; }

            public int ClearAvatarId { get; set; }

            public byte[] JewelSocket { get; set; } = Array.Empty<byte>();

            public ushort Color1 { get; set; }

            public ushort Color2 { get; set; }

            public int DeleteDate { get; set; }
        }
    }
}
