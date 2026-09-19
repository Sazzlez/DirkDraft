using DraftPilot.Core.Data;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The rule that decides which variant of a build slot goes on the card when the choice is made on
/// the record rather than on popularity. Every number below is real: the thin ones come from the
/// captured Darius-vs-Jax matchup guide, the thick ones from the captured Darius Top champion
/// analysis.
/// </summary>
public class BuildChoiceTests
{
    private static ItemSet Set(string name, int play, int wins) => new()
    {
        Items = [name],
        Play = play,
        WinRate = play > 0 ? (double)wins / play : 0,
    };

    /// <summary>
    /// The case the whole rule exists for. Taken at face value the 13-game entry has the better
    /// win rate by 13 points; thirteen games cannot carry that claim, and the ranking must not
    /// let them.
    /// </summary>
    [Fact]
    public void AThinSample_CannotOutrankAThickOne_OnItsRawRate()
    {
        var ranked = BuildChoice.ByWinRate(
            [Set("Doran's Helm", play: 13, wins: 8), Set("Doran's Blade", play: 113, wins: 55)],
            take: 2);

        Assert.Equal("Doran's Blade", ranked[0].Items[0]);
        Assert.Equal("Doran's Helm", ranked[1].Items[0]);
    }

    /// <summary>
    /// The other half of the same rule: where both samples are real, the better record wins even
    /// though the other variant was bought slightly more often. Numbers from the champion
    /// analysis' last_items, where every entry runs into the hundred thousands.
    /// </summary>
    [Fact]
    public void WithRealSamplesOnBothSides_TheBetterRecordWins()
    {
        var ranked = BuildChoice.ByWinRate(
            [
                Set("Youmuu's Ghostblade", play: 250_507, wins: 125_757),
                Set("Dead Man's Plate", play: 251_388, wins: 134_109),
            ],
            take: 2);

        Assert.Equal("Dead Man's Plate", ranked[0].Items[0]);
    }

    /// <summary>
    /// A variant nobody played carries no rate to rank by, and a card slot filled from it would
    /// show an item with "0 Games" next to a percentage invented by the division.
    /// </summary>
    [Fact]
    public void VariantsWithoutGames_AreDropped()
    {
        var ranked = BuildChoice.ByWinRate(
            [Set("Never bought", play: 0, wins: 0), Set("Bought", play: 40, wins: 21)],
            take: 5);

        Assert.Single(ranked);
        Assert.Equal("Bought", ranked[0].Items[0]);
    }

    /// <summary>Same record, more games: the better-established one goes first.</summary>
    [Fact]
    public void OnAnEqualRecord_TheLargerSampleLeads()
    {
        var ranked = BuildChoice.ByWinRate(
            [Set("Rare", play: 100, wins: 55), Set("Common", play: 1_000, wins: 550)],
            take: 2);

        Assert.Equal("Common", ranked[0].Items[0]);
    }

    [Fact]
    public void TheSlotKeepsOnlyAsManyEntriesAsItHasRoomFor()
    {
        var ranked = BuildChoice.ByWinRate(
            [Set("a", 500, 260), Set("b", 500, 255), Set("c", 500, 250), Set("d", 500, 245)],
            take: 3);

        Assert.Equal(3, ranked.Count);
        Assert.Equal(["a", "b", "c"], ranked.Select(set => set.Items[0]));
    }

    /// <summary>
    /// The bound is a lower bound: it always sits below the observed rate, and it closes in on it
    /// as the sample grows. Without that second property the rule would be a sample-size ranking
    /// wearing a win rate's clothes.
    /// </summary>
    [Fact]
    public void TheBoundSitsBelowTheRateAndTightensWithTheSample()
    {
        var thin = BuildChoice.LowerBound(Set("thin", play: 20, wins: 12));
        var thick = BuildChoice.LowerBound(Set("thick", play: 20_000, wins: 12_000));

        Assert.True(thin < 0.6, $"Untergrenze {thin:F3} muss unter der beobachteten Rate 0,600 liegen.");
        Assert.True(thick < 0.6, $"Untergrenze {thick:F3} muss unter der beobachteten Rate 0,600 liegen.");
        Assert.True(thick > thin, $"Mehr Games muss enger werden: {thick:F3} gegen {thin:F3}.");
        Assert.True(0.6 - thick < 0.01, $"20.000 Games sollten die Untergrenze fast auf die Rate ziehen, sind aber {0.6 - thick:F3} entfernt.");
    }

    /// <summary>A rate that is not a number must not sort anything; NaN poisons a comparison sort.</summary>
    [Fact]
    public void ARateThatIsNotANumber_DoesNotBreakTheOrder()
    {
        var broken = Set("kaputt", play: 50, wins: 0);
        broken.WinRate = double.NaN;

        var ranked = BuildChoice.ByWinRate([broken, Set("heil", play: 50, wins: 30)], take: 2);

        Assert.Equal("heil", ranked[0].Items[0]);
    }
}
