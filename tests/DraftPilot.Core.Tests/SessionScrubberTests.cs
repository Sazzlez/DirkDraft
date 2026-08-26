using DraftPilot.Core.Lcu;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// A recording is the kind of file that gets attached to a bug report. The client's session carries
/// a summoner name, a puuid and a live chat JWT, so none of that may survive into one.
/// </summary>
public class SessionScrubberTests
{
    /// <summary>Shaped like the real payload, including the fields that must not be kept.</summary>
    private const string Payload = """
        {
          "localPlayerCellId": 0,
          "gameId": 7962503596,
          "chatDetails": {
            "multiUserChatId": "8474e8d1",
            "multiUserChatPassword": "eyJraWQiOiIxIiwiYWxnIjoiUlMyNTYifQ.payload.signature",
            "mucJwtDto": { "jwt": "eyJraWQiOiIxIiwiYWxnIjoiUlMyNTYifQ.payload.signature" }
          },
          "myTeam": [
            {
              "cellId": 0,
              "championId": 266,
              "assignedPosition": "top",
              "gameName": "Crytekx",
              "tagLine": "EUW",
              "puuid": "a888c28e-a70c-5ae3-b0e2-9fb652257ef5",
              "summonerId": 24618462,
              "spell1Id": 6
            }
          ],
          "theirTeam": [
            {
              "cellId": 5,
              "championId": 0,
              "assignedPosition": "",
              "gameName": "SomebodyElse",
              "puuid": "b999d39f-b81d-6bf4-c1f3-0ac763368fa6"
            }
          ],
          "timer": { "phase": "PLANNING" }
        }
        """;

    [Theory]
    [InlineData("chatDetails")]
    [InlineData("multiUserChatPassword")]
    [InlineData("jwt")]
    [InlineData("eyJraWQiOiIxIiwiYWxnIjoiUlMyNTYifQ")]
    [InlineData("gameId")]
    [InlineData("Crytekx")]
    [InlineData("SomebodyElse")]
    [InlineData("tagLine")]
    [InlineData("puuid")]
    [InlineData("summonerId")]
    public void SensitiveContent_IsRemoved(string needle)
    {
        var scrubbed = SessionScrubber.Scrub(Payload);

        Assert.DoesNotContain(needle, scrubbed, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("localPlayerCellId")]
    [InlineData("cellId")]
    [InlineData("championId")]
    [InlineData("assignedPosition")]
    [InlineData("timer")]
    [InlineData("PLANNING")]
    [InlineData("spell1Id")]
    public void EverythingTheToolReads_Survives(string needle)
    {
        var scrubbed = SessionScrubber.Scrub(Payload);

        Assert.Contains(needle, scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrubbedPayload_StillParsesIntoADraftState()
    {
        var scrubbed = SessionScrubber.Scrub(Payload);
        var session = System.Text.Json.JsonSerializer.Deserialize(
            scrubbed, DraftPilot.Core.Lcu.Models.LcuJson.Default.ChampSelectSession);

        Assert.NotNull(session);

        var state = DraftPilot.Core.Draft.DraftState.From(session);
        Assert.True(state.IsActive);
        Assert.Equal(DraftPilot.Core.Draft.Lane.Top, state.Allies[0].AssignedLane);
        Assert.Equal(266, state.Allies[0].LockedChampionId);
    }

    [Fact]
    public void UnparseablePayload_IsReportedRatherThanMangled()
    {
        Assert.False(SessionScrubber.TryScrub("not json at all", out var result));
        Assert.Equal("not json at all", result);
    }

    [Fact]
    public void PayloadWithoutTeams_IsHandled()
    {
        Assert.True(SessionScrubber.TryScrub("""{"localPlayerCellId":3}""", out var result));
        Assert.Contains("localPlayerCellId", result, StringComparison.Ordinal);
    }
}
