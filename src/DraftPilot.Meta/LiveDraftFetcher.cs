using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta.OpGg;

namespace DraftPilot.Meta;

/// <summary>
/// Parses the matchup guide's JSON into a <see cref="BuildPlan"/>.
/// <para>
/// Unlike the other OP.GG tools, the guide answers in plain JSON rather than the typed-tuple
/// format, so this bypasses <see cref="OpGgResponseParser"/> entirely.
/// </para>
/// </summary>
public static class MatchupGuideParser
{
    public static BuildPlan Parse(
        string json,
        int championId,
        string championName,
        int opponentId,
        string opponentName,
        Lane lane,
        string patch)
    {
        using var document = JsonDocument.Parse(json);

        var plan = new BuildPlan
        {
            SchemaVersion = BuildPlan.CurrentSchemaVersion,
            ChampionId = championId,
            ChampionName = championName,
            OpponentId = opponentId,
            OpponentName = opponentName,
            Lane = lane,
            Patch = patch,
            FetchedAtUtc = DateTimeOffset.UtcNow,
        };

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return plan;

        plan.Runes = BestRunePage(data);
        plan.Starters = ItemSets(data, "starter_items", take: 2);
        plan.Boots = ItemSets(data, "boots", take: 4);
        plan.CoreItems = ItemSets(data, "core_items", take: 3);
        plan.LateItems = ItemSets(data, "last_items", take: 3);
        plan.SummonerSpells = ItemSets(data, "summoner_spells", take: 2);
        plan.SkillPriority = SkillPriority(data);
        plan.SkillOrder = SkillOrder(data);

        return plan;
    }

