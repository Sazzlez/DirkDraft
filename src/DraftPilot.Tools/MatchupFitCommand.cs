using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;

namespace DraftPilot.Tools;

/// <summary>
/// Holds back a fifth of the matchup edges and asks which baseline predicts them best. This decides
/// whether the duel term should be centred at all — the alternative to measuring would be shipping
/// a model change on an argument.
/// <para>
/// The question is concrete. Today a missing edge contributes exactly zero to the score, which
/// means "an even matchup". Since 83 % of the possible pairs have no edge, that default applies far
/// more often than any measured number. If a champion's own baseline predicts a held-out edge
/// better than a flat 50 %, then centring is the better default — and centring also removes from
/// the duel term the general strength that the lane term already counts.
/// </para>
/// </summary>
internal static class MatchupFitCommand
{
    private sealed record Edge(int Champion, int Opponent, Lane Lane, double Rate, int Play);

    /// <summary>One candidate rule for "what do we expect this champion to do, absent an edge".</summary>
    private sealed record Baseline(string Name, Func<Edge, double> Predict);

    public static int Run(string[] args)
    {
        var snapshot = new SnapshotStore().Load();
        if (snapshot is null)
        {
            Console.Error.WriteLine("Kein Snapshot vorhanden. Erst 'update' ausführen.");
            return 1;
        }

        var meta = new MetaLookup(snapshot);
        var folds = args.Length > 1 && int.TryParse(args[1], out var parsed) && parsed >= 2 ? parsed : 5;

        var edges = snapshot.Matchups
            .Where(stat => stat.Play > 0 && stat.Lane != Lane.Unknown)
            .Select(stat => new Edge(stat.ChampionId, stat.OpponentId, stat.Lane, stat.WinRate, stat.Play))
            .ToList();

        Console.WriteLine($"{edges.Count} Kanten, {folds}-fache Kreuzvalidierung.");
        Console.WriteLine();

        Describe(edges, meta);

        // Every edge is tested exactly once, and the baselines derived from edges never see the
        // fold they are scored on. Deterministic split so the numbers are comparable across runs.
        var results = new Dictionary<string, (double LogLoss, double Brier, int Games)>(StringComparer.Ordinal);

        for (var fold = 0; fold < folds; fold++)
        {
            var test = edges.Where(edge => Bucket(edge, folds) == fold).ToList();
            var train = edges.Where(edge => Bucket(edge, folds) != fold).ToList();

            foreach (var baseline in Baselines(meta, train))
            {
                var (loss, brier, games) = Score(test, baseline);
                var seen = results.GetValueOrDefault(baseline.Name);
                results[baseline.Name] = (seen.LogLoss + loss, seen.Brier + brier, seen.Games + games);
            }
        }

        Console.WriteLine("Vorhersage einer zurueckgehaltenen Kante:");
        Console.WriteLine();
        Console.WriteLine("Regel                          LogLoss je Spiel   Brier    besser als 50 %");
        Console.WriteLine(new string('-', 78));

        var flat = results["pauschal 50 %"];
        var flatLoss = flat.LogLoss / flat.Games;

        foreach (var entry in results.OrderBy(pair => pair.Value.LogLoss / pair.Value.Games))
        {
            var loss = entry.Value.LogLoss / entry.Value.Games;
            var brier = entry.Value.Brier / entry.Value.Games;
            var gain = (flatLoss - loss) / flatLoss;

            Console.WriteLine($"{entry.Key,-30} {loss,16:N5}   {brier,6:N5}   {gain,15:P2}");
        }

        Console.WriteLine();
        Console.WriteLine("LogLoss je Spiel: negative Log-Likelihood der tatsaechlich beobachteten Siege");
        Console.WriteLine("unter der vorhergesagten Quote. Kleiner ist besser. 0,69315 = blindes Raten.");

        return 0;
    }

