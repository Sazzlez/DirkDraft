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

    // Thornmail-like armor item (2450 g, 75 armor), an MR item, a lifesteal item, two boots.
    private static readonly ItemCatalog Catalog = ItemCatalog.Parse("""
        {"version":"16.22.1","data":{
          "1001":{"name":"Boots","gold":{"total":300,"purchasable":true},"tags":["Boots"],"into":["3047"]},
          "3047":{"name":"Steelcaps","gold":{"total":1100,"purchasable":true},"tags":["Boots","Armor"],"from":["1001"],"stats":{"FlatArmorMod":25}},
          "3075":{"name":"Thornmail","gold":{"total":2450,"purchasable":true},"tags":["Armor"],"stats":{"FlatArmorMod":75}},
          "3065":{"name":"Visage","gold":{"total":2700,"purchasable":true},"tags":["SpellBlock"],"stats":{"FlatSpellBlockMod":60.0}},
          "3072":{"name":"Bloodthirster","gold":{"total":3400,"purchasable":true},"tags":["LifeSteal"]},
          "3036":{"name":"Dominik","gold":{"total":3000,"purchasable":true},"tags":["ArmorPenetration"]},
          "1029":{"name":"Cloth","gold":{"total":300,"purchasable":true},"tags":["Armor"],"stats":{"FlatArmorMod":15},"into":["3075"]}
        }}
        """);

    [Fact]
    public void TheCatalog_KnowsArmorMagicResistAndSustain()
    {
        Assert.Equal(75, Catalog.Armor(3075));
        Assert.Equal(60, Catalog.MagicResist(3065));
        Assert.True(Catalog.IsSustain(3072));
        Assert.True(Catalog.CountsForSituation(3047));
        Assert.False(Catalog.CountsForSituation(1029)); // a component is not what someone has built
    }

    [Fact]
    public void EnemyItems_AreJudgedAtTheMomentOfTheBuy()
    {
        // Champion 1 buys Dominik's as fourth item at 20:00; the enemy tanks finish their second
        // Thornmail at 18:00 and their third at 25:00 — 75 + 75 = 150 armor by the buy: "armor".
        var buyer = new ParticipantBuild(1, Lane.Top, true, [], [], [], 0, [10, 11, 12, 3036], 0, string.Empty, 100,
            ItemTimesMs: [600_000, 800_000, 1_000_000, 1_200_000]);
        var tank = new ParticipantBuild(2, Lane.Top, false, [], [], [], 0, [3075], 0, string.Empty, 200, ItemTimesMs: [900_000]);
        var late = new ParticipantBuild(3, Lane.Jungle, false, [], [], [], 0, [3075, 3075], 0, string.Empty, 200, ItemTimesMs: [1_080_000, 1_500_000]);
        var tally = new RiotSituationalTally();
        tally.Add(new MatchReading("16.22", 0, 420, true, [buyer, tank, late]), _ => Ad, Catalog);

        var rows = tally.Export();
        Assert.Contains(rows, r => r[0] == 1 && r[3] == Situations.EnemyArmor && r[4] == 3036);
        Assert.Contains(rows, r => r[0] == 1 && r[3] == Situations.EnemyArmor && r[4] == 0);
    }

    [Fact]
    public void ItemSituations_FollowTheThresholds()
    {
        Assert.Contains(Situations.EnemyArmor, Situations.Of([], new EnemyItems(150, 0, 0)));
        Assert.DoesNotContain(Situations.EnemyArmor, Situations.Of([], new EnemyItems(149, 0, 0)));
        Assert.Contains(Situations.EnemyMagicResist, Situations.Of([], new EnemyItems(0, 120, 0)));
        Assert.Contains(Situations.EnemySustain, Situations.Of([], new EnemyItems(0, 0, 2)));
        Assert.Equal(new EnemyItems(100, 60, 1), Situations.ItemsOf([3047, 3075, 3065, 3072, 1029], Catalog));
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
