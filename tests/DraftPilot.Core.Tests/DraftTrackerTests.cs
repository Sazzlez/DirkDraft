using System.Text.Json;
using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu;
using DraftPilot.Core.Lcu.Models;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The tracker's draft-boundary detection. Consumers reset their per-draft state on
/// <see cref="DraftSnapshot.IsNewDraft"/>, so getting the boundary wrong either leaks the previous
/// draft's overrides into the next one or wipes state mid-draft.
/// </summary>
public class DraftTrackerTests
{
    /// <summary>Feeds payloads by hand; RunAsync is never used.</summary>
    private sealed class ManualSource : ISessionSource
    {
        public event Action<string?>? SessionJson;
        public event Action<ClientStatus>? StatusChanged;
#pragma warning disable CS0067
        public event Action<string>? GameflowPhase;
#pragma warning restore CS0067

        public void Push(ChampSelectSession session)
            => SessionJson?.Invoke(JsonSerializer.Serialize(session, LcuJson.Default.ChampSelectSession));

        public void PushEnd() => SessionJson?.Invoke(null);

        public void PushStatus(ClientStatus status) => StatusChanged?.Invoke(status);

        public Task RunAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static ChampSelectSession Session(params (long Cell, int Champion)[] locks)
    {
        var builder = new SessionBuilder().LocalPlayer(2).OnClock(2, "pick");

        foreach (var (cell, champion) in locks)
            builder.Locked(cell, champion);

        return builder.Build();
    }

    [Fact]
    public async Task FirstFrameOfADraft_IsMarkedNew()
    {
        var source = new ManualSource();
        await using var tracker = new DraftTracker(source, debounceMs: 0);

        DraftSnapshot? last = null;
        tracker.Changed += snapshot => last = snapshot;

        source.Push(Session((0, 122)));

        Assert.NotNull(last);
        Assert.True(last!.IsNewDraft);

        // The second frame of the same draft is not new.
        source.Push(Session((0, 122), (1, 64)));
        Assert.False(last!.IsNewDraft);
    }

    [Fact]
    public async Task DraftToDraftWithoutAnInactiveFrame_IsMarkedNew()
    {
        var source = new ManualSource();
        await using var tracker = new DraftTracker(source, debounceMs: 0);

        DraftSnapshot? last = null;
        tracker.Changed += snapshot => last = snapshot;

        // A well-progressed first draft…
        source.Push(Session((0, 122), (1, 64), (3, 51)));
        Assert.True(last!.IsNewDraft);

        // …then the client jumps straight into a fresh champion select: fewer locked picks than
        // before, no null in between. Consumers must get the reset signal anyway.
        source.Push(Session((0, 266)));
        Assert.True(last!.IsNewDraft);
    }

    [Fact]
    public async Task StatusChanges_DoNotLookLikeANewDraft()
    {
        var source = new ManualSource();
        await using var tracker = new DraftTracker(source, debounceMs: 0);

        DraftSnapshot? last = null;
        tracker.Changed += snapshot => last = snapshot;

        source.Push(Session((0, 122)));
        source.PushStatus(ClientStatus.Offline);

        Assert.NotNull(last);
        Assert.False(last!.IsNewDraft);
        Assert.True(last.State.IsActive);
    }
}
