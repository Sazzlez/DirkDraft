using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The augment list is the one place in this tool where a number arrives with no sample size and no
/// documented scale, and the temptation to decode it anyway is strong enough that it was acted on
/// once already. These tests pin down the one thing that IS known — that the rows with no recorded
/// picks are noise — and they pin down the restraint: nothing here is converted into a win rate.
/// </summary>
public class AugmentAdvisorTests
{
    /// <summary>
    /// Real rows for Seraphine, captured on 2026-09-19. The last two are the trap: a flawless
    /// <c>performance</c> of 170 and a near-flawless 141.67, both on augments with no recorded
    /// picks — and both of them 170 times an exact small fraction (1/1 and 5/6).
    /// </summary>
    private static List<AugmentOption> Seraphine() =>
    [
        new() { Id = 1004, Name = "Zurück zu den Wurzeln", Tier = 3, Performance = 91.32, PickRate = 0.20 },
        new() { Id = 1335, Name = "Goldschürfer", Tier = 3, Performance = 89.08, PickRate = 0.19 },
        new() { Id = 1325, Name = "Glaskanone", Tier = 4, Performance = 81.08, PickRate = 0.18 },
        new() { Id = 1061, Name = "O.K.-Bumerang", Tier = 3, Performance = 92.18, PickRate = 0.17 },
        new() { Id = 2016, Name = "Aufgetankt", Tier = 4, Performance = 86.33, PickRate = 0.16 },
        new() { Id = 1116, Name = "Blitzartig", Tier = 4, Performance = 89.04, PickRate = 0.15 },
        new() { Id = 1006, Name = "Klingenwalzer", Tier = 5, Performance = 170.00, PickRate = 0.00 },
        new() { Id = 1347, Name = "Poltergeist", Tier = 5, Performance = 141.67, PickRate = 0.00 },
    ];

    private static IReadOnlyList<RankedAugment> Ranked(int take = 10)
        => AugmentAdvisor.Rank(Seraphine(), take);

    /// <summary>
    /// The whole reason this class exists. Ordering Seraphine's augments on the raw figure puts
    /// "Klingenwalzer" on top at a flawless 170, on a sample the quantisation shows to be a single
    /// observation. It must not appear at all — a name at the top of a list is read as advice
    /// whatever caveat sits beside it.
    /// </summary>
    [Fact]
    public void APerfectScoreWithNoRecordedPicksIsNotShown()
    {
        var ranked = Ranked();

        Assert.DoesNotContain(ranked, row => row.Name == "Klingenwalzer");
        Assert.DoesNotContain(ranked, row => row.Name == "Poltergeist");
        Assert.All(ranked, row => Assert.True(row.PickRate > 0));
    }

    /// <summary>
    /// With those gone, the order is OP.GG's own score, highest first — among the rows that clear
    /// the champion's median popularity. O.K.-Bumerang scores higher but sits at 0.17 against a
    /// median of 0.18, so it does not lead: that is the second filter doing its job, not an
    /// ordering mistake.
    /// </summary>
    [Fact]
    public void TheHighestScoringPopularRowLeads()
    {
        var ranked = Ranked();

        Assert.Equal("Zurück zu den Wurzeln", ranked[0].Name);
        Assert.Equal(91.32, ranked[0].Performance, 2);
        Assert.All(ranked, row => Assert.True(row.PickRate >= 0.18));
    }

    [Fact]
    public void TheOrderIsDescendingByScore()
    {
        var scores = Ranked().Select(row => row.Performance).ToList();

        Assert.Equal(scores.OrderByDescending(score => score), scores);
    }

    /// <summary>
    /// A figure that is exactly 170 times a small fraction gives its own sample away: 127.5 is
    /// three wins in four, 48.57 is two in seven. Those rows are dropped however popular they
    /// claim to be, because the figure itself says there is nothing behind it.
    /// </summary>
    [Theory]
    [InlineData(170.0)]      // 1/1
    [InlineData(141.67)]     // 5/6
    [InlineData(127.5)]      // 3/4
    [InlineData(113.33)]     // 2/3
    [InlineData(85.0)]       // 1/2
    [InlineData(56.67)]      // 1/3
    [InlineData(48.57)]      // 2/7
    [InlineData(42.5)]       // 1/4
    public void ACoarseFigureIsRecognised(double performance)
        => Assert.True(AugmentAdvisor.IsCoarse(performance));

    /// <summary>
    /// And the figures from rows with real usage are not: no ratio over forty or fewer observations
    /// lands on them. These are the values measured on Seraphine's most-played augments.
    /// </summary>
    [Theory]
    [InlineData(91.32)]
    [InlineData(89.08)]
    [InlineData(92.18)]
    [InlineData(81.08)]
    public void AFineGrainedFigureIsNot(double performance)
        => Assert.False(AugmentAdvisor.IsCoarse(performance));

