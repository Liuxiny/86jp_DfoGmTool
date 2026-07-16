using System;
using DfoGmTool.ServerCore.GameWorld;
using GmPvfLib;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal static class AmplifyInitialValueResolver
    {
        private static readonly object Sync = new object();
        private static AmplifyItemFile _config;

        internal static void ResetForPvfChange()
        {
            lock (Sync)
                _config = null;
        }

        internal static ushort Resolve(int rarity)
        {
            var config = GetConfig();
            var baseValue = config.GetBaseValue(AmplifyOptionType.PhysicalAttack);
            var weight = config.RarityWeights.TryGetValue(GetRarityName(rarity), out var value)
                ? value
                : 1d;
            var result = Math.Max(0, (int)(baseValue * weight));
            return (ushort)Math.Min(ushort.MaxValue, result);
        }

        private static AmplifyItemFile GetConfig()
        {
            lock (Sync)
            {
                return _config ??= AmplifyItemFile.Parse(PvfArchiveAccessor.ReadText("etc/amplifyitem.etc"));
            }
        }

        private static string GetRarityName(int rarity)
        {
            return rarity switch
            {
                0 => "common",
                1 => "uncommon",
                2 => "rare",
                3 => "unique",
                4 => "epic",
                5 => "chronicle",
                6 => "legendary",
                _ => string.Empty,
            };
        }
    }
}
