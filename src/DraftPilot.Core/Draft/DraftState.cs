using DraftPilot.Core.Lcu.Models;

namespace DraftPilot.Core.Draft;

public enum DraftPhase
{
    /// <summary>No champion select is running.</summary>
    None,

    /// <summary>Declare-intent phase before bans.</summary>
    Planning,

    /// <summary>Bans and picks are happening.</summary>
    BanPick,

    /// <summary>Everything is locked; runes and summoners only.</summary>
    Finalization,

    /// <summary>The client reported a phase we do not know.</summary>
    Unknown,
}

public enum TurnAction
{
    None,
    Pick,
    Ban,
}

/// <summary>One seat in champion select.</summary>
/// <param name="CellId">The client's identifier for the seat.</param>
/// <param name="Index">Position within its team, 0-4, in the client's display order.</param>
/// <param name="LockedChampionId">0 until the pick is locked in.</param>
/// <param name="HoverChampionId">0 unless a hover is visible; enemy hovers never are in ranked.</param>
/// <param name="AssignedLane">
/// What the client told us. Always <see cref="Lane.Unknown"/> for enemies, and can be unknown for
/// allies in blind pick or custom games.
/// </param>
public sealed record DraftSlot(
    long CellId,
    int Index,
    bool IsAlly,
    int LockedChampionId,
    int HoverChampionId,
    Lane AssignedLane)
{
    /// <summary>The champion to reason about: the locked pick if there is one, otherwise the hover.</summary>
    public int EffectiveChampionId => LockedChampionId != 0 ? LockedChampionId : HoverChampionId;

    public bool IsLocked => LockedChampionId != 0;
}

/// <summary>Who the client currently has on the clock.</summary>
public sealed record ActiveTurn(long CellId, bool IsAlly, bool IsLocalPlayer, TurnAction Action);

/// <summary>
/// The champion select session reduced to what the recommendation engine needs. Built fresh on
/// every push event; cheap enough that there is no reason to mutate in place.
/// </summary>
public sealed class DraftState
{
    private DraftState()
    {
    }

    public static DraftState Inactive { get; } = new();

    public bool IsActive { get; private init; }

    public long LocalCellId { get; private init; } = -1;

    public IReadOnlyList<DraftSlot> Allies { get; private init; } = [];

    public IReadOnlyList<DraftSlot> Enemies { get; private init; } = [];

    public IReadOnlyList<int> AllyBans { get; private init; } = [];

    public IReadOnlyList<int> EnemyBans { get; private init; } = [];

    public DraftPhase Phase { get; private init; } = DraftPhase.None;

    public int SecondsLeft { get; private init; }

    /// <summary>Length of the current phase in seconds; 0 when the client reports none.</summary>
    public int TotalSeconds { get; private init; }

    /// <summary>
    /// How stale <see cref="SecondsLeft"/> already was when the payload arrived, in milliseconds.
    /// Zero when the client did not report its own clock.
    /// </summary>
    public long AgeAtArrivalMs { get; private init; }

    public ActiveTurn? Turn { get; private init; }

    /// <summary>Every champion that is off the table: banned by either side or already locked.</summary>
    public IReadOnlySet<int> Unavailable { get; private init; } = new HashSet<int>();

    public DraftSlot? LocalSlot => Allies.FirstOrDefault(slot => slot.CellId == LocalCellId);

    public DraftSlot? FindSlot(long cellId)
        => Allies.FirstOrDefault(slot => slot.CellId == cellId)
        ?? Enemies.FirstOrDefault(slot => slot.CellId == cellId);

    /// <summary>Projects the session onto a <see cref="DraftState"/>. Never throws on odd input.</summary>
    public static DraftState From(ChampSelectSession? session)
    {
        if (session is null || session.IsSpectating)
            return Inactive;

        var allies = BuildSlots(session.MyTeam, isAlly: true);
        var enemies = BuildSlots(session.TheirTeam, isAlly: false);

        if (allies.Count == 0 && enemies.Count == 0)
            return Inactive;

        var (allyBans, enemyBans) = CollectBans(session);

        var unavailable = new HashSet<int>(allyBans.Count + enemyBans.Count + allies.Count + enemies.Count);
        unavailable.UnionWith(allyBans);
        unavailable.UnionWith(enemyBans);
        foreach (var slot in allies.Concat(enemies).Where(s => s.IsLocked))
            unavailable.Add(slot.LockedChampionId);

        return new DraftState
        {
            IsActive = true,
            LocalCellId = session.LocalPlayerCellId,
            Allies = allies,
            Enemies = enemies,
            AllyBans = allyBans,
            EnemyBans = enemyBans,
            Phase = ParsePhase(session.Timer.Phase),
            SecondsLeft = (int)Math.Max(0, session.Timer.AdjustedTimeLeftInPhase / 1000),
            TotalSeconds = (int)Math.Max(0, session.Timer.TotalTimeInPhase / 1000),
            AgeAtArrivalMs = AgeOf(session.Timer),
            Turn = FindActiveTurn(session),
            Unavailable = unavailable,
        };
    }

