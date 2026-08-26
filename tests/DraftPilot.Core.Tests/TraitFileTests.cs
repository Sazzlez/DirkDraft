using System.Text.Json;
using DraftPilot.Core.Data;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Guards the shipped <c>data/champion_traits.json</c>. Champions missing from it silently lose
/// every composition check — which is exactly what happened when seven post-release champions were
/// absent — so the file itself is under test, not just the code that reads it.
/// </summary>
public class TraitFileTests
{
    private static string FilePath { get; } = Locate();

    /// <summary>Walks up from the test output directory to the repository's data folder.</summary>
    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "data", "champion_traits.json");
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("data/champion_traits.json nicht gefunden — Test läuft außerhalb des Repos?");
    }

    [Fact]
    public void EveryEntryInTheFileParses()
    {
        var table = TraitTable.Load(FilePath);

        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        var declared = document.RootElement.GetProperty("champions").EnumerateObject().Count();

        // A malformed entry would be skipped silently; the counts diverging is the tell.
        Assert.Equal(declared, table.Count);
    }

    [Fact]
    public void CoversTheFullCurrentRoster()
    {
        // 173 champions as of patch 16.17. The count only ever grows; shrinking means entries were
        // lost, not that Riot deleted champions.
        Assert.True(TraitTable.Load(FilePath).Count >= 173,
            $"Nur {TraitTable.Load(FilePath).Count} Champions — Einträge verloren?");
    }

    [Theory]
    [InlineData("Belveth")]
    [InlineData("Ambessa")]
    [InlineData("Mel")]
    [InlineData("Aurora")]
    [InlineData("Yunara")]
    [InlineData("Locke")]
    [InlineData("Zaahen")]
    public void RecentChampionsAreKnown(string key)
    {
        // The seven that were missing. IsKnown is what makes the composition rules fire for them.
        Assert.True(TraitTable.Load(FilePath).For(key).IsKnown);
    }

    [Fact]
    public void ScalingValuesAreAllValid()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));

        foreach (var entry in document.RootElement.GetProperty("champions").EnumerateObject())
        {
            var scaling = entry.Value.GetProperty("scaling").GetString();
            Assert.True(scaling is "early" or "mid" or "late", $"{entry.Name}: scaling '{scaling}'");
        }
    }
}
