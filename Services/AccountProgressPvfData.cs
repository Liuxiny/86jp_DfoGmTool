using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GmPvfLib;

namespace DfoGmTool.Services
{
    // 账户进度规则始终以当前 PVF 为准，避免与客户端常量分叉。
    internal sealed class AccountProgressPvfData
    {
        private static readonly Regex GradeSectionPattern = new Regex(
            @"\[grade\](.*?)\[/grade\]", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex BacktickTextPattern = new Regex(@"`[^`]*`", RegexOptions.Compiled);
        private static readonly Regex IntegerPattern = new Regex(@"-?\d+", RegexOptions.Compiled);

        private readonly string _pvfPath;
        private readonly Lazy<AccountProgressDefinition> _definition;

        public AccountProgressPvfData(string pvfPath)
        {
            if (string.IsNullOrWhiteSpace(pvfPath))
                throw new ArgumentException("PVF path cannot be null or empty.", nameof(pvfPath));

            _pvfPath = pvfPath;
            _definition = new Lazy<AccountProgressDefinition>(Load);
        }

        public AccountProgressDefinition Get()
        {
            return _definition.Value;
        }

        private AccountProgressDefinition Load()
        {
            using (var archive = PvfArchive.Open(_pvfPath))
            {
                var honor = ParseHonor(ReadRequiredText(archive, "etc/honorlevel.etc"));
                var growthCapsule = ParseGrowthCapsule(ReadRequiredText(archive, "etc/expandexpgage.etc"));
                return new AccountProgressDefinition(honor, growthCapsule);
            }
        }

        private static string ReadRequiredText(PvfArchive archive, string path)
        {
            var text = archive.GetFileContent(path);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException("PVF 中缺少账户经验定义: " + path);
            return text;
        }

        private static HonorLevelDefinition ParseHonor(string text)
        {
            var segments = new List<HonorLevelSegment>();
            foreach (Match section in GradeSectionPattern.Matches(text))
            {
                var numbers = ParseIntegers(BacktickTextPattern.Replace(section.Groups[1].Value, " "));
                if (numbers.Count < 5)
                    continue;

                for (var i = 3; i + 1 < numbers.Count; i += 2)
                {
                    var level = numbers[i];
                    var requiredExp = numbers[i + 1];
                    if (level <= 1 || requiredExp < 0 || level > int.MaxValue)
                        continue;

                    segments.Add(new HonorLevelSegment((int)level, requiredExp));
                }
            }

            segments.Sort((left, right) => left.Level.CompareTo(right.Level));
            if (segments.Count == 0)
                throw new InvalidDataException("PVF 荣誉等级经验表为空或无法解析。");

            for (var i = 1; i < segments.Count; i++)
            {
                if (segments[i - 1].Level == segments[i].Level)
                    throw new InvalidDataException("PVF 荣誉等级经验表包含重复等级: " + segments[i].Level);
            }

            var maxExpValues = ParseIntegers(ExtractSection(text, "maxexp on maxlevel"));
            var maxExpOnMaxLevel = maxExpValues.Count > 0 ? Math.Max(0L, maxExpValues[0]) : 0L;
            if (maxExpOnMaxLevel == 0)
                maxExpOnMaxLevel = segments[segments.Count - 1].RequiredExp;
            if (maxExpOnMaxLevel <= 0)
                throw new InvalidDataException("PVF 荣誉等级满级经验无效。");

            return new HonorLevelDefinition(segments, maxExpOnMaxLevel);
        }

        private static GrowthCapsuleDefinition ParseGrowthCapsule(string text)
        {
            var root = new ScriptParser().Parse(text);
            var content = root.GetChild("max gage exp")?.GetFirstDataContent(text);
            var values = ParseIntegers(content);
            if (values.Count == 0 || values[0] <= 0)
                throw new InvalidDataException("PVF 能量胶囊经验上限无效。");

            return new GrowthCapsuleDefinition(values[0]);
        }

        private static List<long> ParseIntegers(string text)
        {
            var values = new List<long>();
            if (string.IsNullOrWhiteSpace(text))
                return values;

            foreach (Match match in IntegerPattern.Matches(text))
            {
                if (long.TryParse(match.Value, out var value))
                    values.Add(value);
            }
            return values;
        }

        private static string ExtractSection(string text, string tag)
        {
            var startTag = "[" + tag + "]";
            var endTag = "[/" + tag + "]";
            var start = text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += startTag.Length;
            var end = text.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }
    }

