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
    public IReadOnlyList<string> Tiers { get; init; } = ["platinum"];

    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>How many recent Solo/Duo games to list per player.</summary>
    public int MatchesPerPlayer { get; init; } = 20;

    /// <summary>How many players to take from one page of a league list before moving on.</summary>
    public int PlayersPerPage { get; init; } = 10;

    /// <summary>Counted games between two saves of the state.</summary>
    public int SaveEvery { get; init; } = 100;

    /// <summary>Stop after this many league pages even with time left; null for no limit.</summary>
    public int? MaxPages { get; init; }

    /// <summary>
    /// In every n-th counted game one player is asked about (champion mastery, one request): the one on
    /// the rarest champion so far, where a win rate is most likely carried by its mains. 4 costs about
    /// a tenth of the games per hour; 0 asks nobody.
    /// </summary>
    public int MasteryEvery { get; init; } = 4;
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

    /// <summary>The draft numbers of the same games: lane rows, duels, duos, bans, damage.</summary>
    public RiotMetaCounts Meta { get; set; } = new();

    /// <summary>Situational item counts of the same games, see <see cref="RiotSituationalTally"/>.</summary>
    public List<int[]> Situational { get; set; } = [];

    /// <summary>Patches whose game lines are kept besides the current one.</summary>
    public const int OlderPatchesKept = 2;

    /// <summary>
    /// The counted games of this patch, one line each (<see cref="RiotGames.Encode"/>): for measuring
    /// what each part of the draft model predicts.
    /// </summary>
    public List<int[]> Games { get; set; } = [];

    /// <summary>What players take against each lane opponent, see <see cref="RiotMatchupTally"/>; this patch only.</summary>
    public List<int[]> Matchups { get; set; } = [];

    /// <summary>
    /// The game lines of the patches before, newest first, at most <see cref="OlderPatchesKept"/>.
    /// Synergies and counters hang on the champions' kits more than on the patch, so the measurement
    /// learns them over three patches; strength it takes from the current one.
    /// </summary>
    public List<PatchGames> OlderGames { get; set; } = [];

    /// <summary>
    /// The last patch's counts, kept when a new patch starts. For the first day of a patch the new
    /// count is too thin to stand on; the previous patch's numbers, marked as such, beat an empty file.
    /// </summary>
    public PreviousPatch? Previous { get; set; }

    /// <summary>
    /// Starts counting a new patch (or platform). The counts of the patch that ends become
    /// <see cref="Previous"/> and its game lines move to the front of <see cref="OlderGames"/> — both
    /// only for the same platform and only when it counted something: a bracket or region switch is
    /// a different population, not an older version of this one.
    /// </summary>
    public void StartPatch(string patch, string platform)
    {
        var samePopulation = Patch.Length > 0 && Platform == platform;
        Previous = samePopulation && Meta.Matches > 0
            ? new PreviousPatch { Patch = Patch, Tally = Tally, Meta = Meta, Situational = Situational }
            : null;

        var ending = samePopulation && Games.Count > 0 ? new[] { new PatchGames { Patch = Patch, Games = Games } } : [];
        OlderGames = samePopulation
            ? [.. ending.Concat((OlderGames ?? []).Where(older => older.Patch != Patch && older.Patch != patch)).Take(OlderPatchesKept)]
            : [];

        Patch = patch;
        Platform = platform;
        OldPatchLatestMs = 0;
        Seen = [];
        Tally = new RiotBuildFile();
        Meta = new RiotMetaCounts();
        Situational = [];
        Games = [];
        Matchups = [];
    }

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

/// <summary>A finished patch's counts, see <see cref="CollectorState.Previous"/>.</summary>
public sealed class PreviousPatch
{
    public string Patch { get; set; } = string.Empty;

    public RiotBuildFile Tally { get; set; } = new();

    public RiotMetaCounts Meta { get; set; } = new();

    public List<int[]> Situational { get; set; } = [];
}

