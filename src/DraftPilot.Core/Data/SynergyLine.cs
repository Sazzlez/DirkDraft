using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>
/// What a duo is expected to win before its own games are counted: a straight line in the two
/// champions' lane strength, fitted to the duo rows of the file.
/// <para>
/// A duo's win rate is partly the two champions being good, and only the rest is them being good
/// TOGETHER. The synergy term exists for the rest. Measured with <c>Tools -- priors</c> on the
/// stored Gold file (2.670 pairs): the duo's log-odds rise by 0,41 per unit of the own lane
/// log-odds and 0,38 per unit of the partner's — about 0,4, not 1. Centring on one global mean, as
/// the term used to, left that 0,4 in: a candidate with a listed duo had its own strength counted a
/// second time, at the term's weight, and a candidate without one did not. Centring on the full sum
/// (slope 1), the way the matchup baseline works, overshoots: it predicted held-out duo games WORSE
/// than the global mean. The fitted line predicts them best of the three.
/// </para>
/// <para>
/// Why 0,4 and not 1 is not established, and the line does not need it to be. Likely contributors:
/// the duo rows come from a different rank bracket than the lane rows (OP.GG's duo endpoint takes
/// no bracket), and both sides being on one team already shares the game between them.
/// </para>
/// </summary>
/// <param name="Intercept">Log-odds of a duo of two average champions; carries OP.GG's listing offset.</param>
/// <param name="Slope">Log-odds per unit of the SUM of both lane log-odds.</param>
/// <param name="Rows">How many pairs the line was fitted on; 0 for the flat fallback.</param>
public readonly record struct SynergyLine(double Intercept, double Slope, int Rows)
{
    /// <summary>
    /// Fewer pairs than this and the line is not fitted at all. A slope from a handful of rows is
    /// noise, and noise in a slope multiplies every candidate's lane strength.
    /// </summary>
    public const int MinimumRows = 100;

    /// <summary>The old behaviour: one global expectation for every duo.</summary>
    public static SynergyLine Flat(double baseline) => new(baseline, 0, 0);

    /// <summary>The expected duo log-odds for two champions with these lane log-odds.</summary>
    public double ExpectedLogit(double mineLogit, double partnerLogit)
        => Intercept + (Slope * (mineLogit + partnerLogit));

    /// <summary>
    /// Fits the line by games-weighted least squares, one row per pair. Falls back to
    /// <see cref="Flat"/> when there is too little to fit or nothing to fit on.
    /// </summary>
    /// <param name="laneRate">The rate a lane row reads as, or null when the champion has none there.</param>
    /// <param name="fallback">The global duo baseline, in log-odds.</param>
    public static SynergyLine Fit(IEnumerable<SynergyStat> rows, Func<int, Lane, double?> laneRate, double fallback)
    {
        var points = rows
            .Where(stat => stat.Play > 0 && double.IsFinite(stat.WinRate))
            // Both partners' synergy lists can carry the same duo; the same games counted twice
            // would pull the line twice as hard towards them.
            .GroupBy(stat => (Low: Math.Min(stat.ChampionId, stat.PartnerId), High: Math.Max(stat.ChampionId, stat.PartnerId)))
            .Select(group => group.OrderByDescending(stat => stat.Play).First())
            .Select(stat => (Stat: stat, Mine: laneRate(stat.ChampionId, stat.Lane), Theirs: laneRate(stat.PartnerId, stat.PartnerLane)))
            .Where(entry => entry.Mine is not null && entry.Theirs is not null)
            .Select(entry => (
                X: ScoreModel.Logit(entry.Mine!.Value) + ScoreModel.Logit(entry.Theirs!.Value),
                Y: ScoreModel.Logit(entry.Stat.WinRate),
                W: (double)entry.Stat.Play))
            .ToList();

        if (points.Count < MinimumRows)
            return Flat(fallback);

        var weights = points.Sum(point => point.W);
        var meanX = points.Sum(point => point.W * point.X) / weights;
        var meanY = points.Sum(point => point.W * point.Y) / weights;

        var sxx = points.Sum(point => point.W * (point.X - meanX) * (point.X - meanX));
        var sxy = points.Sum(point => point.W * (point.X - meanX) * (point.Y - meanY));

        if (sxx <= 0)
            return Flat(fallback);

        // Bounded to what the model can mean: a negative slope would reward a weak partner, and
        // one above 1 would count strength more than once even inside the duo.
        var slope = Math.Clamp(sxy / sxx, 0, 1);

        return new SynergyLine(meanY - (slope * meanX), slope, points.Count);
    }
}
