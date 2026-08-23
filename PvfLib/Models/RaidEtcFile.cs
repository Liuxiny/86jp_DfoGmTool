using System;
using System.Collections.Generic;
using System.Linq;

namespace GmPvfLib
{
    public sealed class RaidRankCondition
    {
        public int MinimumDeathCount { get; init; }
        public int MaximumDeathCount { get; init; }
        public int Rank { get; init; }
    }

    public sealed class RaidStateReward
    {
        public string RewardType { get; init; } = string.Empty;
        public int State { get; init; }
        public int Weight { get; init; }
        public int ItemId { get; init; }
        public int Flags { get; init; }
    }

    public sealed class RaidEtcPhase
    {
        public List<RaidRankCondition> RankConditions { get; } = new List<RaidRankCondition>();
        public List<RaidStateReward> StateRewards { get; } = new List<RaidStateReward>();

        public int ResolveRank(int deathCount)
        {
            foreach (var condition in RankConditions.OrderBy(entry => entry.MinimumDeathCount))
            {
                if (deathCount >= condition.MinimumDeathCount
                    && deathCount <= condition.MaximumDeathCount)
                    return condition.Rank;
            }

            if (RankConditions.Count == 0)
                return -1;
            if (deathCount < RankConditions.Min(entry => entry.MinimumDeathCount))
                return RankConditions.OrderBy(entry => entry.MinimumDeathCount).First().Rank;

            return RankConditions.OrderByDescending(entry => entry.MaximumDeathCount).First().Rank;
        }

        public int GetRewardWeight(string rewardType, int state)
        {
            return GetRewardCandidates(rewardType, state).Sum(entry => Math.Max(0, entry.Weight));
        }

        public bool TrySelectReward(string rewardType, int state, int roll, out RaidStateReward reward)
        {
            reward = null;
            var candidates = GetRewardCandidates(rewardType, state);
            var totalWeight = candidates.Sum(entry => Math.Max(0, entry.Weight));
            if (roll < 0 || roll >= totalWeight)
                return false;

            foreach (var candidate in candidates)
            {
                roll -= Math.Max(0, candidate.Weight);
                if (roll >= 0)
                    continue;

                reward = candidate;
                return true;
            }

            return false;
        }

        private List<RaidStateReward> GetRewardCandidates(string rewardType, int state)
        {
            var typed = StateRewards
                .Where(entry => string.Equals(
                    entry.RewardType,
                    rewardType,
                    StringComparison.OrdinalIgnoreCase)
                    && entry.Weight > 0)
                .ToList();
            var exact = typed.Where(entry => entry.State == state).ToList();
            return exact.Count > 0
                ? exact
                : typed.Where(entry => entry.State == -1).ToList();
        }
    }

    public sealed class RaidEtcFile : PvfModelBase
    {
        public int StartDelaySeconds { get; private set; }
        public int PhaseBreakSeconds { get; private set; }
        public List<int> ShieldChargeRates { get; } = new List<int>();
        public int HatcheryTotalCount { get; private set; }
        public int HatcheryOpenCount { get; private set; }
        public List<int> HatcheryDungeonIds { get; } = new List<int>();
        public List<int> ExceptCheatDungeonIds { get; } = new List<int>();
        public List<RaidEtcPhase> Phases { get; } = new List<RaidEtcPhase>();
        public List<RaidRankCondition> RankConditions { get; } = new List<RaidRankCondition>();
        public List<RaidStateReward> StateRewards { get; } = new List<RaidStateReward>();

