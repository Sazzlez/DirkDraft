using System.Text.Json.Serialization;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>
/// The counted games themselves, one line each, for measuring how much each part of the draft model
/// predicts the winner (docs/draft-konzept.md): which champion played which lane on which side, and
/// who won. No player, no match id, no time — ten champions and a result.
/// </summary>
public static class RiotGames
{
    public const string Format =
        "[5 × Blau, 5 × Rot, Blau gewinnt (1/0)]; je Spieler Champion × 8 + Lane + 1 " +
        "(Lane −1 unbekannt, 0 Top, 1 Jungle, 2 Mid, 3 ADC, 4 Support), je Seite nach Lane sortiert.";

    /// <summary>
    /// One game as eleven numbers (<see cref="Format"/>); null when it does not split into two sides
    /// of five.
    /// </summary>
    public static int[]? Encode(MatchReading game)
    {
        var sides = game.Participants.GroupBy(player => player.TeamId).OrderBy(side => side.Key).ToList();
        if (sides.Count != 2 || sides.Any(side => side.Count() != 5))
            return null;

        var line = new int[11];
        var index = 0;
        foreach (var side in sides)
        {
            foreach (var player in side.OrderBy(player => (int)player.Lane).ThenBy(player => player.ChampionId))
                line[index++] = player.ChampionId * 8 + (int)player.Lane + 1;
        }

        line[10] = sides[0].First().Win ? 1 : 0;
        return line;
    }

    /// <summary>A player's number back as champion and lane.</summary>
    public static (int ChampionId, Lane Lane) Decode(int player) => (player / 8, (Lane)(player % 8 - 1));
}

/// <summary>
/// The games of one patch as the collector hands them to the measurement: an artifact of the run,
/// never published, never read by the app.
/// </summary>
public sealed class RiotGamesFile
{
    public int Version { get; set; } = 1;

    public string Patch { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string Tier { get; set; } = string.Empty;

    public DateTimeOffset CollectedAtUtc { get; set; }

    /// <summary>How a line reads, for whoever opens the file.</summary>
    public string Format { get; set; } = RiotGames.Format;

    public List<int[]> Games { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(RiotGamesFile))]
public sealed partial class RiotGamesJson : JsonSerializerContext;