/// <summary>
/// Who a champion is, for the situations: Riot's class tags and defense rating (Data Dragon) and the
/// curated hard-CC trait. Without it the collector counts no situational items.
/// </summary>
public sealed record ChampionStatics(IReadOnlyDictionary<int, ChampionEntry> Champions, TraitTable Traits);

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
public sealed class RiotCollector(
    RiotApiClient api,
    ItemCatalog items,
    CollectorOptions options,
    Action<string>? log = null,
    ChampionStatics? statics = null)
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

            state.StartPatch(patch, options.Platform);
        }

        var tally = new RiotBuildTally();
        tally.Add(state.Tally.Builds);
        var meta = new RiotMetaTally();
        meta.Add(state.Meta);
        var situational = new RiotSituationalTally();
        situational.Add(state.Situational);

        // Damage type as counted so far in this patch; class and CC from Data Dragon and the traits.
        EnemyProfile Profile(int championId)
        {
            var entry = statics?.Champions.GetValueOrDefault(championId);
            return new EnemyProfile(
                meta.DamageOfChampion(championId),
                entry is not null && (entry.IsTagged("Tank") || entry.Defense >= Situations.FrontlineDefense),
                entry is not null && statics!.Traits.For(entry.Key).HasHardCc);
        }
        var games = new List<int[]>(state.Games);
        // A refused mastery request costs the samples of this run, never the run itself.
        var askMastery = options.MasteryEvery > 0;
        var matchups = new RiotMatchupTally();
        matchups.Add(state.Matchups ?? []);
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
            state.Meta = meta.Export();
            state.Situational = situational.Export();
            state.Games = games;
            state.Matchups = matchups.Export();
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

                    // A body cut short on the way is one game less, never the end of the run.
                    if (RiotMatchReader.TryReadHeader(matchJson) is not { } header)
                    {
                        skipped++;
                        continue;
                    }

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

                    var reading = RiotMatchReader.TryRead(matchJson, timelineJson, items);
                    if (reading is null || reading.Participants.Count != 10)
                    {
                        skipped++;
                        continue;
                    }

                    foreach (var participant in reading.Participants)
                        tally.Add(participant, tier);
                    meta.Add(reading);
                    matchups.Add(reading);

                    var (masteryIndex, masteryPoints) = (-1, 0);
                    if (askMastery && (counted + 1) % options.MasteryEvery == 0)
                    {
                        var puuids = RiotMatchReader.PlayerIds(matchJson);
                        if (puuids.Count == reading.Participants.Count)
                        {
                            // The rarest champion on its lane so far; the first of equals.
                            var rarest = Enumerable.Range(0, reading.Participants.Count)
                                .MinBy(i => meta.LaneGames(reading.Participants[i].ChampionId, reading.Participants[i].Lane));
                            var sampled = reading.Participants[rarest];
                            try
                            {
                                var masteryJson = await api.GetAsync(options.Platform,
                                    $"/lol/champion-mastery/v4/champion-masteries/by-puuid/{Uri.EscapeDataString(puuids[rarest])}/by-champion/{sampled.ChampionId}", ct).ConfigureAwait(false);
                                if (masteryJson is not null && RiotMatchReader.MasteryPoints(masteryJson) is { } points)
                                    (masteryIndex, masteryPoints) = (rarest, points);
                            }
                            catch (RiotApiException ex)
                            {
                                askMastery = false;
                                log?.Invoke($"Mastery-Abfrage für diesen Lauf abgeschaltet: {ex.Message}");
                            }
                        }
                    }

                    if (RiotGames.Encode(reading, masteryIndex, masteryPoints) is { } line)
                        games.Add(line);
                    if (statics is not null)
                        situational.Add(reading, Profile, items);

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

        using var document = RiotMatchReader.TryParse(json);
        if (json is not null && document is null)
            return [];   // unreadable: another page next time, the bound stays

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

        using var document = RiotMatchReader.TryParse(json);
        if (document is null)
            return [];

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
