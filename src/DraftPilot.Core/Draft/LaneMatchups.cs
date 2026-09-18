using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

/// <summary>One lane of the finished draft: who stands there on both sides, and how the duel reads.</summary>
/// <param name="Lane">The lane this row is about.</param>
/// <param name="AllyId">Our champion on that lane, or 0 when nobody was placed there.</param>
/// <param name="EnemyId">Their champion on that lane, or 0.</param>
/// <param name="WinRate">
/// The duel's shrunk win rate from our side, or <see langword="null"/> when one side is missing or
/// OP.GG has no row for the pairing. Null is not 50 %: the difference is exactly what the reader
/// needs to see, so it is kept out of the number instead of being averaged into it.
/// </param>
/// <param name="Play">Games behind <paramref name="WinRate"/>; 0 when there is no rate.</param>
/// <param name="IsInferred">True when the rate is the mirror of the opposite pairing, not measured directly.</param>
public readonly record struct LaneMatchup(
    Lane Lane,
    int AllyId,
    int EnemyId,
    double? WinRate,
    int Play,
    bool IsInferred)
{
    /// <summary>Both sides revealed and a duel statistic exists — the only case that carries a number.</summary>
    public bool HasWinRate => WinRate is not null;
}

/// <summary>
/// The five lanes of a draft side by side. The window's own matchup panel answers "how does MY lane
/// stand"; this answers the question that only has an answer once the draft is done — how the whole
/// board is matched up.
/// <para>
/// Lanes are read from the predictions, not from the client's assignments alone, so a flex pick
/// lands where it will most likely be played. A lane nobody was placed on is left out rather than
/// shown empty: five rows of which three say nothing are worse than two rows that do.
/// </para>
/// </summary>
public static class LaneMatchups
{
    /// <summary>Builds one row per lane that at least one side occupies, in draft display order.</summary>
    public static IReadOnlyList<LaneMatchup> For(
        MetaLookup meta,
        LanePredictionResult allies,
        LanePredictionResult enemies)
    {
        var rows = new List<LaneMatchup>(Lanes.Count);

        foreach (var lane in Lanes.All)
        {
            var ally = allies.ChampionOnLane(lane);
            var enemy = enemies.ChampionOnLane(lane);

            if (ally == 0 && enemy == 0)
                continue;

            var duel = ally != 0 && enemy != 0 ? meta.Matchup(ally, enemy, lane) : null;

            rows.Add(new LaneMatchup(
                lane,
                ally,
                enemy,
                duel?.WinRate,
                duel?.Play ?? 0,
                duel?.IsInferred ?? false));
        }

        return rows;
    }

    /// <summary>
    /// The duel one seat is standing in: the row for its lane, but only when the seat's own
    /// champion is the one that row has on that side.
    /// <para>
    /// The champion check is what makes this safe to call per seat. A lane holds one seat per side,
    /// so matching the lane alone is enough while the rows come from the same prediction the seat
    /// was rendered from — and hands a seat its neighbour's number the moment that stops being
    /// true. Cheap to verify, so it is verified.
    /// </para>
    /// </summary>
    /// <param name="rows">Rows from <see cref="For"/>.</param>
    /// <param name="lane">The lane the seat is on, predicted or assigned.</param>
    /// <param name="championId">What the seat has revealed; 0 while it has revealed nothing.</param>
    /// <param name="isAlly">Which side of the row to compare the champion against.</param>
    public static LaneMatchup? ForSeat(
        IReadOnlyList<LaneMatchup> rows,
        Lane lane,
        int championId,
        bool isAlly)
    {
        if (lane == Lane.Unknown || championId == 0)
            return null;

        foreach (var row in rows)
        {
            if (row.Lane == lane && (isAlly ? row.AllyId : row.EnemyId) == championId)
                return row;
        }

        return null;
    }
}
