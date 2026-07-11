using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GmPvfLib
{
    // 解析 etc/chn_absolute_bind_cube.etc。
    // 文件结构(按[absolute bind cube] key ... [/absolute bind cube]反复出现):
    //   [absolute bind cube]
    //   <key>
    //   [absolute bind plus rare]
    //   `[job]` `part` itemId `part` itemId ... `[job2]` ...
    //   [/absolute bind plus rare]
    //   [/absolute bind cube]
    // key即消耗品PVF [action type] [absolute bind cube] 的第1个参数, 每个key下按职业(PVF英文标签,
    // 如[swordman])分组列出该职业8个部位(hat/hair/face/neck/coat/pants/belt/shoes)各自的目标itemId。
    public sealed class AbsoluteBindCubeFile
    {
        // key -> job标签(含中括号, 如"[swordman]") -> part -> itemId
        public Dictionary<int, Dictionary<string, Dictionary<string, int>>> Cubes { get; } =
            new Dictionary<int, Dictionary<string, Dictionary<string, int>>>();

        public static AbsoluteBindCubeFile Parse(string content)
        {
            var file = new AbsoluteBindCubeFile();
            if (string.IsNullOrEmpty(content))
                return file;

            foreach (Match block in Regex.Matches(content,
                         @"\[absolute bind cube\]\s*(\d+)\s*\[absolute bind plus rare\](.*?)\[/absolute bind plus rare\]",
                         RegexOptions.Singleline))
            {
                if (!int.TryParse(block.Groups[1].Value, out var key))
                    continue;

                file.Cubes[key] = ParseJobPartItems(block.Groups[2].Value);
            }

            return file;
        }

        // body里反引号token是职业标签或部位名, 紧跟的裸数字是该部位的目标itemId。
        private static Dictionary<string, Dictionary<string, int>> ParseJobPartItems(string body)
        {
            var jobMap = new Dictionary<string, Dictionary<string, int>>();
            Dictionary<string, int> currentParts = null;
            string pendingPart = null;

            foreach (Match tok in Regex.Matches(body, "`([^`]*)`|(\\d+)"))
            {
                if (tok.Groups[1].Success)
                {
                    var value = tok.Groups[1].Value.Trim();
                    if (value.StartsWith("[") && value.EndsWith("]"))
                    {
                        currentParts = new Dictionary<string, int>();
                        jobMap[value] = currentParts;
                        pendingPart = null;
                    }
                    else
                    {
                        pendingPart = value;
                    }
                }
                else if (tok.Groups[2].Success && currentParts != null && pendingPart != null)
                {
                    if (int.TryParse(tok.Groups[2].Value, out var itemId))
                        currentParts[pendingPart] = itemId;
                    pendingPart = null;
                }
            }

            return jobMap;
        }
    }
}
