using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

/// <summary>Which criterion a breakdown line is about.</summary>
/// <remarks>
/// Identity is the enum, not the German label, so the wording on screen can be reworded without
/// breaking anything that looks a term up.
/// </remarks>
public enum ScoreTermKind
{
    LaneStrength,
    LaneMatchup,
    EnemyTeam,
    Synergy,
    Composition,
    BanStrength,
    BanLaneThreat,
    BanTeamThreat,
    BanPopularity,
}

/// <summary>One contribution to a champion's score, in log-odds of winning.</summary>
/// <param name="LogOdds">
/// The evidence this criterion adds, or <see langword="null"/> when the data needed to judge it is
/// missing. The distinction matters: a missing win rate and a genuinely even matchup both used to
/// show up as <c>0.00</c>, which made the breakdown unreadable.
/// <para>
/// Exception: for <see cref="ScoreTermKind.BanPopularity"/> the value is not log-odds but the
/// 0..1 gate the ban value is multiplied with — see <see cref="IsGate"/>.
/// </para>
/// </param>
public sealed record ScoreTerm(ScoreTermKind Kind, double? LogOdds)
{
    public bool HasData => LogOdds.HasValue;

    /// <summary>
    /// A multiplier term rather than additive evidence: how likely the enemy takes the champion
    /// at all. Rendered as <c>× 80 %</c> instead of points.
    /// </summary>
    public bool IsGate => Kind == ScoreTermKind.BanPopularity;

    /// <summary>The contribution in percentage points of win rate, for display.</summary>
    public double Points => IsGate ? 0 : ScoreModel.AsPoints(LogOdds ?? 0);

    /// <summary>The 0..1 gate value; only meaningful when <see cref="IsGate"/>.</summary>
    public double Gate => IsGate ? LogOdds ?? 0 : 1;

    /// <summary>Short name for the breakdown's left column.</summary>
    public string Label => Kind switch
    {
        ScoreTermKind.LaneStrength => "Lane-Stärke",
        ScoreTermKind.LaneMatchup => "Duell mit Gegner",
        ScoreTermKind.EnemyTeam => "Übrige Gegner",
        ScoreTermKind.Synergy => "Synergie im Team",
        ScoreTermKind.Composition => "Team-Aufstellung",
        ScoreTermKind.BanStrength => "Stärke allgemein",
        ScoreTermKind.BanLaneThreat => "Gefahr auf Lane",
        ScoreTermKind.BanTeamThreat => "Gefahr fürs Team",
        ScoreTermKind.BanPopularity => "Wie oft genommen",
        _ => Kind.ToString(),
    };

    /// <summary>One sentence saying what the criterion actually measures; shown as a tooltip.</summary>
    public string Hint => Kind switch
    {
        ScoreTermKind.LaneStrength =>
            "Wie gut der Champion auf dieser Lane allgemein läuft — Siegquote und OP.GG-Tier (S bis D).",
        ScoreTermKind.LaneMatchup =>
            "Siegquote direkt gegen den Champion, den wir auf deiner Lane erwarten.",
        ScoreTermKind.EnemyTeam =>
            "Siegquote gegen die übrigen aufgedeckten Gegner; zählt gedämpft, weil sie nicht auf deiner Lane stehen.",
        ScoreTermKind.Synergy =>
            "Siegquote zusammen mit den Mitspielern, die schon gepickt haben.",
        ScoreTermKind.Composition =>
            "Ob der Pick füllt, was dem Team fehlt — Schadensart, Frontline, Engage, CC. Der einzige Punkt ohne Winrate-Grundlage, deshalb bewusst klein gehalten.",
        ScoreTermKind.BanStrength =>
            "Wie stark der Champion auf seiner besten Lane gerade ist.",
        ScoreTermKind.BanLaneThreat =>
            "Wie stark der Champion genau auf deiner Lane wäre — dort trifft dich ein Nicht-Bann direkt.",
        ScoreTermKind.BanTeamThreat =>
            "Siegquote des Champions gegen die Mitspieler, die schon gepickt haben.",
        ScoreTermKind.BanPopularity =>
            "Wie wahrscheinlich der Gegner den Champion überhaupt nimmt — Pick- und Banrate. Ein Bann auf einen Champion, den niemand spielt, ist verschenkt.",
        _ => string.Empty,
    };
}

