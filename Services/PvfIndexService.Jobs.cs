using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using GmPvfLib;

namespace DfoGmTool.Services
{
    public sealed partial class PvfIndexService
    {
        private sealed class JobNameInfo
        {
            public string BaseName = "";
            public List<string> GrowTypeNames = new List<string>();
            public Dictionary<int, List<string>> AwakeningNames = new Dictionary<int, List<string>>();
        }

        private static readonly string[] FrontJobLabels =
        {
            "鬼剑士",
            "格斗家",
            "神枪手",
            "魔法师",
            "圣职者",
            "女神枪手",
            "暗夜使者",
            "男格斗家",
            "男魔法师",
            "黑暗武士",
            "缔造者",
            "女鬼剑士",
            "守护者",
        };

        private static bool IsFixedFrontJob(int job)
        {
            return job >= 0 && job < FrontJobLabels.Length;
        }

        public static string GetFrontJobLabel(int job)
        {
            return IsFixedFrontJob(job) ? FrontJobLabels[job] : "职业" + job;
        }

        private static bool HasNoGrowType(int job)
        {
            return job == 9 || job == 10;
        }

        public string ResolveJobName(int job, int growType)
        {
            if (IsFixedFrontJob(job) && HasNoGrowType(job))
                return GetFrontJobLabel(job);

            var jobs = _jobNames;
            if (jobs == null || !jobs.TryGetValue(job, out var info))
                return IsFixedFrontJob(job) ? GetFrontJobLabel(job) : null;

            if (growType == 0 && IsFixedFrontJob(job))
                return GetFrontJobLabel(job);

            var first = growType & 0xF;
            var second = (growType >> 4) & 0xF;

            if (second > 0 && first > 0 && info.AwakeningNames.TryGetValue(first, out var awakenings)
                && second <= awakenings.Count)
                return awakenings[second - 1];

            if (first > 0 && first <= info.GrowTypeNames.Count)
                return info.GrowTypeNames[first - 1];

            return info.BaseName.Length > 0 ? info.BaseName : null;
        }

        public object GetJobGrowOptions(int job)
        {
            if (IsFixedFrontJob(job) && HasNoGrowType(job))
                return new { baseName = GetFrontJobLabel(job), growTypes = new object[0] };

            var jobs = _jobNames;
            JobNameInfo info = null;
            if (jobs != null)
                jobs.TryGetValue(job, out info);
            if (info == null)
                return new { baseName = IsFixedFrontJob(job) ? GetFrontJobLabel(job) : (string)null, growTypes = new object[0] };

            var growTypes = new List<object>();
            for (var i = 0; i < info.GrowTypeNames.Count; i++)
            {
                if (IsPlaceholderGrowName(info.GrowTypeNames[i]))
                    continue;
                if (!HasGrowTypeQuestStage(job, i + 1, stage: 1))
                    continue;

                List<string> awakenings;
                info.AwakeningNames.TryGetValue(i + 1, out awakenings);
                growTypes.Add(new
                {
                    value = i + 1,
                    label = info.GrowTypeNames[i],
                    awakenings = awakenings != null
                        ? awakenings.Where(name => !IsPlaceholderGrowName(name)).ToArray()
                        : new string[0],
                });
            }

            return new { baseName = IsFixedFrontJob(job) ? GetFrontJobLabel(job) : info.BaseName, growTypes = growTypes.ToArray() };
        }

        public bool TryValidateJobGrowOption(int job, int first, int second, out string error)
        {
            error = null;
            if (job < 0 || job > byte.MaxValue)
            {
                error = "职业范围 0-255";
                return false;
            }
            if (first < 0 || first > 15 || second < 0 || second > 15)
            {
                error = "转职/觉醒范围必须为 0-15";
                return false;
            }
            if (second > 0 && first == 0)
            {
                error = "未转职不能设置觉醒";
                return false;
            }

            if (HasNoGrowType(job) && (first != 0 || second != 0))
            {
                error = GetFrontJobLabel(job) + "没有转职/觉醒分支";
                return false;
            }
            if (IsFixedFrontJob(job) && first == 0 && second == 0)
                return true;

            var jobs = _jobNames;
            JobNameInfo info = null;
            if (jobs == null || !jobs.TryGetValue(job, out info))
            {
                error = "PVF 中找不到职业: " + job;
                return false;
            }

            if (first == 0)
                return true;

            if (first > info.GrowTypeNames.Count
                || IsPlaceholderGrowName(info.GrowTypeNames[first - 1])
                || !HasGrowTypeQuestStage(job, first, stage: 1))
            {
                error = "PVF 中找不到该转职: job=" + job + ", first=" + first;
                return false;
            }

            List<string> awakenings;
            if (second > 0
                && (!info.AwakeningNames.TryGetValue(first, out awakenings)
                    || second > awakenings.Count
                    || IsPlaceholderGrowName(awakenings[second - 1])))
            {
                error = "PVF 中找不到该觉醒: job=" + job + ", first=" + first + ", second=" + second;
                return false;
            }

            return true;
        }

        private bool HasGrowTypeQuestStage(int job, int first, int stage)
        {
            var all = _questMeta;
            if (all == null || first <= 0)
                return true;

            var rewardChainType = stage == 1 ? 1 : 2;
            var growNumber = stage == 1 ? first : stage - 1;
            var grow = first | ((stage >= 3 ? 2 : stage >= 2 ? 1 : 0) << 4);
            return all.Values.Any(m => m != null
                && QuestMatchesJobGrow(m, job, grow)
                && (stage == 1 || m.GrowType == first)
                && ((m.RewardChainType == rewardChainType && m.GrowNumber == growNumber)
                    || (m.JobChangeQuestValue == stage
                        && (stage == 1 ? m.GrowNumber == growNumber : m.GrowNumber <= 0 || m.GrowNumber == growNumber))));
        }

