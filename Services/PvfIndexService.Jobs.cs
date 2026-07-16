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

        public string ResolveJobName(int job, int growType)
        {
            var jobs = _jobNames;
            if (jobs == null || !jobs.TryGetValue(job, out var info))
                return null;

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
            var jobs = _jobNames;
            JobNameInfo info = null;
            if (jobs != null)
                jobs.TryGetValue(job, out info);
            if (info == null)
                return new { baseName = (string)null, growTypes = new object[0] };

            var growTypes = new List<object>();
            for (var i = 0; i < info.GrowTypeNames.Count; i++)
            {
                if (info.GrowTypeNames[i].StartsWith("//"))
                    continue;

                List<string> awakenings;
                info.AwakeningNames.TryGetValue(i + 1, out awakenings);
                growTypes.Add(new
                {
                    value = i + 1,
                    label = info.GrowTypeNames[i],
                    awakenings = awakenings != null ? awakenings.ToArray() : new string[0],
                });
            }

            return new { baseName = info.BaseName, growTypes = growTypes.ToArray() };
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
                || info.GrowTypeNames[first - 1].StartsWith("//", StringComparison.Ordinal))
            {
                error = "PVF 中找不到该转职: job=" + job + ", first=" + first;
                return false;
            }

            List<string> awakenings;
            if (second > 0
                && (!info.AwakeningNames.TryGetValue(first, out awakenings)
                    || second > awakenings.Count))
            {
                error = "PVF 中找不到该觉醒: job=" + job + ", first=" + first + ", second=" + second;
                return false;
            }

            return true;
        }

        public object[] GetAllJobOptions()
        {
            var jobs = _jobNames;
            if (jobs == null)
                return Array.Empty<object>();

            return jobs
                .OrderBy(pair => pair.Key)
                .Select(pair => (object)new
                {
                    value = pair.Key,
                    label = pair.Value.BaseName,
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