/// <summary>One entry in the recommendation list.</summary>
/// <param name="Score">
/// For picks: the estimated win rate of the line-up with this champion, 0..1. For bans: the
/// expected win-rate points the ban denies the enemy (popularity × edge), roughly 0..8.
/// </param>
public sealed record Recommendation(
    int ChampionId,
    string Name,
    double Score,
    IReadOnlyList<Reason> Reasons,
    IReadOnlyList<ScoreTerm> Breakdown);

/// <summary>The full answer for one seat.</summary>
public sealed record RecommendationSet(
    long CellId,
    Lane Lane,
    TurnAction Action,
    IReadOnlyList<Recommendation> Items,
    CompProfile AllyComp,
    CompProfile EnemyComp)
{
    public static RecommendationSet Empty { get; } =
        new(-1, Lane.Unknown, TurnAction.None, [], CompProfile.Empty, CompProfile.Empty);
}

/// <summary>
/// Scores every available champion for one seat and explains the result.
/// <para>
/// Every piece of evidence is a shift in log-odds of winning (see <see cref="ScoreModel"/>); the
/// shifts add and the sum maps back to an estimated win rate. The rates arriving here are already
/// shrunk towards 50 % by sample size, so thin data fades out by itself — no second confidence
/// factor, no clamps, no per-criterion weights to tune.
/// </para>
/// <para>
/// The engine takes a seat as input rather than assuming it is the local player, which is what
/// lets the same code advise a team-mate who is on the clock.
/// </para>
/// </summary>
public sealed class Recommender(MetaLookup meta, TraitTable traits)
{
    private readonly MetaLookup _meta = meta;
    private readonly CompAnalyzer _comp = new(meta, traits);

    /// <param name="state">Current draft.</param>
    /// <param name="target">The seat to advise.</param>
    /// <param name="enemyLanes">Predicted enemy lanes, used to find the direct opponent.</param>
    /// <param name="selectable">
    /// Champions the seat may actually take, or <see langword="null"/> to allow anything. Only ever
    /// available for the local player; the client does not expose what team-mates own.
    /// </param>
    /// <param name="allyLanes">
    /// Predicted lanes for the own team; how the advised seat's lane is resolved when the client
    /// assigned none (blind pick, customs). The enemy prediction cannot answer that — it only
    /// covers enemy seats.
    /// </param>
    /// <param name="limit">How many entries to return.</param>
    public RecommendationSet Recommend(
        DraftState state,
        RecommendationTarget target,
        LanePredictionResult enemyLanes,
        LanePredictionResult? allyLanes = null,
        IReadOnlySet<int>? selectable = null,
        int limit = 10)
    {
        if (!state.IsActive || _meta.IsEmpty)
            return RecommendationSet.Empty;

        var lane = ResolveLane(target, enemyLanes, allyLanes);
        var allyChampions = state.Allies
            .Where(slot => slot.CellId != target.Slot.CellId)
            .Select(slot => slot.EffectiveChampionId)
            .Where(id => id != 0)
            .ToList();

        var enemyChampions = state.Enemies.Select(slot => slot.EffectiveChampionId).Where(id => id != 0).ToList();

        // What an ally is hovering is as good as taken — recommending it invites a pick
        // collision, and banning it would be self-sabotage. For PICKS the advised seat's own
        // hover stays recommendable (this list is where that hover came from); for BANS it is
        // excluded too — suggesting someone bans their own declared pick would be absurd.
        var hoveredByOthers = state.Allies
            .Where(slot => !slot.IsLocked && slot.HoverChampionId != 0)
            .Where(slot => target.Action == TurnAction.Ban || slot.CellId != target.Slot.CellId)
            .Select(slot => slot.HoverChampionId)
            .ToHashSet();

        var allyComp = _comp.Analyze(allyChampions);
        var enemyComp = _comp.Analyze(enemyChampions);

        var items = target.Action == TurnAction.Ban
            ? ScoreBans(state, lane, allyChampions, hoveredByOthers, limit)
            : ScorePicks(state, lane, allyChampions, hoveredByOthers, enemyLanes, allyComp, selectable, limit);

        return new RecommendationSet(target.Slot.CellId, lane, target.Action, items, allyComp, enemyComp);
    }

