// Port of tests/DraftPilot.Core.Tests/CompAnalyzerTests.cs.

import { describe, expect, it } from 'vitest';
import { CompAnalyzer, EMPTY_PROFILE, hasDamageMix, hasIssue, traitCoverage } from '../../src/core/model/compAnalyzer';
import { DamageType, type MetaLookup } from '../../src/core/model/metaLookup';
import type { Reason, ReasonTone } from '../../src/core/model/reason';
import { TraitTable, type ScalingCurve } from '../../src/core/model/traits';
import { MetaBuilder } from './helpers';

const Ad1 = 1, Ad2 = 2, Ad3 = 3, Ad4 = 4;
const Ap1 = 11, Ap2 = 12, Ap3 = 13;
const MixedOne = 21;
const TankTagged = 31, TankByDefense = 32;
const RangedOne = 41;
const Unknown = 99;

function meta(): MetaLookup {
  return new MetaBuilder()
    .champion(Ad1, 'Ad1', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 4 })
    .champion(Ad2, 'Ad2', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 125, defense: 3 })
    .champion(Ad3, 'Ad3', { damage: DamageType.Physical, tags: ['Assassin'], attackRange: 125, defense: 2 })
    .champion(Ad4, 'Ad4', { damage: DamageType.Physical, tags: ['Marksman'], attackRange: 550, defense: 2 })
    .champion(Ap1, 'Ap1', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 550, defense: 3 })
    .champion(Ap2, 'Ap2', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 525, defense: 2 })
    .champion(Ap3, 'Ap3', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 500, defense: 3 })
    .champion(MixedOne, 'Mixed', { damage: DamageType.Mixed, tags: ['Fighter'], attackRange: 175, defense: 5 })
    .champion(TankTagged, 'TankTag', { damage: DamageType.Magic, tags: ['Tank'], attackRange: 125, defense: 5 })
    .champion(TankByDefense, 'TankDef', { damage: DamageType.Physical, tags: ['Fighter'], attackRange: 175, defense: 8 })
    .champion(RangedOne, 'Ranged', { damage: DamageType.Magic, tags: ['Mage'], attackRange: 600, defense: 2 })
    // No damage type, no tags, no range: a champion released after the data was written.
    .champion(Unknown, 'Unknown')
    .build();
}

function traits(...entries: [key: string, engage: number, peel: number, cc: number][]): TraitTable {
  return TraitTable.fromEntries(
    entries.map(([key, engage, peel, cc]) => [key, { engage, peel, cc, scaling: 'mid' as ScalingCurve, waveclear: 1 }]),
  );
}

function scalingTraits(...entries: [key: string, scaling: ScalingCurve][]): TraitTable {
  return TraitTable.fromEntries(entries.map(([key, scaling]) => [key, { engage: 0, peel: 0, cc: 1, scaling, waveclear: 1 }]));
}

function waveTraits(...entries: [key: string, waveclear: number][]): TraitTable {
  return TraitTable.fromEntries(entries.map(([key, waveclear]) => [key, { engage: 0, peel: 0, cc: 1, scaling: 'mid' as ScalingCurve, waveclear }]));
}

const analyzer = (t: TraitTable | null = null): CompAnalyzer => new CompAnalyzer(meta(), t ?? TraitTable.empty);

/**
 * Matches a chip by a distinctive fragment rather than its full wording, and insists it carries
 * an explanation: a chip whose text is jargon and whose tooltip is empty is unusable mid-draft.
 */
function assertChip(reasons: readonly Reason[], fragment: string, tone: ReasonTone): void {
  const match = reasons.find((reason) => reason.text.includes(fragment) && reason.tone === tone);
  expect(match).toBeDefined();
  expect(match!.hint?.trim() ?? '', `Chip "${match!.text}" braucht eine Erklärung.`).not.toBe('');
}

