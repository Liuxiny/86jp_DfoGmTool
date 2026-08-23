using System;
using System.Collections.Generic;
using System.Linq;
using DfoGmTool.ServerCore.Game.Characters;
using GmPvfLib;

namespace DfoGmTool.Services
{
    public sealed partial class PvfIndexService
    {
        private sealed class JobNameInfo
        {
            public string Token = "";
            public string BaseName = "";
            public List<string> GrowTypeNames = new List<string>();
            public Dictionary<int, List<string>> AwakeningNames = new Dictionary<int, List<string>>();
        }

        public static string GetFrontJobLabel(int job)
        {
            return PvfCharacterJobCatalog.Current.GetLabel(job) ?? "职业" + job;
        }

        public string ResolveJobName(int job, int growType)
        {
            var jobs = _jobNames;
            if (jobs == null || !jobs.TryGetValue(job, out var info))
                return null;

            var first = growType & 0xF;
            var second = (growType >> 4) & 0xF;

            if (second > 0)
            {
                var awakenings = ValidAwakeningNames(info, first);
                if (second <= awakenings.Length)
                    return awakenings[second - 1];
            }

            if (first > 0 && first <= info.GrowTypeNames.Count)
                return info.GrowTypeNames[first - 1];

            return info.BaseName.Length > 0 ? info.BaseName : info.Token;
        }

        public object GetJobGrowOptions(int job)
        {
            var jobs = _jobNames;
            JobNameInfo info = null;
            if (jobs != null)
                jobs.TryGetValue(job, out info);
            if (info == null)
                return new { baseName = (string)null, growTypes = new object[0] };

            var growTypes = new List<object>();
            var directAwakenings = ValidAwakeningNames(info, 0);
            if (directAwakenings.Length > 0)
            {
                growTypes.Add(new
                {
                    value = 0,
                    label = info.BaseName.Length > 0 ? info.BaseName : info.Token,
                    awakenings = directAwakenings,
                });
            }

            for (var i = 0; i < info.GrowTypeNames.Count; i++)
            {
                if (IsPlaceholderGrowName(info.GrowTypeNames[i]))
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

            return new { baseName = info.BaseName.Length > 0 ? info.BaseName : info.Token, growTypes = growTypes.ToArray() };
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

            var jobs = _jobNames;
            JobNameInfo info = null;
            if (jobs == null || !jobs.TryGetValue(job, out info))
            {
                error = "PVF 中找不到职业: " + job;
                return false;
            }

            if (first == 0)
            {
                if (second == 0)
                    return true;

                var directAwakenings = ValidAwakeningNames(info, 0);
                if (second <= directAwakenings.Length)
                    return true;

                error = directAwakenings.Length == 0
                    ? "未转职不能设置觉醒"
                    : "PVF 中找不到该觉醒: job=" + job + ", first=0, second=" + second;
                return false;
            }

            if (first > info.GrowTypeNames.Count
                || IsPlaceholderGrowName(info.GrowTypeNames[first - 1])
                )
            {
                error = "PVF 中找不到该转职: job=" + job + ", first=" + first;
                return false;
            }

            if (second > 0
                && (ValidAwakeningNames(info, first).Length < second))
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

        private static string[] ValidAwakeningNames(JobNameInfo info, int first)
        {
            if (info == null
                || !info.AwakeningNames.TryGetValue(first, out var names)
                || names == null)
                return Array.Empty<string>();
            return names.Where(name => !IsPlaceholderGrowName(name)).ToArray();
        }

        private bool QuestMatchesJobGrow(QuestMeta meta, int job, int growType)
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

        private bool MatchesJobTag(string tagString, int job)
        {
            return PvfCharacterJobCatalog.Current.MatchesJobTag(tagString, job);
        }

        public object[] GetAllJobOptions()
        {
            var jobs = _jobNames;
            if (jobs == null)
                return Array.Empty<object>();
            return jobs.OrderBy(pair => pair.Key)
                .Select(pair => (object)new
                {
                    value = pair.Key,
                    label = pair.Value.BaseName.Length > 0 ? pair.Value.BaseName : pair.Value.Token,
                    token = pair.Value.Token,
                })
                .ToArray();
        }

        private Dictionary<int, JobNameInfo> BuildJobNames(PvfArchive archive)
        {
            var result = new Dictionary<int, JobNameInfo>();
            foreach (var pair in PvfCharacterJobCatalog.Current.Jobs)
            {
                var source = pair.Value;
                result[pair.Key] = new JobNameInfo
                {
                    Token = source.Token ?? "",
                    BaseName = source.BaseName ?? "",
                    GrowTypeNames = new List<string>(source.GrowTypeNames),
                    AwakeningNames = source.AwakeningNames.ToDictionary(
                        item => item.Key,
                        item => new List<string>(item.Value)),
                };
            }
            return result;
        }
    }
}
