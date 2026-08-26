using System.Text.Json;
using DraftPilot.Core.Lcu;
using DraftPilot.Core.Lcu.Models;

namespace DraftPilot.Core.Draft;

/// <summary>Everything the UI needs after one change.</summary>
public sealed record DraftSnapshot(DraftState State, RecommendationTarget? Target, ClientStatus Status);

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

    private Pending? _pending;
    private bool _flushScheduled;
    private ClientStatus _status = ClientStatus.Offline;
    private bool _wasActive;

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

    /// <summary>Re-resolves the advised seat after the user clicked a slot or toggled the pin.</summary>
    public void RefreshTarget()
    {
        Target = Turns.Resolve(State);
        Changed?.Invoke(new DraftSnapshot(State, Target, _status));
    }

    private void OnStatusChanged(ClientStatus status)
    {
        _status = status;
        Changed?.Invoke(new DraftSnapshot(State, Target, status));
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
        var session = Deserialize(json);
        var state = DraftState.From(session);

        // A fresh champion select must not inherit the previous draft's pin or manual selection.
        if (state.IsActive && !_wasActive)
            Turns.Reset();

        _wasActive = state.IsActive;

        State = state;
        Target = Turns.Resolve(state);
        Changed?.Invoke(new DraftSnapshot(state, Target, _status));
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
