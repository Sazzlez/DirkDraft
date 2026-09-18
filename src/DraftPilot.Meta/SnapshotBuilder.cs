using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta.OpGg;

namespace DraftPilot.Meta;

public sealed record SnapshotBuildOptions
{
    public string GameMode { get; init; } = "ranked";

    /// <summary>
    /// Rank bracket for the per-champion analysis — lane stats, counters and tier all come back
    /// for it. <c>all</c> is the all-rank aggregate, an empty value leaves the parameter off
    /// (OP.GG then answers with <c>emerald_plus</c>, which is also what the tier list always
    /// returns). A named bracket such as <c>gold</c> or <c>platinum</c> makes the analysis rows
    /// authoritative for the lane stats too, because otherwise one score would mix two populations.
    /// <para>
    /// The old comment here claimed rank-filtered calls come back empty. Measured on 2026-09-18
    /// against the live endpoint, they do not: <c>tier=gold</c> answers for Darius Top with 100.734
    /// games and counters over 241 to 343 games each (docs/opgg-schnittstelle.md).
    /// </para>
    /// </summary>
    public string Tier { get; init; } = "all";

    /// <summary>
    /// Whether <see cref="Tier"/> names one bracket rather than an aggregate. Only then may the
    /// analysis rows replace the tier list's, which always describes OP.GG's default bracket.
    /// </summary>
    public bool HasRankBracket => Tier.Length > 0 && !Tier.Equals("all", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pull the richer duo lists for bot lane, which is where duo choice actually matters.</summary>
    public bool IncludeDuoSynergies { get; init; } = true;

    /// <summary>Fetch champion portraits that are not cached yet.</summary>
    public bool DownloadIcons { get; init; } = true;

    /// <summary>Language for item, rune and spell names, matching the user's client.</summary>
    public string Language { get; init; } = "de_DE";

    /// <summary>Concurrent requests. Deliberately low; this is somebody else's free endpoint.</summary>
    /// <summary>
    /// Parallel OP.GG calls — the update is ~320 of them, and this is its only real throttle.
    /// Measured against the live endpoint (2026-08): throughput scales cleanly to 12 in flight
    /// with zero 429s, so ten is a comfortable middle that still behaves like a guest — the
    /// update runs for two minutes every few days, and the client honours Retry-After should
    /// OP.GG ever start pushing back.
    /// </summary>
    public int MaxConcurrency { get; init; } = 10;
}

public sealed record BuildProgress(string Stage, int Done, int Total)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Done / Total, 0, 1);

    public override string ToString() => Total > 0 ? $"{Stage} {Done}/{Total}" : Stage;
}

/// <summary>
/// Assembles a <see cref="MetaSnapshot"/> from OP.GG. Runs only when the user presses the update
/// button; everything it produces is then read from disk.
/// </summary>
public sealed class SnapshotBuilder : IDisposable
{
    private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";

    /// <summary>Skin variants share the champion list; real champion ids stay well below this.</summary>
    private const int MaxRealChampionId = 3_000;

    private static readonly string[] AnalysisFields =
    [
        "data.damage_type",
        // The patch OP.GG aggregated these numbers over, which is not the client patch we stamp on
        // the file — after a patch day the two disagree, and only this one says whether the numbers
        // still describe the game being played.
        "data.trends.win.version",
        "data.trends.win.created_at",
        "data.summary.positions[].name",
        "data.summary.positions[].stats.play",
        "data.summary.positions[].stats.win_rate",
        "data.summary.positions[].stats.pick_rate",
        "data.summary.positions[].stats.ban_rate",
        "data.summary.positions[].stats.role_rate",
        "data.summary.positions[].stats.tier_data.tier",
        // The only exact win counter this endpoint has: positions[].stats reports win_rate rounded
        // to two decimals, the role split reports wins AND games. Measured against the tier list's
        // exact number for the same bracket, the reconstruction lands within 0.10 points — a tenth
        // of what the rounding costs. The roles cover ~98 % of a lane's games; the rest carries no
        // role and simply does not enter.
        "data.summary.positions[].roles[].stats.play",
        "data.summary.positions[].roles[].stats.win",
        "data.summary.positions[].counters[].champion_name",
        "data.summary.positions[].counters[].play",
        "data.summary.positions[].counters[].win",
        "data.weak_counters[].champion_name",
        "data.weak_counters[].play",
        "data.weak_counters[].my_win_rate",
        "data.strong_counters[].champion_name",
        "data.strong_counters[].play",
        "data.strong_counters[].my_win_rate",
        "data.synergies.adc[].synergy_champion_name",
        "data.synergies.adc[].play",
        "data.synergies.adc[].win_rate",
        "data.synergies.adc[].synergy_tier_data.tier",
        "data.synergies.support[].synergy_champion_name",
        "data.synergies.support[].play",
        "data.synergies.support[].win_rate",
        "data.synergies.support[].synergy_tier_data.tier",
        "data.synergies.jungle[].synergy_champion_name",
        "data.synergies.jungle[].play",
        "data.synergies.jungle[].win_rate",
        "data.synergies.jungle[].synergy_tier_data.tier",
        "data.synergies.mid[].synergy_champion_name",
        "data.synergies.mid[].play",
        "data.synergies.mid[].win_rate",
        "data.synergies.mid[].synergy_tier_data.tier",
    ];

    private static readonly string[] DuoSynergyFields =
    [
        "data.synergies[].synergy_champion_name",
        "data.synergies[].play",
        "data.synergies[].win_rate",
        "data.synergies[].synergy_tier_data.tier",
    ];

    private static readonly string[] LaneMetaFields = BuildLaneMetaFields();

