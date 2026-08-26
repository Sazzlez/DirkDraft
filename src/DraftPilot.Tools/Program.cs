using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu;
using DraftPilot.Tools;

// Deliberately no command-line library: three verbs do not justify the dependency.
if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

using var lifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    lifetime.Cancel();
};

switch (args[0].ToLowerInvariant())
{
    case "watch":
        return await RunWatchAsync(recordTo: null, lifetime.Token);

    case "record":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("record braucht einen Zieldateinamen.");
            return 2;
        }

        return await RunWatchAsync(recordTo: args[1], lifetime.Token);

    case "replay":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("replay braucht eine Aufzeichnungsdatei.");
            return 2;
        }

        return await RunReplayAsync(args[1], args.Length > 2 ? args[2] : "1", lifetime.Token);

    case "update":
        return await SnapshotCommand.RunAsync(lifetime.Token);

    case "scrub":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("scrub braucht eine Aufzeichnungsdatei.");
            return 2;
        }

        return ScrubCommand.Run(args[1], args.Length > 2 ? args[2] : null);

    case "events":
        return await EventsCommand.RunAsync(lifetime.Token);

    case "probe":
        return await ProbeCommand.RunAsync(lifetime.Token);

    case "icons":
        return await SnapshotCommand.DownloadIconsAsync(lifetime.Token);

    case "inspect":
        return SnapshotCommand.Inspect();

    case "recommend":
        return RecommendCommand.Run(args);

    default:
        Console.Error.WriteLine($"Unbekannter Befehl: {args[0]}");
        PrintUsage();
        return 2;
}

static void PrintUsage()
{
    Console.WriteLine("""
        DraftPilot.Tools

          watch                  Mit dem laufenden Client verbinden und Champ Select mitlesen
          record <datei.jsonl>   Wie watch, schreibt zusaetzlich eine Aufzeichnung
          replay <datei.jsonl> [tempo]
                                 Aufzeichnung abspielen. tempo: 1 = Originalgeschwindigkeit,
                                 4 = vierfach, max = ohne Wartezeiten
          update                 Meta-Daten von OP.GG holen und lokal speichern
          probe                  Verbindung zum Client schichtweise pruefen
          events                 Alle Client-Events roh mitschreiben
          scrub <datei.jsonl> [ziel]
                                 Persoenliche Daten aus einer Aufzeichnung entfernen
          icons                  Fehlende Champion-Icons nachladen
          inspect                Den gespeicherten Snapshot zusammenfassen
          recommend <lane> [gegner] [team]
                                 Empfehlungen fuer einen erfundenen Draft rechnen,
                                 z. B. recommend mid Jax,Elise,Syndra Aatrox,LeeSin
        """);
}

static async Task<int> RunWatchAsync(string? recordTo, CancellationToken ct)
{
    ISessionSource source = new LiveSessionSource(
        explicitLockfilePath: null,
        diagnostic: note => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {note}"));

    RecordingSessionSource? recorder = null;
    if (recordTo is not null)
    {
        recorder = new RecordingSessionSource(source, recordTo);
        source = recorder;
        Console.WriteLine($"Aufzeichnung: {Path.GetFullPath(recordTo)}");
    }

    await using var tracker = new DraftTracker(source);
    tracker.Changed += DraftStatePrinter.Print;

    Console.WriteLine("Warte auf den League-Client. Strg+C beendet.");

    try
    {
        await tracker.RunAsync(ct);
    }
    catch (OperationCanceledException)
    {
        // Expected on Ctrl+C.
    }

    if (recorder is not null)
        Console.WriteLine($"{recorder.FrameCount} Frames aufgezeichnet.");

    return 0;
}

static async Task<int> RunReplayAsync(string path, string speedArgument, CancellationToken ct)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Datei nicht gefunden: {path}");
        return 2;
    }

    var speed = speedArgument.Equals("max", StringComparison.OrdinalIgnoreCase)
        ? double.PositiveInfinity
        : double.TryParse(speedArgument, out var parsed) && parsed > 0
            ? parsed
            : 1.0;

    var source = new JsonlSessionSource(path, speed);
    await using var tracker = new DraftTracker(source, debounceMs: 0);
    tracker.Changed += DraftStatePrinter.Print;

    Console.WriteLine($"Wiedergabe: {Path.GetFullPath(path)} ({source.FrameCount} Frames, Tempo {speedArgument})");

    try
    {
        await tracker.RunAsync(ct);
    }
    catch (OperationCanceledException)
    {
        // Expected on Ctrl+C.
    }

    return 0;
}
