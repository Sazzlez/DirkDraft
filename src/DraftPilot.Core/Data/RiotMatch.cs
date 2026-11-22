using System.Globalization;
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
/// <param name="WorthBeforeBoots">
/// What the items held were worth going into the shop visit in which the first boots of any kind were
/// bought (basic Boots or a pair): the inventory <see cref="RiotMatchReader.ShopVisitMs"/> before that
/// purchase. -1 for a game without boots or without a timeline, and for a player with free boots.
/// </param>
/// <param name="FreeBoots">
/// The player took Magical Footwear: the boots come by themselves and cannot be bought before. Such a
/// game says nothing about when boots are bought (18 % of the Platinum games of 13.11.2026, 40 % in
/// the jungle), so every boots timing leaves it out.
/// </param>
/// <param name="BootsVisitList">The shop visits without boots (<see cref="BootsVisit"/>); null without a timeline.</param>
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
    string SkillOrder,
    int TeamId = 0,
    long PhysicalDamage = 0,
    long MagicDamage = 0,
    long TrueDamage = 0,
    IReadOnlyList<int>? Augments = null,
    IReadOnlyList<long>? ItemTimesMs = null,
    long BootsTimeMs = 0,
    int ParticipantId = 0,
    int WorthBeforeBoots = -1,
    bool FreeBoots = false,
    IReadOnlyList<BootsVisit>? BootsVisitList = null)
{
    /// <summary>
    /// Every shop visit after the start in which the player had no boots yet and spent at least what
    /// Boots cost, up to and including the visit in which the first boots were bought. Empty with
    /// free boots or without a timeline.
    /// </summary>
    public IReadOnlyList<BootsVisit> BootsVisits => BootsVisitList ?? [];

    /// <summary>When each of <see cref="Items"/> was bought, ms into the game; empty without a timeline.</summary>
    public IReadOnlyList<long> ItemTimes => ItemTimesMs ?? [];

    /// <summary>
    /// How many finished items were bought before the pair of boots; -1 for a game without a pair or
    /// without a timeline (no purchase time).
    /// </summary>
    public int ItemsBeforeBoots => Boots > 0 && BootsTimeMs > 0 && !FreeBoots ? ItemTimes.Count(time => time < BootsTimeMs) : -1;

    /// <summary>
    /// The finished items and boots owned at a moment — what an enemy's inventory looked like when
    /// someone else bought. Sales are left out: a finished item is rarely sold, and the live game view
    /// sees the same picture.
    /// </summary>
    public IEnumerable<int> OwnedAt(long timeMs)
    {
        if (Boots > 0 && BootsTimeMs <= timeMs)
            yield return Boots;
        for (var i = 0; i < Items.Count && i < ItemTimes.Count; i++)
        {
            if (ItemTimes[i] <= timeMs)
                yield return Items[i];
        }
    }

    /// <summary>The augments picked, in order (Mayhem); empty elsewhere.</summary>
    public IReadOnlyList<int> PickedAugments => Augments ?? [];

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
/// <param name="Bans">
/// The champions banned in the game, each once (both teams may ban the same one); empty when the
/// match carries no ban list.
/// </param>
/// <param name="DurationSeconds">How long the game ran; 0 when unknown.</param>
/// <param name="LaneKills">The duels between lane opponents, in order; empty without a timeline.</param>
/// <param name="MinuteTen">Each player's minute frame at ten minutes (participant id → numbers); null without one.</param>
/// <param name="TeamFights">The teamfights from minute 14 on, in order; null where the timeline had no minute frames.</param>
/// <param name="Objectives">Dragons, Elder, Baron, Atakhan and souls, in order; null where the timeline had no minute frames.</param>
public sealed record MatchReading(
    string Patch,
    long CreatedAtMs,
    int QueueId,
    bool Counts,
    IReadOnlyList<ParticipantBuild> Participants,
    IReadOnlyList<int>? Bans = null,
    IReadOnlyDictionary<int, long[]>? TeamGold = null,
    int DurationSeconds = 0,
    IReadOnlyList<LaneKill>? LaneKills = null,
    IReadOnlyDictionary<int, PlayerFrame>? MinuteTen = null,
    IReadOnlyList<TeamFight>? TeamFights = null,
    IReadOnlyList<ObjectiveTaken>? Objectives = null)
{
    public IReadOnlyList<int> BannedChampions => Bans ?? [];

    public IReadOnlyList<LaneKill> Duels => LaneKills ?? [];

    /// <summary>
    /// Gold lead over the enemy team at a moment, from the timeline's minute frames (the last frame at
    /// or before it); 0 without frames.
    /// </summary>
    public long GoldLead(int teamId, long timeMs)
    {
        if (TeamGold is null || !TeamGold.TryGetValue(teamId, out var own))
            return 0;
        var enemy = TeamGold.Where(pair => pair.Key != teamId).Select(pair => pair.Value).FirstOrDefault();
        if (enemy is null || own.Length == 0)
            return 0;
        var minute = (int)Math.Clamp(timeMs / 60_000, 0, Math.Min(own.Length, enemy.Length) - 1);
        return own[minute] - enemy[minute];
    }
}

/// <summary>A player's numbers in one minute frame of the timeline.</summary>
public sealed record PlayerFrame(int Level, long TotalGold, long Xp);

/// <summary>
/// How one player stood at a moment: level, and what the items held are worth and give in armor
/// and magic resist — what the live game's scoreboard lets anyone work out as well, so the duel
/// scale can be fed the same numbers in a running game.
/// </summary>
public sealed record PlayerMoment(int ParticipantId, int Level, int ItemGold, int Armor, int MagicResist);

/// <summary>
/// One shop visit of a player who has no boots yet and spent at least the price of Boots in it — so
/// boots were a choice: what the items held were worth going in (<see cref="ItemCatalog.CountsForWorth"/>),
/// the gold spent in the visit, and whether boots of any kind were among the buys. Counted to
/// answer what players do with the gold at hand: boots, or a part of the next item
/// (docs/modellpruefung.md, Messung 19).
/// </summary>
public readonly record struct BootsVisit(int Worth, int Spent, bool Bought);

/// <summary>
/// A kill between lane opponents that nobody else took part in, with how every player of that lane
/// stood at the moment: top, jungle and mid one against one without helpers; on the bot lane among
/// the four bot players, the killer's partner the only helper allowed. Who won the duel, at which
/// level and with which items — the duel scale's evidence (docs/lane-vergleich.md).
/// </summary>
public sealed record LaneKill(long TimeMs, int KillerId, int VictimId, IReadOnlyList<PlayerMoment> Lane);

/// <summary>
/// A teamfight: kills close together in time and place with at least
/// <see cref="RiotMatchReader.FightMinimumPerSide"/> players of each team in them — the evidence for
/// the teamfight scale (docs/teamfight-skala.md).
/// </summary>
/// <param name="TimeMs">The first kill; the players stand as they did the millisecond before it.</param>
/// <param name="Victims">Everyone who died in the fight, in order; a team's kills are the other team's victims.</param>
/// <param name="TookPart">
/// Everyone seen in the fight: killers, helpers, victims, whoever hurt a victim or was hurt by one.
/// Someone who only shielded or only took damage from a survivor stays unseen; someone dead until the
/// fight's last kill is left out, even where a damage list still names him.
/// </param>
/// <param name="DeadAtStart">Who was waiting to respawn when the fight began, by Riot's death timer (<see cref="RiotMatchReader.RespawnMs"/>).</param>
/// <param name="Players">Level and items of all ten at the start, by participant id.</param>
public sealed record TeamFight(
    long TimeMs,
    IReadOnlyList<int> Victims,
    IReadOnlyList<int> TookPart,
    IReadOnlyList<int> DeadAtStart,
    IReadOnlyList<PlayerMoment> Players);

/// <summary>
/// What a monster gives a team for its fights. The numbers are the game line's
/// (<see cref="RiotGames.Format"/>): a soul is 20 plus its dragon.
/// </summary>
public enum ObjectiveKind
{
    InfernalDragon = 1,
    MountainDragon = 2,
    OceanDragon = 3,
    CloudDragon = 4,
    HextechDragon = 5,
    ChemtechDragon = 6,
    ElderDragon = 7,
    Baron = 8,
    Atakhan = 9,
    InfernalSoul = 21,
    MountainSoul = 22,
    OceanSoul = 23,
    CloudSoul = 24,
    HextechSoul = 25,
    ChemtechSoul = 26,
}

/// <summary>A monster taken, or a soul given, by a team (Riot's team id, 100 or 200).</summary>
public readonly record struct ObjectiveTaken(long TimeMs, int TeamId, ObjectiveKind Kind);

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
    /// How far back from a purchase the same shop visit reaches. In the fixture game the buys of one
    /// visit lie within six seconds of each other (Blasting Wand, then Boots two seconds later); thirty
    /// seconds cover a slow shopper and stay well short of the next recall.
    /// </summary>
    public const int ShopVisitMs = 30_000;

    /// <summary>Magical Footwear: free boots after twelve minutes, none to buy before.</summary>
    public const int MagicalFootwearRune = 8304;

    /// <summary>
    /// Teamfights count from here on: where the game view swaps the lane scale for team against team
    /// (<c>LANE_PHASE_SECONDS</c> in app/src/core/strength.ts).
    /// </summary>
    public const long FightsFromMs = 14 * 60_000;

    /// <summary>
    /// A kill belongs to a fight when it comes at most this long after the fight's last kill. In a
    /// real Platinum game (22.11.2026) the kills of one fight lay up to 13 seconds apart, the next
    /// fight began more than 20 seconds later.
    /// </summary>
    public const long FightGapMs = 15_000;

    /// <summary>
    /// …and at most this far (map units, the map is some 15.000 across) from one of its kills. A
    /// fight at Baron spread over 4.400 units in that game, each kill within 2.900 of another; a
    /// skirmish on another lane at the same time lies 5.000 and more away.
    /// </summary>
    public const int FightReach = 3_500;

    /// <summary>
    /// Smaller than this on either side is a skirmish, not a teamfight. Kept from three on, so that
    /// the measurement can still decide between three and four (docs/teamfight-skala.md).
    /// </summary>
    public const int FightMinimumPerSide = 3;

    /// <summary>Riot's base death timer per level, in seconds (League of Legends Wiki, "Death").</summary>
    private static readonly double[] BaseRespawnSeconds = [10, 10, 12, 12, 14, 16, 20, 25, 28, 32.5, 35, 37.5, 40, 42.5, 45, 47.5, 50, 52.5];

    /// <summary>
    /// How long a champion who dies at this level and moment waits to respawn: the base timer, growing
    /// from minute 15 by 0,425 % every half minute, from minute 30 by 0,3 %, from minute 45 by 1,45 %,
    /// at most +50 %. Riot does not put respawns in the timeline; this is the wiki's formula, close
    /// enough to tell who was missing when a fight began.
    /// </summary>
    public static long RespawnMs(int level, long deathMs)
    {
        var minutes = deathMs / 60_000.0;
        var growth = minutes < 15 ? 0
            : minutes < 30 ? Math.Ceiling(2 * (minutes - 15)) * 0.00425
            : minutes < 45 ? 0.1275 + Math.Ceiling(2 * (minutes - 30)) * 0.003
            : 0.2175 + Math.Ceiling(2 * (minutes - 45)) * 0.0145;
        return (long)Math.Round(BaseRespawnSeconds[Math.Clamp(level, 1, 18) - 1] * (1 + Math.Min(growth, 0.5)) * 1000);
    }

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


    /// <summary>
    /// A Riot answer as a document, or null when it is missing or not valid JSON. Riot (or the way
    /// there) now and then delivers a body cut short — one lost answer, never the end of a run.
    /// </summary>
    public static JsonDocument? TryParse(string? json)
    {
        if (json is null)
            return null;
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>As <see cref="ReadHeader"/>, or null for an answer that is not a readable match.</summary>
    public static (string Patch, long CreatedAtMs, int QueueId, bool Counts)? TryReadHeader(string matchJson)
    {
        try
        {
            return ReadHeader(matchJson);
        }
        catch (Exception ex) when (Unreadable(ex))
        {
            return null;
        }
    }

    /// <summary>As <see cref="Read"/>, or null when the match or its timeline cannot be read.</summary>
    public static MatchReading? TryRead(string matchJson, string timelineJson, ItemCatalog items)
    {
        try
        {
            return Read(matchJson, timelineJson, items);
        }
        catch (Exception ex) when (Unreadable(ex))
        {
            return null;
        }
    }

    /// <summary>Not valid JSON, or valid JSON in a shape a match never has.</summary>
    private static bool Unreadable(Exception ex)
        => ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException;

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
        var frames = Frames(timeline.RootElement);
        var participants = new List<ParticipantBuild>();

        foreach (var participant in info.GetProperty("participants").EnumerateArray())
        {
            var lane = Lanes.FromLcu(Text(participant, "teamPosition"));
            var championId = Int(participant, "championId");
            // ARAM has no lanes; everywhere else a player without one is a broken record.
            if (championId <= 0 || (lane == Lane.Unknown && !IsAram(queue)))
                continue;

            var id = Int(participant, "participantId");
            var own = events.Where(e => e.ParticipantId == id).ToList();
            var purchases = Purchases(own);
            var boots = purchases.FirstOrDefault(p => items.IsBoots(p.ItemId));
            // The first boots of any kind: basic Boots, or a pair bought in one go.
            var firstBoots = purchases.FirstOrDefault(p => p.ItemId == ItemCatalog.BasicBoots || items.IsBoots(p.ItemId));
            // First purchase of each finished item, in order, with its time.
            var finished = purchases.Where(p => items.IsFinished(p.ItemId)).DistinctBy(p => p.ItemId).ToList();

            var finalItems = Enumerable.Range(0, 7).Select(slot => Int(participant, $"item{slot}")).Where(item => item > 0).ToList();
            var runes = Runes(participant);
            var freeBoots = runes.Contains(MagicalFootwearRune);

            participants.Add(new ParticipantBuild(
                ChampionId: championId,
                Lane: lane,
                Win: participant.TryGetProperty("win", out var win) && win.ValueKind == JsonValueKind.True,
                Runes: runes,
                Spells: [.. new[] { Int(participant, "summoner1Id"), Int(participant, "summoner2Id") }.Where(spell => spell > 0).Order()],
                Starters: [.. purchases.Where(p => p.Timestamp <= StartingWindowMs && !items.IsTrinket(p.ItemId)).Select(p => p.ItemId).Order()],
                Boots: boots?.ItemId ?? 0,
                Items: [.. finished.Select(p => p.ItemId)],
                // The quest item is handed over, not bought, so the end-of-game inventory is the
                // one place it reliably shows up.
                SupportChoice: finalItems.FirstOrDefault(items.IsSupportChoice),
                SkillOrder: SkillOrder(own),
                // Riot numbers the blue side 1 to 5 and the red side 6 to 10; the explicit team id
                // wins where the match carries one.
                TeamId: Int(participant, "teamId") is > 0 and var team ? team : id <= 5 ? 100 : 200,
                PhysicalDamage: Long(participant, "physicalDamageDealtToChampions"),
                MagicDamage: Long(participant, "magicDamageDealtToChampions"),
                TrueDamage: Long(participant, "trueDamageDealtToChampions"),
                Augments: [.. Enumerable.Range(1, 6).Select(slot => Int(participant, $"playerAugment{slot}")).Where(augment => augment > 0)],
                ItemTimesMs: [.. finished.Select(p => p.Timestamp)],
                BootsTimeMs: boots?.Timestamp ?? 0,
                ParticipantId: id,
                WorthBeforeBoots: firstBoots is null || freeBoots
                    ? -1
                    : Moment(id, Math.Max(0, firstBoots.Timestamp - ShopVisitMs), events, frames, items).ItemGold,
                FreeBoots: freeBoots,
                BootsVisitList: freeBoots ? [] : BootsVisits(id, purchases, own, events, frames, items)));
        }

        const int minuteTen = (int)(RiotGames.LaneAtMs / 60_000);
        // Without minute frames there was no timeline worth the name: no fights to tell, not "none".
        var timed = frames.Any(frame => frame.Count > 0);
        return new MatchReading(
            patch, created, queue, counts, participants, Bans(info), TeamGold(timeline.RootElement, info), Int(info, "gameDuration"),
            LaneKills(events, participants, frames, items),
            frames.Count > minuteTen && frames[minuteTen].Count > 0 ? frames[minuteTen] : null,
            timed ? TeamFights(events, participants, frames, items) : null,
            timed ? Objectives(events) : null);
    }

    /// <summary>
    /// The teamfights (<see cref="TeamFight"/>). Kills join the fight they are near in time
    /// (<see cref="FightGapMs"/>) and place (<see cref="FightReach"/>); two fights at the same time on
    /// two ends of the map stay two. Kept from <see cref="FightsFromMs"/> on, with at least
    /// <see cref="FightMinimumPerSide"/> players of each team seen in the fight.
    /// </summary>
    private static List<TeamFight> TeamFights(List<TimelineEvent> events, List<ParticipantBuild> players, List<Dictionary<int, PlayerFrame>> frames, ItemCatalog items)
    {
        var teamOf = new Dictionary<int, int>();
        foreach (var player in players.Where(p => p.ParticipantId > 0))
            teamOf.TryAdd(player.ParticipantId, player.TeamId);

        var kills = events.Where(e => e.Type == "CHAMPION_KILL" && teamOf.ContainsKey(e.VictimId)).ToList();
        var clusters = new List<List<TimelineEvent>>();
        var open = new List<List<TimelineEvent>>();
        foreach (var kill in kills)
        {
            open.RemoveAll(fight => kill.Timestamp - fight[^1].Timestamp > FightGapMs);
            var home = open.Where(fight => fight.Any(other => Near(other, kill))).OrderByDescending(fight => fight[^1].Timestamp).FirstOrDefault();
            if (home is null)
            {
                home = [];
                open.Add(home);
                clusters.Add(home);
            }
            home.Add(kill);
        }

        var fights = new List<TeamFight>();
        foreach (var fight in clusters)
        {
            var start = fight[0].Timestamp;
            if (start < FightsFromMs)
                continue;

            // Who was waiting to respawn when the fight began, and until when.
            var dead = new Dictionary<int, long>();
            foreach (var id in teamOf.Keys.Order())
            {
                var death = kills.LastOrDefault(k => k.VictimId == id && k.Timestamp < start);
                var back = death is null ? 0 : death.Timestamp + RespawnMs(LevelAt(id, death.Timestamp - 1, events, frames), death.Timestamp);
                if (back > start)
                    dead[id] = back;
            }

            var seen = new HashSet<int>();
            foreach (var kill in fight)
            {
                seen.Add(kill.KillerId);
                seen.Add(kill.VictimId);
                seen.UnionWith(kill.Assists);
                seen.UnionWith(kill.Involved);
            }
            seen.IntersectWith(teamOf.Keys);
            // The damage lists reach back further than one fight: a Xayah dead for 23 seconds still
            // stood in one with her feathers (22.11.2026). Whoever was dead until the last kill was not there.
            seen.RemoveWhere(id => dead.TryGetValue(id, out var back) && back > fight[^1].Timestamp);
            var sides = seen.GroupBy(id => teamOf[id]).Select(side => side.Count()).ToList();
            if (sides.Count < 2 || sides.Any(count => count < FightMinimumPerSide))
                continue;

            fights.Add(new TeamFight(
                start,
                [.. fight.Select(kill => kill.VictimId)],
                [.. seen.Order()],
                [.. dead.Keys.Order()],
                [.. teamOf.Keys.Order().Select(id => Moment(id, start - 1, events, frames, items))]));
        }

        return fights;
    }

    /// <summary>Whether two kills are close enough for one fight; a kill without a place is near any.</summary>
    private static bool Near(TimelineEvent a, TimelineEvent b)
    {
        if (a.X < 0 || a.Y < 0 || b.X < 0 || b.Y < 0)
            return true;
        long dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy <= (long)FightReach * FightReach;
    }

    /// <summary>
    /// Dragons by kind, the Elder, Baron, Atakhan, and the souls (<see cref="ObjectiveTaken"/>). The
    /// soul event with team 0 at the third dragon only tells the map which soul it will be; it is left out.
    /// </summary>
    private static List<ObjectiveTaken> Objectives(List<TimelineEvent> events)
    {
        var taken = new List<ObjectiveTaken>();
        foreach (var e in events)
        {
            ObjectiveKind? kind = e.Type switch
            {
                "ELITE_MONSTER_KILL" => e.MonsterType switch
                {
                    "DRAGON" => e.MonsterSubType switch
                    {
                        "FIRE_DRAGON" => ObjectiveKind.InfernalDragon,
                        "EARTH_DRAGON" => ObjectiveKind.MountainDragon,
                        "WATER_DRAGON" => ObjectiveKind.OceanDragon,
                        "AIR_DRAGON" => ObjectiveKind.CloudDragon,
                        "HEXTECH_DRAGON" => ObjectiveKind.HextechDragon,
                        "CHEMTECH_DRAGON" => ObjectiveKind.ChemtechDragon,
                        "ELDER_DRAGON" => ObjectiveKind.ElderDragon,
                        _ => null,
                    },
                    "BARON_NASHOR" => ObjectiveKind.Baron,
                    "ATAKHAN" => ObjectiveKind.Atakhan,
                    _ => null,
                },
                "DRAGON_SOUL_GIVEN" => e.Name.ToUpperInvariant() switch
                {
                    "INFERNAL" => ObjectiveKind.InfernalSoul,
                    "MOUNTAIN" => ObjectiveKind.MountainSoul,
                    "OCEAN" => ObjectiveKind.OceanSoul,
                    "CLOUD" => ObjectiveKind.CloudSoul,
                    "HEXTECH" => ObjectiveKind.HextechSoul,
                    "CHEMTECH" => ObjectiveKind.ChemtechSoul,
                    _ => null,
                },
                _ => null,
            };
            if (kind is { } k && e.TeamId > 0)
                taken.Add(new ObjectiveTaken(e.Timestamp, e.TeamId, k));
        }

        return taken;
    }

    private static bool IsBot(Lane lane) => lane is Lane.Adc or Lane.Support;

    /// <summary>
    /// The kills between lane opponents nobody else took part in (<see cref="LaneKill"/>). A kill
    /// with a stranger's help — a gank, a roam, the tower — says nothing about who wins the duel.
    /// </summary>
    private static List<LaneKill> LaneKills(List<TimelineEvent> events, List<ParticipantBuild> players, List<Dictionary<int, PlayerFrame>> frames, ItemCatalog items)
    {
        var byId = new Dictionary<int, ParticipantBuild>();
        foreach (var player in players.Where(p => p.ParticipantId > 0))
            byId.TryAdd(player.ParticipantId, player);

        var kills = new List<LaneKill>();
        foreach (var kill in events.Where(e => e.Type == "CHAMPION_KILL"))
        {
            if (!byId.TryGetValue(kill.KillerId, out var killer) || !byId.TryGetValue(kill.VictimId, out var victim) || killer.TeamId == victim.TeamId)
                continue;

            List<ParticipantBuild> lane;
            if (IsBot(killer.Lane) && IsBot(victim.Lane))
            {
                var partnerOnly = kill.Assists.All(helper => helper != killer.ParticipantId
                    && byId.TryGetValue(helper, out var partner) && partner.TeamId == killer.TeamId && IsBot(partner.Lane));
                if (!partnerOnly)
                    continue;
                lane = [.. players.Where(p => p.ParticipantId > 0 && IsBot(p.Lane))];
            }
            else if (killer.Lane == victim.Lane && killer.Lane is Lane.Top or Lane.Jungle or Lane.Mid && kill.Assists.Count == 0)
            {
                lane = [killer, victim];
            }
            else
            {
                continue;
            }

            // How they stood going into the duel: the millisecond before. The level the kill itself
            // gives carries the kill's own timestamp (and is listed after it) — counted in, the winner
            // would have been a level ahead in every fifth duel only because he won it.
            kills.Add(new LaneKill(kill.Timestamp, killer.ParticipantId, victim.ParticipantId,
                [.. lane.OrderBy(p => p.ParticipantId).Select(p => Moment(p.ParticipantId, kill.Timestamp - 1, events, frames, items))]));
        }

        return kills;
    }

    /// <summary>
    /// A player's level and inventory at a moment, replayed from the timeline: the level from the
    /// last minute frame and the level-ups since, the items from purchases, sales, items used up or
    /// built into something else, and undos.
    /// </summary>
    /// <summary>
    /// The shop visits without boots (<see cref="BootsVisit"/>). A visit is a run of purchases no more
    /// than <see cref="ShopVisitMs"/> apart; the one at the start of the game is left out. What a visit
    /// cost is what the inventory gained in shop prices — a finished item counts only what was paid on
    /// top of its parts, because the timeline takes the parts away in the same instant.
    /// </summary>
    private static List<BootsVisit> BootsVisits(
        int id, List<TimelineEvent> purchases, List<TimelineEvent> own, List<TimelineEvent> events, List<Dictionary<int, PlayerFrame>> frames, ItemCatalog items)
    {
        var visits = new List<BootsVisit>();
        var bootsPrice = items.Gold(ItemCatalog.BasicBoots);
        if (bootsPrice <= 0)
            return visits;

        for (var start = 0; start < purchases.Count;)
        {
            var end = start;
            while (end + 1 < purchases.Count && purchases[end + 1].Timestamp - purchases[end].Timestamp <= ShopVisitMs)
                end++;

            var bought = false;
            for (var i = start; i <= end; i++)
                bought |= purchases[i].ItemId == ItemCatalog.BasicBoots || items.IsBoots(purchases[i].ItemId);

            var entered = purchases[start].Timestamp;
            if (entered > StartingWindowMs)
            {
                var spent = HeldGold(own, purchases[end].Timestamp, items) - HeldGold(own, entered - 1, items);
                if (spent >= bootsPrice)
                    visits.Add(new BootsVisit(Moment(id, entered - 1, events, frames, items).ItemGold, spent, bought));
            }

            // From here on the player has boots.
            if (bought)
                break;
            start = end + 1;
        }

        return visits;
    }

    /// <summary>Everything one player holds at a moment, in shop prices (potions and boots included).</summary>
    private static int HeldGold(List<TimelineEvent> own, long timeMs, ItemCatalog items)
    {
        var held = new List<int>();
        foreach (var e in own)
        {
            if (e.Timestamp > timeMs)
                break;
            Apply(held, e);
        }

        return held.Sum(items.Gold);
    }

    /// <summary>One event's effect on what a player holds.</summary>
    private static void Apply(List<int> held, TimelineEvent e)
    {
        switch (e.Type)
        {
            case "ITEM_PURCHASED" when e.ItemId > 0:
                held.Add(e.ItemId);
                break;
            case "ITEM_SOLD" or "ITEM_DESTROYED":
                held.Remove(e.ItemId);
                break;
            case "ITEM_UNDO":
                // Undoing a purchase takes the item back (before → nothing), undoing a sale
                // brings it back (nothing → after).
                if (e.BeforeId > 0)
                    held.Remove(e.BeforeId);
                if (e.AfterId > 0)
                    held.Add(e.AfterId);
                break;
        }
    }

    private static PlayerMoment Moment(int id, long timeMs, List<TimelineEvent> events, List<Dictionary<int, PlayerFrame>> frames, ItemCatalog items)
    {
        var held = new List<int>();
        foreach (var e in events)
        {
            if (e.Timestamp > timeMs)
                break;
            if (e.ParticipantId == id && e.Type != "LEVEL_UP")
                Apply(held, e);
        }

        var worth = held.Where(items.CountsForWorth).ToList();
        return new PlayerMoment(id, LevelAt(id, timeMs, events, frames), worth.Sum(items.Gold), worth.Sum(items.Armor), worth.Sum(items.MagicResist));
    }

    /// <summary>A player's level at a moment: the last minute frame's, and the level-ups since.</summary>
    private static int LevelAt(int id, long timeMs, List<TimelineEvent> events, List<Dictionary<int, PlayerFrame>> frames)
    {
        var level = 1;
        for (var minute = (int)Math.Min(timeMs / 60_000, frames.Count - 1); minute >= 0; minute--)
        {
            if (frames[minute].TryGetValue(id, out var frame) && frame.Level > 0)
            {
                level = frame.Level;
                break;
            }
        }

        foreach (var e in events)
        {
            if (e.Timestamp > timeMs)
                break;
            if (e.ParticipantId == id && e.Type == "LEVEL_UP")
                level = Math.Max(level, e.Level);
        }

        return level;
    }

    /// <summary>Each minute frame's numbers per player (participant id → level, total gold, experience).</summary>
    private static List<Dictionary<int, PlayerFrame>> Frames(JsonElement timeline)
    {
        var list = new List<Dictionary<int, PlayerFrame>>();
        if (!timeline.TryGetProperty("info", out var info) || !info.TryGetProperty("frames", out var frames) || frames.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var frame in frames.EnumerateArray())
        {
            var players = new Dictionary<int, PlayerFrame>();
            if (frame.ValueKind == JsonValueKind.Object && frame.TryGetProperty("participantFrames", out var participantFrames) && participantFrames.ValueKind == JsonValueKind.Object)
            {
                foreach (var player in participantFrames.EnumerateObject())
                {
                    if (int.TryParse(player.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && player.Value.ValueKind == JsonValueKind.Object)
                        players[id] = new PlayerFrame(Int(player.Value, "level"), Long(player.Value, "totalGold"), Long(player.Value, "xp"));
                }
            }

            list.Add(players);
        }

        return list;
    }

    /// <summary>Every champion banned in the game, once — a ban by both teams is still one ban.</summary>
    private static List<int> Bans(JsonElement info)
    {
        var bans = new List<int>();
        if (!info.TryGetProperty("teams", out var teams) || teams.ValueKind != JsonValueKind.Array)
            return bans;

        foreach (var team in teams.EnumerateArray())
        {
            if (!team.TryGetProperty("bans", out var list) || list.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var ban in list.EnumerateArray())
            {
                // -1 is "no ban" (a missed turn); anything below 1 is no champion.
                var champion = Int(ban, "championId");
                if (champion > 0 && !bans.Contains(champion))
                    bans.Add(champion);
            }
        }

        return bans;
    }

    private static long Long(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? Math.Max(0, number) : 0;

    private static (string Patch, long CreatedAtMs, int QueueId, bool Counts) Header(JsonElement info)
    {
        var patch = PatchVersion.Line(Text(info, "gameVersion"));
        var created = info.TryGetProperty("gameCreation", out var c) && c.TryGetInt64(out var ms) ? ms : 0;
        var queue = Int(info, "queueId");
        var duration = Int(info, "gameDuration");

        var earlySurrender = info.TryGetProperty("participants", out var participants)
            && participants.ValueKind == JsonValueKind.Array
            && participants.EnumerateArray().Any(p => p.TryGetProperty("gameEndedInEarlySurrender", out var e) && e.ValueKind == JsonValueKind.True);

        var longEnough = queue == RankedSoloQueue ? duration >= MinimumDurationSeconds
            : IsAram(queue) && duration >= MinimumAramSeconds;
        return (patch, created, queue, longEnough && !earlySurrender && patch.Length > 0);
    }

    /// <summary>
    /// The Howling Abyss: plain ARAM and ARAM Mayhem, as <see cref="QueueKinds"/> knows them — no lanes,
    /// short games. Riot's API hides Mayhem games (403, measured 07.11.2026), so in practice the collector
    /// counts plain ARAM as Mayhem's stand-in.
    /// </summary>
    public static bool IsAram(int queueId) => QueueKinds.FromId(queueId) is QueueKind.Aram or QueueKind.AramMayhem;

    /// <summary>
    /// Ten minutes: an ARAM game is short by design, and below this it was a remake or a stomp
    /// nobody finished building in.
    /// </summary>
    public const int MinimumAramSeconds = 10 * 60;

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

    /// <summary>
    /// Each team's total gold per minute frame. Every player is counted, also those without a lane
    /// (who are left out of the build rows): the lead is the team's, not the laners'.
    /// </summary>
    private static Dictionary<int, long[]>? TeamGold(JsonElement timeline, JsonElement info)
    {
        if (!timeline.TryGetProperty("info", out var timelineInfo) || !timelineInfo.TryGetProperty("frames", out var frames) || frames.ValueKind != JsonValueKind.Array)
            return null;

        var teamOf = new Dictionary<string, int>(StringComparer.Ordinal);
        if (info.TryGetProperty("participants", out var participants) && participants.ValueKind == JsonValueKind.Array)
        {
            foreach (var participant in participants.EnumerateArray())
            {
                var id = Int(participant, "participantId");
                var team = Int(participant, "teamId") is > 0 and var t ? t : id <= 5 ? 100 : 200;
                teamOf[id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = team;
            }
        }

        var count = frames.GetArrayLength();
        if (count == 0 || teamOf.Count == 0)
            return null;

        var gold = teamOf.Values.Distinct().ToDictionary(team => team, _ => new long[count]);
        var index = 0;
        foreach (var frame in frames.EnumerateArray())
        {
            if (frame.TryGetProperty("participantFrames", out var players) && players.ValueKind == JsonValueKind.Object)
            {
                foreach (var player in players.EnumerateObject())
                {
                    if (teamOf.TryGetValue(player.Name, out var team) && player.Value.TryGetProperty("totalGold", out var total) && total.TryGetInt64(out var value))
                        gold[team][index] += value;
                }
            }

            index++;
        }

        return gold;
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
                    LevelUpType: Text(e, "levelUpType"),
                    Level: Int(e, "level"),
                    KillerId: Int(e, "killerId"),
                    VictimId: Int(e, "victimId"),
                    Assists: e.TryGetProperty("assistingParticipantIds", out var helpers) && helpers.ValueKind == JsonValueKind.Array
                        ? [.. helpers.EnumerateArray().Where(h => h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out _)).Select(h => h.GetInt32())]
                        : [],
                    X: e.TryGetProperty("position", out var place) && place.ValueKind == JsonValueKind.Object ? Int(place, "x") : -1,
                    Y: place.ValueKind == JsonValueKind.Object ? Int(place, "y") : -1,
                    InvolvedIds: DamagePlayers(e),
                    MonsterType: Text(e, "monsterType"),
                    MonsterSubType: Text(e, "monsterSubType"),
                    // A monster's killer team, a soul's receiving team (0 when the soul is only announced).
                    TeamId: Int(e, "killerTeamId") is > 0 and var killers ? killers : Int(e, "teamId"),
                    Name: Text(e, "name")));
            }
        }

        // Frames are in order and so are their events, but a stable sort costs nothing and makes
        // the order a property of the data rather than of the file.
        return [.. list.OrderBy(e => e.Timestamp)];
    }

    /// <summary>
    /// The players in a kill's damage lists: who hurt the victim (<c>victimDamageReceived</c>, the
    /// source) and whom the victim hurt (<c>victimDamageDealt</c>, the target); 0 is a tower, a
    /// minion or a monster. Read for kills only — every other event has none.
    /// </summary>
    private static IReadOnlyList<int> DamagePlayers(JsonElement e)
    {
        if (Text(e, "type") != "CHAMPION_KILL")
            return [];

        var ids = new List<int>();
        foreach (var list in (ReadOnlySpan<string>)["victimDamageReceived", "victimDamageDealt"])
        {
            if (!e.TryGetProperty(list, out var entries) || entries.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && Int(entry, "participantId") is > 0 and var id && !ids.Contains(id))
                    ids.Add(id);
            }
        }

        return ids;
    }

    private static int Int(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    private static string Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    /// <param name="X">Where a kill or a monster fell, in map units; −1 where the event has no place.</param>
    /// <param name="InvolvedIds">A kill's players from its damage lists (<see cref="DamagePlayers(JsonElement)"/>).</param>
    /// <param name="TeamId">A monster's killer team or a soul's team; 0 for everything else.</param>
    /// <param name="Name">A soul's kind ("Cloud", …).</param>
    private sealed record TimelineEvent(
        string Type, long Timestamp, int ParticipantId, int ItemId, int BeforeId, int AfterId, int SkillSlot, string LevelUpType,
        int Level, int KillerId, int VictimId, IReadOnlyList<int> Assists,
        int X = -1, int Y = -1, IReadOnlyList<int>? InvolvedIds = null, string MonsterType = "", string MonsterSubType = "", int TeamId = 0, string Name = "")
    {
        public IReadOnlyList<int> Involved => InvolvedIds ?? [];
    }
}
