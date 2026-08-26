using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

public class DraftStateTests
{
    [Fact]
    public void NullSession_IsInactive()
    {
        Assert.False(DraftState.From(null).IsActive);
    }

    [Fact]
    public void Spectating_IsInactive()
    {
        // Spectated drafts are not ours to advise on.
        Assert.False(DraftState.From(new SessionBuilder().Spectating().Build()).IsActive);
    }

    [Theory]
    [InlineData(0, Lane.Top)]
    [InlineData(1, Lane.Jungle)]
    [InlineData(2, Lane.Mid)]
    [InlineData(3, Lane.Adc)]
    [InlineData(4, Lane.Support)]
    public void AllyLanes_ComeFromAssignedPosition(int cellId, Lane expected)
    {
        var state = DraftState.From(new SessionBuilder().Build());

        Assert.Equal(expected, state.Allies[cellId].AssignedLane);
    }

    [Fact]
    public void EnemyLanes_AreAlwaysUnknown()
    {
        // Riot hides enemy positions; anything else would be a parsing bug pretending to know.
        var state = DraftState.From(new SessionBuilder().Build());

        Assert.All(state.Enemies, slot => Assert.Equal(Lane.Unknown, slot.AssignedLane));
    }

    [Fact]
    public void MissingAllyPosition_IsUnknownRatherThanGuessed()
    {
        var state = DraftState.From(new SessionBuilder().AllyLane(0, string.Empty).Build());

        Assert.Equal(Lane.Unknown, state.Allies[0].AssignedLane);
    }

    [Fact]
    public void Bans_MergeSummaryAndCompletedActions()
    {
        var session = new SessionBuilder()
            .SummaryBans(ally: [157], enemy: [555])
            .CompletedBan(actorCellId: 1, championId: 84)
            .CompletedBan(actorCellId: 6, championId: 200)
            .Build();

        var state = DraftState.From(session);

        Assert.Equal([157, 84], state.AllyBans);
        Assert.Equal([555, 200], state.EnemyBans);
    }

    [Fact]
    public void Bans_AreNotDuplicatedWhenPresentInBothSources()
    {
        var session = new SessionBuilder()
            .SummaryBans(ally: [157], enemy: [])
            .CompletedBan(actorCellId: 0, championId: 157)
            .Build();

        Assert.Equal([157], DraftState.From(session).AllyBans);
    }

    [Fact]
    public void IncompleteBan_IsNotCountedYet()
    {
        var session = new SessionBuilder().Action(0, 157, "ban", completed: false, inProgress: true).Build();

        Assert.Empty(DraftState.From(session).AllyBans);
    }

    [Fact]
    public void Unavailable_CoversBansAndLockedPicks()
    {
        var session = new SessionBuilder()
            .SummaryBans(ally: [157], enemy: [555])
            .Locked(0, 266)
            .Locked(5, 24)
            .Hovering(2, 103)
            .Build();

        var state = DraftState.From(session);

        Assert.Contains(157, state.Unavailable);
        Assert.Contains(555, state.Unavailable);
        Assert.Contains(266, state.Unavailable);
        Assert.Contains(24, state.Unavailable);

        // A hover is not a commitment, so it stays available.
        Assert.DoesNotContain(103, state.Unavailable);
    }

    [Fact]
    public void Turn_PrefersLocalPlayerWhenSeveralActionsRunAtOnce()
    {
        var session = new SessionBuilder()
            .LocalPlayer(2)
            .OnClock(1, "ban")
            .OnClock(2, "ban")
            .OnClock(6, "ban")
            .Build();

        var turn = DraftState.From(session).Turn;

        Assert.NotNull(turn);
        Assert.Equal(2, turn.CellId);
        Assert.True(turn.IsLocalPlayer);
        Assert.Equal(TurnAction.Ban, turn.Action);
    }

    [Fact]
    public void Turn_PrefersLowestAllyCellWhenLocalPlayerIsNotActing()
    {
        var session = new SessionBuilder()
            .LocalPlayer(2)
            .OnClock(6, "pick")
            .OnClock(3, "pick")
            .OnClock(1, "pick")
            .Build();

        var turn = DraftState.From(session).Turn;

        Assert.NotNull(turn);
        Assert.Equal(1, turn.CellId);
        Assert.True(turn.IsAlly);
    }

    [Fact]
    public void Turn_FallsBackToEnemyWhenOnlyTheyAreActing()
    {
        var session = new SessionBuilder().LocalPlayer(2).OnClock(7, "pick").Build();

        var turn = DraftState.From(session).Turn;

        Assert.NotNull(turn);
        Assert.Equal(7, turn.CellId);
        Assert.False(turn.IsAlly);
    }

    [Fact]
    public void Turn_IgnoresCompletedAndUnknownActions()
    {
        var session = new SessionBuilder()
            .Action(0, 266, "pick", completed: true, inProgress: false)
            .Action(1, 0, "ten_bans_reveal", completed: false, inProgress: true)
            .Build();

        Assert.Null(DraftState.From(session).Turn);
    }

    [Theory]
    [InlineData("PLANNING", DraftPhase.Planning)]
    [InlineData("BAN_PICK", DraftPhase.BanPick)]
    [InlineData("FINALIZATION", DraftPhase.Finalization)]
    [InlineData("SOMETHING_NEW", DraftPhase.Unknown)]
    public void Phase_IsMappedAndUnknownIsTolerated(string raw, DraftPhase expected)
    {
        Assert.Equal(expected, DraftState.From(new SessionBuilder().Phase(raw).Build()).Phase);
    }

    [Fact]
    public void EffectiveChampion_PrefersLockedOverHover()
    {
        var session = new SessionBuilder().Hovering(0, 103).Locked(0, 266).Build();

        Assert.Equal(266, DraftState.From(session).Allies[0].EffectiveChampionId);
    }
}
