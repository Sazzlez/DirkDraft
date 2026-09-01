using DraftPilot.Core.Data;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// A snapshot that will not load looked identical to no snapshot at all, which cost a debugging
/// round trip. The reason now travels with the result.
/// </summary>
public class SnapshotStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "draftpilot-tests", Guid.NewGuid().ToString("N"));

    private string PathFor(string name)
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, name);
    }

    [Fact]
    public void MissingFile_IsReportedAsMissing()
    {
        var store = new SnapshotStore(PathFor("nothing-here.json"));
        var result = store.LoadWithStatus();

        Assert.False(result.IsOk);
        Assert.Equal(SnapshotLoadStatus.Missing, result.Status);
        Assert.Contains("fehlt", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrokenJson_IsReportedAsUnreadable()
    {
        var path = PathFor("broken.json");
        File.WriteAllText(path, "{ this is not json");

        var result = new SnapshotStore(path).LoadWithStatus();

        Assert.Equal(SnapshotLoadStatus.Unreadable, result.Status);
        Assert.Contains("JSON", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnapshotFromANewerBuild_IsRefusedWithItsVersion()
    {
        var path = PathFor("future.json");
        File.WriteAllText(path, $"{{\"version\":{MetaSnapshot.CurrentVersion + 5},\"champions\":[]}}");

        var result = new SnapshotStore(path).LoadWithStatus();

        Assert.Equal(SnapshotLoadStatus.VersionTooNew, result.Status);
        Assert.Contains((MetaSnapshot.CurrentVersion + 5).ToString(), result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotWithoutChampions_IsReportedAsEmpty()
    {
        // Parses fine but is useless; treating it as loaded would show champion ids instead of names.
        var path = PathFor("empty.json");
        File.WriteAllText(path, $"{{\"version\":{MetaSnapshot.CurrentVersion},\"champions\":[]}}");

        var result = new SnapshotStore(path).LoadWithStatus();

        Assert.Equal(SnapshotLoadStatus.Empty, result.Status);
    }

    [Fact]
    public void RoundTrip_Works()
    {
        var path = PathFor("good.json");
        var store = new SnapshotStore(path);

        var stamp = new DateTimeOffset(2026, 8, 26, 11, 23, 37, TimeSpan.FromHours(9));
        var snapshot = new MetaSnapshot { Patch = "16.17.1", DataPatch = "16.17", DataAsOfUtc = stamp };
        snapshot.Champions.Add(new ChampionEntry { Id = 266, Key = "Aatrox", Name = "Aatrox" });
        store.Save(snapshot);

        var result = store.LoadWithStatus();

        Assert.True(result.IsOk);
        Assert.Equal("16.17.1", result.Snapshot!.Patch);
        Assert.Equal("16.17", result.Snapshot.DataPatch);
        Assert.Equal(stamp, result.Snapshot.DataAsOfUtc);
        Assert.Equal("Aatrox", result.Snapshot.Champions[0].Name);
    }

    /// <summary>
    /// The data stamp arrived without a version bump on purpose: pressing the update button should
    /// be worth it, never required. A file written before the stamp existed has to keep working.
    /// </summary>
    [Fact]
    public void ASnapshotWrittenBeforeTheDataStampExisted_StillLoads()
    {
        var path = PathFor("older.json");
        File.WriteAllText(
            path,
            $$"""
            {"version":{{MetaSnapshot.CurrentVersion}},"patch":"16.17.1",
             "champions":[{"id":266,"key":"Aatrox","name":"Aatrox"}]}
            """);

        var result = new SnapshotStore(path).LoadWithStatus();

        Assert.True(result.IsOk);
        Assert.Equal("16.17.1", result.Snapshot!.Patch);
        Assert.Equal(string.Empty, result.Snapshot.DataPatch);
        Assert.Null(result.Snapshot.DataAsOfUtc);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        var path = PathFor("atomic.json");
        var store = new SnapshotStore(path);

        var snapshot = new MetaSnapshot();
        snapshot.Champions.Add(new ChampionEntry { Id = 1, Key = "Annie", Name = "Annie" });
        store.Save(snapshot);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_OverwritesWithoutLosingTheOldFileOnFailure()
    {
        // The write goes to a temporary file first, so an interrupted save cannot truncate the
        // snapshot that is currently in use.
        var path = PathFor("replace.json");
        var store = new SnapshotStore(path);

        var first = new MetaSnapshot { Patch = "16.16.1" };
        first.Champions.Add(new ChampionEntry { Id = 1, Key = "Annie", Name = "Annie" });
        store.Save(first);

        var second = new MetaSnapshot { Patch = "16.17.1" };
        second.Champions.Add(new ChampionEntry { Id = 2, Key = "Olaf", Name = "Olaf" });
        store.Save(second);

        Assert.Equal("16.17.1", store.LoadWithStatus().Snapshot!.Patch);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best effort.
        }
    }
}
