namespace DraftPilot.Core.Lcu;

/// <summary>What we can tell the user about the connection.</summary>
/// <param name="LeagueFound">Whether a League install was located at all.</param>
/// <param name="ClientRunning">Whether the client is up (lockfile present and parsed).</param>
/// <param name="SocketConnected">Whether the event socket is subscribed.</param>
/// <param name="Detail">Human-readable note for the status line, if any.</param>
public sealed record ClientStatus(bool LeagueFound, bool ClientRunning, bool SocketConnected, string? Detail = null)
{
    public static ClientStatus NotFound { get; } =
        new(LeagueFound: false, ClientRunning: false, SocketConnected: false, "League-Installation nicht gefunden");

    public static ClientStatus Offline { get; } =
        new(LeagueFound: true, ClientRunning: false, SocketConnected: false, "Client nicht gestartet");
}

/// <summary>
/// A source of champion select sessions. Implemented once against the live client and once against
/// a recorded file, so everything downstream can be developed and tested without League running.
/// </summary>
public interface ISessionSource : IAsyncDisposable
{
    /// <summary>
    /// Raw <c>/lol-champ-select/v1/session</c> JSON, or <see langword="null"/> when champion
    /// select ended. Raised on a background thread.
    /// </summary>
    event Action<string?>? SessionJson;

    event Action<ClientStatus>? StatusChanged;

    /// <summary>Runs until <paramref name="ct"/> fires.</summary>
    Task RunAsync(CancellationToken ct);
}
