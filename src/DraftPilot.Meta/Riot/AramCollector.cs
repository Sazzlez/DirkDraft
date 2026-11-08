using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using DraftPilot.Core.Data;

namespace DraftPilot.Meta.Riot;

public sealed record AramOptions
{
    public string Platform { get; init; } = "euw1";

    public string Region { get; init; } = "europe";

    /// <summary>
    /// The league lists the first players come from. ARAM has no ranks of its own; drawing from the
    /// same bracket as the ranked count keeps the numbers about the same players.
    /// </summary>
    public IReadOnlyList<string> Tiers { get; init; } = ["platinum"];

    /// <summary>
    /// Plain ARAM (450). Mayhem (2400) would be the real target, but Riot's API lists none of its games
    /// and answers 403 for each (measured 07.11.2026) — so ARAM stands in.
    /// </summary>
    public IReadOnlyList<int> Queues { get; init; } = [450];

    /// <summary>Only for <c>riot queues</c>: whose recent games to list.</summary>
    public string SeedRiotId { get; init; } = string.Empty;

    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(30);

    public int MatchesPerPlayer { get; init; } = 20;

    /// <summary>Players taken from one league page; most of them play no ARAM, so a few per page.</summary>
    public int PlayersPerPage { get; init; } = 10;

    public int SaveEvery { get; init; } = 50;
}

/// <summary>The ARAM collector's state between runs; the counts, never a player.</summary>
public sealed class AramState
{
    public int Version { get; set; } = 1;

    public string Patch { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public long OldPatchLatestMs { get; set; }

    public List<long> Seen { get; set; } = [];

    public RiotAramFile Tally { get; set; } = new();

    /// <summary>The last patch's count, published until the new one has enough games.</summary>
    public RiotAramFile? Previous { get; set; }

    public static AramState Load(string path)
    {
        if (!File.Exists(path))
            return new AramState();
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize(gzip, AramJson.Default.AramState) ?? new AramState();
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
            JsonSerializer.Serialize(gzip, this, AramJson.Default.AramState);
        }

        File.Move(temporary, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(AramState))]
public sealed partial class AramJson : JsonSerializerContext;

/// <summary>
/// Counts plain ARAM games. The first players come from random pages of the league lists; after
/// that, the players of every counted game come next — somebody who just played ARAM is the likeliest
/// to have played more. The list of players lives in memory for one run and is never stored or
/// published.
/// </summary>
public sealed class AramCollector(RiotApiClient api, ItemCatalog items, AramOptions options, Action<string>? log = null)
{
    private static readonly TimeSpan FirstLookBack = TimeSpan.FromDays(14);
    private static readonly string[] Divisions = ["I", "II", "III", "IV"];

    /// <summary>Players waiting; beyond it a counted game adds none.</summary>
    private const int MaxLeads = 2000;

    private readonly Random _random = new();
    private readonly Dictionary<string, int> _pages = [];

    public async Task<CollectorReport> RunAsync(AramState state, Action<AramState> save, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var patch = PatchVersion.Line(items.Version);
        if (patch.Length == 0)
            throw new InvalidOperationException("Der Item-Katalog nennt keinen Patch.");

        if (state.Patch != patch || state.Platform != options.Platform)
        {
            log?.Invoke(state.Patch.Length == 0 ? $"Neuer ARAM-Bestand für Patch {patch}." : $"ARAM: Patch {state.Patch} → {patch}, Zählung beginnt neu.");
            state.Previous = state.Platform == options.Platform && state.Tally.Games > 0 ? state.Tally : null;
            state.Patch = patch;
            state.Platform = options.Platform;
            state.OldPatchLatestMs = 0;
            state.Seen = [];
            state.Tally = new RiotAramFile();
        }

        var tally = new RiotAramTally();
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

        var leads = new Queue<string>();
        var asked = new HashSet<string>(StringComparer.Ordinal);
        var pages = 0;

        while (clock.Elapsed < options.Duration && !ct.IsCancellationRequested)
        {
            // Out of leads: a fresh page of a league list.
            if (leads.Count == 0)
            {
                var tier = options.Tiers[_random.Next(options.Tiers.Count)];
                var division = Divisions[_random.Next(Divisions.Length)];
                foreach (var player in await PlayersAsync(tier, division, ct).ConfigureAwait(false))
                    leads.Enqueue(player);
                if (++pages > 400 && leads.Count == 0)
                    break;
                continue;
            }

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

                // A body cut short on the way is one game less, never the end of the run.
                if (RiotMatchReader.TryReadHeader(matchJson) is not { } header)
                {
                    skipped++;
                    continue;
                }

                if (header.Patch != patch)
                {
                    if (PatchVersion.PatchesBetween(header.Patch, patch) > 0)
                        state.OldPatchLatestMs = Math.Max(state.OldPatchLatestMs, header.CreatedAtMs);
                    skipped++;
                    continue;
                }

                if (!header.Counts || !RiotMatchReader.IsAram(header.QueueId))
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

                var reading = RiotMatchReader.TryRead(matchJson, timelineJson, items);
                if (reading is null || reading.Participants.Count != 10)
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
                    log?.Invoke($"ARAM: {counted} Spiele gezählt ({skipped} übersprungen), {api.Requests} Abrufe, {leads.Count} Spieler offen");
                if (counted % options.SaveEvery == 0)
                    Persist();
            }
        }

