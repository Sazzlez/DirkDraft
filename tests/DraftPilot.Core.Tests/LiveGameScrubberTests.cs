using System.Text.Json.Nodes;
using DraftPilot.Core.Lcu;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The running game names all ten players, and a recording of it is meant to be shared. Unlike the
/// champion select scrubber, this one cannot rely on knowing the payload's shape — the recording
/// exists precisely because the shape is unknown — so what it must get right is finding a name
/// wherever it sits, and keeping everything that is not one.
/// </summary>
public class LiveGameScrubberTests
{
    /// <summary>Shaped like the live payload: names in the player list, the active player and the event feed.</summary>
    private const string Payload = """
        {
          "activePlayer": {
            "summonerName": "Crytekx#EUW",
            "riotIdGameName": "Crytekx",
            "riotIdTagLine": "EUW",
            "currentGold": 1337.5,
            "championStats": { "abilityPower": 0.0, "armor": 42 }
          },
          "allPlayers": [
            {
              "championName": "Darius",
              "rawChampionName": "game_character_displayname_Darius",
              "summonerName": "Crytekx#EUW",
              "riotId": "Crytekx#EUW",
              "level": 6,
              "isBot": false
            },
            {
              "championName": "Jax",
              "summonerName": "Gegner#EUW",
              "riotId": "Gegner#EUW",
              "level": 5,
              "isBot": false
            }
          ],
          "events": {
            "Events": [
              { "EventID": 0, "EventName": "GameStart", "EventTime": 0.03 },
              {
                "EventID": 1,
                "EventName": "ChampionKill",
                "KillerName": "Crytekx#EUW",
                "VictimName": "Gegner#EUW",
                "Assisters": ["Dritter#EUW", "Gegner#EUW"]
              }
            ]
          },
          "gameData": { "gameMode": "ARAM", "gameTime": 421.5, "mapName": "Map12" }
        }
        """;

    private static JsonNode Scrubbed()
    {
        var scrubber = new LiveGameScrubber();
        Assert.True(scrubber.TryScrub(Payload, out var clean));

        return JsonNode.Parse(clean)!;
    }

