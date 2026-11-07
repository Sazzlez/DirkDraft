using System.Text.Json.Serialization;

namespace DraftPilot.Core.Data;

/// <summary>One champion's Mayhem record.</summary>
public sealed class MayhemChampion
{
    public int ChampionId { get; set; }

    public int Games { get; set; }

    public int Wins { get; set; }
}

/// <summary>How a champion did in the games it took one augment.</summary>
public sealed class AugmentCount
{
    public int ChampionId { get; set; }

    public int AugmentId { get; set; }

    public int Games { get; set; }

    public int Wins { get; set; }
}

/// <summary>
/// mayhem.json: DirkDraft's own ARAM Mayhem count — win rate per champion for the bench, builds
/// (lane -1, tier "mayhem") and augments per champion. Counts only, no players.
/// </summary>
public sealed class RiotMayhemFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string Patch { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public DateTimeOffset CollectedAtUtc { get; set; }

    public int Games { get; set; }

    public List<MayhemChampion> Champions { get; set; } = [];

    public List<AugmentCount> Augments { get; set; } = [];

    public List<ChampionBuildStats> Builds { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(RiotMayhemFile))]
public sealed partial class RiotMayhemJson : JsonSerializerContext;

/// <summary>Counts Mayhem games: champions, augments and — through <see cref="RiotBuildTally"/> — builds.</summary>
public sealed class RiotMayhemTally
{
    public const string Tier = "mayhem";

    private readonly Dictionary<int, MayhemChampion> _champions = [];
    private readonly Dictionary<(int Champion, int Augment), AugmentCount> _augments = [];
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

            // An augment picked twice in one game (it cannot be, but a broken record could) is one.
            foreach (var augment in player.PickedAugments.Distinct())
                Augment(player.ChampionId, augment, 1, player.Win ? 1 : 0);

            _builds.Add(player, Tier);
        }
    }

    public void Add(RiotMayhemFile stored)
    {
        Games += stored.Games;
        foreach (var row in stored.Champions)
        {
            var champion = Champion(row.ChampionId);
            champion.Games += row.Games;
            champion.Wins += row.Wins;
        }

        foreach (var row in stored.Augments)
            Augment(row.ChampionId, row.AugmentId, row.Games, row.Wins);

        _builds.Add(stored.Builds);
    }

    /// <param name="keep">Build options kept per slot.</param>
    /// <param name="minimumGames">Build options and augments below this are left out.</param>
    public RiotMayhemFile Export(string patch, string platform, DateTimeOffset collectedAtUtc, int keep, int minimumGames = 1) => new()
    {
        Patch = patch,
        Platform = platform,
        CollectedAtUtc = collectedAtUtc,
        Games = Games,
        Champions = [.. _champions.Values.OrderBy(c => c.ChampionId)
            .Select(c => new MayhemChampion { ChampionId = c.ChampionId, Games = c.Games, Wins = c.Wins })],
        Augments = [.. _augments.Values.Where(a => a.Games >= minimumGames).OrderBy(a => a.ChampionId).ThenBy(a => a.AugmentId)
            .Select(a => new AugmentCount { ChampionId = a.ChampionId, AugmentId = a.AugmentId, Games = a.Games, Wins = a.Wins })],
        Builds = _builds.Export(keep, minimumGames),
    };

    private MayhemChampion Champion(int id)
    {
        if (!_champions.TryGetValue(id, out var champion))
            _champions[id] = champion = new MayhemChampion { ChampionId = id };
        return champion;
    }

    private void Augment(int champion, int augment, int games, int wins)
    {
        if (!_augments.TryGetValue((champion, augment), out var count))
            _augments[(champion, augment)] = count = new AugmentCount { ChampionId = champion, AugmentId = augment };
        count.Games += games;
        count.Wins += wins;
    }
}
