using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>DirkDraft's own ARAM Mayhem count: lane-less players, augments, and stored tallies.</summary>
public sealed class RiotMayhemTests
{
    private static readonly ItemCatalog Items = ItemCatalog.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Riot", "item.json")));

    private static string Match(int queue, int duration, string participants) => $$$"""
        {"metadata":{"participants":[]},"info":{"gameVersion":"16.22.1.1","gameCreation":1,"gameDuration":{{{duration}}},"queueId":{{{queue}}},
         "participants":[{{{participants}}}]}}
        """;

    private const string Mayhem = """
        {"participantId":1,"championId":7,"teamPosition":"","win":true,"playerAugment1":101,"playerAugment2":202,"playerAugment3":0},
        {"participantId":6,"championId":8,"teamPosition":"","win":false,"playerAugment1":101}
        """;

    [Theory]
    [InlineData(2400, 900, true)]
    [InlineData(2400, 500, false)]  // a remake or a stomp
    [InlineData(450, 900, false)]   // plain ARAM is not Mayhem
    [InlineData(420, 899, false)]   // Solo/Duo needs fifteen minutes
    [InlineData(420, 1000, true)]
    public void WhatCounts_DependsOnQueueAndLength(int queue, int duration, bool counts)
        => Assert.Equal(counts, RiotMatchReader.ReadHeader(Match(queue, duration, Mayhem)).Counts);

    [Fact]
    public void MayhemPlayers_AreReadWithoutALane_AndWithTheirAugments()
    {
        var reading = RiotMatchReader.Read(Match(2400, 900, Mayhem), """{"info":{"frames":[]}}""", Items);

        Assert.Equal(2, reading.Participants.Count);
        Assert.All(reading.Participants, p => Assert.Equal(Lane.Unknown, p.Lane));
        Assert.Equal([101, 202], reading.Participants[0].PickedAugments);
    }

    [Fact]
    public void RankedPlayersWithoutALane_AreStillSkipped()
    {
        var reading = RiotMatchReader.Read(Match(420, 1000, Mayhem), """{"info":{"frames":[]}}""", Items);
        Assert.Empty(reading.Participants);
    }

    [Fact]
    public void TheTally_CountsChampionsAugmentsAndBuilds()
    {
        var reading = RiotMatchReader.Read(Match(2400, 900, Mayhem), """{"info":{"frames":[]}}""", Items);
        var tally = new RiotMayhemTally();
        tally.Add(reading);
        tally.Add(reading);

        var file = tally.Export("16.22", "euw1", DateTimeOffset.UnixEpoch, keep: 5);

        Assert.Equal(2, file.Games);
        var seven = Assert.Single(file.Champions, c => c.ChampionId == 7);
        Assert.Equal((2, 2), (seven.Games, seven.Wins));
        var shared = file.Augments.Where(a => a.AugmentId == 101).ToList();
        Assert.Equal(2, shared.Count); // one row per champion
        Assert.Equal(2, Assert.Single(shared, a => a.ChampionId == 8).Games);
        var build = Assert.Single(file.Builds, b => b.ChampionId == 7);
        Assert.Equal((Lane.Unknown, RiotMayhemTally.Tier, 2), (build.Lane, build.Tier, build.Games));
    }

    [Fact]
    public void AStoredTally_AddsUp()
    {
        var reading = RiotMatchReader.Read(Match(2400, 900, Mayhem), """{"info":{"frames":[]}}""", Items);
        var once = new RiotMayhemTally();
        once.Add(reading);

        var merged = new RiotMayhemTally();
        merged.Add(once.Export("16.22", "euw1", DateTimeOffset.UnixEpoch, keep: 5));
        merged.Add(once.Export("16.22", "euw1", DateTimeOffset.UnixEpoch, keep: 5));

        var twice = new RiotMayhemTally();
        twice.Add(reading);
        twice.Add(reading);

        Assert.Equivalent(
            twice.Export("16.22", "euw1", DateTimeOffset.UnixEpoch, keep: 5),
            merged.Export("16.22", "euw1", DateTimeOffset.UnixEpoch, keep: 5),
            strict: true);
    }
}
