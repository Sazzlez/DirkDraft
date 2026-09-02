using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// When a pick list stops being advice. This decides which content the widest column carries for
/// the last minute of every draft, so the boundary matters more than it looks: one seat too many
/// and the panel hides a list somebody still needs.
/// </summary>
public class RecommendationTargetTests
{
    private const int Mine = 122, Ally = 64, Enemy = 24;

    private static RecommendationTarget Target(DraftState state, bool following)
        => new(state.LocalSlot!, TurnAction.Pick, following);

    [Fact]
    public void OurOwnLockedSeatWithNobodyOnTheClock_IsSettled()
    {
        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(0)
            .Locked(0, Mine)
            .Locked(7, Enemy)
            .Build());

        Assert.True(Target(state, following: false).IsSettled);
    }

    /// <summary>
    /// The seat is on the clock — that is a live turn, and the list is the whole point of the
    /// window. Not settled even though the seat happens to carry a champion already (a hover, or a
    /// swap still being decided).
    /// </summary>
    [Fact]
    public void ASeatOnTheClock_IsNeverSettled()
    {
        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(0)
            .Locked(0, Mine)
            .OnClock(0, "pick")
            .Build());

        Assert.False(Target(state, following: true).IsSettled);
    }

    [Fact]
    public void AnUnlockedSeat_IsNeverSettled()
    {
        // Before our own pick there is nothing settled about it, whoever is on the clock.
        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(0)
            .Hovering(0, Mine)
            .Locked(7, Enemy)
            .Build());

        Assert.False(Target(state, following: false).IsSettled);
    }

    /// <summary>
    /// A team-mate on the clock keeps the list, which is why the rule reads the ADVISED seat rather
    /// than the local player: the advised seat is then the team-mate, and theirs is not locked.
    /// </summary>
    [Fact]
    public void ATeamMateStillPicking_KeepsTheList()
    {
        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(0)
            .Locked(0, Mine)
            .OnClock(1, "pick")
            .Build());

        var target = new TurnTracker().Resolve(state);

        Assert.NotNull(target);
        Assert.Equal(1, target.Slot.CellId);
        Assert.False(target.IsSettled);
    }

    /// <summary>
    /// The state the panel was built for: our pick is in, a team-mate has already picked too, and
    /// the enemy is on the clock. The tracker falls back to the last advised seat, and whichever it
    /// picks is locked — so the list has nothing left to say.
    /// </summary>
    [Fact]
    public void WithTheEnemyOnTheClockAfterOurTeamHasPicked_TheListIsSettled()
    {
        var tracker = new TurnTracker();

        var picking = DraftState.From(new SessionBuilder()
            .LocalPlayer(0)
            .Locked(0, Mine)
            .OnClock(1, "pick")
            .Build());

        Assert.False(tracker.Resolve(picking)!.IsSettled);

        var enemyTurn = DraftState.From(new SessionBuilder()
            .LocalPlayer(0)
            .Locked(0, Mine)
            .Locked(1, Ally)
            .OnClock(8, "pick")
            .Build());

        var target = tracker.Resolve(enemyTurn);

        Assert.NotNull(target);
        Assert.True(target.IsSettled);
    }
}
