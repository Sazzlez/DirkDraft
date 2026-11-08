using System.Net;
using System.Text;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta.Riot;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The build collector, pinned against one real ranked game (EUW, 16.16, fetched 2026-11-06 and
/// stripped of every player identifier) and the item catalog of 16.22.1. The expected values were
/// read off the raw timeline by hand, not produced by the code under test.
/// </summary>
public class RiotBuildTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Riot");
    private static readonly string Match = File.ReadAllText(Path.Combine(Fixtures, "match.json"));
    private static readonly string Timeline = File.ReadAllText(Path.Combine(Fixtures, "timeline.json"));
    private static readonly ItemCatalog Items = ItemCatalog.Parse(File.ReadAllText(Path.Combine(Fixtures, "item.json")));

    private const int Kayle = 10;

    [Fact]
    public void TheCatalog_SortsBuysIntoTheirSlots()
    {
        Assert.Equal("16.22.1", Items.Version);

        // Second-tier boots are the decision; basic boots and the third tier are not.
        Assert.True(Items.IsBoots(3009));
        Assert.True(Items.IsBoots(3006));
        Assert.False(Items.IsBoots(1001));
        Assert.False(Items.IsBoots(3172));
        Assert.True(Items.IsBootsUpgrade(3172)); // Gunmetal Greaves, not even tagged Boots

        // Finished: no purchasable upgrade left and at least 1.000 gold.
        Assert.True(Items.IsFinished(3089));  // Rabadon's
        Assert.True(Items.IsFinished(3115));  // Nashor's
        Assert.True(Items.IsFinished(3004));  // Manamune
        Assert.True(Items.IsFinished(3041));  // Mejai's, 1.500 gold
        Assert.False(Items.IsFinished(1055)); // Doran's Blade, a starter
        Assert.False(Items.IsFinished(1026)); // Blasting Wand, a component
        Assert.False(Items.IsFinished(3009)); // boots are their own slot
        Assert.False(Items.IsFinished(3871)); // so is the support quest
        Assert.False(Items.IsFinished(2003)); // a potion
        Assert.False(Items.IsFinished(3340)); // the trinket

        Assert.True(Items.IsSupportChoice(3871));
        Assert.False(Items.IsSupportChoice(3867));
        Assert.True(Items.IsTrinket(3340));

        // What an inventory is worth: the shop price of what is held, without what is used up, free
        // or handed to every support.
        Assert.Equal(450, Items.Gold(1055));
        Assert.True(Items.CountsForWorth(1055));
        Assert.True(Items.CountsForWorth(3134));
        Assert.False(Items.CountsForWorth(2003)); // potion
        Assert.False(Items.CountsForWorth(2055)); // control ward
        Assert.False(Items.CountsForWorth(3340)); // trinket
        Assert.False(Items.CountsForWorth(3865)); // World Atlas …
        Assert.False(Items.CountsForWorth(3867)); // … Bounty of Worlds …
        Assert.False(Items.CountsForWorth(3871)); // … and the choice at the end
        Assert.False(Items.CountsForWorth(999999));
    }

    [Fact]
    public void AGame_IsReadIntoTenBuilds()
    {
        var reading = RiotMatchReader.Read(Match, Timeline, Items);

        Assert.Equal("16.16", reading.Patch);
        Assert.Equal(420, reading.QueueId);
        Assert.True(reading.Counts);
        Assert.Equal(10, reading.Participants.Count);
        Assert.Equal(2, reading.Participants.Count(p => p.Lane == Lane.Support));

        var kayle = reading.Participants.Single(p => p.ChampionId == Kayle);
        Assert.Equal(Lane.Top, kayle.Lane);
        Assert.True(kayle.Win);
        Assert.Equal([8000, 8005, 9101, 9104, 8299, 8200, 8234, 8236, 5005, 5008, 5001], kayle.Runes);
        Assert.Equal([4, 12], kayle.Spells);
        Assert.Equal([1055, 2003], kayle.Starters);  // the trinket bought at 0:15 is not a starter
        Assert.Equal(3009, kayle.Boots);
        Assert.Equal([3115, 2510, 3089], kayle.Items); // Sheen, Tome and the rods are components
        Assert.Equal(0, kayle.SupportChoice);
        Assert.Equal("EQWQQRQEEQREEWWRW", kayle.SkillOrder);
        Assert.Equal("QEW", kayle.SkillMax);
    }

    [Fact]
    public void ASupportsQuestChoice_ComesFromTheEndOfGameInventory()
    {
        var reading = RiotMatchReader.Read(Match, Timeline, Items);

        Assert.Equal(3871, reading.Participants.Single(p => p.ChampionId == 99).SupportChoice);  // Brand: Zaz'Zak's
        Assert.Equal(3877, reading.Participants.Single(p => p.ChampionId == 80).SupportChoice);  // Pantheon: Bloodsong
    }

    [Fact]
    public void AnUndoneBuy_IsNoBuy()
    {
        var timeline = """
            {"info":{"frames":[{"events":[
              {"type":"ITEM_PURCHASED","timestamp":10000,"participantId":1,"itemId":1056},
              {"type":"ITEM_UNDO","timestamp":11000,"participantId":1,"beforeId":1056,"afterId":0},
              {"type":"ITEM_PURCHASED","timestamp":12000,"participantId":1,"itemId":1055},
              {"type":"ITEM_PURCHASED","timestamp":13000,"participantId":1,"itemId":2003}
            ]}]}}
            """;

        var kayle = RiotMatchReader.Read(Match, timeline, Items).Participants.Single(p => p.ChampionId == Kayle);

        Assert.Equal([1055, 2003], kayle.Starters);
    }

    [Theory]
    [InlineData(420, 1591, true)]
    [InlineData(440, 1591, false)]  // Flex
    [InlineData(420, 899, false)]   // under fifteen minutes
    public void OnlyFinishedSoloDuoGames_Count(int queue, int duration, bool counts)
    {
        var match = Match
            .Replace("\"queueId\":  420", $"\"queueId\":  {queue}")
            .Replace("\"gameDuration\":  1591", $"\"gameDuration\":  {duration}");

        Assert.Equal(counts, RiotMatchReader.ReadHeader(match).Counts);
    }

    [Fact]
    public void TheTally_CountsEachSlotWithItsResult()
    {
        var reading = RiotMatchReader.Read(Match, Timeline, Items);
        var tally = new RiotBuildTally();
        foreach (var participant in reading.Participants)
            tally.Add(participant, "gold");

        var kayle = tally.Export(keep: 10).Single(s => s.ChampionId == Kayle);
        Assert.Equal(1, kayle.Games);
        Assert.Equal(1, kayle.Wins);
        Assert.Equal([3115, 2510, 3089], kayle.Core.Single().Ids);
        Assert.Empty(kayle.Fourth);  // a game with three items casts no vote on the fourth
        Assert.Equal("EQWQQRQEEQREEWW", kayle.SkillOrder.Single().Text);
        Assert.Equal("QEW", kayle.SkillMax.Single().Text);

        // Counts stored and read back add up as if counted in one go, which is what a second run does.
        var again = new RiotBuildTally();
        again.Add(tally.Export(keep: 10));
        again.Add(tally.Export(keep: 10));
        var twice = again.Export(keep: 10).Single(s => s.ChampionId == Kayle);
        Assert.Equal(2, twice.Games);
        Assert.Equal(2, twice.Core.Single().Games);
        Assert.Equal(2, twice.Core.Single().Wins);
    }

    [Fact]
    public void TheExport_KeepsTheMostPlayedAndDropsTheThin()
    {
        var tally = new RiotBuildTally();
        void Play(int boots, bool win) => tally.Add(new ParticipantBuild(1, Lane.Top, win, [], [], [], boots, [], 0, string.Empty), "gold");

        for (var i = 0; i < 5; i++)
            Play(3047, i < 3);
        for (var i = 0; i < 2; i++)
            Play(3111, true);
        Play(3006, false);

        var boots = tally.Export(keep: 2, minimumGames: 2).Single().Boots;

        Assert.Equal([3047, 3111], boots.Select(b => b.Ids[0]));
        Assert.Equal(3, boots[0].Wins);
    }

    [Fact]
    public void RequestsAreGroupedByEndpoint_ForTheirMethodLimits()
    {
        Assert.Equal("europe:lol/match/v5/matches/*", RiotApiClient.MethodKey("europe", "/lol/match/v5/matches/EUW1_1"));
        Assert.Equal("europe:lol/match/v5/matches/*/timeline", RiotApiClient.MethodKey("europe", "/lol/match/v5/matches/EUW1_1/timeline"));
        Assert.Equal("europe:lol/match/v5/matches/by-puuid/*/ids", RiotApiClient.MethodKey("europe", "/lol/match/v5/matches/by-puuid/abc/ids?count=20"));
        Assert.Equal("euw1:lol/league/v4/entries", RiotApiClient.MethodKey("euw1", "/lol/league/v4/entries/RANKED_SOLO_5x5/GOLD/I?page=3"));
    }

    [Fact]
    public async Task TheClient_KeepsBelowTheStatedLimitByWaiting()
    {
        var now = DateTimeOffset.UnixEpoch;
        var waits = new List<TimeSpan>();
        var handler = new FakeHandler(_ => Ok("[]", appLimit: "3:10"));

        using var api = new RiotApiClient(
            "RGAPI-test",
            handler,
            clock: () => now,
            delay: (wait, _) => { waits.Add(wait); now += wait; return Task.CompletedTask; });

        // One below the stated three per ten seconds: two go at once, the third waits for the window.
        await api.GetAsync("europe", "/lol/match/v5/matches/EUW1_1", CancellationToken.None);
        await api.GetAsync("europe", "/lol/match/v5/matches/EUW1_2", CancellationToken.None);
        Assert.Empty(waits);

        await api.GetAsync("europe", "/lol/match/v5/matches/EUW1_3", CancellationToken.None);
        Assert.InRange(waits.Sum(w => w.TotalSeconds), 10, 10.2);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task ATooManyRequests_IsWaitedOutAsRiotSays()
    {
        var waits = new List<TimeSpan>();
        var first = true;
        var handler = new FakeHandler(_ =>
        {
            if (!first)
                return Ok("{\"ok\":1}");

            first = false;
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });

        using var api = new RiotApiClient("RGAPI-test", handler, delay: (wait, _) => { waits.Add(wait); return Task.CompletedTask; });

        Assert.Equal("{\"ok\":1}", await api.GetAsync("europe", "/x", CancellationToken.None));
        Assert.Equal(1, api.Throttled);
        Assert.Contains(waits, wait => wait >= TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task ARejectedKey_StopsWithASentenceAndNeverTheKey()
    {
        using var api = new RiotApiClient("RGAPI-secret", new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var error = await Assert.ThrowsAsync<RiotApiException>(() => api.GetAsync("europe", "/x", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, error.Status);
        Assert.DoesNotContain("RGAPI", error.Message);
    }

    [Fact]
    public async Task ACollectorRun_CountsCurrentGamesOnceAndLearnsWhereThePatchBegan()
    {
        static string WithPlayers(string match, string prefix) => match.Replace(
            "\"matchId\":  \"EUW1_1\"",
            "\"matchId\":  \"EUW1_1\", \"participants\": [" + string.Join(',', Enumerable.Range(1, 10).Select(i => $"\"{prefix}{i}\"")) + "]");

        var current = Match.Replace("\"gameVersion\":  \"16.16.804.9184\"", "\"gameVersion\":  \"16.22.123.4567\"");
        var old = Match.Replace("\"gameCreation\":  ", "\"gameCreation\":  1");  // an older patch, created whenever
        var masteryRefused = false;

        var handler = new FakeHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                _ when path.StartsWith("/lol/league/v4/entries/", StringComparison.Ordinal)
                    => Ok("""[{"puuid":"p1","wins":30,"losses":30,"inactive":false},{"puuid":"p2","wins":1,"losses":1,"inactive":false}]"""),
                "/lol/match/v5/matches/by-puuid/p1/ids" => Ok("""["EUW1_100","EUW1_200","EUW1_100","EUW1_400"]"""),
                // A body cut short on the way (as Riot delivered once on 08.11.): skipped, the run goes on.
                "/lol/match/v5/matches/EUW1_400" => Ok(current[..^40]),
                // The players of p1's game are leads; their game is counted, its players are not.
                "/lol/match/v5/matches/by-puuid/q2/ids" => Ok("""["EUW1_300"]"""),
                _ when path.EndsWith("/ids", StringComparison.Ordinal) => Ok("[]"),
                "/lol/match/v5/matches/EUW1_100" => Ok(WithPlayers(current, "q")),
                "/lol/match/v5/matches/EUW1_300" => Ok(WithPlayers(current, "r")),
                _ when path.EndsWith("/timeline", StringComparison.Ordinal) => Ok(Timeline),
                "/lol/match/v5/matches/EUW1_200" => Ok(old),
                _ when path.StartsWith("/lol/champion-mastery/v4/champion-masteries/by-puuid/r", StringComparison.Ordinal)
                    => masteryRefused ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Ok("""{"championLevel":5,"championPoints":4321}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });

        using var api = new RiotApiClient("RGAPI-test", handler, delay: (_, _) => Task.CompletedTask);
        var state = new CollectorState();
        var saves = 0;

        using var stop = new CancellationTokenSource();
        var collector = new RiotCollector(api, Items, new CollectorOptions { Tiers = ["gold"], Duration = TimeSpan.FromMinutes(1), MaxPages = 1, MasteryEvery = 2 });
        var report = await collector.RunAsync(state, _ => saves++, stop.Token);

        Assert.Equal(2, report.Counted);                       // EUW1_100 once, however often it is listed, and the lead's EUW1_300
        Assert.Equal("16.22", state.Patch);
        Assert.Equal(2, state.Tally.Matches["gold"]);
        Assert.Contains(100L, state.Seen);
        Assert.Contains(200L, state.Seen);
        Assert.Contains(300L, state.Seen);
        Assert.Contains(400L, state.Seen);                     // looked at once, unreadable, not asked again
        Assert.True(state.OldPatchLatestMs > 0);               // the 16.16 game marks where 16.22 began
        Assert.True(saves >= 1);
        Assert.Equal(10, state.Tally.Builds.Count);
        Assert.Equal(2, state.Meta.Matches);
        Assert.Equal(2, state.Games.Count);                     // one line per counted game, for measuring the model
        // The first line keeps the lane part (every 8th does; the fixture's timeline has no frames and
        // no kills, so it is just the empty part), the second ends after the draft.
        Assert.Equal([RiotGames.FixedLength, RiotGames.DraftLength], state.Games.Select(line => line.Length));
        // The second counted game (the lead's) had one player asked about, by his place in the line.
        Assert.Equal(-1, state.Games[0][13]);
        Assert.InRange(state.Games[1][13], 0, 9);
        Assert.Equal(4321, state.Games[1][14]);
        Assert.Single(handler.Paths, path => path.Contains("/champion-mastery/", StringComparison.Ordinal));
        Assert.NotEmpty(state.Matchups);

        // The player with two games this season was never asked about.
        Assert.DoesNotContain(handler.Paths, path => path.Contains("/matches/by-puuid/p2/", StringComparison.Ordinal));

        // All ten of p1's game were asked as leads; nobody from the lead's own game was.
        Assert.Equal(10, handler.Paths.Count(path => path.Contains("/matches/by-puuid/q", StringComparison.Ordinal)));
        Assert.DoesNotContain(handler.Paths, path => path.Contains("/matches/by-puuid/r", StringComparison.Ordinal));

        // Nothing that names a player is in what gets stored.
        var stored = System.Text.Json.JsonSerializer.Serialize(state, CollectorJson.Default.CollectorState);
        Assert.DoesNotContain("\"p1\"", stored);
        Assert.DoesNotContain("\"q2\"", stored);
        Assert.DoesNotContain("\"r1\"", stored);

        // A refused mastery request costs the sample, never the run.
        masteryRefused = true;
        var second = new CollectorState();
        using var secondApi = new RiotApiClient("RGAPI-test", handler, delay: (_, _) => Task.CompletedTask);
        var secondReport = await new RiotCollector(secondApi, Items, new CollectorOptions { Tiers = ["gold"], Duration = TimeSpan.FromMinutes(1), MaxPages = 1, MasteryEvery = 1, LaneEvery = 1, LaneGamesPerPatch = 1 })
            .RunAsync(second, _ => { }, stop.Token);
        Assert.Equal(2, secondReport.Counted);
        Assert.All(second.Games, line => Assert.Equal(-1, line[13]));
        // Every line would keep the lane part, but the patch's share is one.
        Assert.Equal([RiotGames.FixedLength, RiotGames.DraftLength], second.Games.Select(line => line.Length));
    }

    [Fact]
    public void ANewPatch_KeepsTheGameLinesOfTheLastThreePatches()
    {
        static List<int[]> Lines(int count) => [.. Enumerable.Range(0, count).Select(i => new int[13])];
        var state = new CollectorState
        {
            Patch = "16.21",
            Platform = "euw1",
            Meta = new RiotMetaCounts { Matches = 3 },
            Games = Lines(3),
            OlderGames = [new PatchGames { Patch = "16.20", Games = Lines(2) }, new PatchGames { Patch = "16.19", Games = Lines(1) }],
        };

        state.StartPatch("16.22", "euw1");

        Assert.Equal("16.22", state.Patch);
        Assert.Empty(state.Games);
        Assert.Equal(["16.21", "16.20"], state.OlderGames.Select(older => older.Patch));   // 16.19 falls out
        Assert.Equal(3, state.OlderGames[0].Games.Count);
        Assert.Equal("16.21", state.Previous!.Patch);

        // Another region is another population: nothing of it is kept.
        state.Games = Lines(4);
        state.StartPatch("16.22", "na1");
        Assert.Empty(state.OlderGames);
        Assert.Null(state.Previous);
    }

    [Fact]
    public void UnreadableAnswers_AreNullNotAnException()
    {
        Assert.Null(RiotMatchReader.TryParse("""{"info": {"gameDuration": 1"""));
        Assert.Null(RiotMatchReader.TryParse(null));
        Assert.Null(RiotMatchReader.TryReadHeader(Match[..^40]));
        Assert.Null(RiotMatchReader.TryReadHeader("""{"metadata": {}}"""));   // valid JSON, no match
        Assert.NotNull(RiotMatchReader.TryReadHeader(Match));
        Assert.Null(RiotMatchReader.TryRead(Match, Timeline[..^40], Items));
        Assert.NotNull(RiotMatchReader.TryRead(Match, Timeline, Items));
        Assert.Null(RiotMatchReader.MasteryPoints("""{"championPoints": 12"""));
    }

    // Prices and stats for the duel test; the item fixture carries no stats.
    private static readonly ItemCatalog DuelItems = ItemCatalog.Parse("""
        {"version":"16.22.1","data":{
          "1055":{"name":"Doran's Blade","gold":{"total":450,"purchasable":true},"tags":["Damage","Lane"]},
          "1054":{"name":"Doran's Shield","gold":{"total":450,"purchasable":true},"tags":["Health","Lane"]},
          "2003":{"name":"Health Potion","gold":{"total":50,"purchasable":true},"tags":["Consumable"]},
          "1029":{"name":"Cloth Armor","gold":{"total":300,"purchasable":true},"tags":["Armor"],"stats":{"FlatArmorMod":15}},
          "1033":{"name":"Null-Magic Mantle","gold":{"total":400,"purchasable":true},"tags":["SpellBlock"],"stats":{"FlatSpellBlockMod":25}},
          "1036":{"name":"Long Sword","gold":{"total":350,"purchasable":true},"tags":["Damage"],"into":["3134"]},
          "3134":{"name":"Serrated Dirk","gold":{"total":1000,"purchasable":true},"tags":["Damage"],"from":["1036","1036"]},
          "3865":{"name":"World Atlas","gold":{"total":400,"purchasable":true},"tags":["GoldPer"]}
        }}
        """);

    /// <summary>
    /// The real game's ten players (1 top, 2 jungle, 3 mid, 4 adc, 5 support; 6–10 the same on red)
    /// with a timeline written for the case: minute frames at 0, 5 and 10, and every kind of kill.
    /// </summary>
    private static string DuelTimeline()
    {
        static string Frame(int level, Func<int, int>? gold = null, Func<int, int>? xp = null, Func<int, int>? levels = null)
            => "{" + string.Join(",", Enumerable.Range(1, 10).Select(id =>
                $"\"{id}\":{{\"participantId\":{id},\"level\":{levels?.Invoke(id) ?? level},\"totalGold\":{gold?.Invoke(id) ?? 500},\"currentGold\":0,\"xp\":{xp?.Invoke(id) ?? 0}}}")) + "}";
        static string Buy(long t, int id, int item) => $"{{\"type\":\"ITEM_PURCHASED\",\"timestamp\":{t},\"participantId\":{id},\"itemId\":{item}}}";
        static string Gone(long t, int id, int item, string type = "ITEM_DESTROYED") => $"{{\"type\":\"{type}\",\"timestamp\":{t},\"participantId\":{id},\"itemId\":{item}}}";
        static string Up(long t, int id, int level) => $"{{\"type\":\"LEVEL_UP\",\"timestamp\":{t},\"participantId\":{id},\"level\":{level}}}";
        static string Kill(long t, int killer, int victim, params int[] helpers)
            => $"{{\"type\":\"CHAMPION_KILL\",\"timestamp\":{t},\"killerId\":{killer},\"victimId\":{victim}"
               + (helpers.Length > 0 ? $",\"assistingParticipantIds\":[{string.Join(",", helpers)}]" : "") + "}";

        var events = new[]
        {
            Buy(0, 0, 3865), Buy(4000, 1, 1055), Buy(4000, 1, 2003), Buy(5000, 6, 1054), Buy(5000, 4, 1055), Buy(5000, 9, 1055),
            Up(100000, 1, 2), Up(110000, 6, 2), Up(150000, 1, 3), Buy(170000, 1, 1029),
            Kill(200000, 1, 6),            // A: top, alone
            Kill(210000, 6, 1, 7),         // the red jungler helped: a gank
            Kill(220000, 3, 6),            // mid kills top: no lane duel
            Kill(230000, 0, 8),            // executed by the tower
            Gone(250000, 5, 3865),         // the quest item was never bought by 5: nothing to take away
            Kill(300000, 4, 9, 5),         // B: bot, the partner helped
            Kill(310000, 10, 5, 7),        // bot with the jungler's help
            Kill(320000, 2, 7),            // C: jungle, alone
            Gone(400000, 1, 1055, "ITEM_SOLD"),
            Buy(450000, 6, 1036), "{\"type\":\"ITEM_UNDO\",\"timestamp\":451000,\"participantId\":6,\"beforeId\":1036,\"afterId\":0}",
            Buy(460000, 6, 1036), Buy(461000, 6, 1036), Buy(470000, 6, 3134), Gone(470000, 6, 1036), Gone(470000, 6, 1036),
            Buy(480000, 1, 1033),
            Kill(500000, 6, 1),            // D: top again, after a sale, an undo and a finished component
        };

        var frames = Enumerable.Range(0, 11).Select(minute =>
        {
            var participants = minute switch
            {
                0 => Frame(1),
                5 => Frame(4, levels: id => id is 5 or 10 ? 3 : 4),
                10 => Frame(7, gold: id => 3000 + 10 * id, xp: id => 4000 + id),
                _ => null,
            };
            return $"{{\"timestamp\":{minute * 60_000 + 10}"
                + (participants is null ? "" : $",\"participantFrames\":{participants}")
                + $",\"events\":[{(minute == 0 ? string.Join(",", events) : "")}]}}";
        });
        return $"{{\"metadata\":{{}},\"info\":{{\"frameInterval\":60000,\"frames\":[{string.Join(",", frames)}]}}}}";
    }

    [Fact]
    public void LaneDuels_AreKillsBetweenLaneOpponentsWithoutStrangers()
    {
        var reading = RiotMatchReader.Read(Match, DuelTimeline(), DuelItems);

        static string Said(LaneKill kill) => $"{kill.TimeMs / 1000}: {kill.KillerId}>{kill.VictimId} " + string.Join(" ", kill.Lane.Select(m => $"[{m.ParticipantId} L{m.Level} {m.ItemGold}g {m.Armor}a {m.MagicResist}mr]"));
        Assert.Equal(
            [
                // Top: 1 has Doran's Blade and Cloth (the potion is used up, it counts nothing), level 3
                // from the level-ups; 6 has Doran's Shield, level 2.
                "200: 1>6 [1 L3 750g 15a 0mr] [6 L2 450g 0a 0mr]",
                // Bot: all four, levels from the frame at 5:00; the supports' quest item counts nothing.
                "300: 4>9 [4 L4 450g 0a 0mr] [5 L3 0g 0a 0mr] [9 L4 450g 0a 0mr] [10 L3 0g 0a 0mr]",
                "320: 2>7 [2 L4 0g 0a 0mr] [7 L4 0g 0a 0mr]",
                // 1 sold Doran's Blade and bought a Mantle; 6's undone Long Sword is gone, the two
                // others went into the Serrated Dirk.
                "500: 6>1 [1 L4 700g 15a 25mr] [6 L4 1450g 0a 0mr]",
            ],
            reading.Duels.Select(Said));
        Assert.Equal(new PlayerFrame(7, 3010, 4001), reading.MinuteTen![1]);

        var line = RiotGames.Encode(reading)!;
        Assert.Equal((3010, 4001), (line[15], line[25]));          // top blue is place 0 …
        Assert.Equal((3060, 4006), (line[20], line[30]));          // … top red place 5
        var duels = RiotGames.Duels(line).ToList();
        Assert.Equal(4, duels.Count);
        Assert.Equal([200, 0, 5, 0, 3, 750, 15, 0, 5, 2, 450, 0, 0], duels[0]);
        Assert.Equal([300, 3, 8, 3, 4, 450, 0, 0, 4, 3, 0, 0, 0, 8, 4, 450, 0, 0, 9, 3, 0, 0, 0], duels[1]);
        Assert.Equal(RiotGames.FixedLength + 4 + 13 + 23 + 13 + 13, line.Length);
    }

    private static HttpResponseMessage Ok(string body, string? appLimit = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (appLimit is not null)
            response.Headers.Add("X-App-Rate-Limit", appLimit);
        return response;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(respond(request));
        }
    }
}
