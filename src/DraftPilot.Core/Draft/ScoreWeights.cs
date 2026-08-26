namespace DraftPilot.Core.Draft;

/// <summary>
/// How much each signal counts. Every term the recommender produces is normalised to roughly
/// -1 to +1 first, so these numbers are directly comparable to each other.
/// </summary>
public sealed record ScoreWeights
{
    /// <summary>Strength of the champion in the lane, from the tier list.</summary>
    public double Tier { get; init; } = 1.0;

    /// <summary>Head-to-head against the champion we expect on the same lane.</summary>
    public double LaneMatchup { get; init; } = 1.0;

    /// <summary>Head-to-head against the rest of the enemy team.</summary>
    public double TeamMatchup { get; init; } = 0.4;

    /// <summary>Duo record with the allies already locked in.</summary>
    public double Synergy { get; init; } = 0.6;

    /// <summary>How well the pick fills what the composition is missing.</summary>
    public double Composition { get; init; } = 0.8;

    /// <summary>Balanced default.</summary>
    public static ScoreWeights Meta { get; } = new();

    /// <summary>Leans on the direct lane matchup.</summary>
    public static ScoreWeights Counter { get; } = new()
    {
        Tier = 0.6,
        LaneMatchup = 1.8,
        TeamMatchup = 0.7,
        Synergy = 0.4,
        Composition = 0.6,
    };

    /// <summary>Leans on synergies and what the team still needs.</summary>
    public static ScoreWeights TeamComp { get; } = new()
    {
        Tier = 0.7,
        LaneMatchup = 0.6,
        TeamMatchup = 0.3,
        Synergy = 1.2,
        Composition = 1.6,
    };

    public static IReadOnlyList<(string Name, ScoreWeights Weights)> Presets { get; } =
    [
        ("Meta", Meta),
        ("Counter", Counter),
        ("Teamcomp", TeamComp),
    ];
}
