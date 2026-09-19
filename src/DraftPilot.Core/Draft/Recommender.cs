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
        ScoreTermKind.LaneMatchup => "Matchup",
        ScoreTermKind.EnemyTeam => "Enemy-Team",
        ScoreTermKind.Synergy => "Synergie",
        ScoreTermKind.Composition => "Teamcomp",
        ScoreTermKind.BanStrength => "Stärke allgemein",
        ScoreTermKind.BanLaneThreat => "Gefahr auf Lane",
        ScoreTermKind.BanTeamThreat => "Gefahr fürs Team",
        ScoreTermKind.BanPopularity => "Pick- und Banrate",
        _ => Kind.ToString(),
    };

    /// <summary>One sentence saying what the criterion actually measures; shown as a tooltip.</summary>
    public string Hint => Kind switch
    {
        ScoreTermKind.LaneStrength =>
            "Wie gut der Champ auf dieser Lane allgemein läuft: die Winrate, geglättet nach "
            + "Stichprobe. OP.GGs Tier steht als eigener Hinweis daneben und zählt hier nicht mit — "
            + "es mischt Winrate und Pickrate, und die Winrate ist schon diese Zeile.",
        ScoreTermKind.LaneMatchup =>
            "Wie viel besser oder schlechter dieser Champ gegen den erwarteten Lane-Gegner "
            + "abschneidet als auf dieser Lane üblich. Die allgemeine Stärke steckt schon in der "
            + "Zeile darüber — sonst würde sie zweimal zählen.",
        ScoreTermKind.EnemyTeam =>
            "Winrate gegen die restlichen aufgedeckten Gegner — alle außer dem Lane-Gegner. Zählt gedämpft, weil sie nicht auf deiner Lane stehen.",
        ScoreTermKind.Synergy =>
            "Winrate zusammen mit den Teammates, die schon gepickt haben.",
        ScoreTermKind.Composition =>
            "Ob der Pick füllt, was dem Team fehlt und was gegen ihre Comp hilft — AD/AP, Frontline, Engage, CC. Der einzige Punkt ohne Winrate-Grundlage, deshalb bewusst klein gehalten.",
        ScoreTermKind.BanStrength =>
            "Wie stark der Champ auf seiner besten Lane gerade ist.",
        ScoreTermKind.BanLaneThreat =>
            "Wie stark der Champ genau auf deiner Lane wäre — dort trifft dich ein Nicht-Ban direkt.",
        ScoreTermKind.BanTeamThreat =>
            "Winrate des Champs gegen die Teammates, die schon gepickt haben.",
        ScoreTermKind.BanPopularity =>
            "Wie wahrscheinlich der Gegner den Champ überhaupt nimmt — Pick- und Banrate. Ein Ban auf einen Champ, den niemand spielt, ist verschenkt.",
        _ => string.Empty,
    };
}

