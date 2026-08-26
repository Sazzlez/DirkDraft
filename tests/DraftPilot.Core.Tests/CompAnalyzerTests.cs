using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

public class CompAnalyzerTests
{
    private const int Ad1 = 1, Ad2 = 2, Ad3 = 3, Ad4 = 4;
    private const int Ap1 = 11, Ap2 = 12, Ap3 = 13;
    private const int MixedOne = 21;
    private const int TankTagged = 31, TankByDefense = 32;
    private const int RangedOne = 41;
    private const int Unknown = 99;

    private static MetaLookup Meta() => new MetaBuilder()
        .Champion(Ad1, "Ad1", DamageType.Physical, ["Fighter"], attackRange: 175, defense: 4)
        .Champion(Ad2, "Ad2", DamageType.Physical, ["Fighter"], attackRange: 125, defense: 3)
        .Champion(Ad3, "Ad3", DamageType.Physical, ["Assassin"], attackRange: 125, defense: 2)
        .Champion(Ad4, "Ad4", DamageType.Physical, ["Marksman"], attackRange: 550, defense: 2)
        .Champion(Ap1, "Ap1", DamageType.Magic, ["Mage"], attackRange: 550, defense: 3)
        .Champion(Ap2, "Ap2", DamageType.Magic, ["Mage"], attackRange: 525, defense: 2)
        .Champion(Ap3, "Ap3", DamageType.Magic, ["Mage"], attackRange: 500, defense: 3)
        .Champion(MixedOne, "Mixed", DamageType.Mixed, ["Fighter"], attackRange: 175, defense: 5)
        .Champion(TankTagged, "TankTag", DamageType.Magic, ["Tank"], attackRange: 125, defense: 5)
        .Champion(TankByDefense, "TankDef", DamageType.Physical, ["Fighter"], attackRange: 175, defense: 8)
        .Champion(RangedOne, "Ranged", DamageType.Magic, ["Mage"], attackRange: 600, defense: 2)
        // No damage type, no tags, no range: a champion released after the data was written.
        .Champion(Unknown, "Unknown")
        .Build();

    private static TraitTable Traits(params (string Key, int Engage, int Peel, int Cc)[] entries)
        => TraitTable.FromEntries(entries.Select(entry => new KeyValuePair<string, ChampionTraits>(
            entry.Key,
            new ChampionTraits(entry.Engage, entry.Peel, entry.Cc, ScalingCurve.Mid, 1, true))));

    private static CompAnalyzer Analyzer(TraitTable? traits = null) => new(Meta(), traits ?? TraitTable.Empty);

    [Fact]
    public void EmptyComp_HasNoFindings()
    {
        Assert.Empty(Analyzer().Analyze([]).Findings);
    }

    [Fact]
    public void DamageRules_StaySilentBelowThreeChampions()
    {
        // Two physical champions is not yet a one-dimensional composition; it is an early draft.
        var profile = Analyzer().Analyze([Ad1, Ad2]);

        Assert.False(profile.Has(CompIssue.NoMagicDamage));
    }

    [Fact]
    public void AllPhysical_FlagsMissingMagicDamage()
    {
        var profile = Analyzer().Analyze([Ad1, Ad2, Ad3]);

        Assert.True(profile.Has(CompIssue.NoMagicDamage));
        Assert.False(profile.Has(CompIssue.NoPhysicalDamage));
        Assert.Equal(1.0, profile.PhysicalShare, precision: 6);
    }

    [Fact]
    public void AllMagic_FlagsMissingPhysicalDamage()
    {
        var profile = Analyzer().Analyze([Ap1, Ap2, Ap3]);

        Assert.True(profile.Has(CompIssue.NoPhysicalDamage));
        Assert.False(profile.Has(CompIssue.NoMagicDamage));
    }

    [Fact]
    public void MixedDamage_CountsHalfToEachSide()
    {
        var profile = Analyzer().Analyze([Ad1, Ad2, MixedOne]);

        Assert.Equal(2.5 / 3, profile.PhysicalShare, precision: 6);
        Assert.Equal(0.5 / 3, profile.MagicShare, precision: 6);
    }

    [Fact]
    public void ChampionsWithoutDamageData_DoNotCountTowardsTheRule()
    {
        // Three known physical plus one unknown must still read as physical-only, not as balanced.
        var profile = Analyzer().Analyze([Ad1, Ad2, Ad3, Unknown]);

        Assert.True(profile.Has(CompIssue.NoMagicDamage));
        Assert.Equal(1.0, profile.PhysicalShare, precision: 6);
    }

    [Fact]
    public void NoFrontline_IsFlagged()
    {
        Assert.True(Analyzer().Analyze([Ad3, Ap1, Ap2]).Has(CompIssue.NoFrontline));
    }

    [Theory]
    [InlineData(TankTagged)]
    [InlineData(TankByDefense)]
    public void Frontline_IsRecognisedByTagOrByDefensiveRating(int frontlineId)
    {
        var profile = Analyzer().Analyze([Ad3, Ap1, frontlineId]);

        Assert.False(profile.Has(CompIssue.NoFrontline));
        Assert.Equal(1, profile.FrontlineCount);
    }

    [Fact]
    public void AllMelee_IsFlaggedFromFourChampions()
    {
        var profile = Analyzer().Analyze([Ad1, Ad2, Ad3, TankByDefense]);

        Assert.True(profile.Has(CompIssue.AllMelee));
        Assert.Equal(0, profile.RangedCount);
    }

    [Fact]
    public void OneRangedChampion_ClearsTheMeleeFlag()
    {
        Assert.False(Analyzer().Analyze([Ad1, Ad2, Ad3, RangedOne]).Has(CompIssue.AllMelee));
    }

