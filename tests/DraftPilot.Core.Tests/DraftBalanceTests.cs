using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The draft balance shown over the two team columns. It is a claim about the whole draft, so the
/// tests pin down the two properties that keep it honest: it stays near the middle unless the data
/// actually says otherwise, and it never invents a side for seats nobody has revealed.
/// </summary>
public class DraftBalanceTests
{
    private const int AllyTop = 1, AllyMid = 2, EnemyTop = 3, EnemyMid = 4;

    private static MetaLookup Meta(double allyTopWinRate = 0.50, double enemyTopWinRate = 0.50, double? topMatchup = null)
    {
        var builder = new MetaBuilder()
            .Champion(AllyTop, "AllyTop").InLane(AllyTop, Lane.Top, winRate: allyTopWinRate, play: 5000)
            .Champion(AllyMid, "AllyMid").InLane(AllyMid, Lane.Mid, winRate: 0.50, play: 5000)
            .Champion(EnemyTop, "EnemyTop").InLane(EnemyTop, Lane.Top, winRate: enemyTopWinRate, play: 5000)
            .Champion(EnemyMid, "EnemyMid").InLane(EnemyMid, Lane.Mid, winRate: 0.50, play: 5000);

        if (topMatchup is { } rate)
            builder.Matchup(AllyTop, EnemyTop, Lane.Top, winRate: rate, play: 5000);

        return builder.Build();
    }

    /// <summary>Both sides on top and mid, with the champions given.</summary>
    private static (DraftState State, LanePredictionResult Allies, LanePredictionResult Enemies) Draft(
        MetaLookup meta, int allyTop = AllyTop, int enemyTop = EnemyTop)
    {
        var session = new SessionBuilder()
            .LocalPlayer(0)
            .Locked(0, allyTop)
            .Locked(2, AllyMid)
            .Locked(5, enemyTop)
            .Locked(7, EnemyMid)
            .Build();

        var state = DraftState.From(session);
        var predictor = new LanePredictor(meta);

        return (state, predictor.Predict(state.Allies), predictor.Predict(state.Enemies));
    }

    [Fact]
    public void WithoutADraft_ThereIsNothingToJudge()
    {
        var balance = DraftBalance.Estimate(Meta(), DraftState.Inactive, LanePredictionResult.Empty, LanePredictionResult.Empty);

        Assert.False(balance.HasData);
    }

    [Fact]
    public void WithoutMetaData_ThereIsNothingToJudge()
    {
        var meta = Meta();
        var (state, allies, enemies) = Draft(meta);

        var balance = DraftBalance.Estimate(MetaLookup.Empty, state, allies, enemies);

        Assert.False(balance.HasData);
    }

    [Fact]
    public void EquallyStrongTeams_AreEven()
    {
        var meta = Meta();
        var (state, allies, enemies) = Draft(meta);

        var balance = DraftBalance.Estimate(meta, state, allies, enemies);

        Assert.True(balance.HasData);
        Assert.Equal(0.5, balance.AllyWinRate, precision: 6);
    }

    [Fact]
    public void TheTwoSides_AlwaysAddUpToOne()
    {
        var meta = Meta(allyTopWinRate: 0.56, topMatchup: 0.62);
        var (state, allies, enemies) = Draft(meta);

        var balance = DraftBalance.Estimate(meta, state, allies, enemies);

        Assert.Equal(1.0, balance.AllyWinRate + balance.EnemyWinRate, precision: 9);
    }

    [Fact]
    public void StrongerLaneWinRates_TipItOurWay()
    {
        var meta = Meta(allyTopWinRate: 0.56, enemyTopWinRate: 0.47);
        var (state, allies, enemies) = Draft(meta);

        var balance = DraftBalance.Estimate(meta, state, allies, enemies);

        Assert.True(balance.AllyWinRate > 0.5, $"erwartet über 50 %, war {balance.AllyWinRate:P2}");
        Assert.Equal(4, balance.RatedChampions);
    }

    [Fact]
    public void AFavourableDuel_TipsItOurWay_AndIsCounted()
    {
        // Same lane strength on both sides; only the direct matchup speaks.
        var meta = Meta(topMatchup: 0.62);
        var (state, allies, enemies) = Draft(meta);

        var balance = DraftBalance.Estimate(meta, state, allies, enemies);

        Assert.True(balance.AllyWinRate > 0.5, $"erwartet über 50 %, war {balance.AllyWinRate:P2}");
        Assert.Equal(1, balance.ContestedLanes);
    }

    [Fact]
    public void ALosingDuel_TipsItTheirWay()
    {
        var meta = Meta(topMatchup: 0.38);
        var (state, allies, enemies) = Draft(meta);

        var balance = DraftBalance.Estimate(meta, state, allies, enemies);

        Assert.True(balance.AllyWinRate < 0.5, $"erwartet unter 50 %, war {balance.AllyWinRate:P2}");
    }

    /// <summary>
    /// Five champions at 52 % are not a 95 % team: the terms are averaged, never summed, so the
    /// number stays in the band real drafts live in.
    /// </summary>
    [Fact]
    public void ManyStrongPicks_DoNotStackIntoACertainty()
    {
        var builder = new MetaBuilder();
        for (var id = 1; id <= 5; id++)
            builder.Champion(id, $"Ally{id}").InLane(id, (Lane)(id - 1), winRate: 0.56, play: 5000);
        for (var id = 6; id <= 10; id++)
            builder.Champion(id, $"Enemy{id}").InLane(id, (Lane)(id - 6), winRate: 0.44, play: 5000);

        var meta = builder.Build();

        var session = new SessionBuilder().LocalPlayer(0);
        for (var seat = 0; seat < 5; seat++)
            session.Locked(seat, seat + 1).Locked(seat + 5, seat + 6);

        var state = DraftState.From(session.Build());
        var predictor = new LanePredictor(meta);
        var balance = DraftBalance.Estimate(meta, state, predictor.Predict(state.Allies), predictor.Predict(state.Enemies));

        Assert.True(balance.AllyWinRate > 0.5);
        Assert.InRange(balance.AllyWinRate, 0.5, 0.65);
    }
}