    [Fact]
    public void NoNameSurvivesAnywhere()
    {
        var scrubber = new LiveGameScrubber();
        Assert.True(scrubber.TryScrub(Payload, out var clean));

        Assert.DoesNotContain("Crytekx", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Gegner", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dritter", clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheEventFeedIsCleanedToo()
    {
        var kill = Scrubbed()["events"]!["Events"]![1]!;

        Assert.StartsWith("Spieler ", kill["KillerName"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.StartsWith("Spieler ", kill["VictimName"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.All(
            kill["Assisters"]!.AsArray(),
            entry => Assert.StartsWith("Spieler ", entry!.GetValue<string>(), StringComparison.Ordinal));
    }

    /// <summary>
    /// The same person keeps one pseudonym, or a kill feed stops being readable — and "who killed
    /// whom" is the only reason to keep the feed at all.
    /// </summary>
    [Fact]
    public void OnePersonIsOnePseudonymEverywhere()
    {
        var root = Scrubbed();
        var kill = root["events"]!["Events"]![1]!;

        var me = root["activePlayer"]!["summonerName"]!.GetValue<string>();
        var victim = kill["VictimName"]!.GetValue<string>();

        Assert.Equal(me, root["allPlayers"]![0]!["summonerName"]!.GetValue<string>());
        Assert.Equal(me, kill["KillerName"]!.GetValue<string>());
        Assert.Equal(victim, root["allPlayers"]![1]!["summonerName"]!.GetValue<string>());
        Assert.Equal(victim, kill["Assisters"]![1]!.GetValue<string>());
        Assert.NotEqual(me, victim);
    }

    /// <summary>
    /// Everything that is not a person stays, in its original type. A recording that lost the
    /// champion, the gold or the game mode would not answer the question it was made for.
    /// </summary>
    [Fact]
    public void NothingElseIsTouched()
    {
        var root = Scrubbed();

        Assert.Equal("Darius", root["allPlayers"]![0]!["championName"]!.GetValue<string>());
        Assert.Equal("game_character_displayname_Darius", root["allPlayers"]![0]!["rawChampionName"]!.GetValue<string>());
        Assert.Equal(6, root["allPlayers"]![0]!["level"]!.GetValue<int>());
        Assert.False(root["allPlayers"]![0]!["isBot"]!.GetValue<bool>());
        Assert.Equal(1337.5, root["activePlayer"]!["currentGold"]!.GetValue<double>());
        Assert.Equal(42, root["activePlayer"]!["championStats"]!["armor"]!.GetValue<int>());
        Assert.Equal("ARAM", root["gameData"]!["gameMode"]!.GetValue<string>());
        Assert.Equal("GameStart", root["events"]!["Events"]![0]!["EventName"]!.GetValue<string>());
    }

    /// <summary>
    /// A field is replaced, never removed. The recording is made to find out whether a field
    /// exists; answering that by deleting fields would defeat the whole exercise.
    /// </summary>
    [Fact]
    public void FieldsAreReplacedNotRemoved()
    {
        var player = Scrubbed()["allPlayers"]![0]!.AsObject();

        Assert.True(player.ContainsKey("summonerName"));
        Assert.True(player.ContainsKey("riotId"));
    }

    /// <summary>
    /// An unknown payload is the normal case here, and a name in it has to be caught by its
    /// property name alone — at any depth, under any parent this scrubber has never seen.
    /// </summary>
    [Fact]
    public void ANameIsCaughtWhereverItAppears()
    {
        var scrubber = new LiveGameScrubber();

        Assert.True(scrubber.TryScrub(
            """{"mayhem":{"augments":[{"offered":{"by":{"summonerName":"Crytekx#EUW"}}}]}}""",
            out var clean));

        Assert.DoesNotContain("Crytekx", clean, StringComparison.Ordinal);
        Assert.Contains("summonerName", clean, StringComparison.Ordinal);
    }

    /// <summary>A blank name is not a person, and blanking it would hide that the game sent nothing.</summary>
    [Fact]
    public void AnEmptyNameStaysEmpty()
    {
        var scrubber = new LiveGameScrubber();

        Assert.True(scrubber.TryScrub("""{"summonerName":""}""", out var clean));
        Assert.Equal("""{"summonerName":""}""", clean);
        Assert.Equal(0, scrubber.Replaced);
    }

    /// <summary>
    /// A payload that cannot be parsed comes back untouched and reports failure, so the caller
    /// declines to write it. Writing an unreadable body "just in case" is how a name escapes.
    /// </summary>
    [Fact]
    public void AnUnparsablePayloadIsRefused()
    {
        var scrubber = new LiveGameScrubber();

        Assert.False(scrubber.TryScrub("""{"summonerName": "Crytekx#EUW" """, out var clean));
        Assert.Equal("""{"summonerName": "Crytekx#EUW" """, clean);
    }

    /// <summary>
    /// A name field holding something other than a string is left alone rather than guessed at —
    /// and, more to the point, does not take the probe down with it.
    /// </summary>
    [Fact]
    public void AnUnexpectedTypeIsSurvived()
    {
        var scrubber = new LiveGameScrubber();

        Assert.True(scrubber.TryScrub("""{"summonerName":null,"riotId":17,"Assisters":[1,2]}""", out var clean));
        Assert.Contains("17", clean, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deep nesting must not recurse the process to death, and it does not — but the guard that
    /// stops it is the parser's own depth limit, which trips before the walker is ever called. That
    /// is the outcome worth having: the payload is refused whole, rather than written half-scrubbed
    /// with the innermost name still in it.
    /// </summary>
    [Fact]
    public void ExtremeNestingIsRefusedRatherThanHalfWalked()
    {
        var payload = Nested(200);
        var scrubber = new LiveGameScrubber();

        Assert.False(scrubber.TryScrub(payload, out var clean));
        Assert.Equal(payload, clean);
    }

    /// <summary>Nesting the parser does accept is scrubbed all the way to the bottom.</summary>
    [Fact]
    public void DeepButAcceptableNestingIsStillScrubbed()
    {
        var scrubber = new LiveGameScrubber();

        Assert.True(scrubber.TryScrub(Nested(50), out var clean));
        Assert.DoesNotContain("Crytekx", clean, StringComparison.Ordinal);
    }

    private static string Nested(int levels)
        => string.Concat(Enumerable.Repeat("""{"a":""", levels))
            + """{"summonerName":"Crytekx#EUW"}"""
            + new string('}', levels);
}
