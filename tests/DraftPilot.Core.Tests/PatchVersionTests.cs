using DraftPilot.Core.Data;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Three sources write the patch three ways, and the footer's warning hangs on comparing them
/// correctly. A false alarm here would tell the user to spend two minutes on an update they do not
/// need; a missed one leaves last patch's tierlist standing without a word.
/// </summary>
public class PatchVersionTests
{
    [Theory]
    // Data Dragon.
    [InlineData("16.18.1", "16.18")]
    // OP.GG's aggregate.
    [InlineData("16.18", "16.18")]
    // The League client, verbatim from /lol-patch/v1/game-version.
    [InlineData("16.18.8175716+branch.releases-16-18.code.public.content.release", "16.18")]
    [InlineData("16.18+branch.releases", "16.18")]
    [InlineData("9.24.1", "9.24")]
    public void EverySpelling_ReducesToItsPatchLine(string value, string expected)
        => Assert.Equal(expected, PatchVersion.Line(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("16")]
    [InlineData("unbekannt")]
    [InlineData("v16.18")]
    [InlineData("16.")]
    public void WhatIsNotAPatchLine_ReadsAsUnknown(string? value)
        => Assert.Equal(string.Empty, PatchVersion.Line(value));

    [Fact]
    public void TheSamePatchInDifferentSpellings_Agrees()
    {
        Assert.True(PatchVersion.SameLine("16.18.1", "16.18.8175716+branch.releases-16-18"));
        Assert.True(PatchVersion.SameLine("16.18", "16.18.1"));
    }

    [Fact]
    public void ADifferentPatch_Disagrees()
    {
        Assert.False(PatchVersion.SameLine("16.17", "16.18.8175716+branch"));
        Assert.False(PatchVersion.SameLine("15.24.1", "16.1.1"));
    }

    /// <summary>
    /// An unreadable version must never produce a warning. The comparison exists to catch a real
    /// patch change, and "the client answered something I cannot parse" is not one.
    /// </summary>
    [Theory]
    [InlineData("16.18", null)]
    [InlineData("16.18", "")]
    [InlineData("", "16.18")]
    [InlineData("unbekannt", "16.18")]
    public void AnUnknownOnEitherSide_CountsAsAgreement(string? left, string? right)
        => Assert.True(PatchVersion.SameLine(left, right));
}