    /// <summary>
    /// The two facts the analysis rested on, re-measured: how far the stored edges sit from even,
    /// and how strongly the duel term tracks the lane term it is added to.
    /// </summary>
    private static void Describe(List<Edge> edges, MetaLookup meta)
    {
        var mean = edges.Average(edge => edge.Rate);
        var weighted = edges.Sum(edge => edge.Rate * edge.Play) / edges.Sum(edge => (double)edge.Play);

        Console.WriteLine($"Mittelwert der Kanten:          {mean:N4}  (nach Games gewichtet {weighted:N4})");

        var pairs = edges
            .Select(edge => (
                Lane: meta.LaneStat(edge.Champion, edge.Lane) is { } stat ? ScoreModel.Logit(stat.WinRate) : double.NaN,
                Duel: ScoreModel.Logit(edge.Rate)))
            .Where(pair => double.IsFinite(pair.Lane))
            .ToList();

        var r = Correlation(pairs);
        var spreadDuel = StandardDeviation(pairs.Select(pair => pair.Duel));
        var spreadLane = StandardDeviation(pairs.Select(pair => pair.Lane));

        Console.WriteLine($"Korrelation Lane-Term/Matchup-Term: {r:N4}   erklaerte Varianz {r * r:P1}");
        Console.WriteLine($"Streuung Matchup-Term {spreadDuel:N4} Logit, Lane-Term {spreadLane:N4} Logit");

        // How much of the duel term is the lane term counted twice: the regression slope times one
        // standard deviation of lane strength, expressed in the same points the breakdown shows.
        var overCredit = ScoreModel.AsPoints(r * spreadDuel);
        Console.WriteLine($"Doppelzaehlung fuer einen Champ 1 Sigma ueber dem Mittel: {overCredit:N2} Punkte");
        Console.WriteLine();
    }

    private static IEnumerable<Baseline> Baselines(MetaLookup meta, List<Edge> train)
    {
        // What the tool does today: no baseline at all. A missing edge is an even matchup.
        yield return new Baseline("pauschal 50 %", _ => 0.5);

        // The collective mean of the edges OP.GG lists, taken from the training folds only. The
        // listed edges are not a random sample of all duels — the source reports the opponents
        // that stand out — so their mean sits below 50 %, and that offset is a property of the
        // list, not of the game.
        var listedMean = train.Sum(edge => edge.Rate * edge.Play) / train.Sum(edge => (double)edge.Play);
        yield return new Baseline($"Kantenmittel gesamt ({listedMean:P1})", _ => listedMean);

        // What the two lane win rates alone imply for this duel. This is the candidate that keeps
        // the mirror exact: swapping the two sides gives exactly the complement, so a shrunk edge
        // and its reverse still add to 1.
        double PairPrior(Edge edge)
        {
            var mine = meta.LaneStat(edge.Champion, edge.Lane);
            var theirs = meta.LaneStat(edge.Opponent, edge.Lane);

            if (mine is null || theirs is null)
                return 0.5;

            return ScoreModel.Sigmoid(ScoreModel.Logit(mine.Value.WinRate) - ScoreModel.Logit(theirs.Value.WinRate));
        }

        yield return new Baseline("Lane-Paar", PairPrior);

        // The same, moved onto the listed edges' own level. Without the shift the prediction is
        // systematically a point too optimistic for every row in this table.
        var shift = ScoreModel.Logit(listedMean);
        yield return new Baseline("Lane-Paar + Listenversatz", edge =>
            ScoreModel.Sigmoid(ScoreModel.Logit(PairPrior(edge)) + shift));

        // The champion's overall rate on that lane, from the tier list. Different bracket than the
        // matchups, which is exactly the objection worth measuring.
        yield return new Baseline("Lane-Winrate (Tierlist)", edge =>
            meta.LaneStat(edge.Champion, edge.Lane) is { } stat ? stat.WinRate : 0.5);

        // The mean of this champion's other known edges on this lane, shrunk by how many there are.
        // Same source and bracket as the target, so no systematic offset.
        var byChampion = train
            .GroupBy(edge => (edge.Champion, edge.Lane))
            .ToDictionary(group => group.Key, group => (
                Mean: group.Sum(edge => edge.Rate * edge.Play) / group.Sum(edge => (double)edge.Play),
                Count: group.Count()));

        // Swept over the shrinkage prior: if the champion mean carries any signal at all, some
        // amount of pull toward 0.5 has to beat a flat 0.5. If none does, it carries none.
        foreach (var prior in new[] { 1, 3, 10, 30 })
        {
            var pull = prior;
            yield return new Baseline($"Kantenmittel, Prior {prior}", edge =>
                byChampion.TryGetValue((edge.Champion, edge.Lane), out var own)
                    ? Shrinkage.Apply(own.Mean, own.Count, pull)
                    : 0.5);
        }

        // Both sides: the champion's own level, adjusted by how the opponent fares generally. An
        // edge is a duel, so the opponent's strength belongs in the prediction too.
        var byOpponent = train
            .GroupBy(edge => (edge.Opponent, edge.Lane))
            .ToDictionary(group => group.Key, group => (
                Mean: group.Sum(edge => edge.Rate * edge.Play) / group.Sum(edge => (double)edge.Play),
                Count: group.Count()));

        yield return new Baseline("Kantenmittel beider Seiten", edge =>
        {
            var own = byChampion.TryGetValue((edge.Champion, edge.Lane), out var mine)
                ? ScoreModel.Logit(Shrinkage.Apply(mine.Mean, mine.Count, 3))
                : 0;

            // The opponent's mean is its win rate INTO others; a champion that generally wins makes
            // life harder, so it enters with the opposite sign.
            var theirs = byOpponent.TryGetValue((edge.Opponent, edge.Lane), out var other)
                ? ScoreModel.Logit(Shrinkage.Apply(other.Mean, other.Count, 3))
                : 0;

            return ScoreModel.Sigmoid(own - theirs);
        });
    }

