using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta.OpGg;

namespace DraftPilot.Meta;

/// <summary>
/// Turns a champion analysis into a <see cref="BuildPlan"/> — the build that needs no opponent.
/// <para>
/// The matchup guide answers for a pairing and is the better source whenever there IS one; this
/// answers for a champion in a game mode. ARAM has no lanes and no known opponent, so it is the
/// only source there at all. Measured on 2026-09-18: Darius in ARAM, core over 1.231 games
/// (docs/opgg-schnittstelle.md).
/// </para>
/// <para>
/// Unlike the guide, this endpoint reports exactly ONE set per slot rather than a ranked list — so
/// there are no alternatives to show, and the build card's "auch gespielt" row stays empty by
/// itself.
/// </para>
/// </summary>
public static class AnalysisBuildParser
{
    /// <summary>The fields this parser needs; asked for exactly, because the endpoint demands it.</summary>
    public static readonly string[] Fields =
    [
        "data.summary.average_stats.play",
        "data.summary.average_stats.win_rate",
        "data.runes.primary_page_id",
        "data.runes.primary_page_name",
        "data.runes.primary_rune_ids",
        "data.runes.primary_rune_names",
        "data.runes.secondary_page_id",
        "data.runes.secondary_page_name",
        "data.runes.secondary_rune_ids",
        "data.runes.secondary_rune_names",
        "data.runes.stat_mod_ids",
        "data.runes.play",
        "data.runes.win",
        "data.starter_items.ids",
        "data.starter_items.ids_names",
        "data.starter_items.play",
        "data.starter_items.win",
        "data.boots.ids",
        "data.boots.ids_names",
        "data.boots.play",
        "data.boots.win",
        "data.core_items.ids",
        "data.core_items.ids_names",
        "data.core_items.play",
        "data.core_items.win",
        "data.last_items[].ids",
        "data.last_items[].ids_names",
        "data.last_items[].play",
        "data.last_items[].win",
        "data.summoner_spells.ids",
        "data.summoner_spells.ids_names",
        "data.summoner_spells.play",
        "data.summoner_spells.win",
        "data.skills.order",
        "data.skill_masteries.ids",
    ];

    public static BuildPlan Parse(OpGgNode response, ChampionEntry champion, Lane lane, string mode, string patch)
    {
        var data = response["data"];

        var plan = new BuildPlan
        {
            SchemaVersion = BuildPlan.CurrentSchemaVersion,
            ChampionId = champion.Id,
            ChampionName = champion.Name,
            OpponentId = 0,
            OpponentName = string.Empty,
            Lane = lane,
            Mode = mode,
            Patch = patch,
            FetchedAtUtc = DateTimeOffset.UtcNow,
        };

        plan.Runes = ReadRunes(data["runes"]);
        plan.Starters = Single(data["starter_items"]);
        plan.Boots = Single(data["boots"]);
        plan.CoreItems = Single(data["core_items"]);
        plan.LateItems = [.. data["last_items"].Items.Select(ReadSet).Where(set => set is not null).Take(3)!];
        plan.SummonerSpells = Single(data["summoner_spells"]);
        plan.SkillOrder = [.. data["skills"]["order"].Items.Select(entry => entry.AsText() ?? string.Empty).Where(step => step.Length > 0)];
        plan.SkillPriority = string.Join(" > ", data["skill_masteries"]["ids"].Items
            .Select(entry => entry.AsText() ?? string.Empty)
            .Where(step => step.Length > 0));

        return plan;
    }

    private static List<ItemSet> Single(OpGgNode node)
        => ReadSet(node) is { } set ? [set] : [];

    private static ItemSet? ReadSet(OpGgNode node)
    {
        var ids = node["ids"].Items.Select(entry => entry.AsInt()).Where(id => id > 0).ToList();
        var names = node["ids_names"].Items.Select(entry => entry.AsText() ?? string.Empty).ToList();

        if (ids.Count == 0 && names.Count == 0)
            return null;

        var play = node["play"].AsInt();
        var wins = node["win"].AsInt();

        return new ItemSet
        {
            ItemIds = ids,
            Items = names,
            Play = play,
            // Wins as a counter, so no rounding to undo — unlike most rates this endpoint reports.
            WinRate = play > 0 ? (double)wins / play : 0,
        };
    }

    private static RunePage? ReadRunes(OpGgNode node)
    {
        var primaryIds = node["primary_rune_ids"].Items.Select(entry => entry.AsInt()).Where(id => id > 0).ToList();
        var secondaryIds = node["secondary_rune_ids"].Items.Select(entry => entry.AsInt()).Where(id => id > 0).ToList();
        var shardIds = node["stat_mod_ids"].Items.Select(entry => entry.AsInt()).Where(id => id > 0).ToList();

        // The same shape the rune import demands: four keystone-and-primary, two secondary, three
        // shards. Anything shorter could not be imported and would only look like a page.
        if (primaryIds.Count < 4 || secondaryIds.Count < 2 || shardIds.Count < 3)
            return null;

        var play = node["play"].AsInt();
        var wins = node["win"].AsInt();

        return new RunePage
        {
            PrimaryPath = node["primary_page_name"].AsText() ?? string.Empty,
            PrimaryPathId = node["primary_page_id"].AsInt(),
            PrimaryRunes = [.. node["primary_rune_names"].Items.Select(entry => entry.AsText() ?? string.Empty)],
            PrimaryRuneIds = primaryIds,
            SecondaryPath = node["secondary_page_name"].AsText() ?? string.Empty,
            SecondaryPathId = node["secondary_page_id"].AsInt(),
            SecondaryRunes = [.. node["secondary_rune_names"].Items.Select(entry => entry.AsText() ?? string.Empty)],
            SecondaryRuneIds = secondaryIds,
            Shards = [.. shardIds.Select(StatShards.NameOf)],
            ShardIds = shardIds,
            Play = play,
            WinRate = play > 0 ? (double)wins / play : 0,
        };
    }
}
