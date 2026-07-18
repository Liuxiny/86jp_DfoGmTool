using System.Globalization;
using System.Text.RegularExpressions;
using GmPvfLib;

var pvfPath = args.Length > 0
    ? args[0]
    : @"F:\Program Files\DNF_86jp\Codes\ServerS4A12_260718\dist\linux-x64\Data\Pvf\Script.pvf";

using var archive = PvfArchive.Open(pvfPath);
var premiumText = archive.GetFileContent("etc/premiumlist_new.etc");
var stackableEntries = LstFile.Parse(archive.GetFileContent("stackable/stackable.lst"))
    .Entries
    .ToDictionary(entry => entry.Id, entry => entry.FilePath.Replace('\\', '/'));

var tokens = Tokenize(premiumText);
var premiumType = 0;
var rows = new List<(int type, int item, int days, string name)>();
for (var i = 0; i + 1 < tokens.Count; i++)
{
    if (tokens[i] == "[type]" && int.TryParse(tokens[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var type))
    {
        premiumType = type;
        continue;
    }

    if (premiumType <= 0 || tokens[i] != "[item]" || i + 4 >= tokens.Count)
        continue;

    if (!int.TryParse(tokens[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemCode)
        || tokens[i + 2] != "[term]"
        || !int.TryParse(tokens[i + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
        continue;

    rows.Add((premiumType, itemCode, days, ResolveStackableName(archive, stackableEntries, itemCode)));
}

foreach (var row in rows.OrderBy(r => r.type).ThenBy(r => r.days).ThenBy(r => r.item))
    Console.WriteLine($"{row.type}\t{row.item}\t{row.days}\t{row.name}");

static string ResolveStackableName(PvfArchive archive, Dictionary<int, string> entries, int itemId)
{
    if (!entries.TryGetValue(itemId, out var relative))
        return "";

    var text = archive.GetFileContent("stackable/" + relative);
    var parsed = StackableItemFile.Parse(text);
    if (!string.IsNullOrWhiteSpace(parsed.Name))
        return parsed.Name;

    var match = Regex.Match(text, @"\[name\][\s\S]*?`([^`]+)`", RegexOptions.Multiline);
    return match.Success ? match.Groups[1].Value : "";
}

static List<string> Tokenize(string text)
{
    var tokens = new List<string>();
    for (var i = 0; i < text.Length;)
    {
        if (char.IsWhiteSpace(text[i]))
        {
            i++;
            continue;
        }

        var end = i + 1;
        if (text[i] == '[')
        {
            end = text.IndexOf(']', i + 1) + 1;
        }
        else if (text[i] == '`')
        {
            end = text.IndexOf('`', i + 1) + 1;
            if (end <= 0)
                end = text.Length;
            i = end;
            continue;
        }
        else
        {
            while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '[')
                end++;
        }

        if (end <= i)
            break;

        tokens.Add(text.Substring(i, end - i));
        i = end;
    }

    return tokens;
}
