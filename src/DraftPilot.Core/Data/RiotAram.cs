using System.Text.Json.Serialization;

namespace DraftPilot.Core.Data;

/// <summary>One champion's ARAM record.</summary>
public sealed class AramChampion
{
    public int ChampionId { get; set; }

    public int Games { get; set; }

    public int Wins { get; set; }
}

/// <summary>
/// aram.json: DirkDraft's own ARAM count — win rate per champion for the bench ("Behalten oder
/// tauschen?") and builds (lane -1, tier "aram"). Counts only, no players.
/// <para>
/// Plain ARAM stands in for ARAM Mayhem: Riot's API answers 403 for Mayhem games and lists none
/// (measured 07.11.2026), so Mayhem cannot be counted. The app says where the numbers come from.
/// </para>
/// </summary>
public sealed class RiotAramFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string Patch { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public DateTimeOffset CollectedAtUtc { get; set; }

    public int Games { get; set; }

    public List<AramChampion> Champions { get; set; } = [];

    public List<ChampionBuildStats> Builds { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(RiotAramFile))]
public sealed partial class RiotAramJson : JsonSerializerContext;

/// <summary>Counts ARAM games: champions and — through <see cref="RiotBuildTally"/> — builds.</summary>
public sealed class RiotAramTally
{
    public const string Tier = "aram";

    private readonly Dictionary<int, AramChampion> _champions = [];
    private readonly RiotBuildTally _builds = new();

    public int Games { get; private set; }

    public void Add(MatchReading game)
    {
        Games++;
        foreach (var player in game.Participants)
        {
            var champion = Champion(player.ChampionId);
            champion.Games++;
            if (player.Win)
                champion.Wins++;

            _builds.Add(player, Tier);
        }
    }

    public void Add(RiotAramFile stored)
    {
        Games += stored.Games;
        foreach (var row in stored.Champions)
        {
            var champion = Champion(row.ChampionId);
            champion.Games += row.Games;
            champion.Wins += row.Wins;
        }

        _builds.Add(stored.Builds);
    }

    /// <param name="keep">Build options kept per slot.</param>
    /// <param name="minimumGames">Build options below this are left out.</param>
    public RiotAramFile Export(string patch, string platform, DateTimeOffset collectedAtUtc, int keep, int minimumGames = 1) => new()
    {
        Patch = patch,
        Platform = platform,
        CollectedAtUtc = collectedAtUtc,
        Games = Games,
        Champions = [.. _champions.Values.OrderBy(c => c.ChampionId)
            .Select(c => new AramChampion { ChampionId = c.ChampionId, Games = c.Games, Wins = c.Wins })],
        Builds = _builds.Export(keep, minimumGames),
    };

    private AramChampion Champion(int id)
    {
        if (!_champions.TryGetValue(id, out var champion))
            _champions[id] = champion = new AramChampion { ChampionId = id };
        return champion;
    }
}
