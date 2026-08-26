using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

public class LanePredictorTests
{
    private static LanePredictor Predictor() => new(TestMeta.StandardCast());

    [Fact]
    public void UnambiguousComp_IsAssignedCorrectly()
    {
        var slots = TestMeta.EnemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus);

        var result = Predictor().Predict(slots);

        Assert.Equal(Lane.Top, result.ForCell(5)!.Lane);
        Assert.Equal(Lane.Jungle, result.ForCell(6)!.Lane);
        Assert.Equal(Lane.Mid, result.ForCell(7)!.Lane);
        Assert.Equal(Lane.Adc, result.ForCell(8)!.Lane);
        Assert.Equal(Lane.Support, result.ForCell(9)!.Lane);
    }

    [Fact]
    public void OutOfOrderComp_IsStillAssignedByChampion()
    {
        // Support first, top last: if seat order dominated the champion prior, this would fail.
        var slots = TestMeta.EnemySlots(TestMeta.Nautilus, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Elise, TestMeta.Jax);

        var result = Predictor().Predict(slots);

        Assert.Equal(Lane.Support, result.ForCell(5)!.Lane);
        Assert.Equal(Lane.Mid, result.ForCell(6)!.Lane);
        Assert.Equal(Lane.Adc, result.ForCell(7)!.Lane);
        Assert.Equal(Lane.Jungle, result.ForCell(8)!.Lane);
        Assert.Equal(Lane.Top, result.ForCell(9)!.Lane);
    }

    [Fact]
    public void ProbabilitiesSumToOnePerSeat()
    {
        var result = Predictor().Predict(TestMeta.EnemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, 0, 0));

        foreach (var prediction in result.Predictions)
            Assert.Equal(1.0, prediction.Probabilities.Sum(), precision: 6);
    }

    [Fact]
    public void UnrevealedSeats_AreSpreadOverTheRemainingLanes()
    {
        // Three locked picks pin three lanes, so the two hidden seats can only be adc or support.
        var result = Predictor().Predict(TestMeta.EnemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, 0, 0));

        var fourth = result.ForCell(8)!;

        Assert.True(fourth.Probabilities[(int)Lane.Adc] > 0.4);
        Assert.True(fourth.Probabilities[(int)Lane.Support] > 0.4);
        Assert.True(fourth.Probabilities[(int)Lane.Top] < 0.05);
    }

    [Fact]
    public void LockedPicks_GetHighConfidence()
    {
        var result = Predictor().Predict(TestMeta.EnemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus));

        foreach (var prediction in result.Predictions)
        {
            Assert.True(prediction.Confidence > 0.7, $"cell {prediction.CellId} only reached {prediction.Confidence:P0}");
            Assert.False(prediction.IsUncertain);
        }
    }

    [Fact]
    public void FlexPick_IsReportedAsUncertain()
    {
        // Sylas alongside two other mid-capable champions: the tool should admit it does not know.
        var slots = TestMeta.EnemySlots(TestMeta.Sylas, TestMeta.Lucian, 0, 0, 0);

        var sylas = Predictor().Predict(slots).ForCell(5)!;

        Assert.True(sylas.Confidence < LanePrediction.LowConfidence);
        Assert.True(sylas.IsUncertain);
    }

    [Fact]
    public void OffMetaPick_StillGetsAnAssignment()
    {
        // Thresh has no recorded top games at all. Forcing the rest of the comp onto the other
        // lanes must not leave the solver with nothing.
        var slots = TestMeta.EnemySlots(TestMeta.Thresh, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus);

        var result = Predictor().Predict(slots);

        Assert.NotEqual(Lane.Unknown, result.ForCell(5)!.Lane);
        Assert.Equal(5, result.Predictions.Count(p => p.Lane != Lane.Unknown));
    }

    [Fact]
    public void EveryLaneIsAssignedExactlyOnce()
    {
        var result = Predictor().Predict(TestMeta.EnemySlots(TestMeta.Jax, TestMeta.Sylas, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus));

        var lanes = result.Predictions.Select(p => p.Lane).ToList();

        Assert.Equal(Lanes.Count, lanes.Distinct().Count());
        Assert.DoesNotContain(Lane.Unknown, lanes);
    }

    [Fact]
    public void ManualOverride_IsHonouredAndReshufflesTheRest()
    {
        // Insist Jax is jungling; Elise then has to move off jungle.
        var slots = TestMeta.EnemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus);
        var overrides = new Dictionary<long, Lane> { [5] = Lane.Jungle };

        var result = Predictor().Predict(slots, overrides);

        Assert.Equal(Lane.Jungle, result.ForCell(5)!.Lane);
        Assert.True(result.ForCell(5)!.IsManual);
        Assert.Equal(Lane.Top, result.ForCell(6)!.Lane);
        Assert.Equal(Lane.Mid, result.ForCell(7)!.Lane);
    }

    [Fact]
    public void ManualOverride_IsNeverFlaggedUncertain()
    {
        var slots = TestMeta.EnemySlots(TestMeta.Sylas, 0, 0, 0, 0);
        var overrides = new Dictionary<long, Lane> { [5] = Lane.Top };

        var sylas = Predictor().Predict(slots, overrides).ForCell(5)!;

        Assert.True(sylas.IsManual);
        Assert.False(sylas.IsUncertain);
    }

    [Fact]
    public void AllyLanes_ComeStraightFromTheClient()
    {
        var state = DraftState.From(new SessionBuilder().Build());

        var result = Predictor().Predict(state.Allies);

        Assert.Equal(Lane.Top, result.ForCell(0)!.Lane);
        Assert.Equal(Lane.Support, result.ForCell(4)!.Lane);
        Assert.Equal(1.0, result.ForCell(2)!.Confidence, precision: 6);
    }

    [Fact]
    public void AllyLanes_FallBackToPredictionWhenTheClientIsSilent()
    {
        // Blind pick and custom games leave assignedPosition empty.
        var session = new SessionBuilder()
            .AllyLane(0, string.Empty)
            .AllyLane(1, string.Empty)
            .AllyLane(2, string.Empty)
            .AllyLane(3, string.Empty)
            .AllyLane(4, string.Empty)
            .Locked(0, TestMeta.Aatrox)
            .Locked(1, TestMeta.LeeSin)
            .Locked(2, TestMeta.Ahri)
            .Locked(3, TestMeta.Ashe)
            .Locked(4, TestMeta.Thresh)
            .Build();

        var result = Predictor().Predict(DraftState.From(session).Allies);

        Assert.Equal(Lane.Top, result.ForCell(0)!.Lane);
        Assert.Equal(Lane.Jungle, result.ForCell(1)!.Lane);
        Assert.Equal(Lane.Mid, result.ForCell(2)!.Lane);
        Assert.Equal(Lane.Adc, result.ForCell(3)!.Lane);
        Assert.Equal(Lane.Support, result.ForCell(4)!.Lane);
    }

    [Fact]
    public void ChampionOnLane_ResolvesTheOpponentForScoring()
    {
        var slots = TestMeta.EnemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus);

        var result = Predictor().Predict(slots);

        Assert.Equal(TestMeta.Jax, result.ChampionOnLane(Lane.Top));
        Assert.Equal(TestMeta.Syndra, result.ChampionOnLane(Lane.Mid));
        Assert.Equal(TestMeta.Nautilus, result.ChampionOnLane(Lane.Support));
    }

    [Fact]
    public void ChampionOnLane_IsZeroWhileTheSeatIsHidden()
    {
        var result = Predictor().Predict(TestMeta.EnemySlots(TestMeta.Jax, 0, 0, 0, 0));

        Assert.Equal(TestMeta.Jax, result.ChampionOnLane(Lane.Top));
        Assert.Equal(0, result.ChampionOnLane(Lane.Mid));
    }

    [Fact]
    public void NoSlots_YieldsNothing()
    {
        Assert.Empty(Predictor().Predict([]).Predictions);
    }

    [Fact]
    public void EmptyMeta_StillProducesAValidAssignment()
    {
        // Before the first data update there are no role priors at all.
        var predictor = new LanePredictor(DraftPilot.Core.Data.MetaLookup.Empty);

        var result = predictor.Predict(TestMeta.EnemySlots(TestMeta.Jax, TestMeta.Elise, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Nautilus));

        Assert.Equal(Lanes.Count, result.Predictions.Select(p => p.Lane).Distinct().Count());
        Assert.All(result.Predictions, p => Assert.True(p.IsUncertain));
    }

    [Fact]
    public void SeatPriors_NudgeButDoNotOverrideTheChampion()
    {
        var slots = TestMeta.EnemySlots(TestMeta.Nautilus, TestMeta.Syndra, TestMeta.Lucian, TestMeta.Elise, TestMeta.Jax);
        var withSeatHint = new LanePredictor(TestMeta.StandardCast(), SeatPriors.Load(SeatPriorsPath));

        var result = withSeatHint.Predict(slots);

        // Seat 0 is nudged towards top, but Nautilus is a support and that has to win.
        Assert.Equal(Lane.Support, result.ForCell(5)!.Lane);
    }

    private static string SeatPriorsPath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "pick_order_priors.json");
}
