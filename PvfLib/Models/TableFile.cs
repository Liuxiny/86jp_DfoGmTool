using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    public class TableFile
    {
        
        public long[] Values { get; private set; }
        
        public string Content { get; private set; }

        
        public long this[int index] => Values[index];

        
        public int Count => Values.Length;

        public static TableFile Parse(string content)
        {
            var tbl = new TableFile { Content = content ?? "" };
            if (string.IsNullOrWhiteSpace(content))
            {
                tbl.Values = Array.Empty<long>();
                return tbl;
            }

            var parts = content.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<long>(parts.Length);
            foreach (var p in parts)
            {
                long v;
                if (long.TryParse(p.Trim(), out v))
                    list.Add(v);
            }
            tbl.Values = list.ToArray();
            return tbl;
        }
    }
}
