using System.Text.Json;
using DraftPilot.Core.Config;

namespace DraftPilot.Core.Data;

public enum ScalingCurve
{
    Unknown,
    Early,
    Mid,
    Late,
}

/// <summary>
/// The four things no API reports. <see cref="IsKnown"/> is false for champions the curated file
/// does not cover — including any released after it was written — and every rule that depends on
/// these values skips such a champion rather than assuming a value for it.
/// </summary>
public readonly record struct ChampionTraits(
    int Engage,
    int Peel,
    int Cc,
    ScalingCurve Scaling,
    int Waveclear,
    bool IsKnown)
{
    public static ChampionTraits Unknown { get; } = new(0, 0, 0, ScalingCurve.Unknown, 0, false);

    /// <summary>Reliable, team-wide initiation.</summary>
    public bool HasHardEngage => IsKnown && Engage >= 2;

    /// <summary>Can protect a carry.</summary>
    public bool HasPeel => IsKnown && Peel >= 2;

    /// <summary>Hard crowd control, not just a slow.</summary>
    public bool HasHardCc => IsKnown && Cc >= 2;
}

/// <summary>Curated traits, loaded from <c>data/champion_traits.json</c>.</summary>
public sealed class TraitTable
{
    private readonly Dictionary<string, ChampionTraits> _byKey;

    private TraitTable(Dictionary<string, ChampionTraits> byKey) => _byKey = byKey;

    /// <summary>No curated traits at all; every dependent rule stays silent.</summary>
    public static TraitTable Empty { get; } = new(new Dictionary<string, ChampionTraits>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Builds a table in memory, for tests and for user overrides.</summary>
    public static TraitTable FromEntries(IEnumerable<KeyValuePair<string, ChampionTraits>> entries)
    {
        var table = new Dictionary<string, ChampionTraits>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, traits) in entries)
            table[key] = traits with { IsKnown = true };

        return new TraitTable(table);
    }

    public int Count => _byKey.Count;

    /// <summary>
    /// Loads the curated file. A missing or broken file degrades to <see cref="Empty"/>: fewer
    /// composition checks, never wrong ones.
    /// </summary>
    public static TraitTable Load(string? path = null)
    {
        path ??= AppPaths.Bundled("champion_traits.json");

        try
        {
            if (!File.Exists(path))
                return Empty;

            using var document = JsonDocument.Parse(File.ReadAllText(path));

            if (!document.RootElement.TryGetProperty("champions", out var champions)
                || champions.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var table = new Dictionary<string, ChampionTraits>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in champions.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object)
                    continue;

                table[entry.Name] = new ChampionTraits(
                    Engage: ReadInt(entry.Value, "engage"),
                    Peel: ReadInt(entry.Value, "peel"),
                    Cc: ReadInt(entry.Value, "cc"),
                    Scaling: ReadScaling(entry.Value),
                    Waveclear: ReadInt(entry.Value, "waveclear"),
                    IsKnown: true);
            }

            return new TraitTable(table);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    /// <summary>Traits for a champion key, or <see cref="ChampionTraits.Unknown"/>.</summary>
    public ChampionTraits For(string? championKey)
    {
        if (string.IsNullOrEmpty(championKey))
            return ChampionTraits.Unknown;

        return _byKey.TryGetValue(championKey, out var traits) ? traits : ChampionTraits.Unknown;
    }

    private static int ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? Math.Clamp(value.GetInt32(), 0, 3)
            : 0;

    private static ScalingCurve ReadScaling(JsonElement element)
    {
        if (!element.TryGetProperty("scaling", out var value) || value.ValueKind != JsonValueKind.String)
            return ScalingCurve.Unknown;

        return value.GetString()?.ToLowerInvariant() switch
        {
            "early" => ScalingCurve.Early,
            "mid" => ScalingCurve.Mid,
            "late" => ScalingCurve.Late,
            _ => ScalingCurve.Unknown,
        };
    }
}
