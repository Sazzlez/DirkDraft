using System.Text.Json.Nodes;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta;
using DraftPilot.Meta.OpGg;

namespace DraftPilot.Tools;

/// <summary>
/// Checks whether the complete duel lists in OP.GG's matchup guide can stand next to the file's
/// own numbers. The guide answers for one champion against EVERY opponent on a lane — where the
/// counter lists name three — but it takes no rank bracket, while the lane rates the duel term is
/// centred on come from the file's bracket. If the two populations disagree, the guide's duels
/// would carry that disagreement into every candidate's score; this measures whether they do.
/// <para>
/// Two numbers decide it. The mean of each guide duel against what the file's lane rates expect
/// for the pair (a population shift would show as a mean away from zero), and the slope of the
/// duel on that expectation (a different spread of strength would show as a slope away from one).
/// </para>
/// </summary>
internal static class GuideCheckCommand
{
    /// <summary>Four regulars per lane that has a duel list — the guide answers Jungle with none.</summary>
    private static readonly (string Champion, Lane Lane)[] Defaults =
    [
        ("Jax", Lane.Top), ("Garen", Lane.Top), ("Darius", Lane.Top), ("Sett", Lane.Top),
        ("Ahri", Lane.Mid), ("Syndra", Lane.Mid), ("Yasuo", Lane.Mid), ("Viktor", Lane.Mid),
        ("Jinx", Lane.Adc), ("Ezreal", Lane.Adc), ("Caitlyn", Lane.Adc), ("Kai'Sa", Lane.Adc),
        ("Thresh", Lane.Support), ("Lux", Lane.Support), ("Nami", Lane.Support), ("Leona", Lane.Support),
    ];

    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var snapshot = new SnapshotStore().Load();
        if (snapshot is null)
        {
            Console.Error.WriteLine("Kein Snapshot vorhanden. Erst 'update' ausführen.");
            return 1;
        }

        snapshot.MatchupBaseline = SnapshotBuilder.MeasureMatchupBaseline(snapshot.Matchups, snapshot.LaneStats);
        var meta = new MetaLookup(snapshot);
        using var client = new OpGgMcpClient();

        var points = new List<(double Expected, double Observed, int Play)>();
        var overlap = new List<(double Guide, double Stored, int GuidePlay, int StoredPlay)>();
        var residuals = new List<(double Stored, double Guide, double Weight)>();

        // Same patch on both sides: the notable counters fetched live, in the file's bracket,
        // against the complete list. What separates them is the selection, not the calendar.
        var sameDay = new List<(double Counter, double Guide, double Weight)>();
        var sameDayRaw = new List<(double CounterRate, int CounterPlay, double Expected, double GuideDeviation, int GuidePlay)>();
        var completeRows = new List<PriorsCommand.Row>();
        var fetcher = new LiveDraftFetcher(client, "ranked", meta.Tier.Length > 0 ? meta.Tier : "all");
        var resolver = new ChampionResolver(meta.Champions);

        foreach (var (name, lane) in Defaults)
        {
            var champion = meta.Champions.FirstOrDefault(entry => entry.Name == name);
            if (champion is null)
                continue;

            // The tool insists on an opponent. The duel list does not depend on it, so any regular
            // of the lane will do.
            var anyOpponent = meta.Roster(lane)
                .Where(id => id != champion.Id)
                .OrderByDescending(id => meta.LaneStat(id, lane)?.Play ?? 0)
                .Select(id => meta.Champion(id)!)
                .First();

            string? text = null;

            // Every spelling, like the real fetch: "Lee Sin" is LEE_SIN to one tool and LEESIN to
            // another, and the wrong one is answered with a refusal rather than an empty list.
            foreach (var mine in ChampionResolver.ApiNames(champion))
            {
                foreach (var theirs in ChampionResolver.ApiNames(anyOpponent))
                {
                    try
                    {
                        text = await client.CallToolRawAsync(
                            "lol_get_lane_matchup_guide",
                            new JsonObject { ["my_champion"] = mine, ["opponent_champion"] = theirs, ["position"] = lane.ToOpGg() },
                            ct);
                        break;
                    }
                    catch (OpGgApiException ex) when (ex.Status is null)
                    {
                    }
                }

                if (text is not null)
                    break;
            }

            var counters = (text is null ? null : JsonNode.Parse(text)?["data"]?["counters"] as JsonArray) ?? [];
            var rows = 0;

            var notable = (await fetcher.FetchEnemyCountersAsync(champion, lane, resolver, ct))
                .Where(stat => stat.Lane == lane)
                .GroupBy(stat => stat.OpponentId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(stat => stat.Play).First());

            foreach (var counter in counters)
            {
                var opponent = counter?["champion_id"]?.GetValue<int>() ?? 0;
                var play = counter?["play"]?.GetValue<int>() ?? 0;
                var win = counter?["win"]?.GetValue<int>() ?? 0;

                if (play <= 0 || opponent == champion.Id || meta.Champion(opponent) is null)
                    continue;

                var rate = (double)win / play;
                var expected = meta.MatchupBaseline(champion.Id, opponent, lane) - snapshot.MatchupBaseline;

                points.Add((expected, ScoreModel.Logit(rate), play));
                completeRows.Add(new PriorsCommand.Row(win, play, ScoreModel.Sigmoid(expected)));
                rows++;

                if (notable.TryGetValue(opponent, out var notableEdge))
                {
                    sameDay.Add((ScoreModel.Logit(notableEdge.WinRate) - expected, ScoreModel.Logit(rate) - expected, Math.Min(play, notableEdge.Play)));
                    sameDayRaw.Add((notableEdge.WinRate, notableEdge.Play, ScoreModel.Sigmoid(expected), ScoreModel.Logit(rate) - expected, play));
                }

                // The same pair as a stored Gold edge, oriented from this champion's side.
                var stored = snapshot.Matchups.FirstOrDefault(stat => stat.Lane == lane && stat.ChampionId == champion.Id && stat.OpponentId == opponent);
                var mirrored = snapshot.Matchups.FirstOrDefault(stat => stat.Lane == lane && stat.ChampionId == opponent && stat.OpponentId == champion.Id);

                var storedRate = stored?.WinRate ?? (mirrored is null ? (double?)null : 1 - mirrored.WinRate);
                var storedPlay = stored?.Play ?? mirrored?.Play ?? 0;

                if (storedRate is { } known)
                {
                    overlap.Add((rate, known, play, storedPlay));

                    // Both as deviations from the same plain expectation, weighted by the smaller
                    // sample — the one that limits how well the pair is known.
                    residuals.Add((ScoreModel.Logit(known) - expected, ScoreModel.Logit(rate) - expected, Math.Min(play, storedPlay)));
                }
            }

            Console.WriteLine($"  {name,-8} {lane.Display(),-8} {rows,3} Duelle, {counters.Sum(counter => counter?["play"]?.GetValue<int>() ?? 0),6:N0} Spiele");
        }

