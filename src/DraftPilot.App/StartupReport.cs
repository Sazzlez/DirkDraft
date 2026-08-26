using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DraftPilot.Core.Config;
using DraftPilot.Core.Data;
using DraftPilot.Core.Lcu;

namespace DraftPilot.App;

/// <summary>
/// Writes down what the app actually found on disk at start-up.
/// <para>
/// Exists because a report like "the champion icons are missing" has several possible causes that
/// look identical from the outside: no snapshot, a snapshot that failed to parse, an empty icon
/// folder, or a build reading a different path than expected. One line per fact settles it.
/// </para>
/// </summary>
public static class StartupReport
{
    public static string Path { get; } = System.IO.Path.Combine(AppPaths.DataDirectory, "startup.log");

    /// <summary>A one-line summary suitable for a tooltip.</summary>
    public static string Summary { get; private set; } = string.Empty;

    public static void Write()
    {
        try
        {
            var report = Build();
            Summary = report.Summary;

            Directory.CreateDirectory(AppPaths.DataDirectory);

            // Appended, not overwritten: overwriting means the next start erases the evidence from
            // the start being investigated. Trimmed so it cannot grow without bound.
            File.AppendAllText(Path, report.Detail + Environment.NewLine, Encoding.UTF8);
            TrimTo(Path, maxLines: 400);
        }
        catch (Exception ex)
        {
            // Diagnostics must never be the reason the app fails to start — and a swallowed failure
            // must not be invisible either: one real start left no entry here, and the gap cost the
            // investigation more than the failure itself. Whatever goes wrong lands in the crash log.
            CrashLog.Write("StartupReport", ex);
        }
    }

    private static (string Summary, string Detail) Build()
    {
        var store = new SnapshotStore();
        var load = store.LoadWithStatus();
        var snapshot = load.Snapshot;
        var traits = TraitTable.Load();

        var iconCount = CountIcons();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unbekannt";

        var text = new StringBuilder()
            .AppendLine($"DraftPilot {version} — {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}")
            .AppendLine($"Programmordner   {AppContext.BaseDirectory}")
            .AppendLine($"AppData-Sicht    {DescribeAppDataView()}")
            .AppendLine()
            .AppendLine("--- League ---")
            .AppendLine($"Installation     {LeagueInstall.Locate() ?? "nicht gefunden"}")
            .AppendLine($"Zertifikat-Pin   {(RiotCertificate.IsPinAvailable ? "eingebettet" : "FEHLT")}")
            .AppendLine()
            .AppendLine("--- Mitgelieferte Daten ---")
            .AppendLine($"Ordner           {AppPaths.BundledDataDirectory}")
            .AppendLine($"champion_traits  {traits.Count} Champions")
            .AppendLine($"pick_order       {(File.Exists(AppPaths.Bundled("pick_order_priors.json")) ? "vorhanden" : "FEHLT")}")
            .AppendLine()
            .AppendLine("--- Erzeugte Daten ---")
            .AppendLine($"snapshot.json    {store.Path}")
            .AppendLine($"                 {(store.Exists ? $"{new FileInfo(store.Path).Length / 1024} KB" : "FEHLT")}")
            .AppendLine($"geladen          {(load.IsOk ? "ja" : $"NEIN — {load.Detail}")}");

        if (snapshot is not null)
        {
            text.AppendLine($"Patch            {snapshot.Patch}")
                .AppendLine($"Champions        {snapshot.Champions.Count}")
                .AppendLine($"Lane-Einträge    {snapshot.LaneStats.Count}")
                .AppendLine($"Matchups         {snapshot.Matchups.Count}")
                .AppendLine($"Synergien        {snapshot.Synergies.Count}");
        }

        text.AppendLine($"Icon-Ordner      {AppPaths.IconDirectory}")
            .AppendLine($"Icons            {iconCount} PNG-Dateien")
            .AppendLine()
            .AppendLine("--- Einstellungen ---")
            .AppendLine($"Datei            {AppPaths.SettingsPath}");

        var summary = snapshot is null
            ? $"Kein lesbarer Snapshot · {iconCount} Icons · Traits {traits.Count}"
            : $"{snapshot.Champions.Count} Champions · {iconCount} Icons · Traits {traits.Count} · Details: {Path}";

        return (summary, text.ToString());
    }

    /// <summary>Keeps the log from growing without bound while preserving the most recent runs.</summary>
    private static void TrimTo(string path, int maxLines)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length <= maxLines)
                return;

            File.WriteAllLines(path, lines[^maxLines..]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing worth failing a start-up over.
        }
    }

    /// <summary>
    /// Says whether this process sees the real per-user AppData or a package's virtualised copy.
    /// <para>
    /// Exists because of a lost afternoon: processes launched from inside a packaged app (MSIX)
    /// get their AppData writes redirected into that package's LocalCache, invisibly. Two builds
    /// looked at "the same" snapshot path and saw different files. This line makes every log entry
    /// say which world it was written in.
    /// </para>
    /// </summary>
    private static string DescribeAppDataView()
    {
        try
        {
            var length = 0;
            // Returns APPMODEL_ERROR_NO_PACKAGE (15700) outside a package, ERROR_INSUFFICIENT_BUFFER (122) inside.
            if (GetCurrentPackageFullName(ref length, null) == 15700)
                return "nativ";

            var buffer = new StringBuilder(length);
            GetCurrentPackageFullName(ref length, buffer);
            return $"PAKET-CONTAINER ({buffer}) — Schreibzugriffe landen im LocalCache dieses Pakets!";
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Pre-Windows-8; there are no packages to be inside of.
            return "nativ";
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);

    private static int CountIcons()
    {
        try
        {
            return Directory.Exists(AppPaths.IconDirectory)
                ? Directory.EnumerateFiles(AppPaths.IconDirectory, "*.png").Count()
                : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }
}
