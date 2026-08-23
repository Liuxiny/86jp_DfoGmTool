using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DfoGmTool.ServerCore.GameWorld;

namespace DfoGmTool.ServerCore.Game.Characters
{
    // The selected PVF is the only source of job ids, tokens and advancement
    // names. Keep this provider deliberately small so all GM consumers share
    // one resettable view instead of growing their own job switch tables.
    internal sealed class PvfCharacterJobCatalog
    {
        private static readonly object Sync = new object();
        private static PvfCharacterJobCatalog _current;
        private static readonly Regex LstPattern = new Regex(@"(\d+)\s+`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex SectionPattern = new Regex(@"\[growtype\s+(\d+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BacktickPattern = new Regex(@"`([^`]*)`", RegexOptions.Compiled);
        private static readonly Regex JobPattern = new Regex(@"\[job\]\s*(?:\r?\n)?\s*(?<value>[^\r\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly Dictionary<int, JobInfo> _byId;
        private readonly Dictionary<string, int> _byToken;

        private PvfCharacterJobCatalog(Dictionary<int, JobInfo> byId, Dictionary<string, int> byToken)
        {
            _byId = byId;
            _byToken = byToken;
        }

        internal sealed class JobInfo
        {
            internal int Id { get; set; }
            internal string Token { get; set; }
            internal string BaseName { get; set; }
            internal int MaxGrowCount { get; set; }
            internal bool HasMaxGrowCount { get; set; }
            internal List<string> GrowTypeNames { get; } = new List<string>();
            internal Dictionary<int, List<string>> AwakeningNames { get; } = new Dictionary<int, List<string>>();
        }

        internal static void ResetForPvfChange()
        {
            lock (Sync)
                _current = null;
        }

        // Selftests use a tiny in-memory PVF-shaped catalog so they do not
        // fall back to a second hard-coded job table.
        internal static void ConfigureForTests(IEnumerable<JobInfo> jobs)
        {
            var byId = new Dictionary<int, JobInfo>();
            var byToken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in jobs ?? Enumerable.Empty<JobInfo>())
            {
                if (info == null || string.IsNullOrWhiteSpace(info.Token))
                    continue;
                byId[info.Id] = info;
                var token = NormalizeToken(info.Token);
                if (!string.IsNullOrEmpty(token) && !byToken.ContainsKey(token))
                    byToken[token] = info.Id;
            }
            lock (Sync)
                _current = new PvfCharacterJobCatalog(byId, byToken);
        }

        internal static PvfCharacterJobCatalog Current
        {
            get
            {
                lock (Sync)
                    return _current ??= Load();
            }
        }

        internal IReadOnlyDictionary<int, JobInfo> Jobs => _byId;

        internal bool TryGet(int job, out JobInfo info) => _byId.TryGetValue(job, out info);

        internal bool TryResolveToken(string token, out int job)
        {
            job = -1;
            var normalized = NormalizeToken(token);
            return !string.IsNullOrEmpty(normalized) && _byToken.TryGetValue(normalized, out job);
        }

        internal string GetToken(int job)
        {
            return _byId.TryGetValue(job, out var info) ? info.Token : null;
        }

        internal string GetLabel(int job)
        {
            return _byId.TryGetValue(job, out var info)
                ? (!string.IsNullOrWhiteSpace(info.BaseName) ? info.BaseName : info.Token)
                : null;
        }

        internal bool IsUsableByJob(string usableJob, int job)
        {
            var normalized = NormalizeTagText(usableJob);
            if (string.IsNullOrEmpty(normalized) || normalized.Contains("[all]", StringComparison.Ordinal))
                return true;
            if (!TryGet(job, out var info) || string.IsNullOrWhiteSpace(info.Token))
                return false;
            var token = NormalizeToken(info.Token);
            return ExtractBracketTokens(normalized).Contains(token);
        }

        internal static string NormalizeToken(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Trim('`', '[', ']')
                .ToLowerInvariant()
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty)
                .Replace("\t", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);
        }

        internal static string NormalizeTagText(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Trim('`')
                .ToLowerInvariant()
                .Replace("_", " ")
                .Replace("\t", " ")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        internal bool MatchesJobTag(string tagString, int job)
        {
            var normalized = NormalizeTagText(tagString);
            if (string.IsNullOrEmpty(normalized))
                return false;
            if (normalized.Contains("[all]", StringComparison.Ordinal))
                return true;
            foreach (var token in GetJobTags(job))
            {
                if (normalized.Contains(token, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal string[] GetJobTags(int job)
        {
            var token = GetToken(job);
            return string.IsNullOrWhiteSpace(token)
                ? Array.Empty<string>()
                : new[] { "[" + token.Trim().ToLowerInvariant().Replace("_", " ") + "]" };
        }

        private static PvfCharacterJobCatalog Load()
        {
            var byId = new Dictionary<int, JobInfo>();
            var byToken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var lst = PvfArchiveAccessor.ReadText("character/character.lst");
                foreach (Match match in LstPattern.Matches(lst ?? string.Empty))
                {
                    if (!int.TryParse(match.Groups[1].Value, out var jobId))
                        continue;
                    try
                    {
                        var path = "character/" + match.Groups[2].Value.Replace('\\', '/');
                        var text = PvfArchiveAccessor.ReadText(path);
                        var info = ParseJob(text, jobId);
                        if (string.IsNullOrWhiteSpace(info.Token))
                            continue;
                        byId[jobId] = info;
                        var token = NormalizeToken(info.Token);
                        if (!string.IsNullOrEmpty(token) && !byToken.ContainsKey(token))
                            byToken[token] = jobId;
                    }
                    catch
                    {
                        // One broken .chr must not hide valid jobs after it.
                    }
                }
            }
            catch
            {
                // An unavailable/invalid PVF produces an empty catalog. The
                // caller will return a PVF-not-ready error rather than revive
                // a stale hard-coded class list.
            }
            return new PvfCharacterJobCatalog(byId, byToken);
        }

        private static JobInfo ParseJob(string text, int id)
        {
            var info = new JobInfo { Id = id, MaxGrowCount = 0 };
            var jobMatch = JobPattern.Match(text ?? string.Empty);
            if (jobMatch.Success)
            {
                var tokenMatch = Regex.Match(jobMatch.Groups["value"].Value, @"\[([^\]]+)\]");
                info.Token = tokenMatch.Success
                    ? tokenMatch.Groups[1].Value.Trim().ToLowerInvariant()
                    : string.Empty;
            }

            var namesMatch = Regex.Match(text ?? string.Empty, @"\[growtype\s+name\]\s*(.+?)(?:\r?\n)", RegexOptions.IgnoreCase);
            var names = namesMatch.Success
                ? BacktickPattern.Matches(namesMatch.Groups[1].Value).Cast<Match>().Select(m => m.Groups[1].Value).ToList()
                : new List<string>();
            if (names.Count > 0)
                info.BaseName = names[0];

            var maxMarker = Regex.Match(text ?? string.Empty, @"\[max\s+grow\s+count\]", RegexOptions.IgnoreCase);
            if (maxMarker.Success)
            {
                info.HasMaxGrowCount = true;
                var maxValue = Regex.Match(
                    (text ?? string.Empty).Substring(maxMarker.Index + maxMarker.Length),
                    @"^(?:[ \t]*\r?\n[ \t]*|[ \t]+)(-?\d+)",
                    RegexOptions.IgnoreCase);
                if (maxValue.Success
                    && int.TryParse(maxValue.Groups[1].Value, out var maxGrow)
                    && maxGrow >= 0)
                    info.MaxGrowCount = maxGrow;
            }

            var growCount = info.HasMaxGrowCount
                ? Math.Min(info.MaxGrowCount, Math.Max(0, names.Count - 1))
                : Math.Max(0, names.Count - 1);
            for (var i = 0; i < growCount; i++)
                info.GrowTypeNames.Add(names[i + 1]);

            foreach (Match section in SectionPattern.Matches(text ?? string.Empty))
            {
                var sectionNumber = int.Parse(section.Groups[1].Value);
                var growType = sectionNumber - 1;
                // growtype section 1 is the direct-awakening branch for
                // jobs whose max grow count is explicitly zero (A21's
                // DSSwordman/CreatorMage). Keep it as branch 0; only later
                // sections are ordinary first-job branches.
                if (growType < 0 || (info.HasMaxGrowCount && growType > info.MaxGrowCount))
                    continue;
                var next = SectionPattern.Match(text ?? string.Empty, section.Index + section.Length);
                var end = next.Success ? next.Index : (text ?? string.Empty).Length;
                var sectionText = (text ?? string.Empty).Substring(section.Index, end - section.Index);
                var awakening = Regex.Match(sectionText, @"\[awakening\s+name\]\s*(.+?)(?:\r?\n)", RegexOptions.IgnoreCase);
                if (!awakening.Success)
                    continue;
                var awakeningNames = BacktickPattern.Matches(awakening.Groups[1].Value)
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToList();
                if (awakeningNames.Count > 0)
                    info.AwakeningNames[growType] = awakeningNames;
            }
            return info;
        }

        private static HashSet<string> ExtractBracketTokens(string value)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(value ?? string.Empty, @"\[([^\]]+)\]"))
            {
                var token = NormalizeToken(match.Groups[1].Value);
                if (!string.IsNullOrEmpty(token))
                    result.Add(token);
            }
            return result;
        }
    }
}