    /// <summary>The action groups flattened, tolerating explicit nulls at either nesting level.</summary>
    private static IEnumerable<ChampSelectAction> AllActions(ChampSelectSession session)
        => session.Actions
            .Where(group => group is not null)
            .SelectMany(group => group)
            .Where(action => action is not null);

    private static List<DraftSlot> BuildSlots(List<ChampSelectPlayer> players, bool isAlly)
    {
        var slots = new List<DraftSlot>(players.Count);

        foreach (var player in players)
        {
            if (player is null)
                continue;

            slots.Add(new DraftSlot(
                CellId: player.CellId,
                // The SLOT's index, not the raw list position: a skipped null entry must not
                // leave a gap — SeatPriors addresses seats by this value.
                Index: slots.Count,
                IsAlly: isAlly,
                LockedChampionId: player.ChampionId,
                HoverChampionId: player.ChampionPickIntent,
                // Riot never exposes enemy positions; guessing them is the LanePredictor's job.
                AssignedLane: isAlly ? Lanes.FromLcu(player.AssignedPosition) : Lane.Unknown));
        }

        return slots;
    }

    /// <summary>
    /// Takes bans from both the summary lists and the completed ban actions. The summary is empty
    /// early in some queues, and the actions are the only source that updates as bans land.
    /// </summary>
    private static (List<int> Ally, List<int> Enemy) CollectBans(ChampSelectSession session)
    {
        var ally = new List<int>(session.Bans.MyTeamBans.Where(id => id != 0));
        var enemy = new List<int>(session.Bans.TheirTeamBans.Where(id => id != 0));

        foreach (var action in AllActions(session))
        {
            if (!string.Equals(action.Type, "ban", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!action.Completed || action.ChampionId == 0)
                continue;

            var target = action.IsAllyAction ? ally : enemy;
            if (!target.Contains(action.ChampionId))
                target.Add(action.ChampionId);
        }

        return (ally, enemy);
    }

    /// <summary>
    /// Finds the action currently on the clock. When several run at once — some queues ban
    /// simultaneously — the local player wins, then the lowest ally cell, then anything else.
    /// </summary>
    private static ActiveTurn? FindActiveTurn(ChampSelectSession session)
    {
        ActiveTurn? best = null;
        var bestRank = int.MaxValue;

        foreach (var action in AllActions(session))
        {
            if (!action.IsInProgress || action.Completed)
                continue;

            var actionType = ParseAction(action.Type);
            if (actionType == TurnAction.None)
                continue;

            var isLocal = action.ActorCellId == session.LocalPlayerCellId;
            var rank = isLocal ? -1 : action.IsAllyAction ? (int)action.ActorCellId : 1_000 + (int)action.ActorCellId;

            if (rank >= bestRank)
                continue;

            bestRank = rank;
            best = new ActiveTurn(action.ActorCellId, action.IsAllyAction, isLocal, actionType);
        }

        return best;
    }

    /// <summary>
    /// How long ago the client built this snapshot. Clamped to a sane window: a client clock that
    /// disagrees with ours by minutes is a wrong clock, not a stale payload, and correcting by it
    /// would be worse than not correcting at all.
    /// </summary>
    private static long AgeOf(ChampSelectTimer timer)
    {
        if (timer.InternalNowInEpochMs <= 0)
            return 0;

        var age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timer.InternalNowInEpochMs;
        return age is >= 0 and <= 10_000 ? age : 0;
    }

    private static TurnAction ParseAction(string? type) => type?.ToLowerInvariant() switch
    {
        "pick" => TurnAction.Pick,
        "ban" => TurnAction.Ban,
        _ => TurnAction.None,
    };

    private static DraftPhase ParsePhase(string? phase) => phase?.ToUpperInvariant() switch
    {
        null or "" => DraftPhase.None,
        "PLANNING" => DraftPhase.Planning,
        "BAN_PICK" => DraftPhase.BanPick,
        "FINALIZATION" => DraftPhase.Finalization,
        _ => DraftPhase.Unknown,
    };
}
