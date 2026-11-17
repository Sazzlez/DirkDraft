using System.Net;
using System.Text;
using DraftPilot.Core.Data;
using DraftPilot.Meta.Riot;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// A request Riot fails on costs one game, not the run (CollectorFailures): until 17.11.2026 a single
/// 400 or six 5xx in a row ended a run of four and a half hours with exit code 1. The run still ends
/// on a rejected key and on an outage — and keeps what it had counted.
/// </summary>
public sealed class CollectorFailureTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Riot");
    private static readonly ItemCatalog Items = ItemCatalog.Parse(File.ReadAllText(Path.Combine(Fixtures, "item.json")));
    private static readonly string Timeline = File.ReadAllText(Path.Combine(Fixtures, "timeline.json"));

    /// <summary>The fixture game, on the patch of the item catalog.</summary>
    private static readonly string Game = File.ReadAllText(Path.Combine(Fixtures, "match.json"))
        .Replace("\"gameVersion\":  \"16.16.804.9184\"", "\"gameVersion\":  \"16.22.123.4567\"");

    private const string League = """[{"puuid":"p1","wins":30,"losses":30,"inactive":false}]""";

    private static CollectorOptions Options => new() { Tiers = ["gold"], Duration = TimeSpan.FromMinutes(1), MaxPages = 1 };

    /// <summary>One league page with one player; his games answer as <paramref name="games"/> says, by match number.</summary>
    private static FakeHandler Ranked(string ids, Func<int, HttpResponseMessage?> games) => new(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.StartsWith("/lol/league/v4/entries/", StringComparison.Ordinal))
            return Ok(League);
        if (path.EndsWith("/ids", StringComparison.Ordinal))
            return Ok(ids);
        if (path.EndsWith("/timeline", StringComparison.Ordinal))
            return Ok(Timeline);

        var number = int.Parse(path[(path.LastIndexOf('_') + 1)..]);
        return games(number) ?? Ok(Game);
    });

    private static RiotApiClient Client(HttpMessageHandler handler) => new("RGAPI-test", handler, delay: (_, _) => Task.CompletedTask);

    [Fact]
    public async Task AGameRiotFailsOn_IsSkipped_AndTheOthersAreCounted()
    {
        // A 400 on the first game, a 500 on every try of the second; the third is fine.
        var handler = Ranked("""["EUW1_100","EUW1_500","EUW1_300"]""", number => number switch
        {
            100 => new HttpResponseMessage(HttpStatusCode.BadRequest),
            500 => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            _ => null,
        });
        using var api = Client(handler);
        var state = new CollectorState();
        var lines = new List<string>();

        var report = await new RiotCollector(api, Items, Options, lines.Add).RunAsync(state, _ => { }, CancellationToken.None);

        Assert.Equal(1, report.Counted);
        Assert.Equal(2, report.Failed);
        Assert.Contains("500", report.LastFailure);
        Assert.Equal(1, state.Tally.Matches["gold"]);
        // The failed games are not asked again by a later run, and each failure is said once.
        Assert.Contains(100L, state.Seen);
        Assert.Contains(500L, state.Seen);
        Assert.Equal(2, lines.Count(line => line.StartsWith("Übersprungen", StringComparison.Ordinal)));
        // Six tries before a 5xx counts as failed, one for a 400.
        Assert.Equal(6, handler.Paths.Count(path => path.EndsWith("EUW1_500", StringComparison.Ordinal)));
        Assert.Equal(1, handler.Paths.Count(path => path.EndsWith("EUW1_100", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task NoAnswerAtAll_IsAFailureLikeAny_NotANetworkExceptionThatEndsTheRun()
    {
        var handler = Ranked("""["EUW1_100","EUW1_300"]""", number => number == 100 ? throw new HttpRequestException("connection reset") : null);
        using var api = Client(handler);

        var report = await new RiotCollector(api, Items, Options).RunAsync(new CollectorState(), _ => { }, CancellationToken.None);

        Assert.Equal(1, report.Counted);
        Assert.Equal(1, report.Failed);
        Assert.Contains("antwortet nicht", report.LastFailure);
    }

    [Fact]
    public async Task AForbiddenGame_IsSkipped_AForbiddenList_EndsTheRun()
    {
        // Riot hides a few games behind a 403; the key is fine.
        using var api = Client(Ranked("""["EUW1_100","EUW1_300"]""", number => number == 100 ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null));
        var report = await new RiotCollector(api, Items, Options).RunAsync(new CollectorState(), _ => { }, CancellationToken.None);
        Assert.Equal((1, 1), (report.Counted, report.Failed));

        // A 403 on a league page or a match list is the key itself.
        using var blocked = Client(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var error = await Assert.ThrowsAsync<RiotApiException>(
            () => new RiotCollector(blocked, Items, Options).RunAsync(new CollectorState(), _ => { }, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, error.Status);
        Assert.Contains("lehnt den Schlüssel ab", error.Message);
    }

    [Fact]
    public async Task AnUnauthorized_EndsTheRunWithTheSameSentence_AndKeepsWhatWasCounted()
    {
        using var api = Client(Ranked("""["EUW1_100","EUW1_300"]""", number => number == 300 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : null));
        var state = new CollectorState();
        var saves = 0;

        var error = await Assert.ThrowsAsync<RiotApiException>(
            () => new RiotCollector(api, Items, Options).RunAsync(state, _ => saves++, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, error.Status);
        Assert.Equal("Riot lehnt den Schlüssel ab (401) — abgelaufen, gesperrt oder falsch eingetragen.", error.Message);
        Assert.DoesNotContain("RGAPI", error.Message);
        // The game counted before the key failed is in the saved state.
        Assert.Equal(1, saves);
        Assert.Equal(1, state.Tally.Matches["gold"]);
        Assert.Equal(1, state.Meta.Matches);
    }

    [Fact]
    public async Task TwentyFailuresInARow_AreAnOutage()
    {
        var ids = "[" + string.Join(',', Enumerable.Range(1, 30).Select(i => $"\"EUW1_{i}\"")) + "]";
        var handler = Ranked(ids, _ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var api = Client(handler);

        var error = await Assert.ThrowsAsync<RiotApiException>(
            () => new RiotCollector(api, Items, Options).RunAsync(new CollectorState(), _ => { }, CancellationToken.None));

        Assert.Contains("20 Abrufe in Folge", error.Message);
        Assert.Equal(20, handler.Paths.Count(path => path.Contains("/matches/EUW1_", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnAnswerInBetween_StartsTheCountOfFailuresOver()
    {
        var failures = new CollectorFailures();
        var failure = new RiotApiException("Riot antwortet 400 auf lol/match/v5/matches/*.", HttpStatusCode.BadRequest);

        for (var i = 0; i < CollectorFailures.DefaultLimit - 1; i++)
            failures.Failed(failure, game: true);
        failures.Succeeded();
        for (var i = 0; i < CollectorFailures.DefaultLimit - 1; i++)
            failures.Failed(failure, game: true);

        Assert.Equal(2 * (CollectorFailures.DefaultLimit - 1), failures.Count);
        Assert.Throws<RiotApiException>(() => failures.Failed(failure, game: true));
    }

    // ---- ARAM: the same rule ------------------------------------------------------------------

    private static readonly string AramGame =
        """{"metadata":{"participants":[]},"info":{"gameVersion":"16.22.1.1","gameCreation":1,"gameDuration":900,"queueId":450,"participants":["""
        + string.Join(',', Enumerable.Range(1, 10).Select(i =>
            "{\"participantId\":" + i + ",\"championId\":" + (i + 6) + ",\"teamPosition\":\"\",\"win\":" + (i <= 5 ? "true" : "false") + "}"))
        + "]}}";

    private static FakeHandler Aram(string ids, Func<int, HttpResponseMessage?> games) => new(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.StartsWith("/lol/league/v4/entries/", StringComparison.Ordinal))
            return Ok(League);
        if (path.EndsWith("/ids", StringComparison.Ordinal))
            return Ok(ids);
        if (path.EndsWith("/timeline", StringComparison.Ordinal))
            return Ok("""{"info":{"frames":[]}}""");

        var number = int.Parse(path[(path.LastIndexOf('_') + 1)..]);
        return games(number) ?? Ok(AramGame);
    });

    [Fact]
    public async Task TheAramRun_SkipsAFailedGame_AndEndsOnARejectedKey()
    {
        // The ARAM run has no page limit: with one player on every page it asks on until its time is up.
        var options = new AramOptions { Tiers = ["gold"], Duration = TimeSpan.FromMilliseconds(400) };

        using var api = Client(Aram("""["EUW1_100","EUW1_300"]""", number => number == 100 ? new HttpResponseMessage(HttpStatusCode.BadRequest) : null));
        var state = new AramState();
        var report = await new AramCollector(api, Items, options).RunAsync(state, _ => { }, CancellationToken.None);
        Assert.Equal((1, 1), (report.Counted, report.Failed));
        Assert.Equal(1, state.Tally.Games);

        using var rejected = Client(Aram("""["EUW1_100","EUW1_300"]""", number => number == 300 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : null));
        var kept = new AramState();
        var error = await Assert.ThrowsAsync<RiotApiException>(() => new AramCollector(rejected, Items, options).RunAsync(kept, _ => { }, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Unauthorized, error.Status);
        Assert.Equal(1, kept.Tally.Games);
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }
}
