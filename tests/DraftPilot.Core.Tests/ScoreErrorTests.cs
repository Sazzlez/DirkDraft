using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The error bar behind every score. Its job is to stop the panel from claiming an order it cannot
/// support — so the tests are about the claims, not about the arithmetic for its own sake.
/// <para>
/// The closed form used here is checked against an actual bootstrap on the stored snapshot by
/// <c>Tools -- noise</c>; measured on the real support draft it agreed to two decimals on all eight
/// candidates (Rell 1,51 analytic against 1,48 resampled).
/// </para>
/// </summary>
public class ScoreErrorTests
{
    private static Recommendation Item(string name, double score, double uncertainty)
        => new(1, name, score, [], [], uncertainty);

    [Fact]
    public void ARateWithoutGames_CarriesNoError()
    {
        // Shrinkage pins it to 0.5, so the term says nothing and must not pretend to a spread.
        Assert.Equal(0, ScoreError.LogitVariance(0.5, play: 0, Shrinkage.LanePrior));
    }

    [Fact]
    public void ThinnerSamples_CarryMoreError()
    {
        var thin = ScoreError.LogitVariance(0.547, play: 111, Shrinkage.SynergyPrior);
        var thick = ScoreError.LogitVariance(0.518, play: 43_504, Shrinkage.LanePrior);

        Assert.True(thin > thick * 10, $"111 Spiele müssen deutlich unsicherer sein als 43.504 ({thin} vs {thick}).");
    }

    /// <summary>
    /// The real numbers behind the support draft that prompted this work: a 43.504-game lane rate
    /// and a 111-game duo. Almost the whole error comes from the duo, which is the point — a single
    /// thin statistic decided the order of the top four.
    /// </summary>
    [Fact]
    public void TheErrorIsDominatedByTheThinnestTerm()
    {
        var budget = new ErrorBudget();
        budget.Add(ScoreError.LogitVariance(0.5178, 43_504, Shrinkage.LanePrior));

        var laneOnly = ScoreError.AsPoints(budget.StandardError);

        // The synergy term is a damped mean over exactly one partner.
        var scale = ScoreModel.SynergyDamping;
        budget.Add(scale * scale * ScoreError.LogitVariance(0.5473, 111, Shrinkage.SynergyPrior));

        var withSynergy = ScoreError.AsPoints(budget.StandardError);

        Assert.InRange(laneOnly, 0.2, 0.3);
        Assert.InRange(withSynergy, 1.3, 1.7);
    }

    [Theory]
    [InlineData(0.06, 0)]   // ±1,5 Punkte: eine Nachkommastelle wäre erfunden
    [InlineData(0.02, 0)]   // ±0,5 Punkte: noch zu grob
    [InlineData(0.01, 1)]   // ±0,25 Punkte: eine Stelle ist gedeckt
    public void TheScoreIsPrintedOnlyAsPreciselyAsItsErrorAllows(double error, int expected)
        => Assert.Equal(expected, ScoreError.Decimals(error));

    /// <summary>
    /// One precision for the whole column. Deciding per row put "57 %" directly beside "54,3 %",
    /// which reads as a mistake rather than as information about the sample behind each.
    /// </summary>
    [Fact]
    public void TheWholeListSharesOnePrecision()
    {
        List<Recommendation> mixed =
        [
            Item("Rell", 0.572, 0.060),
            Item("Braum", 0.543, 0.008),
            Item("Thresh", 0.543, 0.023),
        ];

        // Braum alone would earn a decimal (±0,2 Punkte); the median of the three does not.
        Assert.Equal(1, ScoreError.Decimals(0.008));
        Assert.Equal(0, ScoreError.Decimals(mixed));
    }

    [Fact]
    public void AnEmptyList_KeepsTheFinerPrecision()
        => Assert.Equal(1, ScoreError.Decimals(new List<Recommendation>()));

    [Fact]
    public void AGapSmallerThanTheNoise_IsNotAnOrder()
    {
        // Rell 57,2 % ±1,5 against Leona 56,9 % ±0,9 — the gap is a fifth of the spread.
        var standing = ScoreError.Standing(0.572, 0.060, 0.569, 0.038);

        Assert.Equal(ScoreStanding.Tied, standing);
    }

    [Fact]
    public void AGapWellBeyondTheNoise_IsClear()
    {
        var standing = ScoreError.Standing(0.60, 0.005, 0.52, 0.005);

        Assert.Equal(ScoreStanding.Clear, standing);
    }

    [Fact]
    public void WithoutAnyErrorEstimate_NothingStrongerThanAheadIsClaimed()
    {
        Assert.Equal(ScoreStanding.Ahead, ScoreError.Standing(0.60, 0, 0.52, 0));
        Assert.Equal(ScoreStanding.Tied, ScoreError.Standing(0.52, 0, 0.60, 0));
    }

    [Fact]
    public void TheTiedLeaders_AreCountedByTheirOwnError()
    {
        // The measured support case: four candidates share first place, the fifth does not.
        List<Recommendation> items =
        [
            Item("Rell", 0.572, 0.060),
            Item("Leona", 0.569, 0.038),
            Item("Alistar", 0.560, 0.060),
            Item("Blitzcrank", 0.560, 0.027),
            Item("Thresh", 0.543, 0.023),
        ];

        Assert.Equal(4, ScoreError.CountLeadingTies(items));
    }

    [Fact]
    public void OnThickDataTheSameGapIsNoTie()
    {
        // Identical scores to the case above, but measured over enough games to separate them.
        List<Recommendation> items =
        [
            Item("A", 0.572, 0.004),
            Item("B", 0.569, 0.004),
        ];

        Assert.Equal(1, ScoreError.CountLeadingTies(items));
    }

    [Fact]
    public void ASingleEntryHasNothingToTieWith()
        => Assert.Equal(0, ScoreError.CountLeadingTies([Item("A", 0.57, 0.01)]));
}