        public static RaidEtcFile Parse(string content)
        {
            var root = string.IsNullOrEmpty(content)
                ? new ScriptNode { Tag = "ROOT" }
                : new ScriptParser().Parse(content);
            var file = new RaidEtcFile
            {
                Root = root,
                Content = content ?? string.Empty,
            };

            var startDelay = ParseRootValues(root, "start delay time", file.Content);
            if (startDelay.Length > 0)
                file.StartDelaySeconds = startDelay[0];

            var phaseBreakTime = ParseRootValues(root, "phase break time", file.Content);
            if (phaseBreakTime.Length > 0)
                file.PhaseBreakSeconds = phaseBreakTime[0];

            file.ShieldChargeRates.AddRange(
                ParseRootValues(root, "shield charge rate", file.Content));

            var hatcheryInfo = ParseRootValues(root, "hatchery info", file.Content);
            if (hatcheryInfo.Length >= 2)
            {
                file.HatcheryTotalCount = hatcheryInfo[0];
                file.HatcheryOpenCount = hatcheryInfo[1];
                file.HatcheryDungeonIds.AddRange(hatcheryInfo.Skip(2));
            }

            file.ExceptCheatDungeonIds.AddRange(
                ParseRootValues(root, "except cheat dungeon", file.Content));

            var phaseNodes = root.GetChildren("phase");
            if (phaseNodes.Count == 0)
            {
                file.Phases.Add(ParsePhase(root, file.Content));
            }
            else
            {
                foreach (var phaseNode in phaseNodes)
                    file.Phases.Add(ParsePhase(phaseNode, file.Content));
            }

            if (file.Phases.Count > 0)
            {
                file.RankConditions.AddRange(file.Phases[0].RankConditions);
                file.StateRewards.AddRange(file.Phases[0].StateRewards);
            }

            return file;
        }

        public RaidEtcPhase GetPhase(int phaseIndex)
        {
            return phaseIndex >= 0 && phaseIndex < Phases.Count ? Phases[phaseIndex] : null;
        }

        public int ResolveRank(int deathCount)
        {
            return GetPhase(0)?.ResolveRank(deathCount) ?? -1;
        }

        public int GetRewardWeight(string rewardType, int state)
        {
            return GetPhase(0)?.GetRewardWeight(rewardType, state) ?? 0;
        }

        public bool TrySelectReward(string rewardType, int state, int roll, out RaidStateReward reward)
        {
            var phase = GetPhase(0);
            if (phase != null)
                return phase.TrySelectReward(rewardType, state, roll, out reward);
            reward = null;
            return false;
        }

        private static RaidEtcPhase ParsePhase(ScriptNode phaseNode, string content)
        {
            var phase = new RaidEtcPhase();
            var rankNodes = new List<ScriptNode>();
            var rankNode = phaseNode.GetChild("condition for rank");
            if (rankNode != null)
                rankNodes.AddRange(rankNode.GetChildren("dead count"));
            rankNodes.AddRange(phaseNode.GetChildren("dead count"));
            foreach (var node in rankNodes.GroupBy(entry => entry.StartIndex).Select(group => group.First()))
            {
                var values = ParseIntArray(node.GetFirstDataContent(content));
                if (values == null || values.Length < 3)
                    continue;

                phase.RankConditions.Add(new RaidRankCondition
                {
                    MinimumDeathCount = values[0],
                    MaximumDeathCount = values[1],
                    Rank = values[2],
                });
            }

            var rewardNodes = new List<ScriptNode>();
            var rewardNode = phaseNode.GetChild("clear reward item");
            if (rewardNode != null)
                rewardNodes.AddRange(rewardNode.GetChildren("state reward"));
            rewardNodes.AddRange(phaseNode.GetChildren("state reward"));
            foreach (var node in rewardNodes.GroupBy(entry => entry.StartIndex).Select(group => group.First()))
            {
                var tokens = SplitTokens(node.GetFirstDataContent(content));
                if (tokens.Length < 5
                    || !int.TryParse(tokens[1], out var state)
                    || !int.TryParse(tokens[2], out var weight)
                    || !int.TryParse(tokens[3], out var itemId)
                    || !int.TryParse(tokens[4], out var flags))
                    continue;

                phase.StateRewards.Add(new RaidStateReward
                {
                    RewardType = StripBacktick(tokens[0]),
                    State = state,
                    Weight = weight,
                    ItemId = itemId,
                    Flags = flags,
                });
            }

            return phase;
        }

        private static string[] SplitTokens(string data)
        {
            return (data ?? string.Empty).Split(
                new[] { ' ', (char)9, (char)10, (char)13 },
                StringSplitOptions.RemoveEmptyEntries);
        }

        private static int[] ParseRootValues(ScriptNode root, string tag, string content)
        {
            var node = root?.GetChild(tag);
            return node == null
                ? Array.Empty<int>()
                : ParseIntArray(node.GetFirstDataContent(content)) ?? Array.Empty<int>();
        }
    }
}