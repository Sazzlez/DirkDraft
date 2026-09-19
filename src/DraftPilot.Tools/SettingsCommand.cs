using DraftPilot.Core.Config;

namespace DraftPilot.Tools;

/// <summary>
/// Reads and changes the handful of settings that decide what the numbers mean. There is no
/// settings window, and the two that matter most — the rank bracket and the game mode — change what
/// every score in the tool describes; asking somebody to hand-edit JSON for that is an invitation to
/// a typo that fails silently.
/// <para>
/// Deliberately a short allow-list. Window position and the other conveniences belong to the window
/// and are written by it.
/// </para>
/// </summary>
internal static class SettingsCommand
{
    /// <summary>OP.GG's bracket ids, as measured against the live endpoint on 2026-09-18.</summary>
    private static readonly string[] Tiers =
    [
        "all", "iron", "bronze", "silver", "gold", "platinum", "emerald", "diamond",
        "master", "grandmaster", "challenger", "platinum_plus", "emerald_plus", "diamond_plus",
    ];

    /// <summary>
    /// OP.GG's <c>game_mode</c> enum, minus the two rotating modes this tool has no path for. Only
    /// the stored snapshot listens to this — during a draft the queue comes from the client, and
    /// the live counters and the build follow that, not the setting.
    /// </summary>
    private static readonly string[] GameModes = ["ranked", "flex", "aram"];

    public static int Run(string[] args)
    {
        var settings = AppSettings.Load();

        if (args.Length < 3)
        {
            Print(settings);
            return 0;
        }

        var key = args[1].ToLowerInvariant();
        var value = args[2];

        switch (key)
        {
            case "tier":
                if (!Tiers.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"Unbekanntes Bracket „{value}“. Moeglich: {string.Join(", ", Tiers)}");
                    return 2;
                }

                settings.Tier = value.ToLowerInvariant();
                break;

            case "gamemode":
                if (!GameModes.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"Unbekannter Modus „{value}“. Moeglich: {string.Join(", ", GameModes)}");
                    return 2;
                }

                settings.GameMode = value.ToLowerInvariant();
                break;

            case "recommendationcount":
                if (!int.TryParse(value, out var count) || count is < 1 or > 20)
                {
                    Console.Error.WriteLine("recommendationCount braucht eine Zahl von 1 bis 20.");
                    return 2;
                }

                settings.RecommendationCount = count;
                break;

            default:
                Console.Error.WriteLine($"Unbekannte Einstellung „{key}“. Aenderbar: tier, gamemode, recommendationcount");
                return 2;
        }

        settings.Save();

        Console.WriteLine($"{key} = {value}");
        Console.WriteLine();
        Print(settings);

        if (key is "tier" or "gamemode")
            Console.WriteLine("\nWirksam nach dem naechsten „Daten aktualisieren“ — der Snapshot beschreibt bis dahin den alten Stand.");

        return 0;
    }

    private static void Print(AppSettings settings)
    {
        Console.WriteLine($"Datei: {AppPaths.SettingsPath}");
        Console.WriteLine($"  tier                {settings.Tier}");
        Console.WriteLine($"  gameMode            {settings.GameMode}");
        Console.WriteLine($"  recommendationCount {settings.RecommendationCount}");
        Console.WriteLine($"  dataLanguage        {settings.DataLanguage}");
        Console.WriteLine($"  showUnowned         {settings.ShowUnowned}");
    }
}
