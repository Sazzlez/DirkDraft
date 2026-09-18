using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The seat table is hand-editable JSON that feeds every lane prediction, and the shipped one is
/// deliberately flat — which means nothing in the suite exercised the loader at all: the fixture the
/// predictor test loads is flat too, so a broken parser would have read as "no hint", the same as a
/// working one. These tests use tables that are NOT flat, so the difference is visible.
/// </summary>
public class SeatPriorsTests
{
    private static string WriteTable(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"seat-priors-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ARowIsNormalised_SoOneSeatCannotOutweighTheOthers()
    {
        // Seat 0 leans top, and the row sums to 10 instead of 1.
        var path = WriteTable("""
            { "rows": [
                [6, 1, 1, 1, 1],
                [1, 1, 1, 1, 1],
                [1, 1, 1, 1, 1],
                [1, 1, 1, 1, 1],
                [1, 1, 1, 1, 1]
            ] }
            """);

        try
        {
            var priors = SeatPriors.Load(path);

            Assert.Equal(0.6, priors[0, Lane.Top], precision: 6);
            Assert.Equal(0.1, priors[0, Lane.Mid], precision: 6);
            Assert.Equal(0.2, priors[1, Lane.Top], precision: 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    // A cell that is not a number — the case a hand-edited file produces most easily.
    [InlineData("""{ "rows": [["viel",1,1,1,1],[1,1,1,1,1],[1,1,1,1,1],[1,1,1,1,1],[1,1,1,1,1]] }""")]
    // A row that sums to zero would divide by it.
    [InlineData("""{ "rows": [[0,0,0,0,0],[1,1,1,1,1],[1,1,1,1,1],[1,1,1,1,1],[1,1,1,1,1]] }""")]
    // Fewer rows than seats: an incomplete table is not a table.
    [InlineData("""{ "rows": [[1,1,1,1,1],[1,1,1,1,1]] }""")]
    // The key itself missing.
    [InlineData("""{ "kommentar": "nichts hier" }""")]
    // Not even JSON.
    [InlineData("kaputt")]
    public void ABrokenTable_FallsBackToNoHintAtAll(string json)
    {
        var path = WriteTable(json);

        try
        {
            var priors = SeatPriors.Load(path);

            foreach (var lane in Lanes.All)
                Assert.Equal(1.0 / Lanes.Count, priors[0, lane], precision: 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingFile_IsNotAnError()
    {
        var priors = SeatPriors.Load(Path.Combine(Path.GetTempPath(), "gibt-es-nicht.json"));

        Assert.Equal(1.0 / Lanes.Count, priors[2, Lane.Adc], precision: 6);
    }

    /// <summary>Out-of-range seats and the unknown lane answer with the flat value, not an exception.</summary>
    [Fact]
    public void ASeatOutsideTheTable_AnswersFlat()
    {
        var priors = SeatPriors.Uniform;

        Assert.Equal(1.0 / Lanes.Count, priors[-1, Lane.Top], precision: 6);
        Assert.Equal(1.0 / Lanes.Count, priors[99, Lane.Top], precision: 6);
        Assert.Equal(1.0 / Lanes.Count, priors[0, Lane.Unknown], precision: 6);
    }

    /// <summary>
    /// The shipped table is flat on purpose — the one recorded 5v5 session had seats 2 and 3 swapped
    /// against the canonical order, so the seat index says nothing we could verify. A test rather
    /// than a comment, because "the file is flat" is an assumption the rest of the tuning rests on.
    /// </summary>
    [Fact]
    public void TheShippedTable_IsStillFlat()
    {
        var priors = SeatPriors.Load();

        for (var seat = 0; seat < Lanes.Count; seat++)
        {
            foreach (var lane in Lanes.All)
                Assert.Equal(1.0 / Lanes.Count, priors[seat, lane], precision: 6);
        }
    }
}
