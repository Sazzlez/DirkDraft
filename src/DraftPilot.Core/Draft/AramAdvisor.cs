using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

/// <summary>What OP.GG knows about one champion in ARAM: a win rate and the games behind it.</summary>
/// <param name="WinRate">
/// As reported, which means rounded to two decimals — see <see cref="AramAdvisor.RoundingVariance"/>.
/// </param>
public readonly record struct AramStat(int ChampionId, double WinRate, int Play);

/// <summary>
/// The one decision an ARAM player actually makes: keep the champion you were handed, or take one
/// off the bench.
/// <para>
/// Until this existed the panel had nothing to say on the Howling Abyss. It showed the build and
/// the two compositions and left the swap — the only thing the player can still change — entirely
/// unanswered. There is no pick order to model and no lane to predict: every candidate is simply a
/// champion with an ARAM win rate, and the question is which of them is best.
/// </para>
/// <para>
/// The numbers are coarser than anywhere else in this tool and the ranking says so. OP.GG reports
/// ARAM only through <c>summary.average_stats</c>, whose win rate is rounded to two decimals, and
/// its <c>positions</c> block — the one that carries exact win counts on Summoner's Rift — comes
/// back null for ARAM (measured 2026-09-19). One percentage point of resolution is enough to
/// separate what ARAM actually separates: the mode's win rates run from roughly 45 % to 56 %. It is
/// not enough for fine distinctions, and the error bar is what keeps those honestly tied.
/// </para>
/// </summary>
public static class AramAdvisor
{
    /// <summary>
    /// Prior weight for an ARAM win rate. Samples run into the thousands per champion, so this
    /// barely moves a listed champion; it exists so a champion with a handful of games cannot lead
    /// the bench on noise.
    /// </summary>
    public const int Prior = 400;

    /// <summary>
    /// The variance the two-decimal rounding of the reported win rate adds, in rate space.
    /// <para>
    /// A value rounded to a step of 0.01 carries a uniform error over that step, whose variance is
    /// step² / 12. Small — but at 35.000 games the sampling error is smaller still, and leaving
    /// this out would let the bar claim a precision the source does not have. It is the difference
    /// between "these two are tied" and a confident order over a coin flip.
    /// </para>
    /// </summary>
    public static double RoundingVariance => 0.01 * 0.01 / 12.0;

    /// <summary>
    /// Ranks the player's own champion together with the bench, best first.
    /// </summary>
    /// <param name="ownChampion">The champion currently held, or 0 when none is assigned yet.</param>
    /// <param name="bench">Bench champion ids, in the client's order.</param>
    /// <param name="stats">What is known so far, keyed by champion id. Missing entries are skipped.</param>
    /// <param name="names">Champion names for the rows.</param>
    /// <returns>
    /// The ranking, or an empty list when not even the player's own champion has a number — a list
    /// that cannot include what you already have is not a comparison.
    /// </returns>
    public static IReadOnlyList<Recommendation> Rank(
        int ownChampion,
        IReadOnlyList<int> bench,
        IReadOnlyDictionary<int, AramStat> stats,
        Func<int, string> names)
    {
        var candidates = new List<int>(bench.Count + 1);

        if (ownChampion != 0)
            candidates.Add(ownChampion);

        foreach (var champion in bench)
        {
            if (champion != ownChampion && !candidates.Contains(champion))
                candidates.Add(champion);
        }

        var rows = new List<Recommendation>(candidates.Count);

        foreach (var champion in candidates)
        {
            if (!stats.TryGetValue(champion, out var stat) || stat.Play <= 0)
                continue;

            var isOwn = champion == ownChampion;
            var shrunk = Shrinkage.Apply(stat.WinRate, stat.Play, Prior);

            // Two sources of error, both real: the sample, and the rounding of the reported rate.
            var variance = ScoreError.LogitVariance(stat.WinRate, stat.Play, Prior, 0.5)
                + LogitRoundingVariance(shrunk);

            rows.Add(new Recommendation(
                champion,
                names(champion),
                shrunk,
                Reasons(stat, shrunk, isOwn),
                [new ScoreTerm(ScoreTermKind.LaneStrength, ScoreModel.Logit(shrunk))],
                Math.Sqrt(variance)));
        }

        // Nothing to compare against if the champion in hand has no number: a bench ranked without
        // it answers "which of these is best" when the question is "is any of them better".
        if (ownChampion != 0 && rows.All(row => row.ChampionId != ownChampion))
            return [];

        return [.. rows
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The rounding's variance carried into log-odds, where the score lives. The derivative of the
    /// logit at <paramref name="rate"/> is 1/(p(1−p)), so the variance scales by its square.
    /// </summary>
    private static double LogitRoundingVariance(double rate)
    {
        var clamped = Math.Clamp(rate, 0.01, 0.99);
        var slope = 1 / (clamped * (1 - clamped));

        return slope * slope * RoundingVariance;
    }

    private static List<Reason> Reasons(AramStat stat, double shrunk, bool isOwn)
    {
        var reasons = new List<Reason>(2);

        if (isOwn)
        {
            reasons.Add(Reason.Neutral(
                "dein Champ",
                "Der Champion, den du gerade hast. Er steht in derselben Liste wie die Bank, damit "
                + "sichtbar ist, ob ein Tausch überhaupt etwas bringt."));
        }

        reasons.Add(Reason.Neutral(
            $"{stat.WinRate:P0} WR in ARAM aus {stat.Play:N0} Games",
            $"OP.GGs ARAM-Zahl für diesen Champion. Sie kommt auf zwei Nachkommastellen gerundet — "
            + "ein Prozentpunkt Auflösung — und die Rundung steckt im Fehlerbalken mit drin. "
            + $"Nach Glättung über die Stichprobe: {shrunk:P1}."));

        return reasons;
    }
}
