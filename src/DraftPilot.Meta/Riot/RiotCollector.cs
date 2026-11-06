using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using DraftPilot.Core.Data;

namespace DraftPilot.Meta.Riot;

public sealed record CollectorOptions
{
    /// <summary>Where the league lists live, e.g. <c>euw1</c>.</summary>
    public string Platform { get; init; } = "euw1";

    /// <summary>Where the matches of that platform live, e.g. <c>europe</c>.</summary>
    public string Region { get; init; } = "europe";

    /// <summary>Rank brackets to sample, as Riot spells them in lower case: <c>gold</c>, <c>platinum</c>.</summary>
    public IReadOnlyList<string> Tiers { get; init; } = ["gold", "platinum"];

    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>How many recent Solo/Duo games to list per player.</summary>
    public int MatchesPerPlayer { get; init; } = 20;

    /// <summary>How many players to take from one page of a league list before moving on.</summary>
    public int PlayersPerPage { get; init; } = 10;

    /// <summary>Counted games between two saves of the state.</summary>
    public int SaveEvery { get; init; } = 100;

    /// <summary>Stop after this many league pages even with time left; null for no limit.</summary>
    public int? MaxPages { get; init; }
}

public sealed record CollectorReport(int Counted, int Skipped, int Requests, int Throttled, TimeSpan Elapsed);

/// <summary>
/// What the collector carries from one run to the next. No player is in it: the players are found
/// fresh on the league lists every run, and only the ids of matches already counted are kept —
/// which is what stops a game seen from two of its ten players from counting twice.
/// </summary>
public sealed class CollectorState
{
    /// <summary>Options per slot kept between runs: generous, the published file is cut much tighter.</summary>
    public const int KeptOptions = 40;

    public int Version { get; set; } = 1;

    /// <summary>The patch line being counted. A new patch starts the counts over.</summary>
    public string Patch { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// The newest game seen from an earlier patch, Unix ms. Everything after it is the current
    /// patch, so match lists are asked from here on and old games stop costing requests.
    /// </summary>
    public long OldPatchLatestMs { get; set; }

    /// <summary>Numeric parts of the match ids already looked at, counted or not.</summary>
    public List<long> Seen { get; set; } = [];

    public RiotBuildFile Tally { get; set; } = new();

    public static CollectorState Load(string path)
    {
        if (!File.Exists(path))
            return new CollectorState();

        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize(gzip, CollectorJson.Default.CollectorState) ?? new CollectorState();
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
            JsonSerializer.Serialize(gzip, this, CollectorJson.Default.CollectorState);
        }

        File.Move(temporary, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(CollectorState))]
public sealed partial class CollectorJson : JsonSerializerContext;

/// <summary>
/// Collects ranked games from the Riot API and counts their builds — the way u.gg and OP.GG get
/// theirs, at the scale one personal key allows. Players are drawn from random pages of the league
/// lists of the chosen brackets; their recent Solo/Duo games of the current patch are read, and
/// a game's timeline — the second request, and the one that knows the buying and skilling order —
/// is fetched only once the match itself has shown that the game counts.
/// </summary>
public sealed class RiotCollector(RiotApiClient api, ItemCatalog items, CollectorOptions options, Action<string>? log = null)
{
    private static readonly string[] Divisions = ["I", "II", "III", "IV"];

    /// <summary>How far back to list games before anything is known about where the patch began.</summary>
    private static readonly TimeSpan FirstLookBack = TimeSpan.FromDays(14);

    /// <summary>
    /// The highest page tried per bracket and division. A division of a mid bracket on EUW runs to
    /// hundreds of pages; an empty answer pulls the bound in so the next draw lands inside.
    /// </summary>
    private readonly Dictionary<string, int> _pages = [];

    private readonly Random _random = new();

    /// <summary>Leads kept waiting per bracket; beyond it a league page's player adds none.</summary>
    private const int MaxLeads = 400;

