using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// What a matchup rate falls back to when its own sample is thin.
/// <para>
/// This is not a detail. The median stored matchup has 197 games against a prior weight of 150, so
/// the prior carries 43 % of what the score reads — for a 60-game edge, 71 %. Pulling towards 50 %
/// meant a thin edge quietly asserted "even duel", and with the duel term centred on the champion's
/// own lane rate that asserted, for a strong champion, a PENALTY — handed out wherever OP.GG's
/// sample happened to be small.
/// </para>
/// <para>
/// Cross-validated on the stored Gold file (1.482 edges, five folds, <c>Tools -- matchupfit</c>):
/// predicting a held-out edge from the two lane win rates plus the listing offset beats a flat 50 %
/// by 0,59 % of log loss and nearly halves the squared error (Brier 0,00236 against 0,00438). A
/// flat collective mean gained 0,03 % — which is why the target is per pair, not a constant.
/// </para>
/// </summary>
public class ShrinkageTargetTests
{
    private const int Mine = 1, Weak = 2, Equal = 3;

    /// <summary>Mine is a strong laner, Weak a poor one, Equal exactly as good as Mine.</summary>
    private static MetaLookup Meta(double listingOffset = 0, int duelPlay = 30, double duelRate = 0.5) =>
        new MetaBuilder()
            .Champion(Mine, "Mine").Champion(Weak, "Weak").Champion(Equal, "Equal")
            .InLane(Mine, Lane.Top, winRate: 0.55, play: 40_000)
            .InLane(Weak, Lane.Top, winRate: 0.45, play: 40_000)
            .InLane(Equal, Lane.Top, winRate: 0.55, play: 40_000)
            .Matchup(Mine, Weak, Lane.Top, winRate: duelRate, play: duelPlay)
            .Matchup(Mine, Equal, Lane.Top, winRate: duelRate, play: duelPlay)
            .MatchupBaseline(listingOffset)
            .Build();

    /// <summary>
    /// A duel with almost no games behind it must not claim an even matchup against a champion
    /// ten points weaker. Thirty games against a prior of 150 is five sixths prior — so what the
    /// prior says is very nearly the whole answer.
    /// </summary>
    [Fact]
    public void AThinDuel_FallsBackToWhatTheTwoLaneRatesSay()
    {
        var meta = Meta();

        var againstWeak = meta.Matchup(Mine, Weak, Lane.Top)!.Value.WinRate;
        var againstEqual = meta.Matchup(Mine, Equal, Lane.Top)!.Value.WinRate;

        Assert.True(againstWeak > 0.53, $"Gegen den schwächeren Champion erwartet, gemessen {againstWeak:P2}.");
        Assert.True(againstEqual is > 0.49 and < 0.51, $"Gegen den gleich starken erwartet 50 %, gemessen {againstEqual:P2}.");
    }

    /// <summary>
    /// The shrinkage target and the duel term's baseline are the same number, so a thin edge
    /// contributes nothing at all — no edge, no penalty. This is the property the whole change is
    /// for: "we barely know" must read as "we barely know", not as an opinion.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(150)]
    public void AThinDuel_ContributesNothing(int play)
    {
        var meta = Meta(duelPlay: play, duelRate: 0.5);
        var view = meta.Matchup(Mine, Weak, Lane.Top)!.Value;

        var contribution = ScoreModel.Logit(view.WinRate) - meta.MatchupBaseline(Mine, Weak, Lane.Top);
        var scale = (double)play / (play + Shrinkage.MatchupPrior);

        // What is left is the measured part only, which is the observed rate against the same
        // reference — damped by exactly the weight the sample deserves.
        var measured = ScoreModel.Logit(0.5) - meta.MatchupBaseline(Mine, Weak, Lane.Top);
        Assert.True(
            Math.Abs(contribution) < Math.Abs(measured * scale) + 0.01,
            $"{play} Spiele dürfen höchstens {scale:P0} des Unterschieds tragen, trugen {contribution:N4}.");
    }

    /// <summary>
    /// Mirrored edges still add up to one. OP.GG stores only one direction for 1.134 of its 1.482
    /// edges, so most of the matrix IS the mirror — if shrinking each side towards its own pair
    /// prior broke the complement, half the matrix would disagree with the other half.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-0.0295)]
    public void AnEdgeAndItsMirror_StillAddUpToOne(double listingOffset)
    {
        var meta = Meta(listingOffset);

        var forward = meta.Matchup(Mine, Weak, Lane.Top)!.Value.WinRate;
        var mirror = meta.Matchup(Weak, Mine, Lane.Top)!.Value.WinRate;

        Assert.Equal(1.0, forward + mirror, precision: 10);
    }

    /// <summary>
    /// The listing offset shifts the stored direction, because that is the direction the selection
    /// applies to: OP.GG lists the opponents that stand out AGAINST a champion, so a listed edge is
    /// on average worse for it than the pair alone suggests.
    /// </summary>
    [Fact]
    public void TheListingOffset_MovesTheExpectationOfTheStoredDirection()
    {
        var neutral = Meta().MatchupBaseline(Mine, Weak, Lane.Top);
        var shifted = Meta(listingOffset: -0.0295).MatchupBaseline(Mine, Weak, Lane.Top);

        Assert.Equal(-0.0295, shifted - neutral, precision: 10);
    }

    /// <summary>
    /// Without lane rates on the contested lane the pair says nothing, and the champion's own main
    /// lane stands in. The off-lane term lives entirely on this: its edges were recorded on the
    /// OPPONENT's lane, where our candidate has no row at all.
    /// </summary>
    [Fact]
    public void WithoutARowOnThatLane_TheMainLaneStandsIn()
    {
        var meta = new MetaBuilder()
            .Champion(Mine, "Mine").Champion(Weak, "Weak")
            .InLane(Mine, Lane.Mid, winRate: 0.55, play: 40_000)
            .InLane(Weak, Lane.Top, winRate: 0.45, play: 40_000)
            .Build();

        // The lane rates themselves are shrunk, so the expectation is read back from the lookup
        // rather than from the raw fixture numbers.
        var expected = ScoreModel.Logit(meta.LaneStat(Mine, Lane.Mid)!.Value.WinRate)
            - ScoreModel.Logit(meta.LaneStat(Weak, Lane.Top)!.Value.WinRate);

        Assert.Equal(expected, meta.MatchupBaseline(Mine, Weak, Lane.Top), precision: 10);
    }

    /// <summary>A champion nothing is known about leaves only the listing offset.</summary>
    [Fact]
    public void WithoutAnyLaneRow_OnlyTheListingOffsetRemains()
    {
        var meta = Meta(listingOffset: -0.0295);

        Assert.Equal(-0.0295, meta.MatchupBaseline(Mine, opponentId: 999, lane: Lane.Top), precision: 10);
    }

    /// <summary>
    /// An impossible prior must not reach the arithmetic — Logit(0) and Logit(1) are infinite, and
    /// one infinity would ride the whole model into a NaN score.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(double.NaN)]
    public void AnImpossiblePrior_FallsBackToEven(double priorRate)
        => Assert.Equal(0.5, Shrinkage.Apply(0.5, play: 0, Shrinkage.MatchupPrior, priorRate));
}
