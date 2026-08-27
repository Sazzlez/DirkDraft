using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The one build slot that adapts to the enemy composition. The thresholds must match the hint
/// chips exactly (hint and adjustment may never contradict each other), and the pick must only
/// ever choose among the alternatives the data actually delivered.
/// </summary>
public class SituationalBuildTests
{
    private static CompProfile Comp(double physicalShare, double magicShare, int totalCc, int count = 5)
        => new(count, physicalShare, magicShare, FrontlineCount: 1, RangedCount: 2,
            MaxEngage: 1, MaxPeel: 1, totalCc, LateScalingCount: 0, TraitCoverage: 1, Findings: []);

    private static ItemSet Boots(int id, int play = 100) => new()
    {
        Items = [$"Item {id}"],
        ItemIds = [id],
        Play = play,
        WinRate = 0.5,
    };

    [Fact]
    public void HeavyCrowdControl_OutranksTheDamageSplit()
    {
        // Even against pure AP, tenacity is the harder requirement — Mercs cover both anyway.
        var comp = Comp(physicalShare: 0.1, magicShare: 0.9, totalCc: 7);

        Assert.Equal(BootsPreference.Tenacity, SituationalBuild.PreferredBoots(comp));
    }

    [Theory]
    [InlineData(0.7, 0.3, 2, BootsPreference.Armor)]
    [InlineData(0.3, 0.7, 2, BootsPreference.MagicResist)]
    [InlineData(0.5, 0.5, 2, BootsPreference.None)]
    public void DamageSplit_DecidesBelowTheCcFloor(double physical, double magic, int cc, BootsPreference expected)
    {
        Assert.Equal(expected, SituationalBuild.PreferredBoots(Comp(physical, magic, cc)));
    }

    [Fact]
    public void FewerThanThreeEnemies_IsTooEarlyToJudge()
    {
        Assert.Equal(BootsPreference.None, SituationalBuild.PreferredBoots(Comp(0.9, 0.1, 9, count: 2)));
    }

    [Fact]
    public void PickBoots_FindsThePreferredSetAnywhereInTheList()
    {
        var boots = new[] { Boots(3006, play: 900), Boots(3047, play: 500), Boots(SituationalBuild.MercuryTreads, play: 50) };

        var picked = SituationalBuild.PickBoots(boots, BootsPreference.Tenacity);

        Assert.NotNull(picked);
        Assert.Contains(SituationalBuild.MercuryTreads, picked!.ItemIds);
    }

    [Fact]
    public void PickBoots_ReturnsNullWhenTheDataLacksTheAlternative()
    {
        // Then the caller keeps the most-played set and the hint chip stays plain advice.
        var boots = new[] { Boots(3006), Boots(3009) };

        Assert.Null(SituationalBuild.PickBoots(boots, BootsPreference.Tenacity));
        Assert.Null(SituationalBuild.PickBoots(boots, BootsPreference.None));
    }

    [Fact]
    public void ArmorPreference_PicksSteelcaps()
    {
        var boots = new[] { Boots(SituationalBuild.MercuryTreads), Boots(SituationalBuild.PlatedSteelcaps) };

        var picked = SituationalBuild.PickBoots(boots, BootsPreference.Armor);

        Assert.Contains(SituationalBuild.PlatedSteelcaps, picked!.ItemIds);
    }
}
