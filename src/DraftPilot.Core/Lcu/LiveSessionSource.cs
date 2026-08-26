namespace DraftPilot.Core.Lcu;

/// <summary>
/// Champion select sessions straight from the running client: the lockfile tells us where it is,
/// the event socket pushes every change, and a single HTTP read seeds the current state on connect.
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

    private CancellationTokenSource? _clientScope;
    private LcuClient? _client;
    private LcuEventSocket? _socket;
    private bool _sessionOpen;

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

    /// <summary>The HTTP client for the running instance, or null while the client is down.</summary>
    public LcuClient? Client => _client;

    public async Task RunAsync(CancellationToken ct)
    {
        if (_watcher.LockfilePath is null)
        {
            StatusChanged?.Invoke(ClientStatus.NotFound);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            return;
        }

        _watcher.Connected += OnClientUp;
        _watcher.Disconnected += OnClientDown;

        StatusChanged?.Invoke(ClientStatus.Offline);
        await _watcher.StartAsync(ct).ConfigureAwait(false);

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
        _ = RestartAsync(credentials);
    }

    private void OnClientDown()
    {
        _ = TearDownClientAsync();
        EndSession("Client nicht mehr erreichbar");
        StatusChanged?.Invoke(ClientStatus.Offline);
    }

    private async Task RestartAsync(LcuCredentials credentials)
    {
        await TearDownClientAsync().ConfigureAwait(false);

        var scope = new CancellationTokenSource();
        _clientScope = scope;
        _client = new LcuClient(credentials);

        var socket = new LcuEventSocket(credentials);
        socket.EventReceived += OnLcuEvent;
        socket.ConnectionChanged += OnSocketConnectionChanged;
        _socket = socket;

        // Fire and forget: the socket reconnects on its own until the scope is cancelled.
        _ = socket.RunAsync(scope.Token);

        // Seed the current state; the user may start the tool while already in champion select.
        try
        {
            var raw = await _client.GetChampSelectSessionRawAsync(scope.Token).ConfigureAwait(false);
            if (raw is not null)
            {
                _sessionOpen = true;
                SessionJson?.Invoke(raw);
            }
        }
        catch (OperationCanceledException)
        {
            // Client went away again while we were seeding.
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
            EndSession("Event-Socket getrennt");
    }

    private void OnLcuEvent(LcuEvent lcuEvent)
    {
        if (lcuEvent.Uri.Equals(SessionUri, StringComparison.OrdinalIgnoreCase))
        {
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

        if (phase is { Length: > 0 } && PhasesOutsideChampSelect.Contains(phase, StringComparer.OrdinalIgnoreCase))
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
        var scope = _clientScope;
        var socket = _socket;
        var client = _client;

        _clientScope = null;
        _socket = null;
        _client = null;


        if (scope is not null)
        {
            await scope.CancelAsync().ConfigureAwait(false);
            scope.Dispose();
        }

        if (socket is not null)
        {
            socket.EventReceived -= OnLcuEvent;
            socket.ConnectionChanged -= OnSocketConnectionChanged;
            await socket.DisposeAsync().ConfigureAwait(false);
        }

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
