namespace DraftPilot.Core.Draft;

/// <summary>The seat the recommendation list is currently computed for.</summary>
/// <param name="Slot">The ally seat being advised.</param>
/// <param name="Action">Whether to show pick or ban recommendations.</param>
/// <param name="IsFollowingTurn">
/// True when this seat is the one actually on the clock, false when we are showing an outlook
/// (manually selected, or nobody on our team is picking right now).
/// </param>
public sealed record RecommendationTarget(DraftSlot Slot, TurnAction Action, bool IsFollowingTurn);

/// <summary>
/// Decides which ally seat the recommendation list should advise.
/// <para>
/// By default it follows whoever is on the clock. A manual selection holds until the turn moves
/// on. When nobody on our team is picking, the previous seat stays put so the list never goes
/// blank mid-draft. (An indefinite "pin" existed once; its UI button read as clutter and was
/// removed with it.)
/// </para>
/// </summary>
public sealed class TurnTracker
{
    private long? _manualCellId;
    private long? _manualTurnCellId;
    private long? _lastResolvedCellId;

    /// <summary>Switches the list to a seat. Auto-following resumes once the turn moves on.</summary>
    public void SelectManually(long cellId, DraftState state)
    {
        _manualCellId = cellId;
        _manualTurnCellId = state.Turn?.CellId;
    }

    /// <summary>Forgets the manual selection; called when a new champion select starts.</summary>
    public void Reset()
    {
        _manualCellId = null;
        _manualTurnCellId = null;
        _lastResolvedCellId = null;
    }

    /// <summary>
    /// Resolves the seat to advise, or <see langword="null"/> when there is nothing to advise on.
    /// </summary>
    public RecommendationTarget? Resolve(DraftState state)
    {
        if (!state.IsActive || state.Allies.Count == 0)
            return null;

        var turn = state.Turn;
        var isAllyTurn = turn is { IsAlly: true };

        // A manual click holds until the clock passes to someone else.
        if (_manualCellId is { } manual && turn?.CellId == _manualTurnCellId && FindAlly(state, manual) is { } manualSlot)
            return Build(manualSlot, turn, isFollowingTurn: isAllyTurn && turn!.CellId == manual);

        _manualCellId = null;

        // Normal case: follow the clock.
        if (isAllyTurn && FindAlly(state, turn!.CellId) is { } activeSlot)
        {
            _lastResolvedCellId = activeSlot.CellId;
            return new RecommendationTarget(activeSlot, turn.Action, IsFollowingTurn: true);
        }

        // Enemy is picking, or we are in planning or finalization: keep showing the last seat,
        // falling back to the local player so the panel has something useful on it.
        var fallback = (_lastResolvedCellId is { } last ? FindAlly(state, last) : null)
            ?? state.LocalSlot
            ?? state.Allies[0];

        _lastResolvedCellId = fallback.CellId;
        return Build(fallback, turn, isFollowingTurn: false);
    }

    private static RecommendationTarget Build(DraftSlot slot, ActiveTurn? turn, bool isFollowingTurn)
    {
        // Outside a live turn, pick recommendations are the useful default; during a ban round the
        // whole team is thinking about bans, so mirror that.
        var action = isFollowingTurn && turn is not null
            ? turn.Action
            : turn?.Action == TurnAction.Ban ? TurnAction.Ban : TurnAction.Pick;

        return new RecommendationTarget(slot, action, isFollowingTurn);
    }

    private static DraftSlot? FindAlly(DraftState state, long cellId)
        => state.Allies.FirstOrDefault(slot => slot.CellId == cellId);
}
