using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>Situational items: the enemy situations, and items counted against their own slot.</summary>
public sealed class RiotSituationalTests
{
    private static EnemyProfile Ad => new(DamageType.Physical, false, false);
    private static EnemyProfile Ap => new(DamageType.Magic, false, false);
    private static EnemyProfile Tank => new(DamageType.Magic, true, true);

    [Fact]
    public void Situations_FollowTheEnemyLineUp()
    {
        Assert.Equal([Situations.All, Situations.Physical], Situations.Of([Ad, Ad, Ad, Ap, Ap]));
        Assert.Equal([Situations.All, Situations.Magic, Situations.Tanks], Situations.Of([Ap, Ap, Tank, Tank, Ad]));
        Assert.Equal([Situations.All, Situations.Magic, Situations.Tanks, Situations.HardCc], Situations.Of([Tank, Tank, Tank, Ad, Ad]));
        // Two known enemies are no majority to speak of.
        Assert.Equal([Situations.All], Situations.Of([Ad, Ad, new EnemyProfile(DamageType.Unknown, false, false)]));
    }

    [Fact]
    public void MixedDamage_CountsHalfToEachSide()
    {
        var mixed = new EnemyProfile(DamageType.Mixed, false, false);
        // 2 + 0,5 + 0,5 of 5 = 60 % physical: just enough.
        Assert.Contains(Situations.Physical, Situations.Of([Ad, Ad, mixed, mixed, Ap]));
        Assert.DoesNotContain(Situations.Physical, Situations.Of([Ad, mixed, mixed, Ap, Ap]));
    }

    private static ParticipantBuild Player(int champion, int team, bool win, int boots, params int[] items)
        => new(champion, Lane.Top, win, [], [], [], boots, items, 0, string.Empty, team);

    [Fact]
    public void Items_AreCountedAgainstTheirSlot_UnderEverySituation()
    {
        var tally = new RiotSituationalTally();
        var players = new List<ParticipantBuild>
        {
            Player(1, 100, true, 3047, 10, 11, 12, 20, 21),
            Player(2, 200, false, 3111, 10, 11, 12),
            Player(3, 200, false, 0),
            Player(4, 200, false, 0),
        };
        // Champions 2 to 4 deal physical damage, so champion 1's enemies are "physical".
        EnemyProfile Profile(int id) => id >= 2 ? Ad : Ap;
        for (var i = 0; i < 3; i++)
            tally.Add(new MatchReading("16.22", 0, 420, true, players), Profile);

        var rows = tally.Export();
        int[] Row(int champion, int slot, int situation, int item)
            => rows.Single(r => r[0] == champion && r[2] == slot && r[3] == situation && r[4] == item);

        Assert.Equal([1, (int)Lane.Top, RiotSituationalTally.LateSlot, Situations.Physical, 20, 3, 3], Row(1, 1, Situations.Physical, 20));
        Assert.Equal(3, Row(1, RiotSituationalTally.LateSlot, Situations.All, 0)[5]); // the slot total
        Assert.Equal(3, Row(1, RiotSituationalTally.BootsSlot, Situations.Physical, 3047)[5]);
        // Champion 2 never reached a fourth item: no late slot at all, boots only.
        Assert.DoesNotContain(rows, r => r[0] == 2 && r[2] == RiotSituationalTally.LateSlot);
        Assert.Contains(rows, r => r[0] == 2 && r[2] == RiotSituationalTally.BootsSlot && r[4] == 3111);
    }

    [Fact]
    public void TheFile_KeepsTotalsForTheRowsItKeeps_AndSumsClasses()
    {
        var tally = new RiotSituationalTally();
        tally.Add([
            [1, 0, 1, 0, 0, 30, 15],
            [1, 0, 1, 0, 20, 25, 14],
            [1, 0, 1, 0, 21, 5, 1],
            [2, 0, 1, 0, 0, 10, 5],
            [2, 0, 1, 0, 20, 8, 4],
        ]);

        var file = tally.File("16.22", "euw1", DateTimeOffset.UnixEpoch, new Dictionary<int, string> { [1] = "Fighter", [2] = "Fighter" }, minimumGames: 20, minimumClassGames: 30);

        Assert.Equal([[1, 0, 1, 0, 0, 30, 15], [1, 0, 1, 0, 20, 25, 14]], file.Rows);
        Assert.Equal(["Fighter"], file.ClassNames);
        Assert.Equal([[0, 0, 1, 0, 0, 40, 20], [0, 0, 1, 0, 20, 33, 18]], file.Classes);
    }

    [Fact]
    public void AStoredTally_AddsUp()
    {
        var once = new RiotSituationalTally();
        once.Add(new MatchReading("16.22", 0, 420, true, [Player(1, 100, true, 3047, 1, 2, 3, 4), Player(2, 200, false, 0)]), _ => Ad);

        var merged = new RiotSituationalTally();
        merged.Add(once.Export());
        merged.Add(once.Export());

        Assert.All(merged.Export(), row => Assert.Equal(2, row[5]));
    }
}
