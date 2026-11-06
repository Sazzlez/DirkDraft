using System.Text.Json;
using DraftPilot.Core.Draft;

namespace DraftPilot.Core.Data;

/// <summary>
/// What one player built in one game: the parts a build card is made of, already decided — which
/// boots, which three finished items first, which starting buy. Nothing that identifies the player:
/// no name, no Riot ID, no PUUID ever leaves <see cref="RiotMatchReader"/>.
/// </summary>
/// <param name="Runes">
/// Primary style, its four runes, secondary style, its two runes, then the three shards (offense,
/// flex, defense) — eleven ids, or empty when the game reported no page.
/// </param>
/// <param name="Spells">Both summoner spells, smaller id first: Flash + Ignite is one pair whichever key it sits on.</param>
/// <param name="Starters">What was bought before leaving base for the first time, sorted; potions count, the trinket does not.</param>
/// <param name="Boots">The second-tier boots bought first, 0 for a game without boots.</param>
/// <param name="Items">Finished items in the order they were first bought, each counted once.</param>
/// <param name="SupportChoice">The support quest's final item, 0 when there was none.</param>
/// <param name="SkillOrder">Skill points in order, one letter each (Q, W, E, R), at most eighteen.</param>
public sealed record ParticipantBuild(
    int ChampionId,
    Lane Lane,
    bool Win,
    IReadOnlyList<int> Runes,
    IReadOnlyList<int> Spells,
    IReadOnlyList<int> Starters,
    int Boots,
    IReadOnlyList<int> Items,
    int SupportChoice,
    string SkillOrder)
{
    /// <summary>
    /// The order the three basic abilities were maxed in, e.g. <c>QEW</c>: by the point at which
    /// each got its fifth rank, an ability never maxed after the ones that were, those by points.
    /// Empty when the game was too short to say.
    /// </summary>
    public string SkillMax
    {
        get
        {
            var basics = new[] { 'Q', 'W', 'E' };
            if (SkillOrder.Count(basics.Contains) < 5)
                return string.Empty;

            var maxedAt = new Dictionary<char, int>();
            var points = basics.ToDictionary(key => key, _ => 0);

            for (var index = 0; index < SkillOrder.Length; index++)
            {
                var key = SkillOrder[index];
                if (!points.ContainsKey(key))
                    continue;

                points[key]++;
                if (points[key] == 5)
                    maxedAt[key] = index;
            }

            return new string([.. basics
                .OrderBy(key => maxedAt.TryGetValue(key, out var at) ? at : int.MaxValue)
                .ThenByDescending(key => points[key])
                .ThenBy(key => Array.IndexOf(basics, key))]);
        }
    }
}

/// <summary>One ranked game, reduced to what the builds need.</summary>
/// <param name="Patch">The patch line, e.g. <c>16.22</c>, from the game's own version.</param>
/// <param name="CreatedAtMs">When the game was created, Unix milliseconds.</param>
/// <param name="Counts">
/// Whether the game says anything about builds: Solo/Duo, at least fifteen minutes, not ended by
/// an early surrender. A remake or a four-minute surrender is a game nobody finished building in.
/// </param>
public sealed record MatchReading(string Patch, long CreatedAtMs, int QueueId, bool Counts, IReadOnlyList<ParticipantBuild> Participants);

/// <summary>
/// Reads Riot's MATCH-V5 match and timeline answers into <see cref="ParticipantBuild"/>s. The match
/// knows the runes, spells, lane and result; the timeline knows the order — which is the whole
/// difference between "had these items at the end" and "built them in this order".
/// </summary>
public static class RiotMatchReader
{
    public const int RankedSoloQueue = 420;

    /// <summary>Fifteen minutes: the earliest regular surrender, and below it no build is finished.</summary>
    public const int MinimumDurationSeconds = 15 * 60;

