using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The lane-by-lane overview the in-game view shows. Two properties matter: it pairs the right
/// champions on the right lanes, and it never fills a gap with a number — a missing duel statistic
/// and an even duel look nothing alike to a reader, so they must not look alike in the data.
/// </summary>
public class LaneMatchupsTests
{
    private const int AllyTop = 1, AllyMid = 2, EnemyTop = 3, EnemyMid = 4;

    private static MetaLookup Meta(bool withTopMatchup = true)
    {
        var builder = new MetaBuilder()
            .Champion(AllyTop, "AllyTop").InLane(AllyTop, Lane.Top, winRate: 0.52, play: 5000)
            .Champion(AllyMid, "AllyMid").InLane(AllyMid, Lane.Mid, winRate: 0.50, play: 5000)
            .Champion(EnemyTop, "EnemyTop").InLane(EnemyTop, Lane.Top, winRate: 0.50, play: 5000)
            .Champion(EnemyMid, "EnemyMid").InLane(EnemyMid, Lane.Mid, winRate: 0.50, play: 5000);

        if (withTopMatchup)
            builder.Matchup(AllyTop, EnemyTop, Lane.Top, winRate: 0.58, play: 4000);

        return builder.Build();
    }

    private static (LanePredictionResult Allies, LanePredictionResult Enemies) Predict(
        MetaLookup meta, bool withEnemies = true)
    {
        var session = new SessionBuilder().LocalPlayer(0).Locked(0, AllyTop).Locked(2, AllyMid);

        if (withEnemies)
            session.Locked(5, EnemyTop).Locked(7, EnemyMid);

        var state = DraftState.From(session.Build());
        var predictor = new LanePredictor(meta);

        return (predictor.Predict(state.Allies), predictor.Predict(state.Enemies));
    }

    [Fact]
    public void EachOccupiedLane_BecomesOneRow_InDraftOrder()
    {
        var meta = Meta();
        var (allies, enemies) = Predict(meta);

        var rows = LaneMatchups.For(meta, allies, enemies);

        Assert.Equal([Lane.Top, Lane.Mid], rows.Select(row => row.Lane));
    }

    [Fact]
    public void ALaneNobodyPlaysOn_IsLeftOut()
    {
        var meta = Meta();
        var (allies, enemies) = Predict(meta);

        var rows = LaneMatchups.For(meta, allies, enemies);

        Assert.DoesNotContain(Lane.Jungle, rows.Select(row => row.Lane));
        Assert.DoesNotContain(Lane.Support, rows.Select(row => row.Lane));
    }

    [Fact]
    public void AKnownDuel_CarriesItsRateAndSample()
    {
        var meta = Meta();
        var (allies, enemies) = Predict(meta);

        var top = LaneMatchups.For(meta, allies, enemies).Single(row => row.Lane == Lane.Top);

        Assert.Equal(AllyTop, top.AllyId);
        Assert.Equal(EnemyTop, top.EnemyId);
        Assert.True(top.HasWinRate);

        // Shrunk towards 50 %, so above the middle but short of the raw 58 %.
        Assert.InRange(top.WinRate!.Value, 0.5, 0.58);
        Assert.Equal(4000, top.Play);
    }

    /// <summary>
    /// Mid has both champions but no matchup row. Null rather than 0.5: the view shows the pairing
    /// without a figure, instead of claiming a measured even duel.
    /// </summary>
    [Fact]
    public void ADuelWithoutAStatistic_HasNoRate()
    {
        var meta = Meta();
        var (allies, enemies) = Predict(meta);

        var mid = LaneMatchups.For(meta, allies, enemies).Single(row => row.Lane == Lane.Mid);

        Assert.Equal(AllyMid, mid.AllyId);
        Assert.Equal(EnemyMid, mid.EnemyId);
        Assert.False(mid.HasWinRate);
        Assert.Null(mid.WinRate);
        Assert.Equal(0, mid.Play);
    }

    /// <summary>Blind pick: our side is there, theirs is not — the row still names who we sent.</summary>
    [Fact]
    public void WithTheEnemyHidden_TheRowKeepsOurSideAndHasNoRate()
    {
        var meta = Meta();
        var (allies, enemies) = Predict(meta, withEnemies: false);

        var rows = LaneMatchups.For(meta, allies, enemies);
        var top = rows.Single(row => row.Lane == Lane.Top);

        Assert.Equal(AllyTop, top.AllyId);
        Assert.Equal(0, top.EnemyId);
        Assert.False(top.HasWinRate);
    }

    [Fact]
    public void WithoutAnyDraft_ThereAreNoRows()
    {
        var rows = LaneMatchups.For(Meta(), LanePredictionResult.Empty, LanePredictionResult.Empty);

        Assert.Empty(rows);
    }
}
