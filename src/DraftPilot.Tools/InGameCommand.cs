using System.Text.Json;
using System.Text.Json.Nodes;
using DraftPilot.Core.Lcu;

namespace DraftPilot.Tools;

/// <summary>
/// Listens to the running game on its own port and records what it exposes, to settle one question
/// the client API cannot: does the game report the augments a Mayhem match offers?
/// <para>
/// The client's own schema was asked first and answered no — <c>/help?format=Full</c> carries not a
/// single augment endpoint, only TFT playbooks, skin cosmetics, and a post-game
/// <c>playerAugment1..6</c> that sits beside the runes in match history. The game itself serves a
/// second, entirely separate API on port 2999 that the app has never spoken to, and that is the only
/// place left where a live augment could appear.
/// </para>
/// <para>
/// Rather than guess at endpoint names, this records the SHAPE of the payload and reports every
/// property path the first time it is seen. An augment field cannot appear without being noticed,
/// whatever it ends up being called — and the moment it appears is the moment of the pick, which is
/// exactly what has to be observed.
/// </para>
/// </summary>
internal static class InGameCommand
{
    private const string GamePort = "https://127.0.0.1:2999";

    /// <summary>The cheapest endpoint that only answers once a match is actually running.</summary>
    private const string Heartbeat = "/liveclientdata/gamestats";

    /// <summary>
    /// Where to listen. Overridable so the whole path — waiting, recording, shape diff, event
    /// detection — can be exercised against a stand-in server beforehand. That matters more here
    /// than usual: the real measurement happens once, inside a match that cannot be replayed, and
    /// a probe first run in anger is a probe whose bugs cost a whole game.
    /// </summary>
    private static readonly Uri Endpoint = ResolveEndpoint();

    private static Uri ResolveEndpoint()
    {
        var configured = Environment.GetEnvironmentVariable("DIRKDRAFT_INGAME_ORIGIN");

        if (configured is { Length: > 0 } && Uri.TryCreate(configured, UriKind.Absolute, out var custom))
            return custom;

        return new Uri(GamePort);
    }

    /// <summary>Fast enough that a pick lasting a few seconds cannot fall between two polls.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How often a full payload is written even when nothing new appeared. Every poll would be tens
    /// of megabytes over a match; never would lose the context around an interesting frame.
    /// </summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the full endpoint list is re-checked. Long enough not to hammer the game, short
    /// enough to fall inside an augment offer rather than between two of them.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Consecutive failed polls before the game counts as over. A single dropped request during a
    /// loading screen or a fullscreen switch is normal and must not end the recording.
    /// </summary>
    private const int FailuresUntilOver = 8;

    /// <summary>
    /// The documented endpoints, plus the names an augment endpoint would plausibly carry. The
    /// guesses cost one request each and are here because a schema can be incomplete — the client's
    /// own was, for the champion select bench.
    /// </summary>
    private static readonly string[] Endpoints =
    [
        "/swagger/v3/openapi.json",
        "/swagger/v2/swagger.json",
        "/liveclientdata/allgamedata",
        "/liveclientdata/activeplayer",
        "/liveclientdata/playerlist",
        "/liveclientdata/eventdata",
        "/liveclientdata/gamestats",
        "/liveclientdata/activeplayeraugments",
        "/liveclientdata/playeraugments",
        "/liveclientdata/augments",
        "/liveclientdata/activeplayerrunes",
    ];

