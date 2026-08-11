using System;
using System.Collections.Generic;
using System.Globalization;

namespace GmPvfLib
{
    internal static class PvfScriptValueReader
    {
        internal static int ReadFirstInteger(ScriptNode node, string content)
        {
            var values = ReadIntegers(node, content);
            return values.Count > 0 ? values[0] : -1;
        }

        internal static List<int> ReadIntegers(ScriptNode node, string content)
        {
            var result = new List<int>();
            if (node?.DataItems == null || string.IsNullOrWhiteSpace(content))
                return result;

            foreach (var item in node.DataItems)
            {
                foreach (var token in item.GetContent(content).Split(
                             new[] { ' ', '\t', '\r', '\n' },
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(
                            token.Trim('`'),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var value))
                    {
                        result.Add(value);
                    }
                }
            }

            return result;
        }
    }
}
