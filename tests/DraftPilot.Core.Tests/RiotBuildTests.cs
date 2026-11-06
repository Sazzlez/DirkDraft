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

        var handler = new FakeHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                _ when path.StartsWith("/lol/league/v4/entries/", StringComparison.Ordinal)
                    => Ok("""[{"puuid":"p1","wins":30,"losses":30,"inactive":false},{"puuid":"p2","wins":1,"losses":1,"inactive":false}]"""),
                "/lol/match/v5/matches/by-puuid/p1/ids" => Ok("""["EUW1_100","EUW1_200","EUW1_100"]"""),
                // The players of p1's game are leads; their game is counted, its players are not.
                "/lol/match/v5/matches/by-puuid/q2/ids" => Ok("""["EUW1_300"]"""),
                _ when path.EndsWith("/ids", StringComparison.Ordinal) => Ok("[]"),
                "/lol/match/v5/matches/EUW1_100" => Ok(WithPlayers(current, "q")),
                "/lol/match/v5/matches/EUW1_300" => Ok(WithPlayers(current, "r")),
                _ when path.EndsWith("/timeline", StringComparison.Ordinal) => Ok(Timeline),
                "/lol/match/v5/matches/EUW1_200" => Ok(old),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });

        using var api = new RiotApiClient("RGAPI-test", handler, delay: (_, _) => Task.CompletedTask);
        var state = new CollectorState();
        var saves = 0;

        using var stop = new CancellationTokenSource();
        var collector = new RiotCollector(api, Items, new CollectorOptions { Tiers = ["gold"], Duration = TimeSpan.FromMinutes(1), MaxPages = 1 });
        var report = await collector.RunAsync(state, _ => saves++, stop.Token);

        Assert.Equal(2, report.Counted);                       // EUW1_100 once, however often it is listed, and the lead's EUW1_300
        Assert.Equal("16.22", state.Patch);
        Assert.Equal(2, state.Tally.Matches["gold"]);
        Assert.Contains(100L, state.Seen);
        Assert.Contains(200L, state.Seen);
        Assert.Contains(300L, state.Seen);
        Assert.True(state.OldPatchLatestMs > 0);               // the 16.16 game marks where 16.22 began
        Assert.True(saves >= 1);
        Assert.Equal(10, state.Tally.Builds.Count);

        // The player with two games this season was never asked about.
        Assert.DoesNotContain(handler.Paths, path => path.Contains("/by-puuid/p2/", StringComparison.Ordinal));

        // All ten of p1's game were asked as leads; nobody from the lead's own game was.
        Assert.Equal(10, handler.Paths.Count(path => path.Contains("/by-puuid/q", StringComparison.Ordinal)));
        Assert.DoesNotContain(handler.Paths, path => path.Contains("/by-puuid/r", StringComparison.Ordinal));

        // Nothing that names a player is in what gets stored.
        var stored = System.Text.Json.JsonSerializer.Serialize(state, CollectorJson.Default.CollectorState);
        Assert.DoesNotContain("\"p1\"", stored);
        Assert.DoesNotContain("\"q2\"", stored);
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
