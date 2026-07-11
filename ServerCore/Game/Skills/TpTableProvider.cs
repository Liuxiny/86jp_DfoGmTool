namespace DfoGmTool.ServerCore.Game.Skills
{
    public static class TpTableProvider
    {
        private const int TpStartLevel = 50;

        public static int GetTotalTp(int level)
        {
            if (level < TpStartLevel) return 0;
            return level - TpStartLevel + 1;
        }
    }
}
