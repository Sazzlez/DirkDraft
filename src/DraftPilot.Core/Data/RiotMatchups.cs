using System.Text.Json.Serialization;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>
/// What players take against a particular lane opponent: keystone, secondary tree, summoner spells
/// other than Flash, and each starting item — for runes and starts that fit the matchup instead of
/// the champion's one build (docs/draft-konzept.md, phase 4). Counted from the same games as
/// everything else; the lane opponent is the enemy on the same lane.
/// </summary>
public sealed class RiotMatchupTally
{
    public const int Keystone = 0;
    public const int SecondaryTree = 1;
    public const int Spell = 2;
    public const int Starter = 3;

    /// <summary>Flash goes on every champion; only the other spell says something about the matchup.</summary>
    public const int Flash = 4;

    private readonly Dictionary<(int Champion, Lane Lane, int Opponent, int Kind, int Value), (int Games, int Wins)> _counts = [];

    /// <summary>One game: each player against the enemy on the same lane, where there is exactly one.</summary>
    public void Add(MatchReading game)
    {
        foreach (var player in game.Participants)
        {
            if (player.Lane == Lane.Unknown)
                continue;
            var opponents = game.Participants.Where(other => other.TeamId != player.TeamId && other.Lane == player.Lane).ToList();
            if (opponents.Count != 1 || opponents[0].ChampionId == player.ChampionId)
                continue;
            var opponent = opponents[0].ChampionId;

            // [primary tree, keystone, 3 runes, secondary tree, 2 runes, 3 shards] — see RiotMatchReader.
            if (player.Runes.Count >= 6)
            {
                Bump(player, opponent, Keystone, player.Runes[1]);
                Bump(player, opponent, SecondaryTree, player.Runes[5]);
            }

            foreach (var spell in player.Spells.Where(spell => spell != Flash))
                Bump(player, opponent, Spell, spell);
            foreach (var item in player.Starters.Distinct())
                Bump(player, opponent, Starter, item);
        }
    }

    /// <summary>Adds stored rows, e.g. the state of the previous run.</summary>
    public void Add(IEnumerable<int[]> rows)
    {
        foreach (var row in rows.Where(row => row.Length >= 7))
            Merge((row[0], (Lane)row[1], row[2], row[3], row[4]), row[5], row[6]);
    }

    /// <summary>[champion, lane, opponent, kind, value, games, wins]; sorted, so equal tallies write equal files.</summary>
    public List<int[]> Export() =>
    [
        .. _counts
            .OrderBy(p => p.Key.Champion).ThenBy(p => p.Key.Lane).ThenBy(p => p.Key.Opponent).ThenBy(p => p.Key.Kind).ThenBy(p => p.Key.Value)
            .Select(p => new[] { p.Key.Champion, (int)p.Key.Lane, p.Key.Opponent, p.Key.Kind, p.Key.Value, p.Value.Games, p.Value.Wins }),
    ];

    private void Bump(ParticipantBuild player, int opponent, int kind, int value)
    {
        if (value > 0)
            Merge((player.ChampionId, player.Lane, opponent, kind, value), 1, player.Win ? 1 : 0);
    }

    private void Merge((int, Lane, int, int, int) key, int games, int wins)
    {
        var (g, w) = _counts.GetValueOrDefault(key);
        _counts[key] = (g + games, w + wins);
    }
}

/// <summary>The matchup counts as handed to the measurement: an artifact of the run, not published yet.</summary>
public sealed class RiotMatchupFile
{
    public int Version { get; set; } = 1;

    public string Patch { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public DateTimeOffset CollectedAtUtc { get; set; }

    public string Format { get; set; } =
        "[Champion, Lane, Lane-Gegner, Art, Wert, Spiele, Siege]; Art 0 Keystone, 1 Nebenbaum, 2 Zauber außer Blitz, 3 Startitem.";

    public List<int[]> Rows { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(RiotMatchupFile))]
public sealed partial class RiotMatchupJson : JsonSerializerContext;
