using System.Diagnostics;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta;
using DraftPilot.Meta.OpGg;

namespace DraftPilot.Tools;

/// <summary>
/// The command-line face of the update button. Same code path the app uses, so problems surface
/// here before they surface in a window.
/// </summary>
internal static class SnapshotCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var store = new SnapshotStore();
        Console.WriteLine($"Ziel: {store.Path}");

        var clock = Stopwatch.StartNew();
        var lastLine = string.Empty;

        var progress = new Progress<BuildProgress>(report =>
        {
            var line = report.ToString();
            if (line == lastLine)
                return;

            lastLine = line;
            Console.Write($"\r{line,-40}");
        });

        using var client = new OpGgMcpClient();
        using var builder = new SnapshotBuilder(client);

        MetaSnapshot snapshot;
        try
        {
            snapshot = await builder.BuildAsync(new SnapshotBuildOptions(), progress, ct);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nAbgebrochen. Der vorhandene Snapshot bleibt unverändert.");
            return 1;
        }
        catch (Exception ex) when (ex is OpGgApiException or OpGgParseException or HttpRequestException)
        {
            Console.WriteLine($"\nFehlgeschlagen: {ex.Message}");
            Console.WriteLine("Der vorhandene Snapshot bleibt unverändert.");
            return 1;
        }

        store.Save(snapshot);

        Console.WriteLine();
        Console.WriteLine($"Fertig in {clock.Elapsed.TotalSeconds:F0}s, {client.CallCount} Anfragen.");
        Summarise(snapshot, store);
        return 0;
    }

    /// <summary>
    /// Fetches only the champion portraits for the stored snapshot. Separate verb because it needs
    /// no OP.GG calls at all — handy after adding the icon stage to an existing snapshot.
    /// </summary>
    public static async Task<int> DownloadIconsAsync(CancellationToken ct)
    {
        var snapshot = new SnapshotStore().Load();
        if (snapshot is null)
        {
            Console.Error.WriteLine("Kein Snapshot vorhanden. Erst 'update' ausführen.");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var warnings = new List<string>();
        var lastLine = string.Empty;

        var progress = new Progress<BuildProgress>(report =>
        {
            var line = report.ToString();
            if (line == lastLine)
                return;

            lastLine = line;
            Console.Write($"\r{line,-30}");
        });

        var added = await new IconDownloader(http)
            .DownloadMissingAsync(snapshot.Champions, snapshot.Patch, warnings, progress, ct);

        Console.WriteLine();
        Console.WriteLine($"{added} Icons geladen, {snapshot.Champions.Count} Champions im Snapshot.");

        foreach (var warning in warnings)
            Console.WriteLine($"  - {warning}");

        return 0;
    }

    public static int Inspect()
    {
        var store = new SnapshotStore();
        var snapshot = store.Load();

        if (snapshot is null)
        {
            Console.WriteLine($"Kein lesbarer Snapshot unter {store.Path}. Erst 'update' ausführen.");
            return 1;
        }

        Summarise(snapshot, store);
        return 0;
    }

    private static void Summarise(MetaSnapshot snapshot, SnapshotStore store)
    {
        var lookup = new MetaLookup(snapshot);
        var info = new FileInfo(store.Path);
        var sizeKb = info.Exists ? info.Length / 1024.0 : 0;

        Console.WriteLine();
        Console.WriteLine($"Patch {snapshot.Patch}   gebaut {snapshot.BuiltAtUtc:yyyy-MM-dd HH:mm}Z   {sizeKb:F0} KB");
        Console.WriteLine($"Champions {snapshot.Champions.Count}   Lane-Einträge {snapshot.LaneStats.Count}   "
            + $"Matchups {snapshot.Matchups.Count}   Synergien {snapshot.Synergies.Count}");

        Console.WriteLine();
        Console.WriteLine("Pool je Lane:");
        foreach (var lane in Lanes.All)
            Console.WriteLine($"  {lane.Display(),-8} {lookup.Roster(lane).Count}");

        Console.WriteLine();
        Console.WriteLine("Stärkste je Lane (geglättete Winrate):");
        foreach (var lane in Lanes.All)
        {
            var best = lookup.Roster(lane)
                .Select(id => (Id: id, Stat: lookup.LaneStat(id, lane)))
                .Where(entry => entry.Stat is not null)
                .OrderByDescending(entry => entry.Stat!.Value.WinRate)
                .Take(5)
                .Select(entry => $"{lookup.ChampionName(entry.Id)} {entry.Stat!.Value.WinRate:P1}");

            Console.WriteLine($"  {lane.Display(),-8} {string.Join("  ", best)}");
        }

        var damageKnown = snapshot.Champions.Count(champion => champion.Damage != DamageType.Unknown);
        Console.WriteLine();
        Console.WriteLine($"Schadensart bekannt für {damageKnown} von {snapshot.Champions.Count} Champions.");

        if (snapshot.Warnings.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Hinweise:");
        foreach (var warning in snapshot.Warnings)
            Console.WriteLine($"  - {warning}");
    }
}
