namespace GmPvfLib
{
    
    
    
    public class PvfFileData
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public PvfFileItem Entry { get; internal set; }
        public int Index { get; internal set; } = -1;
    }
}
