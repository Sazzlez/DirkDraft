using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Where a champion belongs when nobody has said. The build fetch needs a position — OP.GG has no
/// "any lane" build — and this is the last answer that is still measured rather than guessed. It
/// only ever runs in the cases where the client assigned nothing and the prediction stayed blank:
/// blind pick, a custom lobby, a queue that hands out no roles.
/// </summary>
public class MainLaneTests
{
    [Fact]
    public void TheLaneWithTheMostGamesWins()
    {
        var meta = new MetaBuilder()
            .Champion(1, "Wanderer")
            .InLane(1, Lane.Top, play: 4_000)
            .InLane(1, Lane.Jungle, play: 31_000)
            .InLane(1, Lane.Mid, play: 900)
            .Build();

        Assert.Equal(Lane.Jungle, meta.MainLane(1));
    }

    /// <summary>
    /// By games, not by win rate. A champion played forty times on a lane it happens to win on is
    /// not a champion that belongs there, and building it for that lane would be the one
    /// consequence of the mistake the reader cannot see.
    /// </summary>
    [Fact]
    public void ARareLaneWithAGreatRecord_DoesNotWin()
    {
        var meta = new MetaBuilder()
            .Champion(1, "Wanderer")
            .InLane(1, Lane.Top, play: 80_000, winRate: 0.49)
            .InLane(1, Lane.Support, play: 40, winRate: 0.72)
            .Build();

        Assert.Equal(Lane.Top, meta.MainLane(1));
    }

    /// <summary>A champion the snapshot lists nowhere has no lane, and the caller must not invent one.</summary>
    [Fact]
    public void AChampionWithoutLaneRows_HasNoMainLane()
    {
        var meta = new MetaBuilder().Champion(1, "Neu").Build();

        Assert.Equal(Lane.Unknown, meta.MainLane(1));
        Assert.Equal(Lane.Unknown, meta.MainLane(999));
    }

    /// <summary>
    /// A row that exists but carries no games — the fallback rows the snapshot writes — still
    /// places the champion. "Listed with zero games" is more than "not listed at all".
    /// </summary>
    [Fact]
    public void ALaneKnownWithoutGames_StillPlacesTheChampion()
    {
        var meta = new MetaBuilder()
            .Champion(1, "Selten")
            .InLane(1, Lane.Support, play: 0)
            .Build();

        Assert.Equal(Lane.Support, meta.MainLane(1));
    }
}
