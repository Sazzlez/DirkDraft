using System.Globalization;
using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu;
using DraftPilot.Tools;

// Deliberately no command-line library: a handful of verbs does not justify the dependency.
if (args.Length == 0 || args[0].ToLowerInvariant() is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

using var lifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;

    try
    {
        lifetime.Cancel();
    }
    catch (ObjectDisposedException)
    {
        // A second Ctrl+C after the command already returned; nothing left to cancel.
    }
};

// One top-level net for every verb: an unwritable record target, a torn recording, a Ctrl+C in
// the lockfile wait — none of them deserves a raw stack trace in the user's face.
try
{
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

        case "runes":
            return await RunesCommand.RunAsync(lifetime.Token);

        default:
            Console.Error.WriteLine($"Unbekannter Befehl: {args[0]}");
            PrintUsage();
            return 2;
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Abgebrochen.");
    return 130;
}
catch (Exception ex)
{
    // The message alone for users; the full trace on request — a bare NullReferenceException
    // without frames is useless to whoever has to fix it.
    Console.Error.WriteLine(
        Environment.GetEnvironmentVariable("DIRKDRAFT_DEBUG") is { Length: > 0 }
            ? $"Fehler: {ex}"
            : $"Fehler: {ex.Message} (DIRKDRAFT_DEBUG=1 für Details)");
    return 1;
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
          runes                  Runenseiten des Accounts anzeigen (nur lesend)
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
    {
        Console.WriteLine($"{recorder.FrameCount} Frames aufgezeichnet.");

        if (recorder.UnscrubbedFrames > 0)
            Console.WriteLine($"{recorder.UnscrubbedFrames} Frames waren nicht bereinigbar und wurden ausgelassen.");
    }

    return 0;
}

static async Task<int> RunReplayAsync(string path, string speedArgument, CancellationToken ct)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Datei nicht gefunden: {path}");
        return 2;
    }

    double speed;
    if (speedArgument.Equals("max", StringComparison.OrdinalIgnoreCase))
    {
        speed = double.PositiveInfinity;
    }
    // Invariant plus comma tolerance. Plain TryParse read "1.5" through the German locale, where
    // the dot is the GROUP separator — and played back at fifteenfold speed.
    else if (double.TryParse(
            speedArgument.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0)
    {
        speed = parsed;
    }
    else
    {
        Console.Error.WriteLine($"Ungültiges Tempo „{speedArgument}“ — Zahl größer 0 oder „max“.");
        return 2;
    }

    var source = new JsonlSessionSource(path, speed);

    if (source.SkippedLines > 0)
        Console.WriteLine($"{source.SkippedLines} unlesbare Zeilen übersprungen (Aufzeichnung abgebrochen?).");

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
