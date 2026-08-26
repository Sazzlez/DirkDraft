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
        plan.Boots = ItemSets(data, "boots", take: 2);
        plan.CoreItems = ItemSets(data, "core_items", take: 3);
        plan.SummonerSpells = ItemSets(data, "summoner_spells", take: 2);
        plan.SkillPriority = SkillPriority(data);

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

        if (bestBuild is not { } runes)
            return null;

        var wins = IntOf(chosen, "win");

        return new RunePage
        {
            PrimaryPath = TextOf(runes, "primary_page_name"),
            PrimaryRunes = Names(runes, "primary_rune_names"),
            SecondaryPath = TextOf(runes, "secondary_page_name"),
            SecondaryRunes = Names(runes, "secondary_rune_names"),
            Shards = [.. Ints(runes, "stat_mod_ids").Select(StatShards.NameOf)],
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
                Play = play,
                WinRate = (double)IntOf(entry, "win") / play,
                PickRate = NumberOf(entry, "pick_rate"),
            });
        }

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
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var value))
                yield return value;
        }
    }

    private static int IntOf(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var result) ? result : 0;

    private static double NumberOf(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble() : 0;

    private static string TextOf(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
}

/// <summary>
/// On-demand fetches for the draft on screen: the matchup guide for the locked pick, and fresh
/// counter lists for the enemies actually being faced. Only ever constructed from the explicit
/// draft button — the data-sovereignty rule (network only on click) applies here exactly as it
/// does to the big update.
/// </summary>
public sealed class LiveDraftFetcher(OpGgMcpClient client, string gameMode)
{
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
                catch (OpGgApiException)
                {
                    // Wrong spelling or no data for this pairing; try the next combination.
                }
                catch (JsonException)
                {
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
    public async Task<IReadOnlyList<MatchupStat>> FetchEnemyCountersAsync(
        ChampionEntry enemy,
        Lane likelyLane,
        ChampionResolver resolver,
        CancellationToken ct)
    {
        var requested = likelyLane == Lane.Unknown ? Lane.Mid : likelyLane;

        foreach (var name in ChampionResolver.ApiNames(enemy))
        {
            var arguments = new JsonObject
            {
                ["game_mode"] = gameMode,
                ["champion"] = name,
                ["position"] = requested.ToOpGg(),
                // "all" is mandatory: rank-filtered counter samples are almost always empty.
                ["tier"] = "all",
                ["desired_output_fields"] = OpGgMcpClient.Fields(
                    "data.summary.positions[].name",
                    "data.summary.positions[].counters[].champion_name",
                    "data.summary.positions[].counters[].play",
                    "data.summary.positions[].counters[].win",
                    "data.strong_counters[].champion_name",
                    "data.strong_counters[].play",
                    "data.strong_counters[].my_win_rate"),
            };

            try
            {
                var node = await client.CallToolAsync("lol_get_champion_analysis", arguments, ct).ConfigureAwait(false);
                return ParseCounters(node, enemy.Id, requested, resolver);
            }
            catch (OpGgApiException)
            {
                // Try the next spelling.
            }
            catch (OpGgParseException)
            {
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

        foreach (var counter in data["strong_counters"].Items)
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

        return stats;
    }
}
