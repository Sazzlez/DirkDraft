namespace DraftPilot.Core.Draft;

/// <summary>How the score compares to the field, once its own sampling error is accounted for.</summary>
public enum ScoreStanding
{
    /// <summary>The lead over the next candidate is smaller than the noise in the numbers.</summary>
    Tied,

    /// <summary>Ahead by more than the noise, but not by much more.</summary>
    Ahead,

    /// <summary>Ahead by a margin no plausible resample would overturn.</summary>
    Clear,
}

/// <summary>
/// The sampling error of a score, and what may honestly be claimed with it.
/// <para>
/// Every term in a score is a win rate measured over a finite number of games, so the score is an
/// estimate with an error bar. Ignoring that produced the panel's worst statement: a list ordered to
/// the tenth of a percentage point when the top four sat inside the noise of a single 111-game duo
/// statistic, and a "starke Wahl" badge handed out on a fixed 53 % threshold no matter how thin the
/// evidence behind it was.
/// </para>
/// <para>
/// The error is propagated analytically rather than by resampling: every term is a logit of a
/// shrunk proportion, which has a closed-form variance, and the terms are independent enough for
/// the sum to be honest. <c>Tools -- noise</c> checks that closed form against an actual bootstrap
/// on the stored snapshot — that is what makes this arithmetic rather than a guess.
/// </para>
/// </summary>
public static class ScoreError
{
    /// <summary>
    /// How many standard errors of the difference two candidates must be apart before their order
    /// means anything. One is the honest minimum: below it, resampling the same data would swap
    /// them roughly a third of the time.
    /// </summary>
    public const double TieSigma = 1.0;

    /// <summary>Beyond this many standard errors from even, the edge is worth stating plainly.</summary>
    public const double ClearSigma = 3.0;

    /// <summary>
    /// Variance of <c>Logit(Shrinkage.Apply(rate, play, prior))</c> in log-odds, given that the
    /// underlying proportion was measured over <paramref name="play"/> games.
    /// <para>
    /// <paramref name="shrunkRate"/> is the rate as it comes out of <see cref="Data.MetaLookup"/> —
    /// already shrunk. The raw proportion is recovered exactly from the shrinkage factor, because
    /// the binomial variance belongs to the raw measurement, not to the shrunk value.
    /// </para>
    /// </summary>
    public static double LogitVariance(double shrunkRate, int play, int prior)
    {
        // No sample means shrinkage pinned the rate to 0.5 and the term carries no information at
        // all: no contribution, and no division by zero.
        if (play <= 0)
            return 0;

        var factor = (double)play / (play + prior);
        var raw = Math.Clamp(0.5 + ((shrunkRate - 0.5) / factor), 0, 1);
        var shrunk = Math.Clamp(shrunkRate, 0.01, 0.99);

        // d/dp Logit(0.5 + f(p - 0.5)) = f / (q(1-q)) with q the shrunk rate.
        var slope = factor / (shrunk * (1 - shrunk));

        return slope * slope * raw * (1 - raw) / play;
    }

    /// <summary>Standard error in percentage points of win rate, for display next to the score.</summary>
    public static double AsPoints(double standardError) => ScoreModel.AsPoints(standardError);

    /// <summary>
    /// How many decimals the score may honestly be printed with. A tenth of a point is meaningless
    /// next to an error bar of one and a half, and printing it anyway is the reason the list looked
    /// more decided than it was.
    /// </summary>
    public static int Decimals(double standardError)
        => AsPoints(standardError) >= 0.5 ? 0 : 1;

    /// <summary>
    /// One precision for a whole list. Deciding it per row is more literal but reads as a typo —
    /// "57 %" directly beside "54,3 %" in the same column. The median decides, so a single very
    /// uncertain entry at the bottom does not coarsen every row above it.
    /// </summary>
    public static int Decimals(IReadOnlyList<Recommendation> items)
    {
        if (items.Count == 0)
            return 1;

        var errors = items.Select(item => item.Uncertainty).Order().ToList();

        return Decimals(errors[errors.Count / 2]);
    }

    /// <summary>
    /// Where a candidate stands against the next one down, measured in standard errors of the
    /// difference. The two errors are treated as independent: the candidates share the opponents,
    /// but each is measured against them by its own separate statistic.
    /// </summary>
    public static ScoreStanding Standing(double score, double error, double nextScore, double nextError)
    {
        var gap = ScoreModel.Logit(score) - ScoreModel.Logit(nextScore);
        var spread = Math.Sqrt((error * error) + (nextError * nextError));

        // Without an error estimate there is nothing to compare against; fall back to saying the
        // least, which is what the old fixed thresholds should have done.
        if (spread <= 0)
            return gap > 0 ? ScoreStanding.Ahead : ScoreStanding.Tied;

        var sigmas = gap / spread;

        return sigmas switch
        {
            >= ClearSigma => ScoreStanding.Clear,
            >= TieSigma => ScoreStanding.Ahead,
            _ => ScoreStanding.Tied,
        };
    }

    /// <summary>
    /// How many entries at the top of the list are indistinguishable from the leader. Replaces a
    /// fixed 0.3-point threshold that had no relation to the data behind the numbers: on a thin
    /// synergy it marked nothing when everything was tied, and on 40.000-game lane data it would
    /// have marked ties that were real differences.
    /// </summary>
    public static int CountLeadingTies(IReadOnlyList<Recommendation> items)
    {
        if (items.Count < 2)
            return 0;

        var leader = items[0];
        var tied = 1;

        while (tied < items.Count
            && Standing(leader.Score, leader.Uncertainty, items[tied].Score, items[tied].Uncertainty) == ScoreStanding.Tied)
        {
            tied++;
        }

        return tied;
    }
}

/// <summary>
/// Collects the sampling variance of the terms making up one candidate's score. Each term adds the
/// variance it actually carries, at the place where its sample sizes are still in hand — deriving
/// it afterwards would mean a second copy of every weighting rule, and the two would drift.
/// </summary>
public sealed class ErrorBudget
{
    private double _variance;

    /// <summary>Standard error of the summed score, in log-odds.</summary>
    public double StandardError => Math.Sqrt(_variance);

    /// <summary>Adds one term's variance. Terms are summed, so their variances are too.</summary>
    public void Add(double variance)
    {
        if (double.IsFinite(variance) && variance > 0)
            _variance += variance;
    }

    public void Reset() => _variance = 0;
}
