namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal sealed class RandomOption
    {
        public byte Type { get; set; }

        public byte Value1 { get; set; }

        public byte Value2 { get; set; }

        public bool IsEmpty => Type == 0;

        public void Clear()
        {
            Type = 0;
            Value1 = 0;
            Value2 = 0;
        }

        public RandomOption Copy()
        {
            var copy = new RandomOption();
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(RandomOption source)
        {
            if (source == null)
            {
                Clear();
                return;
            }

            Type = source.Type;
            Value1 = source.Value1;
            Value2 = source.Value2;
        }
    }
}