        Console.WriteLine();

        var weights = points.Sum(point => (double)point.Play);
        var meanResidual = points.Sum(point => point.Play * (point.Observed - point.Expected)) / weights;
        var meanX = points.Sum(point => point.Play * point.Expected) / weights;
        var meanY = points.Sum(point => point.Play * point.Observed) / weights;
        var slope = points.Sum(point => point.Play * (point.Expected - meanX) * (point.Observed - meanY))
            / points.Sum(point => point.Play * (point.Expected - meanX) * (point.Expected - meanX));

        Console.WriteLine($"{points.Count} Guide-Duelle gegen die Gold-Erwartung aus den Lane-Raten:");
        Console.WriteLine($"  mittlere Abweichung {meanResidual:+0.0000;-0.0000} Logit ({ScoreModel.AsPoints(meanResidual):+0.00;-0.00} Punkte)   — 0 hieße: gleiche Population");
        Console.WriteLine($"  Steigung {slope:0.000}                                — 1 hieße: gleiche Spreizung der Stärke");

        if (overlap.Count > 0)
        {
            var difference = overlap.Sum(pair => (ScoreModel.Logit(pair.Guide) - ScoreModel.Logit(pair.Stored)) * Math.Min(pair.GuidePlay, pair.StoredPlay))
                / overlap.Sum(pair => (double)Math.Min(pair.GuidePlay, pair.StoredPlay));

            // How far apart two independent measurements of the same duel should be from sampling
            // alone, against how far apart they are.
            var expectedSpread = overlap.Average(pair => (1.0 / (pair.GuidePlay * pair.Guide * (1 - pair.Guide)))
                + (1.0 / (pair.StoredPlay * pair.Stored * (1 - pair.Stored))));
            var observedSpread = overlap.Average(pair => Math.Pow(ScoreModel.Logit(pair.Guide) - ScoreModel.Logit(pair.Stored), 2));

            Console.WriteLine();
            Console.WriteLine($"{overlap.Count} Paare stehen in beiden Quellen:");
            Console.WriteLine($"  Guide minus Gold im Mittel {difference:+0.0000;-0.0000} Logit ({ScoreModel.AsPoints(difference):+0.00;-0.00} Punkte)");
            Console.WriteLine($"  Streuung der Differenz {Math.Sqrt(observedSpread):0.000}, aus Stichprobenrauschen allein erwartet {Math.Sqrt(expectedSpread):0.000}");

            // The winner's curse test. A notable-opponent list names the most extreme of some fifty
            // noisy duels, so its values are pushed outward by the selection itself; an independent
            // measurement of the same pairs regresses towards the middle. The slope of the guide's
            // deviation on the stored one says by how much the stored deviations are to be believed.
            var w = residuals.Sum(entry => entry.Weight);
            var mx = residuals.Sum(entry => entry.Weight * entry.Stored) / w;
            var my = residuals.Sum(entry => entry.Weight * entry.Guide) / w;
            var curse = residuals.Sum(entry => entry.Weight * (entry.Stored - mx) * (entry.Guide - my))
                / residuals.Sum(entry => entry.Weight * (entry.Stored - mx) * (entry.Stored - mx));
            var meanAbsStored = residuals.Sum(entry => entry.Weight * Math.Abs(entry.Stored)) / w;
            var meanAbsGuide = residuals.Sum(entry => entry.Weight * Math.Abs(entry.Guide)) / w;

            Console.WriteLine($"  Abweichung von der Erwartung: gespeichert im Mittel {ScoreModel.AsPoints(meanAbsStored):0.00} Punkte, im Guide {ScoreModel.AsPoints(meanAbsGuide):0.00}");
            Console.WriteLine($"  Steigung Guide-Abweichung auf gespeicherte Abweichung: {curse:0.000}   — 1 hieße: die Auswahl übertreibt nicht");
        }

