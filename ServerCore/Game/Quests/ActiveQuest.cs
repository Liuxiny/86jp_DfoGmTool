using System;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.Quests
{
    public readonly struct QuestActivationId : IEquatable<QuestActivationId>
    {
        private readonly Guid _value;

        private QuestActivationId(Guid value) => _value = value;

        public bool IsValid => _value != Guid.Empty;

        internal static QuestActivationId New() =>
            new QuestActivationId(Guid.NewGuid());

        internal static bool TryParse(
            string value,
            out QuestActivationId activationId)
        {
            if (Guid.TryParseExact(value, "N", out var parsed)
                && parsed != Guid.Empty)
            {
                activationId = new QuestActivationId(parsed);
                return true;
            }

            activationId = default;
            return false;
        }

        internal string ToStorageString() =>
            IsValid ? _value.ToString("N") : string.Empty;

        public bool Equals(QuestActivationId other) =>
            _value.Equals(other._value);

        public override bool Equals(object obj) =>
            obj is QuestActivationId other && Equals(other);

        public override int GetHashCode() => _value.GetHashCode();

        public override string ToString() => ToStorageString();
    }

    public sealed class ActiveQuest
    {
        public int Slot;
        public ushort QuestId;
        public uint TriggerValue;
        public long Version;
        public QuestActivationId ActivationId;
    }

    internal static class QuestActiveListRules
    {
        internal static ActiveQuest FindByQuestId(
            IReadOnlyCollection<ActiveQuest> active,
            ushort questId)
        {
            if (active == null)
                return null;

            foreach (var quest in active)
            {
                if (quest != null && quest.QuestId == questId)
                    return quest;
            }
            return null;
        }

        internal static int FindFreeSlot(IReadOnlyCollection<ActiveQuest> active)
        {
            var used = new HashSet<int>();
            if (active != null)
            {
                foreach (var quest in active)
                {
                    if (quest != null)
                        used.Add(quest.Slot);
                }
            }

            for (var slot = 0; slot < QuestSlotLayout.ActiveSlotCount; slot++)
            {
                if (!used.Contains(slot))
                    return slot;
            }
            return -1;
        }
    }
}
