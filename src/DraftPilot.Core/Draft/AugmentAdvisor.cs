using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

/// <summary>One augment, in the order OP.GG's own score puts it.</summary>
/// <param name="Performance">
/// OP.GG's score, passed through untouched. Deliberately not converted into anything — see
/// <see cref="AugmentAdvisor"/> for the two conversions that were tried and measured wrong.
/// </param>
/// <param name="PickRate">OP.GG's popularity figure, likewise untouched.</param>
public readonly record struct RankedAugment(
    int Id,
    string Name,
    int Tier,
    double Performance,
    double PickRate);

/// <summary>
/// Picks out the augments worth naming for one champion, and orders them by OP.GG's own score.
/// <para>
/// This does less arithmetic than anything else in this project, and the restraint is the result of
/// measurement rather than caution. Two readings of the source were tried and both were wrong.
/// </para>
/// <para>
/// <b>Not a win rate.</b> Rows with no recorded picks carry a <c>performance</c> that is always 170
/// times an exact small fraction — 170, 141.67, 127.5, 113.33, 85, 56.67, 48.57, 42.5, 0, that is
/// 170 times 1/1, 5/6, 3/4, 2/3, 1/2, 1/3, 2/7, 1/4, 0 — which says the figure is a ratio on a
/// 170-point scale. Tested by weighting each augment's implied rate by its popularity, which has to
/// reproduce the champion's own win rate: Seraphine matched (0.506 against 0.51) and the other six
/// champions came out low by 2.5 to 9.2 points (Garen 0.428 against 0.52). One match in seven is a
/// coincidence, so the figure is shown as OP.GG's, not as a win rate.
/// </para>
/// <para>
/// <b>No sample size either.</b> If <c>popular</c> were the share of the champion's games its values
/// would sum to the augments a game hands out, at most six. Seraphine's sum is 4.05, which is what
/// made the idea look right; Lee Sin's is 9.09, which makes it impossible. So there is no
/// denominator, nothing to shrink, and no error bar to draw.
/// </para>
/// <para>
/// <b>What is left is two filters, and both are read off the data.</b> Without them the list is
/// worse than useless: ordering Darius's augments on the raw figure puts four rows with a
/// popularity of 0.01 to 0.04 on top — the ones almost nobody takes. That is the boots mistake
/// arriving through a different door.
/// </para>
/// </summary>
public static class AugmentAdvisor
{
    /// <summary>The value <c>performance</c> takes at the top of its scale, whatever it measures.</summary>
    public const double FullScale = 170.0;

    /// <summary>
    /// Above how few observations a value stops betraying itself.
    /// <para>
    /// A score built on <c>n</c> observations can only land on a multiple of 170/n, and at two
    /// decimals that fingerprint is still readable up to about here. Measured on Darius: the rows
    /// whose figure IS reproducible that way average a popularity of 0.001, the rest average 0.127
    /// — the fingerprint and the popularity agree on which rows are thin, which is what makes this
    /// a measurement rather than a threshold.
    /// </para>
    /// <para>
    /// It stops at 40 because of what happens above: the count of distinct fractions with
    /// denominator up to n grows as roughly 3n²/π², so at 40 about 3 % of arbitrary values are hit
    /// by coincidence, at 100 about 18 %, and at 150 about 40 %. Past this point the filter would
    /// throw away more good rows than thin ones.
    /// </para>
    /// </summary>
    public const int CoarseSampleLimit = 40;

    /// <summary>
    /// Ranks what is worth naming, best first by OP.GG's score.
    /// </summary>
    /// <param name="options">Everything the source returned for the champion.</param>
    /// <param name="take">How many rows to keep.</param>
    public static IReadOnlyList<RankedAugment> Rank(IEnumerable<AugmentOption> options, int take)
    {
        if (take <= 0)
            return [];

        var rows = new List<RankedAugment>();

        foreach (var option in options)
        {
            if (!double.IsFinite(option.PickRate) || option.PickRate <= 0)
                continue;

            if (!double.IsFinite(option.Performance) || option.Performance <= 0)
                continue;

            if (string.IsNullOrWhiteSpace(option.Name))
                continue;

            // First filter: the figure gives its own sample away.
            if (IsCoarse(option.Performance))
                continue;

            rows.Add(new RankedAugment(
                option.Id,
                option.Name,
                option.Tier,
                option.Performance,
                option.PickRate));
        }

        // Second filter: the champion's own distribution sets the bar, so no constant has to be
        // invented for it. "More often taken than half of this champion's augments" is a statement
        // about this champion; any fixed number would be a statement about nothing.
        var floor = MedianPickRate(rows);

        return [.. rows
            .Where(row => row.PickRate >= floor)
            .OrderByDescending(row => row.Performance)
            .ThenByDescending(row => row.PickRate)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .Take(take)];
    }

    /// <summary>
    /// Whether the figure can be reproduced exactly by a ratio over at most
    /// <see cref="CoarseSampleLimit"/> observations, which is what a handful of games looks like
    /// once it is rounded to two decimals.
    /// </summary>
    public static bool IsCoarse(double performance)
    {
        if (!double.IsFinite(performance))
            return true;

        for (var n = 1; n <= CoarseSampleLimit; n++)
        {
            // Only one numerator can possibly land on the reported value for this denominator, so
            // it is computed rather than searched.
            var wins = (int)Math.Round(performance * n / FullScale, MidpointRounding.AwayFromZero);

            if (wins < 0 || wins > n)
                continue;

            // The reported value is the two-decimal rounding of the real one, so anything inside
            // half a step is a match.
            if (Math.Abs((FullScale * wins / n) - performance) < 0.005)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The middle popularity of the rows that survived the first filter. Upper median, so an even
    /// count keeps the smaller half out rather than in.
    /// </summary>
    private static double MedianPickRate(List<RankedAugment> rows)
    {
        if (rows.Count == 0)
            return 0;

        var sorted = rows.Select(row => row.PickRate).Order().ToList();

        return sorted[sorted.Count / 2];
    }
}