        private static bool IsPlaceholderGrowName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;
            var normalized = name.Trim().Trim('`').Trim();
            return normalized.StartsWith("//", StringComparison.Ordinal)
                || normalized.StartsWith("growtype_name_", StringComparison.OrdinalIgnoreCase)
                || normalized == "??"
                || normalized == "？？";
        }

        private static bool QuestMatchesJobGrow(QuestMeta meta, int job, int growType)
        {
            if (!string.IsNullOrEmpty(meta.TargetCharacter) && !MatchesJobTag(meta.TargetCharacter, job))
                return false;
            if (!string.IsNullOrEmpty(meta.Job) && meta.Job != "[all]" && !MatchesJobTag(meta.Job, job))
                return false;

            var jcq = meta.JobChangeQuestValue;
            if (jcq == 2 || jcq == 3)
            {
                var firstGrow = growType & 0xF;
                if (meta.GrowType != -1 && meta.GrowType != firstGrow)
                    return false;
            }
            else if (meta.GrowType != -1 && jcq != 1 && jcq != 10 && jcq != 20 && growType >= 0)
            {
                if (meta.GrowType != growType)
                    return false;
            }
            return true;
        }

        private static bool MatchesJobTag(string tagString, int job)
        {
            if (string.IsNullOrEmpty(tagString))
                return false;
            var normalized = tagString
                .ToLowerInvariant()
                .Replace("_", " ")
                .Replace("\t", " ")
                .Replace("\r", " ")
                .Replace("\n", " ");
            if (normalized.Contains("[all]", StringComparison.Ordinal))
                return true;
            foreach (var token in GetJobTags(job))
            {
                if (normalized.Contains(token, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string[] GetJobTags(int job)
        {
            switch (job)
            {
                case 0: return new[] { "[swordman]" };
                case 1: return new[] { "[fighter]" };
                case 2: return new[] { "[gunner]" };
                case 3: return new[] { "[mage]" };
                case 4: return new[] { "[priest]" };
                case 5: return new[] { "[at gunner]" };
                case 6: return new[] { "[thief]" };
                case 7: return new[] { "[at fighter]" };
                case 8: return new[] { "[at mage]" };
                case 9: return new[] { "[demonic swordman]", "[demonicswordman]" };
                case 10: return new[] { "[creator mage]", "[creatormage]" };
                case 11: return new[] { "[at swordman]" };
                case 12: return new[] { "[knight]" };
                default: return Array.Empty<string>();
            }
        }

        public object[] GetAllJobOptions()
        {
            return FrontJobLabels
                .Select((label, value) => (object)new
                {
                    value,
                    label,
                })
                .ToArray();
        }

        private Dictionary<int, JobNameInfo> BuildJobNames(PvfArchive archive)
        {
            var result = new Dictionary<int, JobNameInfo>();
            string lst;
            try
            {
                lst = archive.GetFileContent("character/character.lst");
            }
            catch
            {
                return result;
            }

            if (string.IsNullOrEmpty(lst))
                return result;

            foreach (Match match in LstPattern.Matches(lst))
            {
                int jobId;
                if (!int.TryParse(match.Groups[1].Value, out jobId))
                    continue;

                try
                {
                    var text = archive.GetFileContent("character/" + match.Groups[2].Value.Replace('\\', '/'));
                    if (!string.IsNullOrEmpty(text))
                        result[jobId] = ParseJobNames(text);
                }
                catch
                {
                    Interlocked.Increment(ref _parseFailures);
                }
            }

            return result;
        }

        private static JobNameInfo ParseJobNames(string text)
        {
            var info = new JobNameInfo();

            var growNameMatch = Regex.Match(text, @"\[growtype name\]\s*(.+?)(?:\r?\n)", RegexOptions.IgnoreCase);
            if (growNameMatch.Success)
            {
                var names = BacktickPattern.Matches(growNameMatch.Groups[1].Value);
                if (names.Count > 0)
                    info.BaseName = names[0].Groups[1].Value;
                for (var i = 1; i < names.Count; i++)
                    info.GrowTypeNames.Add(names[i].Groups[1].Value);
            }

            for (var growType = 1; growType <= 6; growType++)
            {
                var section = growType + 1;
                var sectionStart = text.IndexOf("[growtype " + section + "]", StringComparison.OrdinalIgnoreCase);
                if (sectionStart < 0)
                    continue;

                var sectionEnd = text.Length;
                for (var next = section + 1; next <= 8; next++)
                {
                    var nextPos = text.IndexOf("[growtype " + next + "]", sectionStart + 1, StringComparison.OrdinalIgnoreCase);
                    if (nextPos >= 0)
                    {
                        sectionEnd = nextPos;
                        break;
                    }
                }

                var motionPos = text.IndexOf("[waiting motion]", sectionStart + 1, StringComparison.OrdinalIgnoreCase);
                if (motionPos >= 0 && motionPos < sectionEnd)
                    sectionEnd = motionPos;

                var sectionText = text.Substring(sectionStart, sectionEnd - sectionStart);
                var awakeningMatch = Regex.Match(sectionText, @"\[awakening name\]\s*(.+?)(?:\r?\n)", RegexOptions.IgnoreCase);
                if (awakeningMatch.Success)
                {
                    var list = new List<string>();
                    foreach (Match name in BacktickPattern.Matches(awakeningMatch.Groups[1].Value))
                        list.Add(name.Groups[1].Value);
                    if (list.Count > 0)
                        info.AwakeningNames[growType] = list;
                }
            }

            return info;
        }
    }
}