    /// <summary>
    /// The most played rune page, with the most played concrete build inside it. The page-level
    /// sample is what the win rate is quoted from — the concrete variants often have single-digit
    /// game counts that would dress noise up as precision.
    /// </summary>
    private static RunePage? BestRunePage(JsonElement data)
    {
        if (!data.TryGetProperty("rune_pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
            return null;

        JsonElement? bestPage = null;
        var bestPlay = 0;

        foreach (var page in pages.EnumerateArray())
        {
            var play = IntOf(page, "play");
            if (play > bestPlay)
            {
                bestPlay = play;
                bestPage = page;
            }
        }

        if (bestPage is not { } chosen)
            return null;

        JsonElement? bestBuild = null;
        var bestBuildPlay = 0;

        if (chosen.TryGetProperty("builds", out var builds) && builds.ValueKind == JsonValueKind.Array)
        {
            foreach (var build in builds.EnumerateArray())
            {
                var play = IntOf(build, "play");
                if (play > bestBuildPlay)
                {
                    bestBuildPlay = play;
                    bestBuild = build;
                }
            }
        }

        // No usable build inside the page? Fall back to the page element itself — some responses
        // carry the rune ids at page level, and "no build breakdown" must not read as "no runes",
        // which silently disabled the import button. But only when the page actually HAS ids:
        // an empty-but-not-null page would make the plan look valid, get cached, and never be
        // fetched again.
        var runes = bestBuild ?? chosen;
        if (bestBuild is null && Ints(chosen, "primary_rune_ids").Count() < 4)
            return null;

        var wins = IntOf(chosen, "win");

        return new RunePage
        {
            PrimaryPath = TextOf(runes, "primary_page_name"),
            PrimaryPathId = IntOf(runes, "primary_page_id"),
            PrimaryRunes = Names(runes, "primary_rune_names"),
            PrimaryRuneIds = [.. Ints(runes, "primary_rune_ids")],
            SecondaryPath = TextOf(runes, "secondary_page_name"),
            SecondaryPathId = IntOf(runes, "secondary_page_id"),
            SecondaryRunes = Names(runes, "secondary_rune_names"),
            SecondaryRuneIds = [.. Ints(runes, "secondary_rune_ids")],
            Shards = [.. Ints(runes, "stat_mod_ids").Select(StatShards.NameOf)],
            ShardIds = [.. Ints(runes, "stat_mod_ids")],
            Play = bestPlay,
            WinRate = bestPlay > 0 ? (double)wins / bestPlay : 0,
        };
    }

    private static List<ItemSet> ItemSets(JsonElement data, string property, int take)
    {
        var result = new List<ItemSet>();

        if (!data.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var entry in array.EnumerateArray())
        {
            var play = IntOf(entry, "play");
            if (play <= 0)
                continue;

            result.Add(new ItemSet
            {
                Items = Names(entry, "ids_names"),
                ItemIds = [.. Ints(entry, "ids")],
                Play = play,
                WinRate = (double)IntOf(entry, "win") / play,
                PickRate = NumberOf(entry, "pick_rate"),
            });
        }

        // Plausibility net: whether the endpoint quotes rates as 0..1 or 0..100 is not documented
        // anywhere. Decided per LIST, not per entry — a per-entry division would rescale 47 → 0.47
        // but leave a sibling 0.8 (0.8 %) as 80 %, mixing scales within the same list.
        if (result.Count > 0 && result.Max(set => set.PickRate) > 1)
        {
            foreach (var set in result)
                set.PickRate /= 100;
        }

        // Most played, deliberately, and NOT the best win rate — the rule BuildChoice applies to the
        // opponent-free build does not belong here. This endpoint's samples are an order of
        // magnitude too thin for it: in the captured Darius-vs-Jax guide the starters run 113 games
        // at 48,7 % against 13 games at 61,5 %, and every win-rate rule that is willing to promote
        // the second one is also willing to promote noise. Ranking the boots that way is how the
        // situational boot choice came back through the side door, and that one was taken out on
        // purpose. Once the opponent is known, "what most people buy into him" is the answer.
        return [.. result.OrderByDescending(set => set.Play).Take(take)];
    }

    /// <summary>The dominant skill priority, e.g. <c>Q &gt; E &gt; W</c>.</summary>
    private static string SkillPriority(JsonElement data)
    {
        if (!data.TryGetProperty("skill_masteries", out var masteries) || masteries.ValueKind != JsonValueKind.Array)
            return string.Empty;

        JsonElement? best = null;
        var bestPlay = 0;

        foreach (var entry in masteries.EnumerateArray())
        {
            var play = IntOf(entry, "play");
            if (play > bestPlay)
            {
                bestPlay = play;
                best = entry;
            }
        }

        return best is { } chosen ? string.Join(" > ", Names(chosen, "ids")) : string.Empty;
    }

    /// <summary>
    /// The most played level-by-level skill order, 15 entries. Taken from <c>skills</c> rather
    /// than the nested masteries builds because its top entry is the same list with the larger
    /// aggregation.
    /// </summary>
    private static List<string> SkillOrder(JsonElement data)
    {
        if (!data.TryGetProperty("skills", out var skills) || skills.ValueKind != JsonValueKind.Array)
            return [];

        JsonElement? best = null;
        var bestPlay = 0;

        foreach (var entry in skills.EnumerateArray())
        {
            var play = IntOf(entry, "play");
            if (play > bestPlay)
            {
                bestPlay = play;
                best = entry;
            }
        }

        return best is { } chosen ? Names(chosen, "order") : [];
    }

    private static List<string> Names(JsonElement element, string property)
    {
        var names = new List<string>();

        if (element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    names.Add(item.GetString() ?? string.Empty);
                else if (item.ValueKind == JsonValueKind.Number)
                    names.Add(item.GetInt32().ToString());
            }
        }

        return names;
    }

    private static IEnumerable<int> Ints(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in array.EnumerateArray())
        {
            if (AsInt(item) is { } value)
                yield return value;
        }
    }

    private static int IntOf(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) ? AsInt(value) ?? 0 : 0;

