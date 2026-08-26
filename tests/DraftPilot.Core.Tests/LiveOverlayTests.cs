using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The live overlay holds matchups fetched for the draft on screen. It must win over the stocked
/// matrix, provide the mirror edge for free, and vanish completely when the draft ends.
/// </summary>
public class LiveOverlayTests
{
    private const int A = 1, B = 2;

    private static MetaLookup Meta() => new MetaBuilder()
        .Champion(A, "Alpha", DamageType.Physical, ["Fighter"], 175, 5)
        .Champion(B, "Beta", DamageType.Physical, ["Fighter"], 175, 5)
        .InLane(A, Lane.Top, winRate: 0.50, tier: 3, play: 2000)
        .InLane(B, Lane.Top, winRate: 0.50, tier: 3, play: 2000)
        .Matchup(A, B, Lane.Top, winRate: 0.52, play: 200)
        .Build();

    private static MatchupStat Live(double winRate, int play = 4000) => new()
    {
        ChampionId = A,
        OpponentId = B,
        Lane = Lane.Top,
        WinRate = winRate,
        Play = play,
    };

    [Fact]
    public void OverlayWinsOverTheSnapshot()
    {
        var meta = Meta();
        var before = meta.Matchup(A, B, Lane.Top)!.Value;

        meta.ApplyLiveMatchups([Live(0.60)]);
        var after = meta.Matchup(A, B, Lane.Top)!.Value;

        Assert.True(after.IsLive);
        Assert.True(after.WinRate > before.WinRate);
        Assert.Equal(4000, after.Play);
    }

    [Fact]
    public void MirrorEdgeIsInferredFromTheLiveData()
    {
        var meta = Meta();
        meta.ApplyLiveMatchups([Live(0.60)]);

        var mirror = meta.Matchup(B, A, Lane.Top)!.Value;

        Assert.True(mirror.IsLive);
        Assert.True(mirror.IsInferred);
        Assert.True(mirror.WinRate < 0.5);
    }

    [Fact]
    public void ClearRestoresTheSnapshotView()
    {
        var meta = Meta();
        var before = meta.Matchup(A, B, Lane.Top)!.Value;

        meta.ApplyLiveMatchups([Live(0.60)]);
        meta.ClearLiveMatchups();

        Assert.Equal(0, meta.LiveMatchupCount);
        Assert.Equal(before, meta.Matchup(A, B, Lane.Top)!.Value);
    }

    [Fact]
    public void UnknownChampionsAreIgnored()
    {
        var meta = Meta();
        meta.ApplyLiveMatchups([new MatchupStat { ChampionId = 999, OpponentId = B, Lane = Lane.Top, WinRate = 0.6, Play = 100 }]);

        Assert.Equal(0, meta.LiveMatchupCount);
    }
}
