using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.Skills
{
    
    
    
    
    
    
    
    
    
    
    public static class SkillSlotAllocator
    {
        
        private static readonly int[][] GroupCluster =
        {
            new[] { 6, 54 },     
            new[] { 54, 102 },   
            new[] { 102, 150 },  
            new[] { 150, 198 },  
        };

        
        
        
        
        public static int ReformGroup(int rawGroup, bool isActive, int numGrowtypes)
        {
            if (isActive) return 3;
            if (rawGroup >= 0 && rawGroup <= 3) return numGrowtypes <= 2 ? 1 : 0;
            if (rawGroup == 4) return 2;
            return rawGroup; 
        }

        
        
        
        
        
        public static int AllocateNewSlot(bool isActive, int finalGroup, int job, HashSet<int> occupied)
        {
            
            if (isActive && job != 9)
            {
                int s = FirstFree(occupied, 0, 6);
                if (s >= 0) return s;
                s = FirstFree(occupied, 198, 204);
                if (s >= 0) return s;
            }

            if (finalGroup < 0 || finalGroup >= GroupCluster.Length) finalGroup = 0;
            var cluster = GroupCluster[finalGroup];
            return FirstFree(occupied, cluster[0], cluster[1]);
        }

        private static int FirstFree(HashSet<int> occupied, int start, int end)
        {
            for (int i = start; i < end; i++)
                if (!occupied.Contains(i)) return i;
            return -1;
        }
    }
}
