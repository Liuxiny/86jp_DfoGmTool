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
SELECT slot, quest_id, trigger_value, version, activation_id
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
                                version = reader.GetInt64(3),
                                activationId = reader.GetString(4),
                            });
                        }
                    }
                }
            }
            return new { characterId, count = quests.Count, quests };
        }

        // 把进行中任务的触发计数清零, 客户端回城即可正常交付, 奖励走正常发放流程
        public object MarkQuestReady(
            int characterId,
            int questId,
            string expectedActivationId = null)
        {
            if (!QuestActivationId.TryParse(expectedActivationId, out var expectedActivation))
                return Error("任务运行身份无效，请刷新任务列表后重试");

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    var activeQuest = QuestRepository.LoadActiveQuests(conn, tx, characterId)
                        .FirstOrDefault(quest => quest.QuestId == questId);
                    if (activeQuest == null)
                        return Error("该角色没有进行中的任务 " + questId);
                    if (!activeQuest.ActivationId.Equals(expectedActivation))
                        return Error("任务已经重新接取或被服务端替换，请刷新任务列表后重试");

                    if (!QuestRepository.TryUpdateTriggerValueCas(
                        conn,
                        tx,
                        characterId,
                        activeQuest.QuestId,
                        activeQuest.ActivationId,
                        activeQuest.Version,
                        activeQuest.TriggerValue,
                        0))
                    {
                        return Error("任务状态已被服务端更新，请刷新任务列表后重试");
                    }
                    tx.Commit();
                    return new
                    {
                        success = true,
                        characterId,
                        questId,
                        activationId = activeQuest.ActivationId.ToString(),
                        version = activeQuest.Version + 1,
                    };
                }
            }
        }

        // 客户端词条: epic=主线(dstr 6562), normal=普通任务, daily=每日; 其余保留原始标记
        private static string GradeLabel(string grade)
        {
            switch (grade)
            {
                case "epic": return "主线";
                case "side": return "外传";
                case "normal": return "普通";
                case "daily": return "每日";
                case "daily random": return "随机每日";
                case "special daily": return "特殊每日";
                case "repeat": return "重复";
                case "normaly repeat": return "重复";
                case "achievement": return "成就";
                case "training": return "训练";
                case "common unique": return "职业";
                case "system": return "系统";
                case "sub": return "支线";
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

        private static bool IsSideQuest(PvfIndexService.QuestMeta m)
        {
            return m.Grade == "side"
                && m.Region != "event"
                && m.Region != "pvp";
        }

        private static bool IsSystemQuest(PvfIndexService.QuestMeta m)
        {
            return m.Grade == "system"
                && m.Region != "event"
                && m.Region != "pvp"
                && m.RewardChainType != 21
                && !ExtraEquipmentSlotQuestIds.Contains(m.Id);
        }

        // 主线总览: 剧情主线与外传按区域分组, 侧栏用 group 分开显示。
        public object MainQuestOverview(int characterId, PvfIndexService pvfIndex)
        {
            return BuildQuestOverview(characterId, pvfIndex, m => IsMainStoryEpic(m) || IsSideQuest(m),
                mergeUnresolvedToOther: false,
                groupLabelSelector: m => IsSideQuest(m) ? "外传" : "主线");
        }

        public object AllVisibleQuestOverview(int characterId, PvfIndexService pvfIndex)
        {
            var all = pvfIndex.AllQuestMeta;
            if (all == null)
                return Error("任务索引还在构建中，稍等几秒");

            var level = -1;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT level FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    var value = cmd.ExecuteScalar();
                    if (value != null && value != DBNull.Value)
                        level = Convert.ToInt32(value);
                }
            }
            if (level < 0)
                return Error("角色不存在 " + characterId);

            var (_, cleared) = LoadQuestState(characterId);
            var clearedSet = new HashSet<int>(cleared.Where(p => p.Value != 0).Select(p => p.Key));

            return BuildQuestOverview(characterId, pvfIndex,
                m => IsAcceptableQuestLikeServer(m, level, clearedSet, cleared),
                mergeUnresolvedToOther: true);
        }

        private static bool IsAcceptableQuestLikeServer(
            PvfIndexService.QuestMeta m,
            int characterLevel,
            HashSet<int> clearedQuestIds,
            Dictionary<int, int> clearedFlags)
        {
            if (m.Id <= 0 || m.Id > 29999)
                return false;
            if (m.ExposedByNpc == 0)
                return false;
            if (m.IsEvent)
                return false;
            if (m.CreatureKind >= 0)
                return false;
            if (m.ExpertJobType >= 0 && m.ExpertJobLevel >= 0)
                return false;
            if (!IsSelectableQuestGrade(m.Grade))
                return false;

            var minLv = m.MinLevel > 0 ? m.MinLevel : 1;
            var maxLv = m.MaxLevel > 0 ? m.MaxLevel : 99;
            if (characterLevel < minLv || characterLevel > maxLv)
                return false;

            if (!IsRepeatableQuestGrade(m.Grade) && clearedQuestIds.Contains(m.Id))
                return false;

            if (!PreRequiredQuestGroupsSatisfied(m.PreGroups, clearedQuestIds))
                return false;

            var preReqAns = m.PreRequiredQuestAnswer ?? Array.Empty<int>();
            for (var i = 0; i + 1 < preReqAns.Length; i += 2)
            {
                if (!DoesClearedFlagMatchRequiredQuestAnswer(clearedFlags, preReqAns[i], preReqAns[i + 1]))
                    return false;
            }

            foreach (var collisionQuestId in m.CollisionQuest ?? Array.Empty<int>())
            {
                if (collisionQuestId > 0 && clearedQuestIds.Contains(collisionQuestId))
                    return false;
            }

            return true;
        }

        private static bool PreRequiredQuestGroupsSatisfied(int[][] groups, HashSet<int> clearedQuestIds)
        {
            if (groups == null || groups.Length == 0)
                return true;

            foreach (var group in groups)
            {
                var groupOk = true;
                foreach (var questId in group ?? Array.Empty<int>())
                {
                    if (questId > 0 && !clearedQuestIds.Contains(questId))
                    {
                        groupOk = false;
                        break;
                    }
                }
                if (groupOk)
                    return true;
            }
            return false;
        }

        private static bool DoesClearedFlagMatchRequiredQuestAnswer(
            Dictionary<int, int> clearedFlags,
            int requiredQuestId,
            int requiredAnswerIndex)
        {
            if (requiredQuestId <= 0)
                return true;

            var requiredFlag = requiredAnswerIndex >= 0 ? requiredAnswerIndex + 1 : 0;
            if (requiredFlag <= 0 || clearedFlags == null)
                return false;

            int actualFlag;
            return clearedFlags.TryGetValue(requiredQuestId, out actualFlag)
                && actualFlag == requiredFlag;
        }

        private static bool IsRepeatableQuestGrade(string grade)
        {
            return grade == "daily"
                || grade == "daily random"
                || grade == "normaly repeat"
                || grade == "special daily";
        }

        private static bool IsDailyQuestGrade(string grade)
        {
            return grade == "daily"
                || grade == "daily random"
                || grade == "special daily";
        }

        private static bool IsSelectableQuestGrade(string grade)
        {
            return grade == ""
                || grade == "normal"
                || grade == "side"
                || grade == "sub"
                || grade == "epic"
                || grade == "training"
                || grade == "achievement"
                || grade == "daily"
                || grade == "daily random"
                || grade == "normaly repeat"
                || grade == "special daily"
                || grade == "common unique"
                || grade == "system";
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
            var tokens = GetJobTags(job);
            foreach (var token in tokens)
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

        // jcq=1: 一转任务不查growType; jcq=2: 觉醒任务只比转职位; jcq=10/20: 跳过
        private static bool QuestMatchesCharacter(PvfIndexService.QuestMeta m, int job, int growType)
        {
            if (!string.IsNullOrEmpty(m.TargetCharacter) && !MatchesJobTag(m.TargetCharacter, job))
                return false;
            if (!string.IsNullOrEmpty(m.Job) && m.Job != "[all]" && !MatchesJobTag(m.Job, job))
                return false;

            var jcq = m.JobChangeQuestValue;
            if (jcq == 2 || jcq == 3)
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
            bool groupByTargetDungeon = false, bool groupByLinkedDungeon = false,
            int? maxEffectiveLevel = null,
            Func<PvfIndexService.QuestMeta, string> groupLabelSelector = null)
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
            filter = m => baseFilter(m)
                && QuestMatchesCharacter(m, charJob, charGrow)
                && (!maxEffectiveLevel.HasValue || EffectiveLevel(m) <= maxEffectiveLevel.Value);

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
                .GroupBy(m => new
                {
                    Group = groupLabelSelector != null ? groupLabelSelector(m) : null,
                    Region = GroupKey(m),
                })
                // 等级并列时按区域内最小任务ID排序(任务ID随内容加入时序递增,
                // 实证: 安徒恩 2489-2531 早于克洛诺斯岛 3000-3052); "其他"永远排最后
                .OrderBy(g => g.Key.Group == "外传" ? 1 : 0)
                .ThenBy(g => g.Key.Region == "__other__" ? 1 : 0)
                .ThenBy(g => g.Min(m => m.MinLevel))
                .ThenBy(g => g.Min(m => m.Id))
                .Select(g => (object)new
                {
                    region = string.IsNullOrEmpty(g.Key.Group) ? g.Key.Region : g.Key.Group + ":" + g.Key.Region,
                    regionLabel = g.Key.Region == "__other__" ? "其他" : pvfIndex.ResolveRegionName(g.Key.Region),
                    group = g.Key.Group,
                    minLevel = g.Min(m => m.MinLevel),
                    total = g.Count(),
                    completed = g.Count(m => QuestStatus(m.Id, active, cleared) == "已完成"),
                    quests = g.OrderBy(m => m.MinLevel).ThenBy(m => m.Id)
                        .Select(m => (object)new
                        {
                            questId = m.Id,
                            name = m.Name,
                            grade = m.Grade,
                            gradeLabel = GradeLabel(m.Grade),
                            region = m.Region,
                            regionLabel = pvfIndex.ResolveRegionName(m.Region),
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
                foreach (var boundId in ResolveTitleBoundQuestIds(id))
                {
                    if (!seen.Contains(boundId))
                        queue.Enqueue(boundId);
                }
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

            var boundQuestIds = ResolveTitleBoundQuestIds(questId);
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    foreach (var boundId in boundQuestIds)
                    {
                        if (boundId > 0 && boundId <= ushort.MaxValue)
                            QuestRepository.DeleteClearedFlag(conn, tx, characterId, (ushort)boundId);
                    }
                    tx.Commit();
                }
            }

            // 称号簿壳任务: 取消完成时把称号从簿槽撤下
            RemoveTitleIfBookShell(characterId, questId);

            return new { success = true, characterId, questId };
        }

        public object UnclearQuestBatch(int characterId, List<int> questIds)
        {
            if (questIds == null || questIds.Count == 0)
                return Error("questIds 为空");
            if (questIds.Count > 1000)
                return Error("一次最多 1000 个任务");

            var distinctIds = questIds.Distinct().ToArray();
            var boundQuestIds = new HashSet<int>();
            foreach (var qid in distinctIds)
            {
                foreach (var boundId in ResolveTitleBoundQuestIds(qid))
                    boundQuestIds.Add(boundId);
            }
            var cleared = 0;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    foreach (var qid in boundQuestIds)
                    {
                        if (qid <= 0 || qid > ushort.MaxValue)
                            continue;
                        QuestRepository.DeleteClearedFlag(conn, tx, characterId, (ushort)qid);
                        cleared++;
                    }
                    tx.Commit();
                }
            }

            foreach (var qid in distinctIds)
            {
                if (qid <= 0 || qid > ushort.MaxValue)
                    continue;
                RemoveTitleIfBookShell(characterId, qid);
            }

            return new { success = true, characterId, clearedCount = cleared };
        }

        // 任务库搜索: 按当前角色可拥有任务集合过滤, 已完成任务也保留显示。
        public object SearchQuests(int characterId, string query, string grade, string region, int limit, PvfIndexService pvfIndex)
        {
            var all = pvfIndex.AllQuestMeta;
            if (all == null)
                return Error("任务索引还在构建中，稍等几秒");

            if (limit <= 0 || limit > 2000)
                limit = 500;
            query = (query ?? string.Empty).Trim();
            grade = (grade ?? string.Empty).Trim();
            region = (region ?? string.Empty).Trim();

            int level = -1, job = -1, grow = -1;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT level, job, grow_type FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            level = reader.GetInt32(0);
                            job = reader.GetInt32(1);
                            grow = reader.GetInt32(2);
                        }
                    }
                }
            }
            if (level < 0)
                return Error("角色不存在: " + characterId);

            var (active, cleared) = LoadQuestState(characterId);
            var clearedSet = new HashSet<int>(cleared.Where(p => p.Value != 0).Select(p => p.Key));
            var candidates = all.Values
                .Where(m => IsQuestLibraryCandidate(m, level, job, grow, clearedSet, cleared))
                .ToList();

            var grades = candidates
                .GroupBy(m => m.Grade ?? string.Empty)
                .OrderBy(g => GradeSortKey(g.Key))
                .ThenBy(g => GradeLabel(g.Key))
                .Select(g => new { value = g.Key, label = GradeLabel(g.Key), count = g.Count() })
                .ToArray();
            var regions = candidates
                .GroupBy(m => m.Region ?? string.Empty)
                .OrderBy(g => g.Key == "__other__" ? 1 : 0)
                .ThenBy(g => g.Min(m => EffectiveLevel(m)))
                .ThenBy(g => pvfIndex.ResolveRegionName(g.Key))
                .Select(g => new { value = g.Key, label = pvfIndex.ResolveRegionName(g.Key), count = g.Count() })
                .ToArray();

            var filtered = candidates.Where(m =>
                (string.IsNullOrEmpty(grade) || string.Equals(m.Grade ?? string.Empty, grade, StringComparison.Ordinal))
                && (string.IsNullOrEmpty(region) || string.Equals(m.Region ?? string.Empty, region, StringComparison.Ordinal))
                && QuestSearchMatches(m, query));

            var results = filtered
                .OrderBy(m => EffectiveLevel(m))
                .ThenBy(m => m.Id)
                .Take(limit)
                .Select(m => DescribeQuest(m, pvfIndex, QuestStatus(m.Id, active, cleared)))
                .ToArray();

            return new
            {
                characterId,
                query,
                grade,
                region,
                count = results.Length,
                totalCandidates = candidates.Count,
                limit,
                filters = new { grades, regions },
                results,
            };
        }

        private static int GradeSortKey(string grade)
        {
            switch (grade)
            {
                case "epic": return 10;
                case "side": return 20;
                case "normal": return 30;
                case "sub": return 35;
                case "training": return 40;
                case "achievement": return 50;
                case "daily": return 60;
                case "daily random": return 61;
                case "special daily": return 62;
                case "normaly repeat": return 70;
                case "common unique": return 80;
                case "system": return 90;
                default: return 999;
            }
        }

        private static bool QuestSearchMatches(PvfIndexService.QuestMeta m, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            if (int.TryParse(query, out var questId))
                return m.Id == questId;
            return (m.Name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsQuestLibraryCandidate(
            PvfIndexService.QuestMeta m,
            int level,
            int job,
            int grow,
            HashSet<int> clearedSet,
            Dictionary<int, int> cleared)
        {
            if (m == null || !QuestMatchesCharacter(m, job, grow))
                return false;
            if (m.Id <= 0 || m.Id > 29999)
                return false;
            if (m.ExposedByNpc == 0)
                return false;
            if (m.IsEvent)
                return false;
            if (m.CreatureKind >= 0)
                return false;
            if (m.ExpertJobType >= 0 && m.ExpertJobLevel >= 0)
                return false;
            if (!IsSelectableQuestGrade(m.Grade))
                return false;

            var minLv = m.MinLevel > 0 ? m.MinLevel : 1;
            var maxLv = m.MaxLevel > 0 ? m.MaxLevel : 99;
            if (level < minLv || level > maxLv)
                return false;

            if (clearedSet.Contains(m.Id))
                return true;

            return IsAcceptableQuestLikeServer(m, level, clearedSet, cleared);
        }

        // 强制完成: 从进行中移除并用服务端的位图逻辑写入已完成标记(不发奖励)
        public object ForceCompleteQuest(int characterId, int questId)
        {
            if (questId <= 0 || questId > ushort.MaxValue)
                return Error("questId 无效");

            var boundQuestIds = ResolveTitleBoundQuestIds(questId);
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    foreach (var boundId in boundQuestIds)
                    {
                        if (boundId <= 0 || boundId > ushort.MaxValue)
                            continue;
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = @"
DELETE FROM character_active_quests
WHERE character_id = @cid AND quest_id = @qid;";
                            cmd.Parameters.AddWithValue("@cid", characterId);
                            cmd.Parameters.AddWithValue("@qid", boundId);
                            cmd.ExecuteNonQuery();
                        }

                        QuestRepository.MarkQuestCleared(conn, tx, characterId, (ushort)boundId, 1);
                    }
                    tx.Commit();
                }
            }

            // 称号簿壳任务: 走服务端成就链把称号送进簿槽(独立连接, 不能并入上面的事务)
            var titles = new List<int>();
            DeliverTitleIfBookShell(characterId, questId, titles);

            // 转职/觉醒任务(jcq=1/2): 同步授予转职并重算战斗属性
            var growChanged = false;
            foreach (var boundId in boundQuestIds)
                growChanged |= ApplyGrowTypeFromQuest(characterId, _pvfIndex.GetQuestMeta(boundId));

            return new { success = true, characterId, questId, synchronizedQuestIds = boundQuestIds.ToArray(), titleDelivered = titles.Count > 0, growChanged };
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

        public object CompleteVisibleQuestBatch(int characterId, List<int> questIds, PvfIndexService pvfIndex)
        {
            if (questIds == null || questIds.Count == 0)
                return Error("questIds empty");
            if (questIds.Count > 2000)
                return Error("too many quests");

            var all = pvfIndex.AllQuestMeta;
            if (all == null)
                return Error("quest index is still loading");

            var completed = new List<int>();
            var skippedDaily = new List<int>();
            foreach (var qid in questIds.Distinct())
            {
                if (qid <= 0 || qid > ushort.MaxValue)
                    continue;

                PvfIndexService.QuestMeta meta;
                if (all.TryGetValue(qid, out meta) && IsDailyQuestGrade(meta.Grade))
                {
                    skippedDaily.Add(qid);
                    continue;
                }

                ForceCompleteQuest(characterId, qid);
                completed.Add(qid);
            }

            return new
            {
                success = true,
                characterId,
                completedCount = completed.Count,
                skippedDailyCount = skippedDaily.Count,
                skippedDaily = skippedDaily.ToArray(),
            };
        }

        public object MarkVisibleDailyQuestReady(int characterId, int questId, PvfIndexService pvfIndex)
        {
            if (questId <= 0 || questId > ushort.MaxValue)
                return Error("questId invalid");

            var all = pvfIndex.AllQuestMeta;
            if (all == null)
                return Error("quest index is still loading");

            PvfIndexService.QuestMeta meta;
            if (!all.TryGetValue(questId, out meta))
                return Error("quest not found " + questId);
            if (!IsDailyQuestGrade(meta.Grade))
                return Error("not a daily quest " + questId);

            int level = -1, job = -1, grow = -1;
            var activationId = string.Empty;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "SELECT level, job, grow_type FROM characters WHERE character_id = @cid;";
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                level = reader.GetInt32(0);
                                job = reader.GetInt32(1);
                                grow = reader.GetInt32(2);
                            }
                        }
                    }
                    if (level < 0)
                        return Error("character not found " + characterId);

                    var cleared = QuestRepository.LoadClearedFlags(conn, tx, characterId);
                    var clearedSet = new HashSet<int>(cleared.Where(p => p.Value != 0).Select(p => p.Key));
                    if (!QuestMatchesCharacter(meta, job, grow)
                        || !IsAcceptableQuestLikeServer(meta, level, clearedSet, cleared))
                    {
                        return Error("daily quest is not visible for this character " + questId);
                    }

                    var active = QuestRepository.LoadActiveQuests(conn, tx, characterId);
                    var existing = active.FirstOrDefault(q => q.QuestId == (ushort)questId);
                    if (existing != null)
                    {
                        if (!QuestRepository.TryUpdateTriggerValueCas(
                            conn,
                            tx,
                            characterId,
                            existing.QuestId,
                            existing.ActivationId,
                            existing.Version,
                            existing.TriggerValue,
                            0))
                        {
                            return Error("daily quest state changed; refresh and retry");
                        }
                        activationId = existing.ActivationId.ToString();
                    }
                    else
                    {
                        var freeSlot = QuestActiveListRules.FindFreeSlot(active);
                        if (freeSlot < 0)
                            return Error("active quest slots are full");
                        activationId = QuestRepository.InsertActiveQuest(
                            conn,
                            tx,
                            characterId,
                            freeSlot,
                            (ushort)questId,
                            0).ToString();
                    }

                    QuestRepository.DeleteClearedFlag(conn, tx, characterId, (ushort)questId);
                    tx.Commit();
                }
            }

            return new { success = true, characterId, questId, activationId };
        }

        public object ResetVisibleDailyQuests(int characterId, PvfIndexService pvfIndex)
        {
            var all = pvfIndex.AllQuestMeta;
            if (all == null)
                return Error("quest index is still loading");

            int level = -1, job = -1, grow = -1;
            var resetIds = new List<int>();
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "SELECT level, job, grow_type FROM characters WHERE character_id = @cid;";
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                level = reader.GetInt32(0);
                                job = reader.GetInt32(1);
                                grow = reader.GetInt32(2);
                            }
                        }
                    }
                    if (level < 0)
                        return Error("character not found " + characterId);

                    var cleared = QuestRepository.LoadClearedFlags(conn, tx, characterId);
                    var clearedSet = new HashSet<int>(cleared.Where(p => p.Value != 0).Select(p => p.Key));
                    resetIds = all.Values
                        .Where(m => IsDailyQuestGrade(m.Grade)
                            && QuestMatchesCharacter(m, job, grow)
                            && IsAcceptableQuestLikeServer(m, level, clearedSet, cleared))
                        .OrderBy(m => m.Id)
                        .Select(m => m.Id)
                        .ToList();

                    foreach (var qid in resetIds)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "DELETE FROM character_active_quests WHERE character_id = @cid AND quest_id = @qid;";
                            cmd.Parameters.AddWithValue("@cid", characterId);
                            cmd.Parameters.AddWithValue("@qid", qid);
                            cmd.ExecuteNonQuery();
                        }
                        if (qid > 0 && qid <= ushort.MaxValue)
                            QuestRepository.DeleteClearedFlag(conn, tx, characterId, (ushort)qid);
                    }
                    tx.Commit();
                }
            }

            return new { success = true, characterId, resetCount = resetIds.Count, questIds = resetIds.ToArray() };
        }

        public object CompleteCurrentLevelMainQuests(int characterId, PvfIndexService pvfIndex)
        {
            return CompleteCurrentLevelQuestGroup(characterId, pvfIndex, IsMainStoryEpic);
        }

        public object CompleteCurrentLevelSideQuests(int characterId, PvfIndexService pvfIndex)
        {
            return CompleteCurrentLevelQuestGroup(characterId, pvfIndex, IsSideQuest);
        }

        public object CompleteCurrentLevelSystemQuests(int characterId, PvfIndexService pvfIndex)
        {
            return CompleteCurrentLevelQuestGroup(characterId, pvfIndex, IsSystemQuest);
        }

        public object CompleteCurrentLevelNoItemAchievementQuests(int characterId, PvfIndexService pvfIndex)
        {
            return CompleteCurrentLevelQuestGroup(characterId, pvfIndex,
                m => IsNoItemAchievementQuest(m) || IsHellPartyAchievementQuest(m));
        }

        private bool IsNoItemAchievementQuest(PvfIndexService.QuestMeta m)
        {
            if (m == null)
                return false;
            if (m.Grade != "achievement" && !IsNoItemAccessUnlockQuest(m))
                return false;
            if (m.JobChangeQuestValue > 0 || m.RewardChainType == 1 || m.RewardChainType == 2)
                return false;
            if (m.RewardTitleItemId > 0 || FindTitleBookSlotsForQuest(m.Id).Count > 0)
                return false;
            if (!HasNoConcreteQuestReward(m))
                return false;
            return true;
        }

        private static bool IsHellPartyAchievementQuest(PvfIndexService.QuestMeta m)
        {
            return m != null
                && m.Grade == "achievement"
                && string.Equals(m.Region, "hell", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNoItemAccessUnlockQuest(PvfIndexService.QuestMeta m)
        {
            if (m == null || IsRepeatableQuestGrade(m.Grade))
                return false;
            var exceptionQuest = m.ExceptionQuest ?? string.Empty;
            return exceptionQuest.Contains("[urgent]") || exceptionQuest.Contains("urgent");
        }

        private static bool HasNoConcreteQuestReward(PvfIndexService.QuestMeta m)
        {
            return (m.RewardItemIds == null || !m.RewardItemIds.Any(id => id > 0))
                && (m.RewardSelectionItemIds == null || !m.RewardSelectionItemIds.Any(id => id > 0));
        }

        public object GetExtraEquipmentSlotQuestStatus(int characterId)
        {
            if (!TryLoadExtraEquipmentSlotQuestState(characterId, out var unlocked, out var residual, out var error))
                return Error(error);
            return new
            {
                success = true,
                characterId,
                unlocked,
                residualCount = residual.Count,
                residualQuestIds = residual.ToArray(),
                canComplete = unlocked && residual.Count > 0,
            };
        }

        public object CompleteExtraEquipmentSlotQuests(int characterId)
        {
            if (!TryLoadExtraEquipmentSlotQuestState(characterId, out var unlocked, out var residual, out var error))
                return Error(error);
            if (!unlocked)
                return Error("当前角色尚未开启左右槽");
            if (residual.Count == 0)
                return Error("左右槽相关任务没有残留");

            var completed = MarkQuestIdsCleared(characterId, residual);
            return new
            {
                success = true,
                characterId,
                completedCount = completed.Count,
                questIds = completed.ToArray(),
            };
        }

        private bool TryLoadExtraEquipmentSlotQuestState(
            int characterId,
            out bool unlocked,
            out List<int> residualQuestIds,
            out string error)
        {
            unlocked = false;
            residualQuestIds = new List<int>();
            error = null;

            int job = -1, grow = -1, exEquipSlotStat = 0;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT job, grow_type, ex_equip_slot_stat FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            job = reader.GetInt32(0);
                            grow = reader.GetInt32(1);
                            exEquipSlotStat = reader.GetInt32(2);
                        }
                    }
                }
                if (job < 0)
                {
                    error = "角色不存在: " + characterId;
                    return false;
                }

                unlocked = (exEquipSlotStat & 3) == 3;
                var cleared = QuestRepository.LoadClearedFlags(conn, null, characterId);
                foreach (var questId in ExtraEquipmentSlotQuestIds)
                {
                    var meta = _pvfIndex.GetQuestMeta(questId);
                    if (meta == null || !QuestMatchesCharacter(meta, job, grow))
                        continue;
                    if (!cleared.TryGetValue(questId, out var flag) || flag == 0)
                        residualQuestIds.Add(questId);
                }
            }
            return true;
        }

        public object CompleteProfessionQuests(int characterId, PvfIndexService pvfIndex, int? firstChoice)
        {
            var all = pvfIndex.AllQuestMeta;
            if (all == null)
                return Error("任务索引还在构建中, 稍等几秒");

            int level = -1, job = -1, grow = -1;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT level, job, grow_type FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            level = reader.GetInt32(0);
                            job = reader.GetInt32(1);
                            grow = reader.GetInt32(2);
                        }
                    }
                }
            }
            if (level < 0)
                return Error("角色不存在: " + characterId);
            if (level < 15)
                return Error("角色达到 15 级后才能完成转职任务");
            if (job == 9 || job == 10)
                return Error(PvfIndexService.GetFrontJobLabel(job) + "没有转职/觉醒分支");

            var currentFirst = grow & 0xF;
            var currentSecond = (grow >> 4) & 0xF;
            var targetFirst = currentFirst;
            var selectedByUser = false;
            if (targetFirst <= 0)
            {
                if (!firstChoice.HasValue || firstChoice.Value <= 0)
                    return Error("未转职角色需要先选择目标转职");
                targetFirst = firstChoice.Value;
                selectedByUser = true;
            }

            string error;
            if (!pvfIndex.TryValidateJobGrowOption(job, targetFirst, 0, out error))
                return Error(error ?? "无效转职");

            var targetSecond = currentSecond;
            if (level >= 75
                && pvfIndex.TryValidateJobGrowOption(job, targetFirst, 2, out error)
                && HasProfessionQuestStage(job, targetFirst, stage: 3))
            {
                targetSecond = Math.Max(targetSecond, 2);
            }
            else if (level >= 50
                && pvfIndex.TryValidateJobGrowOption(job, targetFirst, 1, out error)
                && HasProfessionQuestStage(job, targetFirst, stage: 2))
            {
                targetSecond = Math.Max(targetSecond, 1);
            }

            var closure = new HashSet<int>();
            AddProfessionQuestClosure(closure, pvfIndex, job, targetFirst, stage: 1);
            if (targetSecond >= 1)
                AddProfessionQuestClosure(closure, pvfIndex, job, targetFirst, stage: 2);
            if (targetSecond >= 2)
                AddProfessionQuestClosure(closure, pvfIndex, job, targetFirst, stage: 3);

            var completed = MarkQuestIdsCleared(characterId, closure);

            var changed = false;
            if (targetFirst != currentFirst || targetSecond != currentSecond)
            {
                changed = ApplyJobAndGrowType(
                    characterId,
                    null,
                    targetFirst,
                    targetSecond,
                    selectedByUser ? GrowSkillSyncMode.Rebuild : GrowSkillSyncMode.MergeAwakening,
                    out error);
                if (!changed)
                    return Error(error ?? "转职/觉醒状态写入失败");
            }

            return new
            {
                success = true,
                characterId,
                completedCount = completed.Count,
                questIds = completed.ToArray(),
                job,
                first = targetFirst,
                second = targetSecond,
                growChanged = changed,
                skillsInitialized = selectedByUser,
            };
        }

        private void AddProfessionQuestClosure(
            HashSet<int> closure,
            PvfIndexService pvfIndex,
            int job,
            int first,
            int stage)
        {
            var grow = (stage <= 1 ? 0 : first) | ((stage > 2 ? 2 : stage > 1 ? 1 : 0) << 4);
            foreach (var questId in ResolveProfessionQuestIds(job, first, stage))
            {
                foreach (var id in BuildQuestClosure(questId, job, grow, pvfIndex))
                    closure.Add(id);
            }
        }

        private bool HasProfessionQuestStage(int job, int first, int stage)
        {
            return ResolveProfessionQuestIds(job, first, stage).Length > 0;
        }

        private List<int> BuildQuestClosure(int questId, int chainJob, int chainGrow, PvfIndexService pvfIndex)
        {
            var all = pvfIndex.AllQuestMeta;
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
                foreach (var boundId in ResolveTitleBoundQuestIds(id))
                {
                    if (!seen.Contains(boundId))
                        queue.Enqueue(boundId);
                }
                PvfIndexService.QuestMeta meta;
                if (all != null && all.TryGetValue(id, out meta))
                {
                    foreach (var pid in SelectPreGroup(meta, chainJob, chainGrow, pvfIndex))
                        queue.Enqueue(pid);
                    if (meta.TargetQuestId > 0)
                        queue.Enqueue(meta.TargetQuestId);
                }
            }
            return closure;
        }

        private List<int> MarkQuestIdsCleared(int characterId, IEnumerable<int> questIds)
        {
            var completed = new List<int>();
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    var cleared = QuestRepository.LoadClearedFlags(conn, tx, characterId);
                    foreach (var id in questIds.Distinct().OrderBy(id => id))
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
            return completed;
        }

        private object CompleteCurrentLevelQuestGroup(
            int characterId,
            PvfIndexService pvfIndex,
            Func<PvfIndexService.QuestMeta, bool> filter)
        {
            var all = pvfIndex.AllQuestMeta;
            if (all == null)
                return Error("任务索引还在构建中，稍等几秒");

            int level = -1, job = -1, grow = -1;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT level, job, grow_type FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            level = reader.GetInt32(0);
                            job = reader.GetInt32(1);
                            grow = reader.GetInt32(2);
                        }
                    }
                }
            }
            if (level < 0)
                return Error("角色不存在 " + characterId);

            var (_, cleared) = LoadQuestState(characterId);
            var targets = all.Values
                .Where(m => filter(m)
                    && QuestMatchesCharacter(m, job, grow)
                    && EffectiveLevel(m) <= level
                    && (!cleared.TryGetValue(m.Id, out var flag) || flag == 0))
                .OrderBy(m => EffectiveLevel(m))
                .ThenBy(m => m.Id)
                .Select(m => m.Id)
                .ToList();

            if (targets.Count == 0)
                return new { success = true, characterId, completedCount = 0, questIds = Array.Empty<int>() };

            var completed = new List<int>();
            foreach (var qid in targets)
            {
                ForceCompleteQuest(characterId, qid);
                completed.Add(qid);
            }

            return new { success = true, characterId, completedCount = completed.Count, questIds = completed.ToArray() };
        }
    }
}
