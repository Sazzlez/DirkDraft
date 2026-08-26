namespace DraftPilot.Core.Lcu;

/// <summary>
/// Watches the League client lockfile and reports when the client becomes available or goes away.
/// Uses a <see cref="FileSystemWatcher"/> rather than polling, so it costs nothing while idle.
/// </summary>
public sealed class LockfileWatcher : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileSystemWatcher? _watcher;
    private LcuCredentials? _current;
    private bool _disposed;

    /// <param name="explicitLockfilePath">
    /// Overrides discovery; use when the user has configured a non-standard install path.
    /// </param>
    public LockfileWatcher(string? explicitLockfilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitLockfilePath))
        {
            LockfilePath = explicitLockfilePath;
            return;
        }

        var install = LeagueInstall.Locate();
        LockfilePath = install is null ? null : LeagueInstall.LockfilePath(install);
    }

    /// <summary>Raised when the client is up and its credentials could be read.</summary>
    public event Action<LcuCredentials>? Connected;

    /// <summary>Raised when the client shuts down.</summary>
    public event Action? Disconnected;

    /// <summary>The resolved lockfile path, or <see langword="null"/> if League was not found.</summary>
    public string? LockfilePath { get; }

    /// <summary>Currently known credentials, or <see langword="null"/> while the client is down.</summary>
    public LcuCredentials? Current => _current;

    /// <summary>Begins watching and reports the current state immediately.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (LockfilePath is null)
            return;

        var directory = Path.GetDirectoryName(LockfilePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        _watcher = new FileSystemWatcher(directory, "lockfile")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        _watcher.Created += OnLockfileEvent;
        _watcher.Changed += OnLockfileEvent;
        _watcher.Deleted += OnLockfileEvent;
        _watcher.Renamed += OnLockfileEvent;

        await RefreshAsync(ct).ConfigureAwait(false);
    }

    private void OnLockfileEvent(object sender, FileSystemEventArgs e) => _ = RefreshAsync(CancellationToken.None);

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (_disposed)
            return;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var credentials = await ReadWithRetryAsync(ct).ConfigureAwait(false);

            if (credentials is null)
            {
                if (_current is null)
                    return;

                _current = null;
                Disconnected?.Invoke();
                return;
            }

            // Unchanged credentials mean nothing happened worth reporting. Raising Connected here
            // anyway would make every listener tear down and rebuild its connection, and anything
            // built on top — like a champion select panel — would blink out and back.
            if (credentials == _current)
                return;

            _current = credentials;
            Connected?.Invoke(credentials);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads the lockfile, tolerating it being briefly absent or half-written.
    /// <para>
    /// The client rewrites this file while it runs, and the watcher fires during the rewrite. A
    /// recording of a live draft showed the same session being re-read every few seconds because a
    /// momentarily missing file was taken as "the client shut down". So a disappeared file only
    /// counts once it has stayed gone for <see cref="GraceMilliseconds"/>.
    /// </para>
    /// </summary>
    private async Task<LcuCredentials?> ReadWithRetryAsync(CancellationToken ct)
    {
        const int GraceMilliseconds = 1_500;
        const int IntervalMilliseconds = 125;
        const int Attempts = GraceMilliseconds / IntervalMilliseconds;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var credentials = LcuCredentials.TryParse(TryReadShared(LockfilePath!));
            if (credentials is not null)
                return credentials;

            await Task.Delay(IntervalMilliseconds, ct).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// The client keeps the lockfile open, so it has to be read with full sharing. A missing file
    /// returns null like any other read failure, which lets the caller retry it the same way.
    /// </summary>
    private static string? TryReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher?.Dispose();
        _gate.Dispose();
    }
}
