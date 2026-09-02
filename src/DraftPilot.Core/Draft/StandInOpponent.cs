using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

/// <summary>
/// Who to build against when the enemy team is never revealed.
/// <para>
/// Blind pick and normal games hide the opposing picks for the entire draft, so the lane opponent
/// stays unknown — and OP.GG has no build endpoint that works without one: the matchup guide, the
/// only source of runes, starting items and skill order, requires an <c>opponent_champion</c>
/// (verified against the live tool list, 29 tools, none of them a generic build). Without a
/// stand-in there is no build and no rune import in those queues at all, which is exactly what a
/// live test turned up.
/// </para>
/// <para>
/// The stand-in is the champion most often actually played on that lane. Runes, summoner spells,
/// starting items and skill order are overwhelmingly a property of the champion being played rather
/// than of the opponent; the item core is where the opponent matters, and that is the part the user
/// has to be told about. The UI therefore always names the stand-in instead of pretending it is the
/// real matchup.
/// </para>
/// </summary>
public static class StandInOpponent
{
    /// <summary>
    /// True when the enemy team is completely unrevealed — no pick, no hover, in any of the five
    /// seats. That is the signature of a queue that will never reveal them, and it is deliberately
    /// stricter than "nobody on my lane yet": in a draft that does reveal, waiting the few seconds
    /// for the real opponent is better than fetching a build against a guess.
    /// </summary>
    public static bool EnemiesAreHidden(DraftState state)
        => state.Enemies.Count > 0 && state.Enemies.All(slot => slot.EffectiveChampionId == 0);

    /// <summary>
    /// The most-played champion on <paramref name="lane"/> that could still be the opponent, or 0
    /// when the snapshot knows no roster for it.
    /// </summary>
    /// <param name="unavailable">Banned or already taken; nobody can face those.</param>
    /// <param name="ownChampion">Excluded as well — a mirror match is the one guess never worth making.</param>
    public static int For(MetaLookup meta, Lane lane, IReadOnlySet<int> unavailable, int ownChampion)
    {
        if (lane == Lane.Unknown)
            return 0;

        var best = 0;
        var bestRate = -1.0;

        foreach (var candidate in meta.Roster(lane))
        {
            if (candidate == ownChampion || unavailable.Contains(candidate))
                continue;

            if (meta.LaneStat(candidate, lane) is not { } stat)
                continue;

            // Pick rate answers "how often does this champion show up on this lane", which is the
            // question. Win rate would name the strongest opponent instead of the likeliest, and
            // building against the worst case every game is its own kind of wrong.
            if (stat.PickRate <= bestRate)
                continue;

            bestRate = stat.PickRate;
            best = candidate;
        }

        return best;
    }
}
