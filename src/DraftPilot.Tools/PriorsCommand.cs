using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta;

namespace DraftPilot.Tools;

/// <summary>
/// Measures how hard each kind of rate should be pulled towards its expectation, instead of
/// asserting it. <see cref="Shrinkage"/> carries three prior weights — lane, matchup, duo — and
/// each was set by eye to "the typical sample size" of its data. That is a guess about the wrong
/// quantity: the right weight depends on how far real rates spread around their expectation, not
/// on how many games a row usually has.
/// <para>
/// No match outcomes are needed to measure it. A row is a count of wins over games, and games are
/// exchangeable, so splitting each row's games at random into two halves gives a training half and
/// a test half of the very same population. The prior weight that best predicts the test half from
/// the training half is the one the data supports. Repeated over many random splits, the noise of
/// any single split averages out.
/// </para>
/// <para>
/// The optimum does not depend on the half: in a beta-binomial model the posterior mean is
/// (wins + κ·μ) / (games + κ) with κ = μ(1 − μ)/τ² − 1, where τ² is the true spread of rates around
/// their expectation μ. κ is a property of the population, not of the sample size, so the weight
/// found on half-rows is the weight for whole ones.
/// </para>
/// </summary>
internal static class PriorsCommand
{
    internal sealed record Row(int Wins, int Games, double Expected);

    internal static readonly int[] Grid =
        [0, 25, 50, 75, 100, 150, 200, 300, 400, 500, 600, 800, 1000, 1200, 1600, 2000, 3000, 5000, 10000, int.MaxValue];

    public static int Run(string[] args)
    {
        var snapshot = new SnapshotStore().Load();
        if (snapshot is null)
        {
            Console.Error.WriteLine("Kein Snapshot vorhanden. Erst 'update' ausführen.");
            return 1;
        }

        var repetitions = args.Length > 1 && int.TryParse(args[1], out var parsed) && parsed > 0 ? parsed : 200;

        // The baselines the next data update would write. This file may predate them, and the
        // experiment has to shrink towards what the app will actually shrink towards.
        snapshot.LaneBaseline = SnapshotBuilder.MeasureLaneBaseline(snapshot.LaneStats);
        snapshot.MatchupBaseline = SnapshotBuilder.MeasureMatchupBaseline(snapshot.Matchups, snapshot.LaneStats);
        snapshot.SynergyBaseline = SnapshotBuilder.MeasureSynergyBaseline(snapshot.Synergies);

        var meta = new MetaLookup(snapshot);

        Console.WriteLine($"Snapshot {snapshot.Patch}, Bracket {snapshot.Tier ?? "?"}, {repetitions} zufällige Teilungen je Zeile.");
        Console.WriteLine("Gemessen wird der Log-Loss der zurückgehaltenen Hälfte; 0,000 % = das heutige Gewicht.");
        Console.WriteLine();

        var lanes = snapshot.LaneStats
            .Where(stat => stat.Play > 0)
            .Select(stat => ToRow(stat.WinRate, stat.Play, meta.LaneTarget))
            .ToList();

        // One row per pair and lane: both champions' counter lists can carry the same duel, and the
        // same games counted twice would tell the experiment twice as much as they know.
        var matchups = snapshot.Matchups
            .Where(stat => stat.Play > 0 && stat.Lane != Lane.Unknown)
            .GroupBy(stat => (stat.Lane, Low: Math.Min(stat.ChampionId, stat.OpponentId), High: Math.Max(stat.ChampionId, stat.OpponentId)))
            .Select(group => group.OrderByDescending(stat => stat.Play).First())
            .Select(stat => ToRow(stat.WinRate, stat.Play, meta.MatchupTarget(stat.ChampionId, stat.OpponentId, stat.Lane)))
            .ToList();

        var synergies = snapshot.Synergies
            .Where(stat => stat.Play > 0)
            .GroupBy(stat => (Low: Math.Min(stat.ChampionId, stat.PartnerId), High: Math.Max(stat.ChampionId, stat.PartnerId)))
            .Select(group => group.OrderByDescending(stat => stat.Play).First())
            .Select(stat => ToRow(stat.WinRate, stat.Play, meta.SynergyTarget))
            .ToList();

        Report("Lane", lanes, Shrinkage.LanePrior, repetitions);

        // The stored edges are the three most notable opponents per list — a selection — and both
        // halves of a selected row share it. This test therefore cannot see the selection effect and
        // reads far too low a weight for them; guidecheck measures it against unselected lists.
        Console.WriteLine("Achtung Matchup: Die gespeicherten Duelle sind eine AUSWAHL (die auffälligsten Gegner),");
        Console.WriteLine("und beide Hälften einer ausgewählten Zeile teilen diese Auswahl. Das Optimum unten ist");
        Console.WriteLine("deshalb zu niedrig; maßgeblich ist 'guidecheck' gegen die vollständigen Listen.");
        Report("Matchup", matchups, Shrinkage.MatchupPrior, repetitions);
        Report("Synergie", synergies, Shrinkage.SynergyPrior, repetitions);

        PairExpectation(snapshot, meta, repetitions);

        return 0;
    }

