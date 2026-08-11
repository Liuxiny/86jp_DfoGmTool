using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GmPvfLib
{
    public static class ScriptValueTokenizer
    {
        private static readonly Regex TokenPattern = new Regex(
            "`([^`]*)`|\\S+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static List<string> Tokenize(string value)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
                return tokens;

            foreach (Match match in TokenPattern.Matches(value))
            {
                tokens.Add(match.Groups[1].Success
                    ? match.Groups[1].Value
                    : match.Value);
            }

            return tokens;
        }
    }
}
