using System.Text.Json;
using System.Text.Json.Serialization;
using DraftPilot.Core.Config;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>One rune page: keystone first in <see cref="PrimaryRunes"/>.</summary>
/// <remarks>
/// Carries both names (for display) and Riot's ids: the ids address the rune icons on disk and are
/// what the client's rune-page endpoint expects, so nothing has to be reverse-translated later.
/// </remarks>
/// <summary>
/// Which game mode a plan describes when it is not a lane matchup — <c>aram</c> today. Empty means
/// the normal case: one champion against one named opponent on one lane. Persisted, because the
/// cache is read back long after the draft that fetched it.
/// </summary>
public static class BuildModes
{
    public const string Aram = "aram";

    /// <summary>The mode in the words the window uses, or empty for a lane matchup.</summary>
    public static string Display(string mode) => mode switch
    {
        Aram => "ARAM",
        _ => string.Empty,
    };
}

public sealed class RunePage
{
    public string PrimaryPath { get; set; } = string.Empty;

    /// <summary>Riot's style id of the primary path, e.g. 8000 for Precision.</summary>
    public int PrimaryPathId { get; set; }

    public List<string> PrimaryRunes { get; set; } = [];

    public List<int> PrimaryRuneIds { get; set; } = [];

    public string SecondaryPath { get; set; } = string.Empty;

    public int SecondaryPathId { get; set; }

    public List<string> SecondaryRunes { get; set; } = [];

    public List<int> SecondaryRuneIds { get; set; } = [];

    /// <summary>Stat shards, already translated to display names.</summary>
    public List<string> Shards { get; set; } = [];

    public List<int> ShardIds { get; set; } = [];

    public double WinRate { get; set; }

    public int Play { get; set; }
}

/// <summary>A set of items (or summoner spells) with how it performed in this matchup.</summary>
public sealed class ItemSet
{
    public List<string> Items { get; set; } = [];

    /// <summary>Riot's item (or spell) ids, parallel to <see cref="Items"/>.</summary>
    public List<int> ItemIds { get; set; } = [];

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
    /// <summary>
    /// Bump when the shape of this class changes in a way old cache files cannot satisfy. Loading
    /// discards mismatches, so the next draft simply fetches fresh instead of showing half a plan.
    /// </summary>
    // 3: boots now carry four alternatives so the situational choice (Mercs vs Steelcaps) has
    // data to pick from; older cached plans hold only two and are refetched.
    // 4: a plan without an opponent is now fetched per queue and per rank bracket, which the file
    // name did not distinguish. Version-3 files were written before that and cannot say which
    // population they describe, so they are discarded rather than shown under a wrong label.
    public const int CurrentSchemaVersion = 4;

    /// <summary>
    /// Defaults to 0, NOT to the current version: a default would also apply while deserialising
    /// an old file that lacks the field, waving exactly the files through that the check exists
    /// for. The parser stamps the current version on every freshly fetched plan.
    /// </summary>
    public int SchemaVersion { get; set; }

    public int ChampionId { get; set; }

    public string ChampionName { get; set; } = string.Empty;

    public int OpponentId { get; set; }

    public string OpponentName { get; set; } = string.Empty;

    public Lane Lane { get; set; }

    /// <summary>
    /// The game mode when this plan is not about a lane matchup — see <see cref="BuildModes"/>.
    /// Empty for the normal case, where <see cref="OpponentName"/> carries the answer instead.
    /// </summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>
    /// What besides patch, champion, lane and opponent this plan depends on — the queue and the
    /// rank bracket it was fetched for, or empty for a matchup plan, whose source takes neither.
    /// Persisted and part of the file name, so two populations cannot share one cache entry; see
    /// <see cref="BuildCache.PathFor"/>.
    /// </summary>
    public string Variant { get; set; } = string.Empty;

    public string Patch { get; set; } = string.Empty;

    public DateTimeOffset FetchedAtUtc { get; set; }

    public RunePage? Runes { get; set; }

    public List<ItemSet> Starters { get; set; } = [];

    public List<ItemSet> Boots { get; set; } = [];

    /// <summary>Three-item cores, best first.</summary>
    public List<ItemSet> CoreItems { get; set; } = [];

    /// <summary>Common late-game buys, one item per entry, most played first.</summary>
    public List<ItemSet> LateItems { get; set; } = [];

    public List<ItemSet> SummonerSpells { get; set; } = [];

    /// <summary>Dominant skill priority, e.g. <c>Q &gt; E &gt; W</c>.</summary>
    public string SkillPriority { get; set; } = string.Empty;

    /// <summary>
    /// Which ability to level at each of the first 15 levels, from the most-played order of this
    /// matchup. OP.GG reports only 15 entries; 16-18 are derived on display and marked as such.
    /// </summary>
    public List<string> SkillOrder { get; set; } = [];

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
        5001 => "Health (scaling)",
        5002 => "Armor",
        5003 => "MR",
        5005 => "Attack Speed",
        5007 => "Ability Haste",
        5008 => "Adaptive Force",
        5010 => "Movement Speed",
        5011 => "Health",
        5013 => "Tenacity",
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

