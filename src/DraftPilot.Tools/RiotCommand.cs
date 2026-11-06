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
            "export" => Export(options),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            riot collect [--minutes 30] [--tiers gold,platinum] [--state build/riot-state.json.gz] [--key-file datei]
            riot show <champion> <lane> [--tier gold] [--state build/riot-state.json.gz]
            riot status [--state build/riot-state.json.gz]
            riot export --out builds.json [--keep 6] [--min-games 3] [--state build/riot-state.json.gz]
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
    private static int Export(Dictionary<string, string> options)
    {
        var statePath = options.GetValueOrDefault("state", Path.Combine("build", "riot-state.json.gz"));
        if (!options.TryGetValue("out", out var outPath) || outPath.Length == 0)
            return Usage();

        var keep = int.Parse(options.GetValueOrDefault("keep", "6"), CultureInfo.InvariantCulture);
        var minimum = int.Parse(options.GetValueOrDefault("min-games", "3"), CultureInfo.InvariantCulture);

        var state = CollectorState.Load(statePath);
        var tally = new RiotBuildTally();
        tally.Add(state.Tally.Builds);

        var file = new RiotBuildFile
        {
            Patch = state.Tally.Patch,
            Platform = state.Tally.Platform,
            CollectedAtUtc = state.Tally.CollectedAtUtc,
            Matches = state.Tally.Matches,
            Builds = tally.Export(keep, minimum),
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(outPath, JsonSerializer.Serialize(file, RiotBuildJson.Default.RiotBuildFile));
        Console.WriteLine($"{file.Builds.Count} Champion-Lanes, {new FileInfo(outPath).Length / 1024:N0} KB → {outPath}");
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
        var tiers = options.GetValueOrDefault("tiers", "gold,platinum")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tier => tier.ToLowerInvariant())
            .ToList();

        using var http = new HttpClient();
        var catalog = await LatestItemsAsync(http, ct);
        Console.WriteLine($"Patch {catalog.Version}, {catalog.Count} Items. Ränge: {string.Join(", ", tiers)}. Laufzeit {minutes:0.#} min.");

        var state = CollectorState.Load(statePath);
        using var api = new RiotApiClient(key);
        var collector = new RiotCollector(
            api,
            catalog,
            new CollectorOptions { Tiers = tiers, Duration = TimeSpan.FromMinutes(minutes) },
            line => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"));

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
