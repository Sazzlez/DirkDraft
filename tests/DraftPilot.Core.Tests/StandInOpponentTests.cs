using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The stand-in opponent that makes a build possible in queues which never reveal the enemy team.
/// It is a proxy, so the tests pin the two things that keep it honest: it is only reached when
/// nothing at all is known, and it never names somebody who cannot be the opponent.
/// </summary>
public class StandInOpponentTests
{
    private const int Popular = 101, Rare = 102, Strong = 103, Mine = 104;

    private static MetaLookup Meta() => new MetaBuilder()
        .Champion(Popular, "Popular")
        .Champion(Rare, "Rare")
        .Champion(Strong, "Strong")
        .Champion(Mine, "Mine")
        .InLane(Popular, Lane.Support, winRate: 0.50, play: 20_000, pickRate: 0.12)
        .InLane(Rare, Lane.Support, winRate: 0.51, play: 2_000, pickRate: 0.01)
        .InLane(Strong, Lane.Support, winRate: 0.56, play: 5_000, pickRate: 0.04)
        .InLane(Mine, Lane.Support, winRate: 0.52, play: 9_000, pickRate: 0.06)
        .Build();

    private static readonly HashSet<int> Nothing = [];

    [Fact]
    public void TheStandInIsTheMostPlayedChampionOnTheLane()
        => Assert.Equal(Popular, StandInOpponent.For(Meta(), Lane.Support, Nothing, ownChampion: Mine));

    /// <summary>
    /// Not the strongest one. Building against the worst case every single game is its own kind of
    /// wrong, and the question the stand-in answers is "who will I probably face".
    /// </summary>
    [Fact]
    public void TheStandInIsNotTheStrongestChampion()
        => Assert.NotEqual(Strong, StandInOpponent.For(Meta(), Lane.Support, Nothing, ownChampion: Mine));

    [Fact]
    public void ABannedOrTakenChampion_IsNeverTheStandIn()
    {
        HashSet<int> unavailable = [Popular];

        Assert.Equal(Strong, StandInOpponent.For(Meta(), Lane.Support, unavailable, ownChampion: Mine));
    }

    [Fact]
    public void OurOwnChampion_IsNeverTheStandIn()
        => Assert.NotEqual(Popular, StandInOpponent.For(Meta(), Lane.Support, Nothing, ownChampion: Popular));

    [Fact]
    public void WithoutARoster_ThereIsNoStandIn()
    {
        Assert.Equal(0, StandInOpponent.For(Meta(), Lane.Top, Nothing, ownChampion: Mine));
        Assert.Equal(0, StandInOpponent.For(Meta(), Lane.Unknown, Nothing, ownChampion: Mine));
    }

    [Fact]
    public void AnEntirelyUnrevealedEnemyTeam_IsTheSignatureToActOn()
    {
        var state = DraftState.From(new SessionBuilder().LocalPlayer(0).Locked(0, Mine).Build());

        Assert.True(StandInOpponent.EnemiesAreHidden(state));
    }

    /// <summary>
    /// One revealed enemy is enough to wait: a draft that shows picks will show the lane opponent
    /// within seconds, and a build fetched against a guess would only have to be replaced.
    /// </summary>
    [Fact]
    public void ASingleRevealedEnemy_StopsTheStandIn()
    {
        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(0)
            .Locked(0, Mine)
            .Locked(7, Popular)
            .Build());

        Assert.False(StandInOpponent.EnemiesAreHidden(state));
    }

    [Fact]
    public void AHoveredEnemyAlsoCounts_AsRevealed()
    {
        var state = DraftState.From(new SessionBuilder()
            .LocalPlayer(0)
            .Locked(0, Mine)
            .Hovering(7, Popular)
            .Build());

        Assert.False(StandInOpponent.EnemiesAreHidden(state));
    }
}
