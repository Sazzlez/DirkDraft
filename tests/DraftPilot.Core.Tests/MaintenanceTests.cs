using System.Text;
using DraftPilot.Core.Config;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>The data directory must not silt up: stale plans, temp leftovers and logs get pruned.</summary>
public class MaintenanceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "draftpilot-tests", Guid.NewGuid().ToString("N"));

    public MaintenanceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static BuildPlan Plan(string patch) => new()
    {
        SchemaVersion = BuildPlan.CurrentSchemaVersion,
        ChampionId = 122,
        ChampionName = "Darius",
        OpponentId = 24,
        OpponentName = "Jax",
        Lane = Lane.Top,
        Patch = patch,
        CoreItems = [new ItemSet { Items = ["Item"], ItemIds = [1], Play = 1 }],
    };

    [Fact]
    public void CleanUp_RemovesOtherPatchesAndKeepsTheCurrentOne()
    {
        var cache = new BuildCache(_directory);
        cache.Save(Plan("16.17.1"));
        cache.Save(Plan("16.16.1"));

        var removed = cache.CleanUp("16.17.1", TimeSpan.FromDays(14));

        Assert.Equal(1, removed);
        Assert.NotNull(cache.Load("16.17.1", 122, Lane.Top, 24));
        Assert.Null(cache.Load("16.16.1", 122, Lane.Top, 24));
    }

    [Fact]
    public void CleanUp_RemovesEntriesPastTheirAge()
    {
        var cache = new BuildCache(_directory);
        cache.Save(Plan("16.17.1"));

        var path = cache.PathFor("16.17.1", 122, Lane.Top, 24);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromDays(30));

        Assert.Equal(1, cache.CleanUp("16.17.1", TimeSpan.FromDays(14)));
        Assert.Null(cache.Load("16.17.1", 122, Lane.Top, 24));
    }

    [Fact]
    public void CleanUp_WithoutAPatch_DeletesNothing()
    {
        // An empty patch would make the prefix "-", which matches no file — the sweep would then
        // clear the ENTIRE cache as "foreign patches".
        var cache = new BuildCache(_directory);
        cache.Save(Plan("16.17.1"));

        Assert.Equal(0, cache.CleanUp("", TimeSpan.FromDays(14)));
        Assert.Equal(0, cache.CleanUp("   ", TimeSpan.FromDays(14)));
        Assert.NotNull(cache.Load("16.17.1", 122, Lane.Top, 24));
    }

    [Fact]
    public void CleanUp_SweepsLeftoverTempFiles_ButSparesFreshOnes()
    {
        var cache = new BuildCache(_directory);
        cache.Save(Plan("16.17.1"));

        // Old leftovers go; a .tmp younger than a minute may be a Save in flight and stays —
        // deleting it under the writer made the plan silently never cache.
        var leftover = Path.Combine(_directory, "16.17.1-1-top-2.json.tmp");
        File.WriteAllText(leftover, "{}");
        File.SetLastWriteTimeUtc(leftover, DateTime.UtcNow - TimeSpan.FromMinutes(5));

        var inFlight = Path.Combine(_directory, "16.17.1-3-top-4.json.tmp");
        File.WriteAllText(inFlight, "{}");

        cache.CleanUp("16.17.1", TimeSpan.FromDays(14));

        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(inFlight));
        Assert.NotNull(cache.Load("16.17.1", 122, Lane.Top, 24));
    }

    [Fact]
    public void SweepTempFiles_LeavesFreshOnesAlone()
    {
        // A .tmp younger than a minute may be an atomic write in flight.
        var fresh = Path.Combine(_directory, "wird-gerade-geschrieben.tmp");
        File.WriteAllText(fresh, "x");

        var old = Path.Combine(_directory, "verwaist.tmp");
        File.WriteAllText(old, "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow - TimeSpan.FromHours(1));

        var removed = Maintenance.SweepTempFiles(_directory);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(fresh));
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void TrimLog_CutsAnOversizedLogToItsNewestEntries()
    {
        var log = Path.Combine(_directory, "crash.log");
        var builder = new StringBuilder();

        for (var i = 0; i < 20_000; i++)
            builder.AppendLine($"--- Eintrag {i} ---");

        File.WriteAllText(log, builder.ToString(), Encoding.UTF8);
        var before = new FileInfo(log).Length;

        Maintenance.TrimLog(log);

        var after = new FileInfo(log).Length;
        Assert.True(before > after, "Die Datei muss kleiner geworden sein.");
        Assert.True(after <= 128 * 1024 + 64, $"Zu groß nach dem Trim: {after} Bytes.");

        // The newest entry survives, and the file starts on a full line.
        var lines = File.ReadAllLines(log);
        Assert.Equal("--- Eintrag 19999 ---", lines[^1]);
        Assert.StartsWith("--- Eintrag ", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TrimLog_LeavesSmallLogsUntouched()
    {
        var log = Path.Combine(_directory, "klein.log");
        File.WriteAllText(log, "ein Eintrag\n");
        var stamp = File.GetLastWriteTimeUtc(log);

        Maintenance.TrimLog(log);
        Maintenance.TrimLog(Path.Combine(_directory, "gibts-nicht.log"));

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(log));
    }
}
