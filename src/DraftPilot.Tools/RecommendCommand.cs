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
        // Pulled out before anything positional is read, so "--terms" may sit anywhere on the line
        // — including directly after the lane, where it would otherwise be parsed as the enemy list.
        var withTerms = args.Contains("--terms", StringComparer.OrdinalIgnoreCase);
        args = [.. args.Where(argument => !argument.Equals("--terms", StringComparison.OrdinalIgnoreCase))];

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
            Console.Error.WriteLine($"Unbekannter Champ: {name}");

        Console.WriteLine($"Snapshot Patch {snapshot.Patch}, kuratierte Traits für {traits.Count} Champs.");
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
        PrintRecommendations("Picks", set, withTerms);

        var banTarget = target with { Action = TurnAction.Ban };
        var bans = recommender.Recommend(state, banTarget, predictions, selectable: null, limit: 8);
        PrintRecommendations("Bans", bans, withTerms);

        PrintComp(bans);
        PrintBalance(meta, state, predictions);
        return 0;
    }

    /// <summary>
    /// The figure the window prints over the two team columns. Here because a scoring change has to
    /// be measurable on both surfaces — the list and the one number that judges the whole draft.
    /// </summary>
    private static void PrintBalance(MetaLookup meta, DraftState state, LanePredictionResult enemies)
    {
        var allies = new LanePredictor(meta, SeatPriors.Load()).Predict(state.Allies);
        var balance = DraftBalance.Estimate(meta, state, allies, enemies);

        Console.WriteLine();
        Console.WriteLine(balance.HasData
            ? $"Draft-Balance: {balance.AllyWinRate:P1} zu {balance.EnemyWinRate:P1} "
                + $"({balance.RatedChampions} bewertete Champs, {balance.ContestedLanes} umkämpfte Lanes)"
            : "Draft-Balance: — (eine Seite ist nicht aufgedeckt)");
    }

    /// <summary>
    /// Builds a session where the local player sits on the requested lane and is on the clock.
    /// Allies fill the other lanes in order; enemies occupy seats in the order given.
    /// </summary>
    internal static DraftState BuildState(Lane lane, List<int> allies, List<int> enemies)
    {
        string[] positions = ["top", "jungle", "middle", "bottom", "utility"];
        var session = new ChampSelectSession
        {
            LocalPlayerCellId = (int)lane,
            Timer = new ChampSelectTimer { Phase = "BAN_PICK" },
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

    /// <summary>
    /// Turns a comma-separated list of names into ids. A single <c>-</c> means "nobody", and it
    /// exists because an empty argument cannot be typed reliably: PowerShell drops <c>""</c> from
    /// the line, so <c>recommend top "" "Ahri,Thresh"</c> silently arrives as an ENEMY list of two
    /// and the tool answers a different question than the one asked.
    /// </summary>
    internal static List<int> Resolve(MetaLookup meta, string list, out List<string> unknown)
    {
        unknown = [];
        var result = new List<int>();

        if (list.Trim() is "-" or "–")
            return result;

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

    internal static string Names(MetaLookup meta, List<int> ids)
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

    /// <summary>
    /// The list, printed the way the window prints it — which is the point of this command: a
    /// scoring change has to be readable here before anyone looks at the UI.
    /// <para>
    /// That means the two things the bare ordering leaves out. The error bar, because a list whose
    /// gaps are smaller than its own noise is a ranking of nothing, and the decimals follow it for
    /// the same reason the window's do. And the marker <c>=</c> on every row the errors cannot
    /// separate from the leader: <see cref="ScoreError.CountLeadingTies"/> decides it, so the tool
    /// and the window cannot drift apart on the one question the list is asked.
    /// </para>
    /// </summary>
    private static void PrintRecommendations(string title, RecommendationSet set, bool withTerms)
    {
        var isBan = set.Action == TurnAction.Ban;
        var tied = isBan ? 0 : ScoreError.CountLeadingTies(set.Items);
        var decimals = isBan ? 1 : ScoreError.Decimals(set.Items);

        var head = $"{title}  ({set.Items.Count} von {set.Lane.Display()})";
        if (tied >= 2)
            head += $" — die ersten {tied} gleichauf";

        Console.WriteLine(head);

        for (var i = 0; i < set.Items.Count; i++)
        {
            var item = set.Items[i];
            var reasons = item.Reasons.Count > 0 ? string.Join(" · ", item.Reasons.Select(reason => reason.Text)) : "-";

            // Picks carry an estimated win rate; bans carry denied win-rate points and no error bar.
            var score = isBan
                ? $"{item.Score,5:+0.0;-0.0} Pkt"
                : $"{item.Score.ToString($"P{decimals}"),7} ±{ScoreError.AsPoints(item.Uncertainty),4:0.0}";

            var mark = !isBan && i < tied ? '=' : ' ';

            Console.WriteLine($" {mark}{score}  {item.Name,-14} {reasons}");

            if (withTerms)
                PrintTerms(item);
        }

        Console.WriteLine();
    }

    /// <summary>
    /// One line per criterion, the same numbers the window shows behind its chevron. Off by
    /// default: eight champions times five criteria is a page, and most runs only ask whether the
    /// order moved.
    /// </summary>
    private static void PrintTerms(Recommendation item)
    {
        foreach (var term in item.Breakdown)
        {
            var value = term switch
            {
                { HasData: false } => "keine Daten",
                { IsGate: true } => $"× {term.Gate:P0}",
                _ => $"{term.Points,+6:+0.00;-0.00;0.00}",
            };

            Console.WriteLine($"          {term.Label,-22} {value,11}");
        }
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

        return $"{profile.Count} Champs, AD {profile.PhysicalShare:P0} / AP {profile.MagicShare:P0}, "
            + $"Frontline {profile.FrontlineCount}, CC {profile.TotalCrowdControl}, "
            + $"Traits {profile.TraitCoverage:P0}  ->  {findings}";
    }
}
