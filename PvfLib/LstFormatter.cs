using System.Text;
using System.Text.RegularExpressions;

namespace GmPvfLib
{
    
    
    
    public static class LstFormatter
    {
        private static readonly Regex Pattern = new Regex(@"(\d+)\s+(`[^`]+`)", RegexOptions.Compiled);

        public static string Format(string input, int indentLevel = 0, int itemsPerLine = 1)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            input = input.Trim();
            var matches = Pattern.Matches(input);

            if (matches.Count == 0)
                return new string(' ', indentLevel * 4) + input;

            string indent = new string(' ', indentLevel * 4);
            int perRow = itemsPerLine > 0
                ? itemsPerLine
                : matches.Count <= 5 ? 1 : matches.Count <= 15 ? 2 : 3;

            var sb = new StringBuilder();
            for (int i = 0; i < matches.Count; i++)
            {
                if (i % perRow == 0)
                {
                    if (i > 0) sb.AppendLine();
                    sb.Append(indent);
                }
                else
                {
                    sb.Append("   ");
                }
                sb.Append($"{matches[i].Groups[1].Value} {matches[i].Groups[2].Value}");
            }
            return sb.ToString();
        }
    }
}
