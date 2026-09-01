using System.Text.Json.Serialization;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

public enum DamageType
{
    Unknown,
    Physical,
    Magic,
    Mixed,
}

/// <summary>Identity and patch-stable facts about a champion.</summary>
public sealed class ChampionEntry
{
    /// <summary>Attack range above which a champion counts as ranged. Melee sits at 125-200.</summary>
    public const int RangedThreshold = 300;

    public int Id { get; set; }

    /// <summary>Riot's internal key, e.g. <c>MonkeyKing</c> for Wukong. Used for icon lookups.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Reported by OP.GG for every champion.</summary>
    public DamageType Damage { get; set; }

    /// <summary>Riot's own class tags: Tank, Fighter, Mage, Assassin, Marksman, Support.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Base attack range from Data Dragon. 0 when the static data was unavailable.</summary>
    public int AttackRange { get; set; }

    /// <summary>Riot's defensive rating, 0-10. 0 when unavailable.</summary>
    public int Defense { get; set; }

    public bool IsRanged => AttackRange >= RangedThreshold;

    public bool HasStaticData => AttackRange > 0;

    public bool IsTagged(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
}

/// <summary>How a champion performs in one lane.</summary>
public sealed class LaneStat
{
    public int ChampionId { get; set; }

    public Lane Lane { get; set; }

    public double WinRate { get; set; }

    public double PickRate { get; set; }

    public double BanRate { get; set; }

    /// <summary>
    /// Share of this champion's games played in this lane. This is the lane prior the predictor
    /// runs on, and unlike a win rate it is a ratio of large counts, so it is trustworthy.
    /// </summary>
    public double RoleRate { get; set; }

    /// <summary>OP.GG's own tier, 1 = strongest, 5 = weakest. 0 when unrated.</summary>
    public int Tier { get; set; }

    /// <summary>Sample size. Small values are why every rate gets shrunk before use.</summary>
    public int Play { get; set; }

    /// <summary>
    /// True when the row comes from the tier list rather than from a champion analysis. Not stored:
    /// it only decides which of two rows for the same lane survives deduplication, and the two
    /// sources count different populations, so the larger sample is not automatically the better
    /// row.
    /// </summary>
    [JsonIgnore]
    public bool FromTierList { get; set; }
}

/// <summary>
/// One directed lane matchup. OP.GG caps these lists at a handful of entries per champion, so the
/// data is deliberately sparse: the notable matchups, not a full matrix.
/// </summary>
public sealed class MatchupStat
{
    public int ChampionId { get; set; }

    public int OpponentId { get; set; }

    public Lane Lane { get; set; }

    /// <summary><see cref="ChampionId"/>'s win rate into <see cref="OpponentId"/>.</summary>
    public double WinRate { get; set; }

    public int Play { get; set; }
}

/// <summary>One duo, e.g. a support and the ADC they are played with.</summary>
public sealed class SynergyStat
{
    public int ChampionId { get; set; }

    public Lane Lane { get; set; }

    public int PartnerId { get; set; }

    public Lane PartnerLane { get; set; }

    public double WinRate { get; set; }

    public int Play { get; set; }

    /// <summary>OP.GG's smoothed synergy tier, 0 = OP … 4 = C. -1 when absent.</summary>
    public int Tier { get; set; } = -1;
}

/// <summary>
/// The on-disk meta data, written by the update button and read by the app. Plain JSON: under a
/// megabyte, loads in milliseconds, and stays readable when something looks wrong.
/// </summary>
public sealed class MetaSnapshot
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Data Dragon version at build time, e.g. <c>16.17.1</c>.</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>
    /// The patch OP.GG aggregated the numbers over, e.g. <c>16.17</c>. Empty in files written before
    /// this was recorded, which is why nothing may depend on it being set.
    /// <para>
    /// Deliberately separate from <see cref="Patch"/>: that one is the client version at the moment
    /// the button was pressed and doubles as a cache key for build plans and icon paths. The two
    /// disagree on patch day, and only this one answers "do these numbers still describe the game".
    /// </para>
    /// </summary>
    public string DataPatch { get; set; } = string.Empty;

    /// <summary>When OP.GG last recomputed the numbers, as opposed to when we fetched them.</summary>
    public DateTimeOffset? DataAsOfUtc { get; set; }

    public DateTimeOffset BuiltAtUtc { get; set; }

    /// <summary>OP.GG game mode the data was pulled for, e.g. <c>ranked</c>.</summary>
    public string GameMode { get; set; } = "ranked";

    /// <summary>Rank filter used, or <see langword="null"/> for the all-tier aggregate.</summary>
    public string? Tier { get; set; }

    public List<ChampionEntry> Champions { get; set; } = [];

    public List<LaneStat> LaneStats { get; set; } = [];

    public List<MatchupStat> Matchups { get; set; } = [];

    public List<SynergyStat> Synergies { get; set; } = [];

    /// <summary>Notes worth showing the user, e.g. which lanes came back without counter data.</summary>
    public List<string> Warnings { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(MetaSnapshot))]
public sealed partial class MetaJson : JsonSerializerContext;
