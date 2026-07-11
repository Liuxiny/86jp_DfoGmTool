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
            var slot = EnsureTitleBookSlots().FirstOrDefault(s => s.ShellQuestId == questId);
            if (slot == null)
                return;
            try
            {
                var result = _titleBookMutation.Value.TriggerAchievement(
                    characterId, questId, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
                if (result.Completed && delivered != null)
                    delivered.Add(questId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GmService] 称号入簿失败 quest={questId}: {ex.Message}");
            }
        }

        private void RemoveTitleIfBookShell(int characterId, int questId)
        {
            var slot = EnsureTitleBookSlots().FirstOrDefault(s => s.ShellQuestId == questId);
            if (slot == null)
                return;
            try
            {
                using (var conn = new SqliteConnection(_config.ConnectionString))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        new CharacterTitleBookRepository(_config.ConnectionString)
                            .ClearItem(conn, tx, characterId, slot.Category, slot.Index);
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GmService] 清簿槽失败 quest={questId}: {ex.Message}");
            }
        }

        // 称号簿五页, 顺序与服务端 CategoryNames { general, specific, pvp, despair, event } 一致
        private static readonly string[] TitleCategoryLabels =
            { "普通成就", "特殊成就", "决斗场", "绝望之塔", "活动" };

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
