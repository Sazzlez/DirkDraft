using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

/// <summary>One weighted contribution to a champion's score.</summary>
/// <param name="Label">Shown in the breakdown, e.g. <c>Lane-Matchup</c>.</param>
/// <param name="Normalised">The signal itself, roughly -1 to +1.</param>
/// <param name="Weight">The user's weight for this signal.</param>
public sealed record ScoreTerm(string Label, double Normalised, double Weight)
{
    public double Contribution => Normalised * Weight;
}

/// <summary>One entry in the recommendation list.</summary>
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
/// The engine takes a seat as input rather than assuming it is the local player, which is what
/// lets the same code advise a team-mate who is on the clock.
/// </para>
/// </summary>
public sealed class Recommender(MetaLookup meta, TraitTable traits)
{
    /// <summary>
    /// Win-rate deltas after shrinkage live in a narrow band — a couple of points either way — so
    /// they are scaled up to fill the -1..+1 range the weights assume.
    /// </summary>
    private const double WinRateScale = 20;

    private const double MatchupScale = 10;

    private readonly MetaLookup _meta = meta;
    private readonly CompAnalyzer _comp = new(meta, traits);

    /// <param name="state">Current draft.</param>
    /// <param name="target">The seat to advise.</param>
    /// <param name="enemyLanes">Predicted enemy lanes, used to find the direct opponent.</param>
    /// <param name="weights">User weighting.</param>
    /// <param name="selectable">
    /// Champions the seat may actually take, or <see langword="null"/> to allow anything. Only ever
    /// available for the local player; the client does not expose what team-mates own.
    /// </param>
    /// <param name="limit">How many entries to return.</param>
    public RecommendationSet Recommend(
        DraftState state,
        RecommendationTarget target,
        LanePredictionResult enemyLanes,
        ScoreWeights weights,
        IReadOnlySet<int>? selectable = null,
        int limit = 10)
    {
        if (!state.IsActive || _meta.IsEmpty)
            return RecommendationSet.Empty;

        var lane = ResolveLane(target, enemyLanes);
        var allyChampions = state.Allies
            .Where(slot => slot.CellId != target.Slot.CellId)
            .Select(slot => slot.EffectiveChampionId)
            .Where(id => id != 0)
            .ToList();

        var enemyChampions = state.Enemies.Select(slot => slot.EffectiveChampionId).Where(id => id != 0).ToList();

        var allyComp = _comp.Analyze(allyChampions);
        var enemyComp = _comp.Analyze(enemyChampions);

        var items = target.Action == TurnAction.Ban
            ? ScoreBans(state, lane, allyChampions, weights, limit)
            : ScorePicks(state, lane, allyChampions, enemyChampions, enemyLanes, allyComp, weights, selectable, limit);

        return new RecommendationSet(target.Slot.CellId, lane, target.Action, items, allyComp, enemyComp);
    }

    /// <summary>
    /// The lane to score for: what the client assigned, else what we predicted for that seat.
    /// </summary>
    private static Lane ResolveLane(RecommendationTarget target, LanePredictionResult predictions)
    {
        if (target.Slot.AssignedLane != Lane.Unknown)
            return target.Slot.AssignedLane;

        return predictions.ForCell(target.Slot.CellId)?.Lane ?? Lane.Unknown;
    }

    private List<Recommendation> ScorePicks(
        DraftState state,
        Lane lane,
        List<int> allyChampions,
        List<int> enemyChampions,
        LanePredictionResult enemyLanes,
        CompProfile allyComp,
        ScoreWeights weights,
        IReadOnlySet<int>? selectable,
        int limit)
    {
        var results = new List<Recommendation>();

        foreach (var candidate in Candidates(lane))
        {
            if (state.Unavailable.Contains(candidate))
                continue;

            if (selectable is not null && !selectable.Contains(candidate))
                continue;

            var reasons = new List<Reason>();

            // Without a lane — custom games, blind pick — the lane-bound tier score is zero for
            // everyone and the list degenerates into an alphabet. Strength on the champion's own
            // best lane is the honest substitute.
            var tierSignal = lane == Lane.Unknown
                ? BestLaneSignal(candidate, lane, reasons)
                : TierSignal(candidate, lane, reasons);

            var terms = new List<ScoreTerm>(5)
            {
                new("Tierlist", tierSignal, weights.Tier),
                new("Lane-Matchup", LaneMatchupSignal(candidate, lane, enemyLanes, reasons), weights.LaneMatchup),
                new("Gegner-Team", TeamMatchupSignal(candidate, lane, enemyLanes, reasons), weights.TeamMatchup),
                new("Synergie", SynergySignal(candidate, allyChampions, reasons), weights.Synergy),
                new("Teamcomp", _comp.Fit(candidate, allyComp, reasons), weights.Composition),
            };

            results.Add(Build(candidate, terms, reasons));
        }

        return Top(results, limit);
    }

