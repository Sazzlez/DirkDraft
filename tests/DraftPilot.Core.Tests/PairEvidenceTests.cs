using DraftPilot.Core.Data;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// How much of a pair's own count to trust (<see cref="PairEvidence"/>): the two-kind pull keeps a
/// small deviation near the expectation and lets a large, well-counted one stand; on pairs where a few
/// are real counters it predicts held-out games better than any single linear weight; and the
/// confirmation test finds noise as noise and a real counter as real.
/// </summary>
public class PairEvidenceTests
{
    [Fact]
    public void LogGamma_MatchesKnownValues()
    {
        Assert.Equal(Math.Log(24), PairEvidence.LogGamma(5), 10);
        Assert.Equal(0.5 * Math.Log(Math.PI), PairEvidence.LogGamma(0.5), 10);
        Assert.Equal(Math.Log(1234.5), PairEvidence.LogGamma(1235.5) - PairEvidence.LogGamma(1234.5), 8);
    }

    [Fact]
    public void Mixture_PullsASmallDeviationInAndLetsALargeOneStand()
    {
        // Two edges of the same pull: nothing believed, everything believed.
        Assert.Equal(0.5, PairEvidence.Mixture(60, 100, 0.5, share: 0, kappa: 100));
        Assert.Equal(PairEvidence.Linear(60, 100, 0.5, 100), PairEvidence.Mixture(60, 100, 0.5, share: 1, kappa: 100), 12);

        // 52 of 100: chance alone gives that easily — pulled in harder than the linear pull at κ₁.
        var small = PairEvidence.Mixture(52, 100, 0.5, share: 0.05, kappa: 100);
        Assert.InRange(small, 0.5, 0.505);
        Assert.True(small < PairEvidence.Linear(52, 100, 0.5, 100));

        // 160 of 250 (64 %): chance hardly gives that — it stands, where today's κ = 1.000 keeps little.
        var large = PairEvidence.Mixture(160, 250, 0.5, share: 0.05, kappa: 100);
        Assert.InRange(large, 0.59, PairEvidence.Linear(160, 250, 0.5, 100) + 1e-12);
        Assert.True(PairEvidence.Linear(160, 250, 0.5, 1000) < 0.53);
    }

    /// <summary>2.000 pairs of 300 games: <paramref name="share"/> of them move ±12 points, the rest nothing.</summary>
    private static List<PairEvidence.Row> World(double share, int seed)
    {
        var random = new Random(seed);
        var rows = new List<PairEvidence.Row>();
        for (var pair = 0; pair < 2000; pair++)
        {
            var rate = random.NextDouble() < share ? (random.Next(2) == 0 ? 0.62 : 0.38) : 0.5;
            var wins = 0;
            for (var game = 0; game < 300; game++)
            {
                if (random.NextDouble() < rate)
                    wins++;
            }
            rows.Add(new PairEvidence.Row(wins, 300, 0.5));
        }
        return rows;
    }

    [Fact]
    public void HeldOutLoss_PrefersTheMixtureWhereAFewPairsAreRealCounters()
    {
        var rows = World(share: 0.05, seed: 3);
        int[] weights = [0, 50, 100, 200, 300, 500, 800, 1000, 2000, 5000, int.MaxValue];
        var estimators = weights
            .Select(kappa => (Func<int, int, double, double>)((wins, games, expected) => PairEvidence.Linear(wins, games, expected, kappa)))
            .Append((wins, games, expected) => PairEvidence.Mixture(wins, games, expected, 0.05, 100))
            .ToList();

        var loss = PairEvidence.HeldOutLoss(rows, repetitions: 20, estimators);

        Assert.True(loss[^1] < loss[..^1].Min());
    }

    [Fact]
    public void Confirm_FindsNoiseAsNoiseAndACounterAsACounter()
    {
        var noise = PairEvidence.Confirm(World(share: 0, seed: 5), repetitions: 10, minimumGames: 40, minimumDeviation: 0.08, kappaToday: 1000);
        Assert.True(noise.PairsPerSplit > 0);
        Assert.InRange(noise.Retained, -0.1, 0.1);

        var counters = PairEvidence.Confirm(World(share: 1, seed: 7), repetitions: 10, minimumGames: 40, minimumDeviation: 0.08, kappaToday: 1000);
        Assert.True(counters.Retained > 0.6);
        // A linear pull at 1.000 keeps about 150 / 1.150 of a half's deviation.
        Assert.InRange(counters.RetainedToday, 0.1, 0.16);
        Assert.InRange(counters.MeanGames, 140, 160);
    }
}
