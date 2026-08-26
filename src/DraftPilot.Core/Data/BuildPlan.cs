using System.Text.Json;
using System.Text.Json.Serialization;
using DraftPilot.Core.Config;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>One rune page: keystone first in <see cref="PrimaryRunes"/>.</summary>
public sealed class RunePage
{
    public string PrimaryPath { get; set; } = string.Empty;

    public List<string> PrimaryRunes { get; set; } = [];

    public string SecondaryPath { get; set; } = string.Empty;

    public List<string> SecondaryRunes { get; set; } = [];

    /// <summary>Stat shards, already translated to display names.</summary>
    public List<string> Shards { get; set; } = [];

    public double WinRate { get; set; }

    public int Play { get; set; }
}

/// <summary>A set of items (or summoner spells) with how it performed in this matchup.</summary>
public sealed class ItemSet
{
    public List<string> Items { get; set; } = [];

    public double WinRate { get; set; }

    public int Play { get; set; }

    public double PickRate { get; set; }
}

/// <summary>
/// What to take into the game: runes, shards, item path and spells for one matchup, straight from
/// observed games. Cached per patch so a repeat of the same matchup costs no network at all.
/// </summary>
public sealed class BuildPlan
{
    public int ChampionId { get; set; }

    public string ChampionName { get; set; } = string.Empty;

    public int OpponentId { get; set; }

    public string OpponentName { get; set; } = string.Empty;

    public Lane Lane { get; set; }

    public string Patch { get; set; } = string.Empty;

    public DateTimeOffset FetchedAtUtc { get; set; }

    public RunePage? Runes { get; set; }

    public List<ItemSet> Starters { get; set; } = [];

    public List<ItemSet> Boots { get; set; } = [];

    /// <summary>Three-item cores, best first.</summary>
    public List<ItemSet> CoreItems { get; set; } = [];

    public List<ItemSet> SummonerSpells { get; set; } = [];

    /// <summary>Dominant skill priority, e.g. <c>Q &gt; E &gt; W</c>.</summary>
    public string SkillPriority { get; set; } = string.Empty;

    public bool IsEmpty => Runes is null && CoreItems.Count == 0 && Starters.Count == 0;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(BuildPlan))]
public sealed partial class BuildPlanJson : JsonSerializerContext;

/// <summary>Riot's stat-shard ids, translated. The API reports only numbers here.</summary>
public static class StatShards
{
    public static string NameOf(int id) => id switch
    {
        5001 => "Leben (skalierend)",
        5002 => "Rüstung",
        5003 => "Magieresistenz",
        5005 => "Angriffstempo",
        5007 => "Fähigkeitstempo",
        5008 => "Adaptive Stärke",
        5010 => "Bewegungstempo",
        5011 => "Leben",
        5013 => "Zähigkeit",
        _ => $"Shard {id}",
    };
}

/// <summary>
/// On-disk cache for fetched build plans, one file per (patch, champion, lane, opponent).
/// A cache hit means the button costs no network at all.
/// </summary>
public sealed class BuildCache(string? directory = null)
{
    private readonly string _directory = directory ?? Path.Combine(AppPaths.DataDirectory, "builds");

    public string PathFor(string patch, int championId, Lane lane, int opponentId)
        => Path.Combine(_directory, $"{Sanitize(patch)}-{championId}-{lane.ToOpGg()}-{opponentId}.json");

    public BuildPlan? Load(string patch, int championId, Lane lane, int opponentId)
    {
        try
        {
            var path = PathFor(patch, championId, lane, opponentId);
            if (!File.Exists(path))
                return null;

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, BuildPlanJson.Default.BuildPlan);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken cache entry is refetched on the next click, not an error.
            return null;
        }
    }

    public void Save(BuildPlan plan)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(plan.Patch, plan.ChampionId, plan.Lane, plan.OpponentId);
            var temporary = path + ".tmp";

            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, plan, BuildPlanJson.Default.BuildPlan);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The plan is still on screen; only the shortcut for next time is lost.
        }
    }

    private static string Sanitize(string patch)
    {
        var cleaned = new string([.. patch.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-')]);
        return cleaned.Length == 0 ? "unbekannt" : cleaned;
    }
}
