using GmPvfLib;
using DfoGmTool.Services;
using System.Text.Json;

if (args.Length == 2 && args[0] == "--jobs")
{
    var pvfPath = Path.GetFullPath(args[1]);
    Environment.SetEnvironmentVariable("PVF_ARCHIVE_PATH", pvfPath);
    var index = new PvfIndexService(pvfPath);
    index.WarmInBackground();
    var deadline = DateTime.UtcNow.AddMinutes(3);
    while (!index.IsReady && index.BuildError == null && DateTime.UtcNow < deadline)
        Thread.Sleep(100);
    if (!index.IsReady) throw new InvalidOperationException(index.BuildError ?? "timeout");
    foreach (var job in index.GetAllJobOptions())
    {
        var property = job.GetType().GetProperty("value")
            ?? throw new InvalidOperationException("job value missing");
        var id = Convert.ToInt32(property.GetValue(job));
        Console.WriteLine(JsonSerializer.Serialize(new { job, options = index.GetJobGrowOptions(id) }));
    }
    return 0;
}

if (args.Length == 2 && args[0] == "--quests")
{
    var pvfPath = Path.GetFullPath(args[1]);
    Environment.SetEnvironmentVariable("PVF_ARCHIVE_PATH", pvfPath);
    var index = new PvfIndexService(pvfPath);
    index.WarmInBackground();
    var deadline = DateTime.UtcNow.AddMinutes(3);
    while (!index.IsReady && index.BuildError == null && DateTime.UtcNow < deadline)
        Thread.Sleep(100);
    if (!index.IsReady) throw new InvalidOperationException(index.BuildError ?? "timeout");
    foreach (var quest in index.AllQuestMeta.Values
        .Where(q => q != null && ((q.Job ?? "").Contains("demonic swordman", StringComparison.OrdinalIgnoreCase)
            || (q.Job ?? "").Contains("creator mage", StringComparison.OrdinalIgnoreCase)
            || (q.TargetCharacter ?? "").Contains("demonic swordman", StringComparison.OrdinalIgnoreCase)
            || (q.TargetCharacter ?? "").Contains("creator mage", StringComparison.OrdinalIgnoreCase)))
        .OrderBy(q => q.Id))
        Console.WriteLine($"{quest.Id}\t{quest.Name}\tjob={quest.Job}\ttarget={quest.TargetCharacter}\tgrow={quest.GrowType}\tjcq={quest.JobChangeQuestValue}\trewardChain={quest.RewardChainType}\tgrowNo={quest.GrowNumber}");
    return 0;
}

if (args.Length != 2) return 2;
using var archive = PvfArchive.Open(Path.GetFullPath(args[0]));
Console.Write(archive.GetFileContent(args[1]));
return 0;
