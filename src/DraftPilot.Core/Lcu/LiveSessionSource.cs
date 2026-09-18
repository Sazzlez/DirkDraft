namespace DraftPilot.Core.Lcu;

/// <summary>
/// Champion select sessions straight from the running client: the lockfile tells us where it is,
/// the event socket pushes every change, and an HTTP read seeds the current state whenever the
/// socket (re)connects.
/// </summary>
public sealed class LiveSessionSource : ISessionSource
{
    private const string SessionUri = "/lol-champ-select/v1/session";
    private const string GameflowUri = "/lol-gameflow/v1/gameflow-phase";

    /// <summary>
    /// Gameflow phases that positively mean champion select is not running.
    /// <para>
    /// Deliberately a list of known values rather than "anything that is not ChampSelect". An
    /// unrecognised or empty phase should leave the panel alone; closing it on a value we do not
    /// understand throws away a working draft view for no reason.
    /// </para>
    /// </summary>
    private static readonly string[] PhasesOutsideChampSelect =
    [
        "None",
        "Lobby",
        "Matchmaking",
        "ReadyCheck",
        "GameStart",
        "InProgress",
        "Reconnect",
        "WaitingForStats",
        "PreEndOfGame",
        "EndOfGame",
        "TerminatedInError",
        "FailedToLaunch",
    ];

    private readonly LockfileWatcher _watcher;
    private readonly Action<string>? _diagnostic;

    /// <summary>Serialises restart and teardown; without it two Connected events in quick
    /// succession leaked a still-running socket that kept delivering every event twice.</summary>
    private readonly SemaphoreSlim _clientGate = new(1, 1);
    private int _generation;
    private int _started;

    private CancellationTokenSource? _clientScope;
    private LcuClient? _client;
    private LcuEventSocket? _socket;

    private volatile bool _sessionOpen;

    // Once the socket has delivered a session or phase itself, the HTTP seed for that resource is
    // stale by definition and gets discarded — the seed races the subscription, and a seed
    // response landing AFTER a socket event used to roll the panel back to an older draft state.
    private volatile bool _socketDeliveredSession;
    private volatile bool _socketDeliveredPhase;

    /// <param name="diagnostic">
    /// Optional sink for connection notes, e.g. why a session was closed. The command-line tools
    /// print these; the window ignores them.
    /// </param>
    public LiveSessionSource(string? explicitLockfilePath = null, Action<string>? diagnostic = null)
    {
        _watcher = new LockfileWatcher(explicitLockfilePath);
        _diagnostic = diagnostic;
    }

    public event Action<string?>? SessionJson;

    public event Action<ClientStatus>? StatusChanged;

    public event Action<string>? GameflowPhase;

    /// <summary>Raised whenever <see cref="Client"/> changes — connect, reconnect or teardown.</summary>
    public event Action? ClientChanged;

    /// <summary>The HTTP client for the running instance, or null while the client is down.</summary>
    public LcuClient? Client => _client;

    public async Task RunAsync(CancellationToken ct)
    {
        if (_watcher.LockfilePath is null)
        {
            StatusChanged?.Invoke(ClientStatus.NotFound);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown; a caller awaiting RunAsync must not see a fault.
            }

            return;
        }

        // A second RunAsync would double-subscribe the watcher and from then on run two restarts
        // per Connected event.
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("RunAsync läuft bereits.");

        _watcher.Connected += OnClientUp;
        _watcher.Disconnected += OnClientDown;

        StatusChanged?.Invoke(ClientStatus.Offline);

