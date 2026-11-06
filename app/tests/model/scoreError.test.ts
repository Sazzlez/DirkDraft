// Port of tests/DraftPilot.Core.Tests/ScoreErrorTests.cs: the error bar behind every score. Its job
// is to stop the panel from claiming an order it cannot support.

import { describe, expect, it } from 'vitest';
import type { Recommendation } from '../../src/core/model/recommender';
import {
  countLeadingTies,
  decimalsFor,
  decimalsOf,
  ErrorBudget,
  errorAsPoints,
  logitVariance,
  standing,
} from '../../src/core/model/scoreError';
import { SYNERGY_DAMPING } from '../../src/core/model/scoreModel';
import { LANE_PRIOR, shrink, SYNERGY_PRIOR } from '../../src/core/model/shrinkage';

const item = (name: string, score: number, uncertainty: number): Recommendation => ({
  championId: 1,
  name,
  score,
  reasons: [],
  breakdown: [],
  uncertainty,
});

describe('ScoreErrorTests', () => {
  it('ARateWithoutGames_CarriesNoError', () => {
    // Shrinkage pins it to 0.5, so the term says nothing and must not pretend to a spread.
    expect(logitVariance(0.5, 0, LANE_PRIOR)).toBe(0);
  });

  // Same prior, same rate, different sample: the thinner one moves more under resampling.
  it('ThinnerSamples_CarryMoreError', () => {
    const thin = logitVariance(0.547, 111, LANE_PRIOR);
    const thick = logitVariance(0.518, 43_504, LANE_PRIOR);

    expect(thin, `111 Spiele müssen deutlich unsicherer sein als 43.504 (${thin} vs ${thick}).`).toBeGreaterThan(thick * 10);
  });

  // A 43.504-game lane rate and a 111-game duo at 59 % raw: at the measured prior the duo no longer
  // carries the error bar on its own — it stays under half a point.
  it('AThinDuoNoLongerDominatesTheError', () => {
    const budget = new ErrorBudget();
    budget.add(logitVariance(0.5178, 43_504, LANE_PRIOR));

    const laneOnly = errorAsPoints(budget.standardError);

    // The synergy term is a damped mean over exactly one partner.
    const scale = SYNERGY_DAMPING;
    const duo = shrink(0.59, 111, SYNERGY_PRIOR);
    budget.add(scale * scale * logitVariance(duo, 111, SYNERGY_PRIOR));

    const withSynergy = errorAsPoints(budget.standardError);

    expect(laneOnly).toBeGreaterThanOrEqual(0.2);
    expect(laneOnly).toBeLessThanOrEqual(0.3);
    expect(withSynergy).toBeGreaterThanOrEqual(0.3);
    expect(withSynergy).toBeLessThanOrEqual(0.6);
  });

  it.each([
    [0.06, 0], // ±1,5 Punkte: eine Nachkommastelle wäre erfunden
    [0.02, 0], // ±0,5 Punkte: noch zu grob
    [0.01, 1], // ±0,25 Punkte: eine Stelle ist gedeckt
  ])('TheScoreIsPrintedOnlyAsPreciselyAsItsErrorAllows(%d, %i)', (error, expected) => {
    expect(decimalsFor(error)).toBe(expected);
  });

  // One precision for the whole column; per row put "57 %" beside "54,3 %".
  it('TheWholeListSharesOnePrecision', () => {
    const mixed = [item('Rell', 0.572, 0.06), item('Braum', 0.543, 0.008), item('Thresh', 0.543, 0.023)];

    // Braum alone would earn a decimal (±0,2 Punkte); the median of the three does not.
    expect(decimalsFor(0.008)).toBe(1);
    expect(decimalsOf(mixed)).toBe(0);
  });

  it('AnEmptyList_KeepsTheFinerPrecision', () => {
    expect(decimalsOf([])).toBe(1);
  });

  it('AGapSmallerThanTheNoise_IsNotAnOrder', () => {
    // Rell 57,2 % ±1,5 against Leona 56,9 % ±0,9 — the gap is a fifth of the spread.
    expect(standing(0.572, 0.06, 0.569, 0.038)).toBe('tied');
  });

  it('AGapWellBeyondTheNoise_IsClear', () => {
    expect(standing(0.6, 0.005, 0.52, 0.005)).toBe('clear');
  });

  it('WithoutAnyErrorEstimate_NothingStrongerThanAheadIsClaimed', () => {
    expect(standing(0.6, 0, 0.52, 0)).toBe('ahead');
    expect(standing(0.52, 0, 0.6, 0)).toBe('tied');
  });

  it('TheTiedLeaders_AreCountedByTheirOwnError', () => {
    // The measured support case: four candidates share first place, the fifth does not.
    const items = [
      item('Rell', 0.572, 0.06),
      item('Leona', 0.569, 0.038),
      item('Alistar', 0.56, 0.06),
      item('Blitzcrank', 0.56, 0.027),
      item('Thresh', 0.543, 0.023),
    ];

    expect(countLeadingTies(items)).toBe(4);
  });

  // Zero, not one: a leader nobody ties with is not a group.
  it('OnThickDataTheSameGapIsNoTie', () => {
    // Identical scores to the case above, but measured over enough games to separate them.
    const items = [item('A', 0.572, 0.004), item('B', 0.569, 0.004)];

    expect(countLeadingTies(items)).toBe(0);
  });

  // A count that is never 1: either there is a group of at least two, or there is none.
  it('TheCountIsNeverExactlyOne', () => {
    const separated = [item('A', 0.6, 0.004), item('B', 0.5, 0.004), item('C', 0.4, 0.004)];
    const tied = [item('A', 0.572, 0.05), item('B', 0.569, 0.05), item('C', 0.4, 0.004)];

    expect(countLeadingTies(separated)).toBe(0);
    expect(countLeadingTies(tied)).toBe(2);
  });

  it('ASingleEntryHasNothingToTieWith', () => {
    expect(countLeadingTies([item('A', 0.57, 0.01)])).toBe(0);
  });
});
