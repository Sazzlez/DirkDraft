using System.Text.Json;
using DraftPilot.Core.Data;

namespace DraftPilot.Meta.Riot;

/// <summary>
/// Turns the collector's draft counts into a <see cref="MetaSnapshot"/> — the same file shape the
/// model has always read, now from DirkDraft's own games: Riot's champion list and class data from
/// Data Dragon, everything else counted, and the baselines measured on this very file the way the
/// OP.GG build measured them on its.
/// </summary>
public static class RiotMetaExport
{
    public const string Source = "riot";

    public static MetaSnapshot Build(
        MetaRows rows,
        IReadOnlyList<ChampionEntry> champions,
        string patch,
        string tier,
        string platform,
        DateTimeOffset collectedAtUtc,
        DateTimeOffset builtAtUtc)
    {
        var lanes = rows.Lanes.ToList();
        var duels = rows.Duels.ToList();
        var duos = rows.Duos.ToList();

        DamagePerGame? PerGame(int id) => rows.DamagePerGame is { } map && map.TryGetValue(id, out var value) ? value : null;

        var entries = champions
            .Select(champion => new ChampionEntry
            {
                Id = champion.Id,
                Key = champion.Key,
                Name = champion.Name,
                Damage = rows.Damage.GetValueOrDefault(champion.Id, DamageType.Unknown),
                PhysicalDamage = PerGame(champion.Id)?.Physical,
                MagicDamage = PerGame(champion.Id)?.Magic,
                Tags = [.. champion.Tags],
                AttackRange = champion.AttackRange,
                Defense = champion.Defense,
            })
            .OrderBy(champion => champion.Id)
            .ToList();

        var warnings = new List<string>();
        var known = entries.Select(champion => champion.Id).ToHashSet();
        var unknown = lanes.Select(stat => stat.ChampionId).Where(id => !known.Contains(id)).Distinct().Count();
        if (unknown > 0)
            warnings.Add($"{unknown} gezählte Champions fehlen in Data Dragon — ihre Zeilen bleiben ungenutzt.");

        // Every duel here is complete, so the expectation is the line fitted on these duels in this
        // bracket — not the listing offset OP.GG's selection needed. Measured, not assumed to be 1/0.
        var line = SnapshotBuilder.MeasureCompleteDuelLine(duels, lanes);

        return new MetaSnapshot
        {
            Patch = patch,
            DataPatch = patch,
            DataAsOfUtc = collectedAtUtc,
            BuiltAtUtc = builtAtUtc,
            GameMode = "ranked",
            Tier = tier,
            Source = Source,
            Platform = platform,
            Games = rows.Matches,
            LaneBaseline = SnapshotBuilder.MeasureLaneBaseline(lanes),
            SynergyBaseline = SnapshotBuilder.MeasureSynergyBaseline(duos),
            MatchupBaseline = SnapshotBuilder.MeasureMatchupBaseline(duels, lanes),
            CompleteDuelSlope = line.Slope,
            CompleteDuelOffset = line.Offset,
            CompleteDuelSample = line.Duels,
            CompleteDuelSlopeError = double.IsFinite(line.StandardError) ? line.StandardError : 0,
            Champions = entries,
            LaneStats = lanes,
            Matchups = duels,
            Synergies = duos,
            Versus = [.. rows.Versus ?? []],
            Warnings = warnings,
        };
    }

    /// <summary>
    /// The champion list with Riot's class tags, attack range and defensive rating, from Data
    /// Dragon's <c>champion.json</c> (any locale; the English one keeps the names plain). A field
    /// shipped in an unexpected type costs that champion's statics, not the whole file.
    /// </summary>
    public static List<ChampionEntry> ParseDataDragon(string championJson)
    {
        using var document = JsonDocument.Parse(championJson);
        var list = new List<ChampionEntry>();
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return list;

        foreach (var entry in data.EnumerateObject())
        {
            var value = entry.Value;
            if (!value.TryGetProperty("key", out var keyElement)
                || keyElement.ValueKind != JsonValueKind.String
                || !int.TryParse(keyElement.GetString(), out var id))
            {
                continue;
            }

            var champion = new ChampionEntry
            {
                Id = id,
                Key = value.TryGetProperty("id", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() ?? entry.Name : entry.Name,
                Name = value.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? entry.Name : entry.Name,
            };

            if (value.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tags.EnumerateArray())
                {
                    if (tag.ValueKind == JsonValueKind.String && tag.GetString() is { Length: > 0 } text)
                        champion.Tags.Add(text);
                }
            }

            if (value.TryGetProperty("stats", out var stats)
                && stats.TryGetProperty("attackrange", out var range)
                && range.ValueKind == JsonValueKind.Number
                && range.TryGetDouble(out var rangeValue))
            {
                champion.AttackRange = (int)Math.Round(rangeValue);
            }

            if (value.TryGetProperty("info", out var info)
                && info.TryGetProperty("defense", out var defense)
                && defense.ValueKind == JsonValueKind.Number
                && defense.TryGetInt32(out var defenseValue))
            {
                champion.Defense = defenseValue;
            }

            list.Add(champion);
        }

        return list;
    }
}
