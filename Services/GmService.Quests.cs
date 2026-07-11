using System;
using System.Collections.Generic;
using System.Linq;
using DfoGmTool.ServerCore.Game.TitleBook;
using DfoGmTool.ServerCore.Game.Characters;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.Dungeon;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.Quests;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        public object ListQuests(int characterId, PvfIndexService pvfIndex)
        {
            var quests = new List<object>();
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT slot, quest_id, trigger_value
FROM character_active_quests
WHERE character_id = @cid
ORDER BY slot;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var questId = reader.GetInt32(1);
                            quests.Add(new
                            {
                                slot = reader.GetInt32(0),
                                questId,
                                name = pvfIndex.ResolveQuestName(questId),
                                triggerValue = reader.GetInt64(2),
                            });
                        }
                    }
                }
            }
            return new { characterId, count = quests.Count, quests };
        }

        // 把进行中任务的触发计数清零, 客户端回城即可正常交付, 奖励走正常发放流程
        public object MarkQuestReady(int characterId, int questId)
        {
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
UPDATE character_active_quests SET trigger_value = 0
WHERE character_id = @cid AND quest_id = @qid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    cmd.Parameters.AddWithValue("@qid", questId);
                    if (cmd.ExecuteNonQuery() == 0)
                        return Error("该角色没有进行中的任务 " + questId);
                }
            }
            return new { success = true, characterId, questId };
        }

        // 客户端词条: epic=主线(dstr 6562), normal=普通任务, daily=每日; 其余保留原始标记
        private static string GradeLabel(string grade)
        {
            switch (grade)
            {
                case "epic": return "主线";
                case "normal": return "普通";
                case "daily": return "每日";
                case "repeat": return "重复";
                case "achievement": return "成就";
                case null: case "": return "?";
                default: return grade;
            }
        }

        private static object DescribeQuest(PvfIndexService.QuestMeta meta, PvfIndexService pvfIndex, string status)
        {
            return new
            {
                questId = meta.Id,
                name = meta.Name,
                grade = meta.Grade,
                gradeLabel = GradeLabel(meta.Grade),
                region = meta.Region,
                regionLabel = pvfIndex.ResolveRegionName(meta.Region),
                minLevel = meta.MinLevel,
                status,
            };
        }

        private (HashSet<int> Active, Dictionary<int, int> Cleared) LoadQuestState(int characterId)
        {
            var active = new HashSet<int>();
            Dictionary<int, int> cleared;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT quest_id FROM character_active_quests WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            active.Add(reader.GetInt32(0));
                    }
                }
                cleared = QuestRepository.LoadClearedFlags(conn, null, characterId);
            }
            return (active, cleared);
        }

        private static string QuestStatus(int questId, HashSet<int> active, Dictionary<int, int> cleared)
        {
            if (active.Contains(questId))
                return "进行中";
            int flag;
            return cleared.TryGetValue(questId, out flag) && flag != 0 ? "已完成" : "未完成";
        }

        public object ListClearedQuests(int characterId, PvfIndexService pvfIndex)
        {
            var quests = new List<object>();
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                var flags = QuestRepository.LoadClearedFlags(conn, null, characterId);
                foreach (var pair in flags.OrderBy(p => p.Key))
                {
                    if (pair.Value == 0)
                        continue;
                    var meta = pvfIndex.GetQuestMeta(pair.Key);
                    quests.Add(new
                    {
                        questId = pair.Key,
                        name = meta != null ? meta.Name : null,
                        grade = meta != null ? meta.Grade : null,
                        gradeLabel = meta != null ? GradeLabel(meta.Grade) : "?",
                        region = meta != null ? meta.Region : null,
                        regionLabel = meta != null ? pvfIndex.ResolveRegionName(meta.Region) : null,
                        minLevel = meta != null ? meta.MinLevel : 0,
                    });
                }
            }
            return new { characterId, count = quests.Count, quests };
        }

        // 剧情主线的收录条件: epic 且非功能性分组(event/pvp)、非远古体系(elvengard 残留)
        private static bool IsMainStoryEpic(PvfIndexService.QuestMeta m)
        {
            return m.Grade == "epic"
                && m.Region != "event"
                && m.Region != "pvp"
                && m.Region != "elvengard";
        }

        // 主线总览: 剧情主线按区域分组
        public object MainQuestOverview(int characterId, PvfIndexService pvfIndex)
        {
            return BuildQuestOverview(characterId, pvfIndex, IsMainStoryEpic,
                mergeUnresolvedToOther: false);
        }


        // 成就总览: 按区域分组, 无地理区域的目录(如 Title/)归并到"其他"
        public object AchievementOverview(int characterId, PvfIndexService pvfIndex)
        {
            // v3: 两个集合 — 【称号】= 出现在称号簿(etc/titlebook.etc)里的成就,
            // 按簿内五页分类(与客户端称号簿页签一致); 【其他】= 不在称号簿里的。
            // 映射来自服务端 TitleBookStaticDataProvider 解析的槽位 QuestId。
            var all = pvfIndex.AllQuestMeta;
            if (all == null)
                return Error("任务索引还在构建中, 稍等几秒");

            int charJob = -1, charGrow = -1;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT job, grow_type FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            charJob = reader.GetInt32(0);
                            charGrow = reader.GetInt32(1);
                        }
                    }
                }
            }
            if (charJob < 0)
                return Error("角色不存在: " + characterId);

            var (active, cleared) = LoadQuestState(characterId);
            var slots = EnsureTitleBookSlots();
            var job = charJob;
            var grow = charGrow;

            // 称号集合: 以簿槽为纲。壳任务两种形态:
            // A. [clear quest] 壳 → int data 指向成就本体(普通/特殊成就多为此);
            // B. 壳自带条件([condition under clear]通塔层 / [pvp quest]段位等),
            //    无本体且壳无名字, 显示名取奖励称号物品名
            var referenced = new HashSet<int>();
            var regionList = new List<object>();

            for (var category = 0; category < TitleCategoryLabels.Length; category++)
            {
                var rows = new List<object>();
                var completedCount = 0;
                var minLevelOfCategory = int.MaxValue;

                foreach (var slot in slots.Where(s => s.Category == category).OrderBy(s => s.Index))
                {
                    var shell = pvfIndex.GetQuestMeta(slot.ShellQuestId);
                    var target = shell != null && shell.TargetQuestId > 0
                        ? pvfIndex.GetQuestMeta(shell.TargetQuestId)
                        : null;

                    referenced.Add(slot.ShellQuestId);
                    if (target != null)
                        referenced.Add(target.Id);

                    // 职业过滤看条件承载者(本体优先, 无本体看壳)
                    var gate = target ?? shell;
                    if (gate != null && !QuestMatchesCharacter(gate, job, grow))
                        continue;

                    var name = (target != null ? target.Name : null)
                        ?? (shell != null ? shell.Name : null)
                        ?? pvfIndex.ResolveItemName(slot.RewardItemId);

                    var minLevel = gate != null ? EffectiveLevel(gate) : 0;
                    if (minLevel < minLevelOfCategory)
                        minLevelOfCategory = minLevel;

                    // 壳 cleared = 称号已领取; 本体 cleared 但壳未领 = 条件达成
                    string status;
                    if (QuestStatus(slot.ShellQuestId, active, cleared) == "已完成")
                    {
                        status = "已完成";
                        completedCount++;
                    }
                    else if (target != null && QuestStatus(target.Id, active, cleared) == "已完成")
                    {
                        status = "条件达成";
                    }
                    else
                    {
                        status = "未完成";
                    }

                    // 前置列只放本体自己的前置链。本体自身不进前置列——行名就取自本体,
                    // 塞进去会显示成"自己是自己的前置"; 本体完成与否由"条件达成"状态表达,
                    // "连前置完成"的服务端闭包(CompleteQuestChain)本来就含本体, 不受显示影响
                    var pre = new List<object>();
                    if (target != null)
                    {
                        foreach (var pid in SelectPreGroup(target, job, grow, pvfIndex))
                        {
                            pre.Add(new
                            {
                                questId = pid,
                                name = pvfIndex.ResolveQuestName(pid),
                                done = QuestStatus(pid, active, cleared) == "已完成",
                            });
                        }
                    }

                    rows.Add(new
                    {
                        questId = slot.ShellQuestId,
                        name,
                        minLevel,
                        status,
                        preRequired = pre.ToArray(),
                    });
                }

                if (rows.Count == 0)
                    continue;

                regionList.Add(new
                {
                    region = "titlebook" + category,
                    regionLabel = TitleCategoryLabels[category],
                    group = "称号",
                    minLevel = minLevelOfCategory == int.MaxValue ? 0 : minLevelOfCategory,
                    total = rows.Count,
                    completed = completedCount,
                    quests = rows.ToArray(),
                });
            }

            // 其他集合: 不被任何簿槽(壳或本体)引用的成就任务, 按体系再分标签:
            // 深渊派对(名字含"深渊派对") / 远古地下城(目标副本在 ancient/
            // timegaterequiem 区, 或名字带"远古") / 觉醒(jcq==2 或名字以"觉醒"开头)
            // 全数据信号分类(不做名字匹配):
            // 深渊派对 = 文件在 Hell/ 目录;
            // 远古 = 条件目标副本(直接目标, 或 [clear map] 地图→所属副本)落在
            //        ancient/timegaterequiem 世界地图区, 或文件在远古内容目录
            //        (alphraira=王遗迹等重制, requiem=镇魂曲, elvengard=通缉令/悲鸣链);
            // 觉醒 = 服务端 [job change quest] == 2 标记
            var ancientFolders = new HashSet<string> { "alphraira", "requiem", "elvengard" };

            int EffectiveTargetDungeon(PvfIndexService.QuestMeta m)
            {
                if (m.TargetDungeonId > 0)
                    return m.TargetDungeonId;
                return m.TargetMapId > 0 ? pvfIndex.ResolveMapDungeon(m.TargetMapId) : -1;
            }

            string OtherTag(PvfIndexService.QuestMeta m)
            {
                if (m.Region == "hell")
                    return "hellparty";
                var dungeonRegion = pvfIndex.ResolveDungeonRegion(EffectiveTargetDungeon(m));
                if (dungeonRegion == "ancient" || dungeonRegion == "timegaterequiem"
                    || ancientFolders.Contains(m.Region))
                    return "ancientdungeon";
                if (m.JobChangeQuestValue == 2)
                    return "awakening";
                return "__other__";
            }

            var otherTagOrder = new[] { "hellparty", "ancientdungeon", "awakening", "__other__" };
            var otherTagLabels = new Dictionary<string, string>
            {
                { "hellparty", "深渊派对" },
                { "ancientdungeon", "远古地下城" },
                { "awakening", "觉醒" },
                { "__other__", "其他" },
            };

            var otherQuests = all.Values
                .Where(m => m.Grade == "achievement"
                    && !referenced.Contains(m.Id)
                    && QuestMatchesCharacter(m, job, grow))
                .ToList();

            // 初始标签 + 沿前置边传播: 链上的交付环/开门环通常自身无条件目标,
            // 但与已归类任务直接相连 — 邻居(前置或后继)标签唯一时继承之
            var tags = otherQuests.ToDictionary(m => m.Id, OtherTag);
            for (var round = 0; round < 5; round++)
            {
                var changed = false;
                foreach (var m in otherQuests)
                {
                    if (tags[m.Id] != "__other__")
                        continue;

                    var neighborTags = new HashSet<string>();
                    foreach (var pid in m.PreRequired)
                    {
                        string t;
                        if (tags.TryGetValue(pid, out t) && t != "__other__")
                            neighborTags.Add(t);
                    }
                    foreach (var o in otherQuests)
                    {
                        if (tags[o.Id] != "__other__" && o.PreRequired.Contains(m.Id))
                            neighborTags.Add(tags[o.Id]);
                    }

                    if (neighborTags.Count == 1)
                    {
                        tags[m.Id] = neighborTags.First();
                        changed = true;
                    }
                }
                if (!changed)
                    break;
            }

            var others = otherQuests
                .GroupBy(m => tags[m.Id])
                .OrderBy(g => Array.IndexOf(otherTagOrder, g.Key));

            foreach (var tagGroup in others)
            {
                var quests = tagGroup.OrderBy(m => EffectiveLevel(m)).ThenBy(m => m.Id).ToList();
                regionList.Add(new
                {
                    region = tagGroup.Key,
                    regionLabel = otherTagLabels[tagGroup.Key],
                    group = "其他",
                    minLevel = quests.Min(m => EffectiveLevel(m)),
                    total = quests.Count,
                    completed = quests.Count(m => QuestStatus(m.Id, active, cleared) == "已完成"),
                    quests = quests.Select(m => (object)new
                    {
                        questId = m.Id,
                        name = m.Name,
                        minLevel = EffectiveLevel(m),
                        status = QuestStatus(m.Id, active, cleared),
                        preRequired = SelectPreGroup(m, job, grow, pvfIndex).Select(pid => (object)new
                        {
                            questId = pid,
                            name = pvfIndex.ResolveQuestName(pid),
                            done = QuestStatus(pid, active, cleared) == "已完成",
                        }).ToArray(),
                    }).ToArray(),
                });
            }

            return new { characterId, regions = regionList.ToArray() };
        }

        // 前置组语义: 组间 OR 组内 AND(与服务端可接判定一致)。展示与补链只取
        // "该角色相关"的一组: 优先所有成员都通过职业匹配的组, 否则退回第一组
        private static int[] SelectPreGroup(PvfIndexService.QuestMeta m, int job, int grow, PvfIndexService pvfIndex)
        {
            if (m.PreGroups == null || m.PreGroups.Length == 0)
                return Array.Empty<int>();
            foreach (var group in m.PreGroups)
            {
                var allMatch = true;
                foreach (var pid in group)
                {
                    var preMeta = pvfIndex.GetQuestMeta(pid);
                    if (preMeta != null && !QuestMatchesCharacter(preMeta, job, grow))
                    {
                        allMatch = false;
                        break;
                    }
                }
                if (allMatch)
                    return group;
            }
            return m.PreGroups[0];
        }

        // [level up] 型任务(达到等级自动完成)的实际门槛在 int data 里,
        // [level] 只是接取窗口(如 时代先锋: level=10-99 但条件是达到Lv70)
        private static int EffectiveLevel(PvfIndexService.QuestMeta m)
        {
            return m.TargetLevel > 0 ? m.TargetLevel : m.MinLevel;
        }

        // ── 职业/转职匹配, 与服务端 QuestData.MatchesJob/MatchesGrowType 同语义 ──

        private static int GetBaseJobIndex(int job)
        {
            switch (job)
            {
                case 0: case 9: case 11: return 0;   // swordman / ds / at
                case 1: case 7: return 1;            // fighter / at
                case 2: case 5: return 2;            // gunner / at
                case 3: case 8: case 10: return 3;   // mage / at / creator
                case 4: return 4;                    // priest
                case 6: return 5;                    // thief
                case 12: return 6;                   // knight
                default: return -1;
            }
        }

        private static bool IsAtVariant(int job)
            => job == 5 || job == 7 || job == 8 || job == 9 || job == 10 || job == 11;

        private static readonly string[] JobTags =
            { "[swordman]", "[fighter]", "[gunner]", "[mage]", "[priest]", "[thief]", "[knight]" };
        private static readonly string[] AtJobTags =
            { "[at swordman]", "[at fighter]", "[at gunner]", "[at mage]", "[at priest]", "[at thief]", "[at knight]" };

        private static bool MatchesJobTag(string tagString, int job)
        {
            var baseIdx = GetBaseJobIndex(job);
            if (baseIdx < 0 || baseIdx >= JobTags.Length)
                return false;
            return IsAtVariant(job)
                ? tagString.Contains(AtJobTags[baseIdx])
                : tagString.Contains(JobTags[baseIdx]);
        }

        // jcq=1: 一转任务不查growType; jcq=2: 觉醒任务只比转职位; jcq=10/20: 跳过
        private static bool QuestMatchesCharacter(PvfIndexService.QuestMeta m, int job, int growType)
        {
            if (!string.IsNullOrEmpty(m.TargetCharacter) && !MatchesJobTag(m.TargetCharacter, job))
                return false;
            if (!string.IsNullOrEmpty(m.Job) && m.Job != "[all]" && !MatchesJobTag(m.Job, job))
                return false;

            var jcq = m.JobChangeQuestValue;
            if (jcq == 2)
            {
                var firstGrow = growType & 0xF;
                if (m.GrowType != -1 && m.GrowType != firstGrow)
                    return false;
            }
            else if (m.GrowType != -1 && jcq != 1 && jcq != 10 && jcq != 20 && growType >= 0)
            {
                if (m.GrowType != growType)
                    return false;
            }
            return true;
        }

        private object BuildQuestOverview(int characterId, PvfIndexService pvfIndex,
            Func<PvfIndexService.QuestMeta, bool> filter, bool mergeUnresolvedToOther,
            bool groupByTargetDungeon = false, bool groupByLinkedDungeon = false)
        {
            var all = pvfIndex.AllQuestMeta;
            if (all == null)
                return Error("任务索引还在构建中, 稍等几秒");

            int charJob = -1, charGrow = -1;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT job, grow_type FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            charJob = reader.GetInt32(0);
                            charGrow = reader.GetInt32(1);
                        }
                    }
                }
            }
            if (charJob < 0)
                return Error("角色不存在: " + characterId);

            var (active, cleared) = LoadQuestState(characterId);
            var baseFilter = filter;
            filter = m => baseFilter(m) && QuestMatchesCharacter(m, charJob, charGrow);

            // ancient 与 timegaterequiem 两张 wdm 的 [name] 都是"时空之门 - 镇魂曲",
            // 同一体系并为一组
            string Canonical(string region) => region == "ancient" ? "timegaterequiem" : region;

            string GroupKey(PvfIndexService.QuestMeta m)
            {
                if (groupByTargetDungeon)
                {
                    var r = pvfIndex.ResolveDungeonRegion(m.TargetDungeonId);
                    return r != null ? Canonical(r) : "__other__";
                }

                if (groupByLinkedDungeon)
                {
                    var dungeonId = m.TargetDungeonId > 0 ? m.TargetDungeonId
                        : m.TargetMapId > 0 ? pvfIndex.ResolveMapDungeon(m.TargetMapId)
                        : -1;
                    if (dungeonId <= 0)
                        dungeonId = m.LinkedDungeonId;
                    var dungeonRegion = pvfIndex.ResolveDungeonRegion(dungeonId);
                    if (dungeonRegion != null)
                        return Canonical(dungeonRegion);
                    return pvfIndex.IsOpenHubRegion(m.Region) ? m.Region : "__other__";
                }

                if (!mergeUnresolvedToOther)
                    return m.Region;
                // 区域名解析不出来(既非城镇也非世界地图区域) = 无地理区域 → 其他
                var label = pvfIndex.ResolveRegionName(m.Region);
                return label == m.Region ? "__other__" : m.Region;
            }

            var regions = all.Values
                .Where(filter)
                .GroupBy(GroupKey)
                // 等级并列时按区域内最小任务ID排序(任务ID随内容加入时序递增,
                // 实证: 安徒恩 2489-2531 早于克洛诺斯岛 3000-3052); "其他"永远排最后
                .OrderBy(g => g.Key == "__other__" ? 1 : 0)
                .ThenBy(g => g.Min(m => m.MinLevel))
                .ThenBy(g => g.Min(m => m.Id))
                .Select(g => (object)new
                {
                    region = g.Key,
                    regionLabel = g.Key == "__other__" ? "其他" : pvfIndex.ResolveRegionName(g.Key),
                    minLevel = g.Min(m => m.MinLevel),
                    total = g.Count(),
                    completed = g.Count(m => QuestStatus(m.Id, active, cleared) == "已完成"),
                    quests = g.OrderBy(m => m.MinLevel).ThenBy(m => m.Id)
                        .Select(m => (object)new
                        {
                            questId = m.Id,
                            name = m.Name,
                            minLevel = m.MinLevel,
                            status = QuestStatus(m.Id, active, cleared),
                            preRequired = SelectPreGroup(m, charJob, charGrow, pvfIndex).Select(pid => (object)new
                            {
                                questId = pid,
                                name = pvfIndex.ResolveQuestName(pid),
                                done = QuestStatus(pid, active, cleared) == "已完成",
                            }).ToArray(),
                        }).ToArray(),
                })
                .ToArray();

            return new { characterId, regions };
        }

        // 连同前置链一起标记完成(BFS 闭包), 不发奖励
        public object CompleteQuestChain(int characterId, int questId, PvfIndexService pvfIndex)
        {
            var all = pvfIndex.AllQuestMeta;
            if (all == null)
                return Error("任务索引还在构建中, 稍等几秒");
            if (!all.ContainsKey(questId))
                return Error("任务不存在: " + questId);

            // 闭包按角色职业选前置组(组间OR只需满足一组, 补其它职业的组是多余写入)
            int chainJob = -1, chainGrow = -1;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT job, grow_type FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            chainJob = reader.GetInt32(0);
                            chainGrow = reader.GetInt32(1);
                        }
                    }
                }
            }

            var closure = new List<int>();
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(questId);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!seen.Add(id))
                    continue;
                closure.Add(id);
                PvfIndexService.QuestMeta meta;
                if (all.TryGetValue(id, out meta))
                {
                    foreach (var pid in SelectPreGroup(meta, chainJob, chainGrow, pvfIndex))
                        queue.Enqueue(pid);
                    // 称号壳任务([clear quest])经 int data 依赖成就本体, 一并纳入闭包
                    if (meta.TargetQuestId > 0)
                        queue.Enqueue(meta.TargetQuestId);
                }
            }

            var completed = new List<int>();
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    var cleared = QuestRepository.LoadClearedFlags(conn, tx, characterId);
                    foreach (var id in closure)
                    {
                        if (id <= 0 || id > ushort.MaxValue)
                            continue;
                        int flag;
                        if (cleared.TryGetValue(id, out flag) && flag != 0)
                            continue;

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "DELETE FROM character_active_quests WHERE character_id = @cid AND quest_id = @qid;";
                            cmd.Parameters.AddWithValue("@cid", characterId);
                            cmd.Parameters.AddWithValue("@qid", id);
                            cmd.ExecuteNonQuery();
                        }
                        QuestRepository.MarkQuestCleared(conn, tx, characterId, (ushort)id, 1);
                        completed.Add(id);
                    }
                    tx.Commit();
                }
            }

            // 链里若含称号簿壳任务, 逐个走服务端成就链入簿(独立连接)
            var titles = new List<int>();
            var growChanged = false;
            foreach (var id in completed)
            {
                DeliverTitleIfBookShell(characterId, id, titles);
                // 链里含转职/觉醒任务(jcq=1/2)时同步授予并重算属性
                PvfIndexService.QuestMeta completedMeta;
                if (all.TryGetValue(id, out completedMeta))
                    growChanged |= ApplyGrowTypeFromQuest(characterId, completedMeta);
            }

            return new { success = true, characterId, questId, chainSize = closure.Count, completedCount = completed.Count, completed, titlesDelivered = titles.Count, growChanged };
        }

        // 撤销完成标记(位图逻辑), 任务可重新接取
        public object UnclearQuest(int characterId, int questId)
        {
            if (questId <= 0 || questId > ushort.MaxValue)
                return Error("questId 无效");

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    QuestRepository.DeleteClearedFlag(conn, tx, characterId, (ushort)questId);
                    tx.Commit();
                }
            }

            // 称号簿壳任务: 取消完成时把称号从簿槽撤下
            RemoveTitleIfBookShell(characterId, questId);

            return new { success = true, characterId, questId };
        }

        // 任务库搜索: PVF 全量任务 + 该角色的进行中/已完成状态 + 类型/区域/等级
        public object SearchQuests(int characterId, string query, int limit, PvfIndexService pvfIndex)
        {
            var matches = pvfIndex.SearchQuests(query, limit);
            if (matches.Count == 0)
                return new { characterId, query, count = 0, results = new object[0] };

            var (active, cleared) = LoadQuestState(characterId);
            var results = matches
                .Select(m => DescribeQuest(m, pvfIndex, QuestStatus(m.Id, active, cleared)))
                .ToArray();

            return new { characterId, query, count = results.Length, results };
        }

        // 强制完成: 从进行中移除并用服务端的位图逻辑写入已完成标记(不发奖励)
        public object ForceCompleteQuest(int characterId, int questId)
        {
            if (questId <= 0 || questId > ushort.MaxValue)
                return Error("questId 无效");

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
DELETE FROM character_active_quests
WHERE character_id = @cid AND quest_id = @qid;";
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        cmd.Parameters.AddWithValue("@qid", questId);
                        cmd.ExecuteNonQuery();
                    }

                    QuestRepository.MarkQuestCleared(conn, tx, characterId, (ushort)questId, 1);
                    tx.Commit();
                }
            }

            // 称号簿壳任务: 走服务端成就链把称号送进簿槽(独立连接, 不能并入上面的事务)
            var titles = new List<int>();
            DeliverTitleIfBookShell(characterId, questId, titles);

            // 转职/觉醒任务(jcq=1/2): 同步授予转职并重算战斗属性
            var growChanged = ApplyGrowTypeFromQuest(characterId, _pvfIndex.GetQuestMeta(questId));

            return new { success = true, characterId, questId, titleDelivered = titles.Count > 0, growChanged };
        }

        // 整链完成的下行部分: 前端把展示中的链子树按顺序发来, 逐个走单任务完成的全套逻辑
        // (称号簿投递/转职觉醒应用)。上行前置由前端先调 complete-chain 覆盖。
        public object CompleteQuestBatch(int characterId, List<int> questIds)
        {
            if (questIds == null || questIds.Count == 0)
                return Error("questIds 为空");
            if (questIds.Count > 1000)
                return Error("一次最多 1000 个任务");

            var completed = new List<int>();
            foreach (var qid in questIds.Distinct())
            {
                if (qid <= 0 || qid > ushort.MaxValue)
                    continue;
                ForceCompleteQuest(characterId, qid);
                completed.Add(qid);
            }
            return new { success = true, characterId, completedCount = completed.Count };
        }
    }
}
