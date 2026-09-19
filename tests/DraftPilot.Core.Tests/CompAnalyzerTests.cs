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

    /// <summary>
    /// Matches a chip by a distinctive fragment rather than its full wording, and insists it carries
    /// an explanation: a chip whose text is jargon and whose tooltip is empty is unusable mid-draft.
    /// </summary>
    private static void AssertChip(IEnumerable<Reason> reasons, string fragment, ReasonTone tone)
    {
        var match = reasons.FirstOrDefault(reason =>
            reason.Text.Contains(fragment, StringComparison.Ordinal) && reason.Tone == tone);

        Assert.NotNull(match);
        Assert.False(string.IsNullOrWhiteSpace(match.Hint), $"Chip \"{match.Text}\" braucht eine Erklärung.");
    }

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

        var score = analyzer.Fit(Ap1, profile, CompProfile.Empty, reasons);

        Assert.True(score > 0);
        AssertChip(reasons, "AP-Schaden", ReasonTone.Pro);
    }

    [Fact]
    public void Fit_RewardsBringingFrontline()
    {
        var analyzer = Analyzer();
        var profile = analyzer.Analyze([Ad3, Ap1, Ap2]);
        var reasons = new List<Reason>();

        analyzer.Fit(TankTagged, profile, CompProfile.Empty, reasons);

        AssertChip(reasons, "steht vorne", ReasonTone.Pro);
    }

    [Fact]
    public void Fit_RewardsBringingEngage()
    {
        var traits = Traits(("Ad1", 0, 0, 1), ("Ad2", 0, 1, 1), ("Ad3", 0, 0, 1), ("TankTag", 2, 2, 3));
        var analyzer = Analyzer(traits);
        var profile = analyzer.Analyze([Ad1, Ad2, Ad3]);
        var reasons = new List<Reason>();

        analyzer.Fit(TankTagged, profile, CompProfile.Empty, reasons);

        AssertChip(reasons, "kann engagen", ReasonTone.Pro);
    }

    [Fact]
    public void Fit_PenalisesDeepeningAnImbalance()
    {
        var analyzer = Analyzer();
        // Three physical plus one magic: no rule fires, but the comp is still physical-heavy.
        var profile = analyzer.Analyze([Ad1, Ad2, Ad3, Ap1]);
        var reasons = new List<Reason>();

        var score = analyzer.Fit(Ad4, profile, CompProfile.Empty, reasons);

        Assert.True(score < 0);
        AssertChip(reasons, "mehr AD-Schaden", ReasonTone.Contra);
    }

    /// <summary>
    /// No verdict at all rather than a zero: the breakdown shows the two differently, and claiming
    /// "neutral" about a champion nothing is known of would be a lie.
    /// </summary>
    [Fact]
    public void Fit_SaysNothingForChampionsWithNoData()
    {
        var analyzer = Analyzer();
        var profile = analyzer.Analyze([Ad1, Ad2, Ad3]);
        var reasons = new List<Reason>();

        Assert.Null(analyzer.Fit(Unknown, profile, CompProfile.Empty, reasons));
        Assert.Empty(reasons);
    }

    [Fact]
    public void Fit_SaysNothingAgainstAnEmptyComposition()
    {
        var analyzer = Analyzer();

        Assert.Null(analyzer.Fit(Ap1, CompProfile.Empty, CompProfile.Empty, []));
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

    /// <summary>
    /// "No data → silence": without Data Dragon statics (an update run while the CDN was down)
    /// frontline and range are unknowable, and flagging every composition as "kein Frontline"
    /// would be a permanent false alarm.
    /// </summary>
    [Fact]
    public void MissingStaticData_KeepsFrontlineAndMeleeRulesSilent()
    {
        var bare = new MetaBuilder()
            .Champion(1, "A", DamageType.Physical)
            .Champion(2, "B", DamageType.Physical)
            .Champion(3, "C", DamageType.Magic)
            .Champion(4, "D", DamageType.Magic)
            .Build();

        var profile = new CompAnalyzer(bare, TraitTable.Empty).Analyze([1, 2, 3, 4]);

        Assert.False(profile.Has(CompIssue.NoFrontline));
        Assert.False(profile.Has(CompIssue.AllMelee));
    }

    [Fact]
    public void CcFloor_IsOnePiecePerTwoChampions()
    {
        // Two CC points across three champions meets the documented floor of one per two.
        var traits = Traits(("Ad1", 0, 0, 1), ("Ad2", 0, 0, 1), ("Ap1", 0, 0, 0));

        var profile = Analyzer(traits).Analyze([Ad1, Ad2, Ap1]);

        Assert.False(profile.Has(CompIssue.LittleCrowdControl));
    }

    [Fact]
    public void CcClearlyBelowTheFloor_IsFlagged()
    {
        var traits = Traits(("Ad1", 0, 0, 1), ("Ad2", 0, 0, 0), ("Ap1", 0, 0, 0));

        var profile = Analyzer(traits).Analyze([Ad1, Ad2, Ap1]);

        Assert.True(profile.Has(CompIssue.LittleCrowdControl));
    }

    /// <summary>
    /// ScoreModel.CompScale is calibrated for -1..+1 ("a covered gap ≈ +4 points, never more").
    /// A candidate covering every gap at once must saturate there, not stack to twice that.
    /// </summary>
    [Fact]
    public void Fit_IsClampedToPlusMinusOne()
    {
        // Three AD melee champions with no engage, peel or CC: every finding at once.
        var traits = Traits(("Ad1", 0, 0, 0), ("Ad2", 0, 0, 0), ("Ad3", 0, 0, 0), ("TankTag", 3, 3, 3));
        var analyzer = Analyzer(traits);
        var profile = analyzer.Analyze([Ad1, Ad2, Ad3]);

        // A ranged magic tank with hard engage, peel and CC covers all of them.
        var fit = analyzer.Fit(TankTagged, profile, CompProfile.Empty, []);

        Assert.NotNull(fit);
        Assert.InRange(fit!.Value, -1, 1);
    }

    /// <summary>
    /// The damage mix is shown as two shares. "0 % / 0 %" would read as a team that deals no
    /// damage, where the truth is that no champion's damage type is known yet.
    /// </summary>
    [Fact]
    public void WithoutASingleKnownDamageType_ThereIsNoMixToShow()
    {
        Assert.False(CompProfile.Empty.HasDamageMix);
        Assert.False(Analyzer().Analyze([Unknown]).HasDamageMix);
        Assert.True(Analyzer().Analyze([Unknown, Ad1]).HasDamageMix);
    }

    // --- What the pick does about the ENEMY line-up. ----------------------------------------
    // These rules are the mirror of the block above. Each one is pinned in both directions: the
    // situation it is for, and a situation where it must stay silent — a rule that fires on every
    // draft is a constant, not a reason.

    private static TraitTable ScalingTraits(params (string Key, ScalingCurve Scaling)[] entries)
        => TraitTable.FromEntries(entries.Select(entry => new KeyValuePair<string, ChampionTraits>(
            entry.Key,
            new ChampionTraits(0, 0, 1, entry.Scaling, 1, true))));

    [Fact]
    public void Fit_RewardsPeelAgainstAnEnemyThatCanEngage()
    {
        var traits = Traits(("TankTag", 2, 2, 3), ("Ad4", 0, 2, 0), ("Ad1", 0, 0, 1), ("Ad2", 0, 0, 1));
        var analyzer = Analyzer(traits);
        var enemies = analyzer.Analyze([TankTagged, Ad1, Ad2]);
        var reasons = new List<Reason>();

        var score = analyzer.Fit(Ad4, CompProfile.Empty, enemies, reasons);

        Assert.True(score > 0);
        AssertChip(reasons, "Engage auf", ReasonTone.Pro);
    }

    [Fact]
    public void Fit_StaysSilentOnPeelWhenTheEnemyCannotEngage()
    {
        var traits = Traits(("Ad1", 0, 0, 1), ("Ad2", 0, 1, 1), ("Ad3", 1, 0, 1), ("Ad4", 0, 2, 0));
        var analyzer = Analyzer(traits);
        var enemies = analyzer.Analyze([Ad1, Ad2, Ad3]);
        var reasons = new List<Reason>();

        analyzer.Fit(Ad4, CompProfile.Empty, enemies, reasons);

        Assert.DoesNotContain(reasons, reason => reason.Text.Contains("Engage", StringComparison.Ordinal));
    }

    [Fact]
    public void Fit_RewardsAnAssassinAgainstAnOpenBackline()
    {
        var analyzer = Analyzer();
        // Two mages and a marksman: nobody with a tank tag or six defence.
        var enemies = analyzer.Analyze([Ap1, Ap2, Ad4]);
        var reasons = new List<Reason>();

        var score = analyzer.Fit(Ad3, CompProfile.Empty, enemies, reasons);

        Assert.True(score > 0);
        AssertChip(reasons, "Backline steht frei", ReasonTone.Pro);
    }

    /// <summary>
    /// The counterweight to the rule above. Without it the term would only ever reward the same
    /// champions, and "the enemy composition is read" would mean "assassins get a bonus".
    /// </summary>
    [Fact]
    public void Fit_PenalisesAnAssassinAgainstTwoFrontliners()
    {
        var analyzer = Analyzer();
        var enemies = analyzer.Analyze([TankTagged, TankByDefense, Ap1]);
        var reasons = new List<Reason>();

        var score = analyzer.Fit(Ad3, CompProfile.Empty, enemies, reasons);

        Assert.True(score < 0);
        AssertChip(reasons, "an ihre Carrys", ReasonTone.Contra);
    }

    [Fact]
    public void Fit_RewardsAnEarlyChampionAgainstALateEnemy()
    {
        var traits = ScalingTraits(
            ("Ad1", ScalingCurve.Late), ("Ad2", ScalingCurve.Late), ("Ad3", ScalingCurve.Late),
            ("Ap1", ScalingCurve.Early));

        var analyzer = Analyzer(traits);
        var enemies = analyzer.Analyze([Ad1, Ad2, Ad3]);
        var reasons = new List<Reason>();

        var score = analyzer.Fit(Ap1, CompProfile.Empty, enemies, reasons);

        Assert.True(score > 0);
        AssertChip(reasons, "Earlygame", ReasonTone.Pro);
    }

    /// <summary>
    /// First pick of the own team, two enemies already revealed: the old signature had nothing to
    /// say there, because it only ever looked at team-mates. That is exactly the moment with the
    /// least other evidence — no duel, no duo, no lane opponent.
    /// </summary>
    [Fact]
    public void Fit_JudgesTheEnemyBeforeTheOwnTeamHasPicked()
    {
        var analyzer = Analyzer();
        var enemies = analyzer.Analyze([Ap1, Ap2, Ad4]);

        Assert.NotNull(analyzer.Fit(Ad3, CompProfile.Empty, enemies, []));
    }

    /// <summary>
    /// The enemy rules share the ally rules' cap on purpose: the term answers one question, and
    /// looking at both halves of the board may make it better informed, not louder.
    /// </summary>
    [Fact]
    public void Fit_StaysClampedWhenBothSidesArgueForThePick()
    {
        var traits = Traits(("Ad1", 0, 0, 0), ("Ad2", 0, 0, 0), ("Ad3", 0, 0, 0), ("TankTag", 3, 3, 3));
        var analyzer = Analyzer(traits);
        var allies = analyzer.Analyze([Ad1, Ad2, Ad3]);
        var enemies = analyzer.Analyze([Ad1, Ad2, Ad3]);

        var fit = analyzer.Fit(TankTagged, allies, enemies, []);

        Assert.NotNull(fit);
        Assert.InRange(fit!.Value, -1, 1);
    }
}