    /// <summary>
    /// The lane to score for: what the client assigned, else what we predicted for that seat —
    /// looked up in the prediction that actually contains the seat. The advised seat is an ally,
    /// so asking the enemy prediction for it always came back empty.
    /// </summary>
    private static Lane ResolveLane(RecommendationTarget target, LanePredictionResult enemyLanes, LanePredictionResult? allyLanes)
    {
        if (target.Slot.AssignedLane != Lane.Unknown)
            return target.Slot.AssignedLane;

        var predictions = target.Slot.IsAlly ? allyLanes : enemyLanes;
        return predictions?.ForCell(target.Slot.CellId)?.Lane ?? Lane.Unknown;
    }

    private List<Recommendation> ScorePicks(
        DraftState state,
        Lane lane,
        List<int> allyChampions,
        IReadOnlySet<int> hoveredByOthers,
        LanePredictionResult enemyLanes,
        CompProfile allyComp,
        IReadOnlySet<int>? selectable,
        int limit)
    {
        var results = new List<Recommendation>();

        foreach (var candidate in Candidates(lane))
        {
            if (state.Unavailable.Contains(candidate) || hoveredByOthers.Contains(candidate))
                continue;

            if (selectable is not null && !selectable.Contains(candidate))
                continue;

            var reasons = new List<Reason>();

            // Without a lane — custom games, blind pick — the lane-bound base is null for everyone
            // and the list degenerates into an alphabet. Strength on the champion's own best lane
            // is the honest substitute.
            var baseTerm = lane == Lane.Unknown
                ? BestLaneLogOdds(candidate, lane, reasons)
                : LaneLogOdds(candidate, lane, reasons);

            var compFit = _comp.Fit(candidate, allyComp, reasons);

            var terms = new List<ScoreTerm>(5)
            {
                new(ScoreTermKind.LaneStrength, baseTerm),
                new(ScoreTermKind.LaneMatchup, LaneMatchupLogOdds(candidate, lane, enemyLanes, reasons)),
                new(ScoreTermKind.EnemyTeam, OffLaneMatchupLogOdds(candidate, lane, enemyLanes, reasons)),
                new(ScoreTermKind.Synergy, SynergyLogOdds(candidate, allyChampions, reasons)),
                new(ScoreTermKind.Composition, compFit is null ? null : ScoreModel.CompScale * compFit),
            };

            var total = terms.Sum(term => term.LogOdds ?? 0);

            results.Add(new Recommendation(
                candidate,
                _meta.ChampionName(candidate),
                ScoreModel.Sigmoid(total),
                Dedupe(reasons),
                terms));
        }

        return Top(results, limit);
    }

    /// <summary>
    /// Bans are valued as the win-rate edge the enemy would gain, times how likely they take the
    /// champion at all: <c>popularity × (ŵ − 0,5)</c>, in percentage points. A terrifying champion
    /// nobody plays is a wasted ban; a mediocre one they always take is not worth one either.
    /// </summary>
    private List<Recommendation> ScoreBans(
        DraftState state,
        Lane lane,
        List<int> allyChampions,
        IReadOnlySet<int> hoveredByOthers,
        int limit)
    {
        var results = new List<Recommendation>();

        foreach (var candidate in Candidates(Lane.Unknown))
        {
            if (state.Unavailable.Contains(candidate) || hoveredByOthers.Contains(candidate))
                continue;

            var reasons = new List<Reason>();
            var terms = new List<ScoreTerm>(4)
            {
                new(ScoreTermKind.BanStrength, BestLaneLogOdds(candidate, lane, reasons)),
                new(ScoreTermKind.BanLaneThreat, LaneThreatLogOdds(candidate, lane, reasons)),
                new(ScoreTermKind.BanTeamThreat, ThreatToAlliesLogOdds(candidate, allyChampions, reasons)),
                new(ScoreTermKind.BanPopularity, PopularityGate(candidate, reasons)),
            };

            var threat = terms.Where(term => !term.IsGate).Sum(term => term.LogOdds ?? 0);
            var gate = terms[^1].HasData ? terms[^1].Gate : 0;
            var deniedPoints = gate * (ScoreModel.Sigmoid(threat) - 0.5) * 100;

            results.Add(new Recommendation(
                candidate,
                _meta.ChampionName(candidate),
                deniedPoints,
                Dedupe(reasons),
                terms));
        }

        return Top(results, limit);
    }

