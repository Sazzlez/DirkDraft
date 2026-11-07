using System.Text.Json.Serialization;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>
/// The counted games themselves, one line each, for measuring how much each part of the draft model
/// predicts the winner (docs/draft-konzept.md): which champion played which lane on which side, and
/// who won, how long it ran and who led after fifteen minutes. No player, no match id, no date.
/// </summary>
public static class RiotGames
{
    public const string Format =
        "[5 × Blau, 5 × Rot, Blau gewinnt (1/0), Dauer in vollen Minuten, Gold-Vorsprung Blau nach 15 Minuten]; " +
        "je Spieler Champion × 8 + Lane + 1 (Lane −1 unbekannt, 0 Top, 1 Jungle, 2 Mid, 3 ADC, 4 Support), " +
        "je Seite nach Lane sortiert. Dauer und Gold 0, wo unbekannt.";

    /// <summary>The moment the early-game lead is read at: the end of the laning phase.</summary>
    public const long EarlyLeadAtMs = 15 * 60_000;

    /// <summary>
    /// One game as thirteen numbers (<see cref="Format"/>); null when it does not split into two
    /// sides of five. Duration and the early lead let the measurement tell early from late
    /// line-ups — what a champion is worth depends on when the game is decided.
    /// </summary>
    public static int[]? Encode(MatchReading game)
    {
        var sides = game.Participants.GroupBy(player => player.TeamId).OrderBy(side => side.Key).ToList();
        if (sides.Count != 2 || sides.Any(side => side.Count() != 5))
            return null;

        var line = new int[13];
        var index = 0;
        foreach (var side in sides)
        {
            foreach (var player in side.OrderBy(player => (int)player.Lane).ThenBy(player => player.ChampionId))
                line[index++] = player.ChampionId * 8 + (int)player.Lane + 1;
        }

        line[10] = sides[0].First().Win ? 1 : 0;
        line[11] = Math.Max(0, game.DurationSeconds) / 60;
        line[12] = (int)Math.Clamp(game.GoldLead(sides[0].Key, EarlyLeadAtMs), int.MinValue, int.MaxValue);
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
