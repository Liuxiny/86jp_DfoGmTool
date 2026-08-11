using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GmPvfLib
{
    
    
    
    public class LstEntry
    {
        public int Id { get; set; }
        public string FilePath { get; set; }

        public override string ToString() => $"{Id} `{FilePath}`";
    }

    
    
    
    public class LstFile
    {
        private static readonly Regex Pattern = new Regex(@"(\d+)\s+`([^`]+)`", RegexOptions.Compiled);

        public List<LstEntry> Entries { get; } = new List<LstEntry>();
        private Dictionary<int, LstEntry> _idIndex;

        
        public static LstFile Parse(string content)
        {
            var lst = new LstFile();
            if (string.IsNullOrEmpty(content)) return lst;

            var matches = Pattern.Matches(content);
            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                lst.Entries.Add(new LstEntry
                {
                    Id = int.Parse(m.Groups[1].Value),
                    FilePath = NormalizeFilePath(m.Groups[2].Value)
                });
            }
            return lst;
        }

        private static string NormalizeFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var normalized = new StringBuilder(path.Length);
            for (int i = 0; i < path.Length; i++)
            {
                switch (path[i])
                {
                    case '\\':
                        normalized.Append('/');
                        break;
                    case '\r':
                        normalized.Append("/r");
                        break;
                    case '\n':
                        normalized.Append("/n");
                        break;
                    case '\t':
                        normalized.Append("/t");
                        break;
                    default:
                        normalized.Append(path[i]);
                        break;
                }
            }
            return normalized.ToString();
        }

        private Dictionary<int, LstEntry> EnsureIdIndex()
        {
            if (_idIndex == null)
            {
                _idIndex = new Dictionary<int, LstEntry>(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    var e = Entries[i];
                    if (!_idIndex.ContainsKey(e.Id))
                        _idIndex[e.Id] = e;
                }
            }
            return _idIndex;
        }

        
        public LstEntry GetById(int id)
        {
            LstEntry entry;
            return EnsureIdIndex().TryGetValue(id, out entry) ? entry : null;
        }

        
        public LstEntry GetByPath(string path)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (string.Equals(Entries[i].FilePath, path, System.StringComparison.OrdinalIgnoreCase))
                    return Entries[i];
            return null;
        }

        
        public string ToContent()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Entries.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(Entries[i].Id);
                sb.Append(" `");
                sb.Append(Entries[i].FilePath);
                sb.Append('`');
            }
            return sb.ToString();
        }
    }
}
