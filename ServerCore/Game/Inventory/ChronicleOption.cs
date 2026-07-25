namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal sealed class ChronicleOption
    {
        public int OptionId { get; set; }

        public byte CharacJob { get; set; }

        public byte FirstGrowType { get; set; }

        public byte EquipmentType { get; set; }

        public byte OptionNo { get; set; }

        public bool IsEmpty => OptionId == 0;

        public void Clear()
        {
            OptionId = 0;
            CharacJob = 0;
            FirstGrowType = 0;
            EquipmentType = 0;
            OptionNo = 0;
        }

        public ChronicleOption Copy()
        {
            var copy = new ChronicleOption();
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(ChronicleOption source)
        {
            if (source == null)
            {
                Clear();
                return;
            }

            OptionId = source.OptionId;
            CharacJob = source.CharacJob;
            FirstGrowType = source.FirstGrowType;
            EquipmentType = source.EquipmentType;
            OptionNo = source.OptionNo;
        }
    }
}
