using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using DraftPilot.Core.Data;

namespace DraftPilot.Meta.Riot;

public sealed record MayhemOptions
{
    public string Platform { get; init; } = "euw1";

    public string Region { get; init; } = "europe";

    /// <summary>Where the snowball starts: a Riot ID ("Name#TAG") of someone who plays Mayhem.</summary>
    public string SeedRiotId { get; init; } = string.Empty;

    /// <summary>
    /// The queue ids asked for. Riot has shipped Mayhem under several (2400, 2410, …); each costs one
    /// list request per player, so only the live one belongs here.
    /// </summary>
    public IReadOnlyList<int> Queues { get; init; } = [2400];

    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(30);

    public int MatchesPerPlayer { get; init; } = 20;

    public int SaveEvery { get; init; } = 50;
}

/// <summary>The Mayhem collector's state between runs; the counts, never a player.</summary>
public sealed class MayhemState
{
    public int Version { get; set; } = 1;

    public string Patch { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public long OldPatchLatestMs { get; set; }

    public List<long> Seen { get; set; } = [];

    public RiotMayhemFile Tally { get; set; } = new();

    /// <summary>The last patch's count, published until the new one has enough games.</summary>
    public RiotMayhemFile? Previous { get; set; }

    public static MayhemState Load(string path)
    {
        if (!File.Exists(path))
            return new MayhemState();
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize(gzip, MayhemJson.Default.MayhemState) ?? new MayhemState();
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        using (var file = File.Create(temporary))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        {
            JsonSerializer.Serialize(gzip, this, MayhemJson.Default.MayhemState);
        }

        File.Move(temporary, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(MayhemState))]
public sealed partial class MayhemJson : JsonSerializerContext;

/// <summary>
/// Counts ARAM Mayhem games. Mayhem has no league lists to draw players from, so the collector
/// starts at one Riot ID and follows the players of every counted game — people who just played
/// Mayhem are the likeliest to have played more. The list of players lives in memory for one run
/// and is never stored or published.
/// </summary>
public sealed class MayhemCollector(RiotApiClient api, ItemCatalog items, MayhemOptions options, Action<string>? log = null)
{
    private static readonly TimeSpan FirstLookBack = TimeSpan.FromDays(14);

    /// <summary>Players waiting; beyond it a counted game adds none.</summary>
    private const int MaxLeads = 2000;

    public async Task<CollectorReport> RunAsync(MayhemState state, Action<MayhemState> save, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var patch = PatchVersion.Line(items.Version);
        if (patch.Length == 0)
            throw new InvalidOperationException("Der Item-Katalog nennt keinen Patch.");

        if (state.Patch != patch || state.Platform != options.Platform)
        {
            log?.Invoke(state.Patch.Length == 0 ? $"Neuer Mayhem-Bestand für Patch {patch}." : $"Mayhem: Patch {state.Patch} → {patch}, Zählung beginnt neu.");
            state.Previous = state.Platform == options.Platform && state.Tally.Games > 0 ? state.Tally : null;
            state.Patch = patch;
            state.Platform = options.Platform;
            state.OldPatchLatestMs = 0;
            state.Seen = [];
            state.Tally = new RiotMayhemFile();
        }

        var tally = new RiotMayhemTally();
        tally.Add(state.Tally);
        var seen = new HashSet<long>(state.Seen);
        var counted = 0;
        var skipped = 0;

        void Persist()
        {
            state.Seen = [.. seen];
            state.Tally = tally.Export(patch, options.Platform, DateTimeOffset.UtcNow, CollectorState.KeptOptions);
            save(state);
        }

        var seed = await SeedAsync(ct).ConfigureAwait(false);
        if (seed is null)
        {
            log?.Invoke($"Riot-ID {options.SeedRiotId} nicht gefunden — kein Mayhem-Lauf.");
            return new CollectorReport(0, 0, api.Requests, api.Throttled, clock.Elapsed);
        }

        var leads = new Queue<string>([seed]);
        var asked = new HashSet<string>(StringComparer.Ordinal);

        while (leads.Count > 0 && clock.Elapsed < options.Duration && !ct.IsCancellationRequested)
        {
            var puuid = leads.Dequeue();
            if (!asked.Add(puuid))
                continue;

            foreach (var matchId in await MatchIdsAsync(puuid, state, ct).ConfigureAwait(false))
            {
                if (clock.Elapsed >= options.Duration || ct.IsCancellationRequested)
                    break;
                if (!RiotCollector.TryNumber(matchId, out var number) || !seen.Add(number))
                    continue;

                var matchJson = await api.GetAsync(options.Region, $"/lol/match/v5/matches/{matchId}", ct).ConfigureAwait(false);
                if (matchJson is null)
                    continue;

                var header = RiotMatchReader.ReadHeader(matchJson);
                if (header.Patch != patch)
                {
                    if (PatchVersion.PatchesBetween(header.Patch, patch) > 0)
                        state.OldPatchLatestMs = Math.Max(state.OldPatchLatestMs, header.CreatedAtMs);
                    skipped++;
                    continue;
                }

                if (!header.Counts || !RiotMatchReader.IsMayhem(header.QueueId))
                {
                    skipped++;
                    continue;
                }

                var timelineJson = await api.GetAsync(options.Region, $"/lol/match/v5/matches/{matchId}/timeline", ct).ConfigureAwait(false);
                if (timelineJson is null)
                {
                    skipped++;
                    continue;
                }

                var reading = RiotMatchReader.Read(matchJson, timelineJson, items);
                if (reading.Participants.Count != 10)
                {
                    skipped++;
                    continue;
                }

                tally.Add(reading);
                counted++;

                if (leads.Count < MaxLeads)
                {
                    foreach (var player in RiotMatchReader.PlayerIds(matchJson))
                    {
                        if (!asked.Contains(player))
                            leads.Enqueue(player);
                    }
                }

                if (counted % 25 == 0)
                    log?.Invoke($"Mayhem: {counted} Spiele gezählt ({skipped} übersprungen), {api.Requests} Abrufe, {leads.Count} Spieler offen");
                if (counted % options.SaveEvery == 0)
                    Persist();
            }
        }

        if (counted == 0 && asked.Count <= 1)
            log?.Invoke($"Mayhem: in den letzten Spielen der Riot-ID keine Spiele der Queue {string.Join("/", options.Queues)} — Queue-ID prüfen (riot queues).");

        Persist();
        return new CollectorReport(counted, skipped, api.Requests, api.Throttled, clock.Elapsed);
    }

    /// <summary>The queue ids of a player's latest games, to find the one Mayhem runs under now.</summary>
    public async Task<IReadOnlyDictionary<int, int>> RecentQueuesAsync(int games, CancellationToken ct)
    {
        var result = new Dictionary<int, int>();
        var seed = await SeedAsync(ct).ConfigureAwait(false);
        if (seed is null)
            return result;

        var json = await api.GetAsync(options.Region, $"/lol/match/v5/matches/by-puuid/{Uri.EscapeDataString(seed)}/ids?count={games}", ct).ConfigureAwait(false);
        if (json is null)
            return result;

        using var document = JsonDocument.Parse(json);
        foreach (var id in document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty))
        {
            var match = await api.GetAsync(options.Region, $"/lol/match/v5/matches/{id}", ct).ConfigureAwait(false);
            if (match is null)
                continue;
            var queue = RiotMatchReader.ReadHeader(match).QueueId;
            result[queue] = result.GetValueOrDefault(queue) + 1;
        }

        return result;
    }

    private async Task<string?> SeedAsync(CancellationToken ct)
    {
        var hash = options.SeedRiotId.LastIndexOf('#');
        if (hash <= 0 || hash == options.SeedRiotId.Length - 1)
            return null;

        var name = options.SeedRiotId[..hash].Trim();
        var tag = options.SeedRiotId[(hash + 1)..].Trim();
        var json = await api.GetAsync(
            options.Region,
            $"/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(tag)}",
            ct).ConfigureAwait(false);
        if (json is null)
            return null;

        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("puuid", out var puuid) && puuid.ValueKind == JsonValueKind.String ? puuid.GetString() : null;
    }

    private async Task<List<string>> MatchIdsAsync(string puuid, MayhemState state, CancellationToken ct)
    {
        var since = state.OldPatchLatestMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(state.OldPatchLatestMs)
            : DateTimeOffset.UtcNow - FirstLookBack;
        var ids = new List<string>();
        foreach (var queue in options.Queues)
        {
            var json = await api.GetAsync(
                options.Region,
                $"/lol/match/v5/matches/by-puuid/{Uri.EscapeDataString(puuid)}/ids?queue={queue}&startTime={since.ToUnixTimeSeconds()}&count={options.MatchesPerPlayer}",
                ct).ConfigureAwait(false);
            if (json is null)
                continue;
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
                ids.AddRange(document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty));
        }

        return ids;
    }
}