describe('CompAnalyzerTests', () => {
  it('EmptyComp_HasNoFindings', () => {
    expect(analyzer().analyze([]).findings).toHaveLength(0);
  });

  it('DamageRules_StaySilentBelowThreeChampions', () => {
    // Two physical champions is not yet a one-dimensional composition; it is an early draft.
    const profile = analyzer().analyze([Ad1, Ad2]);

    expect(hasIssue(profile, 'noMagicDamage')).toBe(false);
  });

  it('AllPhysical_FlagsMissingMagicDamage', () => {
    const profile = analyzer().analyze([Ad1, Ad2, Ad3]);

    expect(hasIssue(profile, 'noMagicDamage')).toBe(true);
    expect(hasIssue(profile, 'noPhysicalDamage')).toBe(false);
    expect(profile.physicalShare).toBeCloseTo(1.0, 6);
  });

  it('AllMagic_FlagsMissingPhysicalDamage', () => {
    const profile = analyzer().analyze([Ap1, Ap2, Ap3]);

    expect(hasIssue(profile, 'noPhysicalDamage')).toBe(true);
    expect(hasIssue(profile, 'noMagicDamage')).toBe(false);
  });

  it('MixedDamage_CountsHalfToEachSide', () => {
    const profile = analyzer().analyze([Ad1, Ad2, MixedOne]);

    expect(profile.physicalShare).toBeCloseTo(2.5 / 3, 6);
    expect(profile.magicShare).toBeCloseTo(0.5 / 3, 6);
  });

  it('ChampionsWithoutDamageData_DoNotCountTowardsTheRule', () => {
    // Three known physical plus one unknown must still read as physical-only, not as balanced.
    const profile = analyzer().analyze([Ad1, Ad2, Ad3, Unknown]);

    expect(hasIssue(profile, 'noMagicDamage')).toBe(true);
    expect(profile.physicalShare).toBeCloseTo(1.0, 6);
  });

  it('NoFrontline_IsFlagged', () => {
    expect(hasIssue(analyzer().analyze([Ad3, Ap1, Ap2]), 'noFrontline')).toBe(true);
  });

  it.each([TankTagged, TankByDefense])('Frontline_IsRecognisedByTagOrByDefensiveRating(%i)', (frontlineId) => {
    const profile = analyzer().analyze([Ad3, Ap1, frontlineId]);

    expect(hasIssue(profile, 'noFrontline')).toBe(false);
    expect(profile.frontlineCount).toBe(1);
  });

  it('AllMelee_IsFlaggedFromFourChampions', () => {
    const profile = analyzer().analyze([Ad1, Ad2, Ad3, TankByDefense]);

    expect(hasIssue(profile, 'allMelee')).toBe(true);
    expect(profile.rangedCount).toBe(0);
  });

  it('OneRangedChampion_ClearsTheMeleeFlag', () => {
    expect(hasIssue(analyzer().analyze([Ad1, Ad2, Ad3, RangedOne]), 'allMelee')).toBe(false);
  });

  it('ChampionsWithoutStaticData_AreNotCountedAsMeleeOrFrontline', () => {
    // The unknown champion must not be quietly assumed melee, which would fire AllMelee wrongly.
    const profile = analyzer().analyze([RangedOne, Unknown, Unknown]);

    expect(profile.rangedCount).toBe(1);
  });

  it('TraitRules_StaySilentWithoutCuratedData', () => {
    // No traits loaded at all: engage, peel and CC cannot be judged, so nothing is claimed.
    const profile = analyzer().analyze([Ad1, Ad2, Ad3, Ad4]);

    expect(hasIssue(profile, 'noEngage')).toBe(false);
    expect(hasIssue(profile, 'noPeel')).toBe(false);
    expect(hasIssue(profile, 'littleCrowdControl')).toBe(false);
    expect(traitCoverage(profile)).toBe(0);
  });

  it('NoEngage_IsFlaggedWhenTraitsAreKnown', () => {
    const t = traits(['Ad1', 0, 0, 1], ['Ad2', 0, 1, 1], ['Ad3', 0, 0, 1]);

    const profile = analyzer(t).analyze([Ad1, Ad2, Ad3]);

    expect(hasIssue(profile, 'noEngage')).toBe(true);
    expect(traitCoverage(profile)).toBeCloseTo(1.0, 6);
  });

  it('HardEngage_ClearsTheEngageFlag', () => {
    const t = traits(['Ad1', 2, 0, 3], ['Ad2', 0, 1, 1], ['Ad3', 0, 0, 1]);

    expect(hasIssue(analyzer(t).analyze([Ad1, Ad2, Ad3]), 'noEngage')).toBe(false);
  });

  it('LittleCrowdControl_IsFlagged', () => {
    const t = traits(['Ad1', 2, 0, 0], ['Ad2', 1, 1, 0], ['Ad3', 1, 0, 1]);

    const profile = analyzer(t).analyze([Ad1, Ad2, Ad3]);

    expect(hasIssue(profile, 'littleCrowdControl')).toBe(true);
    expect(profile.totalCrowdControl).toBe(1);
  });

  it('PartialTraitCoverage_IsReported', () => {
    const t = traits(['Ad1', 2, 2, 3]);

    const profile = analyzer(t).analyze([Ad1, Ad2, Ad3]);

    expect(traitCoverage(profile)).toBeCloseTo(1.0 / 3, 6);
    // One curated champion out of three is not enough to claim anything about engage.
    expect(hasIssue(profile, 'noEngage')).toBe(false);
  });

  it('Fit_RewardsCoveringTheMissingDamageType', () => {
    const a = analyzer();
    const profile = a.analyze([Ad1, Ad2, Ad3]);
    const reasons: Reason[] = [];

    const score = a.fit(Ap1, profile, EMPTY_PROFILE, reasons);

    expect(score).not.toBeNull();
    expect(score!).toBeGreaterThan(0);
    assertChip(reasons, 'AP-Schaden', 'pro');
  });

  it('Fit_RewardsBringingFrontline', () => {
    const a = analyzer();
    const profile = a.analyze([Ad3, Ap1, Ap2]);
    const reasons: Reason[] = [];

    a.fit(TankTagged, profile, EMPTY_PROFILE, reasons);

    assertChip(reasons, 'steht vorne', 'pro');
  });

  it('Fit_RewardsBringingEngage', () => {
    const t = traits(['Ad1', 0, 0, 1], ['Ad2', 0, 1, 1], ['Ad3', 0, 0, 1], ['TankTag', 2, 2, 3]);
    const a = analyzer(t);
    const profile = a.analyze([Ad1, Ad2, Ad3]);
    const reasons: Reason[] = [];

    a.fit(TankTagged, profile, EMPTY_PROFILE, reasons);

    assertChip(reasons, 'kann engagen', 'pro');
  });

  it('Fit_PenalisesDeepeningAnImbalance', () => {
    const a = analyzer();
    // Three physical plus one magic: no rule fires, but the comp is still physical-heavy.
    const profile = a.analyze([Ad1, Ad2, Ad3, Ap1]);
    const reasons: Reason[] = [];

    const score = a.fit(Ad4, profile, EMPTY_PROFILE, reasons);

    expect(score).not.toBeNull();
    expect(score!).toBeLessThan(0);
    assertChip(reasons, 'mehr AD-Schaden', 'contra');
  });

  // No verdict at all rather than a zero: the breakdown shows the two differently, and claiming
  // "neutral" about a champion nothing is known of would be a lie.
  it('Fit_SaysNothingForChampionsWithNoData', () => {
    const a = analyzer();
    const profile = a.analyze([Ad1, Ad2, Ad3]);
    const reasons: Reason[] = [];

    expect(a.fit(Unknown, profile, EMPTY_PROFILE, reasons)).toBeNull();
    expect(reasons).toHaveLength(0);
  });

  it('Fit_SaysNothingAgainstAnEmptyComposition', () => {
    const a = analyzer();

    expect(a.fit(Ap1, EMPTY_PROFILE, EMPTY_PROFILE, [])).toBeNull();
  });

  it('Waveclear_IsMissingOnlyFromFourChampionsOn', () => {
    const a = analyzer(waveTraits(['Ad1', 1], ['Ad2', 1], ['Ad3', 0], ['Ad4', 1], ['Ap1', 2]));

    // Three: the mid laner who usually brings it may still come.
    expect(hasIssue(a.analyze([Ad1, Ad2, Ad3]), 'littleWaveclear')).toBe(false);
    const four = a.analyze([Ad1, Ad2, Ad3, Ad4]);
    expect(four.strongWaveclearCount).toBe(0);
    expect(hasIssue(four, 'littleWaveclear')).toBe(true);
    expect(hasIssue(a.analyze([Ad1, Ad2, Ad3, Ap1]), 'littleWaveclear')).toBe(false);
  });

  it('Fit_RewardsGoodWaveclearWhereTheTeamLacksIt', () => {
    const team = [Ad1, Ad2, Ad3, Ad4] as const;
    const strong = analyzer(waveTraits(['Ad1', 1], ['Ad2', 1], ['Ad3', 0], ['Ad4', 1], ['Ap1', 2]));
    const weak = analyzer(waveTraits(['Ad1', 1], ['Ad2', 1], ['Ad3', 0], ['Ad4', 1], ['Ap1', 1]));
    const reasons: Reason[] = [];

    const withWaveclear = strong.fit(Ap1, strong.analyze(team), EMPTY_PROFILE, reasons)!;
    const without = weak.fit(Ap1, weak.analyze(team), EMPTY_PROFILE, [])!;

    assertChip(reasons, 'guter Waveclear', 'pro');
    expect(withWaveclear - without).toBeCloseTo(0.2, 10);
  });

  it('DuplicateChampions_AreCountedOnce', () => {
    const profile = analyzer().analyze([Ad1, Ad1, Ad1, Ad2]);

    expect(profile.count).toBe(2);
  });

  it('ZeroIds_AreIgnored', () => {
    const profile = analyzer().analyze([0, Ad1, 0, Ad2, 0]);

    expect(profile.count).toBe(2);
  });

  // "No data → silence": without Data Dragon statics (an update run while the CDN was down)
  // frontline and range are unknowable, and flagging every composition as "kein Frontline"
  // would be a permanent false alarm.
  it('MissingStaticData_KeepsFrontlineAndMeleeRulesSilent', () => {
    const bare = new MetaBuilder()
      .champion(1, 'A', { damage: DamageType.Physical })
      .champion(2, 'B', { damage: DamageType.Physical })
      .champion(3, 'C', { damage: DamageType.Magic })
      .champion(4, 'D', { damage: DamageType.Magic })
      .build();

    const profile = new CompAnalyzer(bare, TraitTable.empty).analyze([1, 2, 3, 4]);

    expect(hasIssue(profile, 'noFrontline')).toBe(false);
    expect(hasIssue(profile, 'allMelee')).toBe(false);
  });

  it('CcFloor_IsOnePiecePerTwoChampions', () => {
    // Two CC points across three champions meets the documented floor of one per two.
    const t = traits(['Ad1', 0, 0, 1], ['Ad2', 0, 0, 1], ['Ap1', 0, 0, 0]);

    const profile = analyzer(t).analyze([Ad1, Ad2, Ap1]);

    expect(hasIssue(profile, 'littleCrowdControl')).toBe(false);
  });

  it('CcClearlyBelowTheFloor_IsFlagged', () => {
    const t = traits(['Ad1', 0, 0, 1], ['Ad2', 0, 0, 0], ['Ap1', 0, 0, 0]);

    const profile = analyzer(t).analyze([Ad1, Ad2, Ap1]);

    expect(hasIssue(profile, 'littleCrowdControl')).toBe(true);
  });

  // ScoreModel.CompScale is calibrated for -1..+1 ("a covered gap ≈ +4 points, never more").
  // A candidate covering every gap at once must saturate there, not stack to twice that.
  it('Fit_IsClampedToPlusMinusOne', () => {
    // Three AD melee champions with no engage, peel or CC: every finding at once.
    const t = traits(['Ad1', 0, 0, 0], ['Ad2', 0, 0, 0], ['Ad3', 0, 0, 0], ['TankTag', 3, 3, 3]);
    const a = analyzer(t);
    const profile = a.analyze([Ad1, Ad2, Ad3]);

    // A ranged magic tank with hard engage, peel and CC covers all of them.
    const fit = a.fit(TankTagged, profile, EMPTY_PROFILE, []);

    expect(fit).not.toBeNull();
    expect(fit!).toBeGreaterThanOrEqual(-1);
    expect(fit!).toBeLessThanOrEqual(1);
  });

  // The damage mix is shown as two shares. "0 % / 0 %" would read as a team that deals no
  // damage, where the truth is that no champion's damage type is known yet.
  it('WithoutASingleKnownDamageType_ThereIsNoMixToShow', () => {
    expect(hasDamageMix(EMPTY_PROFILE)).toBe(false);
    expect(hasDamageMix(analyzer().analyze([Unknown]))).toBe(false);
    expect(hasDamageMix(analyzer().analyze([Unknown, Ad1]))).toBe(true);
  });

  // --- What the pick does about the ENEMY line-up. ----------------------------------------
  // These rules are the mirror of the block above. Each one is pinned in both directions: the
  // situation it is for, and a situation where it must stay silent — a rule that fires on every
  // draft is a constant, not a reason.

  it('Fit_RewardsPeelAgainstAnEnemyThatCanEngage', () => {
    const t = traits(['TankTag', 2, 2, 3], ['Ad4', 0, 2, 0], ['Ad1', 0, 0, 1], ['Ad2', 0, 0, 1]);
    const a = analyzer(t);
    const enemies = a.analyze([TankTagged, Ad1, Ad2]);
    const reasons: Reason[] = [];

    const score = a.fit(Ad4, EMPTY_PROFILE, enemies, reasons);

    expect(score).not.toBeNull();
    expect(score!).toBeGreaterThan(0);
    assertChip(reasons, 'Engage auf', 'pro');
  });

  it('Fit_StaysSilentOnPeelWhenTheEnemyCannotEngage', () => {
    const t = traits(['Ad1', 0, 0, 1], ['Ad2', 0, 1, 1], ['Ad3', 1, 0, 1], ['Ad4', 0, 2, 0]);
    const a = analyzer(t);
    const enemies = a.analyze([Ad1, Ad2, Ad3]);
    const reasons: Reason[] = [];

    a.fit(Ad4, EMPTY_PROFILE, enemies, reasons);

    expect(reasons.some((reason) => reason.text.includes('Engage'))).toBe(false);
  });

  it('Fit_RewardsAnAssassinAgainstAnOpenBackline', () => {
    const a = analyzer();
    // Two mages and a marksman: nobody with a tank tag or six defence.
    const enemies = a.analyze([Ap1, Ap2, Ad4]);
    const reasons: Reason[] = [];

    const score = a.fit(Ad3, EMPTY_PROFILE, enemies, reasons);

    expect(score).not.toBeNull();
    expect(score!).toBeGreaterThan(0);
    assertChip(reasons, 'Backline steht frei', 'pro');
  });

  // The counterweight to the rule above. Without it the term would only ever reward the same
  // champions, and "the enemy composition is read" would mean "assassins get a bonus".
  it('Fit_PenalisesAnAssassinAgainstTwoFrontliners', () => {
    const a = analyzer();
    const enemies = a.analyze([TankTagged, TankByDefense, Ap1]);
    const reasons: Reason[] = [];

    const score = a.fit(Ad3, EMPTY_PROFILE, enemies, reasons);

    expect(score).not.toBeNull();
    expect(score!).toBeLessThan(0);
    assertChip(reasons, 'an ihre Carrys', 'contra');
  });

  it('Fit_RewardsAnEarlyChampionAgainstALateEnemy', () => {
    const t = scalingTraits(['Ad1', 'late'], ['Ad2', 'late'], ['Ad3', 'late'], ['Ap1', 'early']);

    const a = analyzer(t);
    const enemies = a.analyze([Ad1, Ad2, Ad3]);
    const reasons: Reason[] = [];

    const score = a.fit(Ap1, EMPTY_PROFILE, enemies, reasons);

    expect(score).not.toBeNull();
    expect(score!).toBeGreaterThan(0);
    assertChip(reasons, 'Earlygame', 'pro');
  });

  // First pick of the own team, two enemies already revealed: the old signature had nothing to
  // say there, because it only ever looked at team-mates. That is exactly the moment with the
  // least other evidence — no duel, no duo, no lane opponent.
  it('Fit_JudgesTheEnemyBeforeTheOwnTeamHasPicked', () => {
    const a = analyzer();
    const enemies = a.analyze([Ap1, Ap2, Ad4]);

    expect(a.fit(Ad3, EMPTY_PROFILE, enemies, [])).not.toBeNull();
  });

  // The enemy rules share the ally rules' cap on purpose: the term answers one question, and
  // looking at both halves of the board may make it better informed, not louder.
  it('Fit_StaysClampedWhenBothSidesArgueForThePick', () => {
    const t = traits(['Ad1', 0, 0, 0], ['Ad2', 0, 0, 0], ['Ad3', 0, 0, 0], ['TankTag', 3, 3, 3]);
    const a = analyzer(t);
    const allies = a.analyze([Ad1, Ad2, Ad3]);
    const enemies = a.analyze([Ad1, Ad2, Ad3]);

    const fit = a.fit(TankTagged, allies, enemies, []);

    expect(fit).not.toBeNull();
    expect(fit!).toBeGreaterThanOrEqual(-1);
    expect(fit!).toBeLessThanOrEqual(1);
  });
});
