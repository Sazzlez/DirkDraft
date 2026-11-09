namespace DraftPilot.Core.Data;

/// <summary>
/// How much of a pair's own count to trust — a duel such as Kayle against Garen on top. Two ways to
/// pull a pair's rate towards what both champions' strength expects, and the split-half test both
/// are measured with (Tools -- priors, docs/modellpruefung.md).
/// <para>
/// <see cref="Linear"/> is what <see cref="Shrinkage"/> does: every pair gets the same κ pseudo
/// games of the expectation. Right for the typical pair — and the typical pair has no counter — so a
/// rare real hard counter is pulled in just as hard as noise.
/// </para>
/// <para>
/// <see cref="Mixture"/> assumes two kinds of pair: most move nothing beyond both strengths, a share
/// π does, spread like a beta with κ₁ pseudo games around the expectation. How far a pair's count
/// lies from the expectation, measured against what chance gives at its number of games, decides how
/// likely it is one of the few — so a small deviation is pulled in hard and a large, well-counted one
/// stands. Whether that predicts better than the linear pull is for the split-half test to say.
/// </para>
/// </summary>
public static class PairEvidence
{
    /// <summary>One counted pair: wins over games, and the rate both champions' strength expects.</summary>
    public sealed record Row(int Wins, int Games, double Expected);

    /// <summary>The linear pull: (wins + κ·expected) / (games + κ); the expectation alone for κ = ∞ (int.MaxValue).</summary>
    public static double Linear(int wins, int games, double expected, double kappa)
        => kappa >= int.MaxValue || games + kappa <= 0 ? expected : (wins + (kappa * expected)) / (games + kappa);

    /// <summary>
    /// The two-kind pull: the chance that the pair is one of the share <paramref name="share"/> that
    /// move something, from how its count fits a beta-binomial (κ₁ = <paramref name="kappa"/>) against
    /// the plain binomial at the expectation; then that chance of the linear estimate and the rest of
    /// the expectation.
    /// </summary>
    public static double Mixture(int wins, int games, double expected, double share, double kappa)
    {
        if (games <= 0 || share <= 0)
            return expected;
        var linear = Linear(wins, games, expected, kappa);
        if (share >= 1)
            return linear;

        var alpha = kappa * expected;
        var beta = kappa * (1 - expected);
        var losses = games - wins;
        // The binomial coefficients cancel between the two likelihoods.
        var logRatio = LogBeta(wins + alpha, losses + beta) - LogBeta(alpha, beta)
            - (wins * Math.Log(expected)) - (losses * Math.Log(1 - expected));
        var logOdds = Math.Log(share / (1 - share)) + logRatio;
        var counter = logOdds > 40 ? 1 : logOdds < -40 ? 0 : 1 / (1 + Math.Exp(-logOdds));
        return (counter * linear) + ((1 - counter) * expected);
    }

    /// <summary>
    /// Mean held-out log loss per game of every estimator, averaged over random splits: each row's
    /// games fall into a training and a test half at random (a binomial split of its wins and of its
    /// losses), the estimator reads the training half, the test half scores it. Every estimator sees
    /// the very same splits.
    /// </summary>
    public static double[] HeldOutLoss(IReadOnlyList<Row> rows, int repetitions, IReadOnlyList<Func<int, int, double, double>> estimators, int seed = 20261008)
    {
        var totals = new double[estimators.Count];
        long testGames = 0;
        var random = new Random(seed);

        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            foreach (var row in rows)
            {
                var trainWins = Binomial(random, row.Wins);
                var trainLosses = Binomial(random, row.Games - row.Wins);
                var testWins = row.Wins - trainWins;
                var testLosses = row.Games - row.Wins - trainLosses;
                if (testWins + testLosses == 0)
                    continue;

                testGames += testWins + testLosses;
                for (var index = 0; index < estimators.Count; index++)
                {
                    // The same clamp the score applies to every rate: an all-win half never costs infinity.
                    var estimate = Math.Clamp(estimators[index](trainWins, trainWins + trainLosses, row.Expected), 0.01, 0.99);
                    totals[index] -= (testWins * Math.Log(estimate)) + (testLosses * Math.Log(1 - estimate));
                }
            }
        }

