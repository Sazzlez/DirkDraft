using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>A champion's numbers in one lane, already shrunk.</summary>
public readonly record struct LaneView(
    double WinRate,
    double PickRate,
    double BanRate,
    double RoleRate,
    int Tier,
    int Play)
{
    /// <summary>How far above or below even this champion sits, after shrinkage.</summary>
    public double WinRateDelta => WinRate - 0.5;
}

/// <summary>A matchup, already shrunk, with the weight its sample deserves.</summary>
/// <param name="IsLive">Fetched on demand for the current draft rather than from the snapshot.</param>
public readonly record struct MatchupView(double WinRate, int Play, double Confidence, bool IsInferred, bool IsLive = false)
{
    public double WinRateDelta => WinRate - 0.5;
}

/// <summary>A duo, already shrunk.</summary>
public readonly record struct SynergyView(double WinRate, int Play, double Confidence, int Tier)
{
    public double WinRateDelta => WinRate - 0.5;
}

/// <summary>
/// Fast, allocation-free access to a <see cref="MetaSnapshot"/>. Built once at load; every lookup
/// afterwards is an array index or a dictionary probe, which is what keeps a full recalculation of
/// all ~170 candidates well under a millisecond.
/// </summary>
public sealed class MetaLookup
{
    private readonly MetaSnapshot _snapshot;
    private readonly Dictionary<int, int> _indexById;
    private readonly ChampionEntry[] _champions;

    /// <summary>Lane stats, indexed by <c>lane * championCount + championIndex</c>.</summary>
    private readonly LaneView?[] _laneStats;

    private readonly Dictionary<long, MatchupView> _matchups;
    private readonly Dictionary<long, SynergyView> _synergies;

    /// <summary>
    /// Matchups fetched on demand for the draft on screen. Consulted before the snapshot: the
    /// stocked matrix is deliberately sparse (the source names ~3 counters per champion and lane),
    /// while these were requested for exactly the enemies being faced right now.
    /// </summary>
    private readonly Dictionary<long, MatchupView> _liveMatchups = [];

    /// <summary>Role priors normalised per champion, indexed like <see cref="_laneStats"/>.</summary>
    private readonly double[] _rolePriors;

    private readonly List<int>[] _laneRosters;

    public MetaLookup(MetaSnapshot snapshot)
    {
        _snapshot = snapshot;
        _champions = [.. snapshot.Champions];
        _indexById = new Dictionary<int, int>(_champions.Length);

        for (var i = 0; i < _champions.Length; i++)
            _indexById[_champions[i].Id] = i;

        _laneStats = new LaneView?[Lanes.Count * _champions.Length];
        _rolePriors = new double[Lanes.Count * _champions.Length];
        _laneRosters = [.. Enumerable.Range(0, Lanes.Count).Select(_ => new List<int>())];

        LoadLaneStats(snapshot);
        NormaliseRolePriors();

        _matchups = BuildMatchups(snapshot);
        _synergies = BuildSynergies(snapshot);
    }

    public string Patch => _snapshot.Patch;

    /// <summary>The patch OP.GG's numbers describe; empty for snapshots written before it existed.</summary>
    public string DataPatch => _snapshot.DataPatch;

    /// <summary>When OP.GG last recomputed, as opposed to when the button was pressed.</summary>
    public DateTimeOffset? DataAsOfUtc => _snapshot.DataAsOfUtc;

    public DateTimeOffset BuiltAtUtc => _snapshot.BuiltAtUtc;

    /// <summary>
    /// The rank bracket these numbers describe, e.g. <c>gold</c> or <c>all</c>. Read by the live
    /// fetch, so the counters it adds during a draft come from the same population as the file, and
    /// by the footer, so the player can see which league the advice is about.
    /// </summary>
    public string Tier => _snapshot.Tier ?? string.Empty;

    public IReadOnlyList<string> Warnings => _snapshot.Warnings;

    public IReadOnlyList<ChampionEntry> Champions => _champions;

    /// <summary>
    /// An empty lookup, so the app runs before the first data update. Deliberately a fresh
    /// instance per read: the type carries mutable live-matchup state, and a shared singleton
    /// mutated by one caller would corrupt the fallback for all of them. (Live matchups can't
    /// land on an empty lookup anyway — no champion resolves — but cheap beats subtle.)
    /// </summary>
    public static MetaLookup Empty => new(new MetaSnapshot());

    public bool IsEmpty => _champions.Length == 0;

    public ChampionEntry? Champion(int championId)
        => _indexById.TryGetValue(championId, out var index) ? _champions[index] : null;

    public string ChampionName(int championId) => Champion(championId)?.Name ?? $"#{championId}";

    /// <summary>Champions with recorded play in a lane, i.e. the candidate pool for that seat.</summary>
    public IReadOnlyList<int> Roster(Lane lane)
        => lane == Lane.Unknown ? [] : _laneRosters[(int)lane];

