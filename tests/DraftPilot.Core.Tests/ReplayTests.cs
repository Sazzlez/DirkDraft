using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Drives a recorded draft through the whole pipeline. This is the regression net that works
/// without a running client.
/// </summary>
public class ReplayTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-draft.jsonl");

    [Fact]
    public async Task Replay_ProducesOneSnapshotPerFrame()
    {
        Assert.Equal(6, (await ReplayAsync()).Count);
    }

    [Fact]
    public async Task Replay_EndsInactive()
    {
        var snapshots = await ReplayAsync();

        Assert.False(snapshots[^1].State.IsActive);
        Assert.Null(snapshots[^1].Target);
    }

    [Fact]
    public async Task Replay_TracksBansAsTheyLand()
    {
        var snapshots = await ReplayAsync();
        var withBans = snapshots.Where(s => s.State.AllyBans.Count > 0).ToList();

        Assert.NotEmpty(withBans);
        Assert.Contains(157, withBans[0].State.AllyBans);
        Assert.Contains(555, withBans[0].State.EnemyBans);
    }

    [Fact]
    public async Task Replay_RevealsEnemyChampionsOnlyOnLockIn()
    {
        var active = (await ReplayAsync()).Where(s => s.State.IsActive).ToList();

        // While an enemy is on the clock, nothing of theirs is visible yet.
        var whileEnemyPicking = active.First(s => s.State.Turn is { IsAlly: false });
        Assert.All(whileEnemyPicking.State.Enemies, slot => Assert.Equal(0, slot.EffectiveChampionId));

        // By the last frame two of them have locked in.
        Assert.Equal(2, active[^1].State.Enemies.Count(slot => slot.IsLocked));
    }

    [Fact]
    public async Task Replay_TargetFollowsTheClockThroughTheDraft()
    {
        var active = (await ReplayAsync()).Where(s => s.Target is not null).ToList();

        // Ban round: our top laner is on the clock.
        var banFrame = active.First(s => s.Target!.Action == TurnAction.Ban && s.Target.IsFollowingTurn);
        Assert.Equal(0, banFrame.Target!.Slot.CellId);

        // Last active frame: the local player is picking on mid.
        var final = active[^1];
        Assert.True(final.Target!.IsFollowingTurn);
        Assert.Equal(2, final.Target.Slot.CellId);
        Assert.Equal(Lane.Mid, final.Target.Slot.AssignedLane);
        Assert.Equal(TurnAction.Pick, final.Target.Action);
    }

    [Fact]
    public async Task Replay_KeepsTargetWhileEnemyPicks()
    {
        var snapshots = await ReplayAsync();
        var enemyTurn = snapshots.First(s => s.State.Turn is { IsAlly: false });

        Assert.NotNull(enemyTurn.Target);
        Assert.False(enemyTurn.Target.IsFollowingTurn);
    }

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
}