    /// <summary>Two terms can legitimately notice the same fact; the user should read it once.</summary>
    private static IReadOnlyList<Reason> Dedupe(List<Reason> reasons)
        => [.. reasons.DistinctBy(reason => reason.Text, StringComparer.Ordinal)];

    /// <summary>Champions worth considering: the lane's roster, or everything when the lane is unclear.</summary>
    private IEnumerable<int> Candidates(Lane lane)
    {
        if (lane != Lane.Unknown)
        {
            var roster = _meta.Roster(lane);
            if (roster.Count > 0)
                return roster;
        }

        return _meta.Champions.Select(champion => champion.Id);
    }

    private static List<Recommendation> Top(List<Recommendation> results, int limit)
        => [.. results
            .OrderByDescending(item => item.Score)
            // Stable order so equal scores do not shuffle between events.
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            // Max(0), not Max(1): RecommendationCount is user-editable JSON, and a zero should
            // mean an empty list, not a surprise single row.
            .Take(Math.Max(0, limit))];

    /// <summary>
    /// The base term: how the champion does on this lane regardless of opponents. The shrunk win
    /// rate carries the strength; the OP.GG tier adds the small extra its pick/ban blend knows.
    /// </summary>
    private double? LaneLogOdds(int championId, Lane lane, ICollection<Reason> reasons)
    {
        if (_meta.LaneStat(championId, lane) is not { } stat)
            return null;

        var logOdds = ScoreModel.Logit(stat.WinRate) + TierNudge(stat.Tier);

        if (stat.Tier is 1 or 2)
        {
            reasons.Add(Reason.Pro(
                $"{ScoreModel.TierName(stat.Tier)} auf {lane.Display()}",
                $"OP.GGs Tierliste für {lane.Display()}, von S (stärkste) bis D (schwächste). "
                + $"Siegquote: {stat.WinRate:P1} über {stat.Play:N0} Spiele."));
        }
        else if (stat.WinRateDelta > 0.015)
        {
            reasons.Add(Reason.Pro(
                $"{stat.WinRate:P1} WR auf {lane.Display()}",
                $"Aus {stat.Play:N0} Spielen im aktuellen Patch. 50 % wäre Durchschnitt."));
        }

        return logOdds;
    }

    private static double TierNudge(int tier)
        => tier is >= 1 and <= 5 ? ScoreModel.TierNudge * (3 - tier) : 0;

    /// <summary>
    /// Strength on whichever lane suits the champion best; used for bans and unknown lanes. The
    /// chip names that lane only when it differs from the seat's own.
    /// </summary>
    private double? BestLaneLogOdds(int championId, Lane seatLane, ICollection<Reason> reasons)
    {
        double? best = null;
        var bestLane = Lane.Unknown;

        foreach (var lane in Lanes.All)
        {
            if (_meta.LaneStat(championId, lane) is not { } stat)
                continue;

            var logOdds = ScoreModel.Logit(stat.WinRate) + TierNudge(stat.Tier);

            if (best is not null && logOdds <= best)
                continue;

            best = logOdds;
            bestLane = lane;
        }

        // 0.08 log-odds ≈ +2 percentage points: strong enough to be worth a chip.
        if (bestLane != Lane.Unknown && bestLane != seatLane && best > 0.08)
        {
            reasons.Add(Reason.Pro(
                $"stark auf {bestLane.Display()}",
                $"Gemessen an Siegquote und OP.GG-Tier ist {bestLane.Display()} die Lane, auf der "
                + "dieser Champion gerade am gefährlichsten ist."));
        }

        return best;
    }

