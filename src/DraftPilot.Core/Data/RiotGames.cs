using System.Text.Json.Serialization;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>
/// The counted games themselves, one line each, for measuring how much each part of the draft model
/// predicts the winner (docs/draft-konzept.md): which champion played which lane on which side, and
/// who won, how long it ran and who led after fifteen minutes. Since version 2 also how each lane
/// stood after ten minutes and every duel between lane opponents — the evidence for the duel scale
/// in the game view (docs/lane-vergleich.md). Since version 3 every line has the ten-minute numbers
/// (what a matchup does to the laning phase, docs/modellpruefung.md §13) and a sample of lines the
/// duels. Since version 4 a line with its duels ends in <see cref="LevelsBeforeDuel"/>: its levels
/// are the ones going into the duel (before, the winner's level-up from the kill was counted in).
/// No player, no match id, no date.
/// </summary>
public static class RiotGames
{
    public const string Format =
        "[5 × Blau, 5 × Rot, Blau gewinnt (1/0), Dauer in vollen Minuten, Gold-Vorsprung Blau nach 15 Minuten, " +
        "Platz des befragten Spielers (0–9, −1 keiner), seine Mastery-Punkte auf dem Champion " +
        "(nur in Zeilen bis zum 13.11.2026, in jeder vierten; seitdem immer −1 und 0), " +
        "10 × Gold nach 10 Minuten, 10 × Erfahrung nach 10 Minuten (je Platz), " +
        "dann entweder −1 (die Duelle dieser Zeile nicht behalten) " +
        "oder je Lane-Duell: Länge L, danach L Zahlen — Sekunde, Platz Sieger, Platz Verlierer, " +
        "je Spieler der Lane: Platz, Level, Itemwert, Rüstung aus Items, Magieresistenz aus Items]; " +
        "je Spieler Champion × 8 + Lane + 1 (Lane −1 unbekannt, 0 Top, 1 Jungle, 2 Mid, 3 ADC, 4 Support), " +
        "je Seite nach Lane sortiert, die Plätze 0–9 in dieser Reihenfolge. Lane-Duell: Kill zwischen Lane-Gegnern " +
        "ohne fremde Hilfe (Bot: unter den vier Bot-Spielern, nur der Partner des Siegers hilft). Itemwert: Ladenpreis " +
        "der gehaltenen Items ohne Tränke, Wards, Schmuck und Support-Quest. Dauer, Gold und Erfahrung 0, wo unbekannt. " +
        "Gold und Erfahrung (ab Platz 15) trägt seit Version 3 jede Zeile mit gelesener Minute 10, die Duelle nur " +
        "ein Teil davon. Zeilen ohne Minute 10 und alle aus Version 1 enden nach 15 Zahlen, in Version 2 auch die ohne Duelle. " +
        "Seit Version 4 endet eine Zeile mit ihren Duellen auf −2: Level und Items sind der Stand unmittelbar vor dem Kill. " +
        "In Zeilen ohne diese −2 enthält das Level des Siegers (und seines Helfers) den Level-Aufstieg durch den Kill selbst.";

    /// <summary>Closes a line whose duels were not kept, so that it never reads as a game without any.</summary>
    public const int NoDuels = -1;

    /// <summary>
    /// Closes a line with its duels since version 4: the levels are the ones going into each duel.
    /// Lines without it were counted while the level a kill gives was read as the winner's level
    /// before it (docs/modellpruefung.md, Messung 27) — whoever measures levels takes only marked lines.
    /// </summary>
    public const int LevelsBeforeDuel = -2;

    /// <summary>The moment the early-game lead is read at: the end of the laning phase.</summary>
    public const long EarlyLeadAtMs = 15 * 60_000;

    /// <summary>The moment each lane's gold and experience are read at: deep in the laning phase.</summary>
    public const long LaneAtMs = 10 * 60_000;

    /// <summary>The draft part every line has: players, winner, duration, early lead, and the two places the mastery sample had.</summary>
    public const int DraftLength = 15;

    /// <summary>A line with the lane part: the draft part, gold and experience at ten minutes, then the duels.</summary>
    public const int FixedLength = 35;

    private const int GoldAtTen = 15;
    private const int XpAtTen = 25;