    private static (double LogLoss, double Brier, int Games) Score(List<Edge> test, Baseline baseline)
    {
        var loss = 0.0;
        var brier = 0.0;
        var games = 0;

        foreach (var edge in test)
        {
            var predicted = Math.Clamp(baseline.Predict(edge), 0.001, 0.999);

            // The edge is a proportion over Play games, so the honest score is the likelihood of
            // those wins and losses — a 9.000-game edge counts more than a 60-game one.
            var wins = (int)Math.Round(edge.Rate * edge.Play);
            var losses = edge.Play - wins;

            loss -= (wins * Math.Log(predicted)) + (losses * Math.Log(1 - predicted));
            brier += edge.Play * (edge.Rate - predicted) * (edge.Rate - predicted);
            games += edge.Play;
        }

        return (loss, brier, games);
    }

    /// <summary>Stable fold assignment: the same edge lands in the same fold on every run.</summary>
    private static int Bucket(Edge edge, int folds)
    {
        var hash = HashCode.Combine(edge.Champion, edge.Opponent, (int)edge.Lane);
        return Math.Abs(hash % folds);
    }

    private static double StandardDeviation(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count < 2)
            return 0;

        var mean = list.Average();
        return Math.Sqrt(list.Sum(value => (value - mean) * (value - mean)) / (list.Count - 1));
    }

    private static double Correlation(List<(double Lane, double Duel)> pairs)
    {
        if (pairs.Count < 2)
            return 0;

        var meanLane = pairs.Average(pair => pair.Lane);
        var meanDuel = pairs.Average(pair => pair.Duel);

        var covariance = pairs.Sum(pair => (pair.Lane - meanLane) * (pair.Duel - meanDuel));
        var spreadLane = Math.Sqrt(pairs.Sum(pair => (pair.Lane - meanLane) * (pair.Lane - meanLane)));
        var spreadDuel = Math.Sqrt(pairs.Sum(pair => (pair.Duel - meanDuel) * (pair.Duel - meanDuel)));

        return spreadLane * spreadDuel == 0 ? 0 : covariance / (spreadLane * spreadDuel);
    }
}