    /// <summary>
    /// The direct duel, with every enemy weighted by how likely they actually play this lane. A
    /// 50/50 flex pick counts half on each of its plausible lanes, instead of fully on the single
    /// most likely one — which used to make the advice flip-flop on flex champions.
    /// </summary>
    private double? LaneMatchupLogOdds(int championId, Lane lane, LanePredictionResult enemyLanes, ICollection<Reason> reasons)
    {
        if (lane == Lane.Unknown)
            return null;

        var total = 0.0;
        var counted = 0;
        var bestProbability = 0.0;
        MatchupView? bestMatchup = null;
        var bestOpponent = 0;

        foreach (var prediction in enemyLanes.Predictions)
        {
            if (prediction.ChampionId == 0)
                continue;

            var probability = ProbabilityOnLane(prediction, lane);
            if (probability < 0.05)
                continue;

            if (_meta.Matchup(championId, prediction.ChampionId, lane) is not { } matchup)
                continue;

            counted++;

            // No extra confidence factor: the win rate is already shrunk by its sample size, and
            // multiplying a second damping on top made thin data vanish entirely.
            total += probability * ScoreModel.Logit(matchup.WinRate);

            if (probability > bestProbability)
            {
                bestProbability = probability;
                bestMatchup = matchup;
                bestOpponent = prediction.ChampionId;
            }
        }

        // The chip names only the most probable opponent; a list of weighted terms is the
        // breakdown's job. Only claim an edge when the sample is worth mentioning.
        if (bestMatchup is { } best && best.Confidence > 0.2 && Math.Abs(best.WinRateDelta) > 0.01)
        {
            var opponent = _meta.ChampionName(bestOpponent);

            // The absolute win rate, not the delta from even: "59 % gegen Vayne" is the number as
            // it would be read out loud.
            var uncertain = bestProbability < 0.6;
            reasons.Add(new Reason(
                $"{best.WinRate:P0} WR gegen {opponent}{(uncertain ? " (falls Lane-Gegner)" : string.Empty)}",
                best.WinRateDelta > 0 ? ReasonTone.Pro : ReasonTone.Contra,
                DescribeMatchup(best, opponent, lane, bestProbability)));
        }

        return counted == 0 ? null : total;
    }

    /// <summary>
    /// The whole story behind a matchup chip: where the number comes from, how solid it is, and why
    /// it might count only partially. Everything the short chip text has to leave out.
    /// </summary>
    private static string DescribeMatchup(MatchupView view, string opponent, Lane lane, double probability)
    {
        var text = $"Von den ausgewerteten Spielen auf {lane.Display()} gewinnt dieser Champion "
            + $"{view.WinRate:P1} gegen {opponent}. 50 % wäre ausgeglichen.";

        text += view.Play > 0
            ? $"\n\nDatenlage: {view.Play:N0} Spiele."
            : "\n\nDatenlage: sehr dünn, entsprechend vorsichtig gewichtet.";

        if (view.IsLive)
            text += " Gerade für diesen Draft von OP.GG geholt.";

        if (view.IsInferred)
            text += $" Abgeleitet aus der Gegenrichtung — gemessen wurde {opponent} gegen diesen Champion.";

        if (probability < 0.6)
        {
            text += $"\n\n{opponent} steht nur mit {probability:P0} Wahrscheinlichkeit auf dieser Lane, "
                + "deshalb zählt das Duell hier nur anteilig.";
        }

        return text;
    }

    /// <summary>P(this seat ends up on the lane). Manual overrides and assigned lanes are already
    /// hard constraints inside the predictor, so the marginal is all that is needed.</summary>
    private static double ProbabilityOnLane(LanePrediction prediction, Lane lane)
    {
        var index = (int)lane;
        return index >= 0 && index < prediction.Probabilities.Length ? prediction.Probabilities[index] : 0;
    }

