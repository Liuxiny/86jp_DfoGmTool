using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal static class ItemGrantExpirationResolver
    {
        private static readonly TimeSpan PvfServerUtcOffset = TimeSpan.FromHours(8);

        internal static bool TryResolve(int itemTemplateId, ItemMetadata metadata, out int expireTime, out string error)
        {
            expireTime = 0;
            error = null;
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();

            if (metadata.IsStackable)
            {
                var stackable = metadata.StackableFile;
                if (stackable == null && !ItemMetadataResolver.TryLoadStackableFile(itemTemplateId, out stackable))
                {
                    error = "物品期限定义无法从 PVF 解析";
                    return false;
                }

                if (!StackableExpirationPolicyResolver.TryResolve(stackable, out var policy))
                {
                    error = "物品期限定义无法从 PVF 解析";
                    return false;
                }

                if (policy.RequiresInstanceExpiration)
                {
                    expireTime = AddDaysFromNow(policy.UsablePeriodDays);
                    return true;
                }

                if (policy.AbsoluteExpirationUnixTime > 0)
                {
                    if (policy.AbsoluteExpirationUnixTime <= now)
                    {
                        error = "物品的固定期限已过期";
                        return false;
                    }

                    expireTime = policy.AbsoluteExpirationUnixTime;
                }

                return true;
            }

            if (!string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal)
                || !ItemMetadataResolver.TryLoadEquipmentFile(itemTemplateId, out var equipment))
            {
                return true;
            }

            if (ItemMetadataResolver.IsNameTagMetadata(metadata))
            {
                expireTime = AddDaysFromNow(30);
                return true;
            }

            var rawExpiration = equipment.GetStringValue("expiration date");
            if (string.IsNullOrWhiteSpace(rawExpiration) || rawExpiration.Trim() == "0")
            {
                expireTime = -1;
                return true;
            }

            if (!TryParsePvfExpirationUnixTime(rawExpiration, -1, out var equipmentExpire))
            {
                error = "装备期限定义无法从 PVF 解析";
                return false;
            }

            if (equipmentExpire <= now)
            {
                error = "装备的固定期限已过期";
                return false;
            }

            expireTime = equipmentExpire;
            return true;
        }

        internal static bool TryParsePvfExpirationUnixTime(string value, int numericValue, out int expirationUnixTime)
        {
            expirationUnixTime = 0;
            if (!string.IsNullOrWhiteSpace(value))
            {
                var normalized = value.Trim().Trim('`');
                var match = Regex.Match(normalized, @"^\d{4}-\d{2}-\d{2}(?:\s+\d{2}:\d{2}:\d{2})?$");
                if (match.Success)
                {
                    var format = match.Value.Length > 10 ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd";
                    if (DateTime.TryParseExact(
                            match.Value,
                            format,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var localDateTime))
                    {
                        if (TryConvertPvfLocalTime(localDateTime, out expirationUnixTime))
                            return true;
                    }
                }
            }

            if (numericValue <= 0)
                return false;

            if (numericValue >= 1000000000)
            {
                expirationUnixTime = numericValue;
                return true;
            }

            if (DateTime.TryParseExact(
                    numericValue.ToString(CultureInfo.InvariantCulture),
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var numericLocalDate))
            {
                if (TryConvertPvfLocalTime(numericLocalDate, out expirationUnixTime))
                    return true;
            }

            return false;
        }

        private static bool TryConvertPvfLocalTime(DateTime localDateTime, out int unixTime)
        {
            var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
            var seconds = new DateTimeOffset(unspecified, PvfServerUtcOffset).ToUnixTimeSeconds();
            if (seconds > 0 && seconds <= int.MaxValue)
            {
                unixTime = (int)seconds;
                return true;
            }

            unixTime = 0;
            return false;
        }

        private static int AddDaysFromNow(int days)
        {
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            var expire = now + (long)days * 86400L;
            return (int)Math.Min(int.MaxValue, expire);
        }
    }
}