    /// <summary>
    /// The second filter, and the one that fixes what the first cannot. Measured on Darius: after
    /// the coarse rows are gone, the raw order still leads with four augments at a popularity of
    /// 0.01 to 0.04 — the ones almost nobody takes. The champion's own median popularity is what
    /// removes them, so no constant has to be invented for it.
    /// </summary>
    [Fact]
    public void RarelyTakenAugmentsDoNotLeadTheList()
    {
        var ranked = AugmentAdvisor.Rank(
            [
                // High score, almost never taken. Not reproducible by a coarse fraction, so only
                // the popularity filter can catch it.
                new() { Id = 1, Name = "Randerscheinung", Tier = 5, Performance = 101.17, PickRate = 0.01 },
                new() { Id = 2, Name = "Auch selten", Tier = 5, Performance = 99.31, PickRate = 0.02 },
                new() { Id = 3, Name = "Üblich", Tier = 3, Performance = 84.38, PickRate = 0.22 },
                new() { Id = 4, Name = "Sehr üblich", Tier = 3, Performance = 83.15, PickRate = 0.23 },
            ],
            10);

        Assert.Equal("Üblich", ranked[0].Name);
        Assert.DoesNotContain(ranked, row => row.Name == "Randerscheinung");
        Assert.All(ranked, row => Assert.True(row.PickRate >= 0.22));
    }

    /// <summary>
    /// Both of OP.GG's figures reach the caller unchanged. This is the test that fails first if
    /// anybody re-adds a conversion: the scale looks like a win rate times 170 and, measured across
    /// seven champions, is not one — Garen's popularity-weighted rate comes out at 0.428 against a
    /// real 0.52. Whatever these numbers are, they are passed through.
    /// </summary>
    [Fact]
    public void BothFiguresArePassedThroughUntouched()
    {
        var row = Ranked().Single(entry => entry.Id == 1004);

        Assert.Equal(91.32, row.Performance, 4);
        Assert.Equal(0.20, row.PickRate, 4);
        Assert.Equal(3, row.Tier);
    }

    /// <summary>
    /// Popularity breaks a tie in the score, because popularity is about this champion while the
    /// tier is OP.GG's verdict on the augment in general.
    /// <para>
    /// The figure has to be one the coarse filter lets through, and a round 90.00 is not: it is
    /// exactly 170 times 9/17. Writing this test with it was the quickest possible demonstration
    /// that the filter catches values nobody would suspect by eye.
    /// </para>
    /// </summary>
    [Fact]
    public void EqualScoresAreSeparatedByPopularity()
    {
        var ranked = AugmentAdvisor.Rank(
            [
                new() { Id = 1, Name = "Etwas seltener", Tier = 5, Performance = 91.32, PickRate = 0.20 },
                new() { Id = 2, Name = "Häufig", Tier = 3, Performance = 91.32, PickRate = 0.21 },
            ],
            10);

        Assert.Equal("Häufig", ranked[0].Name);
    }

    /// <summary>A round number is no safer than any other: 90.00 is 170 times 9/17.</summary>
    [Fact]
    public void ARoundFigureCanStillBeCoarse()
        => Assert.True(AugmentAdvisor.IsCoarse(90.0));

    [Fact]
    public void TakeIsRespected()
        => Assert.Equal(3, Ranked(take: 3).Count);

    [Fact]
    public void TakingNothingReturnsNothing()
        => Assert.Empty(Ranked(take: 0));

    [Fact]
    public void NothingInNothingOut()
        => Assert.Empty(AugmentAdvisor.Rank([], 10));

    /// <summary>
    /// A corrupt or hostile answer must produce no row rather than an exception or a row with a
    /// number nobody can read.
    /// </summary>
    [Fact]
    public void UnreadableRowsAreDropped()
    {
        var ranked = AugmentAdvisor.Rank(
            [
                new() { Id = 1, Name = "NaN-Wert", Tier = 3, Performance = double.NaN, PickRate = 0.2 },
                new() { Id = 2, Name = "Unendlich", Tier = 3, Performance = double.PositiveInfinity, PickRate = 0.2 },
                new() { Id = 3, Name = "Negative Beliebtheit", Tier = 3, Performance = 90, PickRate = -1 },
                new() { Id = 4, Name = "NaN-Beliebtheit", Tier = 3, Performance = 90, PickRate = double.NaN },
                new() { Id = 5, Name = "Wert null", Tier = 3, Performance = 0, PickRate = 0.2 },
                new() { Id = 6, Name = "", Tier = 3, Performance = 90, PickRate = 0.2 },
                new() { Id = 7, Name = "   ", Tier = 3, Performance = 90, PickRate = 0.2 },
            ],
            10);

        Assert.Empty(ranked);
    }

    /// <summary>
    /// One good row among broken ones still comes through: the filter drops rows, not the answer.
    /// </summary>
    [Fact]
    public void ABrokenNeighbourDoesNotSuppressAGoodRow()
    {
        var ranked = AugmentAdvisor.Rank(
            [
                new() { Id = 1, Name = "Kaputt", Tier = 3, Performance = double.NaN, PickRate = 0.2 },
                new() { Id = 2, Name = "Heil", Tier = 3, Performance = 88.0, PickRate = 0.2 },
            ],
            10);

        Assert.Single(ranked);
        Assert.Equal("Heil", ranked[0].Name);
    }
}