    public static async Task<int> RunAsync(string? recordTo, CancellationToken ct)
    {
        recordTo ??= "ingame.jsonl";

        var scrubber = new LiveGameScrubber();

        Console.WriteLine("=== In-Game-Sonde ===");
        Console.WriteLine($"Aufzeichnung: {Path.GetFullPath(recordTo)}");
        Console.WriteLine("Spielernamen werden vor dem Schreiben durch Platzhalter ersetzt.");
        Console.WriteLine();
        Console.WriteLine("Warte auf ein laufendes Spiel (Port 2999). Starte jetzt ein ARAM-Mayhem-");
        Console.WriteLine("Spiel; die Sonde meldet sich von selbst. Strg+C beendet sie.");
        Console.WriteLine();

        using var client = await ConnectAsync(ct);

        if (client is null)
            return 1;

        await using var writer = new StreamWriter(recordTo, append: false)
        {
            // Flushed per line: the usual way this process ends is the user closing the window
            // after the match, and a buffered tail would take the interesting part with it.
            AutoFlush = true,
        };

        Say("Kontakt. Frage die Endpunkte ab.");

        // Seeded with the guesses, then grown by whatever the game's own schema turns out to list.
        var endpoints = new List<string>(Endpoints);
        var answering = await SweepAsync(client, scrubber, writer, endpoints, previous: null, ct);
        Console.WriteLine();

        if (answering.Count == 0)
        {
            Say("Kein Endpunkt hat brauchbar geantwortet. Abbruch.");
            return 1;
        }

        return await PollAsync(client, scrubber, writer, endpoints, answering, ct);
    }