    private List<Recommendation> ScoreBans(
        DraftState state,
        Lane lane,
        List<int> allyChampions,
        ScoreWeights weights,
        int limit)
    {
        var results = new List<Recommendation>();

        foreach (var candidate in Candidates(Lane.Unknown))
        {
            if (state.Unavailable.Contains(candidate))
                continue;

            var reasons = new List<Reason>();
            var terms = new List<ScoreTerm>(4)
            {
                new("Stärke", BestLaneSignal(candidate, lane, reasons), weights.Tier),
                new("Deine Lane", LaneThreatSignal(candidate, lane, reasons), weights.LaneMatchup),
                new("Gegen uns", ThreatToAlliesSignal(candidate, allyChampions, reasons), weights.TeamMatchup),
                new("Beliebtheit", PopularitySignal(candidate, reasons), 0.5),
            };

            results.Add(Build(candidate, terms, reasons));
        }

        return Top(results, limit);
    }

    /// <summary>
    /// Assembles one entry, dropping duplicate reasons. Two terms can legitimately notice the same
    /// fact, but the user should not read it twice.
    /// </summary>
    private Recommendation Build(int championId, List<ScoreTerm> terms, List<Reason> reasons)
        => new(
            championId,
            _meta.ChampionName(championId),
            terms.Sum(term => term.Contribution),
            [.. reasons.DistinctBy(reason => reason.Text, StringComparer.Ordinal)],
            terms);

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
            .Take(Math.Max(1, limit))];

    /// <summary>Lane strength: shrunk win rate plus OP.GG's own tier.</summary>
    private double TierSignal(int championId, Lane lane, ICollection<Reason> reasons)
    {
        if (_meta.LaneStat(championId, lane) is not { } stat)
            return 0;

        var fromWinRate = Math.Clamp(stat.WinRateDelta * WinRateScale, -1, 1);
        var fromTier = stat.Tier is >= 1 and <= 5 ? (3.0 - stat.Tier) / 2.0 : 0;
        var signal = (0.6 * fromWinRate) + (0.4 * fromTier);

        if (stat.Tier == 1)
            reasons.Add(Reason.Pro("Tier 1 auf der Lane"));
        else if (stat.WinRateDelta > 0.015)
            reasons.Add(Reason.Pro($"{stat.WinRate:P1} Winrate"));

        return signal;
    }

    /// <summary>
    /// Strength on whichever lane suits the champion best; used for bans. The chip names that lane
    /// only when it differs from the seat's own, otherwise the lane-threat term already said it.
    /// </summary>
    private double BestLaneSignal(int championId, Lane seatLane, ICollection<Reason> reasons)
    {
        var best = 0.0;
        var bestLane = Lane.Unknown;

        foreach (var lane in Lanes.All)
        {
            if (_meta.LaneStat(championId, lane) is not { } stat)
                continue;

            var fromWinRate = Math.Clamp(stat.WinRateDelta * WinRateScale, -1, 1);
            var fromTier = stat.Tier is >= 1 and <= 5 ? (3.0 - stat.Tier) / 2.0 : 0;
            var signal = (0.6 * fromWinRate) + (0.4 * fromTier);

            if (signal <= best)
                continue;

            best = signal;
            bestLane = lane;
        }

        if (bestLane != Lane.Unknown && bestLane != seatLane && best > 0.4)
            reasons.Add(Reason.Pro($"stark auf {bestLane.Display()}"));

        return best;
    }

    /// <summary>
    /// Prospects on the target lane, with every enemy weighted by how likely they actually play
    /// it. A 50/50 flex pick counts half on each of its plausible lanes, instead of fully on the
    /// single most likely one — which used to make the advice flip-flop on flex champions.
    /// </summary>
    private double LaneMatchupSignal(int championId, Lane lane, LanePredictionResult enemyLanes, ICollection<Reason> reasons)
    {
        if (lane == Lane.Unknown)
            return 0;

        var total = 0.0;
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

            total += probability * Math.Clamp(matchup.WinRateDelta * MatchupScale, -1, 1) * matchup.Confidence;

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
            var sign = best.WinRateDelta > 0 ? "+" : "";
            var qualifier = best.IsLive ? " (live)" : best.IsInferred ? " (abgeleitet)" : string.Empty;
            var flex = bestProbability < 0.6 ? " (Lane unsicher)" : string.Empty;
            reasons.Add(new Reason(
                $"{sign}{best.WinRateDelta:P0} vs {_meta.ChampionName(bestOpponent)}{qualifier}{flex}",
                best.WinRateDelta > 0 ? ReasonTone.Pro : ReasonTone.Contra));
        }

        return total;
    }

    /// <summary>P(this seat ends up on the lane). Manual overrides and assigned lanes are already
    /// hard constraints inside the predictor, so the marginal is all that is needed.</summary>
    private static double ProbabilityOnLane(LanePrediction prediction, Lane lane)
    {
        var index = (int)lane;
        return index >= 0 && index < prediction.Probabilities.Length ? prediction.Probabilities[index] : 0;
    }

    /// <summary>
    /// Prospects against the rest of the enemy team. Each enemy enters with the share of them NOT
    /// already covered by the lane term, so nobody is counted twice and a flex pick's weight is
    /// split consistently between the two terms.
    /// </summary>
    private double TeamMatchupSignal(
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

            var weight = 1 - ProbabilityOnLane(prediction, lane);
            if (weight < 0.05)
                continue;

            var view = FindAnyLaneMatchup(championId, prediction.ChampionId);
            if (view is null)
                continue;

            counted++;
            weightSum += weight;
            total += weight * Math.Clamp(view.Value.WinRateDelta * MatchupScale, -1, 1) * view.Value.Confidence;

            if (view.Value.WinRateDelta > 0.01)
                favourable++;
        }

        if (counted == 0 || weightSum <= 0)
            return 0;

        if (favourable >= 2)
            reasons.Add(Reason.Pro($"gut gegen {favourable} von {counted} weiteren Gegnern"));

        return total / weightSum;
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

    private double SynergySignal(int championId, List<int> allyChampions, ICollection<Reason> reasons)
    {
        var total = 0.0;
        var counted = 0;
        string? bestPartner = null;
        var bestSignal = 0.0;

        foreach (var ally in allyChampions)
        {
            if (_meta.Synergy(championId, ally) is not { } synergy)
                continue;

            var fromWinRate = Math.Clamp(synergy.WinRateDelta * MatchupScale, -1, 1) * synergy.Confidence;

            // OP.GG's own synergy tier is smoothed on their side, so it survives small samples
            // better than the raw duo win rate does.
            var fromTier = synergy.Tier is >= 0 and <= 4 ? (2.0 - synergy.Tier) / 2.0 : 0;
            var signal = (0.5 * fromWinRate) + (0.5 * fromTier);

            total += signal;
            counted++;

            if (signal > bestSignal)
            {
                bestSignal = signal;
                bestPartner = _meta.ChampionName(ally);
            }
        }

        if (counted == 0)
            return 0;

        if (bestPartner is not null && bestSignal > 0.3)
            reasons.Add(Reason.Pro($"Synergie mit {bestPartner}"));

        return total / counted;
    }

    /// <summary>For bans: how dangerous this champion is on the lane of the player on the clock.</summary>
    private double LaneThreatSignal(int championId, Lane lane, ICollection<Reason> reasons)
    {
        if (lane == Lane.Unknown || _meta.LaneStat(championId, lane) is not { } stat)
            return 0;

        var fromWinRate = Math.Clamp(stat.WinRateDelta * WinRateScale, -1, 1);
        var fromTier = stat.Tier is >= 1 and <= 5 ? (3.0 - stat.Tier) / 2.0 : 0;
        var signal = (0.5 * fromWinRate) + (0.5 * fromTier);

        if (signal > 0.5)
        {
            var tier = stat.Tier is >= 1 and <= 5 ? $"Tier {stat.Tier}" : $"{stat.WinRate:P1}";
            reasons.Add(Reason.Pro($"{tier} direkt auf {lane.Display()}"));
        }

        return signal;
    }

    /// <summary>For bans: how badly this champion beats the allies already locked in.</summary>
    private double ThreatToAlliesSignal(int championId, List<int> allyChampions, ICollection<Reason> reasons)
    {
        var total = 0.0;
        var counted = 0;
        string? worst = null;
        var worstSignal = 0.0;

        foreach (var ally in allyChampions)
        {
            var view = FindAnyLaneMatchup(championId, ally);
            if (view is null)
                continue;

            counted++;
            var signal = Math.Clamp(view.Value.WinRateDelta * MatchupScale, -1, 1) * view.Value.Confidence;
            total += signal;

            if (signal > worstSignal)
            {
                worstSignal = signal;
                worst = _meta.ChampionName(ally);
            }
        }

        if (counted == 0)
            return 0;

        if (worst is not null && worstSignal > 0.3)
            reasons.Add(Reason.Pro($"countert {worst}"));

        return total / counted;
    }

    /// <summary>For bans: whether the enemy is likely to take it at all.</summary>
    private double PopularitySignal(int championId, ICollection<Reason> reasons)
    {
        var best = 0.0;
        var bestPickRate = 0.0;
        var bestBanRate = 0.0;

        foreach (var lane in Lanes.All)
        {
            if (_meta.LaneStat(championId, lane) is not { } stat)
                continue;

            // Pick and ban rate are shares of all games; a few percent is already a lot.
            var signal = Math.Clamp((stat.PickRate * 8) + (stat.BanRate * 4), 0, 1);
            if (signal <= best)
                continue;

            best = signal;
            bestPickRate = stat.PickRate;
            bestBanRate = stat.BanRate;
        }

        if (best > 0.6)
        {
            reasons.Add(Reason.Neutral(bestBanRate > bestPickRate
                ? $"{bestBanRate:P0} Banrate"
                : $"{bestPickRate:P0} Pickrate"));
        }

        return best;
    }
}
