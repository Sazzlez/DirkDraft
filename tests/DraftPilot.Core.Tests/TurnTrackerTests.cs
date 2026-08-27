using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

public class TurnTrackerTests
{
    [Fact]
    public void FollowsAllyOnTheClock()
    {
        var state = DraftState.From(new SessionBuilder().LocalPlayer(2).OnClock(4, "pick").Build());
        var target = new TurnTracker().Resolve(state);

        Assert.NotNull(target);
        Assert.Equal(4, target.Slot.CellId);
        Assert.Equal(Lane.Support, target.Slot.AssignedLane);
        Assert.Equal(TurnAction.Pick, target.Action);
        Assert.True(target.IsFollowingTurn);
    }

    [Fact]
    public void SwitchesAsTheClockMoves()
    {
        var tracker = new TurnTracker();

        tracker.Resolve(DraftState.From(new SessionBuilder().OnClock(0, "pick").Build()));
        var second = tracker.Resolve(DraftState.From(new SessionBuilder().OnClock(3, "pick").Build()));

        Assert.NotNull(second);
        Assert.Equal(3, second.Slot.CellId);
    }

    [Fact]
    public void EnemyOnTheClock_KeepsLastAllySeatAsOutlook()
    {
        var tracker = new TurnTracker();
        tracker.Resolve(DraftState.From(new SessionBuilder().OnClock(1, "pick").Build()));

        var target = tracker.Resolve(DraftState.From(new SessionBuilder().OnClock(7, "pick").Build()));

        Assert.NotNull(target);
        Assert.Equal(1, target.Slot.CellId);
        Assert.False(target.IsFollowingTurn);
    }

    [Fact]
    public void NobodyOnTheClock_FallsBackToLocalPlayer()
    {
        var state = DraftState.From(new SessionBuilder().LocalPlayer(3).Phase("PLANNING").Build());

        var target = new TurnTracker().Resolve(state);

        Assert.NotNull(target);
        Assert.Equal(3, target.Slot.CellId);
        Assert.False(target.IsFollowingTurn);
        Assert.Equal(TurnAction.Pick, target.Action);
    }

    [Fact]
    public void BanRoundOutlook_ShowsBansRatherThanPicks()
    {
        // While the enemy is banning, the whole team is thinking about bans too.
        var tracker = new TurnTracker();
        var state = DraftState.From(new SessionBuilder().LocalPlayer(2).OnClock(8, "ban").Build());

        var target = tracker.Resolve(state);

        Assert.NotNull(target);
        Assert.Equal(TurnAction.Ban, target.Action);
        Assert.False(target.IsFollowingTurn);
    }

    [Fact]
    public void ManualSelection_HoldsWhileTheSameSeatIsOnTheClock()
    {
        var tracker = new TurnTracker();
        var state = DraftState.From(new SessionBuilder().OnClock(0, "pick").Build());
        tracker.Resolve(state);

        tracker.SelectManually(2, state);
        var target = tracker.Resolve(state);

        Assert.NotNull(target);
        Assert.Equal(2, target.Slot.CellId);
        Assert.False(target.IsFollowingTurn);
    }

    [Fact]
    public void ManualSelection_ReleasesWhenTheClockMovesOn()
    {
        var tracker = new TurnTracker();
        var first = DraftState.From(new SessionBuilder().OnClock(0, "pick").Build());
        tracker.Resolve(first);
        tracker.SelectManually(2, first);

        var target = tracker.Resolve(DraftState.From(new SessionBuilder().OnClock(3, "pick").Build()));

        Assert.NotNull(target);
        Assert.Equal(3, target.Slot.CellId);
        Assert.True(target.IsFollowingTurn);
    }

    [Fact]
    public void ManualSelection_CanOnlyTargetOwnTeam()
    {
        // Enemy seats are not advisable; the request is ignored rather than half-applied.
        var tracker = new TurnTracker();
        var state = DraftState.From(new SessionBuilder().OnClock(0, "pick").Build());

        tracker.SelectManually(7, state);
        var target = tracker.Resolve(state);

        Assert.NotNull(target);
        Assert.Equal(0, target.Slot.CellId);
    }

    [Fact]
    public void Reset_ClearsTheManualSelection()
    {
        var tracker = new TurnTracker();
        var state = DraftState.From(new SessionBuilder().OnClock(0, "pick").Build());
        tracker.SelectManually(2, state);

        tracker.Reset();

        var target = tracker.Resolve(state);
        Assert.NotNull(target);
        Assert.Equal(0, target.Slot.CellId);
    }

    [Fact]
    public void InactiveDraft_HasNoTarget()
    {
        Assert.Null(new TurnTracker().Resolve(DraftState.Inactive));
    }
}
