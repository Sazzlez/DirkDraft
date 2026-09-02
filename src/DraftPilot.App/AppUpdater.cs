using Velopack;
using Velopack.Sources;

namespace DraftPilot.App;

/// <summary>
/// The tool's own update path, so a new version reaches everyone who installed it without anybody
/// mailing files around.
/// <para>
/// One rule governs it: nothing is downloaded or installed without a click. At start the tool asks
/// GitHub once whether a newer release exists and says so in the window; the user decides when the
/// update happens. Silent installs were ruled out on purpose — a tool that changes itself while
/// nobody asked is the kind of behaviour this project has avoided from the first line.
/// </para>
/// <para>
/// A build that was not installed through the installer (the development harness, a plain copy of
/// the build folder) is not "installed" in Velopack's sense and never checks: there is nothing it
/// could update in place, and the check would only cost a network call and a wrong message.
/// </para>
/// </summary>
public sealed class AppUpdater
{
    /// <summary>
    /// Where releases live. GitHub Releases is the free, ordinary choice for a public repository;
    /// unauthenticated reads allow sixty requests per hour per IP, and the tool makes one per start.
    /// </summary>
    public const string Feed = "https://github.com/OWNER/DirkDraft";

    private readonly UpdateManager _manager = new(new GithubSource(Feed, null, false));

    private UpdateInfo? _pending;

    /// <summary>True only when this copy was put here by the installer and can update in place.</summary>
    public bool CanUpdate => _manager.IsInstalled;

    /// <summary>The installed version as Velopack knows it; null outside an installation.</summary>
    public string? InstalledVersion => _manager.CurrentVersion?.ToString();

    /// <summary>
    /// Asks the feed once. Returns the newer version's number, or <see langword="null"/> when this
    /// copy is current or cannot be updated at all. Network and parse failures come back as null too:
    /// an update check that fails is not news the user needs at start-up.
    /// </summary>
    public async Task<string?> CheckAsync(CancellationToken ct)
    {
        if (!CanUpdate)
            return null;

        try
        {
            _pending = await _manager.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false);
            return _pending?.TargetFullRelease.Version.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Note("Update-Prüfung", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Downloads the release found by <see cref="CheckAsync"/> and restarts into it. Returns only
    /// when something went wrong — on success the process is gone. <paramref name="progress"/>
    /// receives 0–100 while the download runs.
    /// </summary>
    public async Task<string?> InstallAsync(Action<int> progress, CancellationToken ct)
    {
        if (_pending is not { } update)
            return "Keine Aktualisierung vorgemerkt.";

        try
        {
            await _manager.DownloadUpdatesAsync(update, progress, ct).ConfigureAwait(false);

            // Exits the process. Anything worth saving must already be on disk — the window
            // placement is written on every move, the snapshot never lives in memory only.
            _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Update", ex);
            return $"Aktualisierung fehlgeschlagen: {ex.Message}";
        }
    }
}
