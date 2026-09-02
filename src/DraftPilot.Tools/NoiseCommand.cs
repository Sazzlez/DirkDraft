using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;

namespace DraftPilot.Tools;

/// <summary>
/// Resamples the stored snapshot from its own sample sizes and reports how much the recommendation
/// moves. This is the measuring instrument behind <see cref="ScoreError"/>: the scoring code
/// propagates error analytically, which is fast enough to run per candidate during a draft, and
/// this verb is how that closed form gets checked against the thing it approximates.
/// <para>
/// It answers the question the panel cannot: if OP.GG had observed a different but equally likely
/// set of games, would the same champion still be on top?
/// </para>
/// </summary>
internal static class NoiseCommand
{
    public static int Run(string[] args)
    {
        var snapshot = new SnapshotStore().Load();
        if (snapshot is null)
        {
            Console.Error.WriteLine("Kein Snapshot vorhanden. Erst 'update' ausführen.");
            return 1;
        }

        var meta = new MetaLookup(snapshot);
        var traits = TraitTable.Load();

        var laneArgument = args.Length > 1 ? args[1].ToLowerInvariant() : "mid";
        var lane = Lanes.FromOpGg(laneArgument);
        if (lane == Lane.Unknown)
            lane = Lanes.FromLcu(laneArgument == "bot" ? "bottom" : laneArgument);

        if (lane == Lane.Unknown)
        {
            Console.Error.WriteLine("Lane muss top, jungle, mid, adc/bot oder support sein.");
            return 2;
        }

        var enemies = RecommendCommand.Resolve(meta, args.Length > 2 ? args[2] : string.Empty, out _);
        var allies = RecommendCommand.Resolve(meta, args.Length > 3 ? args[3] : string.Empty, out _);
        var rounds = args.Length > 4 && int.TryParse(args[4], out var parsed) && parsed > 0 ? parsed : 400;

        var state = RecommendCommand.BuildState(lane, allies, enemies);
        var target = new TurnTracker().Resolve(state);
        if (target is null)
        {
            Console.Error.WriteLine("Kein Ziel-Slot ermittelt.");
            return 1;
        }

        var priors = SeatPriors.Load();

        Console.WriteLine($"Lane: {lane.Display()}   Gegner: {RecommendCommand.Names(meta, enemies)}   "
            + $"Team: {RecommendCommand.Names(meta, allies)}");
        Console.WriteLine($"{rounds} Ziehungen aus den Stichproben des Snapshots.");
        Console.WriteLine();

        // The reference run on the unperturbed snapshot: this is the list the tool would show.
        var reference = Advise(meta, traits, priors, state, target);
        if (reference.Count == 0)
        {
            Console.Error.WriteLine("Keine Empfehlungen für diese Lane.");
            return 1;
        }

        var tracked = reference.Take(8).Select(item => item.ChampionId).ToList();
        var scores = tracked.ToDictionary(id => id, _ => new List<double>(rounds));
        var leaderCount = new Dictionary<int, int>();
        var rankSum = tracked.ToDictionary(id => id, _ => 0);

        // Fixed seed: the point of the run is to compare two error estimates, and that comparison
        // has to be repeatable across builds.
        var random = new Random(20260901);

        for (var round = 0; round < rounds; round++)
        {
            var shaken = Resample(snapshot, random);
            var items = Advise(new MetaLookup(shaken), traits, priors, state, target);
            if (items.Count == 0)
                continue;

            leaderCount[items[0].ChampionId] = leaderCount.GetValueOrDefault(items[0].ChampionId) + 1;

            for (var rank = 0; rank < items.Count; rank++)
            {
                if (!scores.TryGetValue(items[rank].ChampionId, out var series))
                    continue;

                series.Add(items[rank].Score);
                rankSum[items[rank].ChampionId] += rank + 1;
            }
        }

        Console.WriteLine("Champion         Score   analytisch   gemessen   Rang oe   fuehrt in");
        Console.WriteLine(new string('-', 74));

        foreach (var item in reference.Take(8))
        {
            var series = scores[item.ChampionId];
            var analytic = ScoreError.AsPoints(item.Uncertainty);
            var measured = StandardDeviation(series) * 100;
            var meanRank = series.Count > 0 ? (double)rankSum[item.ChampionId] / series.Count : 0;
            var leads = leaderCount.GetValueOrDefault(item.ChampionId) / (double)rounds;

            Console.WriteLine($"{item.Name,-15} {item.Score,6:P1}   {analytic,8:N2}   {measured,8:N2}   "
                + $"{meanRank,6:N2}   {leads,8:P0}");
        }

        Console.WriteLine();
        var tiedCount = ScoreError.CountLeadingTies(reference);
        Console.WriteLine(tiedCount >= 2
            ? $"Gleichrangig laut analytischem Fehler: die oberen {tiedCount}."
            : "Der erste Platz ist laut analytischem Fehler eindeutig.");

        var topStability = leaderCount.GetValueOrDefault(reference[0].ChampionId) / (double)rounds;
        Console.WriteLine($"Platz 1 bleibt in {topStability:P0} der Ziehungen derselbe Champion.");
        Console.WriteLine();
        Console.WriteLine("Stimmen die Spalten analytisch und gemessen ueberein, beschreibt der Fehlerbalken");
        Console.WriteLine("der Oberflaeche das Rauschen richtig. Weichen sie ab, ist die Naeherung falsch.");

        return 0;
    }

