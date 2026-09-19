using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu.Models;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The Abyss decision: keep the champion you were handed, or take one off the bench. Field names
/// for the bench come from the client's own schema (<c>/help?format=Full</c>, type
/// <c>BenchChampion</c>), read on 2026-09-19 — not from guessing at a payload.
/// </summary>
public class AramAdvisorTests
{
    private const int Mine = 1;
    private const int Strong = 2;
    private const int Weak = 3;

    private static string Name(int id) => id switch
    {
        Mine => "Meiner",
        Strong => "Stark",
        Weak => "Schwach",
        _ => $"#{id}",
    };

    private static Dictionary<int, AramStat> Stats(params (int Champion, double WinRate, int Play)[] rows)
        => rows.ToDictionary(row => row.Champion, row => new AramStat(row.Champion, row.WinRate, row.Play));

    [Fact]
    public void TheBetterRecordLeads()
    {
        var rows = AramAdvisor.Rank(
            Mine,
            [Strong, Weak],
            Stats((Mine, 0.49, 20_000), (Strong, 0.55, 20_000), (Weak, 0.45, 20_000)),
            Name);

        Assert.Equal(["Stark", "Meiner", "Schwach"], rows.Select(row => row.Name));
    }

    /// <summary>
    /// The champion in hand belongs in the list even when it loses. A bench ranked without it
    /// answers "which of these is best" when the question is "is any of them better than mine".
    /// </summary>
    [Fact]
    public void TheChampionInHandIsAlwaysInTheList()
    {
        var rows = AramAdvisor.Rank(Mine, [Strong], Stats((Mine, 0.45, 20_000), (Strong, 0.55, 20_000)), Name);

        Assert.Contains(rows, row => row.ChampionId == Mine);
        Assert.Contains(rows, row => row.Reasons.Any(reason => reason.Text == "dein Champ"));
    }

    /// <summary>
    /// Without a number for the champion in hand there is nothing to compare against, and a list of
    /// bench champions alone would read as "swap to the top one" without saying what you give up.
    /// </summary>
    [Fact]
    public void WithoutANumberForTheOwnChampion_TheListStaysEmpty()
    {
        var rows = AramAdvisor.Rank(Mine, [Strong], Stats((Strong, 0.55, 20_000)), Name);

        Assert.Empty(rows);
    }

