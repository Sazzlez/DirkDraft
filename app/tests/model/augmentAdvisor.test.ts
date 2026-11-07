// Port of tests/DraftPilot.Core.Tests/AugmentAdvisorTests.cs. The augment figure arrives with no
// sample size and no documented scale; these tests pin down that rows with no recorded picks are
// noise, and the restraint: nothing here is converted into a win rate.

import { describe, expect, it } from 'vitest';
import { isCoarseAugment, rankAugments, type AugmentOption } from '../../src/core/model/advisors';

/**
 * Real rows for Seraphine, captured on 2026-09-19. The last two are the trap: 170 and 141.67 on
 * augments with no recorded picks — 170 times an exact small fraction (1/1 and 5/6).
 */
const seraphine = (): AugmentOption[] => [
  { id: 1004, name: 'Zurück zu den Wurzeln', tier: 3, performance: 91.32, pickRate: 0.2 },
  { id: 1335, name: 'Goldschürfer', tier: 3, performance: 89.08, pickRate: 0.19 },
  { id: 1325, name: 'Glaskanone', tier: 4, performance: 81.08, pickRate: 0.18 },
  { id: 1061, name: 'O.K.-Bumerang', tier: 3, performance: 92.18, pickRate: 0.17 },
  { id: 2016, name: 'Aufgetankt', tier: 4, performance: 86.33, pickRate: 0.16 },
  { id: 1116, name: 'Blitzartig', tier: 4, performance: 89.04, pickRate: 0.15 },
  { id: 1006, name: 'Klingenwalzer', tier: 5, performance: 170.0, pickRate: 0.0 },
  { id: 1347, name: 'Poltergeist', tier: 5, performance: 141.67, pickRate: 0.0 },
];

const ranked = (take = 10): AugmentOption[] => rankAugments(seraphine(), take);

