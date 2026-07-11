using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    public class PvfTreeNode
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public PvfFileData FileInfo { get; set; }
        public bool IsDirectory { get; set; }
        public List<PvfTreeNode> Children { get; set; } = new List<PvfTreeNode>();
    }
}
