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
        public sealed class QuestMeta
        {
            public int Id;
            public string Name;
            public string Grade;      // epic/normal/daily/... (去壳)
            public int MinLevel;
            public int[] PreRequired;     // 所有前置组的并集(反向索引/展示回退用)
            public int[][] PreGroups;     // 前置组: 组间 OR, 组内 AND (与服务端可接判定一致)
            public string Region;     // 目录名(小写)
            public string Job;        // [all]/[swordman]/... (去反引号, 小写)
            public int GrowType;
            public int JobChangeQuestValue;
            public int GrowNumber;    // jcq=1/2 时要授予的转职/觉醒编号
            public string TargetCharacter;
            public int TargetDungeonId; // [condition under clear]/[hunt monster] 的 int data 首位, 无/任意为 -1
            public int TargetMapId;     // [clear map] 的 int data 首位, 无为 -1
            public int TargetQuestId;   // [clear quest] 的 int data 首位(称号壳任务→成就本体), 无为 -1
            public int TargetLevel;     // [level up] 的 int data 首位(达到该等级自动完成), 无为 -1
            public int LinkedDungeonId; // [dungeon info] 首个副本ID, 否则怪物奖励表首个副本ID, 无为 -1
        }

        public string ResolveQuestName(int questId)
        {
            var meta = GetQuestMeta(questId);
            return meta != null ? meta.Name : null;
        }

        public QuestMeta GetQuestMeta(int questId)
        {
            var metas = _questMeta;
            if (metas == null)
                return null;
            QuestMeta meta;
            return metas.TryGetValue(questId, out meta) ? meta : null;
        }

        public IReadOnlyDictionary<int, QuestMeta> AllQuestMeta => _questMeta;

        public List<QuestMeta> SearchQuests(string query, int limit)
        {
            var result = new List<QuestMeta>();
            var metas = _questMeta;
            if (metas == null || string.IsNullOrWhiteSpace(query))
                return result;
            if (limit <= 0 || limit > 100)
                limit = 30;

            query = query.Trim();
            int numericId;
            var isNumeric = int.TryParse(query, out numericId);

            foreach (var pair in metas.OrderBy(p => p.Key))
            {
                if (result.Count >= limit)
                    break;
                if ((isNumeric && pair.Key == numericId) ||
                    (pair.Value.Name != null && pair.Value.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    result.Add(pair.Value);
                }
            }
            return result;
        }

        private static readonly Regex IntPattern = new Regex(@"-?\d+", RegexOptions.Compiled);
        private static readonly Regex PreGroupPattern = new Regex(
            @"\[pre required quest\](.*?)\[/pre required quest\]",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // quest.lst 路径本身就是权威分区: n_quest/<区域>/<子区域>/<类型>_<等级>_xxx.qst
        private Dictionary<int, QuestMeta> BuildQuestMeta(PvfArchive archive)
        {
            var result = new Dictionary<int, QuestMeta>();
            var lstPath = FindLstPath(archive, "quest.lst");
            if (lstPath == null)
                return result;

            var lstText = archive.GetFileContent(lstPath);
            if (string.IsNullOrEmpty(lstText))
                return result;

            var rootFolder = lstPath.Contains("/") ? lstPath.Substring(0, lstPath.LastIndexOf('/')) : string.Empty;
            var entries = new List<KeyValuePair<int, string>>();
            foreach (Match match in LstPattern.Matches(lstText))
            {
                int id;
                if (int.TryParse(match.Groups[1].Value, out id))
                    entries.Add(new KeyValuePair<int, string>(id, match.Groups[2].Value));
            }

            var metas = new QuestMeta[entries.Count];
            Parallel.For(0, entries.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                var relative = entries[i].Value.Replace('\\', '/');
                var fullPath = string.IsNullOrEmpty(rootFolder) ? relative : rootFolder + "/" + relative;
                try
                {
                    var text = archive.GetFileContent(fullPath);
                    if (string.IsNullOrEmpty(text))
                        return;
                    var model = QuestFile.Parse(text);

                    var segments = relative.ToLowerInvariant().Split('/');
                    var region = segments.Length > 1 ? segments[0] : "(root)";
                    if (region == "new_quest" && segments.Length > 2)
                        region = segments[1];

                    // 前置组从原文解析(多个 [pre required quest] 块 = 组间 OR 组内 AND,
                    // GmPvfLib 模型只保留单串会丢组结构)
                    var preGroups = new List<int[]>();
                    foreach (Match groupMatch in PreGroupPattern.Matches(text))
                    {
                        var ids = new List<int>();
                        foreach (Match m in IntPattern.Matches(groupMatch.Groups[1].Value))
                        {
                            int v;
                            if (int.TryParse(m.Value, out v) && v > 0)
                                ids.Add(v);
                        }
                        if (ids.Count > 0)
                            preGroups.Add(ids.ToArray());
                    }
                    var preRequired = preGroups.SelectMany(g => g).Distinct().ToList();

                    // int data 首位语义按 type 区分:
                    // [condition under clear] → 目标副本ID(-1=任意副本);
                    // [clear quest] → 目标任务ID(称号壳任务用它指向成就本体)
                    var targetDungeon = -1;
                    var targetMap = -1;
                    var targetQuest = -1;
                    var targetLevel = -1;
                    var type = (model.Type ?? "").ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(model.IntData))
                    {
                        var first = IntPattern.Match(model.IntData);
                        int v;
                        if (first.Success && int.TryParse(first.Value, out v) && v > 0)
                        {
                            if (type.Contains("condition under clear") || type.Contains("hunt monster"))
                                targetDungeon = v;   // 两类的 int data 首位都是副本ID
                            else if (type.Contains("clear map"))
                                targetMap = v;
                            else if (type.Contains("clear quest"))
                                targetQuest = v;
                            else if (type.Contains("level up"))
                                targetLevel = v;
                        }
                    }

                    // 关联副本: [dungeon info] 首个ID, 否则怪物奖励表首个副本ID
                    var linkedDungeon = -1;
                    if (!string.IsNullOrWhiteSpace(model.DungeonInfo))
                    {
                        var firstDungeon = IntPattern.Match(model.DungeonInfo);
                        int v;
                        if (firstDungeon.Success && int.TryParse(firstDungeon.Value, out v) && v > 0)
                            linkedDungeon = v;
                    }
                    if (linkedDungeon <= 0 && model.MonsterRewardItems != null && model.MonsterRewardItems.Count > 0)
                    {
                        var reward = model.MonsterRewardItems[0];
                        if (reward.DungeonId > 0)
                            linkedDungeon = reward.DungeonId;
                    }

                    metas[i] = new QuestMeta
                    {
                        Id = entries[i].Key,
                        Name = model.Name,
                        Grade = NormalizeGrade(model.Grade),
                        MinLevel = model.Level != null && model.Level.Length > 0 ? model.Level[0] : 0,
                        PreRequired = preRequired.ToArray(),
                        PreGroups = preGroups.ToArray(),
                        Region = region,
                        Job = (model.Job ?? "").Replace("`", "").Trim().ToLowerInvariant(),
                        GrowType = model.GrowType,
                        JobChangeQuestValue = model.JobChangeQuestValue,
                        GrowNumber = model.GrowNumber,
                        TargetCharacter = (model.TargetCharacter ?? "").Replace("`", "").Trim().ToLowerInvariant(),
                        TargetDungeonId = targetDungeon,
                        TargetMapId = targetMap,
                        TargetQuestId = targetQuest,
                        TargetLevel = targetLevel,
                        LinkedDungeonId = linkedDungeon,
                    };
                }
                catch
                {
                    Interlocked.Increment(ref _parseFailures);
                }
            });

            foreach (var meta in metas)
            {
                if (meta != null && !result.ContainsKey(meta.Id))
                    result[meta.Id] = meta;
            }
            return result;
        }

        private static string NormalizeGrade(string grade)
        {
            if (string.IsNullOrWhiteSpace(grade))
                return "";
            return grade.Replace("`", "").Trim().TrimStart('[').TrimEnd(']').ToLowerInvariant();
        }
    }
}