    public LaneView? LaneStat(int championId, Lane lane)
    {
        if (lane == Lane.Unknown || !_indexById.TryGetValue(championId, out var index))
            return null;

        return _laneStats[((int)lane * _champions.Length) + index];
    }

    /// <summary>
    /// P(lane | champion), normalised so the five lanes sum to one. Champions the snapshot never
    /// saw fall back to a flat distribution; a KNOWN champion without lane stats returns 0 for
    /// every lane and survives only through the predictor's off-meta floor.
    /// </summary>
    public double RolePrior(int championId, Lane lane)
    {
        if (lane == Lane.Unknown || !_indexById.TryGetValue(championId, out var index))
            return 1.0 / Lanes.Count;

        // Sanitizer, not decoration: one NaN role rate in a snapshot would ride through the
        // predictor's normalisation and turn EVERY seat's prediction into Unknown.
        var prior = _rolePriors[((int)lane * _champions.Length) + index];
        return double.IsFinite(prior) && prior > 0 ? prior : 0;
    }

    /// <summary>True when the snapshot knows which lanes this champion actually plays.</summary>
    public bool HasRoleData(int championId)
    {
        if (!_indexById.TryGetValue(championId, out var index))
            return false;

        for (var lane = 0; lane < Lanes.Count; lane++)
        {
            if (_rolePriors[(lane * _champions.Length) + index] > 0)
                return true;
        }

        return false;
    }

    /// <summary><paramref name="championId"/>'s prospects against <paramref name="opponentId"/> in a lane.</summary>
    public MatchupView? Matchup(int championId, int opponentId, Lane lane)
    {
        if (lane == Lane.Unknown)
            return null;

        if (!_indexById.TryGetValue(championId, out var a) || !_indexById.TryGetValue(opponentId, out var b))
            return null;

        var key = MatchupKey(lane, a, b);

        if (_liveMatchups.TryGetValue(key, out var live))
            return live;

        return _matchups.TryGetValue(key, out var view) ? view : null;
    }

    /// <summary>How many live matchup edges are currently overlaid; shown in the UI.</summary>
    public int LiveMatchupCount => _liveMatchups.Count;

    /// <summary>
    /// Overlays matchups fetched for the current draft. Mirror edges are inferred exactly like the
    /// snapshot's, so a "who beats Darius" answer also updates every candidate's view against him.
    /// </summary>
    public void ApplyLiveMatchups(IEnumerable<MatchupStat> stats)
    {
        foreach (var stat in stats)
        {
            if (stat.Lane == Lane.Unknown || stat.Play <= 0)
                continue;

            if (!_indexById.TryGetValue(stat.ChampionId, out var a) || !_indexById.TryGetValue(stat.OpponentId, out var b))
                continue;

            var view = new MatchupView(
                WinRate: Shrinkage.Apply(stat.WinRate, stat.Play, Shrinkage.MatchupPrior),
                Play: stat.Play,
                Confidence: Shrinkage.Confidence(stat.Play, Shrinkage.MatchupPrior),
                IsInferred: false,
                IsLive: true);

            var key = MatchupKey(stat.Lane, a, b);
            if (!_liveMatchups.TryGetValue(key, out var existing) || stat.Play > existing.Play)
                _liveMatchups[key] = view;

            var mirrorView = view with
            {
                WinRate = Shrinkage.Apply(1 - stat.WinRate, stat.Play, Shrinkage.MatchupPrior),
                IsInferred = true,
            };
            var mirror = MatchupKey(stat.Lane, b, a);
            if (!_liveMatchups.TryGetValue(mirror, out var existingMirror) || stat.Play > existingMirror.Play)
                _liveMatchups[mirror] = mirrorView;
        }
    }

    /// <summary>Drops the overlay; called when the draft it was fetched for ends.</summary>
    public void ClearLiveMatchups() => _liveMatchups.Clear();

    public SynergyView? Synergy(int championId, int partnerId)
    {
        if (!_indexById.TryGetValue(championId, out var a) || !_indexById.TryGetValue(partnerId, out var b))
            return null;

        return _synergies.TryGetValue(SynergyKey(a, b), out var view) ? view : null;
    }

    private void LoadLaneStats(MetaSnapshot snapshot)
    {
        foreach (var stat in snapshot.LaneStats)
        {
            if (stat.Lane == Lane.Unknown || !_indexById.TryGetValue(stat.ChampionId, out var index))
                continue;

            var slot = ((int)stat.Lane * _champions.Length) + index;

            _laneStats[slot] = new LaneView(
                WinRate: Shrinkage.Apply(stat.WinRate, stat.Play, Shrinkage.LanePrior),
                PickRate: stat.PickRate,
                BanRate: stat.BanRate,
                RoleRate: stat.RoleRate,
                // Tier 0 means two different things depending on where the row came from: OP.GG's
                // OP tier in the tier list, and "no tier at all" in the fallback rows the analysis
                // writes for lanes the tier list never listed. Those fallbacks carry no games, so
                // the game count tells the two apart — in either direction, and in files written
                // before this distinction existed.
                Tier: stat.Play > 0 ? stat.Tier : -1,
                Play: stat.Play);

            _rolePriors[slot] = Math.Max(0, stat.RoleRate);

            // A duplicated entry would otherwise scale that champion's weight in every sweep.
            if (!_laneRosters[(int)stat.Lane].Contains(stat.ChampionId))
                _laneRosters[(int)stat.Lane].Add(stat.ChampionId);
        }
    }

