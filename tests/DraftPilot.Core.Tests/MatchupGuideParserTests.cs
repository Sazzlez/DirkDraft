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

    /// <summary>
    /// The ids are what the client's rune-page endpoint and the icon files are addressed by; the
    /// nine of them must come out in the order the endpoint expects (4 + 2 + 3, keystone first).
    /// </summary>
    [Fact]
    public void CarriesTheRuneIds()
    {
        var runes = Plan.Runes!;

        Assert.Equal(8000, runes.PrimaryPathId);
        Assert.Equal(8200, runes.SecondaryPathId);
        Assert.Equal([8010, 9111, 9104, 8299], runes.PrimaryRuneIds);
        Assert.Equal([8224, 8234], runes.SecondaryRuneIds);
        Assert.Equal([5005, 5008, 5001], runes.ShardIds);
    }

    [Fact]
    public void CarriesTheItemIds()
    {
        Assert.All(Plan.CoreItems, set => Assert.Equal(set.Items.Count, set.ItemIds.Count));
        Assert.All(Plan.Starters, set => Assert.NotEmpty(set.ItemIds));
        Assert.All(Plan.SummonerSpells, set => Assert.NotEmpty(set.ItemIds));
    }

    /// <summary>The fixture's most played order starts Q E W and ends with the 15th point in W.</summary>
    [Fact]
    public void ReadsTheLevelByLevelSkillOrder()
    {
        Assert.Equal(15, Plan.SkillOrder.Count);
        Assert.Equal(["Q", "E", "W"], Plan.SkillOrder.Take(3));
        Assert.Equal("R", Plan.SkillOrder[5]);
        Assert.Equal("R", Plan.SkillOrder[10]);
    }

    /// <summary>
    /// Levels 16-18 are not in the data and get derived: the last ult point first, then the
    /// remaining ability points in first-levelled order. Derived steps must say they are derived.
    /// </summary>
    [Fact]
    public void ExtendsTheSkillOrderToEighteenLevels()
    {
        var steps = SkillPlan.Extend(Plan.SkillOrder);

        Assert.Equal(18, steps.Count);
        Assert.All(steps.Take(15), step => Assert.False(step.IsDerived));
        Assert.All(steps.Skip(15), step => Assert.True(step.IsDerived));

        // Darius: Q maxed by 9, E by 13; 15 points leave W at 3 → R16, W17, W18.
        Assert.Equal("R", steps[15].Ability);
        Assert.Equal("W", steps[16].Ability);
        Assert.Equal("W", steps[17].Ability);

        // Every ability ends at its cap: 5/5/5 plus 3 ult points.
        Assert.Equal(3, steps.Count(step => step.Ability == "R"));
        Assert.Equal(5, steps.Count(step => step.Ability == "Q"));
        Assert.Equal(5, steps.Count(step => step.Ability == "W"));
        Assert.Equal(5, steps.Count(step => step.Ability == "E"));
    }

    [Fact]
    public void SkillPlan_RefusesAnUnusableOrder()
    {
        Assert.Empty(SkillPlan.Extend([]));
        Assert.Empty(SkillPlan.Extend(["Q", "W", "E"]));

        // A cache file is just JSON on disk: null entries, garbage abilities and impossible
        // point counts must degrade to "no skill table", not crash or max out nonsense.
        Assert.Empty(SkillPlan.Extend(["Q", null, "E", "Q", "Q", "R", "Q", "E", "Q", "E", "R", "E", "E", "W", "W"]));
        Assert.Empty(SkillPlan.Extend([.. Enumerable.Repeat("X", 15)]));
        Assert.Empty(SkillPlan.Extend([.. Enumerable.Repeat("Q", 15)]));
    }

    /// <summary>
    /// Udyr levels R like a basic ability — four or five observed R points are real data, not
    /// garbage. A 3-point ultimate cap silently threw the whole table away for him.
    /// </summary>
    [Fact]
    public void SkillPlan_AcceptsAnOrderWhereRIsLevelledLikeABasicAbility()
    {
        var udyr = new[] { "Q", "R", "Q", "R", "W", "R", "Q", "R", "Q", "W", "Q", "W", "E", "E", "E" };

        var steps = SkillPlan.Extend(udyr);

        Assert.Equal(18, steps.Count);
        Assert.Equal(4, steps.Take(15).Count(step => step.Ability == "R"));
        Assert.All(steps.Skip(15), step => Assert.True(step.IsDerived));
    }

    [Fact]
    public void SkillPlan_KeepsAnEighteenLevelOrderAsObserved()
    {
        var full = new[] { "Q", "E", "W", "Q", "Q", "R", "Q", "E", "Q", "E", "R", "E", "E", "W", "W", "R", "W", "W" };

        var steps = SkillPlan.Extend(full);

        Assert.Equal(18, steps.Count);
        Assert.All(steps, step => Assert.False(step.IsDerived));
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

    /// <summary>
    /// A file without the field at all — everything written before the field existed — must be
    /// discarded too. This is why the property must NOT default to the current version.
    /// </summary>
    [Fact]
    public void CacheDiscardsAFileWithoutASchemaVersion()
    {
        var directory = Path.Combine(Path.GetTempPath(), "draftpilot-tests", Guid.NewGuid().ToString("N"));
        var cache = new BuildCache(directory);

        try
        {
            cache.Save(Plan);

            var path = cache.PathFor("16.17.1", 122, Lane.Top, 24);
            var lines = File.ReadAllLines(path).Where(line => !line.Contains("schemaVersion")).ToArray();
            File.WriteAllLines(path, lines);

            Assert.Null(cache.Load("16.17.1", 122, Lane.Top, 24));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
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
            Assert.Equal(Plan.Runes.PrimaryRuneIds, loaded.Runes.PrimaryRuneIds);
            Assert.Equal(Plan.Runes.SecondaryRuneIds, loaded.Runes.SecondaryRuneIds);
            Assert.Equal(Plan.Runes.ShardIds, loaded.Runes.ShardIds);
            Assert.Equal(Plan.CoreItems[0].Items, loaded.CoreItems[0].Items);
            Assert.Equal(Plan.CoreItems[0].ItemIds, loaded.CoreItems[0].ItemIds);
            Assert.Equal(Plan.SkillPriority, loaded.SkillPriority);
            Assert.Equal(Plan.SkillOrder, loaded.SkillOrder);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A file from an older build lacks fields this one renders. It must be treated as absent, so
    /// the draft fetches fresh instead of quietly showing half a plan.
    /// </summary>
    [Fact]
    public void CacheDiscardsAnOutdatedSchema()
    {
        var directory = Path.Combine(Path.GetTempPath(), "draftpilot-tests", Guid.NewGuid().ToString("N"));
        var cache = new BuildCache(directory);

        try
        {
            cache.Save(Plan);

            var path = cache.PathFor("16.17.1", 122, Lane.Top, 24);
            var json = File.ReadAllText(path).Replace(
                $"\"schemaVersion\": {BuildPlan.CurrentSchemaVersion}",
                "\"schemaVersion\": 1",
                StringComparison.Ordinal);
            File.WriteAllText(path, json);

            Assert.Null(cache.Load("16.17.1", 122, Lane.Top, 24));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