    [Fact]
    public void ChampionsWithoutDataAreLeftOutRatherThanRankedAtZero()
    {
        var rows = AramAdvisor.Rank(
            Mine, [Strong, Weak], Stats((Mine, 0.50, 20_000), (Strong, 0.53, 20_000)), Name);

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, row => row.ChampionId == Weak);
    }

    /// <summary>A champion sitting on the bench AND in your hand is one row, not two.</summary>
    [Fact]
    public void TheOwnChampionIsNotDuplicatedByTheBench()
    {
        var rows = AramAdvisor.Rank(Mine, [Mine, Strong], Stats((Mine, 0.50, 9_000), (Strong, 0.51, 9_000)), Name);

        Assert.Equal(2, rows.Count);
        Assert.Single(rows, row => row.ChampionId == Mine);
    }

    /// <summary>
    /// A handful of games must not lead the bench. The reported rate is the same 60 % either way;
    /// only the sample tells them apart, and shrinkage is what makes it tell.
    /// </summary>
    [Fact]
    public void AThinSampleDoesNotLeadTheBench()
    {
        var rows = AramAdvisor.Rank(
            Mine, [Strong], Stats((Mine, 0.53, 30_000), (Strong, 0.60, 12)), Name);

        Assert.Equal("Meiner", rows[0].Name);
    }

    /// <summary>
    /// OP.GG reports the ARAM win rate rounded to two decimals and offers nothing exact to
    /// reconstruct it from — measured 2026-09-19, where the <c>positions</c> block comes back null.
    /// One percentage point of resolution is real error and has to be inside the bar, or the list
    /// would order a coin flip with confidence at large sample sizes.
    /// </summary>
    [Fact]
    public void TheRoundingOfTheReportedRateIsInsideTheErrorBar()
    {
        var rows = AramAdvisor.Rank(Mine, [], Stats((Mine, 0.52, 400_000)), Name);

        // At 400.000 games the sampling error is negligible, so what is left is the rounding.
        var floor = Math.Sqrt(AramAdvisor.RoundingVariance) / (0.52 * (1 - 0.52));

        Assert.True(
            rows[0].Uncertainty > floor * 0.9,
            $"Fehlerbalken {rows[0].Uncertainty:F5} unterschreitet die Rundung allein ({floor:F5}).");
    }

    /// <summary>
    /// How much the rounding actually costs. At 400.000 games the sampling error alone would put
    /// the bar near a third of a percentage point; the rounding dominates it several times over,
    /// and leaving it out would let the list order differences the source cannot see.
    /// <para>
    /// Note what this does NOT say: a one-point gap stays resolvable. A step of 0.01 carries a
    /// standard deviation of 0.01/sqrt(12) ≈ 0.3 points, so a full point is still three sigma. It
    /// is the sub-point distinctions the bar now refuses, which is exactly right.
    /// </para>
    /// </summary>
    [Fact]
    public void AtLargeSamplesTheRoundingDominatesTheErrorBar()
    {
        var rows = AramAdvisor.Rank(Mine, [], Stats((Mine, 0.52, 400_000)), Name);

        var samplingOnly = Math.Sqrt(
            ScoreError.LogitVariance(0.52, 400_000, AramAdvisor.Prior, 0.5));

        Assert.True(
            rows[0].Uncertainty > 3 * samplingOnly,
            $"Fehlerbalken {rows[0].Uncertainty:F5} gegen reine Stichprobe {samplingOnly:F5} — "
            + "die Rundung müsste hier klar überwiegen.");
    }

    /// <summary>
    /// The flip side, and the reason the bar is not simply made huge: a full percentage point of
    /// difference over real samples still separates two champions. ARAM win rates span roughly ten
    /// points, so a list that called everything tied would answer nothing.
    /// </summary>
    [Fact]
    public void AFullPointApartIsStillDecidable()
    {
        var rows = AramAdvisor.Rank(
            Mine, [Strong], Stats((Mine, 0.52, 400_000), (Strong, 0.53, 400_000)), Name);

        Assert.Equal("Stark", rows[0].Name);
        Assert.NotEqual(
            ScoreStanding.Tied,
            ScoreError.Standing(rows[0].Score, rows[0].Uncertainty, rows[1].Score, rows[1].Uncertainty));
    }

    // ----- the bench as it arrives from the client -------------------------------------------

    private static ChampSelectSession Session()
    {
        var session = new ChampSelectSession { LocalPlayerCellId = 0, QueueId = 450, BenchEnabled = true };
        session.MyTeam.Add(new ChampSelectPlayer { CellId = 0, ChampionId = Mine, Team = 1 });

        return session;
    }

    [Fact]
    public void TheBenchTravelsFromTheSessionIntoTheDraftState()
    {
        var session = Session();
        session.BenchChampions.Add(new BenchChampion { ChampionId = Strong });
        session.BenchChampions.Add(new BenchChampion { ChampionId = Weak, IsPriority = true });
        session.RerollsRemaining = 2;

        var state = DraftState.From(session);

        Assert.True(state.BenchEnabled);
        Assert.Equal([Strong, Weak], state.Bench);
        Assert.Equal(2, state.RerollsRemaining);
    }

    /// <summary>
    /// The client repeats an entry while a swap is in flight, and it pads the list with zero ids.
    /// Either one would put a champion on the bench twice or rank a champion that is not there.
    /// </summary>
    [Fact]
    public void EmptyAndRepeatedBenchEntriesAreDropped()
    {
        var session = Session();
        session.BenchChampions.Add(new BenchChampion { ChampionId = Strong });
        session.BenchChampions.Add(new BenchChampion { ChampionId = 0 });
        session.BenchChampions.Add(new BenchChampion { ChampionId = Strong });

        Assert.Equal([Strong], DraftState.From(session).Bench);
    }

    /// <summary>A mode without a bench must not look like a bench that is momentarily empty.</summary>
    [Fact]
    public void ARiftDraftHasNoBenchAtAll()
    {
        var session = new ChampSelectSession { LocalPlayerCellId = 0, QueueId = 420 };
        session.MyTeam.Add(new ChampSelectPlayer { CellId = 0, ChampionId = Mine, Team = 1 });

        var state = DraftState.From(session);

        Assert.False(state.BenchEnabled);
        Assert.Empty(state.Bench);
    }

    /// <summary>A recording from before the fields existed must parse, not throw.</summary>
    [Fact]
    public void ASessionWithoutBenchFields_IsNotABench()
    {
        var state = DraftState.From(Session());

        Assert.True(state.BenchEnabled);
        Assert.Empty(state.Bench);
        Assert.Equal(0, state.RerollsRemaining);
    }
}
