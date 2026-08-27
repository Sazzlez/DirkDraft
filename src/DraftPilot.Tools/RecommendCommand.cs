using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu.Models;

namespace DraftPilot.Tools;

/// <summary>
/// Runs the recommendation engine against the stored snapshot for a made-up draft. This is how the
/// scoring gets sanity-checked against real data without sitting in a queue.
/// </summary>
internal static class RecommendCommand
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

        // Both vocabularies: the OP.GG one (mid, adc, support) AND the LCU one (middle, bottom,
        // utility) — plus "bot", because that is what the UI itself prints.
        var laneArgument = args.Length > 1 ? args[1].ToLowerInvariant() : "mid";
        var lane = Lanes.FromOpGg(laneArgument);
        if (lane == Lane.Unknown)
            lane = Lanes.FromLcu(laneArgument == "bot" ? "bottom" : laneArgument);

        if (lane == Lane.Unknown)
        {
            Console.Error.WriteLine("Lane muss top, jungle, mid, adc/bot oder support sein.");
            return 2;
        }

        var enemies = Resolve(meta, args.Length > 2 ? args[2] : string.Empty, out var unknownEnemies);
        var allies = Resolve(meta, args.Length > 3 ? args[3] : string.Empty, out var unknownAllies);

        foreach (var name in unknownEnemies.Concat(unknownAllies))
            Console.Error.WriteLine($"Unbekannter Champion: {name}");

        Console.WriteLine($"Snapshot Patch {snapshot.Patch}, kuratierte Traits für {traits.Count} Champions.");
        Console.WriteLine($"Lane: {lane.Display()}   Gegner: {Names(meta, enemies)}   Team: {Names(meta, allies)}");
        Console.WriteLine();

        var state = BuildState(lane, allies, enemies);
        var target = new TurnTracker().Resolve(state);

        if (target is null)
        {
            Console.Error.WriteLine("Kein Ziel-Slot ermittelt.");
            return 1;
        }

        var predictions = new LanePredictor(meta, SeatPriors.Load()).Predict(state.Enemies);
        PrintPredictions(meta, predictions);

        var recommender = new Recommender(meta, traits);

        var set = recommender.Recommend(state, target, predictions, selectable: null, limit: 8);
        PrintRecommendations("Picks", set);

        var banTarget = target with { Action = TurnAction.Ban };
        var bans = recommender.Recommend(state, banTarget, predictions, selectable: null, limit: 8);
        PrintRecommendations("Bans", bans);

        PrintComp(bans);
        return 0;
    }

    /// <summary>
    /// Builds a session where the local player sits on the requested lane and is on the clock.
    /// Allies fill the other lanes in order; enemies occupy seats in the order given.
    /// </summary>
    private static DraftState BuildState(Lane lane, List<int> allies, List<int> enemies)
    {
        string[] positions = ["top", "jungle", "middle", "bottom", "utility"];
        var session = new ChampSelectSession
        {
            LocalPlayerCellId = (int)lane,
            Timer = new ChampSelectTimer { Phase = "BAN_PICK", AdjustedTimeLeftInPhase = 25_000 },
        };

        var allyQueue = new Queue<int>(allies);

        for (var i = 0; i < 5; i++)
        {
            // The seat we are advising stays empty; the others take the supplied champions.
            var champion = i == (int)lane ? 0 : allyQueue.Count > 0 ? allyQueue.Dequeue() : 0;
            session.MyTeam.Add(new ChampSelectPlayer { CellId = i, ChampionId = champion, AssignedPosition = positions[i], Team = 1 });
        }

        for (var i = 0; i < 5; i++)
        {
            session.TheirTeam.Add(new ChampSelectPlayer
            {
                CellId = i + 5,
                ChampionId = i < enemies.Count ? enemies[i] : 0,
                AssignedPosition = string.Empty,
                Team = 2,
            });
        }

        session.Actions.Add([
            new ChampSelectAction
            {
                Id = 1,
                ActorCellId = (int)lane,
                Type = "pick",
                IsAllyAction = true,
                IsInProgress = true,
            },
        ]);

        return DraftState.From(session);
    }

    private static List<int> Resolve(MetaLookup meta, string list, out List<string> unknown)
    {
        unknown = [];
        var result = new List<int>();

        // The resolver strips punctuation and case, so "Kaisa" finds Kai'Sa and "nunu" finds
        // Nunu & Willump — an exact-name comparison rejected exactly the names people type.
        var resolver = new DraftPilot.Meta.ChampionResolver(meta.Champions);

        foreach (var raw in list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (resolver.Resolve(raw) is { } id)
                result.Add(id);
            else
                unknown.Add(raw);
        }

        return result;
    }

    private static string Names(MetaLookup meta, List<int> ids)
        => ids.Count == 0 ? "-" : string.Join(", ", ids.Select(meta.ChampionName));

    private static void PrintPredictions(MetaLookup meta, LanePredictionResult predictions)
    {
        Console.WriteLine("Lane-Vorhersage Gegner:");

        foreach (var prediction in predictions.Predictions)
        {
            var champion = prediction.ChampionId == 0 ? "(verdeckt)" : meta.ChampionName(prediction.ChampionId);
            var flag = prediction.IsUncertain ? "  ?" : string.Empty;
            Console.WriteLine($"  cell={prediction.CellId}  {champion,-14} -> {prediction.Lane.Display(),-8} {prediction.Confidence:P0}{flag}");
        }

        Console.WriteLine();
    }

    private static void PrintRecommendations(string title, RecommendationSet set)
    {
        Console.WriteLine($"{title}  ({set.Items.Count} von {set.Lane.Display()})");

        foreach (var item in set.Items)
        {
            var reasons = item.Reasons.Count > 0 ? string.Join(" · ", item.Reasons.Select(reason => reason.Text)) : "-";

            // Picks carry an estimated win rate; bans carry denied win-rate points.
            var score = set.Action == TurnAction.Ban ? $"{item.Score,5:+0.0;-0.0} Pkt" : $"{item.Score,6:P1}";
            Console.WriteLine($"  {score}  {item.Name,-14} {reasons}");
        }

        Console.WriteLine();
    }

    private static void PrintComp(RecommendationSet set)
    {
        Console.WriteLine("Teamcomp:");
        Console.WriteLine($"  wir:  {Describe(set.AllyComp)}");
        Console.WriteLine($"  sie:  {Describe(set.EnemyComp)}");
    }

    private static string Describe(CompProfile profile)
    {
        if (profile.Count == 0)
            return "(leer)";

        var findings = profile.Findings.Count > 0
            ? string.Join(", ", profile.Findings.Select(finding => finding.Text))
            : "keine Lücken";

        return $"{profile.Count} Champions, AD {profile.PhysicalShare:P0} / AP {profile.MagicShare:P0}, "
            + $"Frontline {profile.FrontlineCount}, CC {profile.TotalCrowdControl}, "
            + $"Traits {profile.TraitCoverage:P0}  ->  {findings}";
    }
}
