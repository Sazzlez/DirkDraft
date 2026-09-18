using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// What a pick still risks while nobody stands opposite it. The first pick of a draft has no duel
/// term at all, so the list answers "how strong is this champion" while the question in that moment
/// is "how easily can this be answered" — these tests pin the difference between the two.
/// </summary>
public class CounterRiskTests
{
    private const int Mine = 1, Counter = 2, Strong = 3, Even = 4;

    /// <summary>
    /// Mine wins 52 % on its lane, so an average opponent takes 48 % off it. Counter takes 58 %,
    /// which is the excess this is about; Strong is a better champion overall but does nothing
    /// special into Mine; Even is exactly average against it.
    /// </summary>
    private static MetaLookup Meta() => new MetaBuilder()
        .Champion(Mine, "Mine").Champion(Counter, "Counter").Champion(Strong, "Strong").Champion(Even, "Even")
        .InLane(Mine, Lane.Top, winRate: 0.52, play: 40_000)
        .InLane(Counter, Lane.Top, winRate: 0.50, play: 40_000)
        .InLane(Strong, Lane.Top, winRate: 0.56, play: 40_000)
        .InLane(Even, Lane.Top, winRate: 0.50, play: 40_000)
        .Matchup(Counter, Mine, Lane.Top, winRate: 0.58, play: 40_000)
        .Matchup(Strong, Mine, Lane.Top, winRate: 0.48, play: 40_000)
        .Matchup(Even, Mine, Lane.Top, winRate: 0.48, play: 40_000)
        .Build();

    [Fact]
    public void AnAvailableCounter_IsListed()
    {
        var threats = CounterRisk.Open(Meta(), Mine, Lane.Top, new HashSet<int>());

        var threat = Assert.Single(threats);
        Assert.Equal(Counter, threat.ChampionId);
        Assert.Equal(40_000, threat.Play);
    }

    /// <summary>
    /// The whole point of the chip: a counter nobody can take any more is not a risk. Bans, locked
    /// picks and ally hovers all arrive here in the same set.
    /// </summary>
    [Fact]
    public void ACounterThatIsBannedOrTaken_IsNotARisk()
    {
        var threats = CounterRisk.Open(Meta(), Mine, Lane.Top, new HashSet<int> { Counter });

        Assert.Empty(threats);
    }

    /// <summary>
    /// A champion that is simply strong is not a counter. Strong wins 56 % on the lane and still
    /// only 48 % into Mine — exactly what an average opponent gets, so it says nothing about
    /// picking into it. Uncentred, every good champion would have shown up in every list.
    /// </summary>
    [Fact]
    public void AStrongChampionThatDoesNothingSpecial_IsNotACounter()
    {
        var threats = CounterRisk.Open(Meta(), Mine, Lane.Top, new HashSet<int>());

        Assert.DoesNotContain(threats, threat => threat.ChampionId == Strong);
        Assert.DoesNotContain(threats, threat => threat.ChampionId == Even);
    }

    [Fact]
    public void TheStrongestCounterComesFirst_AndTheLimitHolds()
    {
        var meta = new MetaBuilder()
            .Champion(Mine, "Mine").Champion(Counter, "Counter").Champion(Strong, "Strong").Champion(Even, "Even")
            .InLane(Mine, Lane.Top, winRate: 0.50, play: 40_000)
            .InLane(Counter, Lane.Top, winRate: 0.50, play: 40_000)
            .InLane(Strong, Lane.Top, winRate: 0.50, play: 40_000)
            .InLane(Even, Lane.Top, winRate: 0.50, play: 40_000)
            .Matchup(Counter, Mine, Lane.Top, winRate: 0.60, play: 40_000)
            .Matchup(Strong, Mine, Lane.Top, winRate: 0.56, play: 40_000)
            .Matchup(Even, Mine, Lane.Top, winRate: 0.54, play: 40_000)
            .Build();

        var all = CounterRisk.Open(meta, Mine, Lane.Top, new HashSet<int>(), limit: 0);
        var two = CounterRisk.Open(meta, Mine, Lane.Top, new HashSet<int>(), limit: 2);

        Assert.Equal([Counter, Strong, Even], all.Select(threat => threat.ChampionId));
        Assert.Equal(2, two.Count);
    }

    [Theory]
    [InlineData(Lane.Unknown)]
    // A lane the champion is not listed on has no reference rate to measure against.
    [InlineData(Lane.Mid)]
    public void WithoutALaneToStandOn_ThereIsNothingToSay(Lane lane)
        => Assert.Empty(CounterRisk.Open(Meta(), Mine, lane, new HashSet<int>()));

    [Fact]
    public void AChampionTheSnapshotDoesNotKnow_IsNotAnError()
        => Assert.Empty(CounterRisk.Open(Meta(), championId: 999, Lane.Top, new HashSet<int>()));
}
