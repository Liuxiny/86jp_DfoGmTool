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
        // 称号簿壳任务的完成不能只写位图: 服务端正规链(TriggerAchievement)在
        // 计数归零时会把称号写进 character_titlebook 簿槽。GM 强制完成对壳任务
        // 复用该链(计数一次打满→自动入簿), 取消完成反向清簿槽。
        private void DeliverTitleIfBookShell(int characterId, int questId, List<int> delivered)
        {
            foreach (var slot in FindTitleBookSlotsForQuest(questId))
            {
                try
                {
                    var result = _titleBookMutation.Value.TriggerAchievement(
                        characterId, slot.ShellQuestId, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
                    if (result.Completed && delivered != null && !delivered.Contains(slot.ShellQuestId))
                        delivered.Add(slot.ShellQuestId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GmService] 称号入簿失败 quest={questId} shell={slot.ShellQuestId}: {ex.Message}");
                }
            }
        }

        private void RemoveTitleIfBookShell(int characterId, int questId)
        {
            foreach (var slot in FindTitleBookSlotsForQuest(questId))
            {
                try
                {
                    using (var conn = new SqliteConnection(_config.ConnectionString))
                    {
                        conn.Open();
                        using (var tx = conn.BeginTransaction())
                        {
                            new CharacterTitleBookRepository(_config.ConnectionString)
                                .ClearItem(conn, tx, characterId, slot.Category, slot.Index);
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                cmd.CommandText = @"
DELETE FROM character_achievement_complete
WHERE character_id = @cid AND achievement_id = @qid;";
                                cmd.Parameters.AddWithValue("@cid", characterId);
                                cmd.Parameters.AddWithValue("@qid", slot.ShellQuestId);
                                cmd.ExecuteNonQuery();
                            }
                            tx.Commit();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GmService] 清簿槽失败 quest={questId} shell={slot.ShellQuestId}: {ex.Message}");
                }
            }
        }

        private List<TitleBookSlot> FindTitleBookSlotsForQuest(int questId)
        {
            var result = new List<TitleBookSlot>();
            var quest = _pvfIndex.GetQuestMeta(questId);
            foreach (var slot in EnsureTitleBookSlots())
            {
                var shell = _pvfIndex.GetQuestMeta(slot.ShellQuestId);
                if (slot.ShellQuestId == questId
                    || (shell != null && shell.TargetQuestId == questId)
                    || (quest != null && quest.RewardTitleItemId > 0
                        && quest.RewardTitleItemId == slot.RewardItemId))
                {
                    result.Add(slot);
                }
            }
            return result;
        }

        private HashSet<int> ResolveTitleBoundQuestIds(int questId)
        {
            var result = new HashSet<int> { questId };
            var all = _pvfIndex.AllQuestMeta;
            foreach (var slot in FindTitleBookSlotsForQuest(questId))
            {
                result.Add(slot.ShellQuestId);
                var shell = _pvfIndex.GetQuestMeta(slot.ShellQuestId);
                if (shell != null && shell.TargetQuestId > 0)
                    result.Add(shell.TargetQuestId);

                if (slot.RewardItemId > 0 && all != null)
                {
                    foreach (var meta in all.Values)
                    {
                        if (meta.RewardTitleItemId == slot.RewardItemId)
                            result.Add(meta.Id);
                    }
                }
            }
            return result;
        }

        internal int[] GetTitleBoundQuestIdsForTest(int questId)
        {
            return ResolveTitleBoundQuestIds(questId).OrderBy(id => id).ToArray();
        }

        // 称号簿五页, 顺序与服务端 CategoryNames { general, specific, pvp, despair, event } 一致
        private static readonly string[] TitleCategoryLabels =
            { "普通成就", "特殊成就", "决斗场", "绝望之塔", "活动" };

        public object CompleteAllTitleBook(int characterId)
        {
            int job = -1, grow = -1;
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
                            job = reader.GetInt32(0);
                            grow = reader.GetInt32(1);
                        }
                    }
                }
            }
            if (job < 0)
                return Error("角色不存在 " + characterId);

            var repository = new CharacterTitleBookRepository(_config.ConnectionString);
            var slots = EnsureTitleBookSlots()
                .Where(slot => TitleBookSlotMatchesCharacter(slot, job, grow))
                .ToArray();
            var (_, cleared) = LoadQuestState(characterId);
            var missingTitleSlots = 0;
            var pendingQuestSlots = 0;
            var targets = new List<int>();

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                foreach (var slot in slots)
                {
                    var item = repository.LoadItem(conn, null, characterId, slot.Category, slot.Index);
                    var titleMissing = item == null || item.IsEmpty;
                    var questPending = ResolveTitleBoundQuestIds(slot.ShellQuestId)
                        .Any(id => id > 0 && (!cleared.TryGetValue(id, out var flag) || flag == 0));

                    if (titleMissing)
                        missingTitleSlots++;
                    if (questPending)
                        pendingQuestSlots++;
                    if (titleMissing || questPending)
                        targets.Add(slot.ShellQuestId);
                }
            }

            if (targets.Count == 0)
            {
                return new
                {
                    success = true,
                    characterId,
                    completedCount = 0,
                    titleDelivered = 0,
                    skipped = true,
                    message = "称号簿已满，且对应成就任务已完成",
                };
            }

            var completed = 0;
            var titleDelivered = 0;
            foreach (var questId in targets.Distinct())
            {
                var result = ForceCompleteQuest(characterId, questId);
                completed++;
                if (ResultInt(result, "titleDelivered") > 0)
                    titleDelivered++;
            }

            return new
            {
                success = true,
                characterId,
                completedCount = completed,
                titleDelivered,
                missingTitleSlots,
                pendingQuestSlots,
                skipped = false,
            };
        }

        private bool TitleBookSlotMatchesCharacter(TitleBookSlot slot, int job, int grow)
        {
            var shell = _pvfIndex.GetQuestMeta(slot.ShellQuestId);
            var target = shell != null && shell.TargetQuestId > 0
                ? _pvfIndex.GetQuestMeta(shell.TargetQuestId)
                : null;
            var gate = target ?? shell;
            return gate == null || QuestMatchesCharacter(gate, job, grow);
        }

        private static int ResultInt(object result, string propertyName)
        {
            if (result == null)
                return 0;
            var property = result.GetType().GetProperty(propertyName);
            if (property == null)
                return 0;
            var value = property.GetValue(result, null);
            if (value is bool flag)
                return flag ? 1 : 0;
            try { return Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static readonly object _titleBookLock = new object();

        private sealed class TitleBookSlot
        {
            public int Category;
            public int Index;
            public int ShellQuestId;
            public int RewardItemId;
        }

        private static List<TitleBookSlot> _titleBookSlots;

        private static List<TitleBookSlot> EnsureTitleBookSlots()
        {
            if (_titleBookSlots != null)
                return _titleBookSlots;
            lock (_titleBookLock)
            {
                if (_titleBookSlots != null)
                    return _titleBookSlots;

                var list = new List<TitleBookSlot>();
                try
                {
                    var provider = TitleBookStaticDataProvider.LoadDefault();
                    var capacities = TitleBookStaticDataProvider.CategoryCapacities;
                    for (var category = 0; category < capacities.Count; category++)
                    {
                        for (var index = 0; index < capacities[category]; index++)
                        {
                            var slot = provider.GetSlot(category, index);
                            if (!slot.IsOpen || slot.QuestId <= 0)
                                continue;
                            list.Add(new TitleBookSlot
                            {
                                Category = category,
                                Index = index,
                                ShellQuestId = slot.QuestId,
                                RewardItemId = slot.AllowedTitleItemIds.Count > 0 ? slot.AllowedTitleItemIds[0] : -1,
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[GmService] 称号簿加载失败: " + ex.Message);
                }

                _titleBookSlots = list;
                return list;
            }
        }
    }
}
