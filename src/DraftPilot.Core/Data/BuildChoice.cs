namespace DraftPilot.Core.Data;

/// <summary>
/// Which of several observed variants of the same build slot to put on the card, when the choice is
/// made on the win rate rather than on how often the variant was bought.
/// <para>
/// "Highest win rate" cannot be taken literally, and the reason is easy to measure: OP.GG's matchup
/// guide for Darius vs Jax lists a starting item with 13 games at 61,5 % next to one with 113 games
/// at 48,7 %. The raw rate says the first one; thirteen games say nothing at all. So the ranking is
/// by the lower end of the win rate's confidence interval — the Wilson score bound, which is the
/// standard answer to exactly this ranking problem. The 13-game entry scores 0,355 against the
/// 113-game entry's 0,397 and stays where it belongs, while a genuinely better record over a real
/// sample wins.
/// </para>
/// <para>
/// This is for the build that has no opponent in it — see <c>LiveDraftFetcher</c>. The matchup
/// guide keeps taking the most played variant: its samples run from four games to a few dozen, and
/// at that size no win-rate rule separates signal from noise. That is also where the situational
/// boot choice came from, which was removed on purpose.
/// </para>
/// </summary>
public static class BuildChoice
{
    /// <summary>
    /// How many standard errors the bound sits below the observed rate. 1,96 is the 95 % interval —
    /// the conventional value, and the one that puts the 13-game entry above behind the 113-game
    /// one. Lower values start letting small samples through; higher ones turn the ranking back
    /// into a pure sample-size ranking.
    /// </summary>
    private const double Z = 1.96;

    /// <summary>
    /// The variants worth showing, best first: highest lower bound on the win rate, ties broken by
    /// the larger sample. Entries without a single game are dropped — they carry no rate to rank by.
    /// </summary>
    /// <param name="take">How many to keep. The caller's slot has room for so many and no more.</param>
    public static List<ItemSet> ByWinRate(IEnumerable<ItemSet> sets, int take)
        => [.. sets
            .Where(set => set.Play > 0)
            .OrderByDescending(LowerBound)
            .ThenByDescending(set => set.Play)
            .Take(take)];

    /// <summary>
    /// The Wilson score lower bound of a variant's win rate: how well it can be shown to perform,
    /// rather than how well it happened to perform. Zero for a variant with no games — it cannot be
    /// shown to perform at all.
    /// </summary>
    public static double LowerBound(ItemSet set)
    {
        if (set.Play <= 0)
            return 0;

        // A rate outside 0..1 or not a number would come back as NaN and poison the sort order.
        var rate = double.IsNaN(set.WinRate) ? 0 : Math.Clamp(set.WinRate, 0, 1);
        double play = set.Play;

        const double zSquared = Z * Z;

        var denominator = 1 + (zSquared / play);
        var centre = rate + (zSquared / (2 * play));
        var margin = Z * Math.Sqrt((rate * (1 - rate) / play) + (zSquared / (4 * play * play)));

        return (centre - margin) / denominator;
    }
}
