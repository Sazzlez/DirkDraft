using System.Text.Json;
using DraftPilot.Core.Data;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The lane scale fitted at export (<see cref="LaneScaleModel"/>): on duels drawn from a known truth it
/// finds the weights per phase and the champion who is stronger, reads only lines with the levels
/// before the kill, and lands in meta.json under the names the app reads.
/// </summary>
public class LaneScaleTests
{
    // Champions 1–4 all play top; the truth: champion 1 is +0,6 stronger from level 6 to 10.
    private const int Strong = 1;

    /// <summary>One game line with one top duel between place 0 (blue) and place 5 (red), marked with the −2.</summary>
    private static int[] Line(int blueChampion, int redChampion, int blueLevel, int redLevel, int blueGold, int redGold, bool blueWins, bool marked = true)
    {
        var line = new int[RiotGames.FixedLength];
        for (var place = 0; place < 10; place++)
            line[place] = (100 + place) * 8 + place % 5 + 1;    // fillers on their lanes
        line[0] = blueChampion * 8 + 0 + 1;
        line[5] = redChampion * 8 + 0 + 1;
        line[13] = -1;
        int[] duel = [13, 400, blueWins ? 0 : 5, blueWins ? 5 : 0, 0, blueLevel, blueGold, 0, 0, 5, redLevel, redGold, 0, 0];
        return marked ? [.. line, .. duel, RiotGames.LevelsBeforeDuel] : [.. line, .. duel];
    }

    private static List<int[]> World(int duels, int seed = 7)
    {
        var random = new Random(seed);
        double Gauss() => Math.Sqrt(-2 * Math.Log(random.NextDouble() + 1e-12)) * Math.Cos(2 * Math.PI * random.NextDouble());
        var lines = new List<int[]>();
        for (var i = 0; i < duels; i++)
        {
            var blue = 1 + random.Next(4);
            var red = 1 + random.Next(3);
            if (red >= blue)
                red++;
            var early = i % 3 == 0;
            var level = early ? 3 + random.Next(2) : 7 + random.Next(3);
            var (blueLevel, redLevel) = (level + random.Next(-1, 2), level + random.Next(-1, 2));
            var (blueGold, redGold) = ((int)(2000 + 600 * Gauss()), (int)(2000 + 600 * Gauss()));
            // Truth: Level 1–5 0,9 per level and 1,3 per 1.000 gold; Level 6–10 0,5 and 0,7.
            var phase = LaneScaleModel.PhaseOf((blueLevel + redLevel) / 2.0);
            var (wl, wg) = phase == 0 ? (0.9, 1.3) : (0.5, 0.7);
            var z = wl * (blueLevel - redLevel) + wg * (blueGold - redGold) / 1000.0;
            if (phase == 1)
                z += (blue == Strong ? 0.6 : 0) - (red == Strong ? 0.6 : 0);
            lines.Add(Line(blue, red, blueLevel, redLevel, blueGold, redGold, random.NextDouble() < 1 / (1 + Math.Exp(-z))));
        }
        return lines;
    }

    [Fact]
    public void FindsTheWeightsPerPhaseAndTheStrongerChampion()
    {
        var scale = LaneScaleModel.Fit(World(30_000), minimumDuels: 1000)!;

        Assert.Equal(30_000, scale.Duels);
        Assert.Equal(LaneScaleModel.Tau, scale.Tau);
        var early = scale.Weights.Single(w => w.Lane == 0 && w.Phase == 0);
        var middle = scale.Weights.Single(w => w.Lane == 0 && w.Phase == 1);
        Assert.InRange(early.Level, 0.75, 1.05);
        Assert.InRange(early.Gold, 1.1, 1.5);
        Assert.InRange(middle.Level, 0.4, 0.6);
        Assert.InRange(middle.Gold, 0.6, 0.8);
        Assert.Equal(12, scale.Weights.Count);                      // top, jungle, mid and bot, three phases each

        // Only differences between champions mean anything (each duel has one of each side): champion 1
        // stands 0,6 above the others from level 6, and nobody stands out before.
        double Strength(int champion, int phase) => scale.Champions.Single(s => s.ChampionId == champion && s.Lane == 0 && s.Phase == phase).Strength;
        Assert.InRange(Strength(Strong, 1) - Strength(2, 1), 0.45, 0.75);
        Assert.InRange(Strength(Strong, 1) - Strength(3, 1), 0.45, 0.75);
        Assert.InRange(Math.Abs(Strength(2, 1) - Strength(3, 1)), 0, 0.15);
        Assert.InRange(Math.Abs(Strength(Strong, 0) - Strength(2, 0)), 0, 0.2);
        Assert.All(scale.Champions, s => Assert.True(s.Duels > 0));
    }

    [Fact]
    public void ReadsOnlyMarkedLinesAndNeedsEnoughOfThem()
    {
        var lines = World(2000);
        lines.Add(Line(1, 2, 8, 8, 2000, 2000, blueWins: true, marked: false));     // counted before 13.11.2026
        lines.Add(new int[RiotGames.DraftLength]);                                 // no lane part at all

        Assert.Equal(2000, LaneScaleModel.Fit(lines, minimumDuels: 1000)!.Duels);
        Assert.Null(LaneScaleModel.Fit(lines, minimumDuels: 2001));
        Assert.Null(LaneScaleModel.Fit(lines));                                   // far below 40.000
    }

    [Fact]
    public void LandsInMetaJsonUnderTheNamesTheAppReads()
    {
        var snapshot = new MetaSnapshot
        {
            LaneScale = new LaneScale
            {
                Duels = 391_952,
                Tau = 0.45,
                Weights = [new LaneScaleWeights(0, 0, 0.78, 1.2)],
                Champions = [new DuelStrength(75, 0, 2, 1.077, 716)],
            },
        };

        var json = JsonSerializer.Serialize(snapshot, MetaJson.Default.MetaSnapshot);
        Assert.Contains("\"laneScale\":{\"duels\":391952,\"tau\":0.45,\"weights\":[{\"lane\":0,\"phase\":0,\"level\":0.78,\"gold\":1.2}],\"champions\":[{\"championId\":75,\"lane\":0,\"phase\":2,\"strength\":1.077,\"duels\":716}]}", json);
        var back = JsonSerializer.Deserialize(json, MetaJson.Default.MetaSnapshot)!;
        Assert.Equal(1.077, back.LaneScale!.Champions[0].Strength);

        // Without it (OP.GG files, too few duels) the field is left out, not written as null.
        Assert.DoesNotContain("laneScale", JsonSerializer.Serialize(new MetaSnapshot(), MetaJson.Default.MetaSnapshot));
    }
}
