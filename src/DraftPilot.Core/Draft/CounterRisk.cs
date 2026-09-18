using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

/// <summary>One champion that is still available and beats the candidate by more than usual.</summary>
/// <param name="Edge">
/// How much better this opponent does against the candidate than an average opponent of that
/// candidate does, in log-odds. The reference is the candidate's own lane win rate — the same
/// centring the duel term uses, and the reason a champion that is merely strong does not qualify.
/// </param>
public readonly record struct CounterThreat(int ChampionId, double WinRate, int Play, double Edge);

/// <summary>
/// What a pick still risks when the lane opponent has not been revealed yet.
/// <para>
/// The first pick of a draft gets no duel term at all — there is nobody to compare against — so the
/// list answers "how strong is this champion" while the question in that moment is "how easily can
/// this be answered". The data for it is already there: OP.GG lists the opponents that stand out
/// against a champion, and the draft says which of them are still free to take.
/// </para>
/// <para>
/// Deliberately a statement about what EXISTS, not a score. It counts the counters the source
/// happens to know — never "no counters", only "these counters" — and it does not enter the
/// estimated win rate: whether an available counter will actually be picked is exactly the thing
/// nobody can measure here.
/// </para>
/// </summary>
public static class CounterRisk
{
    /// <summary>
    /// How far above the usual an opponent has to land to count. 0.08 log-odds is about two
    /// percentage points and is the same bar every other chip in this engine uses.
    /// </summary>
    public const double MinimumEdge = 0.08;

    /// <summary>
    /// The counters of <paramref name="championId"/> on <paramref name="lane"/> that nobody has
    /// taken yet, strongest first.
    /// </summary>
    /// <param name="unavailable">
    /// Banned, already locked, or hovered by somebody — anything the enemy can no longer reach for.
    /// </param>
    public static IReadOnlyList<CounterThreat> Open(
        MetaLookup meta,
        int championId,
        Lane lane,
        IReadOnlySet<int> unavailable,
        int limit = 3)
    {
        if (lane == Lane.Unknown || meta.LaneStat(championId, lane) is not { } own)
            return [];

        // What an average opponent achieves against this champion: the other side of its own lane
        // win rate. Without this reference the list would fill up with champions that are simply
        // strong, which says nothing about picking INTO them.
        var expected = ScoreModel.Logit(1 - own.WinRate);

        var threats = new List<CounterThreat>();

        foreach (var candidate in meta.Roster(lane))
        {
            if (candidate == championId || unavailable.Contains(candidate))
                continue;

            if (meta.Matchup(candidate, championId, lane) is not { } view)
                continue;

            var edge = ScoreModel.Logit(view.WinRate) - expected;
            if (edge <= MinimumEdge)
                continue;

            threats.Add(new CounterThreat(candidate, view.WinRate, view.Play, edge));
        }

        threats.Sort((left, right) => right.Edge.CompareTo(left.Edge));

        return limit <= 0 || threats.Count <= limit ? threats : threats[..limit];
    }
}