    private readonly OpGgMcpClient _client;
    private readonly HttpClient _staticData;
    private readonly bool _ownsStaticData;

    /// <summary>
    /// How often each requested field was asked for, and how often OP.GG rejected it. A renamed
    /// field otherwise disappears silently and its numbers simply read as zero.
    /// <para>
    /// Both halves are needed, because a rejection is normal: the analysis response leaves out the
    /// synergy branch for the champion's OWN lane, so every support champion reports
    /// <c>data.synergies.support[]…</c> as unmatched. Only a field rejected by EVERY response that
    /// asked for it is actually gone — anything else would cry wolf on every single update.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, (int Requested, int Unmatched)> _fieldReports = new(StringComparer.Ordinal);

    public SnapshotBuilder(OpGgMcpClient client, HttpClient? staticData = null)
    {
        _client = client;
        _ownsStaticData = staticData is null;
        _staticData = staticData ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<MetaSnapshot> BuildAsync(
        SnapshotBuildOptions options,
        IProgress<BuildProgress>? progress,
        CancellationToken ct)
    {
        var snapshot = new MetaSnapshot
        {
            GameMode = options.GameMode,
            Tier = options.Tier,
            BuiltAtUtc = DateTimeOffset.UtcNow,
        };

        _fieldReports.Clear();

        progress?.Report(new BuildProgress("Patch", 0, 0));
        snapshot.Patch = await ReadPatchAsync(snapshot.Warnings, ct).ConfigureAwait(false);

        progress?.Report(new BuildProgress("Champions", 0, 0));
        var champions = await LoadChampionsAsync(ct).ConfigureAwait(false);
        await ApplyStaticDataAsync(champions, snapshot.Patch, snapshot.Warnings, ct).ConfigureAwait(false);
        snapshot.Champions.AddRange(champions.Values.OrderBy(entry => entry.Id));

        var resolver = new ChampionResolver(champions.Values);

        progress?.Report(new BuildProgress("Tierlist", 0, 0));
        await LoadLaneMetaAsync(snapshot, resolver, ct).ConfigureAwait(false);

        var primaryLanes = PrimaryLanes(snapshot);

        await LoadAnalysisAsync(snapshot, resolver, primaryLanes, options, progress, ct).ConfigureAwait(false);

        if (options.IncludeDuoSynergies)
            await LoadDuoSynergiesAsync(snapshot, resolver, primaryLanes, options, progress, ct).ConfigureAwait(false);

        if (options.DownloadIcons)
        {
            var added = await new IconDownloader(_staticData)
                .DownloadMissingAsync(snapshot.Champions, snapshot.Patch, snapshot.Warnings, progress, ct)
                .ConfigureAwait(false);

            if (added > 0)
                snapshot.Warnings.Add($"{added} Champion-Icons neu geladen.");

            // Rune/spell icons and localised names for the in-game build view. ~85 small files
            // once per install, then only what a new patch renames.
            await new AssetDownloader(_staticData)
                .DownloadRuneAndSpellAssetsAsync(snapshot.Patch, options.Language, snapshot.Warnings, progress, ct)
                .ConfigureAwait(false);
        }

        Deduplicate(snapshot);
        snapshot.SynergyBaseline = MeasureSynergyBaseline(snapshot.Synergies);
        snapshot.LaneBaseline = MeasureLaneBaseline(snapshot.LaneStats);
        snapshot.MatchupBaseline = MeasureMatchupBaseline(snapshot.Matchups, snapshot.LaneStats);
        AddCoverageWarnings(snapshot, MissingFields());

        progress?.Report(new BuildProgress("Fertig", 1, 1));
        return snapshot;
    }

    /// <summary>
    /// Reads the current patch from Data Dragon. Only used as a stamp, so a failure is a warning
    /// rather than an abort.
    /// </summary>
    private async Task<string> ReadPatchAsync(List<string> warnings, CancellationToken ct)
    {
        try
        {
            var json = await _staticData.GetStringAsync(VersionsUrl, ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Array
                && document.RootElement.GetArrayLength() > 0
                && document.RootElement[0].ValueKind == JsonValueKind.String)
            {
                return document.RootElement[0].GetString() ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            warnings.Add("Patch-Version konnte nicht gelesen werden.");
        }

        return string.Empty;
    }

    private async Task<Dictionary<int, ChampionEntry>> LoadChampionsAsync(CancellationToken ct)
    {
        var arguments = new JsonObject
        {
            ["desired_output_fields"] = OpGgMcpClient.Fields(
                "data.champions[].champion_id",
                "data.champions[].key",
                "data.champions[].name"),
        };

        var root = await _client.CallToolAsync("lol_list_champions", arguments, ct).ConfigureAwait(false);
        var result = new Dictionary<int, ChampionEntry>();

        foreach (var node in root["data"]["champions"].Items)
        {
            var id = node["champion_id"].AsInt();
            var key = node["key"].AsText() ?? string.Empty;
            var name = node["name"].AsText() ?? string.Empty;

            // The list also carries skin variants (id 60089, key "Jade_Leona"); both signals
            // together separate them from real champions.
            if (id <= 0 || id >= MaxRealChampionId || key.Contains('_', StringComparison.Ordinal))
                continue;

            result[id] = new ChampionEntry { Id = id, Key = key, Name = name };
        }

        return result;
    }

    /// <summary>
    /// Adds Riot's own class tags, attack range and defensive rating from Data Dragon. One request
    /// covers every champion, which is what lets the composition rules work on champions released
    /// after this tool was written instead of relying on a hand-written table that goes stale.
    /// </summary>
    private async Task ApplyStaticDataAsync(
        Dictionary<int, ChampionEntry> champions,
        string patch,
        List<string> warnings,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patch))
        {
            warnings.Add("Ohne Patch-Version keine Champion-Klassendaten — Comp-Regeln laufen eingeschränkt.");
            return;
        }

        var url = $"https://ddragon.leagueoflegends.com/cdn/{patch}/data/en_US/champion.json";

        try
        {
            var json = await _staticData.GetStringAsync(url, ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("data", out var data))
                return;

            var matched = 0;

            foreach (var entry in data.EnumerateObject())
            {
                if (!entry.Value.TryGetProperty("key", out var keyElement)
                    || !int.TryParse(keyElement.GetString(), out var id)
                    || !champions.TryGetValue(id, out var champion))
                {
                    continue;
                }

                if (entry.Value.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tag in tags.EnumerateArray())
                    {
                        if (tag.GetString() is { Length: > 0 } value)
                            champion.Tags.Add(value);
                    }
                }

                // TryGet variants: a field Data Dragon ships as a string (it has happened) must
                // cost that one champion's statics, not abort the whole minutes-long update.
                if (entry.Value.TryGetProperty("stats", out var stats)
                    && stats.TryGetProperty("attackrange", out var range)
                    && range.ValueKind == JsonValueKind.Number
                    && range.TryGetDouble(out var rangeValue))
                {
                    champion.AttackRange = (int)Math.Round(rangeValue);
                }

                if (entry.Value.TryGetProperty("info", out var info)
                    && info.TryGetProperty("defense", out var defense)
                    && defense.ValueKind == JsonValueKind.Number
                    && defense.TryGetInt32(out var defenseValue))
                {
                    champion.Defense = defenseValue;
                }

                matched++;
            }

            if (matched < champions.Count)
                warnings.Add($"Klassendaten für {champions.Count - matched} Champions nicht gefunden.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            warnings.Add("Champion-Klassendaten konnten nicht geladen werden — Comp-Regeln laufen eingeschränkt.");
        }
    }

