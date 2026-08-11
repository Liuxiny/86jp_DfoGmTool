using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.Quests
{
    internal static class QuestSlotLayout
    {
        // The A14 character-select payload owns thirty fixed active-quest slots.
        internal const int ActiveSlotCount = 30;

        // A14 no longer handles the old server's error 0x04. This generic
        // rejection remains a fallback for a full-list race or forged request;
        // the normal client path detects all thirty occupied slots locally.
        internal const byte ActiveListFullFallbackError = 0x17;

        internal static ActiveQuest[] ProjectFixedSlots(
            IReadOnlyCollection<ActiveQuest> activeQuests)
        {
            var slots = new ActiveQuest[ActiveSlotCount];
            if (activeQuests == null)
                return slots;

            foreach (var quest in activeQuests)
            {
                if (quest == null
                    || quest.Slot < 0
                    || quest.Slot >= ActiveSlotCount
                    || slots[quest.Slot] != null)
                {
                    continue;
                }

                slots[quest.Slot] = quest;
            }

            return slots;
        }
    }
}