    /// <summary>
    /// One game as a line (<see cref="Format"/>); null when it does not split into two sides of
    /// five. Duration and the early lead let the measurement tell early from late line-ups — what a
    /// champion is worth depends on when the game is decided. Places 13 and 14 held one player's
    /// mastery until 13.11.2026 (every fourth line); the collector asks nobody any more, they stay
    /// −1 and 0 so that the lines keep their shape.
    /// </summary>
    public static int[]? Encode(MatchReading game)
    {
        var sides = game.Participants.GroupBy(player => player.TeamId).OrderBy(side => side.Key).ToList();
        if (sides.Count != 2 || sides.Any(side => side.Count() != 5))
            return null;

        var line = new int[FixedLength];
        line[13] = -1;
        var places = new Dictionary<int, int>();
        var index = 0;
        foreach (var side in sides)
        {
            foreach (var player in side.OrderBy(player => (int)player.Lane).ThenBy(player => player.ChampionId))
            {
                if (player.ParticipantId > 0)
                {
                    places.TryAdd(player.ParticipantId, index);
                    if (game.MinuteTen?.TryGetValue(player.ParticipantId, out var frame) == true)
                    {
                        line[GoldAtTen + index] = Clamp(frame.TotalGold);
                        line[XpAtTen + index] = Clamp(frame.Xp);
                    }
                }
                line[index++] = player.ChampionId * 8 + (int)player.Lane + 1;
            }
        }

        line[10] = sides[0].First().Win ? 1 : 0;
        line[11] = Math.Max(0, game.DurationSeconds) / 60;
        line[12] = Clamp(game.GoldLead(sides[0].Key, EarlyLeadAtMs));

        var result = new List<int>(line);
        foreach (var duel in game.Duels)
        {
            if (!places.TryGetValue(duel.KillerId, out var winner) || !places.TryGetValue(duel.VictimId, out var loser))
                continue;

            var lane = duel.Lane.Where(m => places.ContainsKey(m.ParticipantId)).OrderBy(m => places[m.ParticipantId]).ToList();
            result.Add(3 + 5 * lane.Count);
            result.AddRange([(int)Math.Clamp(duel.TimeMs / 1000, 0, int.MaxValue), winner, loser]);
            foreach (var m in lane)
                result.AddRange([places[m.ParticipantId], m.Level, m.ItemGold, m.Armor, m.MagicResist]);
        }

        // Only a line with duels has levels to vouch for; one without stays as long as it was.
        if (result.Count > FixedLength)
            result.Add(LevelsBeforeDuel);
        return [.. result];
    }

    /// <summary>
    /// The line as the collector keeps it. With its duels: whole. Without: the draft part and the
    /// numbers after ten minutes, closed by <see cref="NoDuels"/> — some twenty numbers more per game,
    /// where the duels would be some three hundred; the draft part alone when no minute ten was read.
    /// </summary>
    public static int[] Keep(int[] line, bool withDuels)
    {
        if (withDuels || line.Length < FixedLength)
            return line;
        for (var at = DraftLength; at < FixedLength; at++)
        {
            if (line[at] != 0)
                return [.. line[..FixedLength], NoDuels];
        }
        return line[..DraftLength];
    }

    /// <summary>Whether the line carries gold and experience after ten minutes.</summary>
    public static bool HasLanePart(int[] line) => line.Length >= FixedLength;

    /// <summary>
    /// Whether the line's duels were kept — then an empty list means a game without any. True for every
    /// version-2 line with the lane part.
    /// </summary>
    public static bool HasDuelSample(int[] line) => line.Length == FixedLength || (line.Length > FixedLength && line[FixedLength] != NoDuels);

    /// <summary>Whether the line's duels carry the levels going into them (version 4, <see cref="LevelsBeforeDuel"/>).</summary>
    public static bool HasLevelsBeforeDuel(int[] line) => line.Length > FixedLength && line[^1] == LevelsBeforeDuel;

    /// <summary>A player's number back as champion and lane.</summary>
    public static (int ChampionId, Lane Lane) Decode(int player) => (player / 8, (Lane)(player % 8 - 1));

    /// <summary>The duels of a line, each as its own numbers (second, winner, loser, the lane's players); none in a version-1 line.</summary>
    public static IEnumerable<int[]> Duels(int[] line)
    {
        for (var at = FixedLength; at < line.Length;)
        {
            var length = line[at];
            if (length <= 0 || at + 1 + length > line.Length)
                yield break;
            yield return line[(at + 1)..(at + 1 + length)];
            at += 1 + length;
        }
    }

    private static int Clamp(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
}

/// <summary>The game lines of one patch.</summary>
public sealed class PatchGames
{
    public string Patch { get; set; } = string.Empty;

    public List<int[]> Games { get; set; } = [];
}

/// <summary>
/// The game lines the collector hands to the measurement — the current patch and up to two before
/// it: an artifact of the run, never published, never read by the app.
/// </summary>
public sealed class RiotGamesFile
{
    /// <summary>
    /// 2 since 08.11.2026: lanes after ten minutes and the lane duels. 3 since 09.11.2026: the numbers
    /// after ten minutes in every line, the duels in a sample (<see cref="RiotGames.Format"/>). 4 since
    /// 13.11.2026: new lines with duels end in −2, their levels are the ones before the kill; the lines
    /// counted before stay as they are.
    /// </summary>
    public int Version { get; set; } = 4;

    public string Platform { get; set; } = string.Empty;

    public string Tier { get; set; } = string.Empty;

    public DateTimeOffset CollectedAtUtc { get; set; }

    /// <summary>How a line reads, for whoever opens the file.</summary>
    public string Format { get; set; } = RiotGames.Format;

    /// <summary>Newest first: the patch being counted, then the ones before it.</summary>
    public List<PatchGames> Patches { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(RiotGamesFile))]
public sealed partial class RiotGamesJson : JsonSerializerContext;