    private async Task LoadLaneMetaAsync(MetaSnapshot snapshot, ChampionResolver resolver, CancellationToken ct)
    {
        var arguments = new JsonObject
        {
            ["position"] = "all",
            ["desired_output_fields"] = OpGgMcpClient.Fields(LaneMetaFields),
        };

        var response = await _client.CallToolAsync("lol_list_lane_meta_champions", arguments, ct).ConfigureAwait(false);
        RecordFieldDiagnostics(response, LaneMetaFields);

        var positions = response["data"]["positions"];

        var unresolved = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var lane in Lanes.All)
        {
            foreach (var entry in positions[lane.ToOpGg()].Items)
            {
                var name = entry["champion"].AsText();
                if (string.IsNullOrEmpty(name))
                    continue;

                if (resolver.Resolve(name) is not { } championId)
                {
                    unresolved.Add(name);
                    continue;
                }

                snapshot.LaneStats.Add(ReadLaneStat(entry, championId, lane));
            }
        }

        if (unresolved.Count > 0)
            snapshot.Warnings.Add($"Tierlist: {unresolved.Count} Namen nicht zugeordnet ({string.Join(", ", unresolved.Take(5))}).");
    }

    /// <summary>
    /// Recomputes every pick rate from its own game count. OP.GG rounds <c>pick_rate</c> to two
    /// decimals, which puts 85 of 276 rows on exactly 0.01 and 48 on 0.02 — and the ban value reads
    /// <c>pickRate × 8</c>, so one rounding step is 0.08 of a gate that rarely exceeds 0.6.
    /// <para>
    /// The divisor is not given, but it follows from the data: <c>play / pick_rate</c> is the same
    /// sample size for every row, so the median over the rows where the rounding hurts least is a
    /// robust estimate of it. Measured on the stored snapshot the estimates cluster within ±10 % —
    /// exactly the spread two decimals allow — around 1.13 million games.
    /// </para>
    /// <para>
    /// Only the pick rate. The ban rate has no counter to divide (a ban is not a game played), and
    /// the role rate is a share of the champion's own games, of which the tier list shows only the
    /// lanes it happens to list.
    /// </para>
    /// </summary>
    public static void UnroundPickRates(List<LaneStat> rows)
    {
        // Above 5 % the two decimals are worth at most a tenth of the value, which is what makes
        // these rows usable as a ruler for the rest.
        var estimates = rows
            .Where(row => row.Play > 0 && row.PickRate >= 0.05)
            .Select(row => row.Play / row.PickRate)
            .OrderBy(value => value)
            .ToList();

        // Too thin a base and the divisor would be noisier than the rounding it replaces.
        if (estimates.Count < 10)
            return;

        var games = estimates[estimates.Count / 2];

        foreach (var row in rows)
        {
            if (row.Play > 0)
                row.PickRate = row.Play / games;
        }
    }

    /// <summary>
    /// One tier list row. Public and static so it can be tested against a recorded response — the
    /// win rate it produces is the single number every recommendation rests on.
    /// <para>
    /// The rate is computed from the win counter rather than read from <c>win_rate</c>, which OP.GG
    /// rounds to two decimals. On samples of tens of thousands of games that rounding is worth
    /// roughly half a percentage point — ten times more than shrinkage moves the same number, and
    /// enough to make genuinely different champions indistinguishable: Sett (50,43 %) and Darius
    /// (49,83 %) both arrive as 0,50.
    /// </para>
    /// </summary>
    public static LaneStat ReadLaneStat(OpGgNode entry, int championId, Lane lane)
    {
        var play = entry["play"].AsInt();

        return new LaneStat
        {
            ChampionId = championId,
            Lane = lane,
            // HasValue, not just a division: a missing field reads as 0 through AsInt, so a renamed
            // field would quietly turn every champion into a 0 % champion — worse than the rounding
            // this replaces. The fallback keeps the rounded rate, and 0.5 means "unknown" here.
            WinRate = play > 0 && entry["win"].HasValue
                ? (double)entry["win"].AsInt() / play
                : entry["win_rate"].AsNumber(0.5),
            PickRate = entry["pick_rate"].AsNumber(),
            BanRate = entry["ban_rate"].AsNumber(),
            RoleRate = entry["role_rate"].AsNumber(),
            Tier = entry["tier"].AsInt(),
            Play = play,
            FromTierList = true,
        };
    }

    /// <summary>
    /// The lane each champion is most often played in, used as the <c>position</c> argument. The
    /// analysis response reports every lane regardless, so this only steers which lane's
    /// <c>weak_counters</c> come back.
    /// </summary>
    private static Dictionary<int, Lane> PrimaryLanes(MetaSnapshot snapshot)
    {
        var best = new Dictionary<int, (Lane Lane, double Rate)>();

        foreach (var stat in snapshot.LaneStats)
        {
            if (!best.TryGetValue(stat.ChampionId, out var current) || stat.RoleRate > current.Rate)
                best[stat.ChampionId] = (stat.Lane, stat.RoleRate);
        }

        return best.ToDictionary(pair => pair.Key, pair => pair.Value.Lane);
    }

    private async Task LoadAnalysisAsync(
        MetaSnapshot snapshot,
        ChampionResolver resolver,
        Dictionary<int, Lane> primaryLanes,
        SnapshotBuildOptions options,
        IProgress<BuildProgress>? progress,
        CancellationToken ct)
    {
        var champions = snapshot.Champions.ToList();
        var done = 0;
        var failures = new List<string>();

        // Every champion reports the same aggregate, so this is a vote rather than a lookup: one
        // odd answer among 173 should not decide what the footer claims.
        var versions = new Dictionary<string, int>(StringComparer.Ordinal);
        DateTimeOffset? newest = null;

        var gate = new SemaphoreSlim(options.MaxConcurrency);
        var sync = new Lock();

        await Task.WhenAll(champions.Select(async champion =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Off-meta champions have no primary lane; mid is a valid placeholder because the
                // response reports the champion's real positions either way.
                var requested = primaryLanes.TryGetValue(champion.Id, out var lane) ? lane : Lane.Mid;
                var node = await CallAnalysisAsync(champion, requested, options, ct).ConfigureAwait(false);

                lock (sync)
                {
                    if (node is null)
                    {
                        failures.Add(champion.Name);
                    }
                    else
                    {
                        Absorb(snapshot, resolver, champion, requested, node, options.HasRankBracket);
                        RecordFieldDiagnostics(node, AnalysisFields);

                        var (version, asOf) = ReadDataStamp(node);
                        if (version is not null)
                            versions[version] = versions.GetValueOrDefault(version) + 1;

                        if (asOf is { } stamp && (newest is null || stamp > newest))
                            newest = stamp;
                    }

                    progress?.Report(new BuildProgress("Counter", ++done, champions.Count));
                }
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        if (versions.Count > 0)
        {
            snapshot.DataPatch = versions
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .First().Key;

            snapshot.DataAsOfUtc = newest;

            if (versions.Count > 1)
            {
                var listed = string.Join(", ", versions.Keys.OrderBy(key => key, StringComparer.Ordinal));
                snapshot.Warnings.Add($"OP.GG-Zahlen stammen aus mehreren Patches ({listed}) — angezeigt wird der häufigste.");
            }
        }

        if (failures.Count > 0)
            snapshot.Warnings.Add($"Analyse: {failures.Count} Champions ohne Daten ({string.Join(", ", failures.Take(5))}).");
    }

    /// <summary>
    /// The patch and timestamp OP.GG reports for its own aggregate. Both may be absent; the caller
    /// treats that as "this response says nothing" rather than as a zero.
    /// </summary>
    public static (string? Version, DateTimeOffset? AsOf) ReadDataStamp(OpGgNode node)
    {
        var trend = node["data"]["trends"]["win"];

        // A trend is a series by name. The endpoint currently answers with a single value, but if it
        // ever returns the series, the newest entry is the one that describes today's numbers.
        if (trend.Items.Count > 0)
            trend = trend.Items[^1];

        var version = trend["version"].AsText() is { Length: > 0 } text ? text : null;

        var asOf = DateTimeOffset.TryParse(
            trend["created_at"].AsText(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : (DateTimeOffset?)null;

        return (version, asOf);
    }

    /// <summary>
    /// Reads the field names this one response could not match. OP.GG injects the list itself, which
    /// makes it the only warning we get before a renamed field turns into a column of zeroes.
    /// </summary>
    public static void CollectUnmatchedFields(OpGgNode root, SortedSet<string> sink)
    {
        foreach (var field in root["_field_diagnostics"]["unmatched_fields"].Items)
        {
            if (field.AsText() is { Length: > 0 } name)
                sink.Add(name);
        }
    }

    /// <summary>
    /// Books one response against the field list it was asked for, so the build can tell a field
    /// that is genuinely gone from one that is merely absent for this particular champion.
    /// </summary>
    private void RecordFieldDiagnostics(OpGgNode root, IReadOnlyList<string> requested)
    {
        var unmatched = new SortedSet<string>(StringComparer.Ordinal);
        CollectUnmatchedFields(root, unmatched);

        foreach (var field in requested)
        {
            var seen = _fieldReports.GetValueOrDefault(field);
            _fieldReports[field] = (seen.Requested + 1, seen.Unmatched + (unmatched.Contains(field) ? 1 : 0));
        }
    }

    private List<string> MissingFields() => MissingFields(_fieldReports);

    /// <summary>
    /// Fields that no response accepted. A field rejected by SOME responses and not others is simply
    /// absent for those champions — the analysis call omits the synergy branch for the champion's own
    /// lane, so roughly a third of all champions reject one of them. Reporting those would put a
    /// scary line under every single update, and a warning that is always wrong gets ignored.
    /// </summary>
    public static List<string> MissingFields(IReadOnlyDictionary<string, (int Requested, int Unmatched)> reports)
        => [.. reports
            .Where(entry => entry.Value.Requested > 0 && entry.Value.Unmatched == entry.Value.Requested)
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Calls the analysis endpoint, retrying once under the champion's display name. OP.GG accepts
    /// several spellings but not all of them, and a single retry is cheaper than maintaining a
    /// name table that drifts every time a champion is released.
    /// </summary>
    private async Task<OpGgNode?> CallAnalysisAsync(
        ChampionEntry champion,
        Lane requested,
        SnapshotBuildOptions options,
        CancellationToken ct)
    {
        foreach (var name in ChampionResolver.ApiNames(champion))
        {
            var arguments = new JsonObject
            {
                ["game_mode"] = options.GameMode,
                ["champion"] = name,
                ["position"] = requested.ToOpGg(),
                ["tier"] = options.Tier,
                ["desired_output_fields"] = OpGgMcpClient.Fields(AnalysisFields),
            };

            try
            {
                return await _client.CallToolAsync("lol_get_champion_analysis", arguments, ct).ConfigureAwait(false);
            }
            catch (OpGgApiException)
            {
                // Wrong spelling or no data for this champion; try the next name.
            }
            catch (OpGgParseException)
            {
                return null;
            }
        }

        return null;
    }

    private static void Absorb(
        MetaSnapshot snapshot,
        ChampionResolver resolver,
        ChampionEntry champion,
        Lane requested,
        OpGgNode node,
        bool rankSpecific)
    {
        var data = node["data"];

        champion.Damage = ParseDamage(data["damage_type"].AsText());

        // Per-position counters are labelled with their lane, so they can be trusted directly.
        foreach (var position in data["summary"]["positions"].Items)
        {
            var lane = Lanes.FromOpGg(position["name"].AsText()?.ToLowerInvariant());
            if (lane == Lane.Unknown)
                continue;

            AddLaneFallback(snapshot, champion.Id, lane, position["stats"], position["roles"], rankSpecific);

            foreach (var counter in position["counters"].Items)
            {
                var play = counter["play"].AsInt();
                var wins = counter["win"].AsInt();
                if (play <= 0)
                    continue;

                AddMatchup(snapshot, resolver, champion.Id, counter["champion_name"].AsText(), lane, (double)wins / play, play);
            }
        }

        // The top-level counter lists belong to the position we asked for.
        foreach (var listName in new[] { "weak_counters", "strong_counters" })
        {
            foreach (var counter in data[listName].Items)
            {
                var play = counter["play"].AsInt();
                if (play <= 0)
                    continue;

                AddMatchup(snapshot, resolver, champion.Id, counter["champion_name"].AsText(), requested, counter["my_win_rate"].AsNumber(), play);
            }
        }

        foreach (var partnerLane in Lanes.All)
        {
            foreach (var synergy in data["synergies"][partnerLane.ToOpGg()].Items)
            {
                if (resolver.Resolve(synergy["synergy_champion_name"].AsText()) is not { } partnerId)
                    continue;

                var play = synergy["play"].AsInt();
                if (play <= 0)
                    continue;

                snapshot.Synergies.Add(new SynergyStat
                {
                    ChampionId = champion.Id,
                    Lane = requested,
                    PartnerId = partnerId,
                    PartnerLane = partnerLane,
                    WinRate = synergy["win_rate"].AsNumber(),
                    Play = play,
                    Tier = synergy["synergy_tier_data"]["tier"].Exists ? synergy["synergy_tier_data"]["tier"].AsInt() : -1,
                });
            }
        }
    }

    /// <summary>
    /// Records role and win rate for a lane from the analysis response. Two jobs in one, decided by
    /// <paramref name="rankSpecific"/>:
    /// <list type="bullet">
    /// <item>Without a rank bracket it fills the gaps only — lanes the tier list did not list — so
    /// off-meta picks do not fall out of the lane predictor entirely. The tier list keeps every lane
    /// it covers: its sample counts a different population than an <c>all</c>-tier analysis, so the
    /// bigger number is not the better one.</item>
    /// <item>With a bracket configured it REPLACES the tier list's row, because that one always
    /// describes OP.GG's default bracket (emerald_plus). Mixing the two would put two populations
    /// into one score — the lane strength from one, the duels from the other.</item>
    /// </list>
    /// </summary>
    private static void AddLaneFallback(
        MetaSnapshot snapshot, int championId, Lane lane, OpGgNode stats, OpGgNode? roles, bool rankSpecific)
    {
        if (ReadFallbackLaneStat(stats, championId, lane, roles) is not { } fallback)
            return;

        var known = snapshot.LaneStats.FindIndex(stat => stat.ChampionId == championId && stat.Lane == lane);

        if (known < 0)
        {
            snapshot.LaneStats.Add(fallback);
            return;
        }

        if (rankSpecific)
            snapshot.LaneStats[known] = fallback;
    }

    /// <summary>
    /// One lane row built from the analysis call instead of the tier list. Returns
    /// <see langword="null"/> when the response says nothing about this lane.
    /// <para>
    /// Unlike the tier list this endpoint reports no win counter, so these few rows keep the rate
    /// rounded to two decimals. What it does report — and what used to be thrown away here — is the
    /// sample size and the tier: without them shrinkage pinned every one of these rows to exactly
    /// 50 %, which is not what "off-meta lane" means.
    /// </para>
    /// </summary>
    /// <param name="roles">
    /// The position's role split, when the response carried one. It is the only place this endpoint
    /// reports wins as a counter rather than a rounded rate, so it recovers the precision the
    /// rounding costs — and that matters as soon as these rows are the authoritative ones.
    /// </param>
    public static LaneStat? ReadFallbackLaneStat(OpGgNode stats, int championId, Lane lane, OpGgNode? roles = null)
    {
        var roleRate = stats["role_rate"].AsNumber(-1);
        if (roleRate < 0)
            return null;

        return new LaneStat
        {
            ChampionId = championId,
            Lane = lane,
            WinRate = ExactWinRate(roles) ?? stats["win_rate"].AsNumber(0.5),
            PickRate = stats["pick_rate"].AsNumber(),
            BanRate = stats["ban_rate"].AsNumber(),
            RoleRate = roleRate,
            // 0 is LaneStat's documented "unrated"; TierNudge skips it. Not -1, that is SynergyStat.
            Tier = stats["tier_data"]["tier"].AsInt(),
            Play = stats["play"].AsInt(),
        };
    }

    /// <summary>
    /// The win rate summed over a position's role split, or <see langword="null"/> when there is no
    /// split to sum. Deliberately strict: a partial split would describe a different set of games
    /// than the position it is supposed to measure, and the rounded rate is the better answer then.
    /// </summary>
    private static double? ExactWinRate(OpGgNode? roles)
    {
        if (roles is null)
            return null;

        var play = 0;
        var wins = 0;

        foreach (var role in roles.Items)
        {
            var stats = role["stats"];
            if (!stats["play"].HasValue || !stats["win"].HasValue)
                return null;

            play += stats["play"].AsInt();
            wins += stats["win"].AsInt();
        }

        return play > 0 ? (double)wins / play : null;
    }

    private static void AddMatchup(
        MetaSnapshot snapshot,
        ChampionResolver resolver,
        int championId,
        string? opponentName,
        Lane lane,
        double winRate,
        int play)
    {
        if (resolver.Resolve(opponentName) is not { } opponentId || opponentId == championId)
            return;

        snapshot.Matchups.Add(new MatchupStat
        {
            ChampionId = championId,
            OpponentId = opponentId,
            Lane = lane,
            WinRate = winRate,
            Play = play,
        });
    }

    /// <summary>
    /// Pulls the longer duo lists for bot lane. The embedded synergies in the analysis response are
    /// capped at three entries; the dedicated endpoint returns ten, and bot lane is where the duo
    /// actually decides games.
    /// </summary>
    private async Task LoadDuoSynergiesAsync(
        MetaSnapshot snapshot,
        ChampionResolver resolver,
        Dictionary<int, Lane> primaryLanes,
        SnapshotBuildOptions options,
        IProgress<BuildProgress>? progress,
        CancellationToken ct)
    {
        // Bot-lane duos, plus the jungler's pairings with every solo lane: ganks, dives and
        // objective setups are where champion pairs actually win or lose together outside bot.
        var pairs = new List<(ChampionEntry Champion, Lane Lane, Lane PartnerLane)>();

        foreach (var champion in snapshot.Champions)
        {
            if (!primaryLanes.TryGetValue(champion.Id, out var lane))
                continue;

            switch (lane)
            {
                case Lane.Adc:
                    pairs.Add((champion, lane, Lane.Support));
                    break;
                case Lane.Support:
                    pairs.Add((champion, lane, Lane.Adc));
                    break;
                case Lane.Jungle:
                    pairs.Add((champion, lane, Lane.Mid));
                    pairs.Add((champion, lane, Lane.Top));
                    pairs.Add((champion, lane, Lane.Support));
                    break;
            }
        }

        var done = 0;
        var gate = new SemaphoreSlim(options.MaxConcurrency);
        var sync = new Lock();

        await Task.WhenAll(pairs.Select(async pair =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var node = await CallDuoSynergiesAsync(pair.Champion, pair.Lane, pair.PartnerLane, ct).ConfigureAwait(false);

                lock (sync)
                {
                    if (node is not null)
                    {
                        AbsorbDuos(snapshot, resolver, pair.Champion.Id, pair.Lane, pair.PartnerLane, node);
                        RecordFieldDiagnostics(node, DuoSynergyFields);
                    }

                    progress?.Report(new BuildProgress("Duos", ++done, pairs.Count));
                }
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);
    }

    private async Task<OpGgNode?> CallDuoSynergiesAsync(ChampionEntry champion, Lane own, Lane partner, CancellationToken ct)
    {
        foreach (var name in ChampionResolver.ApiNames(champion))
        {
            var arguments = new JsonObject
            {
                ["champion"] = name,
                ["my_position"] = own.ToOpGg(),
                ["synergy_position"] = partner.ToOpGg(),
                ["desired_output_fields"] = OpGgMcpClient.Fields(DuoSynergyFields),
            };

            try
            {
                return await _client.CallToolAsync("lol_get_champion_synergies", arguments, ct).ConfigureAwait(false);
            }
            catch (OpGgApiException)
            {
                // Try the next spelling.
            }
            catch (OpGgParseException)
            {
                return null;
            }
        }

        return null;
    }

    private static void AbsorbDuos(
        MetaSnapshot snapshot,
        ChampionResolver resolver,
        int championId,
        Lane own,
        Lane partnerLane,
        OpGgNode node)
    {
        foreach (var synergy in node["data"]["synergies"].Items)
        {
            if (resolver.Resolve(synergy["synergy_champion_name"].AsText()) is not { } partnerId)
                continue;

            var play = synergy["play"].AsInt();
            if (play <= 0)
                continue;

            snapshot.Synergies.Add(new SynergyStat
            {
                ChampionId = championId,
                Lane = own,
                PartnerId = partnerId,
                PartnerLane = partnerLane,
                WinRate = synergy["win_rate"].AsNumber(),
                Play = play,
                Tier = synergy["synergy_tier_data"]["tier"].Exists ? synergy["synergy_tier_data"]["tier"].AsInt() : -1,
            });
        }
    }

    /// <summary>
    /// Collapses the overlap between the three counter sources and the two synergy sources, keeping
    /// the largest sample for each pair.
    /// </summary>
    private static void Deduplicate(MetaSnapshot snapshot)
    {
        snapshot.Matchups = [.. snapshot.Matchups
            .GroupBy(stat => (stat.ChampionId, stat.OpponentId, stat.Lane))
            .Select(group => group.MaxBy(stat => stat.Play)!)
            .OrderBy(stat => stat.ChampionId)];

        snapshot.Synergies = [.. snapshot.Synergies
            .GroupBy(stat => (stat.ChampionId, stat.PartnerId))
            .Select(group => group.MaxBy(stat => stat.Play)!)
            .OrderBy(stat => stat.ChampionId)];

        // Tier list first, sample size only as a tie-break: an analysis row carries the all-tier
        // aggregate and can outnumber a tier list row for the same lane by thirty to one without
        // being the better answer. AddLaneFallback already prevents the collision — and with a rank
        // bracket configured it resolves it the other way, by replacing the row outright; this keeps
        // the rule where it is visible instead of resting on a check 200 lines away.
        snapshot.LaneStats = [.. snapshot.LaneStats
            .GroupBy(stat => (stat.ChampionId, stat.Lane))
            .Select(group => group.OrderByDescending(stat => stat.FromTierList).ThenByDescending(stat => stat.Play).First())
            .OrderBy(stat => stat.Lane).ThenByDescending(stat => stat.RoleRate)];

        // Per source, not over the whole list: the divisor is a sample size, and a tier list row and
        // an analysis row count different populations. Mixing them would de-round both against an
        // average of two numbers that belong to neither.
        UnroundPickRates([.. snapshot.LaneStats.Where(stat => stat.FromTierList)]);
        UnroundPickRates([.. snapshot.LaneStats.Where(stat => !stat.FromTierList)]);
    }

    /// <summary>
    /// What an average listed duo is worth, in log-odds of the shrunk rate. OP.GG lists the duos
    /// worth mentioning, not all of them, so this is not zero — measured on the file this was
    /// written for: 0.0552, i.e. 51,4 %. The recommender subtracts it, so a duo counts for being
    /// better than the usual one rather than for existing.
    /// <para>
    /// Measured here, once per file, because it is a property of the fetched data: a different
    /// bracket or a different patch may well list a different selection.
    /// </para>
    /// </summary>
    public static double MeasureSynergyBaseline(IReadOnlyList<SynergyStat> synergies)
        => MeanLogit(synergies
            .Where(synergy => synergy.Play >= Shrinkage.SynergyPrior)
            .Select(synergy => synergy.WinRate));

    /// <summary>
    /// What an average lane row is worth, in log-odds. Measured at 0.0086 (50,21 %) on the file
    /// this was written for — near enough to even that it barely moves a row with tens of thousands
    /// of games, and the right target for the rare row that has a few hundred.
    /// </summary>
    public static double MeasureLaneBaseline(IReadOnlyList<LaneStat> lanes)
        => MeanLogit(lanes
            .Where(stat => stat.Play >= Shrinkage.LanePrior && stat.Lane != Lane.Unknown)
            .Select(stat => stat.WinRate));

    /// <summary>
    /// How far a listed matchup sits from what the two champions' lane win rates already imply, in
    /// log-odds. OP.GG lists the opponents that stand out against a champion, so the stored edges
    /// are a selection, not a sample: measured at -0.0295 on the file this was written for, i.e.
    /// about 0,7 points worse for the listed champion than the pair alone would suggest.
    /// <para>
    /// Rows without both lane win rates are skipped rather than counted against 50 % — that would
    /// be measuring the offset against a reference this number is supposed to replace.
    /// </para>
    /// </summary>
    public static double MeasureMatchupBaseline(IReadOnlyList<MatchupStat> matchups, IReadOnlyList<LaneStat> lanes)
    {
        var laneRates = new Dictionary<(int Champion, Lane Lane), double>(lanes.Count);

        foreach (var stat in lanes)
        {
            if (stat.Play > 0 && stat.Lane != Lane.Unknown && stat.WinRate is > 0 and < 1)
                laneRates[(stat.ChampionId, stat.Lane)] = stat.WinRate;
        }

        return MeanLogit(matchups
            .Where(stat => stat.Play >= Shrinkage.MatchupPrior && stat.Lane != Lane.Unknown)
            .Where(stat => laneRates.ContainsKey((stat.ChampionId, stat.Lane))
                && laneRates.ContainsKey((stat.OpponentId, stat.Lane)))
            .Select(stat => ScoreModel.Sigmoid(
                ScoreModel.Logit(stat.WinRate)
                - ScoreModel.Logit(laneRates[(stat.ChampionId, stat.Lane)])
                + ScoreModel.Logit(laneRates[(stat.OpponentId, stat.Lane)]))));
    }

    /// <summary>
    /// The mean of the log-odds of rates that carry enough games to speak for themselves.
    /// <para>
    /// Deliberately raw: the earlier version of this shrank every row towards 50 % first and then
    /// averaged, which measured the pull it was supposed to replace. On the stored file that cost
    /// 0,018 log-odds of the synergy baseline — 0,45 points on every duo, in the direction of
    /// "duos matter less than they do".
    /// </para>
    /// </summary>
    private static double MeanLogit(IEnumerable<double> rates)
    {
        var total = 0.0;
        var counted = 0;

        foreach (var rate in rates)
        {
            if (rate is not (> 0 and < 1))
                continue;

            total += ScoreModel.Logit(rate);
            counted++;
        }

        // Too few rows to average would put a guess where a measurement belongs; zero then leaves
        // the term exactly as it was before any of this.
        return counted >= 50 ? total / counted : 0;
    }

    /// <summary>
    /// States plainly how thin the data is. Silent gaps would read as "we checked and it is fine".
    /// </summary>
    private static void AddCoverageWarnings(MetaSnapshot snapshot, IReadOnlyList<string> missingFields)
    {
        var withMatchups = snapshot.Matchups.Select(stat => stat.ChampionId).Distinct().Count();
        var total = snapshot.Champions.Count;

        snapshot.Warnings.Add(
            $"Matchup-Daten: {snapshot.Matchups.Count} Kanten für {withMatchups} von {total} Champions. " +
            "OP.GG liefert pro Champion nur die auffälligsten Gegner, keine vollständige Matrix.");

        var thin = snapshot.Matchups.Count(stat => stat.Play < 50);
        if (thin > 0)
        {
            snapshot.Warnings.Add($"{thin} Matchups mit unter 50 Spielen — sie zählen fast nur noch als das, "
                + "was die beiden Lane-Siegquoten ohnehin sagen.");
        }

        // Which population the numbers describe, per source — the one thing about this file that
        // cannot be seen by looking at the numbers themselves.
        var bracket = snapshot.Tier ?? string.Empty;
        var rankFiltered = bracket.Length > 0 && !bracket.Equals("all", StringComparison.OrdinalIgnoreCase);

        snapshot.Warnings.Add(rankFiltered
            ? $"Synergien: {snapshot.Synergies.Count} Paare. Lane-Zahlen, Duelle und Tier stammen aus dem "
                + $"Bracket '{snapshot.Tier}'; Synergien kennen keinen Rangfilter und stammen aus OP.GGs "
                + "Standard-Bracket (Emerald und höher)."
            : $"Synergien: {snapshot.Synergies.Count} Paare. Tierlist stammt aus OP.GGs Standard-Bracket "
                + $"(Emerald und höher), Matchups aus dem Bracket '{snapshot.Tier}'.");

        // The one failure neither the parser nor OP.GG's own diagnostics report: if a class stops
        // being declared in the header, every field falls back to a positional name, every lookup
        // misses and the numbers arrive as zero. A count is the cheapest way to notice.
        var withoutPlay = snapshot.LaneStats.Count(stat => stat.Play <= 0);
        if (withoutPlay * 10 >= snapshot.LaneStats.Count && withoutPlay > 0)
        {
            snapshot.Warnings.Add(
                $"Tierlist: {withoutPlay} von {snapshot.LaneStats.Count} Lane-Zeilen ohne Spielzahl — " +
                "vermutlich haben sich die OP.GG-Feldnamen geändert.");
        }

        if (missingFields.Count > 0)
        {
            var listed = string.Join(", ", missingFields.Take(5));
            snapshot.Warnings.Add(
                $"OP.GG liefert {missingFields.Count} angefragte Felder in keiner einzigen Antwort mehr: " +
                $"{listed}. Diese Zahlen fehlen im Snapshot.");
        }
    }

    private static DamageType ParseDamage(string? value) => value?.ToUpperInvariant() switch
    {
        "AD" => DamageType.Physical,
        "AP" => DamageType.Magic,
        "BOTH" => DamageType.Mixed,
        _ => DamageType.Unknown,
    };

    private static string[] BuildLaneMetaFields()
    {
        // "win" is the exact counter behind the rounded win_rate; see ReadLaneStat for why it matters.
        string[] metrics = ["champion", "play", "win", "win_rate", "pick_rate", "role_rate", "ban_rate", "tier"];
        var fields = new List<string>(Lanes.Count * metrics.Length);

        foreach (var lane in Lanes.All)
        {
            foreach (var metric in metrics)
                fields.Add($"data.positions.{lane.ToOpGg()}[].{metric}");
        }

        return [.. fields];
    }

    public void Dispose()
    {
        if (_ownsStaticData)
            _staticData.Dispose();
    }
}

/// <summary>
/// Maps the champion names OP.GG returns onto Riot ids. Names come back in display form
/// (<c>Kai'Sa</c>, <c>Nunu &amp; Willump</c>) while requests want a bare uppercase form, so
/// everything is matched on a normalised key.
/// </summary>
public sealed class ChampionResolver
{
    private readonly Dictionary<string, int> _byNormalisedName = new(StringComparer.Ordinal);

    public ChampionResolver(IEnumerable<ChampionEntry> champions)
    {
        foreach (var champion in champions)
        {
            _byNormalisedName.TryAdd(Normalise(champion.Name), champion.Id);
            _byNormalisedName.TryAdd(Normalise(champion.Key), champion.Id);
        }
    }

    public int? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return _byNormalisedName.TryGetValue(Normalise(name), out var id) ? id : null;
    }

    /// <summary>
    /// Spellings to try for the <c>champion</c> argument, most likely first. OP.GG normalises
    /// away punctuation, so the stripped key usually works; the display name is the fallback.
    /// </summary>
    public static IEnumerable<string> ApiNames(ChampionEntry champion)
    {
        var fromKey = Normalise(champion.Key);
        yield return fromKey;

        var fromName = Normalise(champion.Name);
        if (fromName != fromKey)
            yield return fromName;
    }

    private static string Normalise(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}
