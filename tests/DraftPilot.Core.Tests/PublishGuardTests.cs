using DraftPilot.Meta.Riot;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Before the push: a thin count never replaces a full one on the same patch (the collector's state
/// lives in a cache that can be gone), a new patch and the first file pass.
/// </summary>
public sealed class PublishGuardTests
{
    private static string File(string patch, int games) => $$"""{"version":1,"patch":"{{patch}}","games":{{games}},"champions":[]}""";

    [Fact]
    public void TheSamePatchWithACollapse_IsNotPublished()
    {
        var verdict = PublishGuard.Check(File("16.23", 270_000), File("16.23", 4_200));

        Assert.False(verdict.Publish);
        Assert.Equal((270_000, 4_200), (verdict.PublishedGames, verdict.NewGames));
        // Both numbers are in the sentence the run shows.
        Assert.Contains("4.200", verdict.Reason);
        Assert.Contains("270.000", verdict.Reason);
    }

    [Theory]
    [InlineData(270_000, 134_999, false)]   // just under half
    [InlineData(270_000, 135_000, true)]    // half is enough: a backup from yesterday
    [InlineData(270_000, 271_500, true)]    // the usual run
    public void OnTheSamePatch_HalfThePublishedCountIsTheLine(int published, int fresh, bool publish)
        => Assert.Equal(publish, PublishGuard.Check(File("16.23", published), File("16.23", fresh)).Publish);

    [Fact]
    public void ANewPatch_IsPublished_HoweverSmall()
    {
        var verdict = PublishGuard.Check(File("16.22", 270_000), File("16.23", 2_100));

        Assert.True(verdict.Publish);
        Assert.Contains("Neuer Patch 16.23", verdict.Reason);
        // The full version string of a file reads as its patch line.
        Assert.True(PublishGuard.Check(File("16.22.1", 270_000), File("16.23.1", 2_100)).Publish);
        Assert.False(PublishGuard.Check(File("16.23.1", 270_000), File("16.23", 2_100)).Publish);
    }

    [Fact]
    public void TheFirstFile_IsPublished()
    {
        Assert.True(PublishGuard.Check(null, File("16.23", 2_100)).Publish);
        Assert.True(PublishGuard.Check(string.Empty, File("16.23", 2_100)).Publish);
        // A published file nobody can read is no reason to hold the new one back.
        Assert.True(PublishGuard.Check("{ not json", File("16.23", 2_100)).Publish);
    }

    [Fact]
    public void AnEmptyCount_IsNeverPublished()
    {
        // A state that failed to load exports no patch and no games — with a different "patch", too.
        Assert.False(PublishGuard.Check(File("16.23", 270_000), File(string.Empty, 0)).Publish);
        Assert.False(PublishGuard.Check(null, File("16.23", 0)).Publish);
        Assert.False(PublishGuard.Check(File("16.23", 270_000), null).Publish);
        Assert.False(PublishGuard.Check(File("16.23", 270_000), "[]").Publish);
    }

    [Fact]
    public void APatchThatIsOver_NeverReplacesTheCurrentOne()
    {
        // A state restored from an old backup: its count is large, and a patch behind.
        var verdict = PublishGuard.Check(File("16.23", 30_000), File("16.22", 270_000));

        Assert.False(verdict.Publish);
        Assert.Contains("nicht zurück", verdict.Reason);
    }
}