/// <summary>One entry in the recommendation list.</summary>
/// <param name="Score">
/// For picks: the estimated win rate of the line-up with this champion, 0..1. For bans: the
/// expected win-rate points the ban denies the enemy (popularity × edge), roughly 0..8.
/// </param>
/// <param name="Uncertainty">
/// Standard error of <paramref name="Score"/> in log-odds, from the sample sizes behind the terms.
/// Zero for bans, which are not on a win-rate scale. This is what decides whether two entries may
/// honestly be shown in an order at all — see <see cref="ScoreError"/>.
/// </param>
public sealed record Recommendation(
    int ChampionId,
    string Name,
    double Score,
    IReadOnlyList<Reason> Reasons,
    IReadOnlyList<ScoreTerm> Breakdown,
    double Uncertainty = 0);

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
/// shrunk towards what the rest of the data already implies (see Shrinkage and MetaLookup), so thin
/// data fades out by itself — no second confidence
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
            ? ScoreBans(state, lane, allyChampions, hoveredByOthers, enemyLanes, limit)
            : ScorePicks(state, lane, allyChampions, hoveredByOthers, enemyLanes, allyComp, enemyComp, selectable, limit);

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
        CompProfile enemyComp,
        IReadOnlySet<int>? selectable,
        int limit)
    {
        var results = new List<Recommendation>();

        // Nobody on this lane yet: then the duel term has nothing to say, and the question the pick
        // actually faces is how easily it can be answered later. Once an opponent is revealed the
        // duel itself answers that far better, so this only runs while it cannot.
        var laneIsOpen = lane != Lane.Unknown && enemyLanes.ChampionOnLane(lane) == 0;

        // Everything the enemy can no longer reach for: bans, locked picks on both sides, and what
        // an ally is hovering — that champion is as good as taken.
        var blocked = laneIsOpen
            ? new HashSet<int>(state.Unavailable.Concat(hoveredByOthers))
            : [];

        foreach (var candidate in Candidates(lane))
        {
            if (state.Unavailable.Contains(candidate) || hoveredByOthers.Contains(candidate))
                continue;

            if (selectable is not null && !selectable.Contains(candidate))
                continue;

            var reasons = new List<Reason>();

            // One budget per candidate: the terms add up, so their sampling errors add up too.
            var budget = new ErrorBudget();

            // Without a lane — custom games, blind pick — the lane-bound base is null for everyone
            // and the list degenerates into an alphabet. Strength on the champion's own best lane
            // is the honest substitute.
            var baseTerm = lane == Lane.Unknown
                ? MainLaneLogOdds(candidate, lane, reasons, budget)
                : LaneLogOdds(candidate, lane, reasons, budget);

            var compFit = _comp.Fit(candidate, allyComp, enemyComp, reasons);

            if (laneIsOpen)
                AddCounterRisk(candidate, lane, blocked, reasons);

            var terms = new List<ScoreTerm>(5)
            {
                new(ScoreTermKind.LaneStrength, baseTerm),
                new(ScoreTermKind.LaneMatchup, LaneMatchupLogOdds(candidate, lane, enemyLanes, reasons, budget)),
                new(ScoreTermKind.EnemyTeam, OffLaneMatchupLogOdds(candidate, lane, enemyLanes, reasons, budget)),
                new(ScoreTermKind.Synergy, SynergyLogOdds(candidate, allyChampions, reasons, budget)),
                new(ScoreTermKind.Composition, compFit is null ? null : ScoreModel.CompScale * compFit),
            };

            var total = terms.Sum(term => term.LogOdds ?? 0);

            results.Add(new Recommendation(
                candidate,
                _meta.ChampionName(candidate),
                ScoreModel.Sigmoid(total),
                Dedupe(reasons),
                terms,
                budget.StandardError));
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
        LanePredictionResult enemyLanes,
        int limit)
    {
        var results = new List<Recommendation>();
        var takenByEnemy = EnemyLanesTaken(state, enemyLanes);

        foreach (var candidate in Candidates(Lane.Unknown))
        {
            if (state.Unavailable.Contains(candidate) || hoveredByOthers.Contains(candidate))
                continue;

            var reasons = new List<Reason>();

            // Bans are scored in denied points, not on a win-rate scale, so their error bar would
            // not mean the same thing. The budget is collected and dropped.
            var banBudget = new ErrorBudget();

            // One lane for both strength and popularity, so the two cannot describe different
            // champions. BanLaneThreat stays on the advised seat's lane on purpose - that double
            // counting is what BanLaneFocus is for.
            var banLane = BanLane(candidate, takenByEnemy);

            var terms = new List<ScoreTerm>(4)
            {
                new(ScoreTermKind.BanStrength, BanStrengthLogOdds(banLane, lane, reasons)),
                new(ScoreTermKind.BanLaneThreat, LaneThreatLogOdds(candidate, lane, reasons)),
                new(ScoreTermKind.BanTeamThreat, ThreatToAlliesLogOdds(candidate, allyChampions, reasons)),
                new(ScoreTermKind.BanPopularity, BanPopularity(banLane, reasons)),
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

    /// <summary>
    /// The counters this pick would still be exposed to. A chip, never a term: whether an available
    /// counter actually gets picked is not in the data, and pretending otherwise would put a guess
    /// into a number that calls itself an estimated win rate.
    /// <para>
    /// Absence of the chip means "the source lists none that stand out", not "there are none" — the
    /// tooltip says so, because OP.GG only ever lists the notable opponents of a champion.
    /// </para>
    /// </summary>
    private void AddCounterRisk(int candidate, Lane lane, IReadOnlySet<int> blocked, ICollection<Reason> reasons)
    {
        var threats = CounterRisk.Open(_meta, candidate, lane, blocked, limit: 0);
        if (threats.Count == 0)
            return;

        var named = string.Join(", ", threats.Take(3).Select(threat =>
            $"{_meta.ChampionName(threat.ChampionId)} {threat.WinRate:P0} aus {threat.Play:N0} Games"));

        var more = threats.Count > 3 ? $" und {threats.Count - 3} weitere" : string.Empty;

        reasons.Add(Reason.Contra(
            threats.Count == 1 ? "1 offener Counter" : $"{threats.Count} offene Counter",
            $"Auf {lane.Display()} ist noch niemand aufgedeckt, und diese Champs sind noch frei "
            + $"und schneiden gegen {_meta.ChampionName(candidate)} besser ab als seine Gegner "
            + $"üblicherweise: {named}{more}. Sie zu nehmen steht dem Gegner frei — ob er es tut, "
            + "sagen die Daten nicht, deshalb zählt das hier nicht in die Prozentzahl hinein. "
            + "Aufgelistet ist, was OP.GG als auffällige Gegner kennt, nicht jeder mögliche."));
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
    /// rate carries it, and nothing else does.
    /// <para>
    /// The OP.GG tier used to be added on top, worth 0,04 log-odds per step. Measured on the stored
    /// Gold snapshot (273 lane rows), that was a mistake twice over. It duplicates: the tier
    /// explains 46,8 % of the variance of the very win rate it is added to, and its span across the
    /// ladder — 0,16 log-odds from tier 0 to tier 5 — is the same size as the win-rate span it
    /// duplicates (tier 1 averages 51,7 %, tier 5 averages 47,8 %; 0,156 log-odds). And it smuggles
    /// in popularity: the tier explains 45,2 % of the variance of the PICK rate, so a strong but
    /// rarely played champion was docked points for being rare — in a tool whose whole point is the
    /// best pick for this game, not the most common one.
    /// </para>
    /// <para>
    /// The tier stays on screen as its own chip. It is OP.GG's judgement and worth seeing; it is
    /// simply not a second measurement, and it cannot carry an error bar the way every other term
    /// in this score does.
    /// </para>
    /// </summary>
    private double? LaneLogOdds(int championId, Lane lane, ICollection<Reason> reasons, ErrorBudget budget)
    {
        if (_meta.LaneStat(championId, lane) is not { } stat)
            return null;

        var logOdds = ScoreModel.Logit(stat.WinRate);

        budget.Add(ScoreError.LogitVariance(stat.WinRate, stat.Play, Shrinkage.LanePrior, _meta.LaneTarget));

        if (stat.WinRateDelta > 0.015)
        {
            reasons.Add(Reason.Pro(
                $"{stat.WinRate:P1} WR auf {lane.Display()}",
                $"Aus {stat.Play:N0} Games im aktuellen Patch. 50 % wäre Durchschnitt."));
        }

        // Neutral, because it no longer moves the number: context beside the score, not a part of
        // it. Tier 0 is OP.GG's OP tier, one step above S, and -1 means the row has no tier at all.
        if (stat.Tier is >= 0 and <= 2)
        {
            reasons.Add(Reason.Neutral(
                $"{ScoreModel.TierName(stat.Tier)} auf {lane.Display()}",
                $"OP.GGs eigene Einstufung für {lane.Display()}, von OP (stärkste) über S bis D "
                + "(schwächste). Zählt nicht in die Prozentzahl: sie mischt Winrate und "
                + "Pickrate, und die Winrate steckt schon drin."));
        }

        return logOdds;
    }
    /// <summary>
    /// How strong the candidate is on the lane the enemy would play them. Names that lane in a chip
    /// only when it differs from the advised seat's own — otherwise the threat term below says the
    /// same thing about the same lane.
    /// </summary>
    private double? BanStrengthLogOdds((Lane Lane, LaneView Stat, double Gate)? banLane, Lane seatLane, ICollection<Reason> reasons)
    {
        if (banLane is not { } best)
            return null;

        var logOdds = ScoreModel.Logit(best.Stat.WinRate);

        // 0.08 log-odds ≈ +2 percentage points: strong enough to be worth a chip.
        if (best.Lane != seatLane && logOdds > 0.08)
        {
            reasons.Add(Reason.Pro(
                $"stark auf {best.Lane.Display()}",
                $"{best.Lane.Display()} ist die Lane, auf der dieser Champ gerade am "
                + $"gefährlichsten ist und auf der ihn der Gegner am ehesten spielt: "
                + $"{best.Stat.WinRate:P1} Winrate aus {best.Stat.Play:N0} Games."));
        }

        return logOdds;
    }

    /// <summary>
    /// How likely the enemy takes the champion at all, read from the same lane the strength was.
    /// A terrifying champion nobody plays is a wasted ban.
    /// </summary>
    private static double? BanPopularity((Lane Lane, LaneView Stat, double Gate)? banLane, ICollection<Reason> reasons)
    {
        if (banLane is not { } best)
            return null;

        if (best.Gate > 0.6)
        {
            reasons.Add(best.Stat.BanRate > best.Stat.PickRate
                ? Reason.Neutral(
                    $"in {best.Stat.BanRate:P0} der Games gebannt",
                    "So oft bannen andere diesen Champ. Ein hoher Wert heißt: viele halten ihn "
                    + "für gefährlich — aber vielleicht bannt ihn ohnehin jemand anders.")
                : Reason.Neutral(
                    $"in {best.Stat.PickRate:P0} der Games gepickt",
                    "So oft wird dieser Champ gespielt. Je häufiger, desto wahrscheinlicher "
                    + "nimmt ihn der Gegner, wenn du ihn nicht bannst."));
        }

        return best.Gate;
    }


    /// <summary>
    /// The one lane a ban candidate is judged on: where the enemy would most plausibly play them,
    /// given what their draft already shows.
    /// <para>
    /// The three ban terms used to pick their own lane independently — strength from the champion's
    /// best lane, popularity from whichever lane had the highest pick and ban rate, threat from the
    /// advised seat's lane. That let a mid ban justify itself with "stark auf Bot" while its value
    /// came from a third lane's popularity. Strength and popularity now read the same row.
    /// </para>
    /// <para>
    /// Selected by role rate rather than by the gate: the gate saturates at 1 for 36 of 276 rows —
    /// every champion popular enough to be worth banning — and then cannot tell two lanes apart at
    /// all. Role rate is the share of this champion's games played on that lane, which is exactly
    /// the question being asked (Viktor: Mid 70 %, Bot 27 %).
    /// </para>
    /// <para>
    /// A lane an enemy has already locked is discounted by how sure we are of that read, not
    /// excluded outright. They cannot field a second champion there, so by the second ban round a
    /// jungle-only champion is nearly worthless to ban when their jungler is in — but our lane
    /// prediction can be wrong, and its own confidence is the honest size of that doubt. No new
    /// constant: both factors are measured.
    /// </para>
    /// </summary>
    private (Lane Lane, LaneView Stat, double Gate)? BanLane(int championId, IReadOnlyDictionary<Lane, double> takenByEnemy)
    {
        (Lane Lane, LaneView Stat, double Gate)? best = null;
        var bestPlausibility = -1.0;

        foreach (var lane in Lanes.All)
        {
            if (_meta.LaneStat(championId, lane) is not { } stat)
                continue;

            // How much room the enemy still has for this champion here: everything when the lane is
            // open, only our doubt about the read when it is taken.
            var room = takenByEnemy.TryGetValue(lane, out var confidence) ? 1 - confidence : 1;

            var plausibility = Math.Max(0, stat.RoleRate) * room;
            if (plausibility <= bestPlausibility)
                continue;

            // Pick rate answers "how often is this champion played here", ban rate "how often do
            // others fear them here"; both say how likely the enemy reaches for them at all.
            var gate = Math.Clamp((stat.PickRate * 8) + (stat.BanRate * 4), 0, 1) * room;

            bestPlausibility = plausibility;
            best = (lane, stat, gate);
        }

        return best;
    }

    /// <summary>
    /// Which lanes the enemy has already filled, and how sure we are of each read. Only locked
    /// champions count — a hover can still change — and the confidence travels with the lane so the
    /// caller can discount rather than exclude.
    /// </summary>
    private static Dictionary<Lane, double> EnemyLanesTaken(DraftState state, LanePredictionResult enemyLanes)
    {
        var taken = new Dictionary<Lane, double>();

        foreach (var slot in state.Enemies)
        {
            if (!slot.IsLocked)
                continue;

            if (enemyLanes.ForCell(slot.CellId) is not { Lane: not Lane.Unknown } prediction)
                continue;

            var confidence = Math.Clamp(prediction.Confidence, 0, 1);

            // Two locked enemies read onto the same lane means one read is wrong; keep the surer.
            if (!taken.TryGetValue(prediction.Lane, out var known) || confidence > known)
                taken[prediction.Lane] = confidence;
        }

        return taken;
    }

    /// <summary>
    /// Strength on the lane the champion is actually played on; the stand-in for the lane term when
    /// the seat has no lane at all (custom games, blind pick without an assignment).
    /// <para>
    /// This used to take the HIGHEST win rate of the champion's five rows. That is a maximum over
    /// noisy estimates: with five draws to choose from, the winner tends to be the luckiest sample
    /// rather than the best lane, and the chip could name a lane the champion plays in 3 % of its
    /// games. It also left the codebase answering one question three ways — <see cref="BanLane"/>
    /// by role rate, a lookup helper by play count, this one by win rate. Role rate and play count
    /// are the same ordering for a single champion (the rate is that count over the champion's own
    /// total), so two of the three always agreed; the third is gone, and what remains is the one
    /// implementation in <see cref="MetaLookup.MainLane"/>.
    /// </para>
    /// </summary>
    private double? MainLaneLogOdds(int championId, Lane seatLane, ICollection<Reason> reasons, ErrorBudget budget)
    {
        // One implementation of "where does this champion belong", in MetaLookup, because the build
        // fetch needs the same answer to ask OP.GG for a position. Two loops that agree today are
        // two loops that can stop agreeing, and the disagreement would be invisible: the score
        // would describe one lane while the build describes another.
        var mainLane = _meta.MainLane(championId);

        if (mainLane == Lane.Unknown || _meta.LaneStat(championId, mainLane) is not { } main)
            return null;

        budget.Add(ScoreError.LogitVariance(main.WinRate, main.Play, Shrinkage.LanePrior, _meta.LaneTarget));

        var logOdds = ScoreModel.Logit(main.WinRate);

        // 0.08 log-odds ≈ +2 percentage points: strong enough to be worth a chip.
        if (mainLane != seatLane && logOdds > 0.08)
        {
            reasons.Add(Reason.Pro(
                $"stark auf {mainLane.Display()}",
                $"{mainLane.Display()} ist die Lane, auf der dieser Champ meistens gespielt wird: "
                + $"{main.WinRate:P1} Winrate aus {main.Play:N0} Games."));
        }

        return logOdds;
    }

    /// <summary>
    /// The direct duel, with every enemy weighted by how likely they actually play this lane. A
    /// 50/50 flex pick counts half on each of its plausible lanes, instead of fully on the single
    /// most likely one — which used to make the advice flip-flop on flex champions.
    /// </summary>
    private double? LaneMatchupLogOdds(int championId, Lane lane, LanePredictionResult enemyLanes, ICollection<Reason> reasons, ErrorBudget budget)
    {
        if (lane == Lane.Unknown)
            return null;

        // Centred, because the champion's general strength is already in the lane term two lines
        // up. Measured on the stored edges: the duel rate tracks the lane rate with a slope of
        // 1.23, so adding both counted a champion's general strength about 2.2 times over — 1.8
        // points for a champion one standard deviation above the middle, the same order as the gaps
        // this list is sorted by.
        //
        // The baseline is per opponent, and it is the same number the rate was shrunk towards (see
        // MetaLookup.MatchupBaseline). That is what makes the term mean one thing only: a thin edge
        // and a missing edge both contribute zero, and a measured one contributes exactly how much
        // it beats what the two lane rates already said. Centring on the own rate alone left a thin
        // edge worth minus the champion's own strength — a penalty for being good, handed out
        // wherever OP.GG's sample happened to be small.
        var total = 0.0;
        var variance = 0.0;
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

            var baseline = _meta.MatchupBaseline(championId, prediction.ChampionId, lane);

            // Two sources, because the term is a difference of two measured things: the duel's own
            // rate, and the OPPONENT's lane rate inside the baseline, which nothing else in the sum
            // books. (The candidate's own rate is in there too, but with the opposite sign to the
            // lane term, so those cancel rather than add.) Worth about 0,01 points in Tools --
            // noise, because a lane rate rests on tens of thousands of games; it is here because a
            // term that subtracts a measured number and books none of its error is simply wrong,
            // not because the bar moved. The bar still reads narrower than resampling on rows with
            // many terms (1,30 against 1,56 for Irelia) — that gap is older than this and lives in
            // what resampling also varies: which enemy the predictor puts on which lane.
            variance += probability * probability
                * (ScoreError.LogitVariance(
                        matchup.WinRate,
                        matchup.Play,
                        Shrinkage.MatchupPrior,
                        ScoreModel.Sigmoid(baseline))
                    + OpponentLaneVariance(prediction.ChampionId, lane));

            // No extra confidence factor: the win rate is already shrunk by its sample size, and
            // multiplying a second damping on top made thin data vanish entirely.
            total += probability * (ScoreModel.Logit(matchup.WinRate) - baseline);

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

        if (counted == 0)
            return null;

        budget.Add(variance);
        return total;
    }

    /// <summary>
    /// The whole story behind a matchup chip: where the number comes from, how solid it is, and why
    /// it might count only partially. Everything the short chip text has to leave out.
    /// </summary>
    private static string DescribeMatchup(MatchupView view, string opponent, Lane lane, double probability)
    {
        var text = $"Von den ausgewerteten Games auf {lane.Display()} gewinnt dieser Champ "
            + $"{view.WinRate:P1} gegen {opponent}. 50 % wäre ausgeglichen.";

        text += view.Play > 0
            ? $"\n\nDatenlage: {view.Play:N0} Games."
            : "\n\nDatenlage: sehr dünn, entsprechend vorsichtig gewichtet.";

        if (view.IsLive)
            text += " Gerade für diesen Draft von OP.GG geholt.";

        if (view.IsInferred)
            text += $" Abgeleitet aus der Gegenrichtung — gemessen wurde {opponent} gegen diesen Champ.";

        if (probability < 0.6)
        {
            text += $"\n\n{opponent} steht nur mit {probability:P0} Wahrscheinlichkeit auf dieser Lane, "
                + "deshalb zählt das Matchup hier nur anteilig.";
        }

        return text;
    }

    /// <summary>
    /// The error the opponent's own lane rate contributes to a centred duel term. Zero when there
    /// is no row for them — the baseline then falls back to a constant, which carries no error.
    /// </summary>
    private double OpponentLaneVariance(int opponentId, Lane lane)
        => _meta.LaneStat(opponentId, lane) is { } stat
            ? ScoreError.LogitVariance(stat.WinRate, stat.Play, Shrinkage.LanePrior, _meta.LaneTarget)
            : 0;

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
    /// <para>
    /// Centred on the pair, exactly like the direct duel: averaged over several opponents, a duel
    /// log-odds IS the champion's general strength, which the lane term already carries. Uncentred,
    /// a candidate with edges banked that strength a second time at 0.35 weight while a candidate
    /// without edges banked nothing at all — and which candidates have edges is decided by whom
    /// OP.GG lists as a notable counter, not by how good the pick is.
    /// </para>
    /// </summary>
    private double? OffLaneMatchupLogOdds(
        int championId,
        Lane lane,
        LanePredictionResult enemyLanes,
        ICollection<Reason> reasons,
        ErrorBudget budget)
    {
        var total = 0.0;
        var weightSum = 0.0;
        var variance = 0.0;
        var counted = 0;
        var favourable = 0;

        foreach (var prediction in enemyLanes.Predictions)
        {
            if (prediction.ChampionId == 0)
                continue;

            var offLane = 1 - ProbabilityOnLane(prediction, lane);
            if (offLane < 0.05)
                continue;

            if (FindAnyLaneMatchup(championId, prediction.ChampionId) is not { } found)
                continue;

            var view = found.View;

            // Measured against the pair on the lane the edge was measured on — which need not be
            // this seat's lane at all, and whose reference is therefore not this seat's rate.
            var baseline = _meta.MatchupBaseline(championId, prediction.ChampionId, found.Lane);

            counted++;
            weightSum += offLane;
            total += offLane * (ScoreModel.Logit(view.WinRate) - baseline);
            variance += offLane * offLane
                * (ScoreError.LogitVariance(
                        view.WinRate,
                        view.Play,
                        Shrinkage.MatchupPrior,
                        ScoreModel.Sigmoid(baseline))
                    + OpponentLaneVariance(prediction.ChampionId, found.Lane));

            if (view.WinRateDelta > 0.01)
                favourable++;
        }

        if (counted == 0)
            return null;

        var meanLogOdds = ScoreModel.OffLaneShare * (total / weightSum);

        // The term is a weighted mean, so its error shrinks with the same divisor the mean uses.
        var scale = ScoreModel.OffLaneShare / weightSum;
        budget.Add(scale * scale * variance);

        if (favourable >= 2)
        {
            reasons.Add(Reason.Pro(
                $"über 50 % WR gegen {favourable} von {counted} weiteren Gegnern",
                "Gegner außerhalb der eigenen Lane: gegen so viele von ihnen hat dieser Champ "
                + "eine Winrate über 50 %. Zählt weniger als das direkte Lane-Matchup."));
        }

        return meanLogOdds;
    }

    /// <summary>
    /// Matchup data is recorded per lane. When comparing champions from different lanes, take the
    /// lane where the pairing was actually observed.
    /// </summary>
    private (Lane Lane, MatchupView View)? FindAnyLaneMatchup(int championId, int opponentId)
    {
        (Lane Lane, MatchupView View)? best = null;

        foreach (var lane in Lanes.All)
        {
            if (_meta.Matchup(championId, opponentId, lane) is not { } view)
                continue;

            if (best is null || view.Confidence > best.Value.View.Confidence)
                best = (lane, view);
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
    private double? SynergyLogOdds(int championId, List<int> allyChampions, ICollection<Reason> reasons, ErrorBudget budget)
    {
        var total = 0.0;
        var variance = 0.0;
        var counted = 0;
        string? bestPartner = null;
        var bestLogOdds = 0.0;
        SynergyView bestView = default;

        foreach (var ally in allyChampions)
        {
            if (_meta.Synergy(championId, ally) is not { } synergy)
                continue;

            // The synergy tier is out for the same reason the lane tier is, plus one of its own:
            // measured on 2.909 stored duos it explains 2,9 % of the variance of the very win rate
            // it was added to. Near-zero correlation means it is either an independent signal or
            // noise, and nothing here can tell which — while it moved the term by up to 0,12
            // log-odds either way.

            // Measured against what an average LISTED duo is worth, not against 50 %: OP.GG lists
            // the duos worth mentioning, so the mean of the stored rows sits above even (0.0552 on
            // the file this was measured on, i.e. 51,4 %). Uncentred, merely having a duo row was
            // worth 0,8 points, the estimate grew as allies locked in rather than with the quality
            // of the pick, and a candidate without a row was penalised for a gap in OP.GG's
            // selection. The baseline is zero in older files, which leaves them as they were.
            var logOdds = ScoreModel.Logit(synergy.WinRate) - _meta.SynergyBaseline;

            total += logOdds;
            variance += ScoreError.LogitVariance(synergy.WinRate, synergy.Play, Shrinkage.SynergyPrior, _meta.SynergyTarget);
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

        // The threshold is now measured against the average listed duo, not against even — which is
        // what the chip was always claiming to say. Duos that merely exist no longer earn one.
        if (bestPartner is not null && bestLogOdds > 0.08)
        {
            reasons.Add(Reason.Pro(
                $"spielt gut mit {bestPartner}",
                $"Zusammen mit {bestPartner} im selben Team liegt die Winrate bei "
                + $"{bestView.WinRate:P1} aus {bestView.Play:N0} Games — besser als die üblichen "
                + "Duos, die OP.GG überhaupt auflistet."));
        }

        // Thin duo samples are where the score is least certain; the damping applies to the
        // error exactly as it applies to the value.
        var scale = ScoreModel.SynergyDamping / counted;
        budget.Add(scale * scale * variance);

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

        var logOdds = ScoreModel.BanLaneFocus * ScoreModel.Logit(stat.WinRate);

        if (logOdds > 0.05)
        {
            reasons.Add(Reason.Pro(
                $"{stat.WinRate:P1} WR auf {lane.Display()}",
                // "diese Lane", not "deine": the engine also advises team-mate seats.
                $"Genau die Lane dieses Slots: {stat.WinRate:P1} Winrate aus "
                + $"{stat.Play:N0} Games. Ein Ban wirkt hier direkt."));
        }

        return logOdds;
    }

    /// <summary>
    /// For bans: how badly this champion beats the allies already locked in — beyond what its own
    /// strength and theirs already predict. Centred like every other duel term in this file, and
    /// for the same reason: the champion's general strength is already the term above this one, and
    /// counting it twice put whoever OP.GG happens to list a lot of edges for at the top.
    /// </summary>
    private double? ThreatToAlliesLogOdds(int championId, List<int> allyChampions, ICollection<Reason> reasons)
    {
        var total = 0.0;
        var counted = 0;
        string? worst = null;
        var worstLogOdds = 0.0;
        MatchupView worstView = default;

        foreach (var ally in allyChampions)
        {
            if (FindAnyLaneMatchup(championId, ally) is not { } found)
                continue;

            counted++;
            var logOdds = ScoreModel.Logit(found.View.WinRate) - _meta.MatchupBaseline(championId, ally, found.Lane);
            total += logOdds;

            if (logOdds > worstLogOdds)
            {
                worstLogOdds = logOdds;
                worst = _meta.ChampionName(ally);
                worstView = found.View;
            }
        }

        if (counted == 0)
            return null;

        if (worst is not null && worstLogOdds > 0.1)
        {
            reasons.Add(Reason.Pro(
                $"schlägt unseren {worst}",
                $"Im direkten Matchup gewinnt dieser Champ {worstView.WinRate:P1} gegen {worst}, "
                + $"der bei uns schon gepickt ist. Aus {worstView.Play:N0} Games."));
        }

        // Mean, not sum, for the same reason as the synergy term: otherwise the ban value climbed
        // with every locked ally instead of with the threat itself.
        return ScoreModel.BanAllyShare * (total / counted);
    }
}
