using System;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal sealed class AvatarDetail
    {
        private byte[] _jewelSocket = new byte[DfoGmTool.ServerCore.Game.Inventory.JewelSocket.Size];

        public long AvatarUid { get; set; }

        public int OwnerId { get; set; }

        public int CharacterId { get; set; }

        public int ItemId { get; set; }

        public int ExpireDate { get; set; }

        public int ClearAvatarId { get; set; }

        public byte[] JewelSocket
        {
            get => Copy(_jewelSocket);
            set => _jewelSocket = DfoGmTool.ServerCore.Game.Inventory.JewelSocket.FromBytes(value).ToBytes();
        }

        public DfoGmTool.ServerCore.Game.Inventory.JewelSocket JewelSocketView
        {
            get => DfoGmTool.ServerCore.Game.Inventory.JewelSocket.FromBytes(_jewelSocket);
            set => _jewelSocket = (value ?? new DfoGmTool.ServerCore.Game.Inventory.JewelSocket()).ToBytes();
        }

        public ushort Color1 { get; set; }

        public ushort Color2 { get; set; }

        public int DeleteDate { get; set; }

        public int GetRemainDate(long? nowUnixTime = null)
        {
            if (ExpireDate <= 0)
                return 0;

            var now = nowUnixTime ?? DateTimeOffset.Now.ToUnixTimeSeconds();
            var remain = ExpireDate - now;
            return remain <= 0 ? 0 : remain > int.MaxValue ? int.MaxValue : (int)remain;
        }

        private static byte[] Copy(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<byte>();

            var result = new byte[data.Length];
            Buffer.BlockCopy(data, 0, result, 0, data.Length);
            return result;
        }
    }
}
