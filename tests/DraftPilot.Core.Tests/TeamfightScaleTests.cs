using System.Text.Json;
using DraftPilot.Core.Data;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The teamfight scale fitted at export (<see cref="TeamfightScaleModel"/>): on fights drawn from a
/// known truth it finds the weights of level, items and Baron and the champion who is stronger, leaves
/// out the fights the measurement leaves out, and lands in meta.json under the names the app reads.
/// </summary>
public class TeamfightScaleTests
{
    private const int AllSeen = 0b11111_11111;

    /// <summary>A line with the fight part (version 5): seats for ten champions, no duels kept, then objectives and fights.</summary>
    private static int[] Line(int[] champions, int[] objectives, params int[][] fights)
    {
        var line = new int[RiotGames.FixedLength];
        for (var place = 0; place < 10; place++)
            line[place] = champions[place] * 8 + place % 5 + 1;
        line[13] = -1;
        line[15] = 3000;                                     // minute ten read
        List<int> rest = [RiotGames.NoDuels, RiotGames.FightsFollow, objectives.Length, .. objectives];
        foreach (var fight in fights)
            rest.AddRange([RiotGames.FightLength, .. fight]);
        return [.. line, .. rest];
    }

    private static int[] Fight(int second, int kills0, int kills1, int dead, int seen, int[] levels, int[] worth)
        => [second, kills0, kills1, dead, seen, .. Enumerable.Range(0, 10).SelectMany(p => new[] { levels[p], worth[p] })];

    /// <summary>
    /// Truth: 0,15 per level, 0,07 per 1.000 gold on the carries (places 0, 2, 3 and 5, 7, 8: top, mid,
    /// ADC) and 0,03 on jungle and support, +0,8 for the team with Baron; champion 1 adds +0,5 to its
    /// team between minute 20 and 30. Three fights a game, at 16, 24 and 33 minutes.
    /// </summary>
    private static List<int[]> World(int games, int seed = 11)
    {
        var random = new Random(seed);
        double Gauss() => Math.Sqrt(-2 * Math.Log(random.NextDouble() + 1e-12)) * Math.Cos(2 * Math.PI * random.NextDouble());
        var lines = new List<int[]>();
        for (var g = 0; g < games; g++)
        {
            var champions = Enumerable.Range(1, 20).OrderBy(_ => random.Next()).Take(10).ToArray();
            var baronSide = random.Next(2);
            int[] objectives = random.NextDouble() < 0.5 ? [1400, baronSide, 8] : [];
            var fights = new List<int[]>();
            foreach (var second in new[] { 960, 1440, 1980 })
            {
                var levels = Enumerable.Range(0, 10).Select(_ => 11 + random.Next(4)).ToArray();
                var worth = Enumerable.Range(0, 10).Select(_ => (int)(6000 + second * 3 + 1500 * Gauss())).ToArray();
                double Gold(int[] places) => places.Sum(p => worth[p]) / 1000.0;
                var z = 0.15 * (levels[..5].Sum() - levels[5..].Sum())
                    + 0.07 * (Gold([0, 2, 3]) - Gold([5, 7, 8]))
                    + 0.03 * (Gold([1, 4]) - Gold([6, 9]));
                if (objectives.Length > 0 && second > 1400 && second - 1400 <= 180)
                    z += baronSide == 0 ? 0.8 : -0.8;
                if (TeamfightScaleModel.PhaseOf(second) == 1)
                    z += (champions[..5].Contains(1) ? 0.5 : 0) - (champions[5..].Contains(1) ? 0.5 : 0);
                var wins = random.NextDouble() < 1 / (1 + Math.Exp(-z));
                fights.Add(Fight(second, wins ? 3 : 1, wins ? 1 : 3, 0, AllSeen, levels, worth));
            }
            lines.Add(Line(champions, objectives, [.. fights]));
        }
        return lines;
    }