    public async Task<CollectorReport> RunAsync(CollectorState state, Action<CollectorState> save, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var patch = PatchVersion.Line(items.Version);
        if (patch.Length == 0)
            throw new InvalidOperationException("Der Item-Katalog nennt keinen Patch.");

        // A new patch starts over: last patch's builds describe a game that no longer exists.
        if (state.Patch != patch || state.Platform != options.Platform)
        {
            log?.Invoke(state.Patch.Length == 0 ? $"Neuer Bestand für Patch {patch}." : $"Patch {state.Patch} → {patch}: Zählung beginnt neu.");
            state.Patch = patch;
            state.Platform = options.Platform;
            state.OldPatchLatestMs = 0;
            state.Seen = [];
            state.Tally = new RiotBuildFile();
        }

        var tally = new RiotBuildTally();
        tally.Add(state.Tally.Builds);
        var seen = new HashSet<long>(state.Seen);
        var matches = new Dictionary<string, int>(state.Tally.Matches, StringComparer.OrdinalIgnoreCase);

        var counted = 0;
        var skipped = 0;
        var round = 0;
        var pagesFetched = 0;

        // Players met in a counted game, per bracket: proven active this patch and matched into the
        // same rank, so their lists hold current games where a random name off a league page often
        // has none (measured: 3,7 requests per counted game from league pages alone). One step deep
        // only — a lead's own games add no further leads, or the bracket would drift away from the
        // league list it was drawn from. In memory for this run, never stored.
        var leads = options.Tiers.ToDictionary(tier => tier, _ => new Queue<string>(), StringComparer.OrdinalIgnoreCase);
        var asked = new HashSet<string>(StringComparer.Ordinal);

        void Persist()
        {
            state.Seen = [.. seen];
            state.Tally = new RiotBuildFile
            {
                Patch = patch,
                Platform = options.Platform,
                CollectedAtUtc = DateTimeOffset.UtcNow,
                Matches = new Dictionary<string, int>(matches),
                Builds = tally.Export(CollectorState.KeptOptions),
            };
            save(state);
        }

        while (clock.Elapsed < options.Duration && !ct.IsCancellationRequested
            && (options.MaxPages is not { } pages || pagesFetched < pages || leads.Values.Any(queue => queue.Count > 0)))
        {
            var tier = options.Tiers[round++ % options.Tiers.Count];
            var queue = leads[tier];

            List<string> players;
            var fromLeague = queue.Count == 0;
            if (fromLeague)
            {
                if (options.MaxPages is { } max && pagesFetched >= max)
                    continue;

                pagesFetched++;
                players = await PlayersAsync(tier, Divisions[_random.Next(Divisions.Length)], ct).ConfigureAwait(false);
            }
            else
            {
                players = [];
                while (queue.Count > 0 && players.Count < options.PlayersPerPage)
                    players.Add(queue.Dequeue());
            }

            foreach (var puuid in players)
            {
                if (clock.Elapsed >= options.Duration || ct.IsCancellationRequested)
                    break;

                if (!asked.Add(puuid))
                    continue;

                foreach (var matchId in await MatchIdsAsync(puuid, state, ct).ConfigureAwait(false))
                {
                    if (clock.Elapsed >= options.Duration || ct.IsCancellationRequested)
                        break;

                    if (!TryNumber(matchId, out var number) || !seen.Add(number))
                        continue;

                    var matchJson = await api.GetAsync(options.Region, $"/lol/match/v5/matches/{matchId}", ct).ConfigureAwait(false);
                    if (matchJson is null)
                        continue;

                    var header = RiotMatchReader.ReadHeader(matchJson);
                    if (header.Patch != patch)
                    {
                        // An older game tells us the patch began after it; the next match lists
                        // start there. A newer one (the patch landed mid-run) waits for the next run.
                        if (PatchVersion.PatchesBetween(header.Patch, patch) > 0)
                            state.OldPatchLatestMs = Math.Max(state.OldPatchLatestMs, header.CreatedAtMs);

                        skipped++;
                        continue;
                    }

                    if (!header.Counts)
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

                    foreach (var participant in reading.Participants)
                        tally.Add(participant, tier);

                    if (fromLeague && queue.Count < MaxLeads)
                    {
                        foreach (var player in RiotMatchReader.PlayerIds(matchJson))
                        {
                            if (!asked.Contains(player))
                                queue.Enqueue(player);
                        }
                    }

                    matches[tier] = matches.GetValueOrDefault(tier) + 1;
                    counted++;

                    if (counted % 25 == 0)
                    {
                        var perHour = counted / Math.Max(clock.Elapsed.TotalHours, 1e-6);
                        log?.Invoke($"{counted} Spiele gezählt ({skipped} übersprungen), {api.Requests} Abrufe, {perHour:N0} Spiele/Stunde, {string.Join(", ", matches.Select(m => $"{m.Key} {m.Value}"))}");
                    }

                    if (counted % options.SaveEvery == 0)
                        Persist();
                }
            }
        }

        Persist();
        return new CollectorReport(counted, skipped, api.Requests, api.Throttled, clock.Elapsed);
    }

    /// <summary>
    /// Active players from one random page of a league list: not flagged inactive, and with enough
    /// games this season that their recent ones are likely to be on the current patch.
    /// </summary>
    private async Task<List<string>> PlayersAsync(string tier, string division, CancellationToken ct)
    {
        var key = $"{tier}/{division}";
        var bound = _pages.GetValueOrDefault(key, 200);
        var page = 1 + _random.Next(bound);

        var json = await api.GetAsync(
            options.Platform,
            $"/lol/league/v4/entries/RANKED_SOLO_5x5/{tier.ToUpperInvariant()}/{division}?page={page}",
            ct).ConfigureAwait(false);

        using var document = json is null ? null : JsonDocument.Parse(json);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            _pages[key] = Math.Max(1, page - 1);
            return [];
        }

        return [.. document.RootElement.EnumerateArray()
            .Where(entry => !(entry.TryGetProperty("inactive", out var inactive) && inactive.ValueKind == JsonValueKind.True))
            .Where(entry => Int(entry, "wins") + Int(entry, "losses") >= 20)
            .Select(entry => entry.TryGetProperty("puuid", out var puuid) && puuid.ValueKind == JsonValueKind.String ? puuid.GetString() ?? string.Empty : string.Empty)
            .Where(puuid => puuid.Length > 0)
            .OrderBy(_ => _random.Next())
            .Take(options.PlayersPerPage)];
    }

    private async Task<List<string>> MatchIdsAsync(string puuid, CollectorState state, CancellationToken ct)
    {
        var since = state.OldPatchLatestMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(state.OldPatchLatestMs)
            : DateTimeOffset.UtcNow - FirstLookBack;

        var json = await api.GetAsync(
            options.Region,
            $"/lol/match/v5/matches/by-puuid/{Uri.EscapeDataString(puuid)}/ids?queue={RiotMatchReader.RankedSoloQueue}&type=ranked&startTime={since.ToUnixTimeSeconds()}&count={options.MatchesPerPlayer}",
            ct).ConfigureAwait(false);

        if (json is null)
            return [];

        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? [.. document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty)]
            : [];
    }

    /// <summary>"EUW1_7123456789" → 7123456789. Ids of one platform differ only in the number.</summary>
    public static bool TryNumber(string matchId, out long number)
    {
        number = 0;
        var underscore = matchId.LastIndexOf('_');
        return underscore >= 0 && long.TryParse(matchId.AsSpan(underscore + 1), out number);
    }

    private static int Int(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
}