        Persist();
        return new CollectorReport(counted, skipped, api.Requests, api.Throttled, clock.Elapsed);
    }

    /// <summary>The queue ids of a player's latest games, to find the one a mode runs under now.</summary>
    public async Task<IReadOnlyDictionary<int, int>> RecentQueuesAsync(int games, CancellationToken ct)
    {
        var result = new Dictionary<int, int>();
        var seed = await PuuidOfRiotIdAsync(ct).ConfigureAwait(false);
        if (seed is null)
            return result;

        var json = await api.GetAsync(options.Region, $"/lol/match/v5/matches/by-puuid/{Uri.EscapeDataString(seed)}/ids?count={games}", ct).ConfigureAwait(false);
        if (json is null)
            return result;

        using var document = RiotMatchReader.TryParse(json);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var id in document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty))
        {
            var match = await api.GetAsync(options.Region, $"/lol/match/v5/matches/{id}", ct).ConfigureAwait(false);
            if (match is null)
                continue;
            if (RiotMatchReader.TryReadHeader(match) is { } header)
                result[header.QueueId] = result.GetValueOrDefault(header.QueueId) + 1;
        }

        return result;
    }

    private async Task<string?> PuuidOfRiotIdAsync(CancellationToken ct)
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
        using var document = RiotMatchReader.TryParse(json);
        return document is not null && document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("puuid", out var puuid) && puuid.ValueKind == JsonValueKind.String ? puuid.GetString() : null;
    }

    /// <summary>Active players from one random page of a league list, a few of them, shuffled.</summary>
    private async Task<List<string>> PlayersAsync(string tier, string division, CancellationToken ct)
    {
        var key = $"{tier}/{division}";
        var bound = _pages.GetValueOrDefault(key, 200);
        var page = 1 + _random.Next(bound);

        var json = await api.GetAsync(
            options.Platform,
            $"/lol/league/v4/entries/RANKED_SOLO_5x5/{tier.ToUpperInvariant()}/{division}?page={page}",
            ct).ConfigureAwait(false);

        using var document = RiotMatchReader.TryParse(json);
        if (json is not null && document is null)
            return [];   // unreadable: another page next time, the bound stays

        if (document is null || document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            // Past the last page: the next draw stays below this one.
            _pages[key] = Math.Max(1, page - 1);
            return [];
        }

        return [.. document.RootElement.EnumerateArray()
            .Where(entry => !(entry.TryGetProperty("inactive", out var inactive) && inactive.ValueKind == JsonValueKind.True))
            .Select(entry => entry.TryGetProperty("puuid", out var puuid) && puuid.ValueKind == JsonValueKind.String ? puuid.GetString() ?? string.Empty : string.Empty)
            .Where(puuid => puuid.Length > 0)
            .OrderBy(_ => _random.Next())
            .Take(options.PlayersPerPage)];
    }

    private async Task<List<string>> MatchIdsAsync(string puuid, AramState state, CancellationToken ct)
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
            using var document = RiotMatchReader.TryParse(json);
            if (document is not null && document.RootElement.ValueKind == JsonValueKind.Array)
                ids.AddRange(document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty));
        }

        return ids;
    }
}
