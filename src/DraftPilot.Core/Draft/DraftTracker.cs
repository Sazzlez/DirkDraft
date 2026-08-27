using System.Text.Json;
using DraftPilot.Core.Lcu;
using DraftPilot.Core.Lcu.Models;

namespace DraftPilot.Core.Draft;

/// <summary>Everything the UI needs after one change.</summary>
/// <param name="IsNewDraft">
/// True on the first snapshot of a fresh champion select — including the client jumping straight
/// from one draft into the next without an inactive frame in between. Consumers reset their
/// per-draft state on this, not on their own active-flag bookkeeping, which missed that jump.
/// </param>
public sealed record DraftSnapshot(DraftState State, RecommendationTarget? Target, ClientStatus Status, bool IsNewDraft = false);

/// <summary>
/// Turns raw session payloads into <see cref="DraftState"/> and resolves which seat to advise.
/// Bursts of events — the client sends several as a ban round resolves — are coalesced so the UI
/// updates once instead of flickering.
/// </summary>
public sealed class DraftTracker : IAsyncDisposable
{
    private readonly ISessionSource _source;
    private readonly TimeSpan _debounce;
    private readonly Lock _pendingLock = new();

    /// <summary>
    /// Serialises <see cref="Apply"/> (debounce task or socket thread) against
    /// <see cref="RefreshTarget"/> (UI thread). Without it a click on a slot could race an
    /// incoming event inside <see cref="TurnTracker.Resolve"/> and lose the manual selection.
    /// </summary>
    private readonly Lock _applyLock = new();

    private Pending? _pending;
    private bool _flushScheduled;
    private ClientStatus _status = ClientStatus.Offline;
    private bool _wasActive;
    private long _lastLocalCellId = long.MinValue;
    private int _lastProgress;

    /// <param name="debounceMs">
    /// Coalescing window. 100 ms keeps the panel calm during fast ban rounds; the tests use 0.
    /// </param>
    public DraftTracker(ISessionSource source, int debounceMs = 100)
    {
        _source = source;
        _debounce = TimeSpan.FromMilliseconds(debounceMs);

        _source.SessionJson += OnSessionJson;
        _source.StatusChanged += OnStatusChanged;
    }

    /// <summary>Raised after every coalesced change.</summary>
    public event Action<DraftSnapshot>? Changed;

    public TurnTracker Turns { get; } = new();

    public DraftState State { get; private set; } = DraftState.Inactive;

    public RecommendationTarget? Target { get; private set; }

    public ClientStatus Status => _status;

    public Task RunAsync(CancellationToken ct) => _source.RunAsync(ct);

    /// <summary>Re-resolves the advised seat after the user clicked a slot.</summary>
    public void RefreshTarget()
    {
        lock (_applyLock)
        {
            Target = Turns.Resolve(State);
            Changed?.Invoke(new DraftSnapshot(State, Target, _status));
        }
    }

    private void OnStatusChanged(ClientStatus status)
    {
        // Under the same lock as Apply: read outside it, State and Target could come from two
        // different updates and publish a torn snapshot.
        lock (_applyLock)
        {
            _status = status;
            Changed?.Invoke(new DraftSnapshot(State, Target, status));
        }
    }

    private void OnSessionJson(string? json)
    {
        // The end of champion select should close the panel at once, not after the coalescing window.
        if (json is null)
        {
            lock (_pendingLock)
            {
                _pending = null;
                _flushScheduled = false;
            }

            Apply(null);
            return;
        }

        bool schedule;
        lock (_pendingLock)
        {
            _pending = new Pending(json);
            schedule = !_flushScheduled;
            _flushScheduled = true;
        }

        if (!schedule)
            return;

        if (_debounce <= TimeSpan.Zero)
        {
            Flush();
            return;
        }

        _ = FlushAfterDelayAsync();
    }

    private async Task FlushAfterDelayAsync()
    {
        await Task.Delay(_debounce).ConfigureAwait(false);
        Flush();
    }

    private void Flush()
    {
        Pending? pending;
        lock (_pendingLock)
        {
            pending = _pending;
            _pending = null;
            _flushScheduled = false;
        }

        if (pending is null)
            return;

        Apply(pending.Json);
    }

    private void Apply(string? json)
    {
        lock (_applyLock)
        {
            DraftState state;

            try
            {
                state = DraftState.From(Deserialize(json));
            }
            catch (Exception)
            {
                // Safety net for a payload From cannot digest. Escaping here would kill the
                // socket's receive loop (or die unobserved in the debounce task); keeping the
                // last good state on screen is strictly better than either.
                return;
            }

            // A fresh champion select must not inherit the previous draft's manual selection —
            // also when the client jumps straight from one draft into the next without a null in
            // between. Within one draft the ban/lock count only ever grows, so a shrink means a
            // new session; so does a different own seat.
            var progress = state.Unavailable.Count;
            var isNewDraft = state.IsActive
                && (!_wasActive || progress < _lastProgress || state.LocalCellId != _lastLocalCellId);

            if (isNewDraft)
                Turns.Reset();

            _wasActive = state.IsActive;
            _lastProgress = progress;
            _lastLocalCellId = state.LocalCellId;

            State = state;
            Target = Turns.Resolve(state);
            Changed?.Invoke(new DraftSnapshot(state, Target, _status, isNewDraft));
        }
    }

    private static ChampSelectSession? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize(json, LcuJson.Default.ChampSelectSession);
        }
        catch (JsonException)
        {
            // A shape change on Riot's side degrades the display instead of taking the tool down.
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _source.SessionJson -= OnSessionJson;
        _source.StatusChanged -= OnStatusChanged;
        await _source.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record Pending(string Json);
}