    [Fact]
    public void ChampionsWithoutStaticData_AreNotCountedAsMeleeOrFrontline()
    {
        // The unknown champion must not be quietly assumed melee, which would fire AllMelee wrongly.
        var profile = Analyzer().Analyze([RangedOne, Unknown, Unknown]);

        Assert.Equal(1, profile.RangedCount);
    }

    [Fact]
    public void TraitRules_StaySilentWithoutCuratedData()
    {
        // No traits loaded at all: engage, peel and CC cannot be judged, so nothing is claimed.
        var profile = Analyzer().Analyze([Ad1, Ad2, Ad3, Ad4]);

        Assert.False(profile.Has(CompIssue.NoEngage));
        Assert.False(profile.Has(CompIssue.NoPeel));
        Assert.False(profile.Has(CompIssue.LittleCrowdControl));
        Assert.Equal(0, profile.TraitCoverage);
    }

    [Fact]
    public void NoEngage_IsFlaggedWhenTraitsAreKnown()
    {
        var traits = Traits(("Ad1", 0, 0, 1), ("Ad2", 0, 1, 1), ("Ad3", 0, 0, 1));

        var profile = Analyzer(traits).Analyze([Ad1, Ad2, Ad3]);

        Assert.True(profile.Has(CompIssue.NoEngage));
        Assert.Equal(1.0, profile.TraitCoverage, precision: 6);
    }

    [Fact]
    public void HardEngage_ClearsTheEngageFlag()
    {
        var traits = Traits(("Ad1", 2, 0, 3), ("Ad2", 0, 1, 1), ("Ad3", 0, 0, 1));

        Assert.False(Analyzer(traits).Analyze([Ad1, Ad2, Ad3]).Has(CompIssue.NoEngage));
    }

    [Fact]
    public void LittleCrowdControl_IsFlagged()
    {
        var traits = Traits(("Ad1", 2, 0, 0), ("Ad2", 1, 1, 0), ("Ad3", 1, 0, 1));

        var profile = Analyzer(traits).Analyze([Ad1, Ad2, Ad3]);

        Assert.True(profile.Has(CompIssue.LittleCrowdControl));
        Assert.Equal(1, profile.TotalCrowdControl);
    }

    [Fact]
    public void PartialTraitCoverage_IsReported()
    {
        var traits = Traits(("Ad1", 2, 2, 3));

        var profile = Analyzer(traits).Analyze([Ad1, Ad2, Ad3]);

        Assert.Equal(1.0 / 3, profile.TraitCoverage, precision: 6);
        // One curated champion out of three is not enough to claim anything about engage.
        Assert.False(profile.Has(CompIssue.NoEngage));
    }

    [Fact]
    public void Fit_RewardsCoveringTheMissingDamageType()
    {
        var analyzer = Analyzer();
        var profile = analyzer.Analyze([Ad1, Ad2, Ad3]);
        var reasons = new List<Reason>();

        var score = analyzer.Fit(Ap1, profile, reasons);

        Assert.True(score > 0);
        Assert.Contains(reasons, reason => reason.Text == "deckt AP-Schaden" && reason.Tone == ReasonTone.Pro);
    }

    [Fact]
    public void Fit_RewardsBringingFrontline()
    {
        var analyzer = Analyzer();
        var profile = analyzer.Analyze([Ad3, Ap1, Ap2]);
        var reasons = new List<Reason>();

        analyzer.Fit(TankTagged, profile, reasons);

        Assert.Contains(reasons, reason => reason.Text == "bringt Frontline" && reason.Tone == ReasonTone.Pro);
    }

    [Fact]
    public void Fit_RewardsBringingEngage()
    {
        var traits = Traits(("Ad1", 0, 0, 1), ("Ad2", 0, 1, 1), ("Ad3", 0, 0, 1), ("TankTag", 2, 2, 3));
        var analyzer = Analyzer(traits);
        var profile = analyzer.Analyze([Ad1, Ad2, Ad3]);
        var reasons = new List<Reason>();

        analyzer.Fit(TankTagged, profile, reasons);

        Assert.Contains(reasons, reason => reason.Text == "bringt Engage" && reason.Tone == ReasonTone.Pro);
    }

    [Fact]
    public void Fit_PenalisesDeepeningAnImbalance()
    {
        var analyzer = Analyzer();
        // Three physical plus one magic: no rule fires, but the comp is still physical-heavy.
        var profile = analyzer.Analyze([Ad1, Ad2, Ad3, Ap1]);
        var reasons = new List<Reason>();

        var score = analyzer.Fit(Ad4, profile, reasons);

        Assert.True(score < 0);
        Assert.Contains(reasons, reason => reason.Text == "verstärkt AD-Übergewicht" && reason.Tone == ReasonTone.Contra);
    }

    [Fact]
    public void Fit_IsNeutralForChampionsWithNoData()
    {
        var analyzer = Analyzer();
        var profile = analyzer.Analyze([Ad1, Ad2, Ad3]);
        var reasons = new List<Reason>();

        Assert.Equal(0, analyzer.Fit(Unknown, profile, reasons));
        Assert.Empty(reasons);
    }

    [Fact]
    public void Fit_IsZeroAgainstAnEmptyComposition()
    {
        var analyzer = Analyzer();

        Assert.Equal(0, analyzer.Fit(Ap1, CompProfile.Empty, []));
    }

    [Fact]
    public void DuplicateChampions_AreCountedOnce()
    {
        var profile = Analyzer().Analyze([Ad1, Ad1, Ad1, Ad2]);

        Assert.Equal(2, profile.Count);
    }

    [Fact]
    public void ZeroIds_AreIgnored()
    {
        var profile = Analyzer().Analyze([0, Ad1, 0, Ad2, 0]);

        Assert.Equal(2, profile.Count);
    }
}