    [Fact]
    public void FindsTheWeightsAndTheStrongerChampion()
    {
        var scale = TeamfightScaleModel.Fit(World(12_000), tau: 0.3, minimumFights: 1000)!;

        Assert.Equal(36_000, scale.Fights);
        Assert.InRange(scale.Weights.Level, 0.12, 0.18);
        // Gold on the carries counts more than on jungle and support; the summed weight lies between.
        Assert.InRange(scale.Weights.GoldCarry, 0.055, 0.085);
        Assert.InRange(scale.Weights.GoldRest, 0.01, 0.05);
        Assert.InRange(scale.Weights.Gold, scale.Weights.GoldRest, scale.Weights.GoldCarry);
        Assert.InRange(scale.Weights.Baron, 0.6, 1.0);
        // Nothing of these in this world: their weights stay at 0.
        Assert.Equal(0, scale.Weights.Elder, 3);
        Assert.Equal(0, scale.Weights.Soul, 3);

        // Only differences between champions mean anything: champion 1 stands about 0,5 above the
        // average champion in the middle phase, and not before.
        double Strength(int champion, int phase) => scale.Champions.Single(s => s.ChampionId == champion && s.Phase == phase).Strength;
        var middle = Enumerable.Range(2, 19).Average(c => Strength(c, 1));
        Assert.InRange(Strength(1, 1) - middle, 0.3, 0.6);
        Assert.InRange(Math.Abs(Strength(1, 0) - Enumerable.Range(2, 19).Average(c => Strength(c, 0))), 0, 0.15);
    }

    [Fact]
    public void LeavesOutWhatTheMeasurementLeavesOut()
    {
        int[] champions = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var levels = Enumerable.Repeat(12, 10).ToArray();
        var worth = Enumerable.Repeat(8000, 10).ToArray();
        var afk = levels.ToArray();
        afk[3] = 7;
        var line = Line(champions, [],
            Fight(1000, 3, 1, 0, AllSeen, levels, worth),                   // counts
            Fight(1100, 3, 1, 1 << 6, AllSeen, levels, worth),              // someone dead at the start
            Fight(1200, 3, 1, 0, 0b11111_00111, levels, worth),             // three of side 0 seen
            Fight(1300, 2, 2, 0, AllSeen, levels, worth),                   // a draw
            Fight(1400, 3, 1, 0, AllSeen, afk, worth));                     // an AFK player
        var before = new int[RiotGames.FixedLength];                        // a line without the fight part

        Assert.Equal(1, TeamfightScaleModel.Fit([line, before], minimumFights: 1)!.Fights);
        Assert.Null(TeamfightScaleModel.Fit([line], minimumFights: 2));
        Assert.Null(TeamfightScaleModel.Fit([line]));                      // far below 20.000
    }

    [Fact]
    public void LandsInMetaJsonUnderTheNamesTheAppReads()
    {
        var snapshot = new MetaSnapshot
        {
            TeamfightScale = new TeamfightScale
            {
                Fights = 45_447,
                Tau = 0.06,
                Weights = new TeamfightWeights { Level = 0.142, Gold = 0.049, GoldCarry = 0.066, GoldRest = 0.032, Baron = 0.166, Elder = 0.508, Dragon = 0.062, Soul = 0.265 },
                Champions = [new TeamfightStrength(234, 1, 0.222, 2155)],
            },
        };

        var json = JsonSerializer.Serialize(snapshot, MetaJson.Default.MetaSnapshot);
        Assert.Contains("\"teamfightScale\":{\"fights\":45447,\"tau\":0.06,\"weights\":{\"level\":0.142,\"gold\":0.049,\"goldCarry\":0.066,\"goldRest\":0.032,\"baron\":0.166,\"elder\":0.508,\"dragon\":0.062,\"soul\":0.265},\"champions\":[{\"championId\":234,\"phase\":1,\"strength\":0.222,\"fights\":2155}]}", json);
        Assert.Equal(0.508, JsonSerializer.Deserialize(json, MetaJson.Default.MetaSnapshot)!.TeamfightScale!.Weights.Elder);
        Assert.DoesNotContain("teamfightScale", JsonSerializer.Serialize(new MetaSnapshot(), MetaJson.Default.MetaSnapshot));
    }

    [Theory]
    [InlineData(840, 0)]
    [InlineData(1199, 0)]
    [InlineData(1200, 1)]
    [InlineData(1799, 1)]
    [InlineData(1800, 2)]
    public void ThePhaseFollowsTheMinute(int second, int phase) => Assert.Equal(phase, TeamfightScaleModel.PhaseOf(second));
}