    /// <summary>
    /// Matchups against the rest of the enemy team, as a weighted MEAN of their duel log-odds,
    /// scaled by <see cref="ScoreModel.OffLaneShare"/>. The mean matters: as a sum, four off-lane
    /// enemies outweighed the direct lane duel (4 × 0.35 = 1.4 versus the lane term's 1.0) — the
    /// exact opposite of what this term promises everywhere it is described. Each enemy enters the
    /// mean with the share of them NOT already covered by the lane term, so nobody counts twice.
    /// </summary>
    private double? OffLaneMatchupLogOdds(
        int championId,
        Lane lane,
        LanePredictionResult enemyLanes,
        ICollection<Reason> reasons)
    {
        var total = 0.0;
        var weightSum = 0.0;
        var counted = 0;
        var favourable = 0;

        foreach (var prediction in enemyLanes.Predictions)
        {
            if (prediction.ChampionId == 0)
                continue;

            var offLane = 1 - ProbabilityOnLane(prediction, lane);
            if (offLane < 0.05)
                continue;

            var view = FindAnyLaneMatchup(championId, prediction.ChampionId);
            if (view is null)
                continue;

            counted++;
            weightSum += offLane;
            total += offLane * ScoreModel.Logit(view.Value.WinRate);

            if (view.Value.WinRateDelta > 0.01)
                favourable++;
        }

        if (counted == 0)
            return null;

        var meanLogOdds = ScoreModel.OffLaneShare * (total / weightSum);

        if (favourable >= 2)
        {
            reasons.Add(Reason.Pro(
                $"über 50 % WR gegen {favourable} von {counted} weiteren Gegnern",
                "Gegner außerhalb der eigenen Lane: gegen so viele von ihnen hat dieser Champion "
                + "eine Siegquote über 50 %. Zählt weniger als das direkte Lane-Duell."));
        }

        return meanLogOdds;
    }

    /// <summary>
    /// Matchup data is recorded per lane. When comparing champions from different lanes, take the
    /// lane where the pairing was actually observed.
    /// </summary>
    private MatchupView? FindAnyLaneMatchup(int championId, int opponentId)
    {
        MatchupView? best = null;

        foreach (var lane in Lanes.All)
        {
            if (_meta.Matchup(championId, opponentId, lane) is not { } view)
                continue;

            if (best is null || view.Confidence > best.Value.Confidence)
                best = view;
        }

        return best;
    }

    /// <summary>
    /// Duo records with the allies already locked in — the MEAN across them, damped by
    /// <see cref="ScoreModel.SynergyDamping"/>: thin data, and correlated across team-mates. As a
    /// sum, the term grew with every ally who locked, and the same champion scored visibly higher
    /// late in the draft for no reason but the draft's progress — which broke the fixed verdict
    /// thresholds ("starke Wahl" at 53 %). OP.GG's smoothed synergy tier adds the same small
    /// nudge the lane tier does.
    /// </summary>
    private double? SynergyLogOdds(int championId, List<int> allyChampions, ICollection<Reason> reasons)
    {
        var total = 0.0;
        var counted = 0;
        string? bestPartner = null;
        var bestLogOdds = 0.0;
        SynergyView bestView = default;

        foreach (var ally in allyChampions)
        {
            if (_meta.Synergy(championId, ally) is not { } synergy)
                continue;

            // Synergy tiers run 0..4 with 2 in the middle.
            var tierNudge = synergy.Tier is >= 0 and <= 4 ? ScoreModel.TierNudge * (2 - synergy.Tier) : 0;
            var logOdds = ScoreModel.Logit(synergy.WinRate) + tierNudge;

            total += logOdds;
            counted++;

            if (logOdds > bestLogOdds)
            {
                bestLogOdds = logOdds;
                bestPartner = _meta.ChampionName(ally);
                bestView = synergy;
            }
        }

        if (counted == 0)
            return null;

        if (bestPartner is not null && bestLogOdds > 0.08)
        {
            reasons.Add(Reason.Pro(
                $"spielt gut mit {bestPartner}",
                $"Zusammen mit {bestPartner} im selben Team liegt die Siegquote bei "
                + $"{bestView.WinRate:P1} aus {bestView.Play:N0} Spielen. 50 % wäre Durchschnitt."));
        }

        return ScoreModel.SynergyDamping * (total / counted);
    }

