using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Meta.Riot;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The draft numbers counted from ranked games: what one game adds, that two partial tallies add up
/// exactly, and that the exported rows mean what OP.GG's rows meant.
/// </summary>
public sealed class RiotMetaTests
{
    private static readonly ItemCatalog Items = ItemCatalog.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Riot", "item.json")));

    // Blue: 1 top, 2 jungle, 3 mid, 4 adc, 5 support. Red: 11..15 in the same order.
    private static MatchReading Game(bool blueWins, int[]? blue = null, int[]? red = null, int[]? bans = null, Lane[]? redLanes = null)
    {
        blue ??= [1, 2, 3, 4, 5];
        red ??= [11, 12, 13, 14, 15];
        redLanes ??= [.. Lanes.All];
        var players = new List<ParticipantBuild>();
        for (var i = 0; i < 5; i++)
            players.Add(Player(blue[i], Lanes.All[i], blueWins, 100, physical: 1000, magic: 0));
        for (var i = 0; i < 5; i++)
            players.Add(Player(red[i], redLanes[i], !blueWins, 200, physical: 0, magic: 1000));
        return new MatchReading("16.22", 0, 420, true, players, bans ?? []);
    }

    private static ParticipantBuild Player(int champion, Lane lane, bool win, int team, long physical, long magic)
        => new(champion, lane, win, [], [], [], 0, [], 0, string.Empty, team, physical, magic, 0);

    [Fact]
    public void OneGame_GivesLaneRowsWithPickRoleAndBanRates()
    {
        var tally = new RiotMetaTally();
        tally.Add(Game(blueWins: true, bans: [20, 21]));
        tally.Add(Game(blueWins: false, bans: [20]));

        var rows = tally.Rows(new MetaExportLimits { LaneMinimumGames = 1 });
        var top = Assert.Single(rows.Lanes, stat => stat.ChampionId == 1);

        Assert.Equal(Lane.Top, top.Lane);
        Assert.Equal(2, top.Play);
        Assert.Equal(0.5, top.WinRate, 10);
        Assert.Equal(1.0, top.PickRate, 10);   // in both games
        Assert.Equal(1.0, top.RoleRate, 10);   // always top
        Assert.Equal(0.0, top.BanRate, 10);
        Assert.Equal(-1, top.Tier);
        Assert.Equal(2, rows.Matches);
    }

    [Fact]
    public void ABannedChampion_CountsOncePerGame()
    {
        var tally = new RiotMetaTally();
        tally.Add(Game(blueWins: true, blue: [1, 2, 3, 4, 5], bans: [1, 1]));
        tally.Add(Game(blueWins: true, blue: [6, 2, 3, 4, 5]));

        var rows = tally.Rows(new MetaExportLimits { LaneMinimumGames = 1 });

        Assert.Equal(0.5, Assert.Single(rows.Lanes, stat => stat.ChampionId == 1).BanRate, 10);
    }

    [Fact]
    public void EveryDuel_IsPublishedBothWays_AsComplements()
    {
        var tally = new RiotMetaTally();
        tally.Add(Game(blueWins: true));
        tally.Add(Game(blueWins: true));
        tally.Add(Game(blueWins: false));

        var rows = tally.Rows(new MetaExportLimits { DuelMinimumGames = 1 });
        var forward = Assert.Single(rows.Duels, d => d.ChampionId == 1 && d.OpponentId == 11);
        var backward = Assert.Single(rows.Duels, d => d.ChampionId == 11 && d.OpponentId == 1);

        Assert.Equal(Lane.Top, forward.Lane);
        Assert.Equal(3, forward.Play);
        Assert.Equal(2.0 / 3, forward.WinRate, 10);
        Assert.Equal(1 - forward.WinRate, backward.WinRate, 10);
        Assert.True(forward.FromCompleteList && backward.FromCompleteList);
        Assert.Equal(10, rows.Duels.Count); // five lanes, two directions
    }

    [Fact]
    public void TwoPlayersOnOneLaneOfOneSide_GiveNoDuelThere()
    {
        var tally = new RiotMetaTally();
        // Red lists two "top" players and no support: the top duel is ambiguous, the support one empty.
        tally.Add(Game(blueWins: true, redLanes: [Lane.Top, Lane.Jungle, Lane.Mid, Lane.Adc, Lane.Top]));

        var rows = tally.Rows(new MetaExportLimits { DuelMinimumGames = 1 });

        Assert.DoesNotContain(rows.Duels, d => d.Lane == Lane.Top);
        Assert.DoesNotContain(rows.Duels, d => d.Lane == Lane.Support);
        Assert.Equal(6, rows.Duels.Count);
    }

    [Fact]
    public void Duos_AreTheTenPairsOfEachSide_KeyedByTheLowerId()
    {
        var tally = new RiotMetaTally();
        tally.Add(Game(blueWins: true));

        var rows = tally.Rows(new MetaExportLimits { DuoMinimumGames = 1 });

        Assert.Equal(20, rows.Duos.Count);
        var duo = Assert.Single(rows.Duos, s => s.ChampionId == 4 && s.PartnerId == 5);
        Assert.Equal(Lane.Adc, duo.Lane);
        Assert.Equal(Lane.Support, duo.PartnerLane);
        Assert.Equal(1.0, duo.WinRate, 10);
        Assert.All(rows.Duos, s => Assert.True(s.ChampionId < s.PartnerId));
        Assert.DoesNotContain(rows.Duos, s => s.ChampionId < 10 && s.PartnerId > 10);
    }