    /// <summary>
    /// Until when a purchase counts as the starting buy. The shop closes behind a player leaving
    /// base, minions arrive at 1:05, and a recall before 1:30 is all but unheard of — so everything
    /// bought before then was bought in the fountain at the start.
    /// </summary>
    public const int StartingWindowMs = 90_000;

    /// <summary>
    /// The ten players' PUUIDs, for finding more active players of the same rank while a run lasts.
    /// Deliberately not part of <see cref="ParticipantBuild"/> or <see cref="MatchReading"/>: what
    /// is counted and stored never carries a player, and this list never leaves the collector's memory.
    /// </summary>
    public static IReadOnlyList<string> PlayerIds(string matchJson)
    {
        using var document = JsonDocument.Parse(matchJson);
        return document.RootElement.TryGetProperty("metadata", out var metadata)
            && metadata.TryGetProperty("participants", out var participants)
            && participants.ValueKind == JsonValueKind.Array
                ? [.. participants.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString() ?? string.Empty).Where(p => p.Length > 0)]
                : [];
    }

    /// <summary>Reads the match alone, enough to decide whether its timeline is worth a request.</summary>
    public static (string Patch, long CreatedAtMs, int QueueId, bool Counts) ReadHeader(string matchJson)
    {
        using var document = JsonDocument.Parse(matchJson);
        return Header(document.RootElement.GetProperty("info"));
    }

    public static MatchReading Read(string matchJson, string timelineJson, ItemCatalog items)
    {
        using var match = JsonDocument.Parse(matchJson);
        using var timeline = JsonDocument.Parse(timelineJson);

        var info = match.RootElement.GetProperty("info");
        var (patch, created, queue, counts) = Header(info);

        var events = Events(timeline.RootElement);
        var participants = new List<ParticipantBuild>();

        foreach (var participant in info.GetProperty("participants").EnumerateArray())
        {
            var lane = Lanes.FromLcu(Text(participant, "teamPosition"));
            var championId = Int(participant, "championId");
            if (lane == Lane.Unknown || championId <= 0)
                continue;

            var id = Int(participant, "participantId");
            var own = events.Where(e => e.ParticipantId == id).ToList();
            var purchases = Purchases(own);

            var finalItems = Enumerable.Range(0, 7).Select(slot => Int(participant, $"item{slot}")).Where(item => item > 0).ToList();

            participants.Add(new ParticipantBuild(
                ChampionId: championId,
                Lane: lane,
                Win: participant.TryGetProperty("win", out var win) && win.ValueKind == JsonValueKind.True,
                Runes: Runes(participant),
                Spells: [.. new[] { Int(participant, "summoner1Id"), Int(participant, "summoner2Id") }.Where(spell => spell > 0).Order()],
                Starters: [.. purchases.Where(p => p.Timestamp <= StartingWindowMs && !items.IsTrinket(p.ItemId)).Select(p => p.ItemId).Order()],
                Boots: purchases.Select(p => p.ItemId).FirstOrDefault(items.IsBoots),
                Items: [.. purchases.Select(p => p.ItemId).Where(items.IsFinished).Distinct()],
                // The quest item is handed over, not bought, so the end-of-game inventory is the
                // one place it reliably shows up.
                SupportChoice: finalItems.FirstOrDefault(items.IsSupportChoice),
                SkillOrder: SkillOrder(own)));
        }

        return new MatchReading(patch, created, queue, counts, participants);
    }

    private static (string Patch, long CreatedAtMs, int QueueId, bool Counts) Header(JsonElement info)
    {
        var patch = PatchVersion.Line(Text(info, "gameVersion"));
        var created = info.TryGetProperty("gameCreation", out var c) && c.TryGetInt64(out var ms) ? ms : 0;
        var queue = Int(info, "queueId");
        var duration = Int(info, "gameDuration");

        var earlySurrender = info.TryGetProperty("participants", out var participants)
            && participants.ValueKind == JsonValueKind.Array
            && participants.EnumerateArray().Any(p => p.TryGetProperty("gameEndedInEarlySurrender", out var e) && e.ValueKind == JsonValueKind.True);

        return (patch, created, queue, queue == RankedSoloQueue && duration >= MinimumDurationSeconds && !earlySurrender && patch.Length > 0);
    }

    /// <summary>
    /// Every purchase that stood, in order. An undo takes back the most recent purchase of that
    /// item, so a misclick in the fountain does not become a starting item.
    /// </summary>
    private static List<TimelineEvent> Purchases(List<TimelineEvent> events)
    {
        var purchases = new List<TimelineEvent>();

        foreach (var e in events)
        {
            if (e.Type == "ITEM_PURCHASED" && e.ItemId > 0)
            {
                purchases.Add(e);
            }
            else if (e.Type == "ITEM_UNDO" && e.BeforeId > 0 && e.AfterId == 0)
            {
                var last = purchases.FindLastIndex(p => p.ItemId == e.BeforeId);
                if (last >= 0)
                    purchases.RemoveAt(last);
            }
        }

        return purchases;
    }

    private static string SkillOrder(List<TimelineEvent> events)
        => new([.. events
            // EVOLVE is Kai'Sa's and Kha'Zix's upgrades — not a skill point.
            .Where(e => e.Type == "SKILL_LEVEL_UP" && e.LevelUpType == "NORMAL" && e.SkillSlot is >= 1 and <= 4)
            .Select(e => "QWER"[e.SkillSlot - 1])
            .Take(18)]);

    private static List<int> Runes(JsonElement participant)
    {
        if (!participant.TryGetProperty("perks", out var perks) || !perks.TryGetProperty("styles", out var styles))
            return [];

        var primary = styles.EnumerateArray().FirstOrDefault(s => Text(s, "description") == "primaryStyle");
        var secondary = styles.EnumerateArray().FirstOrDefault(s => Text(s, "description") == "subStyle");
        if (primary.ValueKind != JsonValueKind.Object || secondary.ValueKind != JsonValueKind.Object)
            return [];

        var primaryRunes = Selections(primary);
        var secondaryRunes = Selections(secondary);
        if (primaryRunes.Count != 4 || secondaryRunes.Count != 2)
            return [];

        var shards = perks.TryGetProperty("statPerks", out var stat)
            ? new[] { Int(stat, "offense"), Int(stat, "flex"), Int(stat, "defense") }
            : [0, 0, 0];

        return [Int(primary, "style"), .. primaryRunes, Int(secondary, "style"), .. secondaryRunes, .. shards];

        static List<int> Selections(JsonElement style)
            => style.TryGetProperty("selections", out var selections)
                ? [.. selections.EnumerateArray().Select(s => Int(s, "perk")).Where(perk => perk > 0)]
                : [];
    }

    private static List<TimelineEvent> Events(JsonElement timeline)
    {
        var list = new List<TimelineEvent>();
        if (!timeline.TryGetProperty("info", out var info) || !info.TryGetProperty("frames", out var frames))
            return list;

        foreach (var frame in frames.EnumerateArray())
        {
            if (!frame.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var e in events.EnumerateArray())
            {
                list.Add(new TimelineEvent(
                    Type: Text(e, "type"),
                    Timestamp: e.TryGetProperty("timestamp", out var t) && t.TryGetInt64(out var ms) ? ms : 0,
                    ParticipantId: Int(e, "participantId"),
                    ItemId: Int(e, "itemId"),
                    BeforeId: Int(e, "beforeId"),
                    AfterId: Int(e, "afterId"),
                    SkillSlot: Int(e, "skillSlot"),
                    LevelUpType: Text(e, "levelUpType")));
            }
        }

        // Frames are in order and so are their events, but a stable sort costs nothing and makes
        // the order a property of the data rather than of the file.
        return [.. list.OrderBy(e => e.Timestamp)];
    }

    private static int Int(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    private static string Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private sealed record TimelineEvent(
        string Type, long Timestamp, int ParticipantId, int ItemId, int BeforeId, int AfterId, int SkillSlot, string LevelUpType);
}