    /// <summary>
    /// OP.GG reports each lane's role share independently, so a champion's five values need not sum
    /// to one. Normalising makes the predictor's likelihoods comparable across champions.
    /// </summary>
    private void NormaliseRolePriors()
    {
        for (var index = 0; index < _champions.Length; index++)
        {
            var total = 0.0;
            for (var lane = 0; lane < Lanes.Count; lane++)
                total += _rolePriors[(lane * _champions.Length) + index];

            if (total <= 0)
                continue;

            for (var lane = 0; lane < Lanes.Count; lane++)
                _rolePriors[(lane * _champions.Length) + index] /= total;
        }
    }

    /// <summary>
    /// Indexes the recorded matchups and, for each one, infers the mirror edge when it is missing.
    /// A win rate is symmetric, so knowing Darius beats Nasus 57 % also tells us Nasus is at 43 %
    /// — free coverage that roughly doubles a deliberately sparse data set.
    /// </summary>
    private Dictionary<long, MatchupView> BuildMatchups(MetaSnapshot snapshot)
    {
        var map = new Dictionary<long, MatchupView>(snapshot.Matchups.Count * 2);

        foreach (var stat in snapshot.Matchups)
        {
            if (stat.Lane == Lane.Unknown)
                continue;

            if (!_indexById.TryGetValue(stat.ChampionId, out var a) || !_indexById.TryGetValue(stat.OpponentId, out var b))
                continue;

            // On duplicates the larger sample wins — same rule as the synergy and live-matchup
            // builders. "Last wins" let a 12-game duplicate replace a 3000-game record.
            var key = MatchupKey(stat.Lane, a, b);
            if (map.TryGetValue(key, out var existing) && existing.Play >= stat.Play)
                continue;

            map[key] = new MatchupView(
                WinRate: Shrinkage.Apply(stat.WinRate, stat.Play, Shrinkage.MatchupPrior),
                Play: stat.Play,
                Confidence: Shrinkage.Confidence(stat.Play, Shrinkage.MatchupPrior),
                IsInferred: false);
        }

        foreach (var stat in snapshot.Matchups)
        {
            if (stat.Lane == Lane.Unknown)
                continue;

            if (!_indexById.TryGetValue(stat.ChampionId, out var a) || !_indexById.TryGetValue(stat.OpponentId, out var b))
                continue;

            var mirror = MatchupKey(stat.Lane, b, a);
            if (map.ContainsKey(mirror))
                continue;

            map[mirror] = new MatchupView(
                WinRate: Shrinkage.Apply(1 - stat.WinRate, stat.Play, Shrinkage.MatchupPrior),
                Play: stat.Play,
                Confidence: Shrinkage.Confidence(stat.Play, Shrinkage.MatchupPrior),
                IsInferred: true);
        }

        return map;
    }

    private Dictionary<long, SynergyView> BuildSynergies(MetaSnapshot snapshot)
    {
        var map = new Dictionary<long, SynergyView>(snapshot.Synergies.Count * 2);

        foreach (var stat in snapshot.Synergies)
        {
            if (!_indexById.TryGetValue(stat.ChampionId, out var a) || !_indexById.TryGetValue(stat.PartnerId, out var b))
                continue;

            var view = new SynergyView(
                WinRate: Shrinkage.Apply(stat.WinRate, stat.Play, Shrinkage.SynergyPrior),
                Play: stat.Play,
                Confidence: Shrinkage.Confidence(stat.Play, Shrinkage.SynergyPrior),
                Tier: stat.Tier);

            // A duo is symmetric, so index it from both sides.
            var forward = SynergyKey(a, b);
            if (!map.TryGetValue(forward, out var existing) || stat.Play > existing.Play)
                map[forward] = view;
        }

        return map;
    }

    // Bit-packed rather than decimal-packed so the keys stay correct no matter how many champions
    // Riot ships.
    private static long MatchupKey(Lane lane, int a, int b) => ((long)lane << 40) | ((long)a << 20) | (uint)b;

    private static long SynergyKey(int a, int b)
    {
        // Order-independent so both partners find the same entry.
        var low = Math.Min(a, b);
        var high = Math.Max(a, b);
        return ((long)low << 20) | (uint)high;
    }
}
