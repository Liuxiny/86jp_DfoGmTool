using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using GmPvfLib;

namespace Jp86.GmClient;

public sealed class LocalResourceSettings
{
    public string PvfDirectory { get; set; } = "";
    public string ImagePacks2Directory { get; set; } = "";
}

public sealed class LocalItemRecord
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Category { get; set; } = "";
    public int Rarity { get; set; }
    public int MinLevel { get; set; }
    public string IconPath { get; set; } = "";
    public int IconIndex { get; set; } = -1;
    public string ScriptPath { get; set; } = "";
}

internal sealed class PvfCatalogCache
{
    public string PvfPath { get; set; } = "";
    public long PvfLength { get; set; }
    public long PvfLastWriteUtcTicks { get; set; }
    public List<LocalItemRecord> Items { get; set; } = new();
}

public sealed class LocalPvfCatalog
{
    private static readonly Regex LstPattern = new(@"(\d+)\s+`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex IconPattern = new(@"`?([^`\s]+\.img)`?\s+(-?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _dataDirectory;
    private readonly string _settingsPath;
    private readonly string _cachePath;

    public LocalResourceSettings Settings { get; private set; } = new();
    public IReadOnlyList<LocalItemRecord> Items { get; private set; } = Array.Empty<LocalItemRecord>();
    public string PvfPath { get; private set; } = "";
    public bool IsReady => Items.Count > 0;

    public LocalPvfCatalog()
    {
        _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "86JP", "GM");
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _cachePath = Path.Combine(_dataDirectory, "pvf-index-v1.json.gz");
        Directory.CreateDirectory(_dataDirectory);
        LoadSettings();
        var preferredRoot = @"E:\86jp\夜空魅影";
        var preferredImages = Path.Combine(preferredRoot, "ImagePacks2");
        var changed = false;
        if (string.IsNullOrWhiteSpace(Settings.PvfDirectory) && File.Exists(Path.Combine(preferredRoot, "Script.pvf")))
        {
            Settings.PvfDirectory = preferredRoot;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(Settings.ImagePacks2Directory) && Directory.Exists(preferredImages))
        {
            Settings.ImagePacks2Directory = preferredImages;
            changed = true;
        }
        if (changed) SaveSettings();
    }

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        PvfPath = DiscoverPvf(Settings.PvfDirectory) ?? "";
        if (PvfPath.Length == 0)
        {
            Items = Array.Empty<LocalItemRecord>();
            return false;
        }

        var info = new FileInfo(PvfPath);
        var cache = await TryReadCacheAsync(cancellationToken).ConfigureAwait(false);
        if (cache != null
            && string.Equals(cache.PvfPath, PvfPath, StringComparison.OrdinalIgnoreCase)
            && cache.PvfLength == info.Length
            && cache.PvfLastWriteUtcTicks == info.LastWriteTimeUtc.Ticks
            && cache.Items.Count > 0)
        {
            Items = cache.Items;
            return true;
        }

        return await RebuildAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ConfigurePvfDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        var pvf = DiscoverPvf(directory);
        if (pvf == null)
            throw new InvalidDataException("所选文件夹中未找到可用的 PVF 文件。");
        Settings.PvfDirectory = Path.GetFullPath(directory);
        if (string.IsNullOrWhiteSpace(Settings.ImagePacks2Directory))
        {
            var sibling = Path.Combine(Settings.PvfDirectory, "ImagePacks2");
            var preferred = @"E:\86jp\夜空魅影\ImagePacks2";
            if (Directory.Exists(sibling)) Settings.ImagePacks2Directory = sibling;
            else if (Directory.Exists(preferred)) Settings.ImagePacks2Directory = preferred;
        }
        SaveSettings();
        PvfPath = pvf;
        return await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    public void ConfigureImagePacks2Directory(string directory)
    {
        if (!Directory.Exists(directory) || !Directory.EnumerateFiles(directory, "*.npk", SearchOption.TopDirectoryOnly).Any())
            throw new InvalidDataException("所选文件夹中没有 NPK 图标资源。");
        Settings.ImagePacks2Directory = Path.GetFullPath(directory);
        SaveSettings();
    }

    public async Task<bool> RebuildAsync(CancellationToken cancellationToken = default)
    {
        PvfPath = DiscoverPvf(Settings.PvfDirectory) ?? "";
        if (PvfPath.Length == 0)
            throw new FileNotFoundException("未找到 PVF，请先选择 PVF 所在文件夹。");

        var items = await Task.Run(() => Build(PvfPath, cancellationToken), cancellationToken).ConfigureAwait(false);
        var info = new FileInfo(PvfPath);
        var cache = new PvfCatalogCache
        {
            PvfPath = PvfPath,
            PvfLength = info.Length,
            PvfLastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
            Items = items,
        };
        await WriteCacheAsync(cache, cancellationToken).ConfigureAwait(false);
        Items = items;
        return items.Count > 0;
    }

    public IEnumerable<LocalItemRecord> Search(string query, string kind, int limit = 300)
    {
        query = (query ?? "").Trim();
        _ = int.TryParse(query, out var numericId);
        return Items
            .Where(i => string.Equals(i.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .Where(i => query.Length == 0 || i.ItemId == numericId || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.MinLevel < 0 ? int.MaxValue : i.MinLevel)
            .ThenBy(i => i.Rarity)
            .ThenBy(i => i.Name, StringComparer.CurrentCulture)
            .Take(Math.Clamp(limit, 1, 1000));
    }

    public static string? DiscoverPvf(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
        var direct = Directory.EnumerateFiles(directory, "*.pvf", SearchOption.TopDirectoryOnly).ToList();
        var candidates = direct.Count > 0
            ? direct
            : Directory.EnumerateDirectories(directory).Take(30)
                .SelectMany(child => Directory.EnumerateFiles(child, "*.pvf", SearchOption.TopDirectoryOnly)).ToList();
        return candidates
            .OrderByDescending(path => string.Equals(Path.GetFileName(path), "Script.pvf", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static List<LocalItemRecord> Build(string pvfPath, CancellationToken cancellationToken)
    {
        using var archive = PvfArchive.Open(pvfPath);
        var items = new List<LocalItemRecord>();
        BuildKind(archive, "equipment/equipment.lst", "equipment", "装备", items, cancellationToken);
        BuildKind(archive, "stackable/stackable.lst", "stackable", "消耗品", items, cancellationToken);
        return items.GroupBy(i => i.ItemId).Select(g => g.First()).OrderBy(i => i.ItemId).ToList();
    }

    private static void BuildKind(PvfArchive archive, string lstPath, string kind, string category,
        List<LocalItemRecord> output, CancellationToken cancellationToken)
    {
        var lstText = archive.GetFileContent(lstPath);
        if (string.IsNullOrWhiteSpace(lstText)) return;
        var root = lstPath[..lstPath.LastIndexOf('/')];
        var entries = LstPattern.Matches(lstText).Select(match =>
            (Id: int.Parse(match.Groups[1].Value), Relative: match.Groups[2].Value.Replace('\\', '/'))).ToArray();
        var results = new LocalItemRecord?[entries.Length];
        Parallel.For(0, entries.Length, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
        }, index =>
        {
            try
            {
                var entry = entries[index];
                var scriptPath = root + "/" + entry.Relative.TrimStart('/');
                var text = archive.GetFileContent(scriptPath);
                if (string.IsNullOrWhiteSpace(text)) return;
                if (kind == "equipment")
                {
                    var model = EquipmentFile.Parse(text);
                    if (string.IsNullOrWhiteSpace(model.Name)) return;
                    var icon = ParseIcon(model.Icon);
                    results[index] = new LocalItemRecord
                    {
                        ItemId = entry.Id, Name = model.Name, Kind = kind, Category = category,
                        Rarity = model.Rarity, MinLevel = model.MinimumLevel,
                        IconPath = icon.Path, IconIndex = icon.Index, ScriptPath = scriptPath,
                    };
                }
                else
                {
                    var model = StackableItemFile.Parse(text);
                    if (string.IsNullOrWhiteSpace(model.Name)) return;
                    var icon = ParseIcon(model.Icon);
                    results[index] = new LocalItemRecord
                    {
                        ItemId = entry.Id, Name = model.Name, Kind = kind, Category = category,
                        Rarity = model.Rarity, MinLevel = model.MinimumLevel,
                        IconPath = icon.Path, IconIndex = icon.Index, ScriptPath = scriptPath,
                    };
                }
            }
            catch { /* 损坏或不兼容的单条脚本跳过，不影响其余索引。 */ }
        });
        output.AddRange(results.Where(item => item != null)!);
    }

    private static (string Path, int Index) ParseIcon(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("", -1);
        var match = IconPattern.Match(raw.Replace('\\', '/'));
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out var index)) return ("", -1);
        var path = match.Groups[1].Value.TrimStart('/').ToLowerInvariant();
        if (!path.StartsWith("sprite/", StringComparison.Ordinal)) path = "sprite/" + path;
        return (path, index);
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                Settings = JsonSerializer.Deserialize<LocalResourceSettings>(File.ReadAllText(_settingsPath)) ?? new();
        }
        catch { Settings = new(); }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(_dataDirectory);
        var temp = _settingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Settings, JsonOptions));
        File.Move(temp, _settingsPath, true);
    }

    private async Task<PvfCatalogCache?> TryReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_cachePath)) return null;
            await using var file = File.OpenRead(_cachePath);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            return await JsonSerializer.DeserializeAsync<PvfCatalogCache>(gzip, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch { return null; }
    }

    private async Task WriteCacheAsync(PvfCatalogCache cache, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);
        var temp = _cachePath + ".tmp";
        await using (var file = File.Create(temp))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            await JsonSerializer.SerializeAsync(gzip, cache, cancellationToken: cancellationToken).ConfigureAwait(false);
        File.Move(temp, _cachePath, true);
    }
}