    /// <summary>
    /// For bans: extra threat when the champion is strong exactly on the lane of the player on the
    /// clock. On top of the general-strength term on purpose — a champion you personally have to
    /// face is worth more of a ban than one terrorising some other lane.
    /// </summary>
    private double? LaneThreatLogOdds(int championId, Lane lane, ICollection<Reason> reasons)
    {
        if (lane == Lane.Unknown || _meta.LaneStat(championId, lane) is not { } stat)
            return null;

        var logOdds = ScoreModel.BanLaneFocus * (ScoreModel.Logit(stat.WinRate) + TierNudge(stat.Tier));

        if (logOdds > 0.05)
        {
            var strength = stat.Tier is >= 1 and <= 5
                ? ScoreModel.TierName(stat.Tier)
                : $"{stat.WinRate:P1} WR";

            reasons.Add(Reason.Pro(
                $"{strength} auf {lane.Display()}",
                // "diese Lane", not "deine": the engine also advises team-mate seats.
                $"Genau die Lane dieses Slots: {stat.WinRate:P1} Siegquote aus "
                + $"{stat.Play:N0} Spielen. Ein Bann wirkt hier direkt."));
        }

        return logOdds;
    }

    /// <summary>For bans: how badly this champion beats the allies already locked in.</summary>
    private double? ThreatToAlliesLogOdds(int championId, List<int> allyChampions, ICollection<Reason> reasons)
    {
        var total = 0.0;
        var counted = 0;
        string? worst = null;
        var worstLogOdds = 0.0;
        MatchupView worstView = default;

        foreach (var ally in allyChampions)
        {
            var view = FindAnyLaneMatchup(championId, ally);
            if (view is null)
                continue;

            counted++;
            var logOdds = ScoreModel.Logit(view.Value.WinRate);
            total += logOdds;

            if (logOdds > worstLogOdds)
            {
                worstLogOdds = logOdds;
                worst = _meta.ChampionName(ally);
                worstView = view.Value;
            }
        }

        if (counted == 0)
            return null;

        if (worst is not null && worstLogOdds > 0.1)
        {
            reasons.Add(Reason.Pro(
                $"schlägt unseren {worst}",
                $"Im direkten Duell gewinnt dieser Champion {worstView.WinRate:P1} gegen {worst}, "
                + $"der bei uns schon gepickt ist. Aus {worstView.Play:N0} Spielen."));
        }

        // Mean, not sum, for the same reason as the synergy term: otherwise the ban value climbed
        // with every locked ally instead of with the threat itself.
        return ScoreModel.BanAllyShare * (total / counted);
    }

    /// <summary>
    /// For bans: 0..1, whether the enemy is likely to take the champion at all. Pick and ban rate
    /// are shares of all games, so a few percent is already a lot.
    /// </summary>
    private double? PopularityGate(int championId, ICollection<Reason> reasons)
    {
        double? best = null;
        var bestPickRate = 0.0;
        var bestBanRate = 0.0;

        foreach (var lane in Lanes.All)
        {
            if (_meta.LaneStat(championId, lane) is not { } stat)
                continue;

            var gate = Math.Clamp((stat.PickRate * 8) + (stat.BanRate * 4), 0, 1);
            if (best is not null && gate <= best)
                continue;

            best = gate;
            bestPickRate = stat.PickRate;
            bestBanRate = stat.BanRate;
        }

        if (best > 0.6)
        {
            reasons.Add(bestBanRate > bestPickRate
                ? Reason.Neutral(
                    $"in {bestBanRate:P0} der Spiele gebannt",
                    "So oft bannen andere diesen Champion. Ein hoher Wert heißt: viele halten ihn "
                    + "für gefährlich — aber vielleicht bannt ihn ohnehin jemand anders.")
                : Reason.Neutral(
                    $"in {bestPickRate:P0} der Spiele gepickt",
                    "So oft wird dieser Champion gespielt. Je häufiger, desto wahrscheinlicher "
                    + "nimmt ihn der Gegner, wenn du ihn nicht bannst."));
        }

        return best;
    }
}
