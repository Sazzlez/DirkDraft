using System.Globalization;
using System.Text.Json;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta.Riot;

namespace DraftPilot.Tools;

/// <summary>
/// The build collector on the command line: <c>riot collect</c> reads ranked games from the Riot
/// API and counts their builds into a state file, <c>riot show</c> prints what has been counted for
/// one champion on one lane. The key comes from <c>RIOT_API_KEY</c> or from <c>--key-file</c> and
/// is never printed.
/// </summary>
internal static class RiotCommand
{
    private const string DataDragon = "https://ddragon.leagueoflegends.com";

    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var verb = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
        var (options, positional) = Options(args.Skip(2));

        return verb switch
        {
            "collect" => await CollectAsync(options, ct),
            "show" => await ShowAsync(positional, options, ct),
            "status" => Status(options),
            "export" => await ExportAsync(options, ct),
            "aram" => await AramAsync(options, ct),
            "queues" => await QueuesAsync(options, ct),
            "export-aram" => ExportAram(options),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            riot collect [--minutes 30] [--tiers platinum] [--state build/riot-state.json.gz] [--key-file datei]
            riot show <champion> <lane> [--tier platinum] [--state build/riot-state.json.gz]
            riot status [--state build/riot-state.json.gz]
            riot export --out builds.json [--meta-out meta.json] [--situational-out situational.json] [--tier platinum] [--keep 6] [--min-games 3]
                        [--min-patch-games 2000] [--state build/riot-state.json.gz]
            riot aram [--queues 450] [--tiers platinum] [--minutes 30] [--state build/aram-state.json.gz] [--key-file datei]
            riot queues --riot-id "Name#TAG" [--games 20] [--key-file datei]
            riot export-aram --out aram.json [--keep 6] [--min-games 3] [--min-patch-games 500] [--state build/aram-state.json.gz]
            (Riot-ID für queues auch über die Umgebungsvariable RIOT_ID)
            """);
        return 2;
    }

    /// <summary>What the state holds: games per bracket, and how far the most played champions have got.</summary>
    private static int Status(Dictionary<string, string> options)
    {
        var statePath = options.GetValueOrDefault("state", Path.Combine("build", "riot-state.json.gz"));
        var state = CollectorState.Load(statePath);

        Console.WriteLine($"Patch {state.Patch} · {state.Platform} · Stand {state.Tally.CollectedAtUtc.ToLocalTime():dd.MM. HH:mm}");
        Console.WriteLine($"Spiele: {string.Join(", ", state.Tally.Matches.Select(m => $"{m.Key} {m.Value:N0}"))} · angesehen {state.Seen.Count:N0}");
        Console.WriteLine(
            $"Draft-Zählung: {state.Meta.Matches:N0} Spiele, {state.Meta.Duels.Count:N0} Duelle, {state.Meta.Duos.Count:N0} Duos"
            + (state.Previous is { } previous ? $" · Vorpatch {previous.Patch}: {previous.Meta.Matches:N0} Spiele" : string.Empty));

        foreach (var lane in Lanes.All)
        {
            var top = state.Tally.Builds
                .Where(b => b.Lane == lane)
                .GroupBy(b => b.ChampionId)
                .Select(g => (Champion: g.Key, Games: g.Sum(b => b.Games)))
                .OrderByDescending(x => x.Games)
                .ToList();

            var withEnough = top.Count(x => x.Games >= 100);
            Console.WriteLine($"  {lane.Display(),-8} {top.Count,3} Champions, {withEnough,3} mit ≥ 100 Spielen, meistgespielt {string.Join(", ", top.Take(3).Select(x => $"#{x.Champion} {x.Games}"))}");
        }

        return 0;
    }

    /// <summary>
    /// The file every DirkDraft downloads: the same counts, cut to the options worth showing.
    /// Long tails of one-off starting buys and skill orders are what the state needs to keep
    /// counting correctly and what nobody needs to download.
    /// </summary>
    private static async Task<int> ExportAsync(Dictionary<string, string> options, CancellationToken ct)
    {
        var statePath = options.GetValueOrDefault("state", Path.Combine("build", "riot-state.json.gz"));
        if (!options.TryGetValue("out", out var outPath) || outPath.Length == 0)
            return Usage();

        var keep = int.Parse(options.GetValueOrDefault("keep", "6"), CultureInfo.InvariantCulture);
        var minimum = int.Parse(options.GetValueOrDefault("min-games", "3"), CultureInfo.InvariantCulture);
        var enough = int.Parse(options.GetValueOrDefault("min-patch-games", "2000"), CultureInfo.InvariantCulture);
        var tier = options.GetValueOrDefault("tier", "platinum");

        var state = CollectorState.Load(statePath);

        // Early in a patch the new count is thin; until it has enough games the previous patch's
        // numbers are published instead, under their own patch, so the app knows they trail.
        var (patch, buildFile, metaCounts, situationalCounts) = (state.Patch, state.Tally, state.Meta, state.Situational);
        if (state.Meta.Matches < enough && state.Previous is { } previous && previous.Meta.Matches > state.Meta.Matches)
        {
            (patch, buildFile, metaCounts, situationalCounts) = (previous.Patch, previous.Tally, previous.Meta, previous.Situational);
            Console.WriteLine($"Patch {state.Patch} hat erst {state.Meta.Matches:N0} Spiele — veröffentlicht wird Patch {patch} ({metaCounts.Matches:N0}).");
        }

        var tally = new RiotBuildTally();
        tally.Add(buildFile.Builds);

        var file = new RiotBuildFile
        {
            Patch = patch,
            Platform = buildFile.Platform,
            CollectedAtUtc = buildFile.CollectedAtUtc,
            Matches = buildFile.Matches,
            Builds = tally.Export(keep, minimum),
        };

        Write(outPath, JsonSerializer.Serialize(file, RiotBuildJson.Default.RiotBuildFile));
        Console.WriteLine($"{file.Builds.Count} Champion-Lanes, {new FileInfo(outPath).Length / 1024:N0} KB → {outPath}");

        var metaPath = options.GetValueOrDefault("meta-out", string.Empty);
        var situationalPath = options.GetValueOrDefault("situational-out", string.Empty);
        if (metaPath.Length == 0 && situationalPath.Length == 0)
            return 0;

        using var http = new HttpClient();
        var version = await LatestVersionAsync(http, ct);
        var champions = RiotMetaExport.ParseDataDragon(
            await http.GetStringAsync($"{DataDragon}/cdn/{version}/data/en_US/champion.json", ct));

        if (situationalPath.Length > 0)
        {
            var situational = new RiotSituationalTally();
            situational.Add(situationalCounts);
            var classOf = champions.Where(c => c.Tags.Count > 0).ToDictionary(c => c.Id, c => c.Tags[0]);
            var situationalFile = situational.File(patch, buildFile.Platform, buildFile.CollectedAtUtc, classOf);
            Write(situationalPath, JsonSerializer.Serialize(situationalFile, RiotSituationalJson.Default.RiotSituationalFile));
            Console.WriteLine($"Situativ: {situationalFile.Rows.Count:N0} Champion-Zeilen, {situationalFile.Classes.Count:N0} Klassen-Zeilen, {new FileInfo(situationalPath).Length / 1024:N0} KB → {situationalPath}");
        }

        if (metaPath.Length > 0)
        {
            var meta = new RiotMetaTally();
            meta.Add(metaCounts);

            var snapshot = RiotMetaExport.Build(meta.Rows(), champions, patch, tier, buildFile.Platform, buildFile.CollectedAtUtc, DateTimeOffset.UtcNow);
            Write(metaPath, JsonSerializer.Serialize(snapshot, MetaJson.Default.MetaSnapshot));
            Console.WriteLine(
                $"Meta: {snapshot.Games:N0} Spiele, {snapshot.LaneStats.Count} Lane-Zeilen, {snapshot.Matchups.Count:N0} Duelle, "
                + $"{snapshot.Synergies.Count:N0} Duos, Duell-Linie {snapshot.CompleteDuelSlope:0.00} / {snapshot.CompleteDuelOffset:+0.000;-0.000;0.000}"
                + $"aus {snapshot.CompleteDuelSample:N0}, {new FileInfo(metaPath).Length / 1024:N0} KB → {metaPath}");
        }

        return 0;

        static void Write(string path, string text)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, text);
        }
    }

    private static AramOptions AramOptionsFrom(Dictionary<string, string> options) => new()
    {
        SeedRiotId = options.GetValueOrDefault("riot-id") is { Length: > 0 } given ? given : Environment.GetEnvironmentVariable("RIOT_ID")?.Trim() ?? string.Empty,
        Tiers = [.. options.GetValueOrDefault("tiers", "platinum").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
        Queues = [.. options.GetValueOrDefault("queues", "450")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(queue => int.Parse(queue, CultureInfo.InvariantCulture))],
        Duration = TimeSpan.FromMinutes(double.Parse(options.GetValueOrDefault("minutes", "30"), CultureInfo.InvariantCulture)),
    };

    /// <summary>
    /// Plain ARAM, counted as ARAM Mayhem's stand-in: Riot's API hides Mayhem games (403), so they
    /// cannot be counted (measured 07.11.2026).
    /// </summary>
    private static async Task<int> AramAsync(Dictionary<string, string> options, CancellationToken ct)
    {
        var key = ReadKey(options);
        if (key is null)
        {
            Console.Error.WriteLine("Kein Schlüssel: RIOT_API_KEY setzen oder --key-file angeben.");
            return 2;
        }

        var aram = AramOptionsFrom(options);
        var statePath = options.GetValueOrDefault("state", Path.Combine("build", "aram-state.json.gz"));
        using var http = new HttpClient();
        var catalog = await LatestItemsAsync(http, ct);
        Console.WriteLine($"ARAM · Patch {catalog.Version} · Queues {string.Join(", ", aram.Queues)} · Spieler aus {string.Join(", ", aram.Tiers)} · Laufzeit {aram.Duration.TotalMinutes:0.#} min.");

        var state = AramState.Load(statePath);
        using var api = new RiotApiClient(key);
        var collector = new AramCollector(api, catalog, aram, line => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"));
        var report = await collector.RunAsync(state, s => s.Save(statePath), ct);
        Console.WriteLine(
            $"Fertig: {report.Counted} ARAM-Spiele gezählt, {report.Skipped} übersprungen, {report.Requests} Abrufe "
            + $"({report.Throttled}× gedrosselt) in {report.Elapsed:h\\:mm\\:ss}. Bestand: {state.Tally.Games:N0} Spiele → {statePath}");
        return 0;
    }

    private static async Task<int> QueuesAsync(Dictionary<string, string> options, CancellationToken ct)
    {
        var key = ReadKey(options);
        var aram = AramOptionsFrom(options);
        if (key is null || !aram.SeedRiotId.Contains('#'))
            return Usage();

        using var http = new HttpClient();
        var catalog = await LatestItemsAsync(http, ct);
        using var api = new RiotApiClient(key);
        var collector = new AramCollector(api, catalog, aram);
        var games = int.Parse(options.GetValueOrDefault("games", "20"), CultureInfo.InvariantCulture);
        foreach (var (queue, count) in (await collector.RecentQueuesAsync(games, ct)).OrderByDescending(q => q.Value))
            Console.WriteLine($"Queue {queue,5}: {count,3} Spiele  {QueueKinds.FromId(queue).Display()}");
        return 0;
    }

    private static int ExportAram(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("out", out var outPath) || outPath.Length == 0)
            return Usage();

        var statePath = options.GetValueOrDefault("state", Path.Combine("build", "aram-state.json.gz"));
        var keep = int.Parse(options.GetValueOrDefault("keep", "6"), CultureInfo.InvariantCulture);
        var minimum = int.Parse(options.GetValueOrDefault("min-games", "3"), CultureInfo.InvariantCulture);
        var enough = int.Parse(options.GetValueOrDefault("min-patch-games", "500"), CultureInfo.InvariantCulture);
        var state = AramState.Load(statePath);

        var source = state.Tally.Games < enough && state.Previous is { } previous && previous.Games > state.Tally.Games ? previous : state.Tally;
        if (source.Games == 0)
        {
            Console.WriteLine("Noch keine ARAM-Spiele gezählt — keine aram.json.");
            return 0;
        }

        var tally = new RiotAramTally();
        tally.Add(source);
        var file = tally.Export(source.Patch, source.Platform, source.CollectedAtUtc, keep, minimum);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(outPath, JsonSerializer.Serialize(file, RiotAramJson.Default.RiotAramFile));
        Console.WriteLine($"ARAM: {file.Games:N0} Spiele, {file.Champions.Count} Champions, {file.Builds.Count} Builds, {new FileInfo(outPath).Length / 1024:N0} KB → {outPath}");
        return 0;
    }

    private static async Task<int> CollectAsync(Dictionary<string, string> options, CancellationToken ct)
    {
        var key = ReadKey(options);
        if (key is null)
        {
            Console.Error.WriteLine("Kein Schlüssel: RIOT_API_KEY setzen oder --key-file angeben.");
            return 2;
        }

        var statePath = options.GetValueOrDefault("state", Path.Combine("build", "riot-state.json.gz"));
        var minutes = double.Parse(options.GetValueOrDefault("minutes", "30"), CultureInfo.InvariantCulture);
        var tiers = options.GetValueOrDefault("tiers", "platinum")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tier => tier.ToLowerInvariant())
            .ToList();

        using var http = new HttpClient();
        var catalog = await LatestItemsAsync(http, ct);
        Console.WriteLine($"Patch {catalog.Version}, {catalog.Count} Items. Ränge: {string.Join(", ", tiers)}. Laufzeit {minutes:0.#} min.");

        // Class and CC data for the situational items; the traits file sits in the repository.
        var champions = RiotMetaExport.ParseDataDragon(
            await http.GetStringAsync($"{DataDragon}/cdn/{catalog.Version}/data/en_US/champion.json", ct));
        var traits = TraitTable.Load(options.GetValueOrDefault("traits", Path.Combine("data", "champion_traits.json")));
        Console.WriteLine($"{champions.Count} Champions, {traits.Count} mit Trait-Daten.");

        var state = CollectorState.Load(statePath);
        using var api = new RiotApiClient(key);
        var collector = new RiotCollector(
            api,
            catalog,
            new CollectorOptions { Tiers = tiers, Duration = TimeSpan.FromMinutes(minutes) },
            line => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"),
            new ChampionStatics(champions.ToDictionary(champion => champion.Id), traits));

        var report = await collector.RunAsync(state, s => s.Save(statePath), ct);

        Console.WriteLine(
            $"Fertig: {report.Counted} Spiele gezählt, {report.Skipped} übersprungen, {report.Requests} Abrufe "
            + $"({report.Throttled}× gedrosselt) in {report.Elapsed:h\\:mm\\:ss}. Bestand: "
            + string.Join(", ", state.Tally.Matches.Select(m => $"{m.Key} {m.Value}")) + $" → {statePath}");
        return 0;
    }

    private static async Task<int> ShowAsync(List<string> positional, Dictionary<string, string> options, CancellationToken ct)
    {
        if (positional.Count < 2)
            return Usage();

        var statePath = options.GetValueOrDefault("state", Path.Combine("build", "riot-state.json.gz"));
        var state = CollectorState.Load(statePath);
        if (state.Tally.Builds.Count == 0)
        {
            Console.Error.WriteLine($"Noch nichts gezählt in {statePath}.");
            return 1;
        }

        using var http = new HttpClient();
        var version = await LatestVersionAsync(http, ct);
        var names = await NamesAsync(http, version, ct);

        var championId = names.Champions.FirstOrDefault(pair => string.Equals(pair.Value, positional[0], StringComparison.OrdinalIgnoreCase)
            || string.Equals(pair.Value.Replace("'", string.Empty).Replace(" ", string.Empty), positional[0], StringComparison.OrdinalIgnoreCase)).Key;
        var lane = Lanes.FromOpGg(positional[1]);
        var tier = options.GetValueOrDefault("tier", string.Empty);

        var buckets = state.Tally.Builds
            .Where(b => b.ChampionId == championId && b.Lane == lane && (tier.Length == 0 || b.Tier == tier))
            .ToList();

        if (championId == 0 || lane == Lane.Unknown || buckets.Count == 0)
        {
            Console.Error.WriteLine($"Keine Spiele für {positional[0]} auf {positional[1]}{(tier.Length > 0 ? $" in {tier}" : string.Empty)}.");
            return 1;
        }

        var tally = new RiotBuildTally();
        tally.Add(buckets.Select(b => { b.Tier = tier.Length > 0 ? tier : "alle"; return b; }));
        var stats = tally.Export(keep: 5).Single();

        Console.WriteLine($"{names.Champions[championId]} · {lane.Display()} · {(tier.Length > 0 ? tier : "alle Ränge")} · Patch {state.Patch}: {stats.Games} Spiele, {Rate(stats.Wins, stats.Games)} Siege");
        Section("Runen", stats.Runes, ids => $"{names.Rune(ids[0])}: {string.Join(", ", ids.Skip(1).Take(4).Select(names.Rune))} + {names.Rune(ids[5])}: {string.Join(", ", ids.Skip(6).Take(2).Select(names.Rune))} | Shards {string.Join("/", ids.Skip(8))}");
        Section("Spells", stats.Spells, ids => string.Join(" + ", ids.Select(names.Spell)));
        Section("Start", stats.Starters, ids => string.Join(", ", ids.Select(names.Item)));
        Section("Schuhe", stats.Boots, ids => names.Item(ids[0]));
        Section("Kern", stats.Core, ids => string.Join(" → ", ids.Select(names.Item)));
        Section("Item 4", stats.Fourth, ids => names.Item(ids[0]));
        Section("Item 5", stats.Fifth, ids => names.Item(ids[0]));
        Section("Support-Item", stats.SupportChoice, ids => names.Item(ids[0]));
        Section("Skills", stats.SkillOrder, _ => string.Empty);
        Section("Max", stats.SkillMax, _ => string.Empty);
        return 0;

        static void Section(string title, List<BuildCount> options, Func<List<int>, string> describe)
        {
            if (options.Count == 0)
                return;

            Console.WriteLine($"  {title}");
            foreach (var option in options)
            {
                var text = option.Text is { Length: > 0 } letters ? letters : describe(option.Ids);
                Console.WriteLine($"    {option.Games,6:N0} Spiele  {Rate(option.Wins, option.Games),6}  {text}");
            }
        }
    }

    private static string Rate(int wins, int games) => games > 0 ? ((double)wins / games).ToString("P1", CultureInfo.GetCultureInfo("de-DE")) : "—";

    /// <summary>
    /// The key from the environment (how the scheduled job passes it) or from a file the user put
    /// there. Trimmed, BOM and all — Notepad likes to add one — and handed to nobody but the client.
    /// </summary>
    private static string? ReadKey(Dictionary<string, string> options)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("RIOT_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment.Trim();

        if (options.TryGetValue("key-file", out var path) && File.Exists(path))
        {
            var text = File.ReadAllText(path).Trim().TrimStart('﻿').Trim();
            return text.Length > 0 ? text : null;
        }

        return null;
    }

    /// <summary>"--name value" pairs, and everything else in order as positional arguments.</summary>
    private static (Dictionary<string, string> Options, List<string> Positional) Options(IEnumerable<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();
        string? pending = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                pending = arg[2..];
                options[pending] = string.Empty;
            }
            else if (pending is not null)
            {
                options[pending] = arg;
                pending = null;
            }
            else
            {
                positional.Add(arg);
            }
        }

        return (options, positional);
    }

    private static async Task<string> LatestVersionAsync(HttpClient http, CancellationToken ct)
    {
        using var versions = JsonDocument.Parse(await http.GetStringAsync($"{DataDragon}/api/versions.json", ct));
        return versions.RootElement[0].GetString() ?? throw new InvalidOperationException("Data Dragon nennt keine Version.");
    }

    private static async Task<ItemCatalog> LatestItemsAsync(HttpClient http, CancellationToken ct)
    {
        var version = await LatestVersionAsync(http, ct);
        return ItemCatalog.Parse(await http.GetStringAsync($"{DataDragon}/cdn/{version}/data/en_US/item.json", ct));
    }

    private static async Task<Names> NamesAsync(HttpClient http, string version, CancellationToken ct)
    {
        var names = new Names();

        using (var champions = JsonDocument.Parse(await http.GetStringAsync($"{DataDragon}/cdn/{version}/data/en_US/champion.json", ct)))
        {
            foreach (var champion in champions.RootElement.GetProperty("data").EnumerateObject())
            {
                if (int.TryParse(champion.Value.GetProperty("key").GetString(), out var id))
                    names.Champions[id] = champion.Value.GetProperty("name").GetString() ?? champion.Name;
            }
        }

        using (var items = JsonDocument.Parse(await http.GetStringAsync($"{DataDragon}/cdn/{version}/data/en_US/item.json", ct)))
        {
            foreach (var item in items.RootElement.GetProperty("data").EnumerateObject())
            {
                if (int.TryParse(item.Name, out var id))
                    names.Items[id] = item.Value.GetProperty("name").GetString() ?? item.Name;
            }
        }

        using (var spells = JsonDocument.Parse(await http.GetStringAsync($"{DataDragon}/cdn/{version}/data/en_US/summoner.json", ct)))
        {
            foreach (var spell in spells.RootElement.GetProperty("data").EnumerateObject())
            {
                if (int.TryParse(spell.Value.GetProperty("key").GetString(), out var id))
                    names.Spells[id] = spell.Value.GetProperty("name").GetString() ?? spell.Name;
            }
        }

        using (var runes = JsonDocument.Parse(await http.GetStringAsync($"{DataDragon}/cdn/{version}/data/en_US/runesReforged.json", ct)))
        {
            foreach (var style in runes.RootElement.EnumerateArray())
            {
                names.Runes[style.GetProperty("id").GetInt32()] = style.GetProperty("name").GetString() ?? string.Empty;
                foreach (var slot in style.GetProperty("slots").EnumerateArray())
                {
                    foreach (var rune in slot.GetProperty("runes").EnumerateArray())
                        names.Runes[rune.GetProperty("id").GetInt32()] = rune.GetProperty("name").GetString() ?? string.Empty;
                }
            }
        }

        return names;
    }

    private sealed class Names
    {
        public Dictionary<int, string> Champions { get; } = [];

        public Dictionary<int, string> Items { get; } = [];

        public Dictionary<int, string> Spells { get; } = [];

        public Dictionary<int, string> Runes { get; } = [];

        public string Item(int id) => Items.GetValueOrDefault(id, id.ToString(CultureInfo.InvariantCulture));

        public string Spell(int id) => Spells.GetValueOrDefault(id, id.ToString(CultureInfo.InvariantCulture));

        public string Rune(int id) => Runes.GetValueOrDefault(id, id.ToString(CultureInfo.InvariantCulture));
    }
}