    private static double NumberOf(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return 0;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0,
        };
    }

    /// <summary>
    /// Numbers, also when quoted — the LCU models tolerate <c>"play":"1234"</c> via
    /// AllowReadingFromString, and this parser has to match. Rejecting the quoted form turned
    /// every play count to 0, which discarded all item sets and produced an empty build that was
    /// still marked "fetched". Invariant culture: never the German comma.
    /// </summary>
    private static int? AsInt(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out var number) => number,
        JsonValueKind.String when int.TryParse(
            value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    private static string TextOf(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
}

/// <summary>
/// On-demand fetches for the draft on screen: the matchup guide for the locked pick, and fresh
/// counter lists for the enemies actually being faced. Runs automatically as picks are revealed —
/// at most one call per enemy champion and one per matchup for the whole draft, cached on disk.
/// </summary>
/// <param name="gameMode">
/// OP.GG's name for the queue being played, from <see cref="QueueKinds.OpGgMode"/> — Solo/Duo, Flex
/// and ARAM are three separate populations behind this one parameter. It follows the queue the
/// client reports, not a setting: a setting can say Flex while the game on screen is Solo/Duo.
/// </param>
/// <param name="tier">
/// The rank bracket the stored snapshot was built for. The live counters have to come from the same
/// one — a duel from a different population than the lane rate it is compared against is not a
/// comparison at all.
/// </param>
public sealed class LiveDraftFetcher(
    OpGgMcpClient client, string gameMode, string tier = "all", Action<string>? diagnostic = null)
{
    /// <summary>
    /// The champion's own build, with no opponent in it: the one OP.GG has the most games behind.
    /// <para>
    /// Used wherever the lane opponent is not known — ARAM, where there is none, and every Rift
    /// draft up to the moment the opposing pick is revealed. It is the better answer than a build
    /// against a guessed opponent, and by a wide margin: measured on 2026-09-18, Darius Top's boots
    /// here rest on 49.535 games, while the most played core of the Darius-vs-Jax matchup guide
    /// rests on 11.
    /// </para>
    /// </summary>
    /// <param name="lane">
    /// Where the champion is being played. Passed through as the position, because a Top build and
    /// a Support build of the same champion are not the same build. <see cref="Lane.Unknown"/> is
    /// the ARAM case, where the parameter is required by the schema and ignored by the data —
    /// measured: mid, adc and top answer with identical numbers.
    /// </param>
    public async Task<BuildPlan?> FetchChampionBuildAsync(
        ChampionEntry me,
        Lane lane,
        string mode,
        string patch,
        CancellationToken ct)
    {
        foreach (var name in ChampionResolver.ApiNames(me))
        {
            var arguments = new JsonObject
            {
                ["game_mode"] = mode,
                ["champion"] = name,
                ["position"] = lane == Lane.Unknown ? "mid" : lane.ToOpGg(),
                // The same bracket as the counters and the stored lane numbers. Without it this one
                // call would describe emerald_plus while everything beside it describes the
                // player's own rank.
                ["tier"] = tier,
                ["desired_output_fields"] = OpGgMcpClient.Fields(AnalysisBuildParser.Fields),
            };

            try
            {
                var response = await client.CallToolAsync("lol_get_champion_analysis", arguments, ct)
                    .ConfigureAwait(false);

                // The mode is stamped into the plan only where it IS the plan's subject: on the
                // Abyss, where there is no lane and no opponent to name it by. A Rift plan keeps an
                // empty Mode — that is what tells everything downstream it is a lane build, and
                // "ranked" in that field would make the card head itself with a queue name.
                var plan = AnalysisBuildParser.Parse(
                    response,
                    me,
                    lane,
                    lane == Lane.Unknown ? mode : string.Empty,
                    patch,
                    BuildCache.VariantFor(mode, tier));

                return plan.IsEmpty ? null : plan;
            }
            catch (OpGgApiException ex) when (ex.Status is null)
            {
                // A rejected spelling; try the next one. A transport failure is not ours to swallow.
                diagnostic?.Invoke($"{me.Name} ({mode}): {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>Build, runes and spells for one concrete matchup, or null if OP.GG has nothing.</summary>
    public async Task<BuildPlan?> FetchBuildAsync(
        ChampionEntry me,
        ChampionEntry opponent,
        Lane lane,
        string patch,
        CancellationToken ct)
    {
        foreach (var myName in ChampionResolver.ApiNames(me))
        {
            foreach (var opponentName in ChampionResolver.ApiNames(opponent))
            {
                var arguments = new JsonObject
                {
                    ["my_champion"] = myName,
                    ["opponent_champion"] = opponentName,
                    ["position"] = lane.ToOpGg(),
                };

                try
                {
                    var text = await client.CallToolRawAsync("lol_get_lane_matchup_guide", arguments, ct)
                        .ConfigureAwait(false);

                    return MatchupGuideParser.Parse(text, me.Id, me.Name, opponent.Id, opponent.Name, lane, patch);
                }
                catch (OpGgApiException ex) when (ex.Status is null)
                {
                    // Wrong spelling or no data for this pairing; try the next combination. An HTTP
                    // status means the endpoint itself is unwell — that one goes up, or an outage
                    // looks exactly like "this champion has no build".
                }
                catch (JsonException ex)
                {
                    // The endpoint answered in a shape we do not understand. Silently returning
                    // null made "kein Build" indistinguishable from a broken parser.
                    diagnostic?.Invoke($"Guide-Antwort unlesbar für {myName} vs {opponentName}: {ex.Message}");
                    return null;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Fresh counter data for one enemy: who beats them, per lane, with current samples. The
    /// stocked snapshot carries the same shape of data but only ~3 entries per champion; asking
    /// again for the enemies actually on the board is what makes the counter advice dense.
    /// </summary>
    /// <summary>
    /// The lane a counter call is actually made for. Public because the caller has to remember what
    /// it already fetched, and remembering the predicted lane instead of the requested one means the
    /// two disagree exactly when the prediction was unclear.
    /// </summary>
    public static Lane RequestedLane(Lane likelyLane) => likelyLane == Lane.Unknown ? Lane.Mid : likelyLane;

    public async Task<IReadOnlyList<MatchupStat>> FetchEnemyCountersAsync(
        ChampionEntry enemy,
        Lane likelyLane,
        ChampionResolver resolver,
        CancellationToken ct)
    {
        var requested = RequestedLane(likelyLane);

        foreach (var name in ChampionResolver.ApiNames(enemy))
        {
            var arguments = new JsonObject
            {
                ["game_mode"] = gameMode,
                ["champion"] = name,
                ["position"] = requested.ToOpGg(),
                // The same bracket the snapshot was built for. The claim that used to stand here —
                // rank-filtered counters come back almost always empty — was measured on 2026-09-18
                // and does not hold: tier=gold answers for Darius Top with counters over 241 to 343
                // games each (docs/opgg-schnittstelle.md).
                ["tier"] = tier,
                ["desired_output_fields"] = OpGgMcpClient.Fields(
                    "data.summary.positions[].name",
                    "data.summary.positions[].counters[].champion_name",
                    "data.summary.positions[].counters[].play",
                    "data.summary.positions[].counters[].win",
                    "data.weak_counters[].champion_name",
                    "data.weak_counters[].play",
                    "data.weak_counters[].my_win_rate",
                    "data.strong_counters[].champion_name",
                    "data.strong_counters[].play",
                    "data.strong_counters[].my_win_rate"),
            };

            try
            {
                var node = await client.CallToolAsync("lol_get_champion_analysis", arguments, ct).ConfigureAwait(false);
                return ParseCounters(node, enemy.Id, requested, resolver);
            }
            catch (OpGgApiException ex) when (ex.Status is null)
            {
                // Try the next spelling. A response carrying an HTTP status is an outage, not a
                // misspelling, and has to reach the caller so the status line can say so.
            }
            catch (OpGgParseException ex)
            {
                // Unreadable is not unreachable: no counters, but nothing to retry either.
                diagnostic?.Invoke($"Counter-Antwort unlesbar für {name}: {ex.Message}");
                return [];
            }
        }

        return [];
    }

    /// <summary>Reads counters exactly like the snapshot updater does, as live matchup edges.</summary>
    private static List<MatchupStat> ParseCounters(OpGgNode node, int enemyId, Lane requested, ChampionResolver resolver)
    {
        var stats = new List<MatchupStat>();
        var data = node["data"];

        foreach (var position in data["summary"]["positions"].Items)
        {
            var lane = Lanes.FromOpGg(position["name"].AsText()?.ToLowerInvariant());
            if (lane == Lane.Unknown)
                continue;

            foreach (var counter in position["counters"].Items)
            {
                var play = counter["play"].AsInt();
                if (play <= 0 || resolver.Resolve(counter["champion_name"].AsText()) is not { } counterId || counterId == enemyId)
                    continue;

                stats.Add(new MatchupStat
                {
                    ChampionId = enemyId,
                    OpponentId = counterId,
                    Lane = lane,
                    WinRate = (double)counter["win"].AsInt() / play,
                    Play = play,
                });
            }
        }

        // Both top-level lists, exactly like the snapshot updater: only strong_counters taught the
        // overlay where the enemy is STRONG — the half a counterpick cannot use.
        foreach (var listName in new[] { "weak_counters", "strong_counters" })
        {
            foreach (var counter in data[listName].Items)
            {
                var play = counter["play"].AsInt();
                if (play <= 0 || resolver.Resolve(counter["champion_name"].AsText()) is not { } counterId || counterId == enemyId)
                    continue;

                stats.Add(new MatchupStat
                {
                    ChampionId = enemyId,
                    OpponentId = counterId,
                    Lane = requested,
                    WinRate = counter["my_win_rate"].AsNumber(),
                    Play = play,
                });
            }
        }

        return stats;
    }
}
