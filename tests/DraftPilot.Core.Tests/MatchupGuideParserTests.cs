using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Pins the matchup-guide parser against a captured response (Darius vs Jax, top). The guide is
/// the one OP.GG tool that answers in plain JSON instead of the typed-tuple format.
/// </summary>
public class MatchupGuideParserTests
{
    private static BuildPlan Plan { get; } = MatchupGuideParser.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OpGg", "matchup-guide-darius-jax.json")),
        championId: 122,
        championName: "Darius",
        opponentId: 24,
        opponentName: "Jax",
        Lane.Top,
        patch: "16.17.1");

    [Fact]
    public void CarriesTheMatchupIdentity()
    {
        Assert.Equal(122, Plan.ChampionId);
        Assert.Equal("Jax", Plan.OpponentName);
        Assert.Equal(Lane.Top, Plan.Lane);
        Assert.Equal("16.17.1", Plan.Patch);
        Assert.False(Plan.IsEmpty);
    }

    [Fact]
    public void PicksTheMostPlayedRunePage()
    {
        var runes = Plan.Runes;

        Assert.NotNull(runes);
        Assert.Equal("Precision", runes.PrimaryPath);
        Assert.Equal("Conqueror", runes.PrimaryRunes[0]);
        Assert.Equal(4, runes.PrimaryRunes.Count);
        Assert.Equal(2, runes.SecondaryRunes.Count);

        // Page-level sample, not the single concrete variant's.
        Assert.Equal(55, runes.Play);
        Assert.Equal(33.0 / 55.0, runes.WinRate, precision: 3);
    }

    [Fact]
    public void TranslatesStatShards()
    {
        // stat_mod_ids arrive as bare numbers; the display must not show "5008".
        Assert.Equal(3, Plan.Runes!.Shards.Count);
        Assert.All(Plan.Runes.Shards, shard => Assert.DoesNotMatch("^5\\d{3}$", shard));
        Assert.Contains("Adaptive Stärke", Plan.Runes.Shards);
    }

    [Fact]
    public void RanksCoreItemsBySample()
    {
        Assert.Equal(3, Plan.CoreItems.Count);
        Assert.True(Plan.CoreItems[0].Play >= Plan.CoreItems[1].Play);
        Assert.Equal(3, Plan.CoreItems[0].Items.Count);
    }

    [Fact]
    public void ReadsStartersBootsAndSpells()
    {
        Assert.NotEmpty(Plan.Starters);
        Assert.Contains("Doran's Blade", Plan.Starters[0].Items);

        Assert.NotEmpty(Plan.Boots);
        Assert.Equal("Plated Steelcaps", Plan.Boots[0].Items[0]);

        Assert.NotEmpty(Plan.SummonerSpells);
        Assert.Contains("Flash", Plan.SummonerSpells[0].Items);
    }

    [Fact]
    public void ReadsTheSkillPriority()
    {
        Assert.Equal("Q > E > W", Plan.SkillPriority);
    }

    [Fact]
    public void SurvivesAnEmptyPayload()
    {
        var empty = MatchupGuideParser.Parse("{}", 1, "A", 2, "B", Lane.Mid, "x");

        Assert.True(empty.IsEmpty);
        Assert.Null(empty.Runes);
    }

    [Fact]
    public void CacheRoundTripsThePlan()
    {
        var directory = Path.Combine(Path.GetTempPath(), "draftpilot-tests", Guid.NewGuid().ToString("N"));
        var cache = new BuildCache(directory);

        try
        {
            cache.Save(Plan);
            var loaded = cache.Load("16.17.1", 122, Lane.Top, 24);

            Assert.NotNull(loaded);
            Assert.Equal(Plan.Runes!.PrimaryRunes[0], loaded.Runes!.PrimaryRunes[0]);
            Assert.Equal(Plan.CoreItems[0].Items, loaded.CoreItems[0].Items);
            Assert.Equal(Plan.SkillPriority, loaded.SkillPriority);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