    /// <summary>
    /// Waits until the game answers, and returns the client that got through. Quiet by design apart
    /// from a heartbeat: this runs for as long as it takes to queue, load and start a match.
    /// <para>
    /// The port is checked separately from the request, because otherwise the two failures that
    /// matter here look identical. "No game yet" and "the game is listening but refused our TLS
    /// check" both surface as a null response, and the second one would leave the probe waiting in
    /// silence through the entire match it was set up to observe — an expensive way to learn
    /// nothing, since a Mayhem game cannot be repeated on demand.
    /// </para>
    /// </summary>
    private static async Task<HttpClient?> ConnectAsync(CancellationToken ct)
    {
        var pinned = CreateClient(pinnedCertificate: true);
        HttpClient? unpinned = null;
        var waited = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (await GetAsync(pinned, Heartbeat, ct) is not null)
                {
                    var accepted = pinned;
                    pinned = null!;
                    return accepted;
                }

                if (await PortOpenAsync(ct))
                {
                    unpinned ??= CreateClient(pinnedCertificate: false);

                    if (await GetAsync(unpinned, Heartbeat, ct) is not null)
                    {
                        Say("Hinweis: Das Zertifikat auf Port 2999 kettet sich nicht gegen die Riot-CA.");
                        Say("Die Sonde liest trotzdem weiter - 127.0.0.1, nur lesend, nur diese Messung.");

                        var accepted = unpinned;
                        unpinned = null;
                        return accepted;
                    }
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                waited += 2;

                if (waited % 60 == 0)
                    Say($"... warte weiter ({waited / 60} min).");
            }

            return null;
        }
        finally
        {
            pinned?.Dispose();
            unpinned?.Dispose();
        }
    }

    /// <summary>Whether anything is listening on the game's port at all, TLS aside.</summary>
    private static async Task<bool> PortOpenAsync(CancellationToken ct)
    {
        try
        {
            using var socket = new System.Net.Sockets.TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(500));

            await socket.ConnectAsync(Endpoint.Host, Endpoint.Port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asks every endpoint once and writes what came back, reporting which of them exist at all.
    /// This is the decisive part: an augment endpoint either is in the list or it is not.
    /// <para>
    /// Repeated during the match rather than done once, for two reasons. At first contact the game
    /// is still on the loading screen and may not have everything up yet; and an endpoint that only
    /// exists while an augment is being offered would never be seen by a single sweep at minute
    /// zero. With <paramref name="previous"/> set the sweep is quiet and reports only newcomers.
    /// </para>
    /// </summary>
    /// <param name="endpoints">
    /// Grown in place: whatever the game's own schema lists is appended, so later sweeps cover the
    /// documented surface rather than only the names guessed here.
    /// </param>
    private static async Task<HashSet<string>> SweepAsync(
        HttpClient client,
        LiveGameScrubber scrubber,
        StreamWriter writer,
        List<string> endpoints,
        HashSet<string>? previous,
        CancellationToken ct)
    {
        var loud = previous is null;
        var answering = previous is null ? [] : new HashSet<string>(previous, StringComparer.Ordinal);
        var discovered = new List<string>();

        // Indexed rather than foreach: the list grows while it is being walked, and the schema's
        // own endpoints are appended so this same pass reaches them.
        for (var index = 0; index < endpoints.Count; index++)
        {
            var endpoint = endpoints[index];
            var body = await GetAsync(client, endpoint, ct);

            if (body is null)
            {
                if (loud)
                    Console.WriteLine($"  {endpoint,-44} -");

                continue;
            }

            var isNew = answering.Add(endpoint);

            if (loud)
                Console.WriteLine($"  {endpoint,-44} {body.Length,9:N0} Zeichen");
            else if (isNew)
                Console.WriteLine($"  *** NEUER ENDPUNKT antwortet jetzt: {endpoint}");

            if (loud || isNew)
                WriteFrame(writer, "endpoint", endpoint, body, scrubber);

            if (endpoint.Contains("swagger", StringComparison.OrdinalIgnoreCase))
                discovered.AddRange(SwaggerPaths(body));
        }

        foreach (var path in discovered)
        {
            if (!endpoints.Contains(path))
            {
                endpoints.Add(path);

                if (loud)
                    Console.WriteLine($"  (aus dem Schema ergaenzt: {path})");
            }
        }

        return answering;
    }

    /// <summary>
    /// The endpoints the game documents about itself. Better than a guessed list, and the reason
    /// the sweep asks for the schema first.
    /// </summary>
    private static List<string> SwaggerPaths(string body)
    {
        var found = new List<string>();
        JsonNode? root;

        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return found;
        }

        if (root?["paths"] is not JsonObject paths)
            return found;

        foreach (var (path, _) in paths)
        {
            // Only the ones that need no argument; a templated path would just 404 on every sweep.
            if (path.StartsWith('/') && !path.Contains('{'))
                found.Add(path);
        }

        return found;
    }

    /// <summary>
    /// Watches the match and reports every property path and every event name the first time it
    /// shows up. A payload is written when something new appeared, and otherwise once per heartbeat
    /// so the recording keeps its context.
    /// </summary>
    private static async Task<int> PollAsync(
        HttpClient client,
        LiveGameScrubber scrubber,
        StreamWriter writer,
        List<string> endpoints,
        HashSet<string> answering,
        CancellationToken ct)
    {
        var lastSweep = TimeSpan.Zero;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var events = new HashSet<string>(StringComparer.Ordinal);
        var augmentHits = new List<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var lastHeartbeat = TimeSpan.FromSeconds(-60);
        var failures = 0;
        var polls = 0;
        var written = 0;
        var mode = "";

        Say("Beobachte das Spiel. Neue Felder und Events werden hier gemeldet.");
        Console.WriteLine();

        while (!ct.IsCancellationRequested)
        {
            var body = await GetAsync(client, "/liveclientdata/allgamedata", ct);

            if (body is null)
            {
                if (++failures >= FailuresUntilOver)
                {
                    Say("Keine Antwort mehr - das Spiel ist vorbei.");
                    break;
                }
            }
            else
            {
                failures = 0;
                polls++;

                JsonNode? root = null;
                try
                {
                    root = JsonNode.Parse(body);
                }
                catch (JsonException)
                {
                    // A torn read mid-write; the next poll two seconds later gets a whole one.
                }

                if (root is not null)
                {
                    scrubber.Scrub(root);

                    if (mode.Length == 0 && Text(root["gameData"]?["gameMode"]) is { Length: > 0 } found)
                    {
                        mode = found;
                        Say($"gameMode laut Spiel: \"{mode}\"");
                    }

                    var fresh = new List<string>();
                    Collect(root, "", paths, fresh, 0);

                    var freshEvents = NewEventNames(root, events);

                    foreach (var path in fresh)
                        Report(path, $"AUGMENT-FELD: {path}", $"+ {path}", augmentHits);

                    foreach (var name in freshEvents)
                        Report(name, $"AUGMENT-EVENT: {name}", $"+ Event {name}", augmentHits);

                    var interesting = fresh.Count > 0 || freshEvents.Count > 0;

                    if (interesting || clock.Elapsed - lastHeartbeat >= HeartbeatInterval)
                    {
                        lastHeartbeat = clock.Elapsed;
                        written++;

                        // Already scrubbed in place above, so no second pass here.
                        WriteFrame(
                            writer,
                            interesting ? "change" : "heartbeat",
                            "/liveclientdata/allgamedata",
                            root.ToJsonString(),
                            scrubber: null);
                    }
                }
            }

            // An endpoint that only exists during an augment offer would be missed by the single
            // sweep at minute zero, so the surface is re-checked while the match runs.
            if (clock.Elapsed - lastSweep >= SweepInterval)
            {
                lastSweep = clock.Elapsed;
                answering = await SweepAsync(client, scrubber, writer, endpoints, answering, ct);
            }

            try
            {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Console.WriteLine();
        Say($"{answering.Count} von {endpoints.Count} Endpunkten haben je geantwortet.");
        Say($"{polls} Abfragen, {written} Frames geschrieben, {paths.Count} verschiedene Felder gesehen.");
        Say($"{events.Count} verschiedene Events: {(events.Count == 0 ? "-" : string.Join(", ", events.Order()))}");

        if (augmentHits.Count == 0)
            Say("KEIN Feld und KEIN Event mit \"augment\" im Namen. Das Spiel meldet keine Augments.");
        else
            Say($"AUGMENTS GEFUNDEN: {string.Join(", ", augmentHits)}");

        return 0;
    }

    /// <summary>Prints one newly seen name, loudly when it is the one being looked for.</summary>
    private static void Report(string name, string hitText, string plainText, ICollection<string> hits)
    {
        if (name.Contains("augment", StringComparison.OrdinalIgnoreCase))
        {
            hits.Add(name);
            Console.WriteLine($"  *** {hitText}");
            return;
        }

        Console.WriteLine($"  {plainText}");
    }

    /// <summary>
    /// Walks the payload and adds every property path, noting which ones are new. Array indices are
    /// dropped — ten players are one shape, not ten.
    /// </summary>
    private static void Collect(JsonNode? node, string path, ISet<string> known, ICollection<string> fresh, int depth)
    {
        if (depth > 32)
            return;

        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    var child = path.Length == 0 ? key : $"{path}.{key}";

                    if (known.Add(child))
                        fresh.Add(child);

                    Collect(value, child, known, fresh, depth + 1);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                    Collect(item, path + "[]", known, fresh, depth + 1);

                break;
        }
    }

    private static List<string> NewEventNames(JsonNode root, ISet<string> known)
    {
        var fresh = new List<string>();

        if (root["events"]?["Events"] is not JsonArray list)
            return fresh;

        foreach (var entry in list)
        {
            if (Text(entry?["EventName"]) is { Length: > 0 } name && known.Add(name))
                fresh.Add(name);
        }

        return fresh;
    }

    /// <summary>
    /// A string value, or null for anything else. <c>GetValue&lt;string&gt;</c> throws on a number
    /// or a null, and a probe that dies on an unexpected type defeats its own purpose.
    /// </summary>
    private static string? Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// Writes one recording line. A body that cannot be scrubbed is not written at all — an
    /// unparsable payload may hold anything, and this file is meant to be shareable.
    /// </summary>
    private static void WriteFrame(StreamWriter writer, string kind, string path, string body, LiveGameScrubber? scrubber)
    {
        if (scrubber is not null)
        {
            if (!scrubber.TryScrub(body, out var clean))
            {
                Console.WriteLine($"  (nicht als JSON lesbar, nicht geschrieben: {path})");
                return;
            }

            body = clean;
        }

        var envelope = new JsonObject
        {
            ["kind"] = kind,
            ["path"] = path,
            ["at"] = DateTime.Now.ToString("HH:mm:ss"),
        };

        // The body is spliced in as raw text rather than re-parsed into the envelope: the swagger
        // document is the point of the exercise and has to survive verbatim, whatever shape it has.
        writer.WriteLine($"{envelope.ToJsonString()[..^1]},\"body\":{body}}}");
    }

    private static async Task<string?> GetAsync(HttpClient client, string path, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(path, ct);

            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static HttpClient CreateClient(bool pinnedCertificate)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = pinnedCertificate
                ? RiotCertificate.ValidateHttp
                : static (_, _, _, _) => true,
        };

        return new HttpClient(handler)
        {
            BaseAddress = Endpoint,
            Timeout = TimeSpan.FromSeconds(4),
        };
    }

    private static void Say(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}