    private static IReadOnlyList<Recommendation> Advise(
        MetaLookup meta,
        TraitTable traits,
        SeatPriors priors,
        DraftState state,
        RecommendationTarget target)
    {
        var enemyLanes = new LanePredictor(meta, priors).Predict(state.Enemies);
        var allyLanes = new LanePredictor(meta, priors).Predict(state.Allies);

        return new Recommender(meta, traits).Recommend(state, target, enemyLanes, allyLanes, limit: 40).Items;
    }

    /// <summary>
    /// One draw of the whole snapshot: every rate is redrawn from the sampling distribution its own
    /// game count implies. The normal approximation to the binomial is used deliberately — the
    /// smallest samples in here are in the dozens, where it is already close, and an exact draw for
    /// 4.000 rates times 400 rounds would turn a two-second check into a minute.
    /// </summary>
    private static MetaSnapshot Resample(MetaSnapshot source, Random random)
    {
        var copy = new MetaSnapshot
        {
            Version = source.Version,
            Patch = source.Patch,
            DataPatch = source.DataPatch,
            DataAsOfUtc = source.DataAsOfUtc,
            GameMode = source.GameMode,
            Tier = source.Tier,
            Champions = source.Champions,
        };

        foreach (var stat in source.LaneStats)
        {
            copy.LaneStats.Add(new LaneStat
            {
                ChampionId = stat.ChampionId,
                Lane = stat.Lane,
                WinRate = Draw(stat.WinRate, stat.Play, random),
                PickRate = stat.PickRate,
                BanRate = stat.BanRate,
                RoleRate = stat.RoleRate,
                Tier = stat.Tier,
                Play = stat.Play,
                FromTierList = stat.FromTierList,
            });
        }

        foreach (var stat in source.Matchups)
        {
            copy.Matchups.Add(new MatchupStat
            {
                ChampionId = stat.ChampionId,
                OpponentId = stat.OpponentId,
                Lane = stat.Lane,
                WinRate = Draw(stat.WinRate, stat.Play, random),
                Play = stat.Play,
            });
        }

        foreach (var stat in source.Synergies)
        {
            copy.Synergies.Add(new SynergyStat
            {
                ChampionId = stat.ChampionId,
                Lane = stat.Lane,
                PartnerId = stat.PartnerId,
                PartnerLane = stat.PartnerLane,
                WinRate = Draw(stat.WinRate, stat.Play, random),
                Play = stat.Play,
                Tier = stat.Tier,
            });
        }

        return copy;
    }

    private static double Draw(double rate, int play, Random random)
    {
        if (play <= 0)
            return rate;

        var sigma = Math.Sqrt(Math.Max(rate * (1 - rate), 1e-6) / play);
        return Math.Clamp(rate + (sigma * Normal(random)), 0, 1);
    }

    /// <summary>Box-Muller; one of the two draws is enough and the second is simply dropped.</summary>
    private static double Normal(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    private static double StandardDeviation(List<double> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();
        return Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1));
    }
}
