using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Runs against a recording taken from a real 5v5 tournament draft, scrubbed of personal data.
/// Everything asserted here was measured, not assumed — several of these facts contradicted what the
/// code originally expected.
/// </summary>
public class LiveRecordingTests
{
    private static string FixturePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "live-tournament-draft.jsonl");

    private static async Task<List<DraftSnapshot>> ReplayAsync()
    {
        Assert.True(File.Exists(FixturePath), $"Fixture fehlt: {FixturePath}");

        var source = new JsonlSessionSource(FixturePath, double.PositiveInfinity);
        await using var tracker = new DraftTracker(source, debounceMs: 0);

        var snapshots = new List<DraftSnapshot>();
        tracker.Changed += snapshots.Add;

        await tracker.RunAsync(CancellationToken.None);

        // The first notification is the replay source announcing itself, not a draft frame.
        return [.. snapshots.Skip(1)];
    }

    [Fact]
    public async Task RealSession_IsParsed()
    {
        var snapshots = await ReplayAsync();

        Assert.NotEmpty(snapshots);
        Assert.All(snapshots, snapshot => Assert.True(snapshot.State.IsActive));
        Assert.All(snapshots, snapshot => Assert.Equal(5, snapshot.State.Allies.Count));
        Assert.All(snapshots, snapshot => Assert.Equal(5, snapshot.State.Enemies.Count));
    }

    [Fact]
    public async Task AllySeatOrder_IsNotCanonical()
    {
        // Measured, and it is why the seat prior ships flat: the client listed the team as
        // top, jungle, BOTTOM, MIDDLE, utility. Seats 2 and 3 are not mid and adc.
        var state = (await ReplayAsync())[0].State;

        Assert.Equal(Lane.Top, state.Allies[0].AssignedLane);
        Assert.Equal(Lane.Jungle, state.Allies[1].AssignedLane);
        Assert.Equal(Lane.Adc, state.Allies[2].AssignedLane);
        Assert.Equal(Lane.Mid, state.Allies[3].AssignedLane);
        Assert.Equal(Lane.Support, state.Allies[4].AssignedLane);
    }

    [Fact]
    public async Task EnemyPositions_AreNeverRevealed()
    {
        // Confirms the whole reason the lane predictor exists.
        foreach (var snapshot in await ReplayAsync())
            Assert.All(snapshot.State.Enemies, slot => Assert.Equal(Lane.Unknown, slot.AssignedLane));
    }

    [Fact]
    public async Task BanActionInPlanningPhase_CountsAsTheTurn()
    {
        // The client already has the ban action in progress while the phase still reads PLANNING.
        var state = (await ReplayAsync())[0].State;

        Assert.Equal(DraftPhase.Planning, state.Phase);
        Assert.NotNull(state.Turn);
        Assert.Equal(TurnAction.Ban, state.Turn.Action);
        Assert.True(state.Turn.IsLocalPlayer);
    }

    [Fact]
    public async Task TenBansRevealAction_IsIgnored()
    {
        // A real session carries an action of type "ten_bans_reveal" with actorCellId -1. Treating
        // it as somebody's turn would point the recommendation list at a seat that does not exist.
        foreach (var snapshot in await ReplayAsync())
        {
            if (snapshot.State.Turn is { } turn)
                Assert.True(turn.CellId >= 0, $"Zug auf cell={turn.CellId} zugeordnet");
        }
    }

    [Fact]
    public async Task BanSummaryIsEmptyWhileBansAreStillOpen()
    {
        // bans.numBans was 0 and both summary lists empty even though a ban action was in progress.
        // This is why bans are also collected from the completed actions.
        var state = (await ReplayAsync())[0].State;

        Assert.Empty(state.AllyBans);
        Assert.Empty(state.EnemyBans);
    }

    [Fact]
    public async Task AllyHover_IsVisible()
    {
        // The recording ends with the local player hovering a champion but not locked in.
        var snapshots = await ReplayAsync();
        var withHover = snapshots
            .SelectMany(snapshot => snapshot.State.Allies)
            .Where(slot => !slot.IsLocked && slot.HoverChampionId != 0)
            .ToList();

        Assert.NotEmpty(withHover);
    }

    [Fact]
    public async Task TargetSeat_IsResolvedForEveryFrame()
    {
        foreach (var snapshot in await ReplayAsync())
        {
            Assert.NotNull(snapshot.Target);
            Assert.Contains(snapshot.Target.Slot.CellId, snapshot.State.Allies.Select(slot => slot.CellId));
        }
    }
}
