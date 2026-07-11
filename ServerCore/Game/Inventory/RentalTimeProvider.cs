using System;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    /// 租赁侧时间源。租赁过期使用绝对 Unix 秒，不依赖 DailyReset 或 ClockService 回调。
    public interface IRentalTimeProvider
    {
        uint UtcNowUnixSeconds();
    }

    public sealed class SystemRentalTimeProvider : IRentalTimeProvider
    {
        public static readonly SystemRentalTimeProvider Instance = new SystemRentalTimeProvider();

        private SystemRentalTimeProvider()
        {
        }

        public uint UtcNowUnixSeconds()
            => unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
}
