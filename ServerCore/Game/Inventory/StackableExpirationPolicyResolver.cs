using GmPvfLib;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal readonly struct StackableExpirationPolicy
    {
        internal StackableExpirationPolicy(int absoluteExpirationUnixTime, int usablePeriodDays)
        {
            AbsoluteExpirationUnixTime = absoluteExpirationUnixTime;
            UsablePeriodDays = usablePeriodDays;
        }

        internal int AbsoluteExpirationUnixTime { get; }

        internal int UsablePeriodDays { get; }

        internal bool RequiresInstanceExpiration => UsablePeriodDays > 0;
    }

    internal static class StackableExpirationPolicyResolver
    {
        internal static bool TryResolve(StackableItemFile stackable, out StackableExpirationPolicy policy)
        {
            policy = default;
            if (stackable?.Root == null)
                return false;

            if (!TryReadOptionalSingleValue(
                    stackable,
                    "expiration date",
                    out var hasAbsoluteExpiration,
                    out var absoluteExpirationValue))
                return false;

            var absoluteExpiration = 0;
            if (hasAbsoluteExpiration)
            {
                var numericValue = int.TryParse(absoluteExpirationValue, out var parsed) ? parsed : -1;
                if (numericValue != 0
                    && !ItemGrantExpirationResolver.TryParsePvfExpirationUnixTime(
                        absoluteExpirationValue,
                        numericValue,
                        out absoluteExpiration))
                    return false;
            }

            if (!TryReadOptionalSingleValue(
                    stackable,
                    "usable period",
                    out var hasUsablePeriod,
                    out var usablePeriodValue))
                return false;

            var usablePeriodDays = 0;
            if (hasUsablePeriod
                && (!int.TryParse(usablePeriodValue, out usablePeriodDays) || usablePeriodDays < 0))
                return false;

            policy = new StackableExpirationPolicy(absoluteExpiration, usablePeriodDays);
            return true;
        }

        private static bool TryReadOptionalSingleValue(
            StackableItemFile stackable,
            string tag,
            out bool hasValue,
            out string value)
        {
            hasValue = false;
            value = null;
            List<ScriptNode> nodes = stackable.Root.GetChildren(tag);
            if (nodes.Count == 0)
                return true;
            if (nodes.Count != 1
                || nodes[0].Children.Count != 0
                || nodes[0].DataItems.Count != 1)
                return false;

            value = nodes[0].DataItems[0]
                .GetContent(stackable.Content)
                .Trim()
                .Trim('`')
                .Trim();
            hasValue = value.Length > 0;
            return hasValue;
        }
    }
}
