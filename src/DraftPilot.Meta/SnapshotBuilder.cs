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
    /// Rank filter for the analysis calls. Effectively fixed at <c>all</c>: without it OP.GG
    /// returns no matchup data whatsoever, which was verified against the live endpoint.
    /// </summary>
    public string CounterTier { get; init; } = "all";

    /// <summary>Pull the richer duo lists for bot lane, which is where duo choice actually matters.</summary>
    public bool IncludeDuoSynergies { get; init; } = true;

    /// <summary>Fetch champion portraits that are not cached yet.</summary>
    public bool DownloadIcons { get; init; } = true;

    /// <summary>Concurrent requests. Deliberately low; this is somebody else's free endpoint.</summary>
    public int MaxConcurrency { get; init; } = 3;
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
        "data.summary.positions[].name",
        "data.summary.positions[].stats.win_rate",
        "data.summary.positions[].stats.role_rate",
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

    private static readonly string[] LaneMetaFields = BuildLaneMetaFields();

    private readonly OpGgMcpClient _client;
    private readonly HttpClient _staticData;
    private readonly bool _ownsStaticData;

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
            Tier = options.CounterTier,
            BuiltAtUtc = DateTimeOffset.UtcNow,
        };

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
        }

        Deduplicate(snapshot);
        AddCoverageWarnings(snapshot);

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

            if (document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0)
                return document.RootElement[0].GetString() ?? string.Empty;
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

                if (entry.Value.TryGetProperty("stats", out var stats)
                    && stats.TryGetProperty("attackrange", out var range))
                {
                    champion.AttackRange = (int)Math.Round(range.GetDouble());
                }

                if (entry.Value.TryGetProperty("info", out var info)
                    && info.TryGetProperty("defense", out var defense))
                {
                    champion.Defense = defense.GetInt32();
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

        var positions = (await _client.CallToolAsync("lol_list_lane_meta_champions", arguments, ct).ConfigureAwait(false))
            ["data"]["positions"];

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

                snapshot.LaneStats.Add(new LaneStat
                {
                    ChampionId = championId,
                    Lane = lane,
                    WinRate = entry["win_rate"].AsNumber(),
                    PickRate = entry["pick_rate"].AsNumber(),
                    BanRate = entry["ban_rate"].AsNumber(),
                    RoleRate = entry["role_rate"].AsNumber(),
                    Tier = entry["tier"].AsInt(),
                    Play = entry["play"].AsInt(),
                });
            }
        }

        if (unresolved.Count > 0)
            snapshot.Warnings.Add($"Tierlist: {unresolved.Count} Namen nicht zugeordnet ({string.Join(", ", unresolved.Take(5))}).");
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
                        failures.Add(champion.Name);
                    else
                        Absorb(snapshot, resolver, champion, requested, node);

                    progress?.Report(new BuildProgress("Counter", ++done, champions.Count));
                }
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        if (failures.Count > 0)
            snapshot.Warnings.Add($"Analyse: {failures.Count} Champions ohne Daten ({string.Join(", ", failures.Take(5))}).");
    }

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
                ["tier"] = options.CounterTier,
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
        OpGgNode node)
    {
        var data = node["data"];

        champion.Damage = ParseDamage(data["damage_type"].AsText());

        // Per-position counters are labelled with their lane, so they can be trusted directly.
        foreach (var position in data["summary"]["positions"].Items)
        {
            var lane = Lanes.FromOpGg(position["name"].AsText()?.ToLowerInvariant());
            if (lane == Lane.Unknown)
                continue;

            AddLaneFallback(snapshot, champion.Id, lane, position["stats"]);

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
    /// Records role and win rate for a lane the tier list did not list. Keeps off-meta picks from
    /// falling out of the lane predictor entirely.
    /// </summary>
    private static void AddLaneFallback(MetaSnapshot snapshot, int championId, Lane lane, OpGgNode stats)
    {
        var roleRate = stats["role_rate"].AsNumber(-1);
        if (roleRate < 0)
            return;

        var alreadyKnown = snapshot.LaneStats.Any(stat => stat.ChampionId == championId && stat.Lane == lane);
        if (alreadyKnown)
            return;

        snapshot.LaneStats.Add(new LaneStat
        {
            ChampionId = championId,
            Lane = lane,
            WinRate = stats["win_rate"].AsNumber(0.5),
            RoleRate = roleRate,
            // No sample size is reported here, so leave it at zero: shrinkage then treats the win
            // rate as unknown rather than as fact.
            Play = 0,
        });
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
                        AbsorbDuos(snapshot, resolver, pair.Champion.Id, pair.Lane, pair.PartnerLane, node);

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
                ["desired_output_fields"] = OpGgMcpClient.Fields(
                    "data.synergies[].synergy_champion_name",
                    "data.synergies[].play",
                    "data.synergies[].win_rate",
                    "data.synergies[].synergy_tier_data.tier"),
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

        snapshot.LaneStats = [.. snapshot.LaneStats
            .GroupBy(stat => (stat.ChampionId, stat.Lane))
            .Select(group => group.MaxBy(stat => stat.Play)!)
            .OrderBy(stat => stat.Lane).ThenByDescending(stat => stat.RoleRate)];
    }

    /// <summary>
    /// States plainly how thin the data is. Silent gaps would read as "we checked and it is fine".
    /// </summary>
    private static void AddCoverageWarnings(MetaSnapshot snapshot)
    {
        var withMatchups = snapshot.Matchups.Select(stat => stat.ChampionId).Distinct().Count();
        var total = snapshot.Champions.Count;

        snapshot.Warnings.Add(
            $"Matchup-Daten: {snapshot.Matchups.Count} Kanten für {withMatchups} von {total} Champions. " +
            "OP.GG liefert pro Champion nur die auffälligsten Gegner, keine vollständige Matrix.");

        var thin = snapshot.Matchups.Count(stat => stat.Play < 50);
        if (thin > 0)
            snapshot.Warnings.Add($"{thin} Matchups mit unter 50 Spielen — werden stark zur Mitte gezogen.");

        snapshot.Warnings.Add(
            $"Synergien: {snapshot.Synergies.Count} Paare. Tierlist stammt aus OP.GGs Standard-Bracket, " +
            $"Matchups aus dem Bracket '{snapshot.Tier}'.");
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
        string[] metrics = ["champion", "play", "win_rate", "pick_rate", "role_rate", "ban_rate", "tier"];
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
