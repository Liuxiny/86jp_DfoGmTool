using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

        // 最终职业名: 觉醒名 > 转职名 > 基础名。growType 低4位=转职, 高4位=觉醒
        // (与服务端 CharacterStatComputer.DecodeGrowType 同一位布局)
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

        // 该职业的转职名列表与各转职的觉醒名列表(来自 .chr), 供 GM 下拉选项
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
                // 数据里的注释占位项(如 //(后续确认)剑魔 = 本版本未开放)不进选项
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

        // character/character.lst → 每职业 .chr:
        // [growtype name] 首个反引号=基础名, 其后=各转职名;
        // [growtype N] 段内 [awakening name] = 该转职的觉醒名
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

            // [growtype 1] 是基础职业段, 转职 N 的数据在 [growtype N+1] 段
            // (swordman.chr 实证: 狂战士=first 3, 其觉醒名 狱血魔神/帝血弑天 在 [growtype 4])
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
                    if (nextPos >= 0) { sectionEnd = nextPos; break; }
                }
                var motionPos = text.IndexOf("[waiting motion]", sectionStart + 1, StringComparison.OrdinalIgnoreCase);
                if (motionPos >= 0 && motionPos < sectionEnd)
                    sectionEnd = motionPos;

                var sectionText = text.Substring(sectionStart, sectionEnd - sectionStart);
                // 段内 [awakening name] 一行同时给出一觉/二觉名(在 [awakening 1]/[awakening 2] 前重复出现, 取首个即可)
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