        return [.. totals.Select(total => testGames > 0 ? total / testGames : 0)];
    }

    /// <summary>
    /// What a large deviation is worth: over random splits, the pairs whose training half has at least
    /// <paramref name="minimumGames"/> games and lies at least <paramref name="minimumDeviation"/> from
    /// the expectation. <see cref="Confirmation.Retained"/> is the share of that deviation the test half
    /// shows again (in the same direction), <see cref="Confirmation.RetainedToday"/> the share a linear
    /// pull with <paramref name="kappaToday"/> keeps of it.
    /// </summary>
    public static Confirmation Confirm(IReadOnlyList<Row> rows, int repetitions, int minimumGames, double minimumDeviation, double kappaToday, int seed = 20261008)
    {
        var random = new Random(seed);
        double held = 0, total = 0, today = 0, trainGamesSum = 0;
        var cases = 0;

        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            foreach (var row in rows)
            {
                var trainWins = Binomial(random, row.Wins);
                var trainLosses = Binomial(random, row.Games - row.Wins);
                var trainGames = trainWins + trainLosses;
                var testGames = row.Games - trainGames;
                if (trainGames < minimumGames || testGames <= 0)
                    continue;

                var deviation = ((double)trainWins / trainGames) - row.Expected;
                if (Math.Abs(deviation) < minimumDeviation)
                    continue;

                var again = ((double)(row.Wins - trainWins) / testGames) - row.Expected;
                held += testGames * Math.Sign(deviation) * again;
                total += testGames * Math.Abs(deviation);
                today += testGames * Math.Abs(deviation) * trainGames / (trainGames + kappaToday);
                trainGamesSum += trainGames;
                cases++;
            }
        }

        return cases == 0
            ? new Confirmation(0, 0, 0, 0)
            : new Confirmation((double)cases / repetitions, held / total, today / total, trainGamesSum / cases);
    }

    /// <param name="PairsPerSplit">Pairs that qualified, on average per split.</param>
    /// <param name="Retained">Share of the training half's deviation the test half shows again.</param>
    /// <param name="RetainedToday">Share of it the linear pull of today keeps.</param>
    /// <param name="MeanGames">Games in the qualifying training halves, on average.</param>
    public sealed record Confirmation(double PairsPerSplit, double Retained, double RetainedToday, double MeanGames);

    /// <summary>
    /// Binomial(n, ½). Exact by coin flips for small n; above that the normal approximation, whose
    /// error at these sizes is far below anything the comparison resolves.
    /// </summary>
    public static int Binomial(Random random, int n)
    {
        if (n <= 0)
            return 0;

        if (n <= 200)
        {
            var count = 0;
            for (var i = 0; i < n; i++)
            {
                if (random.Next(2) == 0)
                    count++;
            }

            return count;
        }

        // Box-Muller.
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        var normal = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);

        return Math.Clamp((int)Math.Round((n / 2.0) + (normal * Math.Sqrt(n / 4.0))), 0, n);
    }

    private static double LogBeta(double a, double b) => LogGamma(a) + LogGamma(b) - LogGamma(a + b);

    private static readonly double[] LanczosCoefficients =
    [
        0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
        -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
    ];

    /// <summary>ln Γ(x) for x &gt; 0 (Lanczos, g = 7): about fifteen digits, far more than a count needs.</summary>
    public static double LogGamma(double x)
    {
        if (x < 0.5)
            return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1 - x);

        x -= 1;
        var sum = LanczosCoefficients[0];
        var t = x + 7.5;
        for (var i = 1; i < LanczosCoefficients.Length; i++)
            sum += LanczosCoefficients[i] / (x + i);
        return (0.5 * Math.Log(2 * Math.PI)) + ((x + 0.5) * Math.Log(t)) - t + Math.Log(sum);
    }
}
