using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta;
using DraftPilot.Meta.OpGg;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The build that has no opponent in it, pinned against a response captured from the live endpoint
/// (Darius, top, ranked, all tiers — 2026-09-19). This is what the window shows whenever the lane
/// opponent is not known: blind pick, the stretch before the enemy pick is revealed, and both
/// Abyss queues, where there is no lane opponent at all.
/// </summary>
public class AnalysisBuildParserTests
{
    private static readonly ChampionEntry Darius = new() { Id = 122, Name = "Darius" };

    private static OpGgNode Response { get; } = OpGgResponseParser.Parse(
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "OpGg", "analysis-build-darius-top.txt")));

    private static BuildPlan Plan { get; } =
        AnalysisBuildParser.Parse(Response, Darius, Lane.Top, mode: string.Empty, patch: "16.18.1");

    [Fact]
    public void ThePlanNamesItsChampionAndLaneAndNoOpponent()
    {
        Assert.Equal(122, Plan.ChampionId);
        Assert.Equal(Lane.Top, Plan.Lane);
        Assert.Equal(0, Plan.OpponentId);
        Assert.Equal(string.Empty, Plan.OpponentName);
        Assert.False(Plan.IsEmpty);
    }

    /// <summary>
    /// The whole reason this path replaced the invented stand-in opponent. The same slots in the
    /// matchup guide rest on 11 to 115 games; here they rest on tens and hundreds of thousands,
    /// and that is what makes "the build with the best win rate" a sentence with meaning.
    /// </summary>
    [Fact]
    public void EverySlotRestsOnASampleTheMatchupGuideCannotOffer()
    {
        Assert.True(Plan.CoreItems[0].Play > 10_000, $"Kern hat nur {Plan.CoreItems[0].Play} Games.");
        Assert.True(Plan.Boots[0].Play > 10_000, $"Schuhe haben nur {Plan.Boots[0].Play} Games.");
        Assert.True(Plan.Starters[0].Play > 10_000, $"Startitems haben nur {Plan.Starters[0].Play} Games.");
        Assert.True(Plan.Runes!.Play > 10_000, $"Runen haben nur {Plan.Runes.Play} Games.");
    }

    [Fact]
    public void TheRunePageIsCompleteEnoughToImport()
    {
        var runes = Plan.Runes!;

        // Four primary, two secondary, three shards — the shape the client's endpoint demands.
        Assert.Equal(4, runes.PrimaryRuneIds.Count);
        Assert.Equal(2, runes.SecondaryRuneIds.Count);
        Assert.Equal(3, runes.ShardIds.Count);
        Assert.Equal(8000, runes.PrimaryPathId);

        // Translated, never raw ids: "5008" on the card is the bug this pins.
        Assert.All(runes.Shards, shard => Assert.DoesNotMatch("^5\\d{3}$", shard));
    }

    /// <summary>
    /// The one slot this endpoint answers with a list, and therefore the one place the win-rate
    /// rule has anything to decide. All three entries have six-figure samples, so the record
    /// decides rather than the sample size.
    /// </summary>
    [Fact]
    public void TheLateItemsAreRankedByRecord()
    {
        Assert.NotEmpty(Plan.LateItems);

        var bounds = Plan.LateItems.Select(BuildChoice.LowerBound).ToList();

        for (var i = 1; i < bounds.Count; i++)
            Assert.True(bounds[i - 1] >= bounds[i], $"Platz {i} ({bounds[i]:F4}) steht über Platz {i - 1} ({bounds[i - 1]:F4}).");
    }

    /// <summary>
    /// The mode is what tells the card whether to head itself with a queue name or with a lane.
    /// A Rift plan must leave it empty, or every build card reads "Darius · ranked".
    /// </summary>
    [Fact]
    public void ALanePlan_CarriesNoMode()
        => Assert.Equal(string.Empty, Plan.Mode);

    [Fact]
    public void AnAbyssPlan_CarriesItsMode()
    {
        var aram = AnalysisBuildParser.Parse(Response, Darius, Lane.Unknown, BuildModes.Aram, "16.18.1");

        Assert.Equal(BuildModes.Aram, aram.Mode);
        Assert.Equal(Lane.Unknown, aram.Lane);
        Assert.Equal("ARAM", BuildModes.Display(aram.Mode));
    }

    /// <summary>
    /// The plan has to survive the cache round trip: the opponent-free build is stored under
    /// opponent 0, and a lane build and an ARAM build of the same champion must not collide.
    /// </summary>
    [Fact]
    public void ThePlanSurvivesTheCacheUnderItsOwnKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dirkdraft-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var cache = new BuildCache(directory);
            cache.Save(Plan);

            var loaded = cache.Load("16.18.1", 122, Lane.Top, opponentId: 0);

            Assert.NotNull(loaded);
            Assert.Equal(0, loaded.OpponentId);
            Assert.Equal(Plan.CoreItems[0].Play, loaded.CoreItems[0].Play);

            // A different lane is a different build and must not be served from this file.
            Assert.Null(cache.Load("16.18.1", 122, Lane.Support, opponentId: 0));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
