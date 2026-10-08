using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The duo expectation: what a pair should win from the two champions' lane strength alone. The
/// synergy term counts only what a duo does beyond it, so the line decides what "spielt gut mit"
/// means — and whether a candidate's own strength sneaks into the score a second time.
/// </summary>
public class SynergyLineTests
{
    /// <summary>Lane rates for champions 1..n, spread evenly between 47 % and 53 %.</summary>
    private static double? LaneRate(int championId, Lane lane) => 0.47 + ((championId % 61) * 0.001);

    /// <summary>
    /// Duo rows built to lie exactly on a known line, so the fit has a right answer to find.
    /// </summary>
    private static List<SynergyStat> OnTheLine(double intercept, double slope, int pairs)
    {
        var rows = new List<SynergyStat>(pairs);

        for (var i = 0; i < pairs; i++)
        {
            var mine = 1 + (i % 60);
            var partner = 100 + i;
            var x = ScoreModel.Logit(LaneRate(mine, Lane.Mid)!.Value) + ScoreModel.Logit(LaneRate(partner, Lane.Support)!.Value);

            rows.Add(new SynergyStat
            {
                ChampionId = mine,
                Lane = Lane.Mid,
                PartnerId = partner,
                PartnerLane = Lane.Support,
                WinRate = ScoreModel.Sigmoid(intercept + (slope * x)),
                Play = 500 + (i * 7),
            });
        }

        return rows;
    }

    [Fact]
    public void ALineInTheDataIsFound()
    {
        var line = SynergyLine.Fit(OnTheLine(0.05, 0.4, 300), LaneRate, fallback: 0.07);

        Assert.Equal(0.4, line.Slope, precision: 3);
        Assert.Equal(0.05, line.Intercept, precision: 3);
        Assert.Equal(300, line.Rows);
    }

    /// <summary>A slope fitted on a handful of pairs would multiply every candidate's strength by noise.</summary>
    [Fact]
    public void TooFewPairsFallBackToTheFlatBaseline()
    {
        var line = SynergyLine.Fit(OnTheLine(0.05, 0.4, SynergyLine.MinimumRows - 1), LaneRate, fallback: 0.07);

        Assert.Equal(SynergyLine.Flat(0.07), line);
        Assert.Equal(0.07, line.ExpectedLogit(0.3, -0.2), precision: 12);
    }

    /// <summary>
    /// Outside 0..1 the slope would mean something the model cannot: rewarding a weak partner, or
    /// counting strength more than once inside the duo itself.
    /// </summary>
    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.8, 1.0)]
    public void TheSlopeStaysWithinWhatItCanMean(double slope, double expected)
    {
        var line = SynergyLine.Fit(OnTheLine(0.0, slope, 200), LaneRate, fallback: 0);

        Assert.Equal(expected, line.Slope, precision: 9);
    }

    /// <summary>The same duo listed by both partners is one set of games, not two.</summary>
    [Fact]
    public void ADuoListedFromBothSidesCountsOnce()
    {
        var rows = OnTheLine(0.05, 0.4, 150);
        var mirrored = rows.Select(row => new SynergyStat
        {
            ChampionId = row.PartnerId,
            Lane = row.PartnerLane,
            PartnerId = row.ChampionId,
            PartnerLane = row.Lane,
            WinRate = row.WinRate,
            Play = row.Play,
        });

        var line = SynergyLine.Fit([.. rows, .. mirrored], LaneRate, fallback: 0);

        Assert.Equal(150, line.Rows);
    }

    /// <summary>Pairs without a lane row on either side cannot be placed on the line and are left out.</summary>
    [Fact]
    public void PairsWithoutLaneRowsAreLeftOut()
    {
        var line = SynergyLine.Fit(OnTheLine(0.05, 0.4, 200), (_, _) => null, fallback: 0.07);

        Assert.Equal(SynergyLine.Flat(0.07), line);
    }
}
