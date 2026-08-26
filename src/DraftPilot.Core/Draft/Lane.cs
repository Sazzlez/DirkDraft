namespace DraftPilot.Core.Draft;

/// <summary>
/// The five standard roles. <see cref="Unknown"/> covers both "the client did not tell us"
/// and "we have not predicted it yet".
/// </summary>
public enum Lane
{
    Unknown = -1,
    Top = 0,
    Jungle = 1,
    Mid = 2,
    Adc = 3,
    Support = 4,
}

public static class Lanes
{
    public const int Count = 5;

    /// <summary>All five real lanes in draft display order; the index equals the enum value.</summary>
    public static readonly Lane[] All = [Lane.Top, Lane.Jungle, Lane.Mid, Lane.Adc, Lane.Support];

    /// <summary>Maps the LCU's <c>assignedPosition</c> strings onto <see cref="Lane"/>.</summary>
    public static Lane FromLcu(string? position) => position?.ToLowerInvariant() switch
    {
        "top" => Lane.Top,
        "jungle" => Lane.Jungle,
        "middle" or "mid" => Lane.Mid,
        "bottom" or "bot" or "adc" => Lane.Adc,
        "utility" or "support" => Lane.Support,
        _ => Lane.Unknown,
    };

    /// <summary>Maps OP.GG's position slugs onto <see cref="Lane"/>.</summary>
    public static Lane FromOpGg(string? position) => position?.ToLowerInvariant() switch
    {
        "top" => Lane.Top,
        "jungle" => Lane.Jungle,
        "mid" => Lane.Mid,
        "adc" => Lane.Adc,
        "support" => Lane.Support,
        _ => Lane.Unknown,
    };

    /// <summary>The position slug OP.GG expects in requests.</summary>
    public static string ToOpGg(this Lane lane) => lane switch
    {
        Lane.Top => "top",
        Lane.Jungle => "jungle",
        Lane.Mid => "mid",
        Lane.Adc => "adc",
        Lane.Support => "support",
        _ => "none",
    };

    /// <summary>Short label for the UI.</summary>
    public static string Display(this Lane lane) => lane switch
    {
        Lane.Top => "Top",
        Lane.Jungle => "Jungle",
        Lane.Mid => "Mid",
        Lane.Adc => "Bot",
        Lane.Support => "Support",
        _ => "?",
    };
}