    /// <summary>
    /// The variant string for a build fetched without an opponent: the queue it was fetched for and
    /// the rank bracket. Both callers — the fetch that writes the plan and the lookup that reads it
    /// back — go through here, because two spellings of the same variant means a cache that never
    /// hits and a champion build refetched in every draft.
    /// </summary>
    /// <param name="gameMode">OP.GG's queue name; empty means Summoner's Rift without a mode of its own.</param>
    public static string VariantFor(string gameMode, string tier)
        => $"{(gameMode.Length == 0 ? "sr" : gameMode)}-{(tier.Length == 0 ? "all" : tier)}";

    /// <summary>
    /// The file a plan lives in. The four identifying facts of a matchup plan — patch, champion,
    /// lane, opponent — plus <paramref name="variant"/>.
    /// <para>
    /// The variant exists because the opponent-free build is not identified by those four. It is
    /// fetched per queue and per rank bracket, so Solo/Duo and Flex answer differently for the same
    /// champion on the same lane, as do Gold and Platinum. Without it the two share one file and
    /// whichever draft happened to run first decides what the other one sees — silently, because a
    /// build never says which population it came from. The matchup guide takes neither parameter,
    /// so those plans pass an empty variant and keep the old names.
    /// </para>
    /// </summary>
    public string PathFor(string patch, int championId, Lane lane, int opponentId, string variant = "")
    {
        var suffix = variant.Length == 0 ? string.Empty : "-" + Sanitize(variant);
        return Path.Combine(_directory, $"{Sanitize(patch)}-{championId}-{lane.ToOpGg()}-{opponentId}{suffix}.json");
    }

    public BuildPlan? Load(string patch, int championId, Lane lane, int opponentId, string variant = "")
    {
        try
        {
            var path = PathFor(patch, championId, lane, opponentId, variant);
            if (!File.Exists(path))
                return null;

            using var stream = File.OpenRead(path);
            var plan = JsonSerializer.Deserialize(stream, BuildPlanJson.Default.BuildPlan);

            // A file written by an older build lacks fields this one renders; treat it as absent
            // so the next draft fetches fresh instead of showing half a plan.
            return plan?.SchemaVersion == BuildPlan.CurrentSchemaVersion ? plan : null;
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
            var path = PathFor(plan.Patch, plan.ChampionId, plan.Lane, plan.OpponentId, plan.Variant);
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

    /// <summary>
    /// Deletes plans that no longer serve anyone: other patches (the numbers changed) and entries
    /// past <paramref name="maxAge"/> (the matchup will not repeat with current data). Leftover
    /// <c>.tmp</c> files from interrupted writes go too. Returns how many files were removed —
    /// the cache is a working set, not an archive.
    /// </summary>
    public int CleanUp(string currentPatch, TimeSpan maxAge)
    {
        // An empty patch would make the prefix "-", which matches NO file name — and the sweep
        // below would then delete the entire cache as "foreign patches".
        if (string.IsNullOrWhiteSpace(currentPatch) || !Directory.Exists(_directory))
            return 0;

        var prefix = Sanitize(currentPatch) + "-";
        var cutoff = DateTime.UtcNow - maxAge;
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(_directory))
        {
            var name = Path.GetFileName(file);

            // Same one-minute grace SweepTempFiles gives: a .tmp this young is a Save in
            // progress, and deleting it under the writer made the plan silently never cache.
            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromMinutes(1))
                    continue;
            }
            else
            {
                var stale = !name.StartsWith(prefix, StringComparison.Ordinal)
                    || AgeOf(file) < cutoff;

                if (!stale)
                    continue;
            }

            try
            {
                File.Delete(file);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked or protected; the next run tries again.
            }
        }

        return removed;
    }

    /// <summary>
    /// When the plan's data was fetched: the stamp inside the file when it is readable, the file
    /// time otherwise. The stamp matters because a copied or restored file looks freshly written
    /// while its content may be weeks old. A plan from another schema version reports itself as
    /// ancient — Load refuses such files anyway, so keeping them until maxAge served nobody.
    /// </summary>
    private static DateTime AgeOf(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var plan = JsonSerializer.Deserialize(stream, BuildPlanJson.Default.BuildPlan);

            if (plan is not null && plan.SchemaVersion != BuildPlan.CurrentSchemaVersion)
                return DateTime.MinValue;

            if (plan is { FetchedAtUtc.Ticks: > 0 })
                return plan.FetchedAtUtc.UtcDateTime;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable: judge by the file time below.
        }

        return File.GetLastWriteTimeUtc(file);
    }

    private static string Sanitize(string patch)
    {
        var cleaned = new string([.. patch.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-')]);
        return cleaned.Length == 0 ? "unbekannt" : cleaned;
    }
}