        if (!await _watcher.StartAsync(ct).ConfigureAwait(false))
        {
            // A lockfile path was resolved but its directory is gone (moved install, unmounted
            // drive): nothing will ever be observed, and pretending to be merely offline would
            // have the user waiting forever.
            StatusChanged?.Invoke(ClientStatus.NotFound);
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            await TearDownClientAsync().ConfigureAwait(false);
        }
    }

    private void OnClientUp(LcuCredentials credentials)
    {
        _ = Observed(RestartAsync(credentials), "Neuverbindung");
    }

    private void OnClientDown()
    {
        _ = Observed(TearDownClientAsync(), "Trennung");
        EndSession("Client nicht mehr erreichbar");
        StatusChanged?.Invoke(ClientStatus.Offline);
    }

    /// <summary>Fire-and-forget tasks still get their failures written somewhere. Without this a
    /// broken connection attempt was simply invisible: the tool looked offline for no reason.</summary>
    private async Task Observed(Task task, string what)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostic?.Invoke($"{what} fehlgeschlagen: {ex.Message}");
        }
    }

    private async Task RestartAsync(LcuCredentials credentials)
    {
        var generation = Interlocked.Increment(ref _generation);

        await _clientGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A newer restart is already queued behind us; building a connection it would
            // immediately tear down again is pure churn.
            if (generation != Volatile.Read(ref _generation))
                return;

            await TearDownCurrentAsync().ConfigureAwait(false);

            var scope = new CancellationTokenSource();
            var client = new LcuClient(credentials) { Diagnostic = _diagnostic };

            var socket = new LcuEventSocket(credentials);
            socket.EventReceived += OnLcuEvent;
            socket.ConnectionChanged += OnSocketConnectionChanged;

            _clientScope = scope;
            _client = client;
            _socket = socket;
            ClientChanged?.Invoke();

            // Fire and forget: the socket reconnects on its own until the scope is cancelled.
            // The main seed happens in OnSocketConnectionChanged, AFTER the subscription is
            // live — a seed taken before it raced the first events and could overwrite newer
            // state. The extra seed below is the safety net for a socket that NEVER connects
            // (firewall, subprotocol change): without it there would be no state at all. The
            // _socketDelivered* flags keep it from overwriting anything newer.
            _ = Observed(socket.RunAsync(scope.Token), "Event-Socket");
            _ = Observed(SeedAsync(client, scope.Token), "Erst-Abgleich");
        }
        finally
        {
            _clientGate.Release();
        }
    }

    private void OnSocketConnectionChanged(bool connected)
    {
        StatusChanged?.Invoke(new ClientStatus(
            LeagueFound: true,
            ClientRunning: true,
            SocketConnected: connected,
            Detail: connected ? null : "Verbindung zum Client unterbrochen"));

        if (!connected)
        {
            // The next connect must seed afresh: whatever the socket delivered belongs to the
            // link that just died.
            _socketDeliveredSession = false;
            _socketDeliveredPhase = false;

            // Deliberately NOT ending the session. A dropped socket says nothing about the game —
            // and treating it as "champion select is over" wiped the build, the fetched counter
            // edges and the whole per-draft budget in the middle of a draft, over a hiccup that
            // repairs itself in a second. The panel keeps what it has, the status line says the
            // link is down, and the reseed on reconnect decides: it closes the panel only when the
            // client positively answers that there is no champion select any more.
            return;
        }

        // Every (re)connect seeds the current state over HTTP. This is what repaints the panel
        // after a socket drop during finalization, where no further session events would come.
        if (_client is { } client && _clientScope is { } scope)
            _ = Observed(SeedAsync(client, scope.Token), "Status-Abgleich");
    }

    private async Task SeedAsync(LcuClient client, CancellationToken ct)
    {
        try
        {
            // Phase first: started mid-game, the build view should come up right away — and a
            // phase outside champion select makes reading the session pointless.
            var phase = await client.GetGameflowPhaseAsync(ct).ConfigureAwait(false);

            if (phase is { Length: > 0 } && !_socketDeliveredPhase)
                HandlePhase(phase);

            var outside = phase is { Length: > 0 }
                && PhasesOutsideChampSelect.Contains(phase, StringComparer.OrdinalIgnoreCase);

            if (outside)
                return;

            var session = await client.ReadChampSelectSessionAsync(ct).ConfigureAwait(false);

            // Discarded when the socket has spoken in the meantime: its event is newer.
            if (_socketDeliveredSession || ct.IsCancellationRequested)
                return;

            if (session.Json is { } raw)
            {
                _sessionOpen = true;
                SessionJson?.Invoke(raw);
            }
            else if (session.Answered)
            {
                // The client says there is no champion select. This is the only place a link loss
                // can still close the panel — and it does so on an answer, not on a silence.
                EndSession("Kein Champ Select mehr");
            }
        }
        catch (OperationCanceledException)
        {
            // Client went away again while we were seeding.
        }
    }

    private void OnLcuEvent(LcuEvent lcuEvent)
    {
        if (lcuEvent.Uri.Equals(SessionUri, StringComparison.OrdinalIgnoreCase))
        {
            _socketDeliveredSession = true;

            if (lcuEvent.EventType.Equals("Delete", StringComparison.OrdinalIgnoreCase) || lcuEvent.Data is null)
            {
                EndSession("Session-Ressource gelöscht");
                return;
            }

            _sessionOpen = true;
            SessionJson?.Invoke(lcuEvent.Data);
            return;
        }

        if (!lcuEvent.Uri.Equals(GameflowUri, StringComparison.OrdinalIgnoreCase))
            return;

        // The session resource is deleted a moment after champion select ends; the gameflow phase
        // moves first, so use it to close the panel promptly.
        var phase = lcuEvent.Data?.Trim('"');

        if (phase is not { Length: > 0 })
            return;

        _socketDeliveredPhase = true;
        HandlePhase(phase);
    }

    private void HandlePhase(string phase)
    {
        // Forwarded as-is: the in-game build view opens on GameStart/InProgress and closes after.
        GameflowPhase?.Invoke(phase);

        if (PhasesOutsideChampSelect.Contains(phase, StringComparer.OrdinalIgnoreCase))
            EndSession($"Gameflow-Phase {phase}");
    }

    /// <param name="reason">Why the session is being closed; surfaced to the diagnostic callback.</param>
    private void EndSession(string reason)
    {
        if (!_sessionOpen)
            return;

        _sessionOpen = false;
        _diagnostic?.Invoke($"Session beendet: {reason}");
        SessionJson?.Invoke(null);
    }

    private async Task TearDownClientAsync()
    {
        await _clientGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await TearDownCurrentAsync().ConfigureAwait(false);
        }
        finally
        {
            _clientGate.Release();
        }
    }

    /// <summary>Must be called with <see cref="_clientGate"/> held.</summary>
    private async Task TearDownCurrentAsync()
    {
        var scope = _clientScope;
        var socket = _socket;
        var client = _client;

        _clientScope = null;
        _socket = null;
        _client = null;
        _socketDeliveredSession = false;
        _socketDeliveredPhase = false;

        if (scope is null && socket is null && client is null)
            return;

        ClientChanged?.Invoke();

        // Unsubscribe FIRST: the cancel below wakes the socket loop on a pool thread, and its
        // dying breath (a drop notification, an EndSession) must not land in our handlers after
        // a reconnect has already seeded fresh state.
        if (socket is not null)
        {
            socket.EventReceived -= OnLcuEvent;
            socket.ConnectionChanged -= OnSocketConnectionChanged;
        }

        if (scope is not null)
        {
            await scope.CancelAsync().ConfigureAwait(false);
            scope.Dispose();
        }

        if (socket is not null)
            await socket.DisposeAsync().ConfigureAwait(false);

        client?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.Connected -= OnClientUp;
        _watcher.Disconnected -= OnClientDown;
        _watcher.Dispose();
        await TearDownClientAsync().ConfigureAwait(false);
    }
}