    [Fact]
    public void ThinRows_StayOutOfTheFile()
    {
        var tally = new RiotMetaTally();
        tally.Add(Game(blueWins: true));

        var rows = tally.Rows();

        Assert.Empty(rows.Lanes);
        Assert.Empty(rows.Duels);
        Assert.Empty(rows.Duos);
    }

    [Fact]
    public void AStrayLane_IsNoCandidateThere()
    {
        var tally = new RiotMetaTally();
        for (var i = 0; i < 40; i++)
            tally.Add(Game(blueWins: i % 2 == 0));
        // Champion 1 once in mid: 1 of 41 games is below the 5 % role share.
        tally.Add(Game(blueWins: true, blue: [9, 2, 1, 4, 5]));

        var rows = tally.Rows();

        Assert.Single(rows.Lanes, stat => stat.ChampionId == 1);
        Assert.Equal(Lane.Top, rows.Lanes.Single(stat => stat.ChampionId == 1).Lane);
    }

    [Fact]
    public void AStoredTally_AddsUpToTheSameRows()
    {
        var whole = new RiotMetaTally();
        var first = new RiotMetaTally();
        var second = new RiotMetaTally();
        for (var i = 0; i < 6; i++)
        {
            var game = Game(blueWins: i % 3 == 0, bans: [30 + i]);
            whole.Add(game);
            (i < 4 ? first : second).Add(game);
        }

        var merged = new RiotMetaTally();
        merged.Add(first.Export());
        merged.Add(second.Export());

        var limits = new MetaExportLimits { LaneMinimumGames = 1, DuelMinimumGames = 1, DuoMinimumGames = 1 };
        Assert.Equivalent(whole.Export(), merged.Export(), strict: true);
        Assert.Equivalent(whole.Rows(limits), merged.Rows(limits), strict: true);
    }

    [Theory]
    [InlineData(1000, 0, DamageType.Physical)]
    [InlineData(700, 300, DamageType.Physical)]
    [InlineData(690, 310, DamageType.Mixed)]
    [InlineData(500, 500, DamageType.Mixed)]
    [InlineData(300, 700, DamageType.Magic)]
    [InlineData(0, 0, DamageType.Unknown)]
    public void DamageType_ComesFromTheShareOfDamageDealt(long physical, long magic, DamageType expected)
        => Assert.Equal(expected, RiotMetaTally.DamageOf(physical, magic));

    [Fact]
    public void TheReader_TakesTeamsDamageAndBansFromTheMatch()
    {
        const string match = """
            {"metadata":{"participants":[]},"info":{"gameVersion":"16.22.1.1","gameCreation":1,"gameDuration":1800,"queueId":420,
             "teams":[{"teamId":100,"bans":[{"championId":7},{"championId":-1}]},{"teamId":200,"bans":[{"championId":7},{"championId":8}]}],
             "participants":[
              {"participantId":1,"teamId":100,"championId":1,"teamPosition":"TOP","win":true,
               "physicalDamageDealtToChampions":12000,"magicDamageDealtToChampions":3000,"trueDamageDealtToChampions":500},
              {"participantId":6,"championId":11,"teamPosition":"TOP","win":false}]}}
            """;
        var reading = RiotMatchReader.Read(match, """{"info":{"frames":[]}}""", Items);

        Assert.Equal([7, 8], reading.BannedChampions);
        var top = reading.Participants[0];
        Assert.Equal(100, top.TeamId);
        Assert.Equal(12000, top.PhysicalDamage);
        Assert.Equal(3000, top.MagicDamage);
        Assert.Equal(500, top.TrueDamage);
        Assert.Equal(200, reading.Participants[1].TeamId); // from the participant number
    }

    [Fact]
    public void TheExport_IsASnapshotTheModelCanRead()
    {
        var tally = new RiotMetaTally();
        for (var i = 0; i < 30; i++)
            tally.Add(Game(blueWins: i % 3 != 0, bans: [11]));

        const string dragon = """
            {"data":{
              "Aatrox":{"id":"Aatrox","key":"1","name":"Aatrox","tags":["Fighter"],"info":{"defense":4},"stats":{"attackrange":175}},
              "Ahri":{"id":"Ahri","key":"11","name":"Ahri","tags":["Mage","Assassin"],"info":{"defense":4},"stats":{"attackrange":550.0}},
              "Broken":{"id":"Broken","key":"x"}}}
            """;
        var champions = RiotMetaExport.ParseDataDragon(dragon);
        var snapshot = RiotMetaExport.Build(tally.Rows(), champions, "16.22", "platinum", "euw1", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        Assert.Equal("riot", snapshot.Source);
        Assert.Equal(30, snapshot.Games);
        Assert.Equal(2, snapshot.Champions.Count);
        var ahri = snapshot.Champions.Single(c => c.Id == 11);
        Assert.Equal(DamageType.Magic, ahri.Damage);
        Assert.Equal(550, ahri.AttackRange);
        Assert.Equal(["Mage", "Assassin"], ahri.Tags);
        Assert.Equal(DamageType.Physical, snapshot.Champions.Single(c => c.Id == 1).Damage);

        var meta = new MetaLookup(snapshot);
        Assert.Equal(1.0, meta.LaneStat(11, Lane.Top)!.Value.BanRate, 10);
        Assert.NotNull(meta.Matchup(1, 11, Lane.Top));
        Assert.False(meta.Matchup(1, 11, Lane.Top)!.Value.IsInferred);
        Assert.Contains("8 gezählte Champions", snapshot.Warnings.Single()); // ten counted, two in Data Dragon
    }
}