    /// <summary>
    /// Does a duo's rate carry the two champions' own strength? If it does, a duo term centred on
    /// one global mean books the candidate's general strength a second time — the error that was
    /// measured and removed from the matchup term (slope 1,23 there). Tested two ways: a regression
    /// of the duo's log-odds on both lane log-odds, and the same held-out split as above with the
    /// pair's own expectation as the prior instead of the global one.
    /// </summary>
    private static void PairExpectation(MetaSnapshot snapshot, MetaLookup meta, int repetitions)
    {
        var pairs = snapshot.Synergies
            .Where(stat => stat.Play > 0)
            .GroupBy(stat => (Low: Math.Min(stat.ChampionId, stat.PartnerId), High: Math.Max(stat.ChampionId, stat.PartnerId)))
            .Select(group => group.OrderByDescending(stat => stat.Play).First())
            .Select(stat => (Stat: stat, Mine: meta.LaneStat(stat.ChampionId, stat.Lane), Theirs: meta.LaneStat(stat.PartnerId, stat.PartnerLane)))
            .Where(entry => entry.Mine is not null && entry.Theirs is not null)
            .Select(entry => (
                entry.Stat,
                X1: ScoreModel.Logit(entry.Mine!.Value.WinRate),
                X2: ScoreModel.Logit(entry.Theirs!.Value.WinRate),
                Y: ScoreModel.Logit(entry.Stat.WinRate)))
            .ToList();

        Console.WriteLine($"=== Duo gegen die Einzelstärke beider Champions: {pairs.Count} Paare mit Lane-Zahlen für beide");

        // Weighted least squares, y = a + b1·x1 + b2·x2, weights = games.
        var (a, b1, b2, r2) = Regress(pairs.Select(pair => (pair.X1, pair.X2, pair.Y, (double)pair.Stat.Play)).ToList());
        Console.WriteLine($"    Regression: Duo-Logit = {a:+0.000;-0.000} + {b1:0.000} × eigene Lane + {b2:0.000} × Partner-Lane   (R² {r2:0.000})");
        Console.WriteLine("    Steigung 1 hieße: die Duo-Rate enthält die Einzelstärke vollständig.");

        // The listing offset for the pair expectation, measured like the matchup one: the mean
        // residual of the listed rows against the plain sum.
        var offset = pairs.Sum(pair => pair.Stat.Play * (pair.Y - pair.X1 - pair.X2)) / pairs.Sum(pair => (double)pair.Stat.Play);
        Console.WriteLine($"    Listenversatz gegen die Summe beider Lane-Logits: {offset:+0.0000;-0.0000}");

        var global = pairs.Select(pair => ToRow(pair.Stat.WinRate, pair.Stat.Play, meta.SynergyTarget)).ToList();
        var paired = pairs.Select(pair => ToRow(pair.Stat.WinRate, pair.Stat.Play, ScoreModel.Sigmoid(pair.X1 + pair.X2 + offset))).ToList();

        // The regression line itself as the expectation: as much of each champion's strength as the
        // duo rates actually carry, no more. Fitted on all rows — three numbers from 2.600 rows leak
        // nothing worth mentioning into the held-out halves.
        // Symmetric, as the app uses it: a duo has no direction, and which side a row is stored
        // under is an accident of whose synergy list it came from.
        var line = meta.SynergyLine;
        Console.WriteLine($"    Symmetrisch (wie in der App): Duo-Logit = {line.Intercept:+0.000;-0.000} + {line.Slope:0.000} × (Summe beider Lane-Logits), {line.Rows} Paare");

        var fitted = pairs.Select(pair => ToRow(pair.Stat.WinRate, pair.Stat.Play, ScoreModel.Sigmoid(line.Intercept + (line.Slope * (pair.X1 + pair.X2))))).ToList();

        var globalLoss = CrossValidate(global, repetitions);
        var pairedLoss = CrossValidate(paired, repetitions);
        var fittedLoss = CrossValidate(fitted, repetitions);
        var reference = globalLoss[Array.IndexOf(Grid, NearestGridValue(Shrinkage.SynergyPrior))];

        var (tau, kappa) = MethodOfMoments(paired);
        Console.WriteLine($"    Momentenschätzung um die Paar-Erwartung: τ = {tau * 100:0.00} Punkte → κ ≈ {(double.IsFinite(kappa) ? kappa.ToString("N0") : "∞")}");
        var (tauFit, kappaFit) = MethodOfMoments(fitted);
        Console.WriteLine($"    Momentenschätzung um die Regressionsgerade: τ = {tauFit * 100:0.00} Punkte → κ ≈ {(double.IsFinite(kappaFit) ? kappaFit.ToString("N0") : "∞")}");
        Console.WriteLine($"    {"κ",-20} {"global",10} {"Summe",10} {"Gerade",10}   (0,000 % = heute: global, κ = {Shrinkage.SynergyPrior})");

        for (var index = 0; index < Grid.Length; index++)
        {
            var label = Grid[index] == int.MaxValue ? "∞ (nur Erwartung)" : Grid[index].ToString("N0");
            Console.WriteLine($"    κ = {label,-16} {(globalLoss[index] - reference) / reference * 100,9:+0.000;-0.000;0.000} % "
                + $"{(pairedLoss[index] - reference) / reference * 100,9:+0.000;-0.000;0.000} % "
                + $"{(fittedLoss[index] - reference) / reference * 100,9:+0.000;-0.000;0.000} %");
        }

        Console.WriteLine();
    }

