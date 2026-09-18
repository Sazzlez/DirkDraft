using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta;
using DraftPilot.Meta.OpGg;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The two places where an OP.GG response turns into a lane row. Everything the recommender says
/// about a champion rests on the win rate produced here, so both paths are pinned against real
/// captures — the edge cases are written inline, where a shifted tuple position would be obvious.
/// </summary>
public class SnapshotBuilderTests
{
    private static string FixtureDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "OpGg");

    private static OpGgNode Load(string name)
        => OpGgResponseParser.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, name)));

    private static OpGgNode TopEntry(string champion)
        => Load("lane-meta-win.txt")["data"]["positions"]["top"].Items
            .First(entry => entry["champion"].AsText() == champion);

    [Fact]
    public void LaneWinRate_IsComputedFromWinsAndGamesNotTheRoundedRate()
    {
        var sett = SnapshotBuilder.ReadLaneStat(TopEntry("Sett"), championId: 1, Lane.Top);
        var darius = SnapshotBuilder.ReadLaneStat(TopEntry("Darius"), championId: 2, Lane.Top);

        Assert.Equal(31118d / 61702, sett.WinRate, precision: 6);
        Assert.Equal(39477d / 79223, darius.WinRate, precision: 6);

        // Both report 0.50 in win_rate. Half a percentage point apart is the difference the tool
        // could not see before, and it is larger than anything shrinkage moves at this sample size.
        Assert.True(sett.WinRate - darius.WinRate > 0.005);
    }

    [Fact]
    public void ALaneRow_KeepsTheRestOfTheStatsItIsScoredOn()
    {
        var nasus = SnapshotBuilder.ReadLaneStat(TopEntry("Nasus"), championId: 75, Lane.Top);

        Assert.Equal(97743, nasus.Play);
        Assert.Equal(0.09, nasus.PickRate, precision: 3);
        Assert.Equal(0.47, nasus.BanRate, precision: 3);
        Assert.Equal(0.73, nasus.RoleRate, precision: 3);
        Assert.Equal(1, nasus.Tier);
        Assert.True(nasus.FromTierList);
    }

    [Fact]
    public void ALaneRowWithoutAWinCount_KeepsTheReportedRateInsteadOfReadingZero()
    {
        // The single most dangerous failure mode: a missing field reads as 0 through AsInt, so
        // without the presence check a renamed field would turn every champion into a 0 % champion.
        var entry = Inline("class Top: champion,play,win_rate\n\nTop(\"Sett\",61702,0.5)");

        var stat = SnapshotBuilder.ReadLaneStat(entry, championId: 1, Lane.Top);

        Assert.Equal(0.5, stat.WinRate, precision: 6);
    }

    [Fact]
    public void ALaneRowWithoutGames_FallsBackInsteadOfDividingByZero()
    {
        var entry = Inline("class Top: champion,play,win,win_rate\n\nTop(\"Sett\",0,0,0.5)");

        var stat = SnapshotBuilder.ReadLaneStat(entry, championId: 1, Lane.Top);

        Assert.Equal(0.5, stat.WinRate, precision: 6);
        Assert.True(double.IsFinite(stat.WinRate));
    }

    [Fact]
    public void ALaneRowWithNeitherRate_ReadsAsUnknownRatherThanAsALoss()
    {
        var entry = Inline("class Top: champion,play\n\nTop(\"Sett\",61702)");

        var stat = SnapshotBuilder.ReadLaneStat(entry, championId: 1, Lane.Top);

        Assert.Equal(0.5, stat.WinRate, precision: 6);
    }

    [Fact]
    public void TheLaneFallback_CarriesTheSampleSizeTheAnalysisActuallyReports()
    {
        var stats = Load("analysis-fields.txt")["data"]["summary"]["positions"].Items[0]["stats"];

        var fallback = SnapshotBuilder.ReadFallbackLaneStat(stats, championId: 86, Lane.Top);

        Assert.NotNull(fallback);
        Assert.Equal(394825, fallback.Play);
        Assert.Equal(1, fallback.Tier);
        Assert.Equal(0.09, fallback.PickRate, precision: 3);
        Assert.Equal(0.07, fallback.BanRate, precision: 3);
        Assert.Equal(0.91, fallback.RoleRate, precision: 3);

        // The analysis endpoint reports no win counter, so this row stays on the rounded rate.
        Assert.Equal(0.52, fallback.WinRate, precision: 3);

        // Not from the tier list: on a collision the tier list row must win, because the two
        // samples count different populations.
        Assert.False(fallback.FromTierList);
    }

    [Fact]
    public void TheLaneFallback_WithASampleLeavesTheRateWhereShrinkageCanSeeIt()
    {
        var stats = Load("analysis-fields.txt")["data"]["summary"]["positions"].Items[0]["stats"];
        var fallback = SnapshotBuilder.ReadFallbackLaneStat(stats, championId: 86, Lane.Top)!;

        // Play = 0 used to pin every one of these rows to exactly 50 %, whatever OP.GG reported.
        Assert.NotEqual(0.5, Shrinkage.Apply(fallback.WinRate, fallback.Play, Shrinkage.LanePrior), precision: 4);
    }

    [Fact]
    public void APositionWithoutARoleRate_AddsNoLaneRow()
    {
        var stats = Inline("class Stats: play,win_rate\n\nStats(1000,0.52)");

        Assert.Null(SnapshotBuilder.ReadFallbackLaneStat(stats, championId: 1, Lane.Top));
    }

    [Fact]
    public void UnmatchedFieldNames_AreCollectedFromTheResponse()
    {
        var sink = new SortedSet<string>(StringComparer.Ordinal);

        SnapshotBuilder.CollectUnmatchedFields(Load("lane-meta-win.txt"), sink);
        SnapshotBuilder.CollectUnmatchedFields(Load("analysis-fields.txt"), sink);

        Assert.Equal(
            ["data.positions.top[].gibtsnicht", "data.summary.positions[].stats.win"],
            sink);
    }

    /// <summary>
    /// Measured against the live endpoint: the analysis call leaves out the synergy branch for the
    /// champion's own lane, so a support champion rejects <c>data.synergies.support[]…</c> while a
    /// top champion does not. Reporting that would put a false alarm under every update.
    /// </summary>
    [Fact]
    public void AFieldOnlySomeResponsesReject_IsNotReportedAsMissing()
    {
        var reports = new Dictionary<string, (int Requested, int Unmatched)>(StringComparer.Ordinal)
        {
            ["data.synergies.support[].play"] = (173, 51),
            ["data.summary.positions[].stats.win_rate"] = (173, 0),
        };

        Assert.Empty(SnapshotBuilder.MissingFields(reports));
    }

    [Fact]
    public void AFieldEveryResponseRejects_IsReportedAsMissing()
    {
        var reports = new Dictionary<string, (int Requested, int Unmatched)>(StringComparer.Ordinal)
        {
            ["data.summary.positions[].stats.win"] = (173, 173),
            ["data.synergies.support[].play"] = (173, 51),
        };

        Assert.Equal("data.summary.positions[].stats.win", Assert.Single(SnapshotBuilder.MissingFields(reports)));
    }

    [Fact]
    public void AResponseWithoutDiagnostics_CollectsNothing()
    {
        var sink = new SortedSet<string>(StringComparer.Ordinal);

        SnapshotBuilder.CollectUnmatchedFields(Load("lane-meta-all.txt"), sink);

        Assert.Empty(sink);
    }

    [Fact]
    public void AResponseWithoutTrends_ReportsNoDataStamp()
    {
        var (version, asOf) = SnapshotBuilder.ReadDataStamp(Load("analysis-thresh.txt"));

        Assert.Null(version);
        Assert.Null(asOf);
    }

    /// <summary>
    /// OP.GG rounds the pick rate to two decimals, and the ban value reads it times eight — one
    /// rounding step is 0.08 of a gate that rarely exceeds 0.6. The divisor is not in the response
    /// but follows from it: play / pick_rate is the same sample size in every row.
    /// </summary>
    [Fact]
    public void PickRates_AreRecomputedFromTheGameCounts()
    {
        var rows = Rows(
            // The ruler: rates far enough above the rounding to be worth dividing by.
            (100_000, 0.10),
            (120_000, 0.12),
            (80_000, 0.08),
            (200_000, 0.20),
            (50_000, 0.05),
            (60_000, 0.06),
            (70_000, 0.07),
            (90_000, 0.09),
            (110_000, 0.11),
            (130_000, 0.13),
            // Two champions the rounding cannot tell apart: both arrive as 0.01.
            (14_900, 0.01),
            (5_100, 0.01));

        SnapshotBuilder.UnroundPickRates(rows);

        Assert.Equal(0.0149, rows[^2].PickRate, precision: 4);
        Assert.Equal(0.0051, rows[^1].PickRate, precision: 4);
    }

    [Fact]
    public void WithoutEnoughThickRows_ThePickRatesAreLeftAlone()
    {
        var rows = Rows((100_000, 0.10), (14_900, 0.01));

        SnapshotBuilder.UnroundPickRates(rows);

        Assert.Equal(0.01, rows[^1].PickRate, precision: 4);
    }

    /// <summary>A row without a game count has nothing to divide; it must not end up at zero.</summary>
    [Fact]
    public void ARowWithoutGames_KeepsItsRate()
    {
        var rows = Rows(
            (100_000, 0.10), (120_000, 0.12), (80_000, 0.08), (200_000, 0.20), (50_000, 0.05),
            (60_000, 0.06), (70_000, 0.07), (90_000, 0.09), (110_000, 0.11), (130_000, 0.13),
            (0, 0.02));

        SnapshotBuilder.UnroundPickRates(rows);

        Assert.Equal(0.02, rows[^1].PickRate, precision: 4);
    }

    private static List<LaneStat> Rows(params (int Play, double PickRate)[] entries)
        => [.. entries.Select((entry, index) => new LaneStat
        {
            ChampionId = index + 1,
            Lane = Lane.Top,
            Play = entry.Play,
            PickRate = entry.PickRate,
        })];

    private static OpGgNode Inline(string payload) => OpGgResponseParser.Parse(payload);
}