    internal sealed class AccountProgressDefinition
    {
        public AccountProgressDefinition(HonorLevelDefinition honor, GrowthCapsuleDefinition growthCapsule)
        {
            Honor = honor;
            GrowthCapsule = growthCapsule;
        }

        public HonorLevelDefinition Honor { get; }
        public GrowthCapsuleDefinition GrowthCapsule { get; }
    }

    internal sealed class GrowthCapsuleDefinition
    {
        public GrowthCapsuleDefinition(long requiredExp)
        {
            RequiredExp = requiredExp;
        }

        public long RequiredExp { get; }
    }

    internal sealed class HonorLevelDefinition
    {
        private readonly IReadOnlyList<HonorLevelSegment> _segments;

        public HonorLevelDefinition(IReadOnlyList<HonorLevelSegment> segments, long maxExpOnMaxLevel)
        {
            _segments = segments;
            MaxLevel = segments[segments.Count - 1].Level;
            MaxExpOnMaxLevel = maxExpOnMaxLevel;

            var total = 0L;
            foreach (var segment in _segments)
                total = SaturatingAdd(total, segment.RequiredExp);
            MaxTotalExp = SaturatingAdd(total, MaxExpOnMaxLevel);
        }

        public int MaxLevel { get; }
        public long MaxExpOnMaxLevel { get; }
        public long MaxTotalExp { get; }

        public bool TryGetTotalExpAtLevelStart(int level, out long totalExp)
        {
            totalExp = 0;
            if (level == 1)
                return Resolve(totalExp).Level == level;

            // PVF 条目表示达到该等级所需的经验，目标等级本身的条目也必须计入。
            foreach (var segment in _segments)
            {
                if (segment.Level > level)
                    break;

                totalExp = SaturatingAdd(totalExp, segment.RequiredExp);
                if (segment.Level == level)
                    return Resolve(totalExp).Level == level;
            }
            return false;
        }

        public HonorProgress Resolve(long rawTotalExp)
        {
            var totalExp = Math.Min(Math.Max(0L, rawTotalExp), MaxTotalExp);
            var remaining = totalExp;
            var level = 1;
            foreach (var segment in _segments)
            {
                if (remaining < segment.RequiredExp)
                    break;

                remaining -= segment.RequiredExp;
                level = segment.Level;
            }

            var levelExpCap = level >= MaxLevel
                ? MaxExpOnMaxLevel
                : GetRequiredExpForNextLevel(level);
            return new HonorProgress(totalExp, level, Math.Min(remaining, levelExpCap), levelExpCap);
        }

        private long GetRequiredExpForNextLevel(int level)
        {
            foreach (var segment in _segments)
            {
                if (segment.Level == level + 1)
                    return segment.RequiredExp;
            }
            return 0;
        }

        private static long SaturatingAdd(long left, long right)
        {
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }
    }

    internal readonly struct HonorLevelSegment
    {
        public HonorLevelSegment(int level, long requiredExp)
        {
            Level = level;
            RequiredExp = requiredExp;
        }

        public int Level { get; }
        public long RequiredExp { get; }
    }

    internal readonly struct HonorProgress
    {
        public HonorProgress(long totalExp, int level, long currentLevelExp, long currentLevelExpCap)
        {
            TotalExp = totalExp;
            Level = level;
            CurrentLevelExp = currentLevelExp;
            CurrentLevelExpCap = currentLevelExpCap;
        }

        public long TotalExp { get; }
        public int Level { get; }
        public long CurrentLevelExp { get; }
        public long CurrentLevelExpCap { get; }
    }
}