        if (sameDay.Count > 2)
        {
            var w = sameDay.Sum(entry => entry.Weight);
            var mx = sameDay.Sum(entry => entry.Weight * entry.Counter) / w;
            var my = sameDay.Sum(entry => entry.Weight * entry.Guide) / w;
            var sameDaySlope = sameDay.Sum(entry => entry.Weight * (entry.Counter - mx) * (entry.Guide - my))
                / sameDay.Sum(entry => entry.Weight * (entry.Counter - mx) * (entry.Counter - mx));
            var absCounter = sameDay.Sum(entry => entry.Weight * Math.Abs(entry.Counter)) / w;
            var absGuide = sameDay.Sum(entry => entry.Weight * Math.Abs(entry.Guide)) / w;

            Console.WriteLine();
            Console.WriteLine($"{sameDay.Count} Paare, live am selben Tag in beiden Quellen (Counter-Liste im Bracket der Datei gegen vollständige Liste):");
            Console.WriteLine($"  Abweichung von der Erwartung: Counter-Liste {ScoreModel.AsPoints(absCounter):0.00} Punkte, vollständige Liste {ScoreModel.AsPoints(absGuide):0.00}");
            Console.WriteLine($"  Steigung vollständig auf Counter-Liste: {sameDaySlope:0.000}   — 1 hieße: die Auswahl übertreibt nicht");

            // How hard a notable-list duel has to be pulled to its expectation before it predicts
            // the unselected measurement of the same pair best. The split-half test cannot answer
            // this: both halves of a selected row share the selection.
            Console.WriteLine("  Gewicht für Counter-Listen, gemessen an der vollständigen Liste (mittlerer quadratischer Fehler):");
            var errors = PriorsCommand.Grid.Where(kappa => kappa != int.MaxValue).Select(kappa => (Kappa: kappa, Error: sameDayRaw.Sum(entry =>
            {
                var shrunk = ((entry.CounterRate * entry.CounterPlay) + (kappa * entry.Expected)) / (entry.CounterPlay + (double)kappa);
                var deviation = ScoreModel.Logit(shrunk) - ScoreModel.Logit(entry.Expected);
                return entry.GuidePlay * Math.Pow(entry.GuideDeviation - deviation, 2);
            }) / sameDayRaw.Sum(entry => (double)entry.GuidePlay))).ToList();
            var bestError = errors.MinBy(entry => entry.Error);

            foreach (var (kappa, error) in errors.Where(entry => entry.Kappa is 0 or 150 or 300 or 600 or 1000 or 1600 or 3000 or 10000 || entry.Kappa == bestError.Kappa))
                Console.WriteLine($"    κ = {kappa,6:N0}   {error * 1000:0.000}{(kappa == bestError.Kappa ? "  ← bestes" : string.Empty)}{(kappa == Shrinkage.MatchupPrior ? "  ← heute" : string.Empty)}");
        }

        if (completeRows.Count > 0)
        {
            // The vollständige rows are not a selection, so the usual split-half test applies.
            var loss = PriorsCommand.CrossValidate(completeRows, 200);
            var reference = loss[Array.IndexOf(PriorsCommand.Grid, Shrinkage.MatchupPrior)];
            var best = loss.Select((value, index) => (value, index)).MinBy(pair => pair.value).index;

            Console.WriteLine();
            Console.WriteLine($"Gewicht für vollständige Listen, Halbierungstest über {completeRows.Count} Duelle (0,000 % = κ {Shrinkage.MatchupPrior}):");

            for (var index = 0; index < PriorsCommand.Grid.Length; index++)
            {
                var kappa = PriorsCommand.Grid[index];
                if (kappa is not (0 or 50 or 100 or 150 or 300 or 600 or 1000 or 2000 or 5000 or int.MaxValue) && index != best)
                    continue;

                var label = kappa == int.MaxValue ? "∞" : kappa.ToString("N0");
                Console.WriteLine($"    κ = {label,6}   {(loss[index] - reference) / reference * 100,8:+0.000;-0.000;0.000} %{(index == best ? "  ← bestes" : string.Empty)}");
            }
        }

        return 0;
    }
}