    private static (double A, double B1, double B2, double R2) Regress(List<(double X1, double X2, double Y, double W)> data)
    {
        // Normal equations for two regressors with intercept, weighted.
        double sw = 0, sx1 = 0, sx2 = 0, sy = 0;
        foreach (var (x1, x2, y, w) in data)
        {
            sw += w;
            sx1 += w * x1;
            sx2 += w * x2;
            sy += w * y;
        }

        double m1 = sx1 / sw, m2 = sx2 / sw, my = sy / sw;
        double s11 = 0, s22 = 0, s12 = 0, s1y = 0, s2y = 0, syy = 0;

        foreach (var (x1, x2, y, w) in data)
        {
            double d1 = x1 - m1, d2 = x2 - m2, dy = y - my;
            s11 += w * d1 * d1;
            s22 += w * d2 * d2;
            s12 += w * d1 * d2;
            s1y += w * d1 * dy;
            s2y += w * d2 * dy;
            syy += w * dy * dy;
        }

        var determinant = (s11 * s22) - (s12 * s12);
        var b1 = ((s22 * s1y) - (s12 * s2y)) / determinant;
        var b2 = ((s11 * s2y) - (s12 * s1y)) / determinant;
        var a = my - (b1 * m1) - (b2 * m2);
        var explained = (b1 * s1y) + (b2 * s2y);

        return (a, b1, b2, syy > 0 ? explained / syy : 0);
    }

    private static Row ToRow(double rate, int games, double expected)
        => new(Math.Clamp((int)Math.Round(rate * games), 0, games), games, Math.Clamp(expected, 0.01, 0.99));