describe('AugmentAdvisorTests', () => {
  // A name at the top of a list is read as advice whatever caveat sits beside it.
  it('APerfectScoreWithNoRecordedPicksIsNotShown', () => {
    const rows = ranked();

    expect(rows.some((row) => row.name === 'Klingenwalzer')).toBe(false);
    expect(rows.some((row) => row.name === 'Poltergeist')).toBe(false);
    for (const row of rows) expect(row.pickRate).toBeGreaterThan(0);
  });

  // O.K.-Bumerang scores higher but sits below the median popularity of 0.18, so it does not lead.
  it('TheHighestScoringPopularRowLeads', () => {
    const rows = ranked();

    expect(rows[0]!.name).toBe('Zurück zu den Wurzeln');
    expect(rows[0]!.performance).toBeCloseTo(91.32, 2);
    for (const row of rows) expect(row.pickRate).toBeGreaterThanOrEqual(0.18);
  });

  it('TheOrderIsDescendingByScore', () => {
    const scores = ranked().map((row) => row.performance);

    expect(scores).toEqual([...scores].sort((a, b) => b - a));
  });

  // A figure that is exactly 170 times a small fraction gives its own sample away.
  it.each([
    [170.0], // 1/1
    [141.67], // 5/6
    [127.5], // 3/4
    [113.33], // 2/3
    [85.0], // 1/2
    [56.67], // 1/3
    [48.57], // 2/7
    [42.5], // 1/4
  ])('ACoarseFigureIsRecognised(%d)', (performance) => {
    expect(isCoarseAugment(performance)).toBe(true);
  });

  // The figures from rows with real usage: no ratio over forty or fewer observations lands on them.
  it.each([[91.32], [89.08], [92.18], [81.08]])('AFineGrainedFigureIsNot(%d)', (performance) => {
    expect(isCoarseAugment(performance)).toBe(false);
  });

  // Measured on Darius: the champion's own median popularity removes the rarely taken rows, so no
  // constant has to be invented for it.
  it('RarelyTakenAugmentsDoNotLeadTheList', () => {
    const rows = rankAugments(
      [
        // High score, almost never taken. Not a coarse fraction, so only the popularity filter catches it.
        { id: 1, name: 'Randerscheinung', tier: 5, performance: 101.17, pickRate: 0.01 },
        { id: 2, name: 'Auch selten', tier: 5, performance: 99.31, pickRate: 0.02 },
        { id: 3, name: 'Üblich', tier: 3, performance: 84.38, pickRate: 0.22 },
        { id: 4, name: 'Sehr üblich', tier: 3, performance: 83.15, pickRate: 0.23 },
      ],
      10,
    );

    expect(rows[0]!.name).toBe('Üblich');
    expect(rows.some((row) => row.name === 'Randerscheinung')).toBe(false);
    for (const row of rows) expect(row.pickRate).toBeGreaterThanOrEqual(0.22);
  });

  // Fails first if anybody re-adds a conversion: both figures reach the caller unchanged.
  it('BothFiguresArePassedThroughUntouched', () => {
    const matches = ranked().filter((entry) => entry.id === 1004);
    expect(matches).toHaveLength(1);
    const row = matches[0]!;

    expect(row.performance).toBeCloseTo(91.32, 4);
    expect(row.pickRate).toBeCloseTo(0.2, 4);
    expect(row.tier).toBe(3);
  });

  // Popularity breaks a tie in the score; 91.32 because a round 90.00 is itself coarse (170 · 9/17).
  it('EqualScoresAreSeparatedByPopularity', () => {
    const rows = rankAugments(
      [
        { id: 1, name: 'Etwas seltener', tier: 5, performance: 91.32, pickRate: 0.2 },
        { id: 2, name: 'Häufig', tier: 3, performance: 91.32, pickRate: 0.21 },
      ],
      10,
    );

    expect(rows[0]!.name).toBe('Häufig');
  });

  // A round number is no safer than any other: 90.00 is 170 times 9/17.
  it('ARoundFigureCanStillBeCoarse', () => {
    expect(isCoarseAugment(90.0)).toBe(true);
  });

  it('TakeIsRespected', () => {
    expect(ranked(3)).toHaveLength(3);
  });

  it('TakingNothingReturnsNothing', () => {
    expect(ranked(0)).toHaveLength(0);
  });

  it('NothingInNothingOut', () => {
    expect(rankAugments([], 10)).toHaveLength(0);
  });

  // A corrupt or hostile answer produces no row rather than an exception or an unreadable number.
  it('UnreadableRowsAreDropped', () => {
    const rows = rankAugments(
      [
        { id: 1, name: 'NaN-Wert', tier: 3, performance: Number.NaN, pickRate: 0.2 },
        { id: 2, name: 'Unendlich', tier: 3, performance: Number.POSITIVE_INFINITY, pickRate: 0.2 },
        { id: 3, name: 'Negative Beliebtheit', tier: 3, performance: 90, pickRate: -1 },
        { id: 4, name: 'NaN-Beliebtheit', tier: 3, performance: 90, pickRate: Number.NaN },
        { id: 5, name: 'Wert null', tier: 3, performance: 0, pickRate: 0.2 },
        { id: 6, name: '', tier: 3, performance: 90, pickRate: 0.2 },
        { id: 7, name: '   ', tier: 3, performance: 90, pickRate: 0.2 },
      ],
      10,
    );

    expect(rows).toHaveLength(0);
  });

  // One good row among broken ones still comes through: the filter drops rows, not the answer.
  it('ABrokenNeighbourDoesNotSuppressAGoodRow', () => {
    const rows = rankAugments(
      [
        { id: 1, name: 'Kaputt', tier: 3, performance: Number.NaN, pickRate: 0.2 },
        { id: 2, name: 'Heil', tier: 3, performance: 88.0, pickRate: 0.2 },
      ],
      10,
    );

    expect(rows).toHaveLength(1);
    expect(rows[0]!.name).toBe('Heil');
  });
});
