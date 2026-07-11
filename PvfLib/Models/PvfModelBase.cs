namespace GmPvfLib
{
    
    
    
    
    public abstract class PvfModelBase
    {
        
        public ScriptNode Root { get; protected set; }

        
        public string Content { get; protected set; }

        #region 动态访问

        
        public string GetValue(string tag)
        {
            var node = Root.GetChild(tag);
            return node != null ? node.GetFirstDataContent(Content).Trim() : null;
        }

        
        public int GetIntValue(string tag, int defaultVal = -1)
        {
            string s = GetValue(tag);
            int v;
            return s != null && int.TryParse(s.Trim(), out v) ? v : defaultVal;
        }

        
        public string GetStringValue(string tag)
        {
            return StripBacktick(GetValue(tag));
        }

        
        public bool HasTag(string tag)
        {
            return Root.GetChild(tag) != null;
        }

        #endregion

        #region 静态辅助

        
        protected static string StripBacktick(string s)
        {
            if (s != null && s.Length >= 2 && s[0] == '`' && s[s.Length - 1] == '`')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        
        protected static int ParseInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            var parts = s.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            int v;
            return parts.Length > 0 && int.TryParse(parts[0], out v) ? v : -1;
        }

        
        protected static int[] ParseIntPair(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                int a, b;
                if (int.TryParse(parts[0], out a) && int.TryParse(parts[1], out b))
                    return new[] { a, b };
            }
            if (parts.Length == 1)
            {
                int a;
                if (int.TryParse(parts[0], out a))
                    return new[] { a, a };
            }
            return null;
        }

        
        protected static int[] ParseIntArray(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            var list = new System.Collections.Generic.List<int>(parts.Length);
            foreach (var p in parts)
            {
                int v;
                if (int.TryParse(p, out v))
                    list.Add(v);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        #endregion
    }
}
