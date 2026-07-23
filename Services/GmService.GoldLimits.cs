using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DfoGmTool.ServerCore.GameWorld;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        // 与服务端 GoldLimitDataProvider 保持一致。GM 仅写服务端已有的角色上限数据，
        // 不修改任何服务端实现。
        private const int BaseAuctionGoldLimit = 400_000_000;
        private const int MaximumGoldCarryLimit = 800_000_000;
        private const byte MinimumGoldLimitUpgradeCharacterLevel = 60;
        private static readonly int[] ExpandedGoldLimits =
        {
            0, 500_000_000, 600_000_000, 700_000_000, MaximumGoldCarryLimit,
        };

        private sealed class GoldLimitSnapshot
        {
            public int CharacterLevel { get; set; }
            public int GoldCarryLimit { get; set; }
            public int AuctionGoldLimit { get; set; }
            public byte UpgradeLevel { get; set; }
        }

        public object GetGoldLimitStatus(int characterId)
        {
            GoldLimitSnapshot snapshot;
            try
            {
                snapshot = LoadGoldLimitSnapshot(characterId);
            }
            catch (InvalidOperationException ex)
            {
                return Error(ex.Message);
            }

            return ToGoldLimitStatus(characterId, snapshot);
        }

        // GM 管理操作：直接升为服务端定义的最高档（8 亿），不模拟玩家端逐档扣费。
        public object SetMaximumGoldLimit(int characterId)
        {
            GoldLimitSnapshot current;
            try
            {
                current = LoadGoldLimitSnapshot(characterId);
            }
            catch (InvalidOperationException ex)
            {
                return Error(ex.Message);
            }

            if (current.CharacterLevel < MinimumGoldLimitUpgradeCharacterLevel)
                return Error("角色达到 60 级后才能升级金币上限");
            if (current.UpgradeLevel >= ExpandedGoldLimits.Length - 1)
                return Error("金币上限已是最高值");

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
INSERT INTO character_gold_limits(character_id, gold_carry_limit, auction_gold_limit)
VALUES(@cid, @limit, @limit)
ON CONFLICT(character_id) DO UPDATE SET
    gold_carry_limit = MAX(character_gold_limits.gold_carry_limit, @limit),
    auction_gold_limit = MAX(character_gold_limits.auction_gold_limit, @limit),
    updated_at = CURRENT_TIMESTAMP;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    cmd.Parameters.AddWithValue("@limit", MaximumGoldCarryLimit);
                    cmd.ExecuteNonQuery();
                }
            }

            return ToGoldLimitStatus(characterId, LoadGoldLimitSnapshot(characterId));
        }

        private object ToGoldLimitStatus(int characterId, GoldLimitSnapshot snapshot)
        {
            var isMaximum = snapshot.UpgradeLevel >= ExpandedGoldLimits.Length - 1;
            return new
            {
                success = true,
                characterId,
                characterLevel = snapshot.CharacterLevel,
                goldCarryLimit = snapshot.GoldCarryLimit,
                auctionGoldLimit = snapshot.AuctionGoldLimit,
                maximumGoldCarryLimit = MaximumGoldCarryLimit,
                upgradeLevel = snapshot.UpgradeLevel,
                isMaximum,
                canSetMaximum = snapshot.CharacterLevel >= MinimumGoldLimitUpgradeCharacterLevel && !isMaximum,
                minimumUpgradeCharacterLevel = MinimumGoldLimitUpgradeCharacterLevel,
            };
        }

        private GoldLimitSnapshot LoadGoldLimitSnapshot(int characterId)
        {
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                int characterLevel;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT level FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    var raw = cmd.ExecuteScalar();
                    if (raw == null || raw == DBNull.Value)
                        throw new InvalidOperationException("角色不存在: " + characterId);
                    characterLevel = Convert.ToInt32(raw);
                }

                var baseCarryLimit = GetBaseGoldCarryLimit(characterLevel);
                var savedCarryLimit = 0;
                var savedAuctionLimit = 0;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT gold_carry_limit, auction_gold_limit
FROM character_gold_limits
WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            savedCarryLimit = reader.GetInt32(0);
                            savedAuctionLimit = reader.GetInt32(1);
                        }
                    }
                }

                var carryLimit = Math.Max(baseCarryLimit, savedCarryLimit);
                var auctionLimit = Math.Max(BaseAuctionGoldLimit, savedAuctionLimit);
                return new GoldLimitSnapshot
                {
                    CharacterLevel = characterLevel,
                    GoldCarryLimit = carryLimit,
                    AuctionGoldLimit = auctionLimit,
                    UpgradeLevel = ResolveGoldLimitUpgradeLevel(carryLimit, auctionLimit),
                };
            }
        }

        private static int GetBaseGoldCarryLimit(int level)
        {
            var text = PvfArchiveAccessor.ReadText("etc/(r)goldlimitbylevel.etc");
            var limits = ParseBaseGoldCarryLimits(text);
            if (limits.Count < 100)
                throw new InvalidOperationException("PVF 金币上限表不完整");

            level = Math.Max(0, Math.Min(99, level));
            return limits.TryGetValue(level, out var limit) ? limit : 0;
        }

        private static Dictionary<int, int> ParseBaseGoldCarryLimits(string text)
        {
            var result = new Dictionary<int, int>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            var start = text.IndexOf("[gold limit from level]", StringComparison.OrdinalIgnoreCase);
            var end = text.IndexOf("[/gold limit from level]", StringComparison.OrdinalIgnoreCase);
            start = start >= 0 ? start + "[gold limit from level]".Length : 0;
            if (end < start)
                end = text.Length;

            var numbers = Regex.Matches(text.Substring(start, end - start), @"\d+");
            for (var i = 0; i + 1 < numbers.Count; i += 2)
            {
                if (int.TryParse(numbers[i].Value, out var level)
                    && int.TryParse(numbers[i + 1].Value, out var limit))
                    result[level] = Math.Max(0, limit);
            }
            return result;
        }

        private static byte ResolveGoldLimitUpgradeLevel(int goldCarryLimit, int auctionGoldLimit)
        {
            var synchronizedLimit = Math.Min(goldCarryLimit, auctionGoldLimit);
            for (var level = ExpandedGoldLimits.Length - 1; level >= 1; level--)
            {
                if (synchronizedLimit >= ExpandedGoldLimits[level])
                    return (byte)level;
            }
            return 0;
        }
    }
}
