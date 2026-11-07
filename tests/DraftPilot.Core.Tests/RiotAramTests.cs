using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>DirkDraft's own ARAM count (plain ARAM as Mayhem's stand-in): lane-less players and stored tallies.</summary>
public sealed class RiotAramTests
{
    private static readonly ItemCatalog Items = ItemCatalog.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Riot", "item.json")));

    private static string Match(int queue, int duration, string participants) => $$$"""
        {"metadata":{"participants":[]},"info":{"gameVersion":"16.22.1.1","gameCreation":1,"gameDuration":{{{duration}}},"queueId":{{{queue}}},
         "participants":[{{{participants}}}]}}
        """;

    private const string Aram = """
        {"participantId":1,"championId":7,"teamPosition":"","win":true},
        {"participantId":6,"championId":8,"teamPosition":"","win":false}
        """;

    [Theory]
    [InlineData(450, 900, true)]
    [InlineData(450, 500, false)]   // a remake or a stomp
    [InlineData(2400, 900, true)]   // Mayhem would count too, if Riot ever handed it out
    [InlineData(420, 899, false)]   // Solo/Duo needs fifteen minutes
    [InlineData(420, 1000, true)]
    [InlineData(1700, 1000, false)] // Arena is neither
    public void WhatCounts_DependsOnQueueAndLength(int queue, int duration, bool counts)
        => Assert.Equal(counts, RiotMatchReader.ReadHeader(Match(queue, duration, Aram)).Counts);

    [Fact]
    public void AramPlayers_AreReadWithoutALane()
    {
        var reading = RiotMatchReader.Read(Match(450, 900, Aram), """{"info":{"frames":[]}}""", Items);

        Assert.Equal(2, reading.Participants.Count);
        Assert.All(reading.Participants, p => Assert.Equal(Lane.Unknown, p.Lane));
    }

    [Fact]
    public void RankedPlayersWithoutALane_AreStillSkipped()
    {
        var reading = RiotMatchReader.Read(Match(420, 1000, Aram), """{"info":{"frames":[]}}""", Items);
        Assert.Empty(reading.Participants);
    }

    [Fact]
    public void TheTally_CountsChampionsAndBuilds()
    {
        var reading = RiotMatchReader.Read(Match(450, 900, Aram), """{"info":{"frames":[]}}""", Items);
        var tally = new RiotAramTally();
        tally.Add(reading);
        tally.Add(reading);

        var file = tally.Export("16.22", "euw1", DateTimeOffset.UnixEpoch, keep: 5);

        Assert.Equal(2, file.Games);
        var seven = Assert.Single(file.Champions, c => c.ChampionId == 7);
        Assert.Equal((2, 2), (seven.Games, seven.Wins));
        var eight = Assert.Single(file.Champions, c => c.ChampionId == 8);
        Assert.Equal((2, 0), (eight.Games, eight.Wins));
        var build = Assert.Single(file.Builds, b => b.ChampionId == 7);
        Assert.Equal((Lane.Unknown, RiotAramTally.Tier, 2), (build.Lane, build.Tier, build.Games));
    }

    [Fact]
    public void AStoredTally_AddsUp()
    {
        var reading = RiotMatchReader.Read(Match(450, 900, Aram), """{"info":{"frames":[]}}""", Items);
        var once = new RiotAramTally();
        once.Add(reading);

        var merged = new RiotAramTally();
        merged.Add(once.Export("16.22", "euw1", DateTimeOffset.UnixEpoch, keep: 5));
        merged.Add(once.Export("16.22", "euw1", DateTimeOffset.UnixEpoch, keep: 5));

        var twice = new RiotAramTally();
        twice.Add(reading);
        twice.Add(reading);

        Assert.Equivalent(
            twice.Export("16.22", "euw1", DateTimeOffset.UnixEpoch, keep: 5),
            merged.Export("16.22", "euw1", DateTimeOffset.UnixEpoch, keep: 5),
            strict: true);
    }
}