    private static void Report(string name, List<Row> rows, int current, int repetitions)
    {
        var games = rows.Sum(row => (long)row.Games);
        var sorted = rows.Select(row => row.Games).Order().ToList();

        Console.WriteLine($"=== {name}: {rows.Count} Zeilen, {games:N0} Spiele, Median {sorted[sorted.Count / 2]:N0} je Zeile " +
                          $"(10 % unter {sorted[sorted.Count / 10]:N0}, 90 % unter {sorted[sorted.Count * 9 / 10]:N0})");

        var (tau, kappa) = MethodOfMoments(rows);
        Console.WriteLine($"    Momentenschätzung: wahre Streuung um den Erwartungswert τ = {tau * 100:0.00} Punkte " +
                          $"→ Gewicht κ ≈ {(double.IsFinite(kappa) ? kappa.ToString("N0") : "∞")}");

        var loss = CrossValidate(rows, repetitions);
        var reference = loss[Array.IndexOf(Grid, NearestGridValue(current))];
        var best = loss.Select((value, index) => (value, index)).MinBy(pair => pair.value).index;

        foreach (var (weight, index) in Grid.Select((weight, index) => (weight, index)))
        {
            var label = weight == int.MaxValue ? "∞ (nur Erwartung)" : weight.ToString("N0");
            var marks = (weight == current ? " ← heute" : string.Empty) + (index == best ? " ← bestes" : string.Empty);
            Console.WriteLine($"    κ = {label,-18} {(loss[index] - reference) / reference * 100,8:+0.000;-0.000;0.000} %{marks}");
        }

        Console.WriteLine();
    }

    private static int NearestGridValue(int value) => Grid.MinBy(entry => Math.Abs((double)entry - value));

    /// <summary>
    /// τ² from the excess variance: what the observed rates spread around their expectations,
    /// minus what binomial noise alone explains. Games-weighted, so a ten-game row does not count
    /// as much as a ten-thousand-game one.
    /// </summary>
    private static (double Tau, double Kappa) MethodOfMoments(List<Row> rows)
    {
        double weighted = 0, noise = 0, weights = 0, meanSpread = 0;

        foreach (var row in rows)
        {
            var rate = (double)row.Wins / row.Games;
            var spread = row.Expected * (1 - row.Expected);
            var w = row.Games;

            weighted += w * (rate - row.Expected) * (rate - row.Expected);
            noise += w * spread / row.Games;
            meanSpread += w * spread;
            weights += w;
        }

        var tauSquared = Math.Max(0, (weighted - noise) / weights);
        var kappa = tauSquared <= 0 ? double.PositiveInfinity : (meanSpread / weights / tauSquared) - 1;

        return (Math.Sqrt(tauSquared), kappa);
    }

    /// <summary>
    /// Mean held-out log loss per game for every weight on the grid, averaged over random splits.
    /// Each game lands in the training half with probability one half — a binomial split of the
    /// wins and of the losses, which is exactly how a random half of a row's games would fall.
    /// </summary>
    internal static double[] CrossValidate(List<Row> rows, int repetitions)
    {
        var totals = new double[Grid.Length];
        long testGames = 0;
        var random = new Random(20261008);

        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            foreach (var row in rows)
            {
                var trainWins = Binomial(random, row.Wins);
                var trainLosses = Binomial(random, row.Games - row.Wins);
                var trainGames = trainWins + trainLosses;

                var testWins = row.Wins - trainWins;
                var testLosses = row.Games - row.Wins - trainLosses;

                if (testWins + testLosses == 0)
                    continue;

                testGames += testWins + testLosses;

                for (var index = 0; index < Grid.Length; index++)
                {
                    var kappa = Grid[index];
                    // A training half with no games and no prior weight says nothing; the expectation
                    // stands in rather than 0/0.
                    var estimate = kappa == int.MaxValue || trainGames + kappa == 0
                        ? row.Expected
                        : (trainWins + (kappa * row.Expected)) / (trainGames + (double)kappa);

                    // A zero-weight estimate over an all-win half would be exactly 1 and its loss
                    // infinite; the clamp is the same one the score applies to every rate.
                    estimate = Math.Clamp(estimate, 0.01, 0.99);

                    totals[index] -= (testWins * Math.Log(estimate)) + (testLosses * Math.Log(1 - estimate));
                }
            }
        }

        return [.. totals.Select(total => total / testGames)];
    }

    /// <summary>
    /// Binomial(n, ½). Exact by coin flips for small n; above that the normal approximation, whose
    /// error at these sizes is far below anything the comparison resolves.
    /// </summary>
    private static int Binomial(Random random, int n)
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
}
