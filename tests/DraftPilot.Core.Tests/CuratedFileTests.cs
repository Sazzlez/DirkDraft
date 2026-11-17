using System.Text.Json;
using DraftPilot.Core.Data;
using DraftPilot.Meta.Riot;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Traits and combos have one source, <c>data/</c>, and go to the data branch with the numbers: the
/// published form, and the warning for a champion the traits do not know.
/// </summary>
public sealed class CuratedFileTests
{
    /// <summary>The repository's data folder, found from wherever the tests run (as TraitFileTests does).</summary>
    private static readonly string Data = FindData();

    private static string FindData()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "champion_traits.json")))
                return candidate;
        }

        throw new DirectoryNotFoundException("data/ nicht gefunden — Test läuft außerhalb des Repos?");
    }

    [Fact]
    public void ThePublishedFile_IsTheDocumentWithoutItsComment_AndWithTheVersion()
    {
        var published = CuratedFiles.Publish(
            """
            {
              "$comment": ["Notes for whoever edits the file."],
              // a comment and a trailing comma are forgiven
              "champions": { "Ahri": { "engage": 1, "cc": 2 }, },
            }
            """,
            "champions");

        using var document = JsonDocument.Parse(published!);
        Assert.Equal(CuratedFiles.Version, document.RootElement.GetProperty("version").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("$comment", out _));
        Assert.Equal(2, document.RootElement.GetProperty("champions").GetProperty("Ahri").GetProperty("cc").GetInt32());
        Assert.DoesNotContain('\n', published!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ kaputt")]
    [InlineData("[]")]
    [InlineData("""{ "version": 1 }""")]
    [InlineData("""{ "champions": [] }""")]
    public void ABrokenFile_IsNotPublished(string? json)
        => Assert.Null(CuratedFiles.Publish(json, "champions"));

    [Fact]
    public void TheShippedFiles_ArePublishable()
    {
        // The files of data/, as the tools project copies them beside its exe.
        var traits = CuratedFiles.Publish(File.ReadAllText(Path.Combine(Data, "champion_traits.json")), "champions");
        var combos = CuratedFiles.Publish(File.ReadAllText(Path.Combine(Data, "combos.json")), "combos");

        using var traitDocument = JsonDocument.Parse(traits!);
        Assert.True(traitDocument.RootElement.GetProperty("champions").EnumerateObject().Count() > 150);
        using var comboDocument = JsonDocument.Parse(combos!);
        Assert.Contains("Amumu", comboDocument.RootElement.GetProperty("combos").GetProperty("aoeEngage").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("E und R", comboDocument.RootElement.GetProperty("engageSkill").GetProperty("zac").GetString());
    }

    [Fact]
    public void AChampionWithoutTraits_IsNamed()
    {
        var traits = TraitTable.FromEntries(
        [
            new KeyValuePair<string, ChampionTraits>("Ahri", ChampionTraits.Unknown),
            new KeyValuePair<string, ChampionTraits>("MonkeyKing", ChampionTraits.Unknown),
        ]);
        ChampionEntry[] champions =
        [
            new() { Id = 103, Key = "Ahri", Name = "Ahri" },
            new() { Id = 62, Key = "MonkeyKing", Name = "Wukong" },
            new() { Id = 950, Key = "Naafiri", Name = "Naafiri" },
            new() { Id = 951, Key = "Zaahen", Name = "Zaahen" },
        ];

        // By the key the traits are filed under, said by the name players know; a full table says nothing.
        Assert.Equal(["Naafiri", "Zaahen"], CuratedFiles.WithoutTraits(champions, traits));
        Assert.Empty(CuratedFiles.WithoutTraits(champions[..2], traits));
        Assert.Equal(4, CuratedFiles.WithoutTraits(champions, TraitTable.Empty).Count);
    }
}
